using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public enum ConnectServerUplinkPhase : byte
{
    Idle,
    Connecting,
    AwaitingOpticalScan,
    AwaitingVerification,
    Suspended,
    Completed,
    Failed
}

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class ConnectServerCoordinator : NetworkBehaviour
{
    private const ulong NoClient = ulong.MaxValue;
    private const int CameraWordCount = 4;

    [Header("Relay Pool")]
    [SerializeField] private List<OpticalRelayNode> relayNodes = new();
    [SerializeField, Min(1)] private int requiredRounds = 3;
    [SerializeField] private List<string> authorizationWords = new()
    {
        "LIMA", "OSCAR", "SIERRA", "VICTOR", "ECHO", "KILO"
    };

    [Header("Timing")]
    [SerializeField, Min(0f)] private float connectionDelay = 1.25f;
    [SerializeField, Min(1f)] private float scanTimeLimit = 90f;
    [SerializeField, Min(1f)] private float verificationTimeLimit = 30f;

    [Header("Server Validation")]
    [SerializeField, Min(0.1f)] private float cameraPlayerTolerance = 2.5f;
    [SerializeField, Range(0.5f, 1f)] private float minimumAimDot = 0.975f;
    [SerializeField] private LayerMask obstructionMask = ~0;

    [Header("Trace")]
    [SerializeField, Range(1f, 100f)] private float wrongRelayTrace = 28f;
    [SerializeField, Range(1f, 100f)] private float timeoutTrace = 20f;
    [SerializeField, Range(1f, 100f)] private float traceLimit = 100f;

    [Header("Wrong Capture Alarm")]
    [SerializeField, Min(0f)] private float wrongRelayAlarmRadius = 60f;
    [SerializeField] private AudioClip wrongRelayAlarmClip;
    [SerializeField, Range(0f, 1f)] private float wrongRelayAlarmVolume = 1f;

    [Header("Quest Signal")]
    [SerializeField] private string completionQuestSignal = QuestSignals.B1FConnectServerCompleted;
    [SerializeField] private string completionQuestSourceId = "CONNECT_SERVER";

    private readonly NetworkVariable<ConnectServerUplinkPhase> phase = new(
        ConnectServerUplinkPhase.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<FixedString64Bytes> targetRelayId = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<FixedString64Bytes> targetSector = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> completedRounds = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> trace = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> deadline = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> terminalOperator = new(
        NoClient,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> requestedWordIndex = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private OpticalRelayNode currentTarget;
    private string expectedAuthorization;
    private readonly string[] currentCameraWords = new string[CameraWordCount];
    private int previousTargetIndex = -1;
    private double connectAt;

    public static ConnectServerCoordinator LocalInstance { get; private set; }
    public static event Action<ConnectServerCoordinator> LocalInstanceAvailable;

    public ConnectServerUplinkPhase Phase => phase.Value;
    public string TargetRelayId => targetRelayId.Value.ToString();
    public string TargetSector => targetSector.Value.ToString();
    public int CompletedRounds => completedRounds.Value;
    public int RequiredRounds => requiredRounds;
    public float Trace => trace.Value;
    public double Deadline => deadline.Value;
    public ulong TerminalOperatorClientId => terminalOperator.Value;
    public int RequestedWordNumber => requestedWordIndex.Value + 1;
    public double ServerTime => NetworkManager != null && NetworkManager.IsListening
        ? NetworkManager.ServerTime.Time
        : Time.unscaledTimeAsDouble;

    public event Action StateChanged;
    public event Action<bool, string, string> LocalPhotoResolved;

    public bool TryCompleteForStoryDebugServer()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!IsSpawned || !IsServer)
            return false;
        if (phase.Value == ConnectServerUplinkPhase.Completed)
            return true;

        completedRounds.Value = requiredRounds;
        deadline.Value = 0d;
        targetRelayId.Value = default;
        targetSector.Value = default;
        requestedWordIndex.Value = -1;
        terminalOperator.Value = NoClient;
        expectedAuthorization = string.Empty;
        phase.Value = ConnectServerUplinkPhase.Completed;
        return true;
#else
        return false;
#endif
    }
    public event Action<bool, string> LocalVerificationResolved;

    public override void OnNetworkSpawn()
    {
        phase.OnValueChanged += OnPhaseChanged;
        targetRelayId.OnValueChanged += OnFixedStringChanged;
        targetSector.OnValueChanged += OnFixedStringChanged;
        completedRounds.OnValueChanged += OnIntChanged;
        trace.OnValueChanged += OnFloatChanged;
        deadline.OnValueChanged += OnDoubleChanged;
        terminalOperator.OnValueChanged += OnUlongChanged;
        requestedWordIndex.OnValueChanged += OnIntChanged;

        LocalInstance = this;
        LocalInstanceAvailable?.Invoke(this);
        StateChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= OnPhaseChanged;
        targetRelayId.OnValueChanged -= OnFixedStringChanged;
        targetSector.OnValueChanged -= OnFixedStringChanged;
        completedRounds.OnValueChanged -= OnIntChanged;
        trace.OnValueChanged -= OnFloatChanged;
        deadline.OnValueChanged -= OnDoubleChanged;
        terminalOperator.OnValueChanged -= OnUlongChanged;
        requestedWordIndex.OnValueChanged -= OnIntChanged;
        if (LocalInstance == this)
            LocalInstance = null;
    }

    private void Update()
    {
        if (!IsServer)
            return;

        double now = ServerTime;
        if (phase.Value == ConnectServerUplinkPhase.Connecting && now >= connectAt)
        {
            SelectNextTarget();
            return;
        }

        if (deadline.Value <= 0d || now < deadline.Value)
            return;

        if (phase.Value == ConnectServerUplinkPhase.AwaitingOpticalScan ||
            phase.Value == ConnectServerUplinkPhase.AwaitingVerification)
        {
            AddTrace(timeoutTrace);
            if (phase.Value != ConnectServerUplinkPhase.Failed)
                SelectNextTarget();
        }
    }

    public void RequestStartOrResume()
    {
        if (IsSpawned)
            RequestStartOrResumeServerRpc();
    }

    public void RequestSuspend()
    {
        if (IsSpawned)
            RequestSuspendServerRpc();
    }

    public void SubmitPhoto(OpticalRelayNode node, Vector3 cameraPosition, Vector3 cameraForward)
    {
        if (IsSpawned && node != null)
            SubmitPhotoServerRpc(node.RelayId, cameraPosition, cameraForward);
    }

    public void SubmitVerification(string value)
    {
        if (IsSpawned)
            SubmitVerificationServerRpc(value.Trim().ToUpperInvariant());
    }

    public bool TryGetRelay(string relayId, out OpticalRelayNode relay)
    {
        relay = FindRelay(relayId);
        return relay != null;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartOrResumeServerRpc(ServerRpcParams rpc = default)
    {
        ulong sender = rpc.Receive.SenderClientId;
        switch (phase.Value)
        {
            case ConnectServerUplinkPhase.Idle:
            case ConnectServerUplinkPhase.Failed:
            case ConnectServerUplinkPhase.Completed:
                if (!HasUsableRelayConfiguration())
                {
                    ConfigurationFailureClientRpc(
                        "NO OPTICAL RELAYS CONFIGURED",
                        Target(sender));
                    return;
                }

                terminalOperator.Value = sender;
                completedRounds.Value = 0;
                trace.Value = 0f;
                expectedAuthorization = string.Empty;
                requestedWordIndex.Value = -1;
                phase.Value = ConnectServerUplinkPhase.Connecting;
                deadline.Value = 0d;
                connectAt = ServerTime + connectionDelay;
                break;

            case ConnectServerUplinkPhase.Suspended:
                terminalOperator.Value = sender;
                SelectNextTarget();
                break;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSuspendServerRpc(ServerRpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId != terminalOperator.Value)
            return;
        if (phase.Value != ConnectServerUplinkPhase.AwaitingOpticalScan &&
            phase.Value != ConnectServerUplinkPhase.AwaitingVerification &&
            phase.Value != ConnectServerUplinkPhase.Connecting)
            return;

        expectedAuthorization = string.Empty;
        requestedWordIndex.Value = -1;
        deadline.Value = 0d;
        phase.Value = ConnectServerUplinkPhase.Suspended;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitPhotoServerRpc(
        FixedString64Bytes relayId,
        Vector3 cameraPosition,
        Vector3 cameraForward,
        ServerRpcParams rpc = default)
    {
        ulong sender = rpc.Receive.SenderClientId;
        if (phase.Value != ConnectServerUplinkPhase.AwaitingOpticalScan ||
            sender == terminalOperator.Value)
            return;

        OpticalRelayNode photographed = FindRelay(relayId.ToString());
        if (!ValidateCapture(sender, photographed, cameraPosition, cameraForward))
        {
            PhotoResolvedClientRpc(false, relayId, "CAPTURE REJECTED", Target(sender));
            return;
        }

        if (photographed != currentTarget)
        {
            AddTrace(wrongRelayTrace);
            WorldNoiseSystem.EmitUrgent(photographed.ScanAnchor.position, wrongRelayAlarmRadius);
            RelayAlarmClientRpc(relayId);
            PhotoResolvedClientRpc(
                false,
                relayId,
                "WRONG RELAY // ALARM TRIGGERED",
                Target(sender));
            return;
        }

        expectedAuthorization = currentCameraWords[requestedWordIndex.Value];
        deadline.Value = ServerTime + verificationTimeLimit;
        phase.Value = ConnectServerUplinkPhase.AwaitingVerification;
        PhotoResolvedClientRpc(
            true,
            relayId,
            FormatCameraWordList(),
            Target(sender));
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitVerificationServerRpc(string submitted, ServerRpcParams rpc = default)
    {
        ulong sender = rpc.Receive.SenderClientId;
        if (phase.Value != ConnectServerUplinkPhase.AwaitingVerification ||
            sender != terminalOperator.Value)
            return;

        string normalized = submitted.Trim().ToUpperInvariant();
        if (!normalized.StartsWith("UPLOAD "))
        {
            AddTrace(wrongRelayTrace);
            VerificationResolvedClientRpc(false, "USE: UPLOAD [WORD]", Target(sender));
            return;
        }

        normalized = normalized.Substring(7).Trim();

        if (!string.Equals(normalized, expectedAuthorization, StringComparison.Ordinal))
        {
            AddTrace(wrongRelayTrace);
            VerificationResolvedClientRpc(false, "INVALID AUTH WORD", Target(sender));
            return;
        }

        completedRounds.Value++;
        VerificationResolvedClientRpc(true, "RELAY VERIFIED", Target(sender));
        expectedAuthorization = string.Empty;
        requestedWordIndex.Value = -1;

        if (completedRounds.Value >= requiredRounds)
        {
            deadline.Value = 0d;
            phase.Value = ConnectServerUplinkPhase.Completed;
            if (QuestManager.Instance != null && !string.IsNullOrWhiteSpace(completionQuestSignal))
                QuestManager.Instance.ReportProgress(
                    completionQuestSignal,
                    completionQuestSourceId);
        }
        else
        {
            SelectNextTarget();
        }
    }

    private bool ValidateCapture(
        ulong sender,
        OpticalRelayNode relay,
        Vector3 cameraPosition,
        Vector3 cameraForward)
    {
        if (relay == null || !relay.Selectable ||
            !relay.IsInFrontAndRange(cameraPosition, relay.CaptureDistance))
            return false;

        if (!NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client) ||
            client.PlayerObject == null ||
            Vector3.Distance(client.PlayerObject.transform.position, cameraPosition) > cameraPlayerTolerance)
            return false;

        Vector3 toAnchor = relay.ScanAnchor.position - cameraPosition;
        float distance = toAnchor.magnitude;
        if (distance <= 0.001f ||
            Vector3.Dot(cameraForward.normalized, toAnchor / distance) < minimumAimDot)
            return false;

        if (!Physics.Raycast(
                cameraPosition,
                toAnchor / distance,
                out RaycastHit hit,
                distance + 0.15f,
                obstructionMask,
                QueryTriggerInteraction.Collide))
            return false;

        return relay.OwnsCollider(hit.collider);
    }

    private void SelectNextTarget()
    {
        List<int> usableIndices = new();
        for (int i = 0; i < relayNodes.Count; i++)
        {
            OpticalRelayNode relay = relayNodes[i];
            if (relay != null && relay.Selectable && !string.IsNullOrWhiteSpace(relay.RelayId))
                usableIndices.Add(i);
        }

        if (usableIndices.Count == 0)
        {
            phase.Value = ConnectServerUplinkPhase.Failed;
            deadline.Value = 0d;
            return;
        }

        int selected = usableIndices[UnityEngine.Random.Range(0, usableIndices.Count)];
        if (usableIndices.Count > 1 && selected == previousTargetIndex)
        {
            int position = usableIndices.IndexOf(selected);
            selected = usableIndices[(position + 1) % usableIndices.Count];
        }

        previousTargetIndex = selected;
        currentTarget = relayNodes[selected];
        targetRelayId.Value = currentTarget.RelayId;
        targetSector.Value = currentTarget.Sector;
        GenerateWordChallenge();
        deadline.Value = ServerTime + scanTimeLimit;
        phase.Value = ConnectServerUplinkPhase.AwaitingOpticalScan;
    }

    private void AddTrace(float amount)
    {
        trace.Value = Mathf.Min(traceLimit, trace.Value + amount);
        if (trace.Value < traceLimit)
            return;

        expectedAuthorization = string.Empty;
        requestedWordIndex.Value = -1;
        deadline.Value = 0d;
        phase.Value = ConnectServerUplinkPhase.Failed;
    }

    private bool HasUsableRelayConfiguration()
    {
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (OpticalRelayNode relay in relayNodes)
            if (relay != null && relay.Selectable && identities.Add(relay.RelayId))
                return true;
        return false;
    }

    private OpticalRelayNode FindRelay(string id)
    {
        string normalized = id.Trim().Replace(' ', '_').ToUpperInvariant();
        foreach (OpticalRelayNode relay in relayNodes)
            if (relay != null && relay.RelayId == normalized)
                return relay;
        return null;
    }

    private void GenerateWordChallenge()
    {
        string[] fallbacks = { "LIMA", "OSCAR", "SIERRA", "VICTOR", "ECHO", "KILO" };
        List<string> pool = new();
        foreach (string configured in authorizationWords)
        {
            string normalized = string.IsNullOrWhiteSpace(configured)
                ? string.Empty
                : configured.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(normalized) && !pool.Contains(normalized))
                pool.Add(normalized);
        }

        foreach (string fallback in fallbacks)
            if (!pool.Contains(fallback))
                pool.Add(fallback);

        for (int i = 0; i < CameraWordCount; i++)
        {
            int selected = UnityEngine.Random.Range(0, pool.Count);
            currentCameraWords[i] = pool[selected];
            pool.RemoveAt(selected);
        }

        requestedWordIndex.Value = UnityEngine.Random.Range(0, CameraWordCount);
        expectedAuthorization = string.Empty;
    }

    private string FormatCameraWordList()
    {
        return $"[01] {currentCameraWords[0]}\n" +
               $"[02] {currentCameraWords[1]}\n" +
               $"[03] {currentCameraWords[2]}\n" +
               $"[04] {currentCameraWords[3]}";
    }

    [ClientRpc]
    private void PhotoResolvedClientRpc(
        bool success,
        FixedString64Bytes relayId,
        FixedString128Bytes message,
        ClientRpcParams rpc = default)
    {
        LocalPhotoResolved?.Invoke(success, relayId.ToString(), message.ToString());
    }

    [ClientRpc]
    private void RelayAlarmClientRpc(FixedString64Bytes relayId)
    {
        OpticalRelayNode relay = FindRelay(relayId.ToString());
        relay?.PlayLocalAlarm(wrongRelayAlarmClip, wrongRelayAlarmVolume);
    }

    [ClientRpc]
    private void VerificationResolvedClientRpc(
        bool success,
        FixedString64Bytes message,
        ClientRpcParams rpc = default)
    {
        LocalVerificationResolved?.Invoke(success, message.ToString());
    }

    [ClientRpc]
    private void ConfigurationFailureClientRpc(
        FixedString64Bytes message,
        ClientRpcParams rpc = default)
    {
        LocalVerificationResolved?.Invoke(false, message.ToString());
    }

    private static ClientRpcParams Target(ulong clientId) => new()
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
    };

    private void OnPhaseChanged(ConnectServerUplinkPhase _, ConnectServerUplinkPhase __) => StateChanged?.Invoke();
    private void OnFixedStringChanged(FixedString64Bytes _, FixedString64Bytes __) => StateChanged?.Invoke();
    private void OnIntChanged(int _, int __) => StateChanged?.Invoke();
    private void OnFloatChanged(float _, float __) => StateChanged?.Invoke();
    private void OnDoubleChanged(double _, double __) => StateChanged?.Invoke();
    private void OnUlongChanged(ulong _, ulong __) => StateChanged?.Invoke();
}
