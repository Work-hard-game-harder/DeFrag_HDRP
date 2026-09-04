using System.Collections;
using System;
using EasyPeasyFirstPersonController;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerItemDropper : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InventoryUI inventoryUI;
    [Tooltip("Usually the player camera. If empty, this object's transform is used.")]
    [SerializeField] private Transform dropOrigin;

    [Header("Drop and Throw")]
    [SerializeField] private float preferredDistance = 1.25f;
    [SerializeField] private float minimumDistance = 0.45f;
    [SerializeField] private float clearanceRadius = 0.2f;
    [SerializeField] private float throwForce = 8f;
    [SerializeField] private float upwardThrowForce = 1f;
    [SerializeField] private LayerMask blockingLayers = ~0;
    [SerializeField] private float groundSearchDistance = 5f;
    [SerializeField] private float groundClearance = 0.02f;

    [Header("Collision Safety")]
    [SerializeField] private float ignorePlayerCollisionTime = 0.4f;

    private Collider[] playerColliders;
    private WalkieTalkieController walkieTalkieController;

    private void Awake()
    {
        if (dropOrigin == null) dropOrigin = Camera.main != null ? Camera.main.transform : transform;
        if (inventoryUI == null) inventoryUI = FindAnyObjectByType<InventoryUI>();
        playerColliders = transform.root.GetComponentsInChildren<Collider>(true);
        walkieTalkieController = transform.root.GetComponentInChildren<WalkieTalkieController>(true);
    }

    public void Configure(InventoryUI ui, Transform origin)
    {
        inventoryUI = ui;
        if (origin != null) dropOrigin = origin;
        playerColliders = transform.root.GetComponentsInChildren<Collider>(true);
        walkieTalkieController = transform.root.GetComponentInChildren<WalkieTalkieController>(true);
    }

    private void Update()
    {
        if (GameplayInputGate.IsBlocked)
            return;

        if (Keyboard.current == null || inventoryUI == null) return;

        if (Keyboard.current.gKey.wasPressedThisFrame)
            SpawnSelectedItem(false);

        if (Keyboard.current.qKey.wasPressedThisFrame) SpawnSelectedItem(true);
    }

    private bool HasWalkieTalkie()
    {
        if (walkieTalkieController == null)
            walkieTalkieController = transform.root.GetComponentInChildren<WalkieTalkieController>(true);

        return walkieTalkieController != null && walkieTalkieController.HasWalkieTalkie;
    }

    private void SpawnSelectedItem(bool shouldThrow)
    {
        InventoryInfo selectedItem = inventoryUI.GetSelectedItem();
        if (selectedItem?.itemData == null) return;

        if (InventoryManager.Instance != null &&
            InventoryManager.Instance.TryGetNetworkObjectId(
                selectedItem, out ulong networkObjectId))
        {
            RequestNetworkDrop(selectedItem, networkObjectId, shouldThrow);
            return;
        }

        GameObject prefab = selectedItem.itemData.itemPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"[Drop] '{selectedItem.itemData.itemName}'의 ItemData에 Item Prefab이 없습니다.");
            return;
        }

        if (!TryFindSpawnPosition(out Vector3 spawnPosition))
        {
            Debug.Log("[Drop] 앞 공간이 막혀 있어 아이템을 놓을 수 없습니다.");
            return;
        }

        GameObject spawned = Instantiate(prefab, spawnPosition, dropOrigin.rotation);
        SetLayerRecursively(spawned, LayerMask.NameToLayer("Interactable"));

        GetItem pickup = spawned.GetComponent<GetItem>();
        if (pickup == null) pickup = spawned.AddComponent<GetItem>();
        pickup.Configure(selectedItem.itemData);

        Collider[] spawnedColliders = EnsureWorldColliders(spawned);
        foreach (Collider itemCollider in spawnedColliders)
        {
            // A world item needs solid collision so it cannot pass through the floor.
            itemCollider.isTrigger = false;
            itemCollider.enabled = true;
        }

        Physics.SyncTransforms();

        Rigidbody rb = spawned.GetComponent<Rigidbody>();
        if (rb == null) rb = spawned.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        SetPlayerCollisionIgnored(spawnedColliders, true);
        StartCoroutine(RestorePlayerCollisions(spawnedColliders));

        if (shouldThrow)
        {
            Vector3 force = dropOrigin.forward * throwForce + Vector3.up * upwardThrowForce;
            rb.AddForce(force, ForceMode.Impulse);

            ThrownItemSettler settler = spawned.GetComponent<ThrownItemSettler>();
            if (settler == null) settler = spawned.AddComponent<ThrownItemSettler>();
            settler.Initialize(
                rb,
                selectedItem.itemData.impactSound,
                selectedItem.itemData.impactVolume,
                selectedItem.itemData.impactNoiseRadius,
                0.15f);
        }
        else
        {
            PlaceOnGround(spawned.transform, spawnedColliders, rb);
            PlayLocalImpactAndEmitNoise(spawned.transform.position, selectedItem.itemData);
        }

        InventoryManager.Instance.RemoveItem(selectedItem);
    }

    private static void PlayLocalImpactAndEmitNoise(Vector3 position, ItemData itemData)
    {
        if (itemData == null)
            return;

        if (itemData.impactSound != null)
            AudioSource.PlayClipAtPoint(itemData.impactSound, position, itemData.impactVolume);

        WorldNoiseSystem.Emit(position, itemData.impactNoiseRadius);
    }

    private void RequestNetworkDrop(
        InventoryInfo selectedItem,
        ulong networkObjectId,
        bool shouldThrow)
    {
        if (!TryFindSpawnPosition(out Vector3 spawnPosition))
        {
            Debug.Log("[Drop] 앞 공간이 막혀 있어 아이템을 놓을 수 없습니다.");
            return;
        }

        NetworkPlayerInventory networkInventory =
            transform.root.GetComponentInChildren<NetworkPlayerInventory>(true);
        if (networkInventory == null)
        {
            Debug.LogError("[Drop] 로컬 플레이어에 NetworkPlayerInventory가 없습니다.", this);
            return;
        }

        Vector3 velocity = shouldThrow
            ? dropOrigin.forward * throwForce + Vector3.up * upwardThrowForce
            : Vector3.zero;

        float cameraBatteryRatio = -1f;
        if (selectedItem.itemData is CameraItemData &&
            TryGetComponent(out CameraBattery cameraBattery))
        {
            cameraBatteryRatio = cameraBattery.ChargeRatio;
        }

        networkInventory.RequestDrop(
            networkObjectId,
            spawnPosition,
            dropOrigin.rotation,
            velocity,
            cameraBatteryRatio);
    }

    private void PlaceOnGround(Transform itemTransform, Collider[] itemColliders, Rigidbody rb)
    {
        Physics.SyncTransforms();
        Vector3 rayOrigin = itemTransform.position + Vector3.up * 0.5f;
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, groundSearchDistance,
            blockingLayers, QueryTriggerInteraction.Ignore);

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (IsPlayerCollider(hit.collider) || ContainsCollider(itemColliders, hit.collider)) continue;

            Bounds bounds = CalculateBounds(itemColliders, itemTransform.position);
            float bottomOffset = itemTransform.position.y - bounds.min.y;
            Vector3 position = itemTransform.position;
            position.y = hit.point.y + bottomOffset + groundClearance;
            itemTransform.position = position;
            Physics.SyncTransforms();

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            return;
        }

        // No floor was found: keep physics enabled instead of placing inside unknown geometry.
        Debug.LogWarning("[Drop] 내려놓을 바닥을 찾지 못해 물리 낙하로 처리합니다.");
    }

    private static Collider[] EnsureWorldColliders(GameObject item)
    {
        Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
        if (colliders.Length > 0) return colliders;

        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[Drop] '{item.name}'에 Renderer와 Collider가 모두 없습니다.");
            return colliders;
        }

        Bounds localBounds = CalculateLocalRendererBounds(item.transform, renderers);
        BoxCollider generatedCollider = item.AddComponent<BoxCollider>();
        generatedCollider.center = localBounds.center;
        generatedCollider.size = localBounds.size;
        return new Collider[] { generatedCollider };
    }

    private static Bounds CalculateLocalRendererBounds(Transform root, Renderer[] renderers)
    {
        bool initialized = false;
        Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);

        foreach (Renderer renderer in renderers)
        {
            Bounds worldBounds = renderer.bounds;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;

            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                Vector3 worldCorner = new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z);
                Vector3 localCorner = root.InverseTransformPoint(worldCorner);

                if (!initialized)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        return localBounds;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null || layer < 0) return;

        target.layer = layer;
        foreach (Transform child in target.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private static Bounds CalculateBounds(Collider[] colliders, Vector3 fallbackCenter)
    {
        bool initialized = false;
        Bounds bounds = new Bounds(fallbackCenter, Vector3.zero);

        foreach (Collider itemCollider in colliders)
        {
            if (itemCollider == null || !itemCollider.enabled) continue;
            if (!initialized)
            {
                bounds = itemCollider.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(itemCollider.bounds);
            }
        }

        return bounds;
    }

    private static bool ContainsCollider(Collider[] colliders, Collider target)
    {
        foreach (Collider itemCollider in colliders)
            if (itemCollider == target) return true;
        return false;
    }

    private bool TryFindSpawnPosition(out Vector3 spawnPosition)
    {
        Vector3 origin = dropOrigin.position;
        Vector3 direction = dropOrigin.forward.normalized;
        float distance = preferredDistance;

        RaycastHit[] hits = Physics.SphereCastAll(origin, clearanceRadius, direction,
            preferredDistance, blockingLayers, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (!IsPlayerCollider(hit.collider))
                distance = Mathf.Min(distance, hit.distance - clearanceRadius);
        }

        if (distance < minimumDistance)
        {
            spawnPosition = default;
            return false;
        }

        spawnPosition = origin + direction * distance;
        Collider[] overlaps = Physics.OverlapSphere(spawnPosition, clearanceRadius,
            blockingLayers, QueryTriggerInteraction.Ignore);

        foreach (Collider overlap in overlaps)
            if (!IsPlayerCollider(overlap)) return false;

        return true;
    }

    private bool IsPlayerCollider(Collider target)
    {
        if (target == null || playerColliders == null) return false;
        foreach (Collider playerCollider in playerColliders)
            if (playerCollider == target) return true;
        return false;
    }

    private void SetPlayerCollisionIgnored(Collider[] itemColliders, bool ignored)
    {
        if (itemColliders == null || playerColliders == null) return;

        foreach (Collider itemCollider in itemColliders)
        foreach (Collider playerCollider in playerColliders)
            if (itemCollider != null && playerCollider != null)
                Physics.IgnoreCollision(itemCollider, playerCollider, ignored);
    }

    private IEnumerator RestorePlayerCollisions(Collider[] itemColliders)
    {
        yield return new WaitForSeconds(ignorePlayerCollisionTime);
        SetPlayerCollisionIgnored(itemColliders, false);
    }
}

public sealed class ThrownItemSettler : MonoBehaviour
{
    private Rigidbody targetRigidbody;
    private AudioClip impactSound;
    private float impactVolume;
    private float noiseRadius;
    private float settleAfterTime;
    private bool settled;

    public void Initialize(Rigidbody rb, AudioClip clip, float volume, float radius, float delay)
    {
        targetRigidbody = rb;
        impactSound = clip;
        impactVolume = volume;
        noiseRadius = radius;
        settleAfterTime = Time.time + delay;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (settled || targetRigidbody == null || Time.time < settleAfterTime) return;

        foreach (ContactPoint contact in collision.contacts)
        {
            // Stop only on a floor-like surface, not when merely brushing a wall.
            if (contact.normal.y < 0.45f) continue;

            settled = true;

            if (impactSound != null)
                AudioSource.PlayClipAtPoint(impactSound, contact.point, impactVolume);

            WorldNoiseSystem.Emit(contact.point, noiseRadius);

            targetRigidbody.linearVelocity = Vector3.zero;
            targetRigidbody.angularVelocity = Vector3.zero;
            targetRigidbody.isKinematic = true;
            enabled = false;
            return;
        }
    }
}
