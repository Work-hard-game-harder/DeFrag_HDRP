using EasyPeasyFirstPersonController;
using DeFrag.Monsters.Common;
using DeFrag.Combat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(MonsterAttackHitbox))]
public class MonsterAI : MonoBehaviour, IMonsterPlayerTargetReceiver
{
    public enum MonsterState { Idle, Search, Chase, Attack, Investigate, Missing }
    public MonsterState currentState = MonsterState.Search;

    [Header("Detection Settings")]
    public Transform player;
    public float chaseRange = 10f;
    public float attackRange = 2f;
    public float attackStopDistance = 1.5f;
    public float searchRadius = 20f;
    public float fieldOfView = 120f;
    public LayerMask obstacleMask;

    [Header("Chase Settings")]
    public float lostPlayerTime = 3f;
    [Min(0f)]
    public float lostPlayerUpdateInterval = 1f;

    [Header("Missing Settings")]
    public float missingDuration = 2f;

    [Header("Investigate Settings")]
    public float investigateDuration = 10f;     // 수색 유지 시간
    public float investigateRadius = 5f;         // 수색 반경

    [Header("Speed Settings")]
    public float walkSpeed = 2f;
    public float runSpeed = 5f;

    [Header("Idle Settings")]
    public float minIdleTime = 2f;
    public float maxIdleTime = 5f;

    [Header("Initial Search Destination")]
    [Tooltip("스폰 직후 최초 한 번만 우선 이동할 씬 목적지입니다. 스폰 담당자가 주입합니다.")]
    [SerializeField] private Transform initialSearchDestination;
    [SerializeField, Min(0.1f)] private float initialDestinationArrivalDistance = 0.75f;
    [SerializeField, Min(0.1f)] private float initialDestinationSampleRadius = 5f;

    [Header("Rotation Settings")]
    public float rotationSpeed = 10f;

    [Header("Chase Detour Settings")]
    public int detourSampleCount = 8;
    public float detourSampleRadius = 6f;
    public float detourRecheckInterval = 0.4f;

    [Header("Catch Up Settings")]
    public float maxChaseDistance = 25f;      // 이 거리를 넘으면 캐치업 텔레포트 발동
    public float catchUpRadius = 6f;          // 플레이어 주변 이 반경 안에 랜덤 배치
    public float catchUpCooldown = 8f;        // 연속 텔레포트 방지용 쿨다운

    [Header("Stuck Settings")]
    public float stuckCheckInterval = 1f;
    public float stuckThreshold = 0.1f;

    [Header("World Noise Detection Settings")]
    [Min(0f)] public float soundDetectionRange = 60f;

    [Header("Animation")]
    public Animator animator;

    [Header("Attack Damage")]
    [SerializeField] private MonsterAttackHitbox attackHitbox;
    [Tooltip("Attack 상태가 유지될 때 다음 공격 cycle을 시작하는 간격입니다.")]
    [SerializeField, Min(0.05f)] private float attackCycleDuration = 1f;
    [Tooltip("공격 cycle 시작 후 실제 타격 판정을 실행할 시간입니다. Animation Event가 있으면 Event가 우선합니다.")]
    [SerializeField, Min(0f)] private float attackHitDelay = 0.35f;

    [Header("Behavior Designer")]
    [Tooltip("Behavior Designer 트리를 런타임에 자동 구성합니다. 끄면 기존 Update 방식으로 동작합니다.")]
    [SerializeField] private bool useBehaviorDesigner = true;
    [Tooltip("멀티플레이 중에는 서버에서만 몬스터 판단과 이동을 실행합니다.")]
    [SerializeField] private bool serverAuthoritative = true;

    // 애니메이터 파라미터 캐싱
    private static readonly int IsIdle = Animator.StringToHash("isIdle");
    private static readonly int IsWalking = Animator.StringToHash("isWalking");
    private static readonly int IsRunning = Animator.StringToHash("isRunning");
    private static readonly int IsAttack = Animator.StringToHash("isAttack");
    private static readonly int IsMissing = Animator.StringToHash("isMissing");
    private static readonly int MissingState = Animator.StringToHash("Base Layer.Missing");

    private NavMeshAgent agent;
    private ChaseDetourNavigator chaseNavigator;
    private NavMeshPath randomDestinationPath;
    private CatchUpNavigator catchUpNavigator;
    private float idleTimer = 0f;
    private float idleDuration = 0f;
    private float lostPlayerTimer = 0f;
    private float lostPlayerUpdateTimer = 0f;
    private float missingTimer = 0f;
    private float investigateTimer = 0f;
    private float stuckTimer = 0f;
    private Vector3 lastKnownPlayerPos;
    private Vector3 lastPosition;
    private bool canSeePlayer = false;
    private bool canDetectPlayer = false;
    private int preparedFrame = -1;
    private bool initialized;
    private float attackCycleStartedAt;
    private NetworkMonsterPlayerTargetResolver targetResolver;
    private bool initialDestinationPending;

    public MonsterState CurrentState => currentState;
    public bool UsesBehaviorDesigner => useBehaviorDesigner;

    public void SetBehaviorDesignerEnabled(bool enabled)
    {
        useBehaviorDesigner = enabled;
    }

    public void SetPlayerTarget(Transform target)
    {
        if (player == target)
            return;

        player = target;
        preparedFrame = -1;

        if (target != null && initialized)
            lastKnownPlayerPos = target.position;
    }

    public void SetInitialSearchDestination(Transform destination)
    {
        initialSearchDestination = destination;
        initialDestinationPending = destination != null;

        if (initialized && currentState == MonsterState.Search)
            SetSearchDestination();
    }

    public bool HasSimulationAuthority
    {
        get
        {
            if (!serverAuthoritative)
                return true;

            return MonsterSimulationAuthority.HasServerAuthority();
        }
    }

    private void Awake()
    {
        targetResolver = GetComponent<NetworkMonsterPlayerTargetResolver>();
        if (targetResolver == null)
            targetResolver = gameObject.AddComponent<NetworkMonsterPlayerTargetResolver>();

        attackHitbox = attackHitbox != null
            ? attackHitbox
            : GetComponent<MonsterAttackHitbox>();
        if (attackHitbox == null)
        {
            attackHitbox = gameObject.AddComponent<MonsterAttackHitbox>();
            attackHitbox.ConfigureSphere(10, attackRange);
        }

        if (useBehaviorDesigner && GetComponent<MonsterBehaviorTreeInstaller>() == null)
            gameObject.AddComponent<MonsterBehaviorTreeInstaller>();
    }


    private void OnEnable()
    {
        WorldNoiseSystem.NoiseEmitted += OnWorldNoiseHeard;
    }

    private void OnDisable()
    {
        WorldNoiseSystem.NoiseEmitted -= OnWorldNoiseHeard;
        attackHitbox?.EndAttackCycle();
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        lastPosition = transform.position;
        chaseNavigator = new ChaseDetourNavigator(agent, detourSampleCount, detourSampleRadius, detourRecheckInterval);
        randomDestinationPath = new NavMeshPath();
        catchUpNavigator = new CatchUpNavigator(agent, maxChaseDistance, catchUpRadius, catchUpCooldown);   // 이 줄이 있는지 확인
        initialDestinationPending = initialSearchDestination != null;

        currentState = MonsterState.Idle;
        ChangeState(MonsterState.Search);
        initialized = true;
    }

    void Update()
    {
        if (useBehaviorDesigner)
            return;

        if (!initialized || !HasSimulationAuthority || agent == null || !agent.isOnNavMesh)
            return;

        RefreshNetworkPlayerTarget();
        if (player == null)
            return;

        PrepareBehaviorFrame();
        ExecuteCurrentState();
    }

    /// <summary>
    /// Behavior Designer의 각 상태 Task가 호출하는 단일 진입점입니다.
    /// 같은 프레임에 Selector가 여러 상태를 평가해도 감지는 한 번만 갱신됩니다.
    /// </summary>
    public bool TickBehaviorState(MonsterState state)
    {
        if (!initialized || !HasSimulationAuthority || agent == null || !agent.isOnNavMesh)
            return false;

        RefreshNetworkPlayerTarget();
        if (player == null)
            return false;

        PrepareBehaviorFrame();
        if (currentState != state)
            return false;

        ExecuteCurrentState();
        return true;
    }

    private void PrepareBehaviorFrame()
    {
        if (preparedFrame == Time.frameCount || !initialized || !HasSimulationAuthority ||
            player == null || agent == null || !agent.isOnNavMesh)
            return;

        preparedFrame = Time.frameCount;

        canSeePlayer = CheckPlayerVisibility();
        float distToPlayer = Vector3.Distance(transform.position, player.position);
        bool hasTrackableVisual = canSeePlayer && !ShouldIgnoreVisiblePlayer(distToPlayer);
        bool isActivelyChasing = currentState == MonsterState.Chase
                || currentState == MonsterState.Attack;
        // 실제 추적 가능한 시야가 있을 때만 현재 플레이어 위치로 Catch-Up합니다.
        // 시야를 잃은 뒤 Warp로 플레이어 근처에 재등장해 Lost 타이머를
        // 계속 초기화하던 경로를 차단합니다.
        bool canCatchUpToPlayer = isActivelyChasing && hasTrackableVisual;

        if (catchUpNavigator.TryCatchUp(transform.position, player.position, canCatchUpToPlayer))
            lastKnownPlayerPos = player.position;
        UpdateStateMachine(distToPlayer, hasTrackableVisual);
    }

    private void RefreshNetworkPlayerTarget()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (targetResolver == null || manager == null || !manager.IsListening || !manager.IsServer)
            return;

        float visibilityRange = currentState == MonsterState.Investigate
            ? Mathf.Max(chaseRange, searchRadius)
            : chaseRange;

        if (targetResolver.TryAcquireVisiblePlayer(
                visibilityRange,
                fieldOfView,
                obstacleMask,
                1f,
                0f,
                out Transform visiblePlayer))
        {
            SetPlayerTarget(visiblePlayer);
            return;
        }

        // 시야에서 사라진 기존 대상은 LostPlayerTime 동안 유지합니다.
        // 대상이 없거나 죽었을 때만 다음 생존 플레이어를 기준 대상으로 잡습니다.
        if (player == null && targetResolver.TryAcquireNearestLivingPlayer(out Transform fallbackPlayer))
            SetPlayerTarget(fallbackPlayer);
    }

    void UpdateStateMachine(float distToPlayer, bool hasTrackableVisual)
    {
        // Missing은 플레이어를 놓친 사실을 표현하는 전용 상태입니다.
        // 이 상태가 끝날 때까지 감지 결과로 Chase/Attack에 재진입하지 않고,
        // HandleMissing에서 반드시 Search로 전환되도록 합니다.
        if (currentState == MonsterState.Missing)
        {
            canDetectPlayer = false;
            return;
        }

        if (hasTrackableVisual)
        {
            canDetectPlayer = true;
            lastKnownPlayerPos = player.position;
            lostPlayerTimer = 0f;   // 다시 보이면 유예 타이머 리셋

            if (distToPlayer <= attackRange)
                ChangeState(MonsterState.Attack);
            else
                ChangeState(MonsterState.Chase);

            return;
        }

        // 추적 중 시야를 잃은 경우 Lost Player 유예를 우선합니다.
        if (currentState == MonsterState.Chase || currentState == MonsterState.Attack)
        {
            canDetectPlayer = false;
            lostPlayerTimer += Time.deltaTime;

            // Lost Player 유예 동안에는 플레이어의 현재 위치를 일정 간격으로 갱신해
            // Chase 범위나 시야를 벗어나도 설정 시간까지 계속 추적합니다.
            // Catch-Up Warp는 비활성 상태이므로 실제 이동은 NavMesh 추적으로만 수행됩니다.
            lostPlayerUpdateTimer += Time.deltaTime;
            bool firstLostFrame = lostPlayerTimer <= Time.deltaTime;
            if (firstLostFrame || lostPlayerUpdateInterval <= 0f ||
                lostPlayerUpdateTimer >= lostPlayerUpdateInterval)
            {
                lastKnownPlayerPos = player.position;
                lostPlayerUpdateTimer = 0f;
            }

            if (lostPlayerTimer >= lostPlayerTime)
            {
                lostPlayerTimer = 0f;
                ChangeState(MonsterState.Missing);
            }

            return;
        }

        canDetectPlayer = false;
    }

    void ExecuteCurrentState()
    {
        switch (currentState)
        {
            case MonsterState.Idle:
                HandleIdle();
                break;
            case MonsterState.Search:
                HandleSearch();
                break;
            case MonsterState.Chase:
                HandleChase();
                break;
            case MonsterState.Attack:
                HandleAttack();
                break;
            case MonsterState.Investigate:
                HandleInvestigate();
                break;
            case MonsterState.Missing:
                HandleMissing();
                break;
        }
    }

    // 플레이어 숨기 상태 체크
    bool IsPlayerHiding()
    {
        StarterAssets.PersonController playerController =
            player.GetComponentInParent<StarterAssets.PersonController>();
        return playerController != null && playerController.IsHiding;
    }

    bool IsPlayerCrouching()
    {
        StarterAssets.PersonController playerController =
            player.GetComponentInParent<StarterAssets.PersonController>();
        return playerController != null && playerController.IsCrouching;
    }

    bool ShouldIgnoreVisiblePlayer(float distToPlayer)
    {
        if (IsPlayerHiding())
            return true;

        bool isCrouching = IsPlayerCrouching();

        // Investigate에서는 Crouching 자체를 은신 행동으로 취급합니다.
        // 플레이어가 일어나면 같은 프레임의 시야 판정에서 Chase/Attack으로 전환됩니다.
        if (currentState == MonsterState.Investigate)
            return isCrouching;

        // 이미 추적 중일 때는 Chase 범위 안의 웅크리기는 놓치지 않지만,
        // 범위 밖으로 벗어나 웅크리면 더 이상 감지하지 못합니다.
        bool isActivelyChasing = currentState == MonsterState.Chase ||
                                 currentState == MonsterState.Attack;
        return isActivelyChasing && distToPlayer > chaseRange && isCrouching;
    }

    bool CheckPlayerVisibility()
    {
        // Investigate 중에는 Search 반경까지 시야를 확장합니다.
        // 이 확장 구간에서는 서 있는 플레이어만 발각되며 웅크리기는 위 조건에서 제외됩니다.
        float visibilityRange = currentState == MonsterState.Investigate
            ? Mathf.Max(chaseRange, searchRadius)
            : chaseRange;
        return MonsterPerceptionUtility.CanSeeTarget(
            transform,
            player,
            visibilityRange,
            fieldOfView,
            obstacleMask,
            1f,
            0f);
    }
    private void OnWorldNoiseHeard(Vector3 noisePosition, float noiseRadius)
    {
        if (!HasSimulationAuthority || agent == null || !agent.isOnNavMesh) return;

        float audibleRange = Mathf.Min(noiseRadius, soundDetectionRange);
        if (Vector3.Distance(transform.position, noisePosition) > audibleRange) return;

        lastKnownPlayerPos = noisePosition;
        investigateTimer = 0f;

        // Chase/Attack 중에는 UpdateStateMachine의 Lost Player 유예를 유지합니다.
        // 소리는 마지막 위치만 보정하고 Investigate로 즉시 덮어쓰지 않습니다.
        if (currentState == MonsterState.Chase || currentState == MonsterState.Attack)
            return;

        if (currentState != MonsterState.Investigate)
            ChangeState(MonsterState.Investigate);
        else
            SetRandomDestinationNear(lastKnownPlayerPos, investigateRadius);
    }
    void HandleIdle()
    {
        idleTimer += Time.deltaTime;
        if (idleTimer >= idleDuration)
            ChangeState(MonsterState.Search);
    }

    void HandleSearch()
    {
        RotateTowardsMoveDirection();
        CheckIfStuck();
        float arrivalDistance = initialDestinationPending
            ? Mathf.Max(agent.stoppingDistance, initialDestinationArrivalDistance)
            : 0.5f;
        bool reachedDestination = !agent.pathPending &&
                                  agent.hasPath &&
                                  agent.remainingDistance <= arrivalDistance;
        if (reachedDestination)
        {
            initialDestinationPending = false;
            ChangeState(MonsterState.Idle);
        }
    }

    void HandleChase()
    {
        if (canDetectPlayer)
        {
            // 시야 또는 소리로 실제 감지되는 동안에만 현재 위치를 갱신합니다.
            lastKnownPlayerPos = player.position;
            lostPlayerUpdateTimer = 0f;
        }

        chaseNavigator.MoveTowards(lastKnownPlayerPos);
        RotateTowardsMovement();
        CheckIfStuck();
    }
    void HandleAttack()
    {
        UpdateAttackDamageCycle();

        float distToPlayer = Vector3.Distance(transform.position, player.position);
        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        dirToPlayer.y = 0;

        if (distToPlayer < attackStopDistance)
            agent.Move(-dirToPlayer * agent.speed * Time.deltaTime);
        else if (distToPlayer > attackRange)
            agent.SetDestination(player.position);
        else
            agent.ResetPath();

        RotateTowardsTarget(player.position);
    }

    /// <summary>
    /// 현재 Animator에 타격 Animation Event가 없어도 호스트에서 공격을 검증할 수 있도록
    /// 공격 상태 동안 일정한 주기로 공용 히트박스의 타격 시점을 실행합니다.
    /// Animation Event를 추가하면 같은 공격 주기 안에서는 중복 데미지가 자동 차단됩니다.
    /// </summary>
    private void UpdateAttackDamageCycle()
    {
        if (attackHitbox == null)
            return;

        float safeHitDelay = Mathf.Max(0f, attackHitDelay);
        float safeCycleDuration = Mathf.Max(0.05f, attackCycleDuration, safeHitDelay);

        if (!attackHitbox.IsAttackCycleActive)
        {
            attackCycleStartedAt = Time.time;
            attackHitbox.BeginAttackCycle();
        }
        else if (Time.time >= attackCycleStartedAt + safeCycleDuration)
        {
            attackHitbox.EndAttackCycle();
            attackCycleStartedAt = Time.time;
            attackHitbox.BeginAttackCycle();
        }

        if (Time.time >= attackCycleStartedAt + safeHitDelay)
            attackHitbox.ResolveAttackHit();
    }

    // 공격 애니메이션의 실제 타격 프레임에서 호출할 수 있는 선택적 Animation Event입니다.
    public void AnimationEvent_ResolveAttackHit()
    {
        if (currentState == MonsterState.Attack)
            attackHitbox?.ResolveAttackHit();
    }

    void HandleInvestigate()
    {
        investigateTimer += Time.deltaTime;

        // 마지막 위치 근처를 돌아다님
        if (!agent.pathPending && agent.hasPath && agent.remainingDistance < 0.5f)
            SetRandomDestinationNear(lastKnownPlayerPos, investigateRadius);

        RotateTowardsMoveDirection();

        // 10초 후 Search로 복귀
        if (investigateTimer >= investigateDuration)
        {
            investigateTimer = 0f;
            ChangeState(MonsterState.Search);
        }

        // UpdateStateMachine에서 은신 예외를 통과해 실제 감지된 경우에만 추격합니다.
        if (canDetectPlayer && canSeePlayer)
        {
            investigateTimer = 0f;
            ChangeState(MonsterState.Chase);
        }
    }

    void HandleMissing()
    {
        missingTimer += Time.deltaTime;

        if (missingTimer >= missingDuration)
        {
            missingTimer = 0f;
            ChangeState(MonsterState.Search);
        }
    }


    void SetRandomDestination()
    {
        SetRandomDestinationNear(transform.position, searchRadius);
    }

    private void SetSearchDestination()
    {
        if (TrySetInitialSearchDestination())
            return;

        SetRandomDestination();
    }

    private bool TrySetInitialSearchDestination()
    {
        if (!initialDestinationPending)
            return false;

        if (initialSearchDestination == null)
        {
            initialDestinationPending = false;
            return false;
        }

        if (randomDestinationPath == null)
            randomDestinationPath = new NavMeshPath();

        if (!NavMesh.SamplePosition(
                initialSearchDestination.position,
                out NavMeshHit hit,
                initialDestinationSampleRadius,
                agent.areaMask) ||
            !agent.CalculatePath(hit.position, randomDestinationPath) ||
            randomDestinationPath.status != NavMeshPathStatus.PathComplete)
        {
            Debug.LogWarning(
                "[MonsterAI] 최초 배전함 목적지까지 완전한 NavMesh 경로를 찾지 못해 랜덤 순찰로 전환합니다.",
                this);
            initialDestinationPending = false;
            return false;
        }

        agent.SetDestination(hit.position);
        return true;
    }

    // 특정 위치 근처에서 랜덤 목적지 설정 (재사용 가능하도록 분리)
    void SetRandomDestinationNear(Vector3 center, float radius)
    {
        if (randomDestinationPath == null)
            randomDestinationPath = new NavMeshPath();

        if (MonsterNavMeshUtility.TryFindRandomReachablePosition(
                agent,
                center,
                radius,
                30,
                5f,
                randomDestinationPath,
                out Vector3 destination))
        {
            agent.SetDestination(destination);
            return;
        }

        Debug.LogWarning("[Search] SamplePosition 30회 실패, 폴백 경로 사용");
        agent.SetDestination(transform.position + transform.forward * 3f);
    }

    void RotateTowardsMoveDirection()
    {
        if (agent.velocity.magnitude > 0.1f)
        {
            Vector3 moveDir = agent.velocity.normalized;
            moveDir.y = 0;
            RotateTowards(moveDir);
        }
    }

    void RotateTowardsTarget(Vector3 targetPos)
    {
        Vector3 dirToTarget = (targetPos - transform.position).normalized;
        dirToTarget.y = 0;
        if (dirToTarget != Vector3.zero)
            RotateTowards(dirToTarget);
    }

    void RotateTowards(Vector3 direction)
    {
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
    }
    void RotateTowardsMovement()
    {
        if (agent.desiredVelocity.sqrMagnitude > 0.01f)
        {
            Vector3 moveDir = agent.desiredVelocity.normalized;
            moveDir.y = 0f;
            if (moveDir != Vector3.zero)
                RotateTowards(moveDir);
        }
    }
    void CheckIfStuck()
    {
        stuckTimer += Time.deltaTime;
        if (stuckTimer >= stuckCheckInterval)
        {
            float movedDistance = Vector3.Distance(transform.position, lastPosition);
            if (movedDistance < stuckThreshold && agent.hasPath)
            {
                agent.ResetPath();
                if (currentState == MonsterState.Search)
                    SetSearchDestination();
                else
                    SetRandomDestination();
            }
            lastPosition = transform.position;
            stuckTimer = 0f;
        }
    }
    void ChangeState(MonsterState newState)
    {
        if (currentState == newState) return;

        if (currentState == MonsterState.Attack)
            attackHitbox?.EndAttackCycle();

        currentState = newState;
        SetAllAnimationsFalse();

        switch (currentState)
        {
            case MonsterState.Idle:
                agent.ResetPath();
                agent.speed = 0f;
                animator.SetBool(IsIdle, true);
                idleTimer = 0f;
                idleDuration = Random.Range(minIdleTime, maxIdleTime);
                break;

            case MonsterState.Search:
                agent.speed = walkSpeed;
                animator.SetBool(IsWalking, true);
                SetSearchDestination();
                break;

            case MonsterState.Chase:
                agent.speed = runSpeed;
                animator.SetBool(IsRunning, true);
                chaseNavigator.Reset();
                lostPlayerTimer = 0f;
                lostPlayerUpdateTimer = 0f;   // 추가: 새로 Chase에 들어올 때마다 주기 초기화
                break;

            case MonsterState.Attack:
                agent.speed = 0f;
                agent.ResetPath();
                animator.SetBool(IsAttack, true);
                attackCycleStartedAt = Time.time;
                attackHitbox?.BeginAttackCycle();
                break;

            case MonsterState.Investigate:
                agent.speed = walkSpeed;
                animator.SetBool(IsWalking, true);
                investigateTimer = 0f;
                SetRandomDestinationNear(lastKnownPlayerPos, investigateRadius);
                break;

            case MonsterState.Missing:
                agent.speed = 0f;
                agent.ResetPath();
                animator.SetBool(IsMissing, true);
                animator.CrossFade(MissingState, 0.1f, 0, 0f);
                lostPlayerTimer = 0f;
                lostPlayerUpdateTimer = 0f;
                missingTimer = 0f;
                break;
        }
    }

    void SetAllAnimationsFalse()
    {
        animator.SetBool(IsIdle, false);
        animator.SetBool(IsWalking, false);
        animator.SetBool(IsRunning, false);
        animator.SetBool(IsAttack, false);
        animator.SetBool(IsMissing, false);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (initialSearchDestination != null && initialDestinationPending)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, initialSearchDestination.position);
            Gizmos.DrawWireSphere(initialSearchDestination.position, initialDestinationArrivalDistance);
        }

        Gizmos.color = Color.cyan;
        Vector3 leftDir = Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * transform.forward;
        Vector3 rightDir = Quaternion.Euler(0, fieldOfView * 0.5f, 0) * transform.forward;
        Gizmos.DrawLine(transform.position, transform.position + leftDir * chaseRange);
        Gizmos.DrawLine(transform.position, transform.position + rightDir * chaseRange);

        if (currentState == MonsterState.Chase || currentState == MonsterState.Investigate)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(lastKnownPlayerPos, 0.3f);

            if (currentState == MonsterState.Investigate)
            {
                Gizmos.color = new Color(1f, 0f, 1f, 0.2f);
                Gizmos.DrawWireSphere(lastKnownPlayerPos, investigateRadius);
            }
        }
    }
}
