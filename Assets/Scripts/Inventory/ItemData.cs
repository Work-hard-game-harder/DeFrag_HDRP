using UnityEngine;

public enum ItemType
{
    Consumable,
    Key,
    Equipment,
    Clue
}

[CreateAssetMenu(fileName = "New Item", menuName = "Item")]
public class ItemData : ScriptableObject
{
    [Header("기본 정보")]
    public string itemName;
    public Sprite icon;
    public ItemType type;

    [Header("저장용 ID")]
    public string itemID;

    [Header("설명")]
    [TextArea]
    public string description;
}
