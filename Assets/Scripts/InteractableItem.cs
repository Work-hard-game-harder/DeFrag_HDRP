using UnityEngine;
using UnityEngine.Events;

public class InteractableItem : MonoBehaviour, IInteractable
{
    [Header("Interaction")]
    [SerializeField] protected string itemName = "조사 대상";
    [SerializeField] protected bool isHoldInteraction = true;

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

    public void Interact(PlayerInteraction player)
    {
        if (!isInteracted)
        {
            isInteracted = true;

            if (progressesQuest && QuestManager.Instance != null)
            {
                QuestManager.Instance.ProgressActiveQuest(questProgressAmount);
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
