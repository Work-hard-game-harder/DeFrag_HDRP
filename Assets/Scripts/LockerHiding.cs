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
    private Vector3 doorRestEuler;
    [Header("Timing")]
    [SerializeField, Min(0.1f)] private float enterDuration = 1.2f;
    [SerializeField, Min(0.1f)] private float exitDuration = 1.1f;
    [SerializeField, Min(0.5f)] private float useDistance = 3f;
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
        if (doorPivot != null) doorRestEuler = doorPivot.localEulerAngles;
    }
    public override void OnNetworkSpawn()
    {
        lockers.Add(this);
        ApplyDoor(Phase == LockerPhase.Entering || Phase == LockerPhase.Exiting);
    }
    public override void OnNetworkDespawn()
    {
        lockers.Remove(this);
        session?.Finish();
        session = null;
        ApplyDoor(false);
        PlayState(returnState);
    }
    [ServerRpc(RequireOwnership = false)]
    private void EnterServerRpc(ServerRpcParams rpc = default)
    {
        if (Phase != LockerPhase.Empty || insideAnchor == null || viewAnchor == null || exitAnchor == null) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(rpc.Receive.SenderClientId, out var client)) return;
        NetworkObject player = client.PlayerObject;
        if (player == null || !player.IsSpawned || Vector3.Distance(player.transform.position, transform.position) > useDistance) return;
        if (player.TryGetComponent<PlayerStats>(out var stats) && stats.IsDead) return;
        foreach (var locker in lockers)
            if (locker != null && locker.occupant.Value == player.NetworkObjectId) return;
        occupant.Value = player.NetworkObjectId;
        phaseEnd.Value = NetworkManager.ServerTime.Time + enterDuration;
        phase.Value = LockerPhase.Entering;
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
            ApplyDoor(false);
            if (hasDisplayed && displayedPhase != LockerPhase.Empty) PlayState(returnState);
            displayedPhase = LockerPhase.Empty; hasDisplayed = true;
            return;
        }
        // Apply on every peer before starting character/camera presentation.
        // Hidden closes after entry; Empty closes after the local exit is restored.
        ApplyDoor(Phase == LockerPhase.Entering || Phase == LockerPhase.Exiting);
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
            PlayState(Phase == LockerPhase.Entering ? enterState : Phase == LockerPhase.Hidden ? hiddenState : exitState);
        }
    }
    private void ApplyDoor(bool open)
    {
        if (doorPivot == null) return;
        doorPivot.localRotation = Quaternion.Euler(
            doorRestEuler.x, doorRestEuler.y, open ? doorOpenZ : doorClosedZ);
    }
    private void PlayState(string state)
    {
        if (remoteAnimator == null || string.IsNullOrWhiteSpace(state)) return;
        int hash = Animator.StringToHash(state);
        if (remoteAnimator.HasState(0, hash)) remoteAnimator.CrossFadeInFixedTime(hash, 0.1f);
    }
}
