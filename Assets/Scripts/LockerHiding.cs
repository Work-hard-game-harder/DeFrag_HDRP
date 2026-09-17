using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum LockerPhase : byte { Empty, Entering, Hidden, Exiting }

/// <summary>Server-owned occupancy. Player movement remains on its existing owning client.</summary>
[RequireComponent(typeof(NetworkObject))]
[DisallowMultipleComponent]
public sealed class LockerHiding : NetworkBehaviour, IInteractable
{
    private const ulong Nobody = ulong.MaxValue;
    private static readonly HashSet<LockerHiding> lockers = new();
    [Header("Anchors (feet / eyes / safe exit feet)")]
    [SerializeField] private Transform insideAnchor;
    [SerializeField] private Transform viewAnchor;
    [SerializeField] private Transform exitAnchor;
    [Header("Door pivot (local Z rotation)")]
    [SerializeField] private Transform doorPivot;
    [SerializeField] private float doorOpenZ = -140f;
    [SerializeField] private float doorClosedZ = 0f;
    [SerializeField, Min(0.05f)] private float doorOpenDuration = 0.25f;
    [SerializeField, Min(0.05f)] private float doorCloseDuration = 0.3f;
    [SerializeField, Range(0f, 0.15f)] private float hiddenDoorAjar = 0.035f;
    [SerializeField, Range(0f, 0.75f)] private float movementDoorLead = 0.25f;
    private Quaternion doorClosedRotation;
    private Quaternion doorOpenRotation;
    private float doorBlend;
    [Header("Timing")]
    [SerializeField, Min(0.1f)] private float enterDuration = 1.2f;
    [SerializeField, Min(0.1f)] private float exitDuration = 1.1f;
    [SerializeField, Min(0.5f)] private float useDistance = 3f;
    [Header("Interaction validation")]
    [Tooltip("Distance is measured from this collider's surface instead of the imported model pivot.")]
    [SerializeField] private Collider interactionCollider;
    [Header("Remote player Animator full state paths")]
    [SerializeField] private string enterState = "Base Layer.LockerEnter";
    [SerializeField] private string hiddenState = "Base Layer.LockerHidden";
    [SerializeField] private string exitState = "Base Layer.LockerExit";
    [SerializeField] private string returnState = "Base Layer.Idle Walk Run Blend";
    [Header("First person motion")]
    [SerializeField] private float cameraBob = 0.04f;
    [SerializeField] private float cameraRoll = 3f;
    private readonly NetworkVariable<ulong> occupant = new(Nobody);
    private readonly NetworkVariable<LockerPhase> phase = new(LockerPhase.Empty);
    private readonly NetworkVariable<double> phaseEnd = new();
    private LockerLocalSession session;
    private Animator remoteAnimator;
    private LockerPhase displayedPhase;
    private bool hasDisplayed;
    public LockerPhase Phase => phase.Value;
    public float Duration => Phase == LockerPhase.Exiting ? exitDuration : enterDuration;
    public float Progress => Mathf.Clamp01(1f - (float)(phaseEnd.Value - NetworkManager.ServerTime.Time) / Duration);
    public Transform Inside => insideAnchor;
    public Transform View => viewAnchor;
    public Transform Exit => exitAnchor;
    public float Bob => cameraBob;
    public float Roll => cameraRoll;
    public float MovementDoorLead => movementDoorLead;

    public static bool IsPlayerHidden(NetworkObject player)
    {
        foreach (var locker in lockers)
            if (locker != null && locker.IsSpawned && locker.Phase == LockerPhase.Hidden &&
                locker.occupant.Value == player.NetworkObjectId) return true;
        return false;
    }
    public string GetInteractionText() => Phase == LockerPhase.Empty
        ? "락커에 숨기 (E 꾹 누르기)" : "이미 누가 있는 듯 하다";
    public bool IsHoldInteraction() => Phase == LockerPhase.Empty;
    public void Interact(PlayerInteraction player)
    {
        if (IsSpawned && Phase == LockerPhase.Empty && !GameplayInputGate.IsBlocked) EnterServerRpc();
    }
    private void Awake()
    {
        if (doorPivot != null)
        {
            Vector3 restEuler = doorPivot.localEulerAngles;
            doorClosedRotation = Quaternion.Euler(restEuler.x, restEuler.y, doorClosedZ);
            doorOpenRotation = Quaternion.Euler(restEuler.x, restEuler.y, doorOpenZ);
        }
        if (interactionCollider == null)
            interactionCollider = GetComponentInChildren<Collider>(true);
    }
    public override void OnNetworkSpawn()
    {
        lockers.Add(this);
        SetDoorImmediate(GetDoorTargetBlend());
    }
    public override void OnNetworkDespawn()
    {
        lockers.Remove(this);
        session?.Finish();
        session = null;
        SetDoorImmediate(0f);
        PlayState(returnState);
    }
    [ServerRpc(RequireOwnership = false)]
    private void EnterServerRpc(ServerRpcParams rpc = default)
    {
        if (Phase != LockerPhase.Empty || insideAnchor == null || viewAnchor == null) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(rpc.Receive.SenderClientId, out var client)) return;
        NetworkObject player = client.PlayerObject;
        if (player == null || !player.IsSpawned || !IsPlayerWithinUseDistance(player.transform.position)) return;
        if (player.TryGetComponent<PlayerStats>(out var stats) && stats.IsDead) return;
        foreach (var locker in lockers)
            if (locker != null && locker.occupant.Value == player.NetworkObjectId) return;
        occupant.Value = player.NetworkObjectId;
        phaseEnd.Value = NetworkManager.ServerTime.Time + enterDuration;
        phase.Value = LockerPhase.Entering;
    }

    private bool IsPlayerWithinUseDistance(Vector3 playerPosition)
    {
        // Imported Blender/FBX locker roots can have a large scale and a pivot far
        // away from the visible door. Validate against the surface the player
        // actually aimed at so entry does not depend on standing at one exact spot.
        if (interactionCollider != null && interactionCollider.enabled &&
            interactionCollider.gameObject.activeInHierarchy)
        {
            Vector3 closestPoint = interactionCollider.ClosestPoint(playerPosition);
            return Vector3.Distance(playerPosition, closestPoint) <= useDistance;
        }

        return Vector3.Distance(playerPosition, transform.position) <= useDistance;
    }
    public void RequestExit() { if (IsSpawned) ExitServerRpc(); }
    [ServerRpc(RequireOwnership = false)]
    private void ExitServerRpc(ServerRpcParams rpc = default)
    {
        if (Phase != LockerPhase.Hidden || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(occupant.Value, out var player) ||
            player.OwnerClientId != rpc.Receive.SenderClientId) return;
        phaseEnd.Value = NetworkManager.ServerTime.Time + exitDuration;
        phase.Value = LockerPhase.Exiting;
    }
    public void CancelLocalEntry() { if (IsSpawned) CancelServerRpc(); }
    [ServerRpc(RequireOwnership = false)]
    private void CancelServerRpc(ServerRpcParams rpc = default)
    {
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(occupant.Value, out var player) &&
            player.OwnerClientId == rpc.Receive.SenderClientId) ClearOccupant();
    }
    private void ClearOccupant() { phase.Value = LockerPhase.Empty; occupant.Value = Nobody; }
    private void Update()
    {
        if (!IsSpawned) return;
        NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(occupant.Value, out var player);
        if (IsServer && Phase != LockerPhase.Empty)
        {
            if (player == null || !NetworkManager.ConnectedClients.ContainsKey(player.OwnerClientId) ||
                (player.TryGetComponent<PlayerStats>(out var stats) && stats.IsDead)) ClearOccupant();
            else if (NetworkManager.ServerTime.Time >= phaseEnd.Value)
            {
                if (Phase == LockerPhase.Entering) phase.Value = LockerPhase.Hidden;
                else if (Phase == LockerPhase.Exiting) ClearOccupant();
            }
        }
        if (Phase == LockerPhase.Empty)
        {
            session?.Finish(); session = null;
            UpdateDoorAnimation();
            if (hasDisplayed && displayedPhase != LockerPhase.Empty) PlayState(returnState);
            displayedPhase = LockerPhase.Empty; hasDisplayed = true;
            return;
        }
        // Apply on every peer before starting character/camera presentation.
        // Hidden closes after entry; Empty closes after the local exit is restored.
        UpdateDoorAnimation();
        if (player == null) return;
        if (player.IsOwner && session == null)
        {
            session = player.gameObject.AddComponent<LockerLocalSession>();
            if (!session.Begin(this, player)) { Destroy(session); session = null; CancelLocalEntry(); return; }
        }
        if (!player.IsOwner) remoteAnimator = player.GetComponent<Animator>();
        if (!hasDisplayed || displayedPhase != Phase)
        {
            displayedPhase = Phase; hasDisplayed = true;
            string requestedState = Phase == LockerPhase.Entering
                ? enterState
                : Phase == LockerPhase.Hidden ? hiddenState : exitState;
            if (!PlayState(requestedState) && Phase == LockerPhase.Hidden)
                PlayState(returnState);
        }
    }
    private float GetDoorTargetBlend()
    {
        if (Phase == LockerPhase.Entering || Phase == LockerPhase.Exiting)
            return 1f;
        return Phase == LockerPhase.Hidden ? hiddenDoorAjar : 0f;
    }

    private void UpdateDoorAnimation()
    {
        if (doorPivot == null) return;
        float target = GetDoorTargetBlend();
        float duration = target > doorBlend ? doorOpenDuration : doorCloseDuration;
        doorBlend = Mathf.MoveTowards(doorBlend, target, Time.deltaTime / Mathf.Max(0.01f, duration));
        doorPivot.localRotation = Quaternion.Slerp(doorClosedRotation, doorOpenRotation, doorBlend);
    }

    private void SetDoorImmediate(float blend)
    {
        doorBlend = Mathf.Clamp01(blend);
        if (doorPivot != null)
            doorPivot.localRotation = Quaternion.Slerp(doorClosedRotation, doorOpenRotation, doorBlend);
    }
    private bool PlayState(string state)
    {
        if (remoteAnimator == null || string.IsNullOrWhiteSpace(state)) return false;
        int hash = Animator.StringToHash(state);
        if (!remoteAnimator.HasState(0, hash)) return false;
        remoteAnimator.CrossFadeInFixedTime(hash, 0.1f);
        return true;
    }
}
