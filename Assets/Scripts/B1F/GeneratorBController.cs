using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DeFrag.B1F
{
    public enum GeneratorBSessionMode : byte
    {
        Search,
        Fuel,
        Pressure
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
        [Header("Quest Fuel Spawn")]
        [SerializeField] private string requiredQuestId = "b1f_full_power";
        [SerializeField] private GeneratorFuelCan fuelPrefab;
        [SerializeField] private Transform[] fuelSpawnPoints = Array.Empty<Transform>();
        private readonly NetworkList<ulong> spawnedFuelIds = new();
        private bool fuelSpawned;
        private bool spawnWarningShown;
        [SerializeField] private GeneratorBInteractionPoint controlPanelPoint;
        [SerializeField] private GeneratorBInteractionPoint fuelInletPoint;
        [SerializeField] private Camera controlInteractionCamera;
        [SerializeField] private Camera fuelInteractionCamera;
        [SerializeField, Min(0.5f)] private float maximumInteractionDistance = 5f;

        [Header("Search")]
        [SerializeField, Min(1f)] private float signalInterval = 6f;

        [Header("Cooperative Pressure")]
        [SerializeField, Range(1, 3)] private int requiredFuelCans = 2;
        [SerializeField, Min(5f)] private float fillDuration = 18f;
        [SerializeField, Range(0.05f, 0.5f)] private float minimumPressure = 0.25f;
        [SerializeField, Range(0.55f, 0.95f)] private float dangerPressure = 0.8f;
        [SerializeField, Min(0.01f)] private float pressureRisePerSecond = 0.12f;
        [SerializeField, Min(0.01f)] private float ventPerSecond = 0.22f;
        [SerializeField, Min(0.5f)] private float overpressureGrace = 2f;
        [SerializeField, Min(0.5f)] private float alarmCooldown = 2f;
        private readonly NetworkVariable<ulong> fuelOperator = new(NoController);
        private readonly NetworkVariable<float> pressure = new(0.45f);
        private readonly NetworkVariable<float> fuelProgress = new(0f);
        private readonly NetworkVariable<byte> consumedFuelCans = new(0);
        private readonly NetworkVariable<float> dangerTime = new(0f);
        private readonly NetworkVariable<bool> pouring = new(false);
        private readonly NetworkVariable<bool> venting = new(false);
        private readonly NetworkVariable<double> resumeAt = new(0d);
        private bool pourRequested, ventRequested;
        private double fuelHeartbeat, panelHeartbeat;
        public float Pressure => pressure.Value;
        public float MinimumPressure => minimumPressure;
        public float DangerPressure => dangerPressure;
        public float DangerSecondsLeft => Mathf.Max(0f, overpressureGrace - dangerTime.Value);
        public bool IsPouring => pouring.Value;
        public bool IsVenting => venting.Value;
        public bool IsCoolingDown => ServerTime < resumeAt.Value;
        public bool BothOperatorsPresent => controllingClient.Value != NoController && fuelOperator.Value != NoController;
        public bool OwnsSession(ulong id, GeneratorBSessionMode mode) =>
            mode == GeneratorBSessionMode.Fuel ? fuelOperator.Value == id : controllingClient.Value == id;

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

        [Header("Quest Signal")]
        [SerializeField] private string completionQuestSignal = QuestSignals.B1FGeneratorBCompleted;
        [SerializeField] private string completionQuestSourceId = "GENERATOR_B";

        private readonly NetworkVariable<bool> searchActive = new(
            false, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> controllingClient = new(
            NoController, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> completed = new(
            false, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private double nextSignalAt;
        private Coroutine pourRoutine;
        private Coroutine generatorAudioRoutine;
        private Quaternion pourRestRotation;
        private bool pourVisualInitialized;
        private bool localPourVisualState;

        public bool SearchActive => searchActive.Value;
        public bool IsComplete => completed.Value;
        public float FuelRatio => fuelProgress.Value;
        public int FuelPercent => Mathf.FloorToInt(FuelRatio * 100f);
        public int ConsumedFuelCans => consumedFuelCans.Value;
        public int RequiredFuelCans => requiredFuelCans;
        public double ServerTime => NetworkManager != null && NetworkManager.IsListening
            ? NetworkManager.ServerTime.Time
            : Time.unscaledTimeAsDouble;
        public GeneratorFuelCan FuelCan => GetNearestWorldFuel(transform.position);
        public bool FuelSignalVisible => searchActive.Value && !completed.Value &&
                                         FuelCan != null && IsPowerStateValid();

        private GeneratorFuelCan ResolveFuel(ulong id)
        {
            return NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var item)
                ? item.GetComponent<GeneratorFuelCan>() : null;
        }

        public GeneratorFuelCan GetNearestWorldFuel(Vector3 position)
        {
            GeneratorFuelCan nearest = null;
            float best = float.PositiveInfinity;
            foreach (ulong id in spawnedFuelIds)
            {
                var candidate = ResolveFuel(id);
                if (candidate == null || candidate.WorldItem.State != NetworkItemState.World) continue;
                float distance = (candidate.SignalAnchor.position - position).sqrMagnitude;
                if (distance < best) { nearest = candidate; best = distance; }
            }
            return nearest;
        }

        private GeneratorFuelCan GetHeldFuel(ulong clientId)
        {
            foreach (ulong id in spawnedFuelIds)
            {
                var candidate = ResolveFuel(id);
                if (candidate != null && candidate.WorldItem.State == NetworkItemState.Held &&
                    candidate.WorldItem.HolderClientId == clientId) return candidate;
            }
            return null;
        }

        private void TrySpawnFuelServer()
        {
            if (fuelSpawned || !IsPowerStateValid()) return;
            var points = new System.Collections.Generic.List<Transform>();
            foreach (var point in fuelSpawnPoints)
                if (point != null && !points.Contains(point)) points.Add(point);
            if (fuelPrefab == null || points.Count < 3)
            {
                if (!spawnWarningShown) Debug.LogError("[Generator B] Assign Fuel Prefab and at least three distinct Fuel Spawn Points.", this);
                spawnWarningShown = true;
                return;
            }
            for (int i = 0; i < 3; i++)
            {
                int selected = UnityEngine.Random.Range(i, points.Count);
                (points[i], points[selected]) = (points[selected], points[i]);
                var can = Instantiate(fuelPrefab, points[i].position, points[i].rotation);
                can.WorldItem.NetworkObject.Spawn(true);
                spawnedFuelIds.Add(can.WorldItem.NetworkObjectId);
            }
            fuelSpawned = true;
        }

        public override void OnNetworkSpawn()
        {
            if (pourVisual != null)
            {
                pourRestRotation = pourVisual.localRotation;
                pourVisualInitialized = true;
            }
            LocalInstance = this;
            LocalInstanceAvailable?.Invoke(this);
            if (completed.Value)
                StartLocalGeneratorAudio(false);
            if (!IsServer)
                return;

            nextSignalAt = ServerTime;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        private void Reset() => TryAutoAssignInteractionPoints();

        private void OnValidate()
        {
            fillDuration = Mathf.Max(5f, fillDuration);
            requiredFuelCans = Mathf.Clamp(requiredFuelCans, 1, 3);
            minimumPressure = Mathf.Clamp(minimumPressure, 0.05f, 0.5f);
            dangerPressure = Mathf.Clamp(dangerPressure, minimumPressure + 0.1f, 0.95f);
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
            if (!IsServer) return;
            UpdatePressureServer();
            TrySpawnFuelServer();
            if (!searchActive.Value || completed.Value || !IsPowerStateValid()) return;
            if (ServerTime >= nextSignalAt)
            {
                PlayFuelSignalClientRpc();
                nextSignalAt = ServerTime + signalInterval;
            }
        }

        public string GetInteractionText(GeneratorBInteractionType interactionType)
        {
            if (!completed.Value && !IsPowerStateValid()) return "발전기 B // 현재 목표에서 사용할 수 없음";
            if (completed.Value)
                return "발전기 B // FULL POWER";

            if (interactionType == GeneratorBInteractionType.FuelInlet)
                return HasLocalPlayerFuel()
                    ? "발전기 B 주유구 // 비상 연료 주입 (E)"
                    : "발전기 B 주유구 // 비상 연료 필요";

            if (requiredHackingPad != null && !HasLocalRequiredHackingPad())
                return "발전기 B 제어 패널 // 해킹패드 필요";
            return searchActive.Value
                ? "발전기 B 제어 패널 // 압력 밸브 조작 (E)"
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

        public void SetCooperativeInput(GeneratorBSessionMode mode, bool held)
        {
            if (IsSpawned)
                SetCooperativeInputServerRpc(mode, held);
        }

        public void ReleaseLocalControl()
        {
            if (IsSpawned)
                ReleaseControlServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSessionServerRpc(
            GeneratorBInteractionType interactionType,
            ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            GeneratorBInteractionPoint requestedPoint = GetInteractionPoint(interactionType);
            if (completed.Value || !IsPowerStateValid() ||
                requestedPoint == null ||
                !TryGetPlayer(sender, out NetworkObject playerObject) ||
                Vector3.Distance(playerObject.transform.position, requestedPoint.transform.position) >
                maximumInteractionDistance)
                return;

            GeneratorBSessionMode mode = interactionType == GeneratorBInteractionType.FuelInlet
                ? GeneratorBSessionMode.Fuel
                : searchActive.Value ? GeneratorBSessionMode.Pressure : GeneratorBSessionMode.Search;
            if (mode == GeneratorBSessionMode.Fuel)
            {
                if (fuelOperator.Value != NoController || controllingClient.Value == sender) return;
            }
            else if (controllingClient.Value != NoController || fuelOperator.Value == sender) return;
            if (mode == GeneratorBSessionMode.Fuel && !IsFuelHeldBy(sender))
                return;
            if (mode != GeneratorBSessionMode.Fuel &&
                requiredHackingPad != null &&
                (!playerObject.TryGetComponent(out NetworkPlayerInventory inventory) ||
                 !inventory.ContainsHeldItem(requiredHackingPad)))
                return;

            if (mode == GeneratorBSessionMode.Fuel)
            { fuelOperator.Value = sender; fuelHeartbeat = ServerTime; }
            else { controllingClient.Value = sender; panelHeartbeat = ServerTime; }

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
            if (sender != controllingClient.Value || completed.Value || !IsPowerStateValid())
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

        private bool ConsumeCurrentFuelCanServer()
        {
            ulong operatorId = fuelOperator.Value;
            var can = GetHeldFuel(operatorId);
            if (can == null || !TryGetPlayer(operatorId, out var player) ||
                !player.TryGetComponent<NetworkPlayerInventory>(out var inventory) ||
                !inventory.TryConsumeHeldItemServer(can.WorldItem.NetworkObjectId)) return false;
            consumedFuelCans.Value++;
            fuelProgress.Value = Mathf.Clamp01(
                consumedFuelCans.Value / (float)requiredFuelCans);
            pourRequested = ventRequested = false;
            pouring.Value = venting.Value = false;
            fuelOperator.Value = NoController;
            PlayPourResultClientRpc(true);
            FuelCanConsumedClientRpc(
                consumedFuelCans.Value,
                requiredFuelCans,
                TargetClient(operatorId));
            return true;
        }

        private void CompleteFuelServer()
        {
            fuelProgress.Value = 1f;
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
        private void SetCooperativeInputServerRpc(
            GeneratorBSessionMode mode,
            bool held,
            ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (completed.Value || !IsPowerStateValid()) return;
            if (mode == GeneratorBSessionMode.Fuel && fuelOperator.Value == sender)
            {
                pourRequested = held;
                fuelHeartbeat = ServerTime;
            }
            else if (mode == GeneratorBSessionMode.Pressure && controllingClient.Value == sender)
            {
                ventRequested = held;
                panelHeartbeat = ServerTime;
            }
        }

        private void UpdatePressureServer()
        {
            if (completed.Value || !IsPowerStateValid()) return;

            if (searchActive.Value)
            {
                if (fuelOperator.Value != NoController && ServerTime - fuelHeartbeat > 3d)
                {
                    fuelOperator.Value = NoController;
                    pourRequested = false;
                }
                if (controllingClient.Value != NoController && ServerTime - panelHeartbeat > 3d)
                {
                    controllingClient.Value = NoController;
                    ventRequested = false;
                }
            }

            bool fuelAlive = fuelOperator.Value != NoController &&
                             ServerTime - fuelHeartbeat < 0.75d &&
                             IsFuelHeldBy(fuelOperator.Value);
            bool panelAlive = controllingClient.Value != NoController &&
                              ServerTime - panelHeartbeat < 0.75d;
            bool ready = fuelAlive && panelAlive && ServerTime >= resumeAt.Value;
            bool activePour = ready && pourRequested;
            bool activeVent = ready && ventRequested;
            pouring.Value = activePour;
            venting.Value = activeVent;

            float delta = Time.deltaTime;
            float change = activePour ? pressureRisePerSecond : -pressureRisePerSecond * 0.12f;
            if (activeVent) change -= ventPerSecond;
            pressure.Value = Mathf.Clamp01(pressure.Value + change * delta);

            bool productive = activePour && pressure.Value >= minimumPressure &&
                              pressure.Value < dangerPressure;
            if (productive)
            {
                float nextCanThreshold = Mathf.Clamp01(
                    (consumedFuelCans.Value + 1f) / requiredFuelCans);
                fuelProgress.Value = Mathf.Min(
                    nextCanThreshold,
                    fuelProgress.Value + delta / fillDuration);
            }

            if (activePour && pressure.Value >= dangerPressure)
                dangerTime.Value += delta;
            else
                dangerTime.Value = Mathf.Max(0f, dangerTime.Value - delta * 1.5f);

            if (dangerTime.Value >= overpressureGrace)
            {
                dangerTime.Value = 0f;
                pressure.Value = Mathf.Max(minimumPressure, dangerPressure - 0.18f);
                resumeAt.Value = ServerTime + alarmCooldown;
                pourRequested = false;
                pouring.Value = false;
                Vector3 position = fuelInletPoint != null
                    ? fuelInletPoint.transform.position : transform.position;
                WorldNoiseSystem.Emit(position, failedPourNoiseRadius);
                PlayPourResultClientRpc(false);
            }

            float threshold = Mathf.Clamp01(
                (consumedFuelCans.Value + 1f) / requiredFuelCans);
            if (fuelOperator.Value != NoController && fuelProgress.Value >= threshold &&
                ConsumeCurrentFuelCanServer() && consumedFuelCans.Value >= requiredFuelCans)
                CompleteFuelServer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReleaseControlServerRpc(ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (controllingClient.Value == sender)
            {
                controllingClient.Value = NoController;
                ventRequested = false;
            }
            if (fuelOperator.Value == sender)
            {
                fuelOperator.Value = NoController;
                pourRequested = false;
            }
        }

        private bool IsFuelHeldBy(ulong clientId) =>
            GetHeldFuel(clientId) != null;

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
            QuestManager.Instance != null && QuestManager.Instance.IsQuestActive(requiredQuestId) &&
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
            if (fuelOperator.Value == clientId)
                fuelOperator.Value = NoController;
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
        private void FuelCanConsumedClientRpc(
            int consumed,
            int required,
            ClientRpcParams clientRpc = default)
        {
            GeneratorBLocalSession.Active?.ResolveFuelCanConsumed(this, consumed, required);
        }

        [ClientRpc]
        private void PlayFuelSignalClientRpc()
        {
            foreach (ulong id in spawnedFuelIds)
            {
                var can = ResolveFuel(id);
                if (can != null && can.WorldItem.State == NetworkItemState.World) can.PlaySignal();
            }
        }

        [ClientRpc]
        private void StopFuelSignalClientRpc()
        {
            foreach (ulong id in spawnedFuelIds) ResolveFuel(id)?.StopSignal();
        }

        [ClientRpc]
        private void PlayPourResultClientRpc(bool success)
        {
            if (generatorAudioSource != null)
            {
                AudioClip clip = success ? pourSuccessClip : pourFailureClip;
                if (clip != null)
                    generatorAudioSource.PlayOneShot(clip);
            }

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

        public void SetLocalPourPresentation(bool active)
        {
            if (pourVisual == null || localPourVisualState == active) return;
            if (!pourVisualInitialized)
            {
                pourRestRotation = pourVisual.localRotation;
                pourVisualInitialized = true;
            }
            localPourVisualState = active;
            if (pourRoutine != null) StopCoroutine(pourRoutine);
            Quaternion target = active
                ? pourRestRotation * Quaternion.Euler(pourTiltEuler)
                : pourRestRotation;
            pourRoutine = StartCoroutine(RotatePour(
                pourVisual.localRotation, target, pourTiltDuration));
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
