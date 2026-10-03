using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// ForcedOpen is appended so existing serialized/replicated values keep their meaning.
public enum LockerPhase : byte { Empty, Entering, Hidden, Exiting, ForcedOpen }

/// <summary>Server-owned occupancy. Player movement remains on its existing owning client.</summary>
[RequireComponent(typeof(NetworkObject))]
[DisallowMultipleComponent]
public sealed class LockerHiding : NetworkBehaviour, IInteractable, IInteractionDirectionFilter
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
    [Header("Front-only interaction")]
    [Tooltip("문 정면 방향에서 이 각도 안에 서 있을 때만 숨기 안내가 뜹니다.")]
    [SerializeField, Range(5f, 89f)] private float frontApproachAngle = 40f;
    [Tooltip("시선이 문 정면에서 이 각도 안일 때만 숨기 안내가 뜹니다.")]
    [SerializeField, Range(5f, 89f)] private float frontViewAngle = 50f;
    [Tooltip("서버 판정에 더하는 여유 각도입니다 (복제 위치 오차 허용).")]
    [SerializeField, Range(0f, 30f)] private float serverAngleTolerance = 10f;
    [Header("Remote player Animator full state paths")]
    [SerializeField] private string enterState = "Base Layer.LockerEnter";
    [SerializeField] private string hiddenState = "Base Layer.LockerHidden";
    [SerializeField] private string exitState = "Base Layer.LockerExit";
    [SerializeField] private string returnState = "Base Layer.Idle Walk Run Blend";
    [Tooltip("들어가기·나오기 클립 재생 속도를 단계 길이에 맞추는 Animator float 파라미터입니다.")]
    [SerializeField] private string motionSpeedParameter = "LockerMotionSpeed";
    [SerializeField] private AnimationClip enterClip;
    [SerializeField] private AnimationClip exitClip;
    [SerializeField] private Vector2 motionSpeedRange = new(0.5f, 4f);
    [Header("First person motion")]
    [SerializeField] private float cameraBob = 0.04f;
    [SerializeField] private float cameraRoll = 3f;

    [Header("Monster interaction")]
    [Tooltip("몬스터가 문을 강제로 열 때 문이 젖혀지는 시간입니다.")]
    [SerializeField, Min(0.02f)] private float forcedDoorOpenDuration = 0.08f;
    [Tooltip("강제 개방 후 플레이어가 락커 밖으로 끌려나오는 시간입니다.")]
    [SerializeField, Min(0.1f)] private float forcedOpenDuration = 0.45f;
    [Tooltip("몬스터가 서서 문을 노려보는 위치입니다. 비우면 문 정면에서 자동 계산합니다.")]
    [SerializeField] private Transform monsterStandPoint;
    [SerializeField, Min(0.3f)] private float monsterStandDistance = 1.2f;
    [Tooltip("숨이 차서 헐떡일 때 주변 몬스터에게 들리는 반경입니다.")]
    [SerializeField, Min(0f)] private float gaspNoiseRadius = 9f;

    [Header("Door audio (3D, played on every peer)")]
    [SerializeField] private AudioClip[] creakOpenClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] creakCloseClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip forcedOpenClip;
    [SerializeField] private AudioClip knockClip;
    [SerializeField] private AudioClip gaspClip;
    [SerializeField, Range(0f, 1f)] private float doorVolume = 0.85f;
    [SerializeField, Min(0.1f)] private float doorAudioMaxDistance = 18f;
    [Tooltip("노크 시 문이 흔들리는 각도(도)입니다.")]
    [SerializeField, Range(0f, 10f)] private float knockRattleDegrees = 3.5f;

    [Header("Hidden player local audio (owner only, 2D)")]
    [SerializeField] private AudioClip heartbeatClip;
    [SerializeField] private AudioClip breathingLoopClip;
    [SerializeField] private AudioClip lurkTensionClip;
    public AudioClip HeartbeatClip => heartbeatClip;
    public AudioClip BreathingLoopClip => breathingLoopClip;
    public AudioClip LurkTensionClip => lurkTensionClip;

    private readonly NetworkVariable<ulong> occupant = new(Nobody);
    private readonly NetworkVariable<LockerPhase> phase = new(LockerPhase.Empty);
    private readonly NetworkVariable<double> phaseEnd = new();
    private readonly NetworkVariable<bool> occupantHoldingBreath = new(false);
    private LockerLocalSession session;
    private Animator remoteAnimator;
    private LockerPhase displayedPhase;
    private bool hasDisplayed;
    private LockerPhase audioPhase;
    private AudioSource doorAudio;
    private float rattleUntil;

    /// <summary>Server only: a hidden occupant ran out of breath. Args: locker, occupant.</summary>
    public static event Action<LockerHiding, NetworkObject> ServerOccupantGasped;

    public static IReadOnlyCollection<LockerHiding> All => lockers;
    public LockerPhase Phase => phase.Value;
    public float Duration => Phase switch
    {
        LockerPhase.Exiting => exitDuration,
        LockerPhase.ForcedOpen => forcedOpenDuration,
        _ => enterDuration
    };
    public float Progress => Mathf.Clamp01(1f - (float)(phaseEnd.Value - NetworkManager.ServerTime.Time) / Duration);
    public Transform Inside => insideAnchor;
    public Transform View => viewAnchor;
    public Transform Exit => exitAnchor;
    public float Bob => cameraBob;
    public float Roll => cameraRoll;
    public float MovementDoorLead => movementDoorLead;
    public bool OccupantHoldingBreath => occupantHoldingBreath.Value;
    public bool IsConcealing => Phase == LockerPhase.Entering || Phase == LockerPhase.Hidden;

    public NetworkObject Occupant =>
        IsSpawned && occupant.Value != Nobody &&
        NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(occupant.Value, out NetworkObject player)
            ? player
            : null;

    /// <summary>World-space centre of the door leaf (used for audio and facing).</summary>
    public Vector3 DoorCenter
    {
        get
        {
            Renderer doorRenderer = doorPivot != null ? doorPivot.GetComponent<Renderer>() : null;
            return doorRenderer != null ? doorRenderer.bounds.center : transform.position;
        }
    }

    /// <summary>Horizontal unit vector pointing out of the locker through the door.</summary>
    public Vector3 Outward
    {
        get
        {
            Renderer body = GetComponent<Renderer>();
            Vector3 center = body != null ? body.bounds.center : transform.position;
            Vector3 outward = DoorCenter - center;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f && exitAnchor != null && insideAnchor != null)
                outward = exitAnchor.position - insideAnchor.position;
            outward.y = 0f;
            return outward.sqrMagnitude > 1e-6f ? outward.normalized : transform.forward;
        }
    }

    /// <summary>Floor point in front of the door where a monster stands to stare at it.</summary>
    public Vector3 MonsterStandPoint
    {
        get
        {
            if (monsterStandPoint != null)
                return monsterStandPoint.position;
            Vector3 point = DoorCenter + Outward * monsterStandDistance;
            point.y = insideAnchor != null ? insideAnchor.position.y : transform.position.y;
            return point;
        }
    }

    public static bool IsPlayerHidden(NetworkObject player)
    {
        foreach (var locker in lockers)
            if (locker != null && locker.IsSpawned && locker.Phase == LockerPhase.Hidden &&
                locker.occupant.Value == player.NetworkObjectId) return true;
        return false;
    }

    /// <summary>True while the player is climbing in or hidden: they are not a visual target.</summary>
    public static bool IsPlayerConcealed(NetworkObject player)
    {
        foreach (var locker in lockers)
            if (locker != null && locker.IsSpawned && locker.IsConcealing &&
                locker.occupant.Value == player.NetworkObjectId) return true;
        return false;
    }

    public static LockerHiding FindByOccupant(NetworkObject player)
    {
        if (player == null) return null;
        foreach (var locker in lockers)
            if (locker != null && locker.IsSpawned && locker.occupant.Value == player.NetworkObjectId) return locker;
        return null;
    }

    public string GetInteractionText() => Phase == LockerPhase.Empty
        ? "락커에 숨기 (E 꾹 누르기)" : "이미 누가 있는 듯 하다";
    public bool IsHoldInteraction() => Phase == LockerPhase.Empty;

    /// <summary>Local HUD filter: only offer the locker when standing in front of the door and facing it.</summary>
    public bool CanInteractFrom(Vector3 viewerPosition, Vector3 viewDirection)
    {
        if (!IsInFrontOfDoor(viewerPosition, frontApproachAngle)) return false;
        Vector3 flatView = viewDirection;
        flatView.y = 0f;
        return flatView.sqrMagnitude > 1e-4f && Vector3.Angle(-Outward, flatView) <= frontViewAngle;
    }

    private bool IsInFrontOfDoor(Vector3 position, float maxAngle)
    {
        Vector3 offset = position - DoorCenter;
        offset.y = 0f;
        return offset.sqrMagnitude > 1e-4f && Vector3.Angle(Outward, offset) <= maxAngle;
    }

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
        audioPhase = Phase;
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
        if (!IsInFrontOfDoor(player.transform.position, frontApproachAngle + serverAngleTolerance)) return;
        if (player.TryGetComponent<PlayerStats>(out var stats) && stats.IsDead) return;
        foreach (var locker in lockers)
            if (locker != null && locker.occupant.Value == player.NetworkObjectId) return;
        occupant.Value = player.NetworkObjectId;
        occupantHoldingBreath.Value = false;
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
        if (Phase != LockerPhase.Hidden || !IsOccupantSender(rpc)) return;
        phaseEnd.Value = NetworkManager.ServerTime.Time + exitDuration;
        phase.Value = LockerPhase.Exiting;
    }
    public void CancelLocalEntry() { if (IsSpawned) CancelServerRpc(); }
    [ServerRpc(RequireOwnership = false)]
    private void CancelServerRpc(ServerRpcParams rpc = default)
    {
        if (IsOccupantSender(rpc)) ClearOccupant();
    }

    // ── Breath (owning client → server) ──────────────────────────────────────────
    public void ReportHoldingBreath(bool holding) { if (IsSpawned) HoldingBreathServerRpc(holding); }
    [ServerRpc(RequireOwnership = false)]
    private void HoldingBreathServerRpc(bool holding, ServerRpcParams rpc = default)
    {
        if (IsConcealing && IsOccupantSender(rpc)) occupantHoldingBreath.Value = holding;
    }

    public void ReportGasp() { if (IsSpawned) GaspServerRpc(); }
    [ServerRpc(RequireOwnership = false)]
    private void GaspServerRpc(ServerRpcParams rpc = default)
    {
        if (!IsConcealing || !IsOccupantSender(rpc)) return;
        occupantHoldingBreath.Value = false;
        GaspClientRpc();
        WorldNoiseSystem.Emit(DoorCenter, gaspNoiseRadius);
        ServerOccupantGasped?.Invoke(this, Occupant);
    }
    [ClientRpc]
    private void GaspClientRpc() => PlayDoorClip(gaspClip, 0.9f);

    // ── Monster hooks (server) ──────────────────────────────────────────────────
    /// <summary>Server: the monster rips the door open and pulls the occupant out.</summary>
    public bool TryForceOpenServer()
    {
        if (!IsServer || !IsConcealing) return false;
        occupantHoldingBreath.Value = false;
        phaseEnd.Value = NetworkManager.ServerTime.Time + forcedOpenDuration;
        phase.Value = LockerPhase.ForcedOpen;
        return true;
    }

    /// <summary>Local presentation of a monster knocking on / rattling the door.</summary>
    public void PlayKnockLocal()
    {
        PlayDoorClip(knockClip, 1f);
        rattleUntil = Time.time + 0.5f;
    }

    private bool IsOccupantSender(ServerRpcParams rpc) =>
        NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(occupant.Value, out var player) &&
        player.OwnerClientId == rpc.Receive.SenderClientId;

    private void ClearOccupant()
    {
        phase.Value = LockerPhase.Empty;
        occupant.Value = Nobody;
        occupantHoldingBreath.Value = false;
    }
    private void Update()
    {
        if (!IsSpawned) return;
        NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(occupant.Value, out var player);
        if (IsServer && Phase != LockerPhase.Empty)
        {
            if (player == null || !NetworkManager.ConnectedClients.ContainsKey(player.OwnerClientId) ||
                (Phase != LockerPhase.ForcedOpen && player.TryGetComponent<PlayerStats>(out var stats) && stats.IsDead))
                ClearOccupant();
            else if (NetworkManager.ServerTime.Time >= phaseEnd.Value)
            {
                if (Phase == LockerPhase.Entering) phase.Value = LockerPhase.Hidden;
                else if (Phase == LockerPhase.Exiting || Phase == LockerPhase.ForcedOpen) ClearOccupant();
            }
        }
        UpdateDoorAudio();
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
        if (player.IsOwner && session == null && Phase != LockerPhase.ForcedOpen)
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
            ApplyMotionSpeed();
            if (!PlayState(requestedState) && Phase == LockerPhase.Hidden)
                PlayState(returnState);
        }
    }

    // Creaks follow the replicated phase so every peer hears the same door.
    private void UpdateDoorAudio()
    {
        LockerPhase current = Phase;
        if (current == audioPhase) return;
        LockerPhase previous = audioPhase;
        audioPhase = current;
        switch (current)
        {
            case LockerPhase.Entering:
            case LockerPhase.Exiting:
                PlayDoorClip(Pick(creakOpenClips), 1f);
                break;
            case LockerPhase.Hidden:
                PlayDoorClip(Pick(creakCloseClips), 0.8f);
                break;
            case LockerPhase.ForcedOpen:
                PlayDoorClip(forcedOpenClip, 1f);
                break;
            case LockerPhase.Empty when previous == LockerPhase.Exiting:
                PlayDoorClip(Pick(creakCloseClips), 1f);
                break;
        }
    }

    private void PlayDoorClip(AudioClip clip, float volumeScale)
    {
        if (clip == null) return;
        if (doorAudio == null)
        {
            var audioObject = new GameObject("Locker Door Audio");
            audioObject.transform.SetParent(transform, false);
            doorAudio = audioObject.AddComponent<AudioSource>();
            doorAudio.playOnAwake = false;
            doorAudio.spatialBlend = 1f;
            doorAudio.rolloffMode = AudioRolloffMode.Linear;
            doorAudio.minDistance = 1.2f;
            doorAudio.maxDistance = doorAudioMaxDistance;
            doorAudio.dopplerLevel = 0f;
        }
        doorAudio.transform.position = DoorCenter;
        doorAudio.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
        doorAudio.PlayOneShot(clip, doorVolume * volumeScale);
    }

    private static AudioClip Pick(AudioClip[] clips) =>
        clips != null && clips.Length > 0 ? clips[UnityEngine.Random.Range(0, clips.Length)] : null;

    private float GetDoorTargetBlend()
    {
        if (Phase == LockerPhase.Entering || Phase == LockerPhase.Exiting || Phase == LockerPhase.ForcedOpen)
            return 1f;
        return Phase == LockerPhase.Hidden ? hiddenDoorAjar : 0f;
    }

    private void UpdateDoorAnimation()
    {
        if (doorPivot == null) return;
        float target = GetDoorTargetBlend();
        float duration = target > doorBlend
            ? Phase == LockerPhase.ForcedOpen ? forcedDoorOpenDuration : doorOpenDuration
            : doorCloseDuration;
        doorBlend = Mathf.MoveTowards(doorBlend, target, Time.deltaTime / Mathf.Max(0.01f, duration));
        Quaternion rotation = Quaternion.Slerp(doorClosedRotation, doorOpenRotation, doorBlend);
        if (Time.time < rattleUntil)
        {
            // Short decaying shake while a monster tests the door.
            float remaining = (rattleUntil - Time.time) / 0.5f;
            float angle = Mathf.Sin(Time.time * 55f) * knockRattleDegrees * remaining;
            rotation *= Quaternion.Euler(0f, 0f, angle);
        }
        doorPivot.localRotation = rotation;
    }

    private void SetDoorImmediate(float blend)
    {
        doorBlend = Mathf.Clamp01(blend);
        if (doorPivot != null)
            doorPivot.localRotation = Quaternion.Slerp(doorClosedRotation, doorOpenRotation, doorBlend);
    }
    // The enter/exit clips are longer than the gameplay phases; play them faster so the
    // remote body finishes the motion exactly when the replicated phase ends.
    private void ApplyMotionSpeed()
    {
        if (remoteAnimator == null || string.IsNullOrWhiteSpace(motionSpeedParameter)) return;
        AnimationClip clip = Phase == LockerPhase.Entering ? enterClip
            : Phase == LockerPhase.Exiting || Phase == LockerPhase.ForcedOpen ? exitClip : null;
        if (clip == null) return;
        int hash = Animator.StringToHash(motionSpeedParameter);
        foreach (AnimatorControllerParameter parameter in remoteAnimator.parameters)
        {
            if (parameter.nameHash != hash || parameter.type != AnimatorControllerParameterType.Float) continue;
            float speed = clip.length / Mathf.Max(0.05f, Duration);
            remoteAnimator.SetFloat(hash, Mathf.Clamp(speed, motionSpeedRange.x, motionSpeedRange.y));
            return;
        }
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
