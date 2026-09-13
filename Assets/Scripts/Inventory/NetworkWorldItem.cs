using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public enum NetworkItemState : byte
{
    World,
    Held
}

[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkWorldItem : NetworkBehaviour
{
    public const ulong NoHolder = ulong.MaxValue;

    [Header("Item")]
    [SerializeField] private ItemData itemData;

    [Header("World Components")]
    [SerializeField] private Rigidbody itemRigidbody;
    [SerializeField] private Collider[] worldColliders;
    [SerializeField] private Renderer[] worldRenderers;

    [Header("Drop Placement")]
    [SerializeField, Min(0.1f)] private float groundSearchDistance = 5f;
    [SerializeField, Min(0f)] private float groundClearance = 0.02f;
    [SerializeField, Min(0f)] private float throwAngularSpeed = 4f;

    private readonly NetworkVariable<NetworkItemState> state =
        new NetworkVariable<NetworkItemState>(
            NetworkItemState.World,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> holderClientId =
        new NetworkVariable<ulong>(
            NoHolder,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> cameraBatteryRatio =
        new NetworkVariable<float>(
            1f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private bool impactNoiseArmed;
    private float impactNoiseReadyAt;
    private NetworkTransform networkTransform;
    private Quaternion initialRotationOffset = Quaternion.identity;
    private float groundBottomOffset;

    private const float ImpactArmDelay = 0.1f;

    public ItemData Data => itemData;
    public NetworkItemState State => state.Value;
    public ulong HolderClientId => holderClientId.Value;
    public bool IsAvailable => state.Value == NetworkItemState.World;
    public float CameraBatteryRatio => cameraBatteryRatio.Value;

    private void Reset()
    {
        EnsureWorldComponentReferences();
    }

    public override void OnNetworkSpawn()
    {
        EnsureWorldComponentReferences();
        Quaternion initialYaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        initialRotationOffset = Quaternion.Inverse(initialYaw) * transform.rotation;
        CacheGroundBottomOffset();
        state.OnValueChanged += HandleStateChanged;
        ApplyWorldPresentation(state.Value);

        Debug.Log(
            $"[NetworkWorldItem] Spawned: {name}, " +
            $"Server={IsServer}, State={state.Value}",
            this);
    }

    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= HandleStateChanged;
    }

    [ContextMenu("Server Test: Set Held")]
    private void TestSetHeld()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Play Mode에서 실행해야 합니다.", this);
            return;
        }

        if (!IsSpawned)
        {
            Debug.LogWarning("이 아이템은 아직 네트워크에 Spawn되지 않았습니다.", this);
            return;
        }

        if (!IsServer)
        {
            Debug.LogWarning("이 테스트는 Host/Server에서 실행해야 합니다.", this);
            return;
        }

        SetHeldServer(NetworkManager.ServerClientId);
    }

    [ContextMenu("Server Test: Set World")]
    private void TestSetWorld()
    {
        if (!Application.isPlaying || !IsSpawned || !IsServer)
            return;

        SetWorldServer(transform.position, transform.rotation, Vector3.zero, true);
    }

    public bool SetHeldServer(ulong newHolderClientId)
    {
        if (!IsServer)
        {
            Debug.LogError(
                "[NetworkWorldItem] SetHeldServer는 서버에서만 호출할 수 있습니다.",
                this);
            return false;
        }

        if (state.Value != NetworkItemState.World)
            return false;

        impactNoiseArmed = false;
        holderClientId.Value = newHolderClientId;
        state.Value = NetworkItemState.Held;
        return true;
    }

    public bool SetWorldServer(
        Vector3 position,
        Quaternion rotation,
        Vector3 initialVelocity,
        bool placeOnGround)
    {
        if (!IsServer)
        {
            Debug.LogError(
                "[NetworkWorldItem] SetWorldServer는 서버에서만 호출할 수 있습니다.",
                this);
            return false;
        }

        Quaternion stableRotation =
            Quaternion.Euler(0f, rotation.eulerAngles.y, 0f) * initialRotationOffset;

        transform.SetPositionAndRotation(position, stableRotation);
        Physics.SyncTransforms();

        if (placeOnGround)
            position = FindGroundedPosition(position);

        transform.SetPositionAndRotation(position, stableRotation);

        if (networkTransform != null)
            networkTransform.Teleport(position, stableRotation, transform.localScale);

        holderClientId.Value = NoHolder;
        state.Value = NetworkItemState.World;

        if (itemRigidbody != null)
        {
            itemRigidbody.isKinematic = placeOnGround;
            itemRigidbody.useGravity = !placeOnGround;
            itemRigidbody.linearVelocity = initialVelocity;
            itemRigidbody.angularVelocity = placeOnGround || initialVelocity.sqrMagnitude < 0.01f
                ? Vector3.zero
                : Vector3.Cross(initialVelocity.normalized, Vector3.up) * throwAngularSpeed;
        }

        // Only collisions caused after an explicit player drop/throw produce gameplay noise.
        // This prevents items initially placed in a scene from alerting monsters on startup.
        impactNoiseArmed = true;
        impactNoiseReadyAt = Time.time + ImpactArmDelay;

        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !impactNoiseArmed || state.Value != NetworkItemState.World ||
            itemData == null || Time.time < impactNoiseReadyAt || collision.contactCount == 0)
        {
            return;
        }

        if (collision.relativeVelocity.magnitude < Mathf.Max(0f, itemData.minimumImpactSpeed))
            return;

        impactNoiseArmed = false;
        Vector3 impactPosition = collision.GetContact(0).point;

        // B2F monster decisions are server-authoritative. Clients only receive the sound effect.
        WorldNoiseSystem.Emit(impactPosition, itemData.impactNoiseRadius);
        PlayImpactSoundClientRpc(impactPosition);
    }

    [ClientRpc]
    private void PlayImpactSoundClientRpc(Vector3 impactPosition)
    {
        if (itemData != null && itemData.impactSound != null)
        {
            AudioSource.PlayClipAtPoint(
                itemData.impactSound,
                impactPosition,
                itemData.impactVolume);
        }
    }

    public void SetCameraBatteryRatioServer(float ratio)
    {
        if (!IsServer)
        {
            Debug.LogError(
                "[NetworkWorldItem] 배터리 상태는 서버에서만 변경할 수 있습니다.",
                this);
            return;
        }

        if (itemData is CameraItemData)
            cameraBatteryRatio.Value = Mathf.Clamp01(ratio);
    }

    private void HandleStateChanged(
        NetworkItemState previousValue,
        NetworkItemState newValue)
    {
        ApplyWorldPresentation(newValue);
    }

    private void ApplyWorldPresentation(NetworkItemState currentState)
    {
        bool isInWorld = currentState == NetworkItemState.World;

        if (worldRenderers != null)
        {
            foreach (Renderer targetRenderer in worldRenderers)
            {
                if (targetRenderer != null)
                    targetRenderer.enabled = isInWorld;
            }
        }

        if (worldColliders != null)
        {
            foreach (Collider targetCollider in worldColliders)
            {
                if (targetCollider != null)
                    targetCollider.enabled = isInWorld;
            }
        }

        if (itemRigidbody != null)
        {
            bool serverSimulatesWorldPhysics =
                isInWorld && IsServer;

            itemRigidbody.isKinematic = !serverSimulatesWorldPhysics;
            itemRigidbody.useGravity = serverSimulatesWorldPhysics;

            if (!serverSimulatesWorldPhysics)
            {
                itemRigidbody.linearVelocity = Vector3.zero;
                itemRigidbody.angularVelocity = Vector3.zero;
            }
        }
    }

    private void EnsureWorldComponentReferences()
    {
        if (networkTransform == null)
            networkTransform = GetComponent<NetworkTransform>();

        if (itemRigidbody == null)
            itemRigidbody = GetComponent<Rigidbody>();

        if (worldColliders == null || worldColliders.Length == 0)
            worldColliders = GetComponentsInChildren<Collider>(true);

        if (worldRenderers == null || worldRenderers.Length == 0)
            worldRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private Vector3 FindGroundedPosition(Vector3 requestedPosition)
    {
        Vector3 rayOrigin = requestedPosition + Vector3.up * 0.5f;
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            groundSearchDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            if (IsWorldItemCollider(hit.collider) || IsHolderCollider(hit.collider))
                continue;

            requestedPosition.y = hit.point.y + groundBottomOffset + groundClearance;
            return requestedPosition;
        }

        return requestedPosition;
    }

    private void CacheGroundBottomOffset()
    {
        bool initialized = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);

        Physics.SyncTransforms();

        foreach (Collider itemCollider in worldColliders)
        {
            if (itemCollider == null)
                continue;

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

        groundBottomOffset = initialized
            ? Mathf.Max(0f, transform.position.y - bounds.min.y)
            : 0f;
    }

    private bool IsWorldItemCollider(Collider candidate)
    {
        if (candidate == null)
            return false;

        foreach (Collider itemCollider in worldColliders)
        {
            if (candidate == itemCollider)
                return true;
        }

        return false;
    }

    private bool IsHolderCollider(Collider candidate)
    {
        if (candidate == null || holderClientId.Value == NoHolder || NetworkManager == null)
            return false;

        NetworkObject holder = NetworkManager.SpawnManager.GetPlayerNetworkObject(
            holderClientId.Value);
        return holder != null && candidate.transform.IsChildOf(holder.transform);
    }
}
