using UnityEngine;

public class InventoryItem : MonoBehaviour, IInteractable
{
    [Header("인벤토리 아이템 설정")]
    public string itemName = "보안실 키카드";
    public string itemID = "KEYCARD_LV1";  // 인벤토리 시스템이 인식할 고유 ID
    public bool isHoldInteraction = false;

    public string GetInteractionText()
    {
        return isHoldInteraction ? $"{itemName} 줍기 (E 꾹 누르기)" : $"{itemName} 줍기 (E)";
    }

    public bool IsHoldInteraction() => isHoldInteraction;

    public void Interact(PlayerInteraction player)
    {
        
        Debug.Log($"[인벤토리 추가] '{itemName}' 수집완료. ID: {itemID}");

        // 1. 퀘스트 매니저 카운트 증가
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.ProgressActiveQuest(1);
        }

        // 2. ★ 인벤토리 팀원 코드 연동 구역
        // 나중에 팀원의 인벤토리 매니저 클래스가 나오면 주석을 풀고 연결하세요.
        
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.AddItem(itemID);
        }
        

        // 3. UI 닫아주고 오브젝트 파괴
        player.CloseAllUI();
        Destroy(gameObject);
    }
}