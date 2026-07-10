using UnityEngine;
using UnityEngine.InputSystem;

public class InventoryUI : MonoBehaviour
{
    public GameObject inventoryPanel;
    [Header("SlotPanelObject")]
    //[SerializeField] private Transform mainInventoryPanel; (비활성화)
    [SerializeField] private GameObject quickSlotsPanel;

    //public InventorySlot[] inventorySlots;
    public InventorySlot[] quickSlots;
    void Start()
    {
        if (quickSlotsPanel != null)
        {
            quickSlotsPanel.SetActive(true); // 시작할 때 인벤토리 창 끄기
        }

    }
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame)
        {
            quickSlotsPanel.SetActive(!quickSlotsPanel.activeSelf);
        }
    }
    public void UpdateUI()
    {
        // 1. 전체 슬롯 깔끔하게 비우기 (초기화)
        /*for (int i = 0; i < inventorySlots.Length; i++)
        {
            inventorySlots[i].Clear();
        }*/

        /* for (int i = 0; i < quickSlots.Length; i++)
         {
             quickSlots[i].Clear();
         }

         // 2. 인벤토리 리스트에 들어있는 아이템만큼 UI 슬롯에 바인딩
         for (int i = 0; i < InventoryManager.Instance.items.Count; i++)
         {
             InventoryInfo data = InventoryManager.Instance.items[i]; // InventoryItem으로 타입 일치!-->충돌나서 InventoryInfo로 변경

             if (i < inventorySlots.Length)
             {
                 inventorySlots[i].SetItem(data);
             }

             if (i < quickSlots.Length)
             {
                 quickSlots[i].SetItem(data);
             }
         }
     }
        */
        foreach (InventorySlot slot in quickSlots)
        {
            slot.Clear();
        }

        // 아이템 표시
        for (int i = 0; i < InventoryManager.Instance.items.Count && i < quickSlots.Length; i++)
        {
            quickSlots[i].SetItem(InventoryManager.Instance.items[i]);
        }
    }
}