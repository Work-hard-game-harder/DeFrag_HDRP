using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace DeFrag.B1F
{
    public enum B1FStoryDebugStart : byte
    {
        None,
        AfterDistributionBoxA,
        AfterConnectServer,
        AfterGeneratorB
    }

    [DisallowMultipleComponent]
    public sealed class B1FStoryDebugCheckpoint : MonoBehaviour
    {
        [Header("Editor / Development Build Only")]
        [Tooltip("선택한 지점 직후 상태로 B1F를 시작합니다.\n" +
                 "After Distribution Box A: 배전함 A 앞, Connect Server 퀘스트 활성화.\n" +
                 "After Connect Server: 관제실 안, 탈출 시퀀스(경고 → 다운로드) 시작.\n" +
                 "After Generator B: 발전기 B 앞, 발전기 가동 직후(전력 복구 → 다운로드 재개).")]
        [SerializeField] private B1FStoryDebugStart startCheckpoint;
        [Tooltip("스폰된 TV 몬스터의 AI와 이동을 서버에서 정지시킵니다.")]
        [SerializeField] private bool freezeTvMonsterInPlace;

        [Header("Debug Loadout")]
        [Tooltip("체크포인트 시작 시 해킹패드와 카메라를 호스트/클라이언트에게 랜덤으로 나눠 인벤토리 1번 칸에 넣습니다.")]
        [SerializeField] private bool giveRoleItems = true;
        [SerializeField] private NetworkWorldItem hackingPadItem;
        [SerializeField] private NetworkWorldItem cameraItem;

        [Header("Local Debug Bypass")]
        [Tooltip("체크하면 지정된 Quest Barrier의 Collider를 호스트와 각 클라이언트에서 비활성화합니다.")]
        [SerializeField] private bool disableQuestBarriers;
        [SerializeField] private QuestBarrier[] questBarriersToDisable;

        [Header("Scene References")]
        [SerializeField] private DistributionBoxController distributionBoxA;
        [SerializeField] private B1FPowerController powerController;
        [SerializeField] private ConnectServerCoordinator connectServerCoordinator;
        [SerializeField] private B1FEscapeSequence escapeSequence;
        [SerializeField] private GeneratorBController generatorB;
        [Tooltip("비어 있으면 씬의 GameplaySpawnPointRegistry를 사용합니다. 리스폰 위치는 레지스트리의 체크포인트 스폰 포인트입니다.")]
        [SerializeField] private GameplaySpawnPointRegistry spawnPointRegistry;

        [Header("Checkpoint Quest IDs")]
        [SerializeField] private string distributionBoxAQuestId = "b1f_emergency_power";
        [SerializeField] private string connectServerQuestId = "b1f_connect_server";
        [Tooltip("After Generator B는 이 퀘스트까지 완료 처리한 뒤, 발전기 완료 신호로 다음 단계를 진행합니다.")]
        [SerializeField] private string downloadInterruptedQuestId = "b1f_download_initial";

        [Header("Connect Server Terminal")]
        [SerializeField] private string connectServerTerminalId = "terminal_31";

        [Header("Timeouts")]
        [SerializeField, Min(1f)] private float networkReadyTimeout = 20f;
        [Tooltip("배전함 연출, 전력 전환, 몬스터 등장 컷씬처럼 시간이 걸리는 단계를 기다리는 최대 시간입니다.")]
        [SerializeField, Min(1f)] private float storyStateTimeout = 90f;

        // Result of the most recent server step coroutine (coroutines cannot return values).
        private bool stepSucceeded;

        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ApplyLocalQuestBarrierOverride();
            if (startCheckpoint == B1FStoryDebugStart.None && !freezeTvMonsterInPlace)
                return;

            StartCoroutine(RunServerCheckpoint());
            if (startCheckpoint != B1FStoryDebugStart.None)
                StartCoroutine(MoveLocalPlayerWhenCheckpointReached());
#endif
        }

        // ───────────────────────── Server: shared story state ─────────────────────────

        private IEnumerator RunServerCheckpoint()
        {
            float deadline = Time.realtimeSinceStartup + networkReadyTimeout;
            while (!IsNetworkReady() && Time.realtimeSinceStartup < deadline)
                yield return null;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
            {
                Debug.LogError(
                    "[B1F Story Debug] Network session did not become ready before the timeout.",
                    this);
                yield break;
            }

            // Every peer owns this scene object. Shared story state, items and monster AI
            // are changed only by the server; each owner moves its own player afterwards.
            if (!manager.IsServer)
                yield break;

            if (powerController == null)
            {
                Debug.LogError("[B1F Story Debug] Power Controller is not assigned.", this);
                yield break;
            }

            yield return WaitServer(() => powerController.IsSpawned, deadline,
                "Power Controller was not spawned.");
            if (!stepSucceeded)
                yield break;

            powerController.SetTvMonsterStoryDebugFrozenServer(freezeTvMonsterInPlace);
            if (startCheckpoint == B1FStoryDebugStart.None)
            {
                Debug.Log("[B1F Story Debug] TV Monster position lock is armed.", this);
                yield break;
            }

            yield return WaitServer(
                () => QuestManager.Instance != null && QuestManager.Instance.IsInitialized,
                deadline, "QuestManager was not initialized.");
            if (!stepSucceeded)
                yield break;

            if (giveRoleItems)
                yield return GiveRoleItemsServer(manager, deadline);

            if (!ValidateDistributionBox())
                yield break;

            switch (startCheckpoint)
            {
                case B1FStoryDebugStart.AfterDistributionBoxA:
                    yield return CompleteDistributionBoxAServer(deadline);
                    if (!stepSucceeded || !TryCompleteQuestsThrough(distributionBoxAQuestId))
                        yield break;
                    Debug.Log(
                        "[B1F Story Debug] Applied AFTER DISTRIBUTION BOX A: " +
                        "Emergency Power sequence started and b1f_connect_server is active.",
                        this);
                    break;

                case B1FStoryDebugStart.AfterConnectServer:
                    yield return CompleteDistributionBoxAServer(deadline);
                    if (!stepSucceeded)
                        yield break;
                    yield return CompleteConnectServerServer(manager, deadline);
                    if (!stepSucceeded || !TryCompleteQuestsThrough(connectServerQuestId))
                        yield break;
                    Debug.Log(
                        "[B1F Story Debug] Applied AFTER CONNECT SERVER: " +
                        "Connect Server is complete and the escape sequence starts.",
                        this);
                    break;

                case B1FStoryDebugStart.AfterGeneratorB:
                    yield return ApplyAfterGeneratorBServer(manager, deadline);
                    break;
            }
        }

        private IEnumerator CompleteDistributionBoxAServer(float networkDeadline)
        {
            yield return WaitServer(() => distributionBoxA.IsSpawned, networkDeadline,
                "Distribution Box A NetworkObject was not spawned.");
            if (!stepSucceeded)
                yield break;

            if (!distributionBoxA.IsCompleted &&
                !distributionBoxA.TryCompleteForStoryDebugServer())
            {
                Debug.LogError(
                    "[B1F Story Debug] Distribution Box A could not enter the debug checkpoint.",
                    distributionBoxA);
                stepSucceeded = false;
                yield break;
            }

            yield return WaitServer(() => distributionBoxA.IsCompleted, StoryDeadline(),
                "Distribution Box A completion timed out.");
        }

        private IEnumerator CompleteConnectServerServer(NetworkManager manager, float networkDeadline)
        {
            stepSucceeded = false;
            if (connectServerCoordinator == null)
            {
                Debug.LogError("[B1F Story Debug] Connect Server Coordinator is not assigned.", this);
                yield break;
            }

            while (!connectServerCoordinator.IsSpawned && Time.realtimeSinceStartup < networkDeadline)
                yield return null;
            if (!connectServerCoordinator.TryCompleteForStoryDebugServer())
            {
                Debug.LogError("[B1F Story Debug] Connect Server could not be completed.", this);
                yield break;
            }

            CooperativeTerminalHintRelay relay = null;
            while (relay == null && Time.realtimeSinceStartup < networkDeadline)
            {
                relay = FindServerTerminalRelay(manager);
                if (relay == null)
                    yield return null;
            }

            if (relay == null || !relay.TryCompleteTerminalCommandForStoryDebugServer(
                    connectServerTerminalId,
                    TerminalCommands.ConnectServer))
            {
                Debug.LogWarning(
                    "[B1F Story Debug] Connect Server terminal completion could not be synchronized. " +
                    "The coordinator and quest checkpoint will still be applied.",
                    this);
            }

            stepSucceeded = true;
        }

        /// <summary>
        /// Rebuilds the state the real story has at the moment Generator B starts:
        /// door breached, story blackout, download paused. The generator's own completion
        /// path then restores full power, alerts the TV monster and advances the quest.
        /// </summary>
        private IEnumerator ApplyAfterGeneratorBServer(NetworkManager manager, float networkDeadline)
        {
            if (escapeSequence == null || generatorB == null)
            {
                Debug.LogError(
                    "[B1F Story Debug] Escape Sequence and Generator B must be assigned for AFTER GENERATOR B.",
                    this);
                yield break;
            }

            yield return WaitServer(() => escapeSequence.IsSpawned && generatorB.IsSpawned,
                networkDeadline, "Escape Sequence or Generator B was not spawned.");
            if (!stepSucceeded)
                yield break;

            // Enter the generator stage before Connect Server completes so the server never
            // starts the warning cutscene for this checkpoint.
            if (!escapeSequence.TrySkipToGeneratorRestorationForStoryDebugServer())
            {
                Debug.LogError("[B1F Story Debug] Escape Sequence could not skip to generator restoration.", this);
                yield break;
            }

            yield return CompleteDistributionBoxAServer(networkDeadline);
            if (!stepSucceeded)
                yield break;
            yield return CompleteConnectServerServer(manager, networkDeadline);
            if (!stepSucceeded)
                yield break;

            // The real blackout happens after Emergency Power and the TV monster entrance.
            // Interrupting the power transition earlier would prevent the monster spawn.
            yield return WaitServer(
                () => powerController.CurrentState == B1FPowerState.EmergencyPower &&
                      powerController.IsTvMonsterSpawned,
                StoryDeadline(), "Emergency Power or the TV Monster spawn timed out.");
            if (!stepSucceeded)
                yield break;

            powerController.SetStoryBlackoutServer();
            if (!TryCompleteQuestsThrough(downloadInterruptedQuestId))
                yield break;

            if (!generatorB.TryCompleteForStoryDebugServer())
            {
                Debug.LogError("[B1F Story Debug] Generator B could not be completed.", generatorB);
                yield break;
            }

            Debug.Log(
                "[B1F Story Debug] Applied AFTER GENERATOR B: generator is running, " +
                "full power is restoring and the download resumes.",
                this);
        }

        private IEnumerator GiveRoleItemsServer(NetworkManager manager, float networkDeadline)
        {
            if (hackingPadItem == null || cameraItem == null)
            {
                Debug.LogWarning(
                    "[B1F Story Debug] Give Role Items is checked, but the Hacking Pad or Camera item is not assigned.",
                    this);
                yield break;
            }

            List<NetworkPlayerInventory> inventories = new();
            while (Time.realtimeSinceStartup < networkDeadline)
            {
                if (hackingPadItem.IsSpawned && cameraItem.IsSpawned &&
                    TryCollectPlayerInventories(manager, inventories))
                    break;
                yield return null;
            }

            if (inventories.Count == 0)
            {
                Debug.LogError("[B1F Story Debug] Player inventories were not ready for the debug loadout.", this);
                yield break;
            }

            // Each player receives one role item in slot 1. A solo host receives both.
            bool padFirst = UnityEngine.Random.value < 0.5f;
            NetworkWorldItem first = padFirst ? hackingPadItem : cameraItem;
            NetworkWorldItem second = padFirst ? cameraItem : hackingPadItem;
            if (inventories.Count > 1 && UnityEngine.Random.value < 0.5f)
                (inventories[0], inventories[1]) = (inventories[1], inventories[0]);

            GiveItemServer(inventories[0], first);
            GiveItemServer(inventories[Mathf.Min(1, inventories.Count - 1)], second);
        }

        private void GiveItemServer(NetworkPlayerInventory inventory, NetworkWorldItem item)
        {
            if (inventory.TryGiveWorldItemForStoryDebugServer(item))
            {
                string itemName = item.Data != null ? item.Data.itemName : item.name;
                Debug.Log($"[B1F Story Debug] {itemName} -> client {inventory.OwnerClientId}", this);
                return;
            }

            Debug.LogWarning(
                $"[B1F Story Debug] Could not give {item.name} to client {inventory.OwnerClientId}. " +
                "It may already be held.",
                item);
        }

        private static bool TryCollectPlayerInventories(
            NetworkManager manager, List<NetworkPlayerInventory> inventories)
        {
            inventories.Clear();
            foreach (NetworkClient client in manager.ConnectedClientsList)
            {
                NetworkObject player = client.PlayerObject;
                if (player == null || !player.IsSpawned ||
                    !player.TryGetComponent(out NetworkPlayerInventory inventory))
                {
                    inventories.Clear();
                    return false;
                }
                inventories.Add(inventory);
            }
            return inventories.Count > 0;
        }

        private bool TryCompleteQuestsThrough(string questId)
        {
            if (QuestManager.Instance.TryCompleteThroughForStoryDebugServer(questId))
                return true;

            Debug.LogError($"[B1F Story Debug] Could not apply the quest checkpoint through '{questId}'.", this);
            return false;
        }

        // ───────────────────────── Every peer: move the owned player ─────────────────────────

        private IEnumerator MoveLocalPlayerWhenCheckpointReached()
        {
            float deadline = Time.realtimeSinceStartup + networkReadyTimeout;
            while (!IsNetworkReady() && Time.realtimeSinceStartup < deadline)
                yield return null;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
                yield break;

            // Player transforms are owner-authoritative, so each peer moves only its own player
            // once the replicated story state shows the checkpoint has been applied.
            deadline = StoryDeadline();
            NetworkObject localPlayer = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                localPlayer = manager.LocalClient?.PlayerObject;
                if (localPlayer != null && localPlayer.IsSpawned && IsCheckpointReplicated())
                    break;
                localPlayer = null;
                yield return null;
            }

            if (localPlayer == null)
            {
                Debug.LogWarning("[B1F Story Debug] Checkpoint was not reached in time; the player was not moved.", this);
                yield break;
            }

            GameplaySpawnPointRegistry registry =
                spawnPointRegistry != null ? spawnPointRegistry : GameplaySpawnPointRegistry.Instance;
            bool isHost = manager.LocalClientId == NetworkManager.ServerClientId;
            Transform point = registry != null
                ? registry.GetCheckpointSpawnPoint(ToSpawnCheckpoint(startCheckpoint), isHost)
                : null;
            if (point == null)
            {
                Debug.LogError(
                    $"[B1F Story Debug] No {(isHost ? "host" : "client")} spawn point is assigned for {startCheckpoint}.",
                    this);
                yield break;
            }

            MoveOwnedPlayer(localPlayer, point);
            Debug.Log($"[B1F Story Debug] Moved local player to {point.name}.", point);
        }

        private bool IsCheckpointReplicated() => startCheckpoint switch
        {
            B1FStoryDebugStart.AfterDistributionBoxA =>
                distributionBoxA != null && distributionBoxA.IsCompleted,
            B1FStoryDebugStart.AfterConnectServer =>
                connectServerCoordinator != null &&
                connectServerCoordinator.Phase == ConnectServerUplinkPhase.Completed,
            B1FStoryDebugStart.AfterGeneratorB => generatorB != null && generatorB.IsComplete,
            _ => false
        };

        private static B1FCheckpoint ToSpawnCheckpoint(B1FStoryDebugStart start) => start switch
        {
            B1FStoryDebugStart.AfterDistributionBoxA => B1FCheckpoint.DistributionBoxACompleted,
            B1FStoryDebugStart.AfterConnectServer => B1FCheckpoint.ConnectServerBreachCompleted,
            B1FStoryDebugStart.AfterGeneratorB => B1FCheckpoint.GeneratorCompleted,
            _ => B1FCheckpoint.Initial
        };

        private static void MoveOwnedPlayer(NetworkObject player, Transform point)
        {
            CharacterController characterController = player.GetComponent<CharacterController>();
            bool controllerWasEnabled = characterController != null && characterController.enabled;
            if (controllerWasEnabled)
                characterController.enabled = false;

            player.transform.SetPositionAndRotation(point.position, point.rotation);
            NetworkTransform networkTransform = player.GetComponent<NetworkTransform>();
            if (networkTransform != null && networkTransform.CanCommitToTransform)
                networkTransform.Teleport(point.position, point.rotation, player.transform.localScale);

            if (controllerWasEnabled)
                characterController.enabled = true;
        }

        // ───────────────────────── Helpers ─────────────────────────

        private void ApplyLocalQuestBarrierOverride()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!disableQuestBarriers)
                return;
            if (questBarriersToDisable == null || questBarriersToDisable.Length == 0)
            {
                Debug.LogWarning(
                    "[B1F Story Debug] Disable Quest Barriers is checked, but no barriers are assigned.",
                    this);
                return;
            }

            foreach (QuestBarrier barrier in questBarriersToDisable)
                barrier?.SetStoryDebugBypassed(true);
#endif
        }

        /// <summary>Waits for a server condition and stores the outcome in <see cref="stepSucceeded"/>.</summary>
        private IEnumerator WaitServer(Func<bool> condition, float deadline, string timeoutError)
        {
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;

            stepSucceeded = condition();
            if (!stepSucceeded)
                Debug.LogError($"[B1F Story Debug] {timeoutError}", this);
        }

        private float StoryDeadline() => Time.realtimeSinceStartup + storyStateTimeout;

        private bool ValidateDistributionBox()
        {
            if (distributionBoxA == null)
            {
                Debug.LogError("[B1F Story Debug] Distribution Box A is not assigned.", this);
                return false;
            }
            if (!distributionBoxA.IsBoxA)
            {
                Debug.LogError(
                    "[B1F Story Debug] The assigned distribution box is not configured as Box A.",
                    distributionBoxA);
                return false;
            }
            return true;
        }

        private static CooperativeTerminalHintRelay FindServerTerminalRelay(
            NetworkManager manager)
        {
            foreach (NetworkClient client in manager.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                    continue;

                CooperativeTerminalHintRelay relay =
                    client.PlayerObject.GetComponentInChildren<CooperativeTerminalHintRelay>(true);
                if (relay != null && relay.IsSpawned && relay.IsServer)
                    return relay;
            }
            return null;
        }

        private static bool IsNetworkReady()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening;
        }
    }
}
