using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlot : MonoBehaviour
{
    private InventoryInfo currentItem; // InventoryItem--> InventoryInfo로 변경(충돌남)
    public Image icon;
    public Image countImage;
    public Sprite[] numberSprites;
    [SerializeField] private GameObject selectedFrame;
    [SerializeField] private Color selectedOutlineColor = Color.red;
    [SerializeField] private Vector2 selectedOutlineDistance = new Vector2(3f, -3f);

    private Outline selectionOutline;

    private void Awake()
    {
        if (selectedFrame == null)
        {
            Graphic slotGraphic = GetComponent<Graphic>();
            if (slotGraphic != null)
            {
                selectionOutline = GetComponent<Outline>();
                if (selectionOutline == null) selectionOutline = gameObject.AddComponent<Outline>();

                selectionOutline.effectColor = selectedOutlineColor;
                selectionOutline.effectDistance = selectedOutlineDistance;
                selectionOutline.useGraphicAlpha = false;
                selectionOutline.enabled = false;
            }
        }
    }

    public void SetItem(InventoryInfo item)
    {
        this.currentItem = item;

        // 데이터가 없으면 슬롯 비우기
        if (item == null || item.itemData == null)
        {
            Clear();
            return;
        }

        icon.sprite = item.itemData.icon;
        icon.color = Color.white; // 알파 켜기

        if (item.count <= 1)
        {
            countImage.gameObject.SetActive(false);
            countImage.sprite = null;
        }
        else
        {
            countImage.gameObject.SetActive(true);
            countImage.color = Color.white;
            int index = Mathf.Clamp(item.count - 1, 0, numberSprites.Length - 1);
            countImage.sprite = numberSprites[index];
        }
    }

    public void Clear()
    {
        icon.sprite = null;
        currentItem = null;
        icon.color = new Color(1, 1, 1, 0); // 완전 투명화

        countImage.gameObject.SetActive(false);
        countImage.sprite = null;
}

    public void SetSelected(bool selected)
    {
        if (selectedFrame != null)
        {
            selectedFrame.SetActive(selected);
        }
        else if (selectionOutline != null)
        {
            selectionOutline.enabled = selected;
        }
    }

    // Existing slot Button events already call this method.
    public void UseItem()
    {
        InventoryUI inventoryUI = FindAnyObjectByType<InventoryUI>();
        inventoryUI?.SelectSlot(this);
    }
}



   
