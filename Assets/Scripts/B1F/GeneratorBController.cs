using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace DeFrag.B1F
{
    public enum GeneratorBSessionMode : byte
    {
        Panel,
        Crank
    }

    public enum GeneratorBRejectReason : byte
    {
        PanelOccupied,
        CrankOccupied,
        HackingPadRequired,
        FuelRequired,
        AlreadyOperating
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class GeneratorBController : NetworkBehaviour
    {
        public const ulong NoController = ulong.MaxValue;

        public static GeneratorBController LocalInstance { get; private set; }
        public static event Action<GeneratorBController> LocalInstanceAvailable;

        [Header("Gameplay References")]
        [SerializeField] private B1FPowerController powerController;
        [Tooltip("Optional. When assigned, only the player holding this item may use the control panel.")]
        [SerializeField] private ItemData requiredHackingPad;
        [SerializeField] private GeneratorBInteractionPoint controlPanelPoint;
        [SerializeField] private GeneratorBInteractionPoint fuelInletPoint;
        [SerializeField] private Camera controlInteractionCamera;
        [SerializeField] private Camera fuelInteractionCamera;
        [SerializeField, Min(0.5f)] private float maximumInteractionDistance = 5f;
        [SerializeField, Min(0.5f)] private float operatorTimeout = 3f;

        [Header("Quest Fuel Spawn")]
        [SerializeField] private string requiredQuestId = "b1f_full_power";
        [SerializeField] private GeneratorFuelCan fuelPrefab;
        [SerializeField] private Transform[] fuelSpawnPoints = Array.Empty<Transform>();
        [SerializeField, Min(1f)] private float signalInterval = 6f;
        [SerializeField, Range(1, 3)] private int requiredFuelCans = 2;

        [Header("Cold Start")]
        [SerializeField] private GeneratorBCrankSettings crankSettings = GeneratorBCrankSettings.Default;
        [Tooltip("역화 소음이 몬스터에게 들리는 반경입니다.")]
        [SerializeField, Min(0f)] private float backfireNoiseRadius = 30f;
        [SerializeField, Min(0.1f)] private float maximumIgnitionInputAge = 0.5f;

        [Header("Facility Radar")]
        [SerializeField] private FacilityRadarSettings radarSettings = FacilityRadarSettings.Default;
        [Tooltip("미끼 소음이 몬스터에게 들리는 반경입니다.")]
        [SerializeField, Min(1f)] private float decoyNoiseRadius = 60f;
        [SerializeField, Min(1f)] private float decoyRechargeSeconds = 40f;
        [Tooltip("레이더가 연료 위치를 뭉개서 보여주는 반경입니다. 가까이 가면 정확한 위치가 드러납니다.")]
        [SerializeField, Min(1f)] private float fuelZoneRadius = 8f;
        [SerializeField, Min(1f)] private float fuelRevealDistance = 12f;

        [Header("Presentation")]
        [Tooltip("런타임 UI 폰트입니다. 한글 글리프가 있는 터미널 폰트를 지정하세요.")]
        [SerializeField] private TMP_FontAsset uiFont;
        [Tooltip("연료 주입 완료부터 시동 성공까지 재생되는 긴장 BGM입니다.")]
        [SerializeField] private AudioClip coldStartMusic;
        [SerializeField, Range(0f, 1f)] private float coldStartMusicVolume = 0.55f;
        [Tooltip("점화 바늘과 함께 도는 발전기 플라이휠(선택).")]
        [SerializeField] private Transform flywheelVisual;
        [SerializeField] private Vector3 flywheelLocalAxis = Vector3.right;
        [SerializeField, Range(0f, 1f)] private float crankEngineVolume = 0.8f;

        [Header("Generator Audio")]
        [SerializeField] private AudioSource generatorAudioSource;
        [SerializeField] private AudioClip pourSuccessClip;
        [Tooltip("역화 순간 절차 생성 폭발음 위에 겹쳐 재생됩니다.")]
        [SerializeField] private AudioClip pourFailureClip;
        [SerializeField] private AudioClip generatorStartedClip;
        [SerializeField] private AudioClip generatorRunningLoopClip;
        [SerializeField, Range(0f, 1f)] private float generatorRunningLoopVolume = 1f;

        [Header("Local Pour Presentation")]
        [SerializeField] private Transform pourVisual;
        [SerializeField] private Vector3 pourTiltEuler = new(0f, 0f, 72f);
        [SerializeField, Min(0.05f)] private float pourTiltDuration = 0.35f;
        [SerializeField, Min(0.1f)] private float pourHoldDuration = 1.2f;

        [Header("Quest Signal")]
        [SerializeField] private string completionQuestSignal = QuestSignals.B1FGeneratorBCompleted;
        [SerializeField] private string completionQuestSourceId = "GENERATOR_B";

        private readonly NetworkList<ulong> spawnedFuelIds = new();
        private readonly NetworkVariable<bool> searchActive = new();
        private readonly NetworkVariable<bool> completed = new();
        private readonly NetworkVariable<ulong> panelOperator = new(NoController);
        private readonly NetworkVariable<ulong> crankOperator = new(NoController);
        private readonly NetworkVariable<byte> consumedFuelCans = new();
        private readonly NetworkVariable<float> rpm = new();
        private readonly NetworkVariable<float> crankAngle = new();
        private readonly NetworkVariable<byte> ignitedCylinders = new();
        private readonly NetworkVariable<byte> backfireCount = new();
        private readonly NetworkVariable<double> ignitionReadyAt = new();
        private readonly NetworkVariable<double> decoyReadyAt = new();

        private GeneratorBCrankSimulation crank;
        private bool fuelSpawned;
        private bool spawnWarningShown;
        private double nextSignalAt;
        private double panelHeartbeat;
        private double crankHeartbeat;

        private AudioSource engineTurnSource;
        private AudioSource musicSource;
        private Coroutine generatorAudioRoutine;
        private Coroutine fullPowerRoutine;
        private Coroutine pourRoutine;
        private Coroutine musicFadeRoutine;
        private Quaternion pourRestRotation;
        private bool pourVisualInitialized;
        private Quaternion flywheelRestRotation;
        private float smoothedAngle;

        public event Action<Vector3> DecoyFired;
        public event Action<GeneratorBIgnitionResult> IgnitionResolved;

        public bool SearchActive => searchActive.Value;
        public bool IsComplete => completed.Value;
        public bool IsPrimed => consumedFuelCans.Value >= requiredFuelCans;
        public int ConsumedFuelCans => consumedFuelCans.Value;
        public int RequiredFuelCans => requiredFuelCans;
        public float Rpm => rpm.Value;
        public float ServerAngle => crankAngle.Value;
        public float SmoothedAngle => smoothedAngle;
        public int IgnitedCylinders => ignitedCylinders.Value;
        public int RequiredCylinders => Mathf.Max(1, crankSettings.cylinderBands?.Length ?? 1);
        public float SectorDegrees => CrankModel.GetSectorDegrees(backfireCount.Value);
        public Vector2 CurrentBand => CrankModel.GetBand(ignitedCylinders.Value);
        public bool IgnitionCoolingDown => ServerTime < ignitionReadyAt.Value;
        public float DecoyCooldownRemaining => Mathf.Max(0f, (float)(decoyReadyAt.Value - ServerTime));
        public bool BothOperatorsPresent => panelOperator.Value != NoController && crankOperator.Value != NoController;
        public bool CrankOperatorPresent => crankOperator.Value != NoController;
        public TMP_FontAsset UiFont => uiFont;
        public FacilityRadarSettings RadarSettings => radarSettings;
        public float FuelZoneRadius => fuelZoneRadius;
        public float FuelRevealDistance => fuelRevealDistance;
        public float DegreesPerSecondAtFullRpm => crankSettings.degreesPerSecondAtFullRpm;
        public Vector3 GeneratorPosition => fuelInletPoint != null ? fuelInletPoint.transform.position : transform.position;
        public double ServerTime => NetworkManager != null && NetworkManager.IsListening
            ? NetworkManager.ServerTime.Time
            : Time.unscaledTimeAsDouble;
        public GeneratorFuelCan FuelCan => GetNearestWorldFuel(transform.position);
        public bool FuelSignalVisible => searchActive.Value && !completed.Value && !IsPrimed &&
                                         FuelCan != null && IsPowerStateValid();

        private GeneratorBCrankSimulation CrankModel => crank ??= new GeneratorBCrankSimulation(crankSettings);

        public bool OwnsSession(ulong clientId, GeneratorBSessionMode mode) =>
            mode == GeneratorBSessionMode.Crank ? crankOperator.Value == clientId : panelOperator.Value == clientId;

        public IEnumerable<GeneratorFuelCan> WorldFuelCans()
        {
            foreach (ulong id in spawnedFuelIds)
            {
                GeneratorFuelCan candidate = ResolveFuel(id);
                if (candidate != null && candidate.WorldItem.State == NetworkItemState.World)
                    yield return candidate;
            }
        }

        public GeneratorFuelCan GetNearestWorldFuel(Vector3 position)
        {
            GeneratorFuelCan nearest = null;
            float best = float.PositiveInfinity;
            foreach (GeneratorFuelCan candidate in WorldFuelCans())
            {
                float distance = (candidate.SignalAnchor.position - position).sqrMagnitude;
                if (distance < best) { nearest = candidate; best = distance; }
            }
            return nearest;
        }

        public override void OnNetworkSpawn()
        {
            if (pourVisual != null)
            {
                pourRestRotation = pourVisual.localRotation;
                pourVisualInitialized = true;
            }
            if (flywheelVisual != null)
                flywheelRestRotation = flywheelVisual.localRotation;

            crank = new GeneratorBCrankSimulation(crankSettings);
            consumedFuelCans.OnValueChanged += OnFuelCountChanged;
            completed.OnValueChanged += OnCompletedChanged;
            LocalInstance = this;
            LocalInstanceAvailable?.Invoke(this);

            if (completed.Value)
                StartLocalGeneratorAudio(false);
            else if (IsPrimed)
                SetColdStartMusic(true);

            if (!IsServer)
                return;

            nextSignalAt = ServerTime;
            decoyReadyAt.Value = 0d;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            consumedFuelCans.OnValueChanged -= OnFuelCountChanged;
            completed.OnValueChanged -= OnCompletedChanged;
            StopRoutine(ref generatorAudioRoutine);
            StopRoutine(ref fullPowerRoutine);
            StopRoutine(ref musicFadeRoutine);
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            if (LocalInstance == this)
            {
                LocalInstance = null;
                LocalInstanceAvailable?.Invoke(null);
            }
        }

        private void Reset() => TryAutoAssignInteractionPoints();

        private void OnValidate()
        {
            requiredFuelCans = Mathf.Clamp(requiredFuelCans, 1, 3);
            TryAutoAssignInteractionPoints();
            if (crank != null)
                crank.Settings = crankSettings;
        }

        private void Update()
        {
            if (IsSpawned)
                UpdateLocalCrankPresentation();

            if (!IsServer || !IsSpawned)
                return;

            TrySpawnFuelServer();
            ReleaseStaleOperatorsServer();
            if (completed.Value || !IsPowerStateValid())
                return;

            if (searchActive.Value && !IsPrimed && ServerTime >= nextSignalAt)
            {
                PlayFuelSignalClientRpc();
                nextSignalAt = ServerTime + signalInterval;
            }

            if (IsPrimed)
                SimulateCrankServer();
        }

        public string GetInteractionText(GeneratorBInteractionType interactionType)
        {
            if (completed.Value) return "발전기 B // 가동 중";
            if (!IsPowerStateValid()) return "발전기 B // 현재 목표에서 사용할 수 없음";

            if (interactionType == GeneratorBInteractionType.FuelInlet)
            {
                if (IsPrimed) return "발전기 B 시동 크랭크 // 돌리기 (E)";
                string count = $"[{consumedFuelCans.Value}/{requiredFuelCans}]";
                return HasLocalPlayerFuel()
                    ? $"발전기 B 주유구 // 연료 붓기 (E)  {count}"
                    : $"발전기 B 주유구 // 연료통이 필요합니다  {count}";
            }

            if (requiredHackingPad != null && !HasLocalRequiredHackingPad())
                return "발전기 B 제어 패널 // 해킹패드 필요";
            return IsPrimed
                ? "발전기 B 제어 패널 // 점화 제어 (E)"
                : "발전기 B 제어 패널 // 시설 레이더 열기 (E)";
        }

        public void InteractAt(GeneratorBInteractionType interactionType, PlayerInteraction player)
        {
            if (player != null && !completed.Value && IsPowerStateValid() && IsSpawned)
                RequestSessionServerRpc(interactionType);
        }

        public void SendHeartbeat()
        {
            if (IsSpawned) HeartbeatServerRpc();
        }

        public void SubmitCrankStroke(int side)
        {
            if (IsSpawned) CrankStrokeServerRpc((byte)Mathf.Clamp(side, 0, 1));
        }

        public void SubmitIgnition()
        {
            if (IsSpawned) IgniteServerRpc(ServerTime);
        }

        public void SubmitDecoy(Vector3 worldPosition)
        {
            if (IsSpawned) DecoyServerRpc(worldPosition);
        }

        public void ReleaseLocalControl()
        {
            if (IsSpawned) ReleaseControlServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSessionServerRpc(GeneratorBInteractionType interactionType, ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            GeneratorBInteractionPoint point = interactionType == GeneratorBInteractionType.FuelInlet
                ? fuelInletPoint
                : controlPanelPoint;
            if (completed.Value || !IsPowerStateValid() || point == null ||
                !TryGetPlayer(sender, out NetworkObject playerObject) ||
                Vector3.Distance(playerObject.transform.position, point.transform.position) > maximumInteractionDistance)
                return;

            if (interactionType == GeneratorBInteractionType.ControlPanel)
                BeginPanelServer(sender, playerObject);
            else if (!IsPrimed)
                TryPourServer(sender);
            else
                BeginCrankServer(sender);
        }

        private void BeginPanelServer(ulong sender, NetworkObject playerObject)
        {
            if (crankOperator.Value == sender)
            {
                RejectClientRpc(GeneratorBRejectReason.AlreadyOperating, TargetClient(sender));
                return;
            }
            if (panelOperator.Value != NoController && panelOperator.Value != sender)
            {
                RejectClientRpc(GeneratorBRejectReason.PanelOccupied, TargetClient(sender));
                return;
            }
            if (requiredHackingPad != null &&
                (!playerObject.TryGetComponent(out NetworkPlayerInventory inventory) ||
                 !inventory.ContainsHeldItem(requiredHackingPad)))
            {
                RejectClientRpc(GeneratorBRejectReason.HackingPadRequired, TargetClient(sender));
                return;
            }

            panelOperator.Value = sender;
            panelHeartbeat = ServerTime;
            if (!searchActive.Value)
            {
                searchActive.Value = true;
                nextSignalAt = ServerTime;
                SearchStartedClientRpc(sender);
            }
            BeginLocalSessionClientRpc(GeneratorBSessionMode.Panel, TargetClient(sender));
        }

        private void BeginCrankServer(ulong sender)
        {
            if (panelOperator.Value == sender)
            {
                RejectClientRpc(GeneratorBRejectReason.AlreadyOperating, TargetClient(sender));
                return;
            }
            if (crankOperator.Value != NoController && crankOperator.Value != sender)
            {
                RejectClientRpc(GeneratorBRejectReason.CrankOccupied, TargetClient(sender));
                return;
            }

            crankOperator.Value = sender;
            crankHeartbeat = ServerTime;
            BeginLocalSessionClientRpc(GeneratorBSessionMode.Crank, TargetClient(sender));
        }

        private void TryPourServer(ulong sender)
        {
            GeneratorFuelCan can = GetHeldFuel(sender);
            if (can == null || !TryGetPlayer(sender, out NetworkObject player) ||
                !player.TryGetComponent(out NetworkPlayerInventory inventory) ||
                !inventory.TryConsumeHeldItemServer(can.WorldItem.NetworkObjectId))
            {
                RejectClientRpc(GeneratorBRejectReason.FuelRequired, TargetClient(sender));
                return;
            }

            consumedFuelCans.Value = (byte)Mathf.Min(requiredFuelCans, consumedFuelCans.Value + 1);
            PlayPourClientRpc();
            if (!IsPrimed)
                return;

            CrankModel.Reset();
            ignitedCylinders.Value = 0;
            backfireCount.Value = 0;
            ignitionReadyAt.Value = 0d;
            StopFuelSignalClientRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void HeartbeatServerRpc(ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (panelOperator.Value == sender) panelHeartbeat = ServerTime;
            if (crankOperator.Value == sender) crankHeartbeat = ServerTime;
        }

        [ServerRpc(RequireOwnership = false)]
        private void CrankStrokeServerRpc(byte side, ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != crankOperator.Value || !IsPrimed || completed.Value)
                return;
            crankHeartbeat = ServerTime;
            CrankModel.ApplyStroke(side, ServerTime);
        }

        [ServerRpc(RequireOwnership = false)]
        private void IgniteServerRpc(double observedServerTime, ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (sender != panelOperator.Value || !IsPrimed || completed.Value || !IsPowerStateValid() ||
                IgnitionCoolingDown)
                return;

            panelHeartbeat = ServerTime;
            double age = ServerTime - observedServerTime;
            if (age < -0.1d || age > maximumIgnitionInputAge)
                observedServerTime = ServerTime;

            GeneratorBIgnitionResult result = CrankModel.Evaluate(
                ignitedCylinders.Value, backfireCount.Value, observedServerTime);
            switch (result)
            {
                case GeneratorBIgnitionResult.Hit:
                    CrankModel.ApplyHit();
                    ignitedCylinders.Value++;
                    ignitionReadyAt.Value = ServerTime + crankSettings.hitCooldown;
                    IgnitionResultClientRpc(result);
                    if (ignitedCylinders.Value >= RequiredCylinders)
                        CompleteServer();
                    break;
                case GeneratorBIgnitionResult.Backfire:
                    CrankModel.ApplyBackfire();
                    backfireCount.Value = (byte)Mathf.Min(byte.MaxValue, backfireCount.Value + 1);
                    ignitionReadyAt.Value = ServerTime + crankSettings.backfireCooldown;
                    WorldNoiseSystem.Emit(GeneratorPosition, backfireNoiseRadius);
                    IgnitionResultClientRpc(result);
                    break;
                default:
                    IgnitionResultClientRpc(result, TargetClient(sender));
                    break;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void DecoyServerRpc(Vector3 worldPosition, ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != panelOperator.Value || completed.Value ||
                ServerTime < decoyReadyAt.Value)
                return;

            if (UnityEngine.AI.NavMesh.SamplePosition(worldPosition, out UnityEngine.AI.NavMeshHit hit,
                    radarSettings.decoySampleRadius, UnityEngine.AI.NavMesh.AllAreas))
                worldPosition = hit.position;
            else
                return;

            decoyReadyAt.Value = ServerTime + decoyRechargeSeconds;
            WorldNoiseSystem.EmitUrgent(worldPosition, decoyNoiseRadius);
            DecoyFiredClientRpc(worldPosition);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReleaseControlServerRpc(ServerRpcParams rpc = default) =>
            ReleaseOperatorServer(rpc.Receive.SenderClientId);

        private void SimulateCrankServer()
        {
            CrankModel.Tick(Time.deltaTime, ServerTime);
            if (!Mathf.Approximately(rpm.Value, CrankModel.Rpm))
                rpm.Value = CrankModel.Rpm;
            if (!Mathf.Approximately(crankAngle.Value, CrankModel.Angle))
                crankAngle.Value = CrankModel.Angle;
        }

        private void CompleteServer()
        {
            completed.Value = true;
            searchActive.Value = false;
            panelOperator.Value = NoController;
            crankOperator.Value = NoController;
            StopFuelSignalClientRpc();
            PlayGeneratorStartedClientRpc();
            if (powerController != null && !powerController.ForceTvMonsterInvestigateServer(GeneratorPosition))
                Debug.LogWarning("[GeneratorB] TV Monster could not begin forced generator investigation.", this);
            BeginFullPowerTransitionServer();
            if (QuestManager.Instance != null && !string.IsNullOrWhiteSpace(completionQuestSignal))
                QuestManager.Instance.ReportProgress(completionQuestSignal, completionQuestSourceId);
        }

        private void BeginFullPowerTransitionServer()
        {
            if (powerController == null)
            {
                Debug.LogError("[GeneratorB] Full power controller is not assigned on the server.", this);
                return;
            }
            StopRoutine(ref fullPowerRoutine);
            fullPowerRoutine = StartCoroutine(SetFullPowerWhenReady());
        }

        private IEnumerator SetFullPowerWhenReady()
        {
            const float timeout = 8f;
            float elapsed = 0f;
            while (elapsed < timeout && powerController.CurrentState != B1FPowerState.FullPower)
            {
                if (powerController.CanRestoreGenerator)
                    powerController.SetFullPowerServer();
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (powerController.CurrentState != B1FPowerState.FullPower)
                Debug.LogError($"[GeneratorB] Failed to enter FullPower from {powerController.CurrentState}.", this);
            fullPowerRoutine = null;
        }

        private void TrySpawnFuelServer()
        {
            if (fuelSpawned || !IsPowerStateValid()) return;
            var points = new List<Transform>();
            foreach (Transform point in fuelSpawnPoints)
                if (point != null && !points.Contains(point)) points.Add(point);
            if (fuelPrefab == null || points.Count < 3)
            {
                if (!spawnWarningShown)
                    Debug.LogError("[Generator B] Assign Fuel Prefab and at least three distinct Fuel Spawn Points.", this);
                spawnWarningShown = true;
                return;
            }

            // Marked before spawning so an invalid prefab can never respawn cans every frame.
            fuelSpawned = true;
            for (int i = 0; i < 3; i++)
            {
                int selected = UnityEngine.Random.Range(i, points.Count);
                (points[i], points[selected]) = (points[selected], points[i]);
                GeneratorFuelCan can = Instantiate(fuelPrefab, points[i].position, points[i].rotation);
                if (!can.gameObject.activeSelf)
                    can.gameObject.SetActive(true);

                NetworkObject fuelNetworkObject = can.WorldItem.NetworkObject;
                if (fuelNetworkObject == null)
                {
                    Debug.LogError("[Generator B] Fuel prefab requires a NetworkObject.", can);
                    Destroy(can.gameObject);
                    continue;
                }

                fuelNetworkObject.Spawn(true);
                spawnedFuelIds.Add(fuelNetworkObject.NetworkObjectId);
            }
        }

        private void ReleaseStaleOperatorsServer()
        {
            double now = ServerTime;
            if (panelOperator.Value != NoController && now - panelHeartbeat > operatorTimeout)
                panelOperator.Value = NoController;
            if (crankOperator.Value != NoController && now - crankHeartbeat > operatorTimeout)
                crankOperator.Value = NoController;
        }

        private void ReleaseOperatorServer(ulong clientId)
        {
            if (panelOperator.Value == clientId) panelOperator.Value = NoController;
            if (crankOperator.Value == clientId) crankOperator.Value = NoController;
        }

        private void OnClientDisconnected(ulong clientId) => ReleaseOperatorServer(clientId);

        private GeneratorFuelCan ResolveFuel(ulong id) =>
            NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject item)
                ? item.GetComponent<GeneratorFuelCan>()
                : null;

        private GeneratorFuelCan GetHeldFuel(ulong clientId)
        {
            foreach (ulong id in spawnedFuelIds)
            {
                GeneratorFuelCan candidate = ResolveFuel(id);
                if (candidate != null && candidate.WorldItem.State == NetworkItemState.Held &&
                    candidate.WorldItem.HolderClientId == clientId)
                    return candidate;
            }
            return null;
        }

        private bool HasLocalPlayerFuel()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && GetHeldFuel(manager.LocalClientId) != null;
        }

        private bool HasLocalRequiredHackingPad()
        {
            if (requiredHackingPad == null)
                return true;
            NetworkPlayerInventory inventory = NetworkManager.Singleton?.LocalClient?.PlayerObject?
                .GetComponent<NetworkPlayerInventory>();
            return inventory != null && inventory.ContainsHeldItem(requiredHackingPad);
        }

        private void TryAutoAssignInteractionPoints()
        {
            foreach (GeneratorBInteractionPoint point in GetComponentsInChildren<GeneratorBInteractionPoint>(true))
            {
                if (point == null) continue;
                if (point.InteractionType == GeneratorBInteractionType.ControlPanel && controlPanelPoint == null)
                    controlPanelPoint = point;
                else if (point.InteractionType == GeneratorBInteractionType.FuelInlet && fuelInletPoint == null)
                    fuelInletPoint = point;
            }
        }

        private bool IsPowerStateValid() =>
            QuestManager.Instance != null && QuestManager.Instance.IsQuestActive(requiredQuestId) &&
            powerController != null &&
            (powerController.CurrentState == B1FPowerState.EmergencyPower || powerController.CanRestoreGenerator);

        private bool TryGetPlayer(ulong clientId, out NetworkObject playerObject)
        {
            playerObject = null;
            return NetworkManager != null &&
                   NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                   (playerObject = client.PlayerObject) != null;
        }

        private static ClientRpcParams TargetClient(ulong clientId) => new()
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        [ClientRpc]
        private void BeginLocalSessionClientRpc(GeneratorBSessionMode mode, ClientRpcParams clientRpc = default)
        {
            PlayerInteraction player = NetworkManager.Singleton?.LocalClient?.PlayerObject?
                .GetComponentInChildren<PlayerInteraction>(true);
            Camera sessionCamera = mode == GeneratorBSessionMode.Crank ? fuelInteractionCamera : controlInteractionCamera;
            if (player == null || sessionCamera == null)
            {
                ReleaseLocalControl();
                return;
            }

            GeneratorBLocalSession session = GetComponent<GeneratorBLocalSession>();
            if (session == null)
                session = gameObject.AddComponent<GeneratorBLocalSession>();
            session.Begin(this, player, sessionCamera, mode);
        }

        [ClientRpc]
        private void RejectClientRpc(GeneratorBRejectReason reason, ClientRpcParams clientRpc = default)
        {
            string message = reason switch
            {
                GeneratorBRejectReason.PanelOccupied => "동료가 제어 패널을 사용 중입니다",
                GeneratorBRejectReason.CrankOccupied => "동료가 크랭크를 돌리는 중입니다",
                GeneratorBRejectReason.HackingPadRequired => "제어 패널은 해킹패드를 든 채로 사용해야 합니다",
                GeneratorBRejectReason.FuelRequired => "연료통을 손에 들고 주유구를 사용하세요",
                _ => "이미 발전기의 다른 위치를 조작 중입니다"
            };
            GeneratorBToast.Show(message, GeneratorBToast.Warning, 3f, uiFont);
        }

        [ClientRpc]
        private void SearchStartedClientRpc(ulong radarOperator)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId == radarOperator)
                return;
            GeneratorBToast.Show(
                "해커가 시설 레이더를 켰습니다!\n카메라(C) → IR 모드(우클릭)로 연료 신호를 추적하세요.\n무전으로 해커의 길 안내를 들으세요.",
                GeneratorBToast.Info, 7f, uiFont);
        }

        [ClientRpc]
        private void PlayPourClientRpc()
        {
            if (generatorAudioSource != null && pourSuccessClip != null)
                generatorAudioSource.PlayOneShot(pourSuccessClip);
            if (pourVisual != null)
            {
                StopRoutine(ref pourRoutine);
                pourRoutine = StartCoroutine(PourPresentation());
            }
        }

        [ClientRpc]
        private void IgnitionResultClientRpc(GeneratorBIgnitionResult result, ClientRpcParams clientRpc = default)
        {
            if (result == GeneratorBIgnitionResult.Backfire)
            {
                AudioSource.PlayClipAtPoint(ProceduralSfx.Backfire, GeneratorPosition, 1f);
                if (generatorAudioSource != null && pourFailureClip != null)
                    generatorAudioSource.PlayOneShot(pourFailureClip);
            }
            else if (result == GeneratorBIgnitionResult.Hit)
            {
                AudioSource.PlayClipAtPoint(ProceduralSfx.IgnitionCatch, GeneratorPosition, 1f);
            }
            IgnitionResolved?.Invoke(result);
        }

        [ClientRpc]
        private void DecoyFiredClientRpc(Vector3 worldPosition)
        {
            AudioSource.PlayClipAtPoint(ProceduralSfx.DecoyBurst, worldPosition, 1f);
            DecoyFired?.Invoke(worldPosition);
        }

        [ClientRpc]
        private void PlayFuelSignalClientRpc()
        {
            foreach (GeneratorFuelCan can in WorldFuelCans())
                can.PlaySignal();
        }

        [ClientRpc]
        private void StopFuelSignalClientRpc()
        {
            foreach (ulong id in spawnedFuelIds)
                ResolveFuel(id)?.StopSignal();
        }

        [ClientRpc]
        private void PlayGeneratorStartedClientRpc() => StartLocalGeneratorAudio(true);

        private void OnFuelCountChanged(byte previous, byte next)
        {
            if (next <= previous)
                return;
            if (next >= requiredFuelCans)
            {
                GeneratorBToast.Show(
                    "연료 주입 완료! 이제 시동을 겁니다.\n한 명은 주유구 크랭크(A·D 연타), 한 명은 제어 패널 점화(SPACE)!",
                    GeneratorBToast.Info, 7f, uiFont);
                SetColdStartMusic(true);
            }
            else
            {
                GeneratorBToast.Show($"연료 {next}/{requiredFuelCans} 주입 — 연료통이 더 필요합니다",
                    GeneratorBToast.Info, 4f, uiFont);
            }
        }

        private void OnCompletedChanged(bool previous, bool next)
        {
            if (!next) return;
            SetColdStartMusic(false);
            if (engineTurnSource != null)
                engineTurnSource.Stop();
            GeneratorBToast.Show("발전기 가동!\n괴물이 소리를 듣고 달려옵니다 — 도망치세요!", GeneratorBToast.Warning, 5f, uiFont);
        }

        private void UpdateLocalCrankPresentation()
        {
            bool cranking = IsPrimed && !completed.Value;
            float rpmValue = cranking ? rpm.Value : 0f;

            // Integrate locally for smooth motion and pull toward the replicated server angle.
            smoothedAngle = Mathf.Repeat(smoothedAngle + rpmValue * crankSettings.degreesPerSecondAtFullRpm * Time.deltaTime, 360f);
            smoothedAngle = Mathf.Repeat(
                smoothedAngle + Mathf.DeltaAngle(smoothedAngle, crankAngle.Value) * Mathf.Clamp01(Time.deltaTime * 8f), 360f);

            if (flywheelVisual != null)
                flywheelVisual.localRotation = flywheelRestRotation * Quaternion.AngleAxis(smoothedAngle, flywheelLocalAxis);

            if (!cranking)
                return;
            EnsureEngineTurnSource();
            engineTurnSource.volume = Mathf.Lerp(0f, crankEngineVolume, Mathf.Clamp01(rpmValue * 3f));
            engineTurnSource.pitch = Mathf.Lerp(0.35f, 2.2f, rpmValue);
            if (!engineTurnSource.isPlaying)
                engineTurnSource.Play();
        }

        private void EnsureEngineTurnSource()
        {
            if (engineTurnSource != null) return;
            GameObject sourceObject = new("Generator B Crank Audio");
            sourceObject.transform.SetParent(transform, false);
            sourceObject.transform.position = GeneratorPosition;
            engineTurnSource = sourceObject.AddComponent<AudioSource>();
            engineTurnSource.clip = ProceduralSfx.EngineTurnLoop;
            engineTurnSource.loop = true;
            engineTurnSource.playOnAwake = false;
            engineTurnSource.spatialBlend = 1f;
            engineTurnSource.rolloffMode = AudioRolloffMode.Linear;
            engineTurnSource.minDistance = 3f;
            engineTurnSource.maxDistance = 35f;
        }

        private void SetColdStartMusic(bool playing)
        {
            if (coldStartMusic == null) return;
            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                musicSource.clip = coldStartMusic;
                musicSource.loop = true;
                musicSource.playOnAwake = false;
                musicSource.spatialBlend = 0f;
                musicSource.volume = 0f;
            }
            StopRoutine(ref musicFadeRoutine);
            musicFadeRoutine = StartCoroutine(FadeMusic(playing ? coldStartMusicVolume : 0f, playing ? 2f : 3f));
        }

        private IEnumerator FadeMusic(float target, float duration)
        {
            if (target > 0f && !musicSource.isPlaying)
                musicSource.Play();
            float start = musicSource.volume;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                musicSource.volume = Mathf.Lerp(start, target, t / duration);
                yield return null;
            }
            musicSource.volume = target;
            if (target <= 0f)
                musicSource.Stop();
            musicFadeRoutine = null;
        }

        private void StartLocalGeneratorAudio(bool playStartup)
        {
            if (generatorAudioSource == null) return;
            StopRoutine(ref generatorAudioRoutine);
            generatorAudioRoutine = StartCoroutine(PlayGeneratorAudioSequence(playStartup));
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

        private IEnumerator PourPresentation()
        {
            if (!pourVisualInitialized)
            {
                pourRestRotation = pourVisual.localRotation;
                pourVisualInitialized = true;
            }
            Quaternion tilted = pourRestRotation * Quaternion.Euler(pourTiltEuler);
            yield return RotatePour(pourVisual.localRotation, tilted, pourTiltDuration);
            yield return new WaitForSecondsRealtime(pourHoldDuration);
            yield return RotatePour(pourVisual.localRotation, pourRestRotation, pourTiltDuration);
            pourRoutine = null;
        }

        private IEnumerator RotatePour(Quaternion from, Quaternion to, float duration)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                pourVisual.localRotation = Quaternion.Slerp(from, to, elapsed / duration);
                yield return null;
            }
            pourVisual.localRotation = to;
        }

        private void StopRoutine(ref Coroutine routine)
        {
            if (routine != null)
                StopCoroutine(routine);
            routine = null;
        }
    }
}
