using UnityEngine;

[DisallowMultipleComponent]
public sealed class HintThresholdAssistPresentation : MonoBehaviour
{
    [SerializeField] private HintConfirmationTracker hintTracker;
    [SerializeField] private SubtitleTrigger assistSubtitle;

    private bool hasPlayed;

    private void OnEnable()
    {
        if (hintTracker == null)
            hintTracker = HintConfirmationTracker.Instance;

        if (hintTracker == null)
        {
            Debug.LogError(
                "[HintThresholdAssistPresentation] HintConfirmationTracker가 연결되지 않았습니다.",
                this);
            return;
        }

        hintTracker.ThresholdPresentationStarted -= HandleThresholdPresentationStarted;
        hintTracker.ThresholdPresentationStarted += HandleThresholdPresentationStarted;
    }

    private void OnDisable()
    {
        if (hintTracker != null)
            hintTracker.ThresholdPresentationStarted -= HandleThresholdPresentationStarted;
    }

    private void HandleThresholdPresentationStarted()
    {
        if (hasPlayed)
            return;

        if (assistSubtitle == null)
        {
            Debug.LogError(
                "[HintThresholdAssistPresentation] Assist Subtitle이 연결되지 않았습니다.",
                this);
            return;
        }

        hasPlayed = true;
        assistSubtitle.PlaySubtitleFromInteract();
    }
}
