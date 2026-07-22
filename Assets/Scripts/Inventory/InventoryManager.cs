using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class InventoryInfo
{
    public ItemData itemData;
    public int count;
}

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance;

    [Header("Inventory")]
    [Min(1)] public int maxSlots = 4;
    public InventoryUI inventoryUI;
    public List<InventoryInfo> items = new List<InventoryInfo>();

    [Header("Item Database")]
    public List<ItemData> itemDatabase = new List<ItemData>();

    private void Awake() => Instance = this;

    public bool AddItem(ItemData item)
    {
        if (item == null)
        {
            Debug.LogWarning("[Inventory] null ItemData는 추가할 수 없습니다.");
            return false;
        }

        InventoryInfo existingItem = items.Find(info => info.itemData == item);
        if (existingItem != null)
        {
            existingItem.count++;
            RefreshInventory();
            return true;
        }

        if (items.Count >= maxSlots)
        {
            Debug.Log($"[Inventory] 슬롯이 가득 차서 '{item.itemName}'을(를) 획득하지 못했습니다.");
            return false;
        }

        items.Add(new InventoryInfo { itemData = item, count = 1 });
        RefreshInventory();
        return true;
    }

    public bool AddItem(string id)
    {
        ItemData foundItem = itemDatabase.Find(data => data != null && data.itemID == id);
        if (foundItem == null)
        {
            Debug.LogWarning($"[Inventory] ID '{id}'에 해당하는 ItemData를 찾을 수 없습니다.");
            return false;
        }

        return AddItem(foundItem);
    }

    public bool RemoveItem(InventoryInfo item)
    {
        if (item == null || !items.Contains(item)) return false;

        item.count--;
        if (item.count <= 0) items.Remove(item);

        RefreshInventory();
        return true;
    }

    public bool ContainsItemOfType<T>() where T : ItemData
    {
        return items.Exists(item => item.itemData is T);
    }

    private void RefreshInventory()
    {
        inventoryUI?.UpdateUI();
        PlayerStats playerStats = FindAnyObjectByType<PlayerStats>();
        playerStats?.UpdateInventoryList();
    }
}
