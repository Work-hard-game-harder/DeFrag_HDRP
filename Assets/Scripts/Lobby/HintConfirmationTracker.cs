using System.Collections.Generic;
using DeFrag.Lobby;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class HintConfirmationTracker : MonoBehaviour
{
    [Min(1)] [SerializeField] private int emergencyPowerThreshold = 3;
    [SerializeField] private LobbyPowerController powerController;
    [SerializeField] private UnityEvent<int> onConfirmedHintCountChanged;

    private readonly HashSet<string> confirmedHintIds = new();

    public int ConfirmedHintCount => confirmedHintIds.Count;

    public void ConfirmHint(string hintId, Object context)
    {
        if (string.IsNullOrWhiteSpace(hintId))
        {
            Debug.LogError("[HintConfirmationTracker] Hint ID가 비어 있습니다.", context);
            return;
        }

        if (!confirmedHintIds.Add(hintId))
            return;

        int count = confirmedHintIds.Count;
        Debug.Log($"[LobbyHint] 확인 완료: {hintId} ({count}/{emergencyPowerThreshold})", context);
        onConfirmedHintCountChanged?.Invoke(count);

        if (powerController != null)
            powerController.PlayHintWarning(count >= emergencyPowerThreshold);
    }
}
