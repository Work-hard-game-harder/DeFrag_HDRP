using UnityEngine;

public class GetItem : MonoBehaviour
{
    public ItemData item;

    private void OnTriggerEnter(Collider collision)
    {
        // 충돌한 오브젝트의 태그가 Player인지 확인
        if (collision.CompareTag("Player"))
        {
            InventoryManager.Instance.AddItem(item);

            //맵에서 오브젝트 삭제
            Destroy(gameObject);
        }


        // 4. 데이터 세이브 (인벤토리 및 스탯 백업)
        PlayerStats playerStats = collision.GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            playerStats.SaveData();
        }

        // 획득했으므로 필드에서 아이템 오브젝트 삭제
        Destroy(gameObject);
    }
}
