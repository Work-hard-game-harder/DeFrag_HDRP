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
    [SerializeField] private TMP_Text countText;
    [SerializeField] private GameObject selectedFrame;
    [SerializeField] private Color selectedOutlineColor = Color.red;
    [SerializeField] private Vector2 selectedOutlineDistance = new Vector2(3f, -3f);

    private Outline selectionOutline;

    private void Awake()
    {
        ConfigureDisplayGraphics();

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
            if (countImage != null)
            {
                countImage.gameObject.SetActive(false);
                countImage.sprite = null;
            }
            if (countText != null) countText.gameObject.SetActive(false);
        }
        else
        {
            if (countImage != null && numberSprites != null && numberSprites.Length > 0)
            {
                countImage.gameObject.SetActive(true);
                countImage.color = Color.white;
                int index = Mathf.Clamp(item.count - 1, 0, numberSprites.Length - 1);
                countImage.sprite = numberSprites[index];
            }
            if (countText != null)
            {
                countText.gameObject.SetActive(true);
                countText.text = item.count.ToString();
            }
        }
    }

    public void Clear()
    {
        icon.sprite = null;
        currentItem = null;
        icon.color = new Color(1, 1, 1, 0); // 완전 투명화

        if (countImage != null)
        {
            countImage.gameObject.SetActive(false);
            countImage.sprite = null;
        }
        if (countText != null) countText.gameObject.SetActive(false);
}

    public void Configure(Image itemIcon, TMP_Text itemCountText)
    {
        icon = itemIcon;
        countText = itemCountText;
        ConfigureDisplayGraphics();
    }

    private void ConfigureDisplayGraphics()
    {
        // The slot's root Button/Graphic owns pointer input. These child graphics are
        // visual-only and must not intercept dropdown or other modal-menu raycasts.
        if (icon != null) icon.raycastTarget = false;
        if (countImage != null) countImage.raycastTarget = false;
        if (countText != null) countText.raycastTarget = false;
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



   
