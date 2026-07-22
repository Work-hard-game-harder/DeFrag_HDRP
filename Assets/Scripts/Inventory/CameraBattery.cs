using System;
using UnityEngine;

public sealed class CameraBattery : MonoBehaviour
{
    [SerializeField, Min(1f)] private float capacity = 100f;
    [SerializeField] private float charge = 100f;

    public float ChargeRatio => charge / capacity;
    public bool IsEmpty => charge <= 0f;

    public event Action<float> ChargeChanged;

    public bool TryRecharge(float ratio)
    {
        if (ratio <= 0f || charge >= capacity)
            return false;

        charge = Mathf.Min(charge + capacity * ratio, capacity);
        ChargeChanged?.Invoke(ChargeRatio);
        return true;
    }

    public void Drain(float amount)
    {
        if (amount <= 0f)
            return;

        charge = Mathf.Max(0f, charge - amount);
        ChargeChanged?.Invoke(ChargeRatio);
    }
}
