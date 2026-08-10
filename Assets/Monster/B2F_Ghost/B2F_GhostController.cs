using DeFrag.Monsters.Common;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(NetworkMonsterPlayerTargetResolver))]
[RequireComponent(typeof(Rigidbody))]
public sealed class B2F_GhostController : NetworkBehaviour, IMonsterPlayerTargetReceiver
{
    private enum GhostState : byte
    {
        SpawnIdle,
        Moving,
        PerformingMotion
    }

    [Header("Sequence")]
    [Min(0f)]
    [SerializeField] private float spawnIdleDuration = 2f;
    [Min(0.05f)]
    [SerializeField] private float targetRetryInterval = 0.5f;
    [Min(0.05f)]
    [SerializeField] private float specificMotionDuration = 2f;

    [Header("Movement")]
    [Min(0f)]
    [SerializeField] private float moveSpeed = 1.5f;
    [Min(0f)]
    [SerializeField] private float stoppingDistance = 1f;
    [Min(0f)]
    [SerializeField] private float rotationSpeed = 3f;
    [SerializeField] private bool keepSpawnHeight = true;

    [Header("Contact")]
    [SerializeField] private Collider contactCollider;

    [Header("Animation (Optional)")]
    [SerializeField] private Animator animator;
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string moveStateName = "Move";
    [SerializeField] private string specificMotionStateName = "SpecificMotion";
    [Min(0f)]
    [SerializeField] private float animationFadeDuration = 0.1f;

    private readonly NetworkVariable<GhostState> synchronizedState =
        new NetworkVariable<GhostState>(
            GhostState.SpawnIdle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private NetworkMonsterPlayerTargetResolver targetResolver;
    private Rigidbody ghostRigidbody;
    private Transform targetPlayer;
    private float stateStartedAt;
    private float nextTargetAttemptAt;
    private float spawnHeight;
    private Vector3 lockedMotionFacingPosition;
    private bool hasLockedMotionFacingPosition;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Reset()
    {
        ResolveReferences();
    }

    public override void OnNetworkSpawn()
    {
        synchronizedState.OnValueChanged += HandleStateChanged;
        ApplyStatePresentation(synchronizedState.Value);

        if (!IsServer)
            return;

        spawnHeight = transform.position.y;
        targetPlayer = null;
        SetState(GhostState.SpawnIdle, true);
        nextTargetAttemptAt = Time.time + spawnIdleDuration;
    }

    public override void OnNetworkDespawn()
    {
        synchronizedState.OnValueChanged -= HandleStateChanged;
        targetPlayer = null;
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer)
            return;

        switch (synchronizedState.Value)
        {
            case GhostState.SpawnIdle:
                UpdateSpawnIdle();
                break;
            case GhostState.Moving:
                UpdateMoving();
                break;
            case GhostState.PerformingMotion:
                UpdateSpecificMotion();
                break;
        }
    }

    public void SetPlayerTarget(Transform target)
    {
        if (IsServer && synchronizedState.Value != GhostState.PerformingMotion)
            targetPlayer = target;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryBeginContactMotion(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryBeginContactMotion(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryBeginContactMotion(collision.collider);
    }

    /// <summary>
    /// Call this from an Animation Event on the last frame of the specific motion.
    /// A duration fallback also despawns the ghost when no event is configured.
    /// </summary>
    public void CompleteSpecificMotion()
    {
        if (!IsSpawned || !IsServer ||
            synchronizedState.Value != GhostState.PerformingMotion)
        {
            return;
        }

        NetworkObject.Despawn(true);
    }

    private void UpdateSpawnIdle()
    {
        if (Time.time < nextTargetAttemptAt)
            return;

        if (targetResolver != null &&
            targetResolver.TryAcquireNearestLivingPlayer(out Transform nearestPlayer))
        {
            targetPlayer = nearestPlayer;
            SetState(GhostState.Moving);
            return;
        }

        // PlayerObjects can spawn after the ghost, so retry instead of keeping a null target.
        nextTargetAttemptAt = Time.time + targetRetryInterval;
    }

    private void UpdateMoving()
    {
        if (targetPlayer == null)
        {
            ReturnToTargetSearch();
            return;
        }

        Vector3 targetPosition = targetPlayer.position;
        Vector3 movementTarget = targetPosition;
        if (keepSpawnHeight)
            movementTarget.y = spawnHeight;

        Vector3 toTarget = movementTarget - transform.position;
        float stopDistanceSqr = stoppingDistance * stoppingDistance;
        if (toTarget.sqrMagnitude <= stopDistanceSqr)
        {
            BeginSpecificMotion(targetPlayer);
            return;
        }

        Vector3 direction = toTarget.normalized;
        transform.position = Vector3.MoveTowards(
            transform.position,
            movementTarget,
            moveSpeed * Time.deltaTime);

        RotateTowards(direction);
    }

    private void UpdateSpecificMotion()
    {
        if (hasLockedMotionFacingPosition)
            FaceTarget(lockedMotionFacingPosition);

        if (Time.time - stateStartedAt >= specificMotionDuration)
            CompleteSpecificMotion();
    }

    private void TryBeginContactMotion(Collider other)
    {
        if (!IsSpawned || !IsServer || other == null ||
            synchronizedState.Value == GhostState.PerformingMotion)
        {
            return;
        }

        PlayerStats playerStats = other.GetComponentInParent<PlayerStats>();
        if (playerStats == null || playerStats.IsDead)
            return;

        BeginSpecificMotion(playerStats.transform);
    }

    private void BeginSpecificMotion(Transform contactedPlayer)
    {
        if (!IsServer || synchronizedState.Value == GhostState.PerformingMotion)
            return;

        if (contactedPlayer != null)
        {
            lockedMotionFacingPosition = contactedPlayer.position;
            hasLockedMotionFacingPosition = true;
            FaceTarget(lockedMotionFacingPosition);
        }
        else
        {
            hasLockedMotionFacingPosition = false;
        }

        // Contact is committed once. Moving away can no longer resume pursuit.
        targetPlayer = null;
        SetState(GhostState.PerformingMotion);
    }

    private void ReturnToTargetSearch()
    {
        targetPlayer = null;
        SetState(GhostState.SpawnIdle);
        nextTargetAttemptAt = Time.time + targetRetryInterval;
    }

    private void SetState(GhostState nextState, bool forcePresentation = false)
    {
        if (!IsServer)
            return;

        stateStartedAt = Time.time;

        if (synchronizedState.Value == nextState)
        {
            if (forcePresentation)
                ApplyStatePresentation(nextState);
            return;
        }

        synchronizedState.Value = nextState;
    }

    private void HandleStateChanged(GhostState previousState, GhostState newState)
    {
        ApplyStatePresentation(newState);
    }

    private void ApplyStatePresentation(GhostState state)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        string stateName = state switch
        {
            GhostState.SpawnIdle => idleStateName,
            GhostState.Moving => moveStateName,
            GhostState.PerformingMotion => specificMotionStateName,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(stateName))
            return;

        int stateHash = Animator.StringToHash(stateName);
        if (animator.HasState(0, stateHash))
            animator.CrossFadeInFixedTime(stateHash, animationFadeDuration);
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        if (keepSpawnHeight)
            direction.y = 0f;

        RotateTowards(direction.normalized);
    }

    private void RotateTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime);
    }

    private void ResolveReferences()
    {
        if (targetResolver == null)
            targetResolver = GetComponent<NetworkMonsterPlayerTargetResolver>();

        if (ghostRigidbody == null)
            ghostRigidbody = GetComponent<Rigidbody>();

        if (ghostRigidbody != null)
        {
            ghostRigidbody.useGravity = false;
            ghostRigidbody.isKinematic = true;
        }

        if (contactCollider == null)
            contactCollider = GetComponent<Collider>();

        if (contactCollider != null)
            contactCollider.isTrigger = true;

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
    }

    private void OnValidate()
    {
        spawnIdleDuration = Mathf.Max(0f, spawnIdleDuration);
        targetRetryInterval = Mathf.Max(0.05f, targetRetryInterval);
        specificMotionDuration = Mathf.Max(0.05f, specificMotionDuration);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        stoppingDistance = Mathf.Max(0f, stoppingDistance);
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        animationFadeDuration = Mathf.Max(0f, animationFadeDuration);
        ResolveReferences();
    }
}
