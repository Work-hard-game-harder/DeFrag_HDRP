using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DeFrag.B1F
{
    [DisallowMultipleComponent]
    public sealed class B1FStoryDebugCheckpoint : MonoBehaviour
    {
        [Header("Editor / Development Build Only")]
        [Tooltip("배전함 A 성공 직후 상태로 시작합니다. Connect Server 퀘스트가 활성화됩니다.")]
        [SerializeField] private bool startAfterDistributionBoxA;
        [Tooltip("Connect Server 성공 직후 상태로 시작합니다. Full Power 퀘스트가 활성화됩니다.")]
        [SerializeField] private bool startAfterConnectServer;
        [Tooltip("스폰된 TV 몬스터의 AI와 이동을 서버에서 정지시킵니다.")]
        [SerializeField] private bool freezeTvMonsterInPlace;

        [Header("Local Debug Bypass")]
        [Tooltip("체크하면 지정된 Quest Barrier의 Collider를 호스트와 각 클라이언트에서 비활성화합니다.")]
        [SerializeField] private bool disableQuestBarriers;
        [SerializeField] private QuestBarrier[] questBarriersToDisable;

        [Header("Scene References")]
        [SerializeField] private DistributionBoxController distributionBoxA;
        [SerializeField] private B1FPowerController powerController;
        [SerializeField] private ConnectServerCoordinator connectServerCoordinator;

        [Header("Checkpoint Quest IDs")]
        [SerializeField] private string distributionBoxAQuestId = "b1f_emergency_power";
        [SerializeField] private string connectServerQuestId = "b1f_connect_server";

        [Header("Connect Server Terminal")]
        [SerializeField] private string connectServerTerminalId = "terminal_31";
        [SerializeField, Min(1f)] private float networkReadyTimeout = 20f;

        private IEnumerator Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ApplyLocalQuestBarrierOverride();

            bool useStoryCheckpoint = startAfterDistributionBoxA || startAfterConnectServer;
            if (!useStoryCheckpoint && !freezeTvMonsterInPlace)
                yield break;

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

            // Every peer owns this scene object. Shared story state and monster AI
            // are changed only by the server.
            if (!manager.IsServer)
                yield break;

            if (powerController == null)
            {
                Debug.LogError("[B1F Story Debug] Power Controller is not assigned.", this);
                yield break;
            }

            while (!powerController.IsSpawned && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!powerController.IsSpawned)
            {
                Debug.LogError("[B1F Story Debug] Power Controller was not spawned.", this);
                yield break;
            }

            powerController.SetTvMonsterStoryDebugFrozenServer(freezeTvMonsterInPlace);
            if (!useStoryCheckpoint)
            {
                Debug.Log("[B1F Story Debug] TV Monster position lock is armed.", this);
                yield break;
            }

            while ((QuestManager.Instance == null || !QuestManager.Instance.IsInitialized) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            if (QuestManager.Instance == null || !QuestManager.Instance.IsInitialized)
            {
                Debug.LogError("[B1F Story Debug] QuestManager was not initialized.", this);
                yield break;
            }

            if (!ValidateDistributionBox())
                yield break;

            while (!distributionBoxA.IsSpawned && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!distributionBoxA.IsSpawned)
            {
                Debug.LogError(
                    "[B1F Story Debug] Distribution Box A NetworkObject was not spawned.",
                    this);
                yield break;
            }

            if (!distributionBoxA.IsCompleted &&
                !distributionBoxA.TryCompleteForStoryDebugServer())
            {
                Debug.LogError(
                    "[B1F Story Debug] Distribution Box A could not enter the debug checkpoint.",
                    distributionBoxA);
                yield break;
            }

            while (!distributionBoxA.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!distributionBoxA.IsCompleted)
            {
                Debug.LogError("[B1F Story Debug] Distribution Box A completion timed out.", this);
                yield break;
            }

            if (startAfterConnectServer)
            {
                yield return ApplyAfterConnectServer(deadline, manager);
                yield break;
            }

            if (!QuestManager.Instance.TryCompleteThroughForStoryDebugServer(
                    distributionBoxAQuestId))
            {
                Debug.LogError(
                    "[B1F Story Debug] Could not apply the Distribution Box A quest checkpoint.",
                    this);
                yield break;
            }

            Debug.Log(
                "[B1F Story Debug] Applied AFTER DISTRIBUTION BOX A: " +
                "Emergency Power sequence started and b1f_connect_server is active.",
                this);
#else
            yield break;
#endif
        }

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

        private IEnumerator ApplyAfterConnectServer(float deadline, NetworkManager manager)
        {
            if (connectServerCoordinator == null)
            {
                Debug.LogError("[B1F Story Debug] Connect Server Coordinator is not assigned.", this);
                yield break;
            }

            while (!connectServerCoordinator.IsSpawned && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!connectServerCoordinator.TryCompleteForStoryDebugServer())
            {
                Debug.LogError("[B1F Story Debug] Connect Server could not be completed.", this);
                yield break;
            }

            CooperativeTerminalHintRelay relay = null;
            while (relay == null && Time.realtimeSinceStartup < deadline)
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

            if (!QuestManager.Instance.TryCompleteThroughForStoryDebugServer(connectServerQuestId))
            {
                Debug.LogError(
                    "[B1F Story Debug] Could not apply the Connect Server quest checkpoint.",
                    this);
                yield break;
            }

            Debug.Log(
                "[B1F Story Debug] Applied AFTER CONNECT SERVER: " +
                "Connect Server is complete and b1f_full_power is active.",
                this);
        }

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
