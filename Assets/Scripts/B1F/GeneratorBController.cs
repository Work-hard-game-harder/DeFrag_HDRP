using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DeFrag.B1F
{
    public enum GeneratorBSessionMode : byte
    {
        Search,
        Fuel
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class GeneratorBController : NetworkBehaviour
    {
        public const ulong NoController = ulong.MaxValue;
        public const string SearchCommand = "SEARCH FUEL_B_CONTINUOUS";

        public static GeneratorBController LocalInstance { get; private set; }
        public static event Action<GeneratorBController> LocalInstanceAvailable;

        [Header("Gameplay References")]
        [SerializeField] private B1FPowerController powerController;
        [Tooltip("Optional. When assigned, only the player carrying this item may start SEARCH.")]
        [SerializeField] private ItemData requiredHackingPad;
        [SerializeField] private GeneratorFuelCan fuelCan;
        [SerializeField] private GeneratorBInteractionPoint controlPanelPoint;
        [SerializeField] private GeneratorBInteractionPoint fuelInletPoint;
        [SerializeField] private Camera controlInteractionCamera;
        [SerializeField] private Camera fuelInteractionCamera;
        [SerializeField, Min(0.5f)] private float maximumInteractionDistance = 5f;

        [Header("Search")]
        [SerializeField, Min(1f)] private float signalInterval = 20f;

        [Header("Fuel Timing")]
        [SerializeField, Range(1, 8)] private int requiredPours = 3;
        [SerializeField, Min(0.25f)] private float gaugeOneWayDuration = 1.2f;
        [SerializeField, Range(0.05f, 0.6f)] private float successZoneWidth = 0.2f;
        [SerializeField, Min(0f)] private float nextAttemptDelay = 0.45f;

        [Header("Failure Noise")]
        [SerializeField, Min(0f)] private float failedPourNoiseRadius = 18f;
        [SerializeField] private AudioSource generatorAudioSource;
        [SerializeField] private AudioClip pourSuccessClip;
        [SerializeField] private AudioClip pourFailureClip;
        [SerializeField] private AudioClip generatorStartedClip;
        [SerializeField] private AudioClip generatorRunningLoopClip;
        [SerializeField, Range(0f, 1f)] private float generatorRunningLoopVolume = 1f;

        [Header("Local Pour Presentation")]
        [SerializeField] private Transform pourVisual;
        [SerializeField] private Vector3 pourTiltEuler = new(0f, 0f, 72f);
        [SerializeField, Min(0.05f)] private float pourTiltDuration = 0.35f;
        [SerializeField, Min(0.05f)] private float pourHoldDuration = 0.25f;

        [Header("Quest Signal")]
        [SerializeField] private string completionQuestSignal = QuestSignals.B1FGeneratorBCompleted;
        [SerializeField] private string completionQuestSourceId = "GENERATOR_B";

        private readonly NetworkVariable<bool> searchActive = new(
            false, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> controllingClient = new(
            NoController, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> successfulPours = new(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> timingTarget = new(
            0.5f, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> timingStart = new(
            0d, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> attemptSerial = new(
            0, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> completed = new(
            false, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private double nextSignalAt;
        private NetworkItemState lastFuelState = NetworkItemState.World;
        private Coroutine pourRoutine;
        private Coroutine generatorAudioRoutine;

        public bool SearchActive => searchActive.Value;
        public bool IsComplete => completed.Value;
        public int SuccessfulPours => successfulPours.Value;
        public int RequiredPours => requiredPours;
        public float FuelRatio => requiredPours <= 0
            ? 0f
            : Mathf.Clamp01(successfulPours.Value / (float)requiredPours);
        public int FuelPercent => requiredPours <= 0
            ? 0
            : Mathf.Clamp(successfulPours.Value * 100 / requiredPours, 0, 100);
        public float TimingTarget => timingTarget.Value;
        public float SuccessZoneWidth => successZoneWidth;
        public double TimingStart => timingStart.Value;
        public float GaugeOneWayDuration => gaugeOneWayDuration;
        public int AttemptSerial => attemptSerial.Value;
        public double ServerTime => NetworkManager != null && NetworkManager.IsListening
            ? NetworkManager.ServerTime.Time
            : Time.unscaledTimeAsDouble;
        public GeneratorFuelCan FuelCan => fuelCan;
        public bool FuelSignalVisible => searchActive.Value && !completed.Value &&
                                         fuelCan != null && fuelCan.WorldItem != null &&
                                         fuelCan.WorldItem.State == NetworkItemState.World;

        public override void OnNetworkSpawn()
        {
            LocalInstance = this;
            LocalInstanceAvailable?.Invoke(this);
            if (completed.Value)
                StartLocalGeneratorAudio(false);
            if (!IsServer)
                return;

            nextSignalAt = ServerTime;
            if (fuelCan != null && fuelCan.WorldItem != null)
                lastFuelState = fuelCan.WorldItem.State;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        private void Reset() => TryAutoAssignInteractionPoints();

        private void OnValidate()
        {
            requiredPours = Mathf.Clamp(requiredPours, 1, 8);
            TryAutoAssignInteractionPoints();
        }

        public override void OnNetworkDespawn()
        {
            if (generatorAudioRoutine != null)
            {
                StopCoroutine(generatorAudioRoutine);
                generatorAudioRoutine = null;
            }
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            if (LocalInstance == this)
            {
                LocalInstance = null;
                LocalInstanceAvailable?.Invoke(null);
            }
        }

        private void Update()
        {
            if (!IsServer || !searchActive.Value || completed.Value ||
                fuelCan == null || fuelCan.WorldItem == null)
                return;

            NetworkItemState state = fuelCan.WorldItem.State;
            if (state != lastFuelState)
            {
                lastFuelState = state;
                if (state == NetworkItemState.Held)
                    StopFuelSignalClientRpc();
                else
                    nextSignalAt = ServerTime;
            }

            if (state == NetworkItemState.World && ServerTime >= nextSignalAt)
            {
                PlayFuelSignalClientRpc();
                nextSignalAt = ServerTime + signalInterval;
            }
        }

        public string GetInteractionText(GeneratorBInteractionType interactionType)
        {
            if (completed.Value)
                return "발전기 B // FULL POWER";

            if (interactionType == GeneratorBInteractionType.FuelInlet)
                return HasLocalPlayerFuel()
                    ? "발전기 B 주유구 // 비상 연료 주입 (E)"
                    : "발전기 B 주유구 // 비상 연료 필요";

            if (requiredHackingPad != null && !HasLocalRequiredHackingPad())
                return "발전기 B 제어 패널 // 해킹패드 필요";
            return searchActive.Value
                ? "발전기 B 제어 패널 // 연료 탐색 재확인 (E)"
                : "발전기 B 제어 패널 // 시스템 진단 (E)";
        }

        public void InteractAt(
            GeneratorBInteractionType interactionType,
            PlayerInteraction player)
        {
            if (player == null || completed.Value || !IsPowerStateValid())
                return;
            RequestSessionServerRpc(interactionType);
        }

        public void SubmitSearchCommand(string command)
        {
            string normalized = string.IsNullOrWhiteSpace(command)
                ? string.Empty
                : command.Trim().ToUpperInvariant();
            SubmitSearchCommandServerRpc(normalized);
        }

        public void SubmitFuelHit(double observedServerTime, int submittedAttemptSerial) =>
            SubmitFuelHitServerRpc(observedServerTime, submittedAttemptSerial);

        public void ReleaseLocalControl()
        {
            if (IsSpawned)
                ReleaseControlServerRpc();
        }

        public float EvaluateGauge(double serverTimestamp)
        {
            if (gaugeOneWayDuration <= 0f)
                return 0f;
            return Mathf.PingPong(
                Mathf.Max(0f, (float)(serverTimestamp - timingStart.Value)) /
                gaugeOneWayDuration,
                1f);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSessionServerRpc(
            GeneratorBInteractionType interactionType,
            ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            GeneratorBInteractionPoint requestedPoint = GetInteractionPoint(interactionType);
            if (completed.Value || !IsPowerStateValid() ||
                (controllingClient.Value != NoController && controllingClient.Value != sender) ||
                requestedPoint == null ||
                !TryGetPlayer(sender, out NetworkObject playerObject) ||
                Vector3.Distance(playerObject.transform.position, requestedPoint.transform.position) >
                maximumInteractionDistance)
                return;

            GeneratorBSessionMode mode = interactionType == GeneratorBInteractionType.FuelInlet
                ? GeneratorBSessionMode.Fuel
                : GeneratorBSessionMode.Search;
            if (mode == GeneratorBSessionMode.Fuel && !IsFuelHeldBy(sender))
                return;
            if (mode == GeneratorBSessionMode.Search &&
                requiredHackingPad != null &&
                (!playerObject.TryGetComponent(out NetworkPlayerInventory inventory) ||
                 !inventory.ContainsHeldItem(requiredHackingPad)))
                return;

            controllingClient.Value = sender;
            if (mode == GeneratorBSessionMode.Fuel)
                BeginAttemptServer();

            BeginLocalSessionClientRpc(
                mode,
                TargetClient(sender));
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitSearchCommandServerRpc(
            string command,
            ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (sender != controllingClient.Value || completed.Value)
                return;

            bool accepted = string.Equals(
                command, SearchCommand, StringComparison.OrdinalIgnoreCase);
            if (accepted)
            {
                searchActive.Value = true;
                nextSignalAt = ServerTime;
                controllingClient.Value = NoController;
            }

            ResolveSearchCommandClientRpc(accepted, TargetClient(sender));
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitFuelHitServerRpc(
            double observedServerTime,
            int submittedAttemptSerial,
            ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (sender != controllingClient.Value || completed.Value ||
                !IsFuelHeldBy(sender) || submittedAttemptSerial != attemptSerial.Value)
                return;

            double minAllowed = ServerTime - 1.5d;
            double maxAllowed = ServerTime + 0.15d;
            double timestamp = Math.Clamp(observedServerTime, minAllowed, maxAllowed);
            float position = EvaluateGauge(timestamp);
            bool success = Math.Abs(position - timingTarget.Value) <=
                           successZoneWidth * 0.5f;

            if (!success)
            {
                Vector3 noisePosition = fuelInletPoint != null
                    ? fuelInletPoint.transform.position
                    : transform.position;
                WorldNoiseSystem.Emit(noisePosition, failedPourNoiseRadius);
                PlayPourResultClientRpc(false);
                BeginAttemptServer();
                return;
            }

            successfulPours.Value++;
            PlayPourResultClientRpc(true);
            if (successfulPours.Value < requiredPours)
            {
                BeginAttemptServer();
                return;
            }

            if (!TryGetPlayer(sender, out NetworkObject playerObject) ||
                !playerObject.TryGetComponent(out NetworkPlayerInventory inventory) ||
                fuelCan == null || fuelCan.WorldItem == null ||
                !inventory.TryConsumeHeldItemServer(fuelCan.WorldItem.NetworkObjectId))
            {
                successfulPours.Value = (byte)Mathf.Max(0, requiredPours - 1);
                BeginAttemptServer();
                return;
            }

            searchActive.Value = false;
            completed.Value = true;
            controllingClient.Value = NoController;
            if (QuestManager.Instance != null && !string.IsNullOrWhiteSpace(completionQuestSignal))
                QuestManager.Instance.ReportProgress(
                    completionQuestSignal,
                    completionQuestSourceId);
            StopFuelSignalClientRpc();
            PlayGeneratorStartedClientRpc();
            Vector3 investigationPosition = fuelInletPoint != null
                ? fuelInletPoint.transform.position
                : transform.position;
            if (powerController != null &&
                !powerController.ForceTvMonsterInvestigateServer(investigationPosition))
            {
                Debug.LogWarning(
                    "[GeneratorB] TV Monster could not begin forced generator investigation.",
                    this);
            }
            powerController?.SetFullPowerServer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReleaseControlServerRpc(ServerRpcParams rpc = default)
        {
            if (controllingClient.Value == rpc.Receive.SenderClientId)
                controllingClient.Value = NoController;
        }

        private void BeginAttemptServer()
        {
            timingTarget.Value = UnityEngine.Random.Range(0.22f, 0.78f);
            timingStart.Value = ServerTime + nextAttemptDelay;
            attemptSerial.Value++;
        }

        private bool IsFuelHeldBy(ulong clientId) =>
            fuelCan != null && fuelCan.WorldItem != null &&
            fuelCan.WorldItem.State == NetworkItemState.Held &&
            fuelCan.WorldItem.HolderClientId == clientId;

        private bool HasLocalPlayerFuel()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening &&
                   IsFuelHeldBy(manager.LocalClientId);
        }

        private bool HasLocalRequiredHackingPad()
        {
            if (requiredHackingPad == null)
                return true;

            NetworkManager manager = NetworkManager.Singleton;
            NetworkPlayerInventory inventory = manager?.LocalClient?.PlayerObject?
                .GetComponent<NetworkPlayerInventory>();
            return inventory != null && inventory.ContainsHeldItem(requiredHackingPad);
        }

        private GeneratorBInteractionPoint GetInteractionPoint(
            GeneratorBInteractionType interactionType) =>
            interactionType == GeneratorBInteractionType.FuelInlet
                ? fuelInletPoint
                : controlPanelPoint;

        private void TryAutoAssignInteractionPoints()
        {
            GeneratorBInteractionPoint[] points =
                GetComponentsInChildren<GeneratorBInteractionPoint>(true);
            foreach (GeneratorBInteractionPoint point in points)
            {
                if (point == null)
                    continue;
                if (point.InteractionType == GeneratorBInteractionType.ControlPanel &&
                    controlPanelPoint == null)
                    controlPanelPoint = point;
                else if (point.InteractionType == GeneratorBInteractionType.FuelInlet &&
                         fuelInletPoint == null)
                    fuelInletPoint = point;
            }
        }

        private bool IsPowerStateValid() =>
            powerController != null &&
            powerController.CurrentState == B1FPowerState.EmergencyPower;

        private bool TryGetPlayer(ulong clientId, out NetworkObject playerObject)
        {
            playerObject = null;
            return NetworkManager != null &&
                   NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                   (playerObject = client.PlayerObject) != null;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (controllingClient.Value == clientId)
                controllingClient.Value = NoController;
        }

        private static ClientRpcParams TargetClient(ulong clientId) => new()
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        [ClientRpc]
        private void BeginLocalSessionClientRpc(
            GeneratorBSessionMode mode,
            ClientRpcParams clientRpc = default)
        {
            NetworkManager manager = NetworkManager.Singleton;
            PlayerInteraction player = manager?.LocalClient?.PlayerObject?
                .GetComponentInChildren<PlayerInteraction>(true);
            Camera sessionCamera = mode == GeneratorBSessionMode.Fuel
                ? fuelInteractionCamera
                : controlInteractionCamera;
            if (player == null || sessionCamera == null)
                return;

            GeneratorBLocalSession session = GetComponent<GeneratorBLocalSession>();
            if (session == null)
                session = gameObject.AddComponent<GeneratorBLocalSession>();
            session.Begin(this, player, sessionCamera, mode);
        }

        [ClientRpc]
        private void ResolveSearchCommandClientRpc(
            bool accepted,
            ClientRpcParams clientRpc = default)
        {
            GeneratorBLocalSession.Active?.ResolveSearchCommand(this, accepted);
        }

        [ClientRpc]
        private void PlayFuelSignalClientRpc() => fuelCan?.PlaySignal();

        [ClientRpc]
        private void StopFuelSignalClientRpc() => fuelCan?.StopSignal();

        [ClientRpc]
        private void PlayPourResultClientRpc(bool success)
        {
            if (generatorAudioSource != null)
            {
                AudioClip clip = success ? pourSuccessClip : pourFailureClip;
                if (clip != null)
                    generatorAudioSource.PlayOneShot(clip);
            }

            if (success && GeneratorBLocalSession.Active != null &&
                GeneratorBLocalSession.Active.IsFor(this))
                PlayLocalPourAnimation();
        }

        [ClientRpc]
        private void PlayGeneratorStartedClientRpc()
        {
            StartLocalGeneratorAudio(true);
        }

        private void StartLocalGeneratorAudio(bool playStartup)
        {
            if (generatorAudioSource == null)
                return;
            if (generatorAudioRoutine != null)
                StopCoroutine(generatorAudioRoutine);
            generatorAudioRoutine = StartCoroutine(
                PlayGeneratorAudioSequence(playStartup));
        }

        private IEnumerator PlayGeneratorAudioSequence(bool playStartup)
        {
            generatorAudioSource.Stop();
            generatorAudioSource.loop = false;

            if (playStartup && generatorStartedClip != null)
            {
                generatorAudioSource.clip = generatorStartedClip;
                generatorAudioSource.volume = 1f;
                generatorAudioSource.Play();
                yield return new WaitForSecondsRealtime(generatorStartedClip.length);
            }

            if (generatorRunningLoopClip != null)
            {
                generatorAudioSource.clip = generatorRunningLoopClip;
                generatorAudioSource.volume = generatorRunningLoopVolume;
                generatorAudioSource.loop = true;
                generatorAudioSource.Play();
            }

            generatorAudioRoutine = null;
        }

        private void PlayLocalPourAnimation()
        {
            if (pourVisual == null)
                return;
            if (pourRoutine != null)
                StopCoroutine(pourRoutine);
            pourRoutine = StartCoroutine(AnimatePour());
        }

        private IEnumerator AnimatePour()
        {
            Quaternion start = pourVisual.localRotation;
            Quaternion tilted = start * Quaternion.Euler(pourTiltEuler);
            yield return RotatePour(start, tilted, pourTiltDuration);
            yield return new WaitForSecondsRealtime(pourHoldDuration);
            yield return RotatePour(tilted, start, pourTiltDuration);
            pourRoutine = null;
        }

        private IEnumerator RotatePour(Quaternion from, Quaternion to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                pourVisual.localRotation = Quaternion.Slerp(from, to, elapsed / duration);
                yield return null;
            }
            pourVisual.localRotation = to;
        }
    }
}
