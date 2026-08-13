using UnityEngine;
using UnityEngine.Events;

public class InteractableItem : MonoBehaviour, IInteractable
{
    public enum HintPresentationMode
    {
        Sprite,
        None,
        Subtitle,
        Sequence
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
    [Tooltip("Sequence 모드에서 재생할 영상/애니메이션 화면입니다.")]
    [SerializeField] private HintSequencePresentation sequencePresentation;

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
    [Min(1)]
    [SerializeField] protected int questProgressAmount = 1;

    [Header("Events")]
    [SerializeField] protected UnityEvent onInteractEvent;

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
        if (!isInteracted)
        {
            CompleteFirstInteraction();
            ReportHintConfirmation();
        }

        if (player == null)
        {
            return;
        }

        if (useCameraPresentation)
        {
            if (cameraPresentation != null)
                cameraPresentation.Begin(player);
            else
                Debug.LogWarning("[InteractableItem] Camera Presentation is not assigned.", this);
            return;
        }

        switch (presentationMode)
        {
            case HintPresentationMode.Subtitle:
                player.CloseAllUI();
                subtitlePresentation?.PlaySubtitleFromInteract();
                break;

            case HintPresentationMode.Sprite:
                player.OpenHint(hintSprite);
                break;

            case HintPresentationMode.Sequence:
                player.OpenSequence(sequencePresentation);
                break;

            default:
                player.CloseAllUI();
                break;
        }
    }

    private void ReportHintConfirmation()
    {
        if (hintConfirmationTracker != null)
            hintConfirmationTracker.ConfirmHint(hintId, this);
    }

    private void CompleteFirstInteraction()
    {
        isInteracted = true;

        // A tracked LobbyF hint advances through the server relay so every
        // player progresses exactly once. Untracked items stay personal.
        bool usesSharedHintProgress = hintConfirmationTracker != null &&
                                      !string.IsNullOrWhiteSpace(hintId);
        if (progressesQuest && !usesSharedHintProgress && QuestManager.Instance != null)
        {
            QuestManager.Instance.ProgressActiveQuest(questProgressAmount);
            QuestManager.Instance.RevealPendingQuestAfterSubtitle();
        }

        onInteractEvent?.Invoke();
    }
}
