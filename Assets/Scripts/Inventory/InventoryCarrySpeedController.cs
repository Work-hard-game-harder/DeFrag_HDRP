using StarterAssets;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkPlayerInventory))]
public sealed class InventoryCarrySpeedController : MonoBehaviour
{
    private NetworkPlayerInventory inventory;
    private PersonController movement;
    private float baseMoveSpeed;
    private float baseSprintSpeed;
    private bool initialized;

    private void Awake()
    {
        inventory = GetComponent<NetworkPlayerInventory>();
        movement = GetComponent<PersonController>();
    }

    private void OnEnable()
    {
        if (inventory == null)
            inventory = GetComponent<NetworkPlayerInventory>();
        if (movement == null)
            movement = GetComponent<PersonController>();

        if (movement != null && !initialized)
        {
            baseMoveSpeed = movement.MoveSpeed;
            baseSprintSpeed = movement.SprintSpeed;
            initialized = true;
        }

        if (inventory != null)
            inventory.HeldItemsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null)
            inventory.HeldItemsChanged -= Refresh;
        ApplyMultiplier(1f);
    }

    private void Refresh()
    {
        if (inventory == null || !inventory.IsOwner)
            return;
        ApplyMultiplier(inventory.GetHeldMovementMultiplier());
    }

    private void ApplyMultiplier(float multiplier)
    {
        if (!initialized || movement == null)
            return;

        float safeMultiplier = Mathf.Clamp(multiplier, 0.1f, 1f);
        movement.MoveSpeed = baseMoveSpeed * safeMultiplier;
        movement.SprintSpeed = baseSprintSpeed * safeMultiplier;
    }
}
