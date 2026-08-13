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

    // Presentation progress exists on every peer. Only the server writes the
    // authoritative set used for duplicate rejection and threshold decisions.
    private readonly HashSet<string> confirmedHintIds = new();
    private readonly HashSet<string> serverConfirmedHintIds = new();

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
}
