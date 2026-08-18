using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace DeFrag.B1F
{
    public enum DistributionCameraView : byte
    {
        BankA,
        BankB,
        BankC,
        MainKnob
    }

    public enum DistributionPuzzlePhase : byte
    {
        WaitingForBankAData,
        BankA,
        WaitingForBankBData,
        BankB,
        WaitingForBankCData,
        BankC,
        MainKnob,
        Completed
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class DistributionBoxController : NetworkBehaviour, IInteractable
    {
        private const int SwitchCount = 15;
        private const int SwitchesPerBank = 5;
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

        [Header("Camera Presets")]
        [Tooltip("These cameras are disabled pose/FOV presets. Only Interaction Camera renders.")]
        [SerializeField] private Camera bankACameraPreset;
        [SerializeField] private Camera bankBCameraPreset;
        [SerializeField] private Camera bankCCameraPreset;
        [SerializeField] private Camera mainKnobCameraPreset;
        [SerializeField, Range(1f, 179f)] private float bankFieldOfView = 39.5f;
        [SerializeField, Range(1f, 179f)] private float mainKnobFieldOfView = 60f;

        [Header("Local Camera Presentation")]
        [SerializeField, Min(0.01f)] private float cameraBlendDuration = 0.75f;
        [SerializeField] private AnimationCurve cameraBlendCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Min(0.01f)] private float cameraLookSensitivity = 2f;
        [SerializeField, Range(0f, 90f)] private float cameraYawLimit = 24f;
        [SerializeField, Range(0f, 90f)] private float cameraPitchLimit = 18f;
        [SerializeField, Range(0f, 90f)] private float cameraDownPitchLimit = 42f;

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
        [Tooltip("현재 로컬 회전을 기준으로 실패할 때 추가할 회전 오프셋입니다.")]
        [SerializeField] private Vector3 mainKnobFailureLocalEuler = new(-55f, 0f, 0f);
        [Tooltip("현재 로컬 회전을 기준으로 성공할 때 추가할 회전 오프셋입니다.")]
        [SerializeField] private Vector3 mainKnobSuccessLocalEuler = new(-110f, 0f, 0f);
        [Tooltip("노브 콜라이더가 가려져도 화면 중앙에서 이 반경 안에 피벗이 있으면 제출 대상으로 판정합니다.")]
        [SerializeField, Range(0.01f, 0.2f)] private float mainKnobAimViewportRadius = 0.075f;
        [SerializeField, Min(0.01f)] private float mainKnobMoveDuration = 0.5f;
        [SerializeField] private AnimationCurve mainKnobCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Shared Effects")]
        [SerializeField] private UnityEvent onFailureElectricalEffect = new();
        [SerializeField] private UnityEvent onSuccess = new();

        [Header("Shared Audio")]
        [SerializeField] private AudioSource interactionAudioSource;
        [SerializeField] private AudioClip switchToggleClip;
        [SerializeField] private AudioClip mainKnobPullClip;
        [SerializeField] private AudioClip mainKnobFailureClip;

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
        private readonly NetworkVariable<DistributionPuzzlePhase> phase = new(
            DistributionPuzzlePhase.WaitingForBankAData,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private ushort answerMask;
        private bool hintSessionActive;
        private ulong hintOwnerClient = NoClient;
        private double nextHintRefreshTime;
        private Coroutine doorRoutine;
        private Coroutine mainKnobRoutine;
        private Quaternion capturedDoorClosedLocalRotation = Quaternion.identity;
        private Quaternion capturedMainKnobRestLocalRotation = Quaternion.identity;
        private ushort offlineSwitchMask;
        private bool offlineOccupied;

        public bool IsCompleted => completed.Value;
        public bool IsHintSessionActive => hintSessionActive;
        public DistributionPuzzlePhase Phase => phase.Value;

        private void Awake()
        {
            if (doorPivot != null)
                capturedDoorClosedLocalRotation = doorPivot.localRotation;
            if (mainKnobPivot != null)
                capturedMainKnobRestLocalRotation = mainKnobPivot.localRotation;
            ConfigureSwitches();
            ApplySwitchMask(currentSwitchMask.Value, true);
            SetDoorImmediate(false);
            SetMainKnobImmediate(Vector3.zero);
        }

        public override void OnNetworkSpawn()
        {
            controllingClient.OnValueChanged += OnControllerChanged;
            currentSwitchMask.OnValueChanged += OnSwitchMaskChanged;
            completed.OnValueChanged += OnCompletedChanged;
            phase.OnValueChanged += OnPhaseChanged;
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
            phase.OnValueChanged -= OnPhaseChanged;
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
            if (!IsSwitchInActiveBank(index)) return;
            if (IsSpawned) ToggleSwitchServerRpc(index);
            else ApplyOfflineToggle(index);
        }

        public void RequestSubmitFromLocalPlayer()
        {
            Debug.Log(
                $"[DistributionBox] MainKnob pressed. IsSpawned={IsSpawned}, " +
                $"IsOwner={IsOwner}, IsHintActive={hintSessionActive}.",
                this);
            if (IsSpawned) SubmitServerRpc();
            else ApplyOfflineSubmit();
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
            else StartOfflineHintSession();
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
            if (!CanControllerAct(rpc.Receive.SenderClientId) || index < 0 ||
                index >= SwitchCount || !IsSwitchInActiveBank(index))
                return;

            currentSwitchMask.Value ^= (ushort)(1 << index);
            TryCompleteActiveBank();
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitServerRpc(ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!CanControllerAct(sender))
            {
                Debug.LogWarning(
                    $"[DistributionBox] MainKnob submit from client {sender} was rejected: " +
                    $"controller={controllingClient.Value}, completed={completed.Value}, " +
                    $"canUse={CanUseForCurrentPowerState()}.",
                    this);
                return;
            }

            if (phase.Value != DistributionPuzzlePhase.MainKnob)
            {
                PlayRejectedSubmitClientRpc(Target(sender));
                return;
            }

            CompletePuzzle(sender);
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
            if (!IsWaitingForBankData(phase.Value)) return;
            hintOwnerClient = rpc.Receive.SenderClientId;
            hintSessionActive = true;
            phase.Value = phase.Value switch
            {
                DistributionPuzzlePhase.WaitingForBankAData => DistributionPuzzlePhase.BankA,
                DistributionPuzzlePhase.WaitingForBankBData => DistributionPuzzlePhase.BankB,
                _ => DistributionPuzzlePhase.BankC
            };
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
        private void ShowHintClientRpc(
            ushort mask,
            int bankIndex,
            float duration,
            ClientRpcParams rpc = default)
        {
            DistributionHintPresenter.GetOrCreate().Show(mask, bankIndex, duration);
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
            PlayOneShot(mainKnobPullClip);
            PlayOneShot(mainKnobFailureClip);
            AnimateMainKnobFailure();
            onFailureElectricalEffect?.Invoke();
        }

        [ClientRpc]
        private void AdvanceTerminalClientRpc(
            DistributionPuzzlePhase nextPhase,
            ClientRpcParams rpc = default)
        {
            DistributionHintPresenter.TryHide();
            B1FDistributionTerminalAdapter.NotifyLocalBankAdvanced(nextPhase);
        }

        [ClientRpc]
        private void PlayRejectedSubmitClientRpc(ClientRpcParams rpc = default)
        {
            PlayOneShot(mainKnobPullClip);
            AnimateMainKnobFailure();
        }

        [ClientRpc]
        private void PlaySuccessClientRpc()
        {
            PlayOneShot(mainKnobPullClip);
            AnimateMainKnob(mainKnobSuccessLocalEuler, false);
        }

        private void GenerateAndSendHint()
        {
            if (!IsServer || hintOwnerClient == NoClient) return;
            int bankIndex = GetActiveBankIndex();
            if (bankIndex < 0) return;
            ushort bankMask = GetBankMask(bankIndex);
            int bankOffset = bankIndex * SwitchesPerBank;
            ushort currentLocalMask =
                (ushort)((currentSwitchMask.Value & bankMask) >> bankOffset);
            ushort nextLocalMask;
            do nextLocalMask = (ushort)Random.Range(1, 1 << SwitchesPerBank);
            while (nextLocalMask == currentLocalMask);
            answerMask = (ushort)((currentSwitchMask.Value & ~bankMask) |
                                  (nextLocalMask << bankOffset));
            nextHintRefreshTime = NetworkManager.ServerTime.Time + hintRefreshSeconds;
            ShowHintClientRpc(answerMask, bankIndex, hintRefreshSeconds, Target(hintOwnerClient));
        }

        private void TryCompleteActiveBank()
        {
            if (!IsServer || !hintSessionActive) return;
            int bankIndex = GetActiveBankIndex();
            if (bankIndex < 0) return;
            ushort bankMask = GetBankMask(bankIndex);
            if ((currentSwitchMask.Value & bankMask) != (answerMask & bankMask)) return;

            hintSessionActive = false;
            DistributionPuzzlePhase nextPhase = bankIndex switch
            {
                0 => DistributionPuzzlePhase.WaitingForBankBData,
                1 => DistributionPuzzlePhase.WaitingForBankCData,
                _ => DistributionPuzzlePhase.MainKnob
            };
            phase.Value = nextPhase;
            ulong terminalClient = hintOwnerClient;
            hintOwnerClient = NoClient;
            answerMask = 0;
            AdvanceTerminalClientRpc(nextPhase, Target(terminalClient));
        }

        private void CompletePuzzle(ulong controllingPlayer)
        {
            completed.Value = true;
            phase.Value = DistributionPuzzlePhase.Completed;
            PlaySuccessClientRpc();
            onSuccess?.Invoke();
            if (isBoxA) powerController?.SetEmergencyPowerServer();
            else powerController?.SetFullPowerServer();
            EndSessionClientRpc(Target(controllingPlayer));
            controllingClient.Value = NoClient;
        }

        private static bool IsWaitingForBankData(DistributionPuzzlePhase value) =>
            value == DistributionPuzzlePhase.WaitingForBankAData ||
            value == DistributionPuzzlePhase.WaitingForBankBData ||
            value == DistributionPuzzlePhase.WaitingForBankCData;

        private int GetActiveBankIndex() => phase.Value switch
        {
            DistributionPuzzlePhase.BankA => 0,
            DistributionPuzzlePhase.BankB => 1,
            DistributionPuzzlePhase.BankC => 2,
            _ => -1
        };

        private bool IsSwitchInActiveBank(int index)
        {
            int bankIndex = GetActiveBankIndex();
            return bankIndex >= 0 && index / SwitchesPerBank == bankIndex;
        }

        private static ushort GetBankMask(int bankIndex) =>
            (ushort)(((1 << SwitchesPerBank) - 1) << (bankIndex * SwitchesPerBank));

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
        private void OnSwitchMaskChanged(ushort previous, ushort next)
        {
            ApplySwitchMask(next, false);
            if (previous != next) PlayOneShot(switchToggleClip);
        }
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
            PlayOneShot(switchToggleClip);
        }

        private void OnPhaseChanged(
            DistributionPuzzlePhase previous,
            DistributionPuzzlePhase next)
        {
            ShowLocalCameraView(GetCameraViewForPhase(next));
        }

        private static DistributionCameraView GetCameraViewForPhase(
            DistributionPuzzlePhase value) => value switch
            {
                DistributionPuzzlePhase.BankB or DistributionPuzzlePhase.WaitingForBankBData =>
                    DistributionCameraView.BankB,
                DistributionPuzzlePhase.BankC or DistributionPuzzlePhase.WaitingForBankCData =>
                    DistributionCameraView.BankC,
                DistributionPuzzlePhase.MainKnob or DistributionPuzzlePhase.Completed =>
                    DistributionCameraView.MainKnob,
                _ => DistributionCameraView.BankA
            };

        private void StartOfflineHintSession()
        {
            if (completed.Value || !CanUseForCurrentPowerState()) return;

            hintSessionActive = true;
            answerMask = GenerateDifferentMask(answerMask);
            DistributionHintPresenter.GetOrCreate().Show(answerMask, 0, hintRefreshSeconds);
        }

        private void ApplyOfflineSubmit()
        {
            if (!offlineOccupied || completed.Value || !CanUseForCurrentPowerState())
                return;

            if (!hintSessionActive)
            {
                PlayOneShot(mainKnobPullClip);
                AnimateMainKnobFailure();
                return;
            }

            if ((offlineSwitchMask & SwitchMask) == answerMask)
            {
                hintSessionActive = false;
                DistributionHintPresenter.TryHide();
                PlayOneShot(mainKnobPullClip);
                AnimateMainKnob(mainKnobSuccessLocalEuler, false);
                onSuccess?.Invoke();
                return;
            }

            PlayOneShot(mainKnobPullClip);
            PlayOneShot(mainKnobFailureClip);
            AnimateMainKnobFailure();
            hintSessionActive = false;
            answerMask = 0;
            DistributionHintPresenter.TryHide();
            B1FDistributionTerminalAdapter.ResetLocalTerminal();
        }

        private static ushort GenerateDifferentMask(ushort previous)
        {
            ushort next;
            do next = (ushort)Random.Range(0, SwitchMask + 1);
            while (next == previous);
            return next;
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
            session.Begin(
                this,
                player,
                camera,
                interactionCamera,
                GetCameraPreset(GetCameraViewForPhase(phase.Value)),
                GetCameraFieldOfView(GetCameraViewForPhase(phase.Value)),
                localInteractionDistance,
                cameraBlendDuration,
                cameraBlendCurve,
                cameraLookSensitivity,
                cameraYawLimit,
                cameraPitchLimit,
                cameraDownPitchLimit);
        }

        public void ShowLocalCameraView(DistributionCameraView view)
        {
            DistributionBoxLocalSession session = DistributionBoxLocalSession.Active;
            if (session == null || !session.IsFor(this)) return;
            session.BlendToPreset(
                GetCameraPreset(view),
                GetCameraFieldOfView(view),
                cameraBlendDuration,
                cameraBlendCurve);
        }

        private Camera GetCameraPreset(DistributionCameraView view)
        {
            Camera preset = view switch
            {
                DistributionCameraView.BankA => bankACameraPreset,
                DistributionCameraView.BankB => bankBCameraPreset,
                DistributionCameraView.BankC => bankCCameraPreset,
                _ => mainKnobCameraPreset
            };

            return preset != null ? preset : interactionCamera;
        }

        private float GetCameraFieldOfView(DistributionCameraView view) =>
            view == DistributionCameraView.MainKnob ? mainKnobFieldOfView : bankFieldOfView;

        private void PlayOneShot(AudioClip clip)
        {
            if (interactionAudioSource != null && clip != null)
                interactionAudioSource.PlayOneShot(clip);
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
                GetDoorLocalRotation(open),
                doorMoveDuration,
                doorMoveCurve,
                () => doorRoutine = null));
        }

        private void SetDoorImmediate(bool open)
        {
            if (doorPivot != null)
                doorPivot.localRotation = GetDoorLocalRotation(open);
        }

        private Quaternion GetDoorLocalRotation(bool open) =>
            open
                ? capturedDoorClosedLocalRotation * Quaternion.Euler(doorOpenLocalEulerOffset)
                : capturedDoorClosedLocalRotation;

        private void AnimateMainKnobFailure()
        {
            if (mainKnobRoutine != null) StopCoroutine(mainKnobRoutine);
            mainKnobRoutine = StartCoroutine(MainKnobFailureRoutine());
        }

        private IEnumerator MainKnobFailureRoutine()
        {
            yield return RotateRoutine(mainKnobPivot, GetMainKnobLocalRotation(mainKnobFailureLocalEuler),
                mainKnobMoveDuration, mainKnobCurve, null);
            yield return RotateRoutine(mainKnobPivot, capturedMainKnobRestLocalRotation,
                mainKnobMoveDuration, mainKnobCurve, null);
            mainKnobRoutine = null;
        }

        private void AnimateMainKnob(Vector3 target, bool returnToRest)
        {
            if (mainKnobRoutine != null) StopCoroutine(mainKnobRoutine);
            mainKnobRoutine = StartCoroutine(RotateRoutine(
                mainKnobPivot, GetMainKnobLocalRotation(target), mainKnobMoveDuration, mainKnobCurve,
                () => mainKnobRoutine = null));
        }

        private void SetMainKnobImmediate(Vector3 localEulerOffset)
        {
            if (mainKnobPivot != null)
                mainKnobPivot.localRotation = GetMainKnobLocalRotation(localEulerOffset);
        }

        private Quaternion GetMainKnobLocalRotation(Vector3 localEulerOffset) =>
            capturedMainKnobRestLocalRotation * Quaternion.Euler(localEulerOffset);

        public bool IsMainKnobUnderCrosshair(Camera camera, float maximumDistance)
        {
            if (camera == null || mainKnobPivot == null) return false;

            Renderer knobRenderer = mainKnobPivot.GetComponent<Renderer>() ??
                                    mainKnobPivot.GetComponentInChildren<Renderer>(true);
            if (knobRenderer == null)
            {
                Vector3 pivotViewport = camera.WorldToViewportPoint(mainKnobPivot.position);
                if (pivotViewport.z <= 0f || pivotViewport.z > maximumDistance) return false;
                Vector2 pivotOffset = new(pivotViewport.x - 0.5f, pivotViewport.y - 0.5f);
                return pivotOffset.sqrMagnitude <=
                       mainKnobAimViewportRadius * mainKnobAimViewportRadius;
            }

            Bounds bounds = knobRenderer.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector2 viewportMin = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 viewportMax = new(float.NegativeInfinity, float.NegativeInfinity);
            float nearestDepth = float.PositiveInfinity;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                Vector3 viewport = camera.WorldToViewportPoint(corner);
                if (viewport.z <= 0f) continue;

                nearestDepth = Mathf.Min(nearestDepth, viewport.z);
                viewportMin = Vector2.Min(viewportMin, viewport);
                viewportMax = Vector2.Max(viewportMax, viewport);
            }

            if (nearestDepth == float.PositiveInfinity || nearestDepth > maximumDistance)
                return false;

            Vector2 crosshair = new(0.5f, 0.5f);
            Vector2 padding = Vector2.one * mainKnobAimViewportRadius;
            return crosshair.x >= viewportMin.x - padding.x &&
                   crosshair.x <= viewportMax.x + padding.x &&
                   crosshair.y >= viewportMin.y - padding.y &&
                   crosshair.y <= viewportMax.y + padding.y;
        }

        private static IEnumerator RotateRoutine(
            Transform target,
            Vector3 targetEuler,
            float duration,
            AnimationCurve curve,
            System.Action completedAction)
        {
            return RotateRoutine(
                target,
                Quaternion.Euler(targetEuler),
                duration,
                curve,
                completedAction);
        }

        private static IEnumerator RotateRoutine(
            Transform target,
            Quaternion end,
            float duration,
            AnimationCurve curve,
            System.Action completedAction)
        {
            if (target == null) yield break;
            Quaternion start = target.localRotation;
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
