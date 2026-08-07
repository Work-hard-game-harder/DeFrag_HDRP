using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace DeFrag.B1F
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class DistributionBoxController : NetworkBehaviour, IInteractable
    {
        private const int SwitchCount = 15;
        private const ushort SwitchMask = (1 << SwitchCount) - 1;
        private const ulong NoClient = ulong.MaxValue;

        [Header("Game Rules")]
        [SerializeField] private B1FPowerController powerController;
        [SerializeField] private bool isBoxA = true;
        [SerializeField, Min(1f)] private float maximumUseDistance = 6f;
        [SerializeField, Min(1f)] private float hintRefreshSeconds = 30f;

        [Header("Interaction")]
        [SerializeField] private string availableText = "배전함 열기 (E 홀드)";
        [SerializeField] private string occupiedText = "다른 플레이어가 조작 중입니다";
        [SerializeField] private string unavailableText = "현재 전력 상태에서는 사용할 수 없습니다";
        [Tooltip("조작 중에만 활성화할 배전함 전용 Camera입니다.")]
        [SerializeField] private Camera interactionCamera;
        [SerializeField, Min(0.5f)] private float localInteractionDistance = 4f;

        [Header("Box Door")]
        [SerializeField] private Transform doorPivot;
        [SerializeField] private Vector3 doorOpenLocalEulerOffset = new(0f, 110f, 0f);
        [SerializeField, Min(0.01f)] private float doorMoveDuration = 0.65f;
        [SerializeField] private AnimationCurve doorMoveCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Switches 001-015")]
        [SerializeField] private DistributionSwitch[] switches = new DistributionSwitch[SwitchCount];

        [Header("Main Knob")]
        [SerializeField] private Transform mainKnobPivot;
        [SerializeField] private Vector3 mainKnobRestLocalEuler = new(38.058f, 0f, 0f);
        [SerializeField] private Vector3 mainKnobFailureLocalEuler = new(-55f, 0f, 0f);
        [SerializeField] private Vector3 mainKnobSuccessLocalEuler = new(-110f, 0f, 0f);
        [SerializeField, Min(0.01f)] private float mainKnobMoveDuration = 0.5f;
        [SerializeField] private AnimationCurve mainKnobCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Shared Effects")]
        [SerializeField] private UnityEvent onFailureElectricalEffect = new();
        [SerializeField] private UnityEvent onSuccess = new();

        private readonly NetworkVariable<ulong> controllingClient = new(
            NoClient,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ushort> currentSwitchMask = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> completed = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private ushort answerMask;
        private bool hintSessionActive;
        private ulong hintOwnerClient = NoClient;
        private double nextHintRefreshTime;
        private Coroutine doorRoutine;
        private Coroutine mainKnobRoutine;
        private Vector3 capturedDoorClosedLocalEuler;
        private ushort offlineSwitchMask;
        private bool offlineOccupied;

        public bool IsCompleted => completed.Value;
        public bool IsHintSessionActive => hintSessionActive;

        private void Awake()
        {
            if (doorPivot != null)
                capturedDoorClosedLocalEuler = doorPivot.localEulerAngles;
            ConfigureSwitches();
            ApplySwitchMask(currentSwitchMask.Value, true);
            SetDoorImmediate(false);
            SetMainKnobImmediate(mainKnobRestLocalEuler);
        }

        public override void OnNetworkSpawn()
        {
            controllingClient.OnValueChanged += OnControllerChanged;
            currentSwitchMask.OnValueChanged += OnSwitchMaskChanged;
            completed.OnValueChanged += OnCompletedChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            ApplySwitchMask(currentSwitchMask.Value, true);
            SetDoorImmediate(controllingClient.Value != NoClient);
            if (completed.Value) SetMainKnobImmediate(mainKnobSuccessLocalEuler);
        }

        public override void OnNetworkDespawn()
        {
            controllingClient.OnValueChanged -= OnControllerChanged;
            currentSwitchMask.OnValueChanged -= OnSwitchMaskChanged;
            completed.OnValueChanged -= OnCompletedChanged;
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        private void Update()
        {
            if (!IsServer || !hintSessionActive || NetworkManager == null)
                return;

            if (NetworkManager.ServerTime.Time >= nextHintRefreshTime)
                GenerateAndSendHint();
        }

        public string GetInteractionText()
        {
            if (!CanUseForCurrentPowerState()) return unavailableText;
            return controllingClient.Value == NoClient ? availableText : occupiedText;
        }

        public bool IsHoldInteraction() => true;

        public void Interact(PlayerInteraction player)
        {
            if (player == null || completed.Value) return;

            Debug.Log($"[DistributionBox] Hold completed. IsSpawned={IsSpawned}, " +
                      $"CanUse={CanUseForCurrentPowerState()}", this);

            NetworkObject playerNetworkObject = player.GetComponentInParent<NetworkObject>();
            if (IsSpawned && playerNetworkObject != null)
            {
                RequestControlServerRpc();
                return;
            }

            if (offlineOccupied || !CanUseForCurrentPowerState())
                return;
            offlineOccupied = true;
            AnimateDoor(true);
            BeginLocalSession(player);
        }

        public void RequestToggleFromLocalPlayer(int index)
        {
            if (index < 0 || index >= SwitchCount) return;
            if (IsSpawned) ToggleSwitchServerRpc(index);
            else ApplyOfflineToggle(index);
        }

        public void RequestSubmitFromLocalPlayer()
        {
            if (IsSpawned) SubmitServerRpc();
        }

        public void RequestReleaseFromLocalPlayer()
        {
            if (IsSpawned) ReleaseControlServerRpc();
            else
            {
                offlineOccupied = false;
                AnimateDoor(false);
            }
        }

        public void RequestHintSessionFromLocalPlayer()
        {
            if (IsSpawned) StartHintSessionServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestControlServerRpc(ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (controllingClient.Value != NoClient)
            {
                Debug.Log($"[DistributionBox] Client {sender} rejected: already occupied.", this);
                return;
            }
            if (completed.Value || !CanUseForCurrentPowerState())
            {
                Debug.Log($"[DistributionBox] Client {sender} rejected: unavailable power state.", this);
                return;
            }
            if (!IsClientNearBox(sender))
            {
                Debug.Log($"[DistributionBox] Client {sender} rejected: farther than {maximumUseDistance}m.", this);
                return;
            }

            controllingClient.Value = sender;
            Debug.Log($"[DistributionBox] Client {sender} acquired control.", this);
            BeginSessionClientRpc(Target(sender));
        }

        [ServerRpc(RequireOwnership = false)]
        private void ToggleSwitchServerRpc(int index, ServerRpcParams rpc = default)
        {
            if (!CanControllerAct(rpc.Receive.SenderClientId) || index < 0 || index >= SwitchCount)
                return;

            currentSwitchMask.Value ^= (ushort)(1 << index);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitServerRpc(ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!CanControllerAct(sender) || !hintSessionActive)
                return;

            if ((currentSwitchMask.Value & SwitchMask) == answerMask)
            {
                completed.Value = true;
                hintSessionActive = false;
                HideHintClientRpc(Target(hintOwnerClient));
                hintOwnerClient = NoClient;
                PlaySuccessClientRpc();
                onSuccess?.Invoke();
                if (isBoxA) powerController?.SetEmergencyPowerServer();
                else powerController?.SetFullPowerServer();
                EndSessionClientRpc(Target(sender));
                controllingClient.Value = NoClient;
            }
            else
            {
                PlayFailureClientRpc();
                InvalidateHintSession(true);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReleaseControlServerRpc(ServerRpcParams rpc = default)
        {
            if (controllingClient.Value == rpc.Receive.SenderClientId)
                controllingClient.Value = NoClient;
        }

        [ServerRpc(RequireOwnership = false)]
        private void StartHintSessionServerRpc(ServerRpcParams rpc = default)
        {
            if (completed.Value || !CanUseForCurrentPowerState()) return;
            if (controllingClient.Value == rpc.Receive.SenderClientId) return;
            hintOwnerClient = rpc.Receive.SenderClientId;
            hintSessionActive = true;
            GenerateAndSendHint();
        }

        [ClientRpc]
        private void BeginSessionClientRpc(ClientRpcParams rpc = default)
        {
            PlayerInteraction player = FindLocalPlayerInteraction();
            if (player != null) BeginLocalSession(player);
        }

        [ClientRpc]
        private void EndSessionClientRpc(ClientRpcParams rpc = default)
        {
            DistributionBoxLocalSession.Active?.EndSession();
        }

        [ClientRpc]
        private void ShowHintClientRpc(ushort mask, float duration, ClientRpcParams rpc = default)
        {
            DistributionHintPresenter.GetOrCreate().Show(mask, duration);
        }

        [ClientRpc]
        private void HideHintClientRpc(ClientRpcParams rpc = default)
        {
            DistributionHintPresenter.TryHide();
        }

        [ClientRpc]
        private void ResetTerminalClientRpc(ClientRpcParams rpc = default)
        {
            DistributionHintPresenter.TryHide();
            B1FDistributionTerminalAdapter.ResetLocalTerminal();
        }

        [ClientRpc]
        private void PlayFailureClientRpc()
        {
            AnimateMainKnobFailure();
            onFailureElectricalEffect?.Invoke();
        }

        [ClientRpc]
        private void PlaySuccessClientRpc()
        {
            AnimateMainKnob(mainKnobSuccessLocalEuler, false);
        }

        private void GenerateAndSendHint()
        {
            if (!IsServer || hintOwnerClient == NoClient) return;

            ushort next;
            do next = (ushort)Random.Range(0, SwitchMask + 1);
            while (next == answerMask);
            answerMask = next;
            nextHintRefreshTime = NetworkManager.ServerTime.Time + hintRefreshSeconds;
            ShowHintClientRpc(answerMask, hintRefreshSeconds, Target(hintOwnerClient));
        }

        private void InvalidateHintSession(bool resetTerminal)
        {
            if (!hintSessionActive || hintOwnerClient == NoClient) return;
            ulong previousOwner = hintOwnerClient;
            hintSessionActive = false;
            hintOwnerClient = NoClient;
            answerMask = 0;
            if (resetTerminal) ResetTerminalClientRpc(Target(previousOwner));
            else HideHintClientRpc(Target(previousOwner));
        }

        private bool CanControllerAct(ulong sender) =>
            controllingClient.Value == sender && !completed.Value && CanUseForCurrentPowerState();

        private bool CanUseForCurrentPowerState()
        {
            if (powerController == null) return false;
            return isBoxA ? powerController.CanUseBoxA : powerController.CanUseBoxB;
        }

        private bool IsClientNearBox(ulong clientId)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
                client.PlayerObject == null)
                return false;
            Vector3 playerPosition = client.PlayerObject.transform.position;
            float maximumDistanceSqr = maximumUseDistance * maximumUseDistance;
            Collider[] boxColliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider boxCollider in boxColliders)
            {
                if (boxCollider == null || !boxCollider.enabled) continue;
                Vector3 closestPoint = boxCollider.ClosestPoint(playerPosition);
                if ((playerPosition - closestPoint).sqrMagnitude <= maximumDistanceSqr)
                    return true;
            }

            return (playerPosition - transform.position).sqrMagnitude <= maximumDistanceSqr;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (controllingClient.Value == clientId) controllingClient.Value = NoClient;
            if (hintOwnerClient == clientId)
            {
                hintSessionActive = false;
                hintOwnerClient = NoClient;
                answerMask = 0;
            }
        }

        private void OnValidate()
        {
            if (switches == null || switches.Length != SwitchCount)
                Debug.LogWarning("[DistributionBox] Switches 배열은 Knob 001~015 순서의 15칸이어야 합니다.", this);
        }

        private void OnControllerChanged(ulong previous, ulong next) => AnimateDoor(next != NoClient);
        private void OnSwitchMaskChanged(ushort previous, ushort next) => ApplySwitchMask(next, false);
        private void OnCompletedChanged(bool previous, bool next)
        {
            if (next) SetDoorImmediate(false);
        }

        private void ConfigureSwitches()
        {
            for (int i = 0; i < switches.Length; i++)
                if (switches[i] != null) switches[i].Configure(i);
        }

        private void ApplySwitchMask(ushort mask, bool immediate)
        {
            for (int i = 0; i < switches.Length && i < SwitchCount; i++)
                switches[i]?.ApplyState((mask & (1 << i)) != 0, immediate);
        }

        private void ApplyOfflineToggle(int index)
        {
            offlineSwitchMask ^= (ushort)(1 << index);
            ApplySwitchMask(offlineSwitchMask, false);
        }

        private void BeginLocalSession(PlayerInteraction player)
        {
            Camera camera = player.GetComponent<Camera>();
            if (camera == null) camera = player.GetComponentInChildren<Camera>(true);
            if (camera == null || interactionCamera == null)
            {
                Debug.LogError("[DistributionBox] Player Camera or Interaction Camera is not assigned.", this);
                return;
            }

            DistributionBoxLocalSession session =
                camera.GetComponent<DistributionBoxLocalSession>() ??
                camera.gameObject.AddComponent<DistributionBoxLocalSession>();
            session.Begin(this, player, camera, interactionCamera, localInteractionDistance);
        }

        private static PlayerInteraction FindLocalPlayerInteraction()
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject playerObject = manager != null && manager.LocalClient != null
                ? manager.LocalClient.PlayerObject
                : null;
            return playerObject != null
                ? playerObject.GetComponentInChildren<PlayerInteraction>(true)
                : null;
        }

        private static ClientRpcParams Target(ulong clientId) => new()
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        private void AnimateDoor(bool open)
        {
            if (doorPivot == null) return;
            if (doorRoutine != null) StopCoroutine(doorRoutine);
            doorRoutine = StartCoroutine(RotateRoutine(
                doorPivot,
                open ? capturedDoorClosedLocalEuler + doorOpenLocalEulerOffset : capturedDoorClosedLocalEuler,
                doorMoveDuration,
                doorMoveCurve,
                () => doorRoutine = null));
        }

        private void SetDoorImmediate(bool open)
        {
            if (doorPivot != null)
                doorPivot.localRotation = Quaternion.Euler(
                    open ? capturedDoorClosedLocalEuler + doorOpenLocalEulerOffset : capturedDoorClosedLocalEuler);
        }

        private void AnimateMainKnobFailure()
        {
            if (mainKnobRoutine != null) StopCoroutine(mainKnobRoutine);
            mainKnobRoutine = StartCoroutine(MainKnobFailureRoutine());
        }

        private IEnumerator MainKnobFailureRoutine()
        {
            yield return RotateRoutine(mainKnobPivot, mainKnobFailureLocalEuler,
                mainKnobMoveDuration, mainKnobCurve, null);
            yield return RotateRoutine(mainKnobPivot, mainKnobRestLocalEuler,
                mainKnobMoveDuration, mainKnobCurve, null);
            mainKnobRoutine = null;
        }

        private void AnimateMainKnob(Vector3 target, bool returnToRest)
        {
            if (mainKnobRoutine != null) StopCoroutine(mainKnobRoutine);
            mainKnobRoutine = StartCoroutine(RotateRoutine(
                mainKnobPivot, target, mainKnobMoveDuration, mainKnobCurve,
                () => mainKnobRoutine = null));
        }

        private void SetMainKnobImmediate(Vector3 euler)
        {
            if (mainKnobPivot != null) mainKnobPivot.localRotation = Quaternion.Euler(euler);
        }

        private static IEnumerator RotateRoutine(
            Transform target,
            Vector3 targetEuler,
            float duration,
            AnimationCurve curve,
            System.Action completedAction)
        {
            if (target == null) yield break;
            Quaternion start = target.localRotation;
            Quaternion end = Quaternion.Euler(targetEuler);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = curve.Evaluate(Mathf.Clamp01(elapsed / duration));
                target.localRotation = Quaternion.SlerpUnclamped(start, end, t);
                yield return null;
            }
            target.localRotation = end;
            completedAction?.Invoke();
        }
    }
}
