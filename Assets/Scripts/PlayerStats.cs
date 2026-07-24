using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 100;

    // 기존 UI와 네트워크 어댑터의 호환성을 위해 public 필드를 유지합니다.
    // 체력 변경은 TakeDamage 또는 ApplyHealth를 통해 처리합니다.
    public int Health = 100;
    public int MaxHealth => maxHealth;
    public bool IsDead { get; private set; }

    public List<string> Inventory = new List<string>();

    public event Action<int, int> HealthChanged;
    public event Action Died;

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
        ApplyHealth(health);
        Inventory = inventory != null
            ? new List<string>(inventory)
            : new List<string>();
    }

    /// <summary>
    /// 플레이어에게 데미지를 적용합니다.
    /// 네트워크 계층은 권한을 가진 측에서만 이 메서드가 호출되도록 보장해야 합니다.
    /// </summary>
    /// <returns>실제로 체력이 감소했으면 true를 반환합니다.</returns>
    public bool TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead)
            return false;

        int previousHealth = Health;
        Health = Mathf.Max(0, Health - amount);

        if (Health == previousHealth)
            return false;

        HealthChanged?.Invoke(previousHealth, Health);

        if (Health == 0)
            Die();

        return true;
    }

    /// <summary>
    /// 층 전환 데이터 복원 또는 서버에서 확정된 체력 동기화에 사용합니다.
    /// </summary>
    public void ApplyHealth(int health)
    {
        int previousHealth = Health;
        bool wasDead = IsDead;

        Health = Mathf.Clamp(health, 0, maxHealth);
        IsDead = Health == 0;

        if (Health != previousHealth)
            HealthChanged?.Invoke(previousHealth, Health);

        if (!wasDead && IsDead)
            Died?.Invoke();
    }

    private void Die()
    {
        if (IsDead)
            return;

        IsDead = true;
        Died?.Invoke();
    }

    public void SaveData()
    {
        //데이터 내용 변경시 매니저에 최종 저장
        /*GameDataManager.Instance.Health = Health;
        GameDataManager.Instance.Inventory = new List<string>(Inventory); */
        UpdateInventoryList();

        //GameDataManager.Instance.Health = Health;
        //GameDataManager.Instance.Inventory = new List<string>(Inventory);
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
