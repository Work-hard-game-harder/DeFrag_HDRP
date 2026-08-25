using System.Collections.Generic;
using DeFrag.Lobby;
using StarterAssets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class HintConfirmationTracker : MonoBehaviour
{
    public static HintConfirmationTracker Instance { get; private set; }

    [Min(1)] [SerializeField] private int emergencyPowerThreshold = 3;
    [SerializeField] private LobbyPowerController powerController;
    [SerializeField] private UnityEvent<int> onConfirmedHintCountChanged;

    [Header("Shared Broadcast")]
    [SerializeField] private string sharedBroadcastId = "TV";
    [SerializeField] private HintCameraPresentation sharedBroadcastPresentation;

    // Presentation progress exists on every peer. Only the server writes the
    // authoritative set used for duplicate rejection and threshold decisions.
    private readonly HashSet<string> confirmedHintIds = new();
    private readonly HashSet<string> serverConfirmedHintIds = new();
    private double serverBroadcastStartTime = double.NegativeInfinity;
    private float serverBroadcastDuration;

    public int ConfirmedHintCount => confirmedHintIds.Count;

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
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

        // Single-player scene testing follows the same server-authoritative path.
        if (!serverConfirmedHintIds.Add(hintId)) return;
        int count = serverConfirmedHintIds.Count;
        ApplyServerConfirmation(
            hintId, count, count >= emergencyPowerThreshold, context);
    }

    public bool TryConfirmOnServer(string hintId, out int count, out bool emergency)
    {
        count = serverConfirmedHintIds.Count;
        emergency = count >= emergencyPowerThreshold;
        if (string.IsNullOrWhiteSpace(hintId) || !serverConfirmedHintIds.Add(hintId))
            return false;

        count = serverConfirmedHintIds.Count;
        emergency = count >= emergencyPowerThreshold;
        return true;
    }

    public void ApplyServerConfirmation(
        string hintId,
        int count,
        bool emergency,
        Object context = null)
    {
        if (!confirmedHintIds.Add(hintId)) return;

        Debug.Log(
            $"[LobbyHint] Confirmed: {hintId} ({count}/{emergencyPowerThreshold})",
            context);
        onConfirmedHintCountChanged?.Invoke(count);

        // Hint-linked quest progress is shared. Personal acquisition quests
        // remain on their original local path.
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.ProgressActiveQuest(1);
            QuestManager.Instance.RevealPendingQuestAfterSubtitle();
        }

        powerController?.PlayHintWarning(emergency);
    }

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
