using System.Collections.Generic;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    public int Health;
    public List<string> Inventory = new List<string>();

    private void Start()
    {
        if(GameDataManager.Instance != null)
        {
            ApplyData(GameDataManager.Instance.Health, GameDataManager.Instance.Inventory); //게임 시작시 게임데이터 매니저에서 데이터를 불러옴
            Debug.Log($"[PlayerStats] Loaded Health: {Health}, Inventory: {string.Join(", ", Inventory)}");

        }

        if(InventoryManager.Instance != null)
        {
            InventoryManager.Instance.items.Clear(); // 기존 아이템 리스트 초기화
            foreach (string itemId in Inventory)
            {
                InventoryManager.Instance.AddItem(itemId); // 인벤토리에 아이템 추가
            }
        }

    }
    public void ApplyData(int health, List<string> inventory)
    {
        Health = health;
        Inventory = new List<string>(inventory);
    }

    public void SaveData()
    {
        //데이터 내용 변경시 매니저에 최종 저장
        /*GameDataManager.Instance.Health = Health;
        GameDataManager.Instance.Inventory = new List<string>(Inventory); */
        UpdateInventoryList();

        GameDataManager.Instance.Health = Health;
        GameDataManager.Instance.Inventory = new List<string>(Inventory);
        Debug.Log("GameDataManager에 로드 성공");
    }
    public void UpdateInventoryList()
    {
        if (InventoryManager.Instance == null) return;

        Inventory.Clear(); // 싹 비우고 최신 데이터로 재배치
        foreach (InventoryInfo info in InventoryManager.Instance.items)
        {
            if (info.itemData != null)
            {
                // 아이템 개수(count)만큼 문자열 리스트에 아이템 이름을 똑같이 더해줌
                for (int i = 0; i < info.count; i++)
                {
                    Inventory.Add(info.itemData.itemID);
                }
            }
        }
    }
    private void OnDestroy()
    {
        // 씬 전환이 일어날 때 자동으로 백업하기
        SaveData();
    }
}
