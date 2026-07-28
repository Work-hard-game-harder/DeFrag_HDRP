using UnityEngine;
using UnityEngine.Events;

public class InteractableItem : MonoBehaviour, IInteractable
{
    [Header("Interaction")]
    [SerializeField] protected string itemName = "조사 대상";
    [SerializeField] protected bool isHoldInteraction = true;
    [Tooltip("Prevents screens, papers, and other flat hints from being used from behind.")]
    [SerializeField] protected bool requireFrontFacing = true;

    [Header("Hint")]
    [SerializeField] protected Sprite hintSprite;

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
            isInteracted = true;

            if (progressesQuest && QuestManager.Instance != null)
            {
                QuestManager.Instance.ProgressActiveQuest(questProgressAmount);

                // 이 상호작용으로 퀘스트가 완료되어 다음 단계가 대기 중이면 즉시 공개.
                // 아직 완료되지 않았다면 QuestManager 내부에서 자동으로 무시됨.
                QuestManager.Instance.RevealPendingQuestAfterSubtitle();
            }

            onInteractEvent?.Invoke();
        }

        if (player == null)
        {
            return;
        }

        if (hintSprite != null && player.hintImage != null && player.hintPanel != null)
        {
            player.hintImage.sprite = hintSprite;
            player.hintPanel.SetActive(true);
            player.TogglePlayerControl(false);
        }
        else
        {
            player.CloseAllUI();
        }
    }
}