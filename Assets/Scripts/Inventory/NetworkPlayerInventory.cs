using System.Collections;
using System;
using DeFrag.B1F;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkPlayerInventory : NetworkBehaviour
{
    [Header("Server Validation")]
    [SerializeField, Min(1)] private int maximumSlots = 4;
    [SerializeField, Min(0.1f)] private float maximumPickupDistance = 7f;
    [SerializeField, Min(0.1f)] private float maximumDropDistance = 2.5f;
    [SerializeField, Min(0.1f)] private float maximumThrowSpeed = 12f;

    private NetworkList<ulong> heldItemIds;

    public event Action HeldItemsChanged;

    private void Awake()
    {
        heldItemIds = new NetworkList<ulong>();
    }

    public override void OnNetworkSpawn()
    {
        heldItemIds.OnListChanged += HandleHeldItemsChanged;

        if (IsOwner)
        {
            if (GetComponent<InventoryCarrySpeedController>() == null)
                gameObject.AddComponent<InventoryCarrySpeedController>();
            StartCoroutine(SynchronizeWhenLocalInventoryIsReady());
        }
    }

    public override void OnNetworkDespawn()
    {
        heldItemIds.OnListChanged -= HandleHeldItemsChanged;
    }

    public void RequestPickup(NetworkWorldItem item)
    {
        if (!IsOwner || item == null || !item.IsSpawned)
            return;

        RequestPickupServerRpc(item.NetworkObjectId);
    }

    public void RequestDrop(
        ulong itemNetworkObjectId,
        Vector3 requestedPosition,
        Quaternion requestedRotation,
        Vector3 requestedVelocity,
        float cameraBatteryRatio = -1f)
    {
        if (!IsOwner || itemNetworkObjectId == 0)
            return;

        RequestDropServerRpc(
            itemNetworkObjectId,
            requestedPosition,
            requestedRotation,
            requestedVelocity,
            cameraBatteryRatio);
    }

    [ServerRpc]
    private void RequestPickupServerRpc(ulong itemNetworkObjectId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
                itemNetworkObjectId, out NetworkObject itemObject))
            return;

        NetworkWorldItem item = itemObject.GetComponent<NetworkWorldItem>();
        if (item == null || !item.IsAvailable)
            return;

        if (Vector3.Distance(transform.position, item.transform.position) >
            maximumPickupDistance)
            return;

        if (!CanAddServer(item.Data))
            return;

        if (!item.SetHeldServer(OwnerClientId))
            return;

        heldItemIds.Add(itemNetworkObjectId);
    }

    [ServerRpc]
    private void RequestDropServerRpc(
        ulong itemNetworkObjectId,
        Vector3 requestedPosition,
        Quaternion requestedRotation,
        Vector3 requestedVelocity,
        float cameraBatteryRatio)
    {
        int heldIndex = FindHeldItemIndex(itemNetworkObjectId);
        if (heldIndex < 0)
            return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
                itemNetworkObjectId, out NetworkObject itemObject))
            return;

        NetworkWorldItem item = itemObject.GetComponent<NetworkWorldItem>();
        if (item == null || item.HolderClientId != OwnerClientId)
            return;

        Vector3 playerPosition = transform.position;
        if (Vector3.Distance(playerPosition, requestedPosition) > maximumDropDistance)
            requestedPosition = playerPosition + transform.forward * 1.25f + Vector3.up * 0.5f;

        if (requestedVelocity.magnitude > maximumThrowSpeed)
            requestedVelocity = requestedVelocity.normalized * maximumThrowSpeed;

        if (item.Data is CameraItemData && cameraBatteryRatio >= 0f)
            item.SetCameraBatteryRatioServer(cameraBatteryRatio);

        if (!item.SetWorldServer(
                requestedPosition,
                requestedRotation,
                requestedVelocity))
            return;

        heldItemIds.RemoveAt(heldIndex);
    }

    private bool CanAddServer(ItemData itemData)
    {
        if (itemData == null)
            return false;

        int occupiedSlots = 0;
        bool sameTypeAlreadyHeld = false;

        for (int i = 0; i < heldItemIds.Count; i++)
        {
            if (!TryResolveItem(heldItemIds[i], out NetworkWorldItem heldItem))
                continue;

            if (heldItem.Data == itemData)
            {
                sameTypeAlreadyHeld = true;
                continue;
            }

            bool counted = false;
            for (int earlier = 0; earlier < i; earlier++)
            {
                if (TryResolveItem(heldItemIds[earlier], out NetworkWorldItem earlierItem) &&
                    earlierItem.Data == heldItem.Data)
                {
                    counted = true;
                    break;
                }
            }

            if (!counted)
                occupiedSlots++;
        }

        return sameTypeAlreadyHeld || occupiedSlots < maximumSlots;
    }

    private int FindHeldItemIndex(ulong itemNetworkObjectId)
    {
        for (int i = 0; i < heldItemIds.Count; i++)
        {
            if (heldItemIds[i] == itemNetworkObjectId)
                return i;
        }

        return -1;
    }

    private bool TryResolveItem(ulong itemNetworkObjectId, out NetworkWorldItem item)
    {
        item = null;
        return NetworkManager != null &&
               NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
                   itemNetworkObjectId, out NetworkObject itemObject) &&
               itemObject.TryGetComponent(out item);
    }

    public float GetHeldMovementMultiplier()
    {
        float multiplier = 1f;
        for (int i = 0; i < heldItemIds.Count; i++)
        {
            if (!TryResolveItem(heldItemIds[i], out NetworkWorldItem item))
                continue;

            GeneratorFuelCan fuelCan = item.GetComponent<GeneratorFuelCan>();
            if (fuelCan != null)
                multiplier = Mathf.Min(multiplier, fuelCan.MovementMultiplier);
        }

        return multiplier;
    }

    public bool ContainsHeldItem(ItemData itemData)
    {
        if (itemData == null)
            return false;

        for (int i = 0; i < heldItemIds.Count; i++)
        {
            if (TryResolveItem(heldItemIds[i], out NetworkWorldItem item) &&
                item.Data == itemData)
                return true;
        }

        return false;
    }

    public bool TryConsumeHeldItemServer(ulong itemNetworkObjectId)
    {
        if (!IsServer)
        {
            Debug.LogError("[NetworkInventory] Held item consumption is server-only.", this);
            return false;
        }

        int heldIndex = FindHeldItemIndex(itemNetworkObjectId);
        if (heldIndex < 0 ||
            !TryResolveItem(itemNetworkObjectId, out NetworkWorldItem item) ||
            item.HolderClientId != OwnerClientId)
            return false;

        heldItemIds.RemoveAt(heldIndex);
        if (item.NetworkObject != null && item.NetworkObject.IsSpawned)
            item.NetworkObject.Despawn(true);
        return true;
    }

    private void HandleHeldItemsChanged(NetworkListEvent<ulong> change)
    {
        if (!IsOwner)
            return;

        if (InventoryManager.Instance != null)
        {
            switch (change.Type)
            {
                case NetworkListEvent<ulong>.EventType.Add:
                    AddLocalNetworkItem(change.Value, true);
                    break;

                case NetworkListEvent<ulong>.EventType.Remove:
                case NetworkListEvent<ulong>.EventType.RemoveAt:
                    InventoryManager.Instance.RemoveNetworkItem(change.Value);
                    break;

                default:
                    SynchronizeLocalInventory();
                    break;
            }
        }

        HeldItemsChanged?.Invoke();
    }

    private IEnumerator SynchronizeWhenLocalInventoryIsReady()
    {
        while (IsSpawned && InventoryManager.Instance == null)
            yield return null;

        if (IsSpawned && IsOwner)
        {
            SynchronizeLocalInventory();
            HeldItemsChanged?.Invoke();
        }
    }

    private void SynchronizeLocalInventory()
    {
        if (!IsOwner || InventoryManager.Instance == null)
            return;

        InventoryManager.Instance.ClearNetworkItems();
        for (int i = 0; i < heldItemIds.Count; i++)
            AddLocalNetworkItem(heldItemIds[i], false);
    }

    private void AddLocalNetworkItem(ulong itemNetworkObjectId, bool presentPickupFeedback)
    {
        if (!TryResolveItem(itemNetworkObjectId, out NetworkWorldItem item) ||
            item.Data == null)
            return;

        if (!InventoryManager.Instance.AddNetworkItem(item.Data, itemNetworkObjectId))
            return;

        if (!presentPickupFeedback)
            return;

        PlayerInteraction interaction = GetComponentInChildren<PlayerInteraction>(true);
        item.GetComponent<GetItem>()?.CompleteNetworkPickupPresentation(interaction);
    }
}
