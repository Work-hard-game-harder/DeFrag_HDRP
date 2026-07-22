using UnityEngine;

[CreateAssetMenu(fileName = "New BatteryItemData", menuName = "Inventory/Battery ItemData")]
public sealed class BatteryItemData : ItemData
{
    [SerializeField, Range(0f, 1f)] private float rechargeRatio;

    public float RechargeRatio => rechargeRatio;
}
