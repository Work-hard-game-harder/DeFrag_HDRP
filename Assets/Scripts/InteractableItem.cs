using UnityEngine;
using UnityEngine.Events;

public class InteractableItem : MonoBehaviour, IInteractable
{
    public enum HintPresentationMode
    {
        Sprite = 0,
        None = 1,
        Subtitle = 2
    }

    [Header("Interaction")]
    [SerializeField] protected string itemName = "조사 대상";
    [SerializeField] protected bool isHoldInteraction = true;
    [Tooltip("Prevents screens, papers, and other flat hints from being used from behind.")]
    [SerializeField] protected bool requireFrontFacing = true;

    [Header("Hint Presentation")]
    [SerializeField] private HintPresentationMode presentationMode;
    [Tooltip("Subtitle 모드에서 재생할 자막 트리거입니다.")]
    [SerializeField] private SubtitleTrigger subtitlePresentation;
    [Tooltip("Sprite 모드에서 표시할 이미지입니다.")]
    [SerializeField] protected Sprite hintSprite;

    [Header("Optional Camera Presentation")]
    [Tooltip("When enabled, this camera presentation replaces the normal hint presentation.")]
    [SerializeField] private bool useCameraPresentation;
    [SerializeField] private HintCameraPresentation cameraPresentation;

    [Header("Hint Progress")]
    [Tooltip("LobbyF 힌트 진행도에서 중복을 구분할 안정적인 ID입니다.")]
    [SerializeField] private string hintId;
    [SerializeField] private HintConfirmationTracker hintConfirmationTracker;

    [Header("Quest")]
    [SerializeField] protected bool progressesQuest = true;
    [Tooltip("이 상호작용이 완료할 퀘스트 신호입니다. 비어 있으면 진행하지 않습니다.")]
    [SerializeField] protected string questSignal;
    [SerializeField] protected string questSourceId;
    [Min(1)]
    [SerializeField] protected int questProgressAmount = 1;

    [Header("Events")]
    [SerializeField] protected UnityEvent onInteractEvent;
    [Tooltip("Subtitle 모드에서 모든 자막 재생이 끝난 뒤 호출됩니다.")]
    [SerializeField] private UnityEvent onSubtitlePresentationCompleted;

    [HideInInspector] public bool isInteracted;

    public string GetInteractionText()
    {
        if (isInteracted)
        {
            return $"{itemName} 다시 보기 (E)";
        }

        return isHoldInteraction
            ? $"{itemName} 조사하기 (E 꾹 누르기)"
            : $"{itemName} 조사하기 (E)";
    }

    public bool IsHoldInteraction() => !isInteracted && isHoldInteraction;

    public bool CanInteractFrom(RaycastHit hit, Vector3 viewDirection)
    {
        return !requireFrontFacing || Vector3.Dot(viewDirection, hit.normal) < -0.1f;
    }

    public void Interact(PlayerInteraction player)
    {
        bool isFirstInteraction = !isInteracted;
        bool reportPresentationClosed = isFirstInteraction &&
                                        hintConfirmationTracker != null &&
                                        !string.IsNullOrWhiteSpace(hintId);
        if (isFirstInteraction)
        {
            CompleteFirstInteraction();
            ReportHintConfirmation();
        }

        if (player == null)
        {
            if (reportPresentationClosed)
                ReportHintPresentationClosed();
            return;
        }

        if (useCameraPresentation)
        {
            if (cameraPresentation != null)
                cameraPresentation.Begin(
                    player,
                    reportPresentationClosed ? ReportHintPresentationClosed : null);
            else
            {
                Debug.LogWarning("[InteractableItem] Camera Presentation is not assigned.", this);
                if (reportPresentationClosed)
                    ReportHintPresentationClosed();
            }
            return;
        }

        switch (presentationMode)
        {
            case HintPresentationMode.Subtitle:
                player.CloseAllUI();
                if (subtitlePresentation != null)
                    subtitlePresentation.PlaySubtitleFromInteract(
                        () =>
                        {
                            onSubtitlePresentationCompleted?.Invoke();
                            if (reportPresentationClosed)
                                ReportHintPresentationClosed();
                        });
                else
                {
                    onSubtitlePresentationCompleted?.Invoke();
                    if (reportPresentationClosed)
                        ReportHintPresentationClosed();
                }
                break;

            case HintPresentationMode.Sprite:
                player.OpenHint(
                    hintSprite,
                    reportPresentationClosed ? ReportHintPresentationClosed : null);
                break;

            default:
                player.CloseAllUI();
                if (reportPresentationClosed)
                    ReportHintPresentationClosed();
                break;
        }
    }

    private void ReportHintConfirmation()
    {
        if (hintConfirmationTracker != null)
            hintConfirmationTracker.ConfirmHint(hintId, this);
    }

    private void ReportHintPresentationClosed()
    {
        if (hintConfirmationTracker != null)
            hintConfirmationTracker.CompleteHintPresentation(hintId, this);
    }

    private void CompleteFirstInteraction()
    {
        isInteracted = true;

        // A tracked LobbyF hint advances through the server relay so every
        // player progresses exactly once. Untracked items stay personal.
        bool usesSharedHintProgress = hintConfirmationTracker != null &&
                                      !string.IsNullOrWhiteSpace(hintId);
        if (progressesQuest && !usesSharedHintProgress && QuestManager.Instance != null &&
            !string.IsNullOrWhiteSpace(questSignal))
        {
            string source = string.IsNullOrWhiteSpace(questSourceId) ? gameObject.name : questSourceId;
            QuestManager.Instance.ReportProgress(questSignal, source, questProgressAmount);
            QuestManager.Instance.RevealPendingQuestAfterSubtitle();
        }

        onInteractEvent?.Invoke();
    }
}
