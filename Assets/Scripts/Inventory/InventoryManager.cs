using System.Collections.Generic;
using UnityEngine;

// 데이터 구조 통일
[System.Serializable]
public class InventoryInfo
{
    public ItemData itemData;
    public int count;
}

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance;
    public InventoryUI inventoryUI;

    public List<InventoryInfo> items = new List<InventoryInfo>();

    [Header("추후 매니저에서 관리할 수 있도록 아이템 데이터 베이스 생성")]
    public List<ItemData> itemDatabase = new List<ItemData>();


    private void Awake()
    {
        Instance = this;
    }

    public void AddItem(ItemData item)
    {
        Debug.Log("획득 : " + item.itemName);
        foreach (InventoryInfo Info in items)
        {
            if (Info.itemData==item)
            {
                Info.count++;
                inventoryUI.UpdateUI();
                SyncPlayerStats();
                return;
            }
        }

        InventoryInfo newItem = new InventoryInfo();
        newItem.itemData = item;
        newItem.count = 1;
        items.Add(newItem);

        inventoryUI.UpdateUI();
        SyncPlayerStats();
    }
    public void AddItem(string id)
    {
        // 1. 데이터베이스(혹은 매니저)에서 string id와 이름이 일치하는 ItemData를 조회함
        ItemData foundItem = FindItemById(id);

        if (foundItem != null)
        {
            // 2. 찾았다면 기존에 잘 만들어둔 AddItem(ItemData item)을 재활용해서 인벤토리에 장착!
            AddItem(foundItem);
        }
        else
        {
            Debug.LogWarning($"[Inventory] 데이터베이스에서 ID '{id}'에 해당하는 아이템을 찾을 수 없습니다.");
        }
    }
    // 오브젝트 자체를 넘겨받아 삭제할 수 있도록 수정
    public void RemoveItem(InventoryInfo item)
    {
        if (items.Contains(item))
        {
            item.count--;
            if (item.count <= 0)
            {
                items.Remove(item);
            }
            inventoryUI.UpdateUI();
        }
    }
    private ItemData FindItemById(string id)
    {
        foreach (ItemData data in itemDatabase)
        {
            // 여기서는 itemData의 itemName이나 에셋 이름을 비교해
            if (data != null && data.itemID == id)
            {
                return data;
            }
        }
        return null;
    }

    private void SyncPlayerStats()
    {
        PlayerStats playerStats = FindAnyObjectByType<PlayerStats>();
        if (playerStats != null)
        {
            playerStats.UpdateInventoryList();
        }
    }
}
