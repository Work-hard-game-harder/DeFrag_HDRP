using System;
using System.Collections.Generic;
using DeFrag.Lobby;
using StarterAssets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

[DisallowMultipleComponent]
public sealed class HintConfirmationTracker : MonoBehaviour
{
    public static HintConfirmationTracker Instance { get; private set; }

    [Min(1)] [SerializeField] private int emergencyPowerThreshold = 3;
    [SerializeField] private LobbyPowerController powerController;
    [SerializeField] private UnityEvent<int> onConfirmedHintCountChanged;

    [Header("Quest Progress")]
    [Tooltip("이 Hint ID가 서버에서 처음 확인되면 아래 Quest Signal을 한 번 보고합니다.")]
    [SerializeField] private string questProgressHintId;
    [SerializeField] private string questProgressSignal;
    [SerializeField] private string questProgressSourceId;

    [Header("Deferred Shared Presentation")]
    [Tooltip("이 Hint는 확인 직후가 아니라 기존 자막 종료 후 공용 불빛 연출을 시작합니다.")]
    [SerializeField] private string deferredPresentationHintId;

    [Header("Shared Broadcast")]
    [SerializeField] private string sharedBroadcastId = "TV";
    [SerializeField] private HintCameraPresentation sharedBroadcastPresentation;

    // Presentation progress exists on every peer. Only the server writes the
    // authoritative set used for duplicate rejection and threshold decisions.
    private readonly HashSet<string> confirmedHintIds = new();
    private readonly HashSet<string> serverConfirmedHintIds = new();
    private readonly HashSet<string> startedPresentationHintIds = new();
    private readonly HashSet<string> serverStartedPresentationHintIds = new();
    private readonly HashSet<string> serverCompletedPresentationHintIds = new();
    private readonly HashSet<string> serverClosedHintIds = new();
    private string serverThresholdHintId;
    private ulong serverThresholdHintOwnerClientId = ulong.MaxValue;
    private bool serverThresholdPresentationStarted;
    private int authoritativeConfirmedHintCount;
    private bool thresholdPresentationApplied;
    private bool thresholdAssistPending;
    private string localThresholdHintId;
    private double serverBroadcastStartTime = double.NegativeInfinity;
    private float serverBroadcastDuration;

    public int ConfirmedHintCount => Mathf.Max(
        confirmedHintIds.Count,
        authoritativeConfirmedHintCount);
    public int EmergencyPowerThreshold => emergencyPowerThreshold;
    public event Action<int> ConfirmedHintCountChanged;
    public event Action<string> SharedHintPresentationStarted;
    public event Action<string, bool> HintWarningFocusRequested;
    public event Action ThresholdPresentationStarted;

    private void Awake()
    {
        Instance = this;
        if (powerController != null)
            powerController.EmergencyPowerStarted += HandleEmergencyPowerStarted;
    }

    private void OnDestroy()
    {
        if (powerController != null)
            powerController.EmergencyPowerStarted -= HandleEmergencyPowerStarted;
        if (Instance == this) Instance = null;
    }

    public void ConfirmHint(string hintId, Object context)
    {
        if (string.IsNullOrWhiteSpace(hintId))
        {
            Debug.LogError("[HintConfirmationTracker] Hint ID is empty.", context);
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            NetworkObject localPlayer = networkManager.LocalClient?.PlayerObject;
            PersonController relay = localPlayer != null
                ? localPlayer.GetComponent<PersonController>()
                : null;
            if (relay == null)
            {
                Debug.LogError("[LobbyHint] Local network player relay was not found.", context);
                return;
            }

            relay.RequestLobbyHintConfirmation(hintId);
            return;
        }

        // Single-player scene testing follows the same authoritative path.
        if (!TryConfirmOnServer(
                hintId,
                ulong.MaxValue,
                out int count,
                out bool emergency))
            return;

        ApplyServerConfirmation(hintId.Trim(), count, emergency, context);
    }

    public void CompleteHintPresentation(string hintId, Object context)
    {
        if (string.IsNullOrWhiteSpace(hintId))
            return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            NetworkObject localPlayer = networkManager.LocalClient?.PlayerObject;
            PersonController relay = localPlayer != null
                ? localPlayer.GetComponent<PersonController>()
                : null;
            if (relay == null)
            {
                Debug.LogError("[LobbyHint] Local network player relay was not found.", context);
                return;
            }

            relay.RequestLobbyHintClosed(hintId);
            return;
        }

        if (TryCloseHintOnServer(hintId))
            ApplyThresholdPresentationStart(context);
    }

    public bool TryConfirmOnServer(
        string hintId,
        ulong requesterClientId,
        out int count,
        out bool emergency)
    {
        count = serverConfirmedHintIds.Count;
        emergency = count >= emergencyPowerThreshold;
        if (string.IsNullOrWhiteSpace(hintId))
            return false;

        string normalizedHintId = hintId.Trim();
        if (!serverConfirmedHintIds.Add(normalizedHintId))
            return false;

        count = serverConfirmedHintIds.Count;
        emergency = count >= emergencyPowerThreshold;
        if (count == emergencyPowerThreshold)
        {
            serverThresholdHintId = normalizedHintId;
            serverThresholdHintOwnerClientId = requesterClientId;
        }
        return true;
    }

    public bool TryCloseHintOnServer(
        string hintId,
        ulong requesterClientId = ulong.MaxValue)
    {
        if (serverThresholdPresentationStarted ||
            string.IsNullOrWhiteSpace(hintId) ||
            string.IsNullOrWhiteSpace(serverThresholdHintId))
        {
            return false;
        }

        string normalizedHintId = hintId.Trim();
        if (!serverConfirmedHintIds.Contains(normalizedHintId) ||
            !string.Equals(
                normalizedHintId,
                serverThresholdHintId,
                StringComparison.OrdinalIgnoreCase) ||
            requesterClientId != serverThresholdHintOwnerClientId ||
            !serverClosedHintIds.Add(normalizedHintId))
        {
            return false;
        }

        serverThresholdPresentationStarted = true;
        return true;
    }

    public bool TryStartHintPresentationOnServer(string hintId, out bool emergency)
    {
        emergency = serverConfirmedHintIds.Count >= emergencyPowerThreshold;
        if (string.IsNullOrWhiteSpace(hintId))
            return false;

        string normalizedHintId = hintId.Trim();
        if (!serverConfirmedHintIds.Contains(normalizedHintId) ||
            !serverStartedPresentationHintIds.Add(normalizedHintId))
            return false;

        return IsDeferredPresentationHint(normalizedHintId);
    }

    public bool TryCompleteHintPresentationQuestOnServer(string hintId)
    {
        if (string.IsNullOrWhiteSpace(hintId))
            return false;

        string normalizedHintId = hintId.Trim();
        if (!serverStartedPresentationHintIds.Contains(normalizedHintId) ||
            !serverCompletedPresentationHintIds.Add(normalizedHintId))
            return false;

        if (ReportConfirmedHintQuestProgress(normalizedHintId))
            return true;

        // The quest manager may not have been ready or the expected quest was
        // not active. Allow a later completion request to retry safely.
        serverCompletedPresentationHintIds.Remove(normalizedHintId);
        return false;
    }

    public void ApplySharedHintPresentationStart(string hintId, bool emergency)
    {
        if (string.IsNullOrWhiteSpace(hintId) ||
            !startedPresentationHintIds.Add(hintId.Trim()))
            return;

        // This is the one configured story hint that keeps its existing Assist.
        // If it is also the threshold hint, the final power transition restarts
        // the warning safely and still waits for that flicker before Assist #2.
        powerController?.PlayHintWarning(false);
        HintWarningFocusRequested?.Invoke(hintId, true);
        SharedHintPresentationStarted?.Invoke(hintId);
    }

    public void ApplyThresholdPresentationStart(Object context = null)
    {
        if (thresholdPresentationApplied)
            return;

        thresholdPresentationApplied = true;
        thresholdAssistPending = true;
        Debug.Log(
            "[LobbyHint] The third hint presentation closed. Starting the shared power/Assist event.",
            context);
        HintWarningFocusRequested?.Invoke(localThresholdHintId, false);

        if (powerController == null ||
            powerController.CurrentState == LobbyPowerState.EmergencyPower)
        {
            HandleEmergencyPowerStarted();
            return;
        }

        powerController.PlayHintWarning(true);
    }

    private void HandleEmergencyPowerStarted()
    {
        if (!thresholdAssistPending)
            return;

        thresholdAssistPending = false;
        ThresholdPresentationStarted?.Invoke();
    }

    private bool ReportConfirmedHintQuestProgress(string hintId)
    {
        if (string.IsNullOrWhiteSpace(questProgressHintId) ||
            string.IsNullOrWhiteSpace(questProgressSignal) ||
            !string.Equals(
                hintId.Trim(),
                questProgressHintId.Trim(),
                System.StringComparison.OrdinalIgnoreCase))
            return false;

        QuestManager questManager = QuestManager.Instance;
        if (questManager == null)
            return false;

        string sourceId = string.IsNullOrWhiteSpace(questProgressSourceId)
            ? hintId
            : questProgressSourceId;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            return questManager.ReportProgress(questProgressSignal, sourceId);
        }

        if (!networkManager.IsServer)
            return false;

        bool changed = questManager.TryReportSharedProgressOnServer(
                questProgressSignal,
                sourceId,
                1);
        if (changed)
            questManager.BroadcastSharedSnapshotFromServer();
        return changed;
    }

    public void ApplyServerConfirmation(
        string hintId,
        int count,
        bool emergency,
        Object context = null)
    {
        if (!confirmedHintIds.Add(hintId)) return;

        authoritativeConfirmedHintCount = Mathf.Max(
            authoritativeConfirmedHintCount,
            count);
        if (count == emergencyPowerThreshold)
            localThresholdHintId = hintId?.Trim();

        Debug.Log(
            $"[LobbyHint] Confirmed: {hintId} ({count}/{emergencyPowerThreshold})",
            context);
        onConfirmedHintCountChanged?.Invoke(count);
        ConfirmedHintCountChanged?.Invoke(count);

        // Deferred hints start their shared light/camera presentation only after
        // the interacting player's existing subtitle has finished.
        if (!IsDeferredPresentationHint(hintId) && count < emergencyPowerThreshold)
        {
            powerController?.PlayHintWarning(false);
            HintWarningFocusRequested?.Invoke(hintId, false);
        }
    }

    private bool IsDeferredPresentationHint(string hintId) =>
        !string.IsNullOrWhiteSpace(deferredPresentationHintId) &&
        string.Equals(
            hintId?.Trim(),
            deferredPresentationHintId.Trim(),
            StringComparison.OrdinalIgnoreCase);

    public void RequestSharedBroadcast(string broadcastId, float duration, Object context)
    {
        if (string.IsNullOrWhiteSpace(broadcastId) || duration <= 0f) return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            NetworkObject localPlayer = networkManager.LocalClient?.PlayerObject;
            PersonController relay = localPlayer != null
                ? localPlayer.GetComponent<PersonController>()
                : null;
            if (relay == null)
            {
                Debug.LogError("[LobbyBroadcast] Local network player relay was not found.", context);
                return;
            }

            relay.RequestLobbyBroadcastStart(broadcastId, duration);
            return;
        }

        ApplyServerBroadcastStart(broadcastId, Time.unscaledTimeAsDouble, duration, context);
    }

    public bool TryStartBroadcastOnServer(
        string broadcastId,
        float duration,
        out double startTime)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        double now = networkManager != null
            ? networkManager.ServerTime.Time
            : Time.unscaledTimeAsDouble;

        startTime = serverBroadcastStartTime;
        bool sameBroadcastIsActive = broadcastId == sharedBroadcastId &&
                                     now - serverBroadcastStartTime < serverBroadcastDuration;
        if (sameBroadcastIsActive) return false;
        if (broadcastId != sharedBroadcastId || duration <= 0f) return false;

        serverBroadcastStartTime = now;
        serverBroadcastDuration = duration;
        startTime = now;
        return true;
    }

    public bool TryGetActiveBroadcastOnServer(
        out string broadcastId,
        out double startTime,
        out float duration)
    {
        broadcastId = sharedBroadcastId;
        startTime = serverBroadcastStartTime;
        duration = serverBroadcastDuration;

        NetworkManager networkManager = NetworkManager.Singleton;
        double now = networkManager != null
            ? networkManager.ServerTime.Time
            : Time.unscaledTimeAsDouble;
        return duration > 0f && now - startTime < duration;
    }

    public void ApplyServerBroadcastStart(
        string broadcastId,
        double serverStartTime,
        float duration,
        Object context = null)
    {
        if (broadcastId != sharedBroadcastId || sharedBroadcastPresentation == null)
            return;

        Debug.Log(
            $"[LobbyBroadcast] '{broadcastId}' started at server time {serverStartTime:0.000}.",
            context);
        sharedBroadcastPresentation.PlaySharedNetworkBroadcast(serverStartTime, duration);
    }
}
