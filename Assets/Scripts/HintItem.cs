using UnityEngine;

public class HintItem : MonoBehaviour, IInteractable
{
    [Header("힌트 설정")]
    public string itemName = "의문의 문서";
    public Sprite hintSprite;             // 화면에 띄울 힌트 이미지
    public bool isHoldInteraction = true; // 최초 조사 시 꾹 누르기 여부
    
    [HideInInspector]
    public bool isInteracted = false;     // 최초 상호작용 여부 체크

    public string GetInteractionText()
    {
        if (isInteracted)
        {
            return $"{itemName} 다시 보기 (E)";
        }
        else
        {
            return isHoldInteraction ? $"{itemName} 조사하기 (E 꾹 누르기)" : $"{itemName} 조사하기 (E)";
        }
    }

    public bool IsHoldInteraction()
    {
        // 이미 한 번 읽었다면 다음부터는 딸깍(단타)으로 작동
        if (isInteracted) return false;
        return isHoldInteraction;
    }

    public void Interact(PlayerInteraction player)
    {
        if (!isInteracted)
        {
            isInteracted = true;
            Debug.Log($"[최초 힌트] '{itemName}' 발견");

            // 퀘스트 카운트 증가
            if (QuestManager.Instance != null)
            {
                QuestManager.Instance.ProgressActiveQuest(1);
            }
        }

        // 힌트 이미지 UI 표시 및 플레이어 조작 차단
        if (hintSprite != null && player != null)
        {
            player.hintImage.sprite = hintSprite;
            player.hintPanel.SetActive(true);
            player.TogglePlayerControl(false);
        }
    }
}