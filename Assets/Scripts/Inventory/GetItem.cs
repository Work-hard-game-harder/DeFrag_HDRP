using UnityEngine;

public class GetItem : MonoBehaviour, IInteractable
{
    [Header("인벤토리 아이템 설정")]
    public ItemData itemName;
    public string itemID;
    public bool isHoldInteraction = false;

    public string GetInteractionText()
    {
        return isHoldInteraction ? $"{itemName.itemName} 줍기 (E 꾹 누르기)" : $"{itemName.itemName} 줍기 (E)";
    }

    public bool IsHoldInteraction() => isHoldInteraction;

     public void Interact(PlayerInteraction player)
    {
        
        Debug.Log($"[인벤토리 추가] '{itemName}' 수집완료. ID: {itemID}");

        /* //필요시 퀘스트 아이템으로써 증가를 노릴 수도 있다.
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.ProgressActiveQuest(1);
        }
        */
        
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.AddItem(itemID);
        }
        
        PlayerStats playerStats = player.GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            playerStats.SaveData();

        }

        // 3. UI 닫아주고 오브젝트 파괴
        player.CloseAllUI();
        Destroy(gameObject);
    }
}
