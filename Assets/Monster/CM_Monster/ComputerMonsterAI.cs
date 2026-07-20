using EasyPeasyFirstPersonController;
using UnityEngine;
using UnityEngine.AI;

public class MonsterAI : MonoBehaviour
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

    [Header("Sound Detection Settings")]
    [Min(0f)] public float soundDetectionRange = 60f;
    [Range(0f, 1f)] public float minimumVoiceVolume = 0.05f;
    [Min(0f)] public float minimumVoiceDetectionRange = 5f;
    [Min(0.1f)] public float voiceRangeExponent = 1.35f;
    [Min(0.05f)] public float voicePositionUpdateInterval = 0.5f;

    [Header("Animation")]
    public Animator animator;

    // 애니메이터 파라미터 캐싱
    private static readonly int IsIdle = Animator.StringToHash("isIdle");
    private static readonly int IsWalking = Animator.StringToHash("isWalking");
    private static readonly int IsRunning = Animator.StringToHash("isRunning");
    private static readonly int IsAttacking = Animator.StringToHash("isAttacking");
    private static readonly int IsMissing = Animator.StringToHash("isMissing");
    private static readonly int MissingState = Animator.StringToHash("Base Layer.Missing");

    private NavMeshAgent agent;
    private ChaseDetourNavigator chaseNavigator;
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
    private SoundEmitter playerSoundEmitter;
    private float currentVoiceDetectionRange;
    private float voicePositionUpdateTimer;


    private void OnEnable()
    {
        WorldNoiseSystem.NoiseEmitted += OnWorldNoiseHeard;
    }

    private void OnDisable()
    {
        WorldNoiseSystem.NoiseEmitted -= OnWorldNoiseHeard;
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        lastPosition = transform.position;
        if (player != null)
            playerSoundEmitter = player.GetComponentInChildren<SoundEmitter>();

        chaseNavigator = new ChaseDetourNavigator(agent, detourSampleCount, detourSampleRadius, detourRecheckInterval);
        catchUpNavigator = new CatchUpNavigator(agent, maxChaseDistance, catchUpRadius, catchUpCooldown);   // 이 줄이 있는지 확인

        currentState = MonsterState.Idle;
        ChangeState(MonsterState.Search);
    }

    void Update()
    {
        if (player == null || !agent.isOnNavMesh) return;

        canSeePlayer = CheckPlayerVisibility();
        float distToPlayer = Vector3.Distance(transform.position, player.position);
        bool hearsPlayer = CheckSoundDetection();
        bool isActivelyChasing = currentState == MonsterState.Chase
                || currentState == MonsterState.Attack;
        bool crouchingOutsideChaseRange = isActivelyChasing
                && distToPlayer > chaseRange
                && IsPlayerCrouching();
        bool isPursuing = isActivelyChasing && !hearsPlayer;

        if (crouchingOutsideChaseRange)
            isPursuing = false;

        if (catchUpNavigator.TryCatchUp(transform.position, player.position, isPursuing))
            lastKnownPlayerPos = player.position;
        UpdateStateMachine(distToPlayer, hearsPlayer);
        ExecuteCurrentState();
    }

    void UpdateStateMachine(float distToPlayer, bool hearsPlayer)
    {
        // Missing은 플레이어를 놓친 사실을 표현하는 전용 상태입니다.
        // 이 상태가 끝날 때까지 감지 결과로 Chase/Attack에 재진입하지 않고,
        // HandleMissing에서 반드시 Search로 전환되도록 합니다.
        if (currentState == MonsterState.Missing)
        {
            canDetectPlayer = false;
            return;
        }

        bool isActivelyChasing = currentState == MonsterState.Chase
                || currentState == MonsterState.Attack;

        // 시야 감지는 정확한 추적으로, 목소리 감지는 위치 조사로 구분합니다.
        if (canSeePlayer)
        {
            canDetectPlayer = true;
            lastKnownPlayerPos = player.position;
            lostPlayerTimer = 0f;   // 다시 보이면 유예 타이머 리셋

            // 이미 추적 중이고 Chase 범위 안이라면 웅크리거나 정지해도 놓치지 않습니다.
            bool ignoreCrouchHiding = isActivelyChasing
                    && distToPlayer <= chaseRange;
            if (IsPlayerHiding() && !ignoreCrouchHiding)
            {
                if (currentState != MonsterState.Investigate)
                    ChangeState(MonsterState.Investigate);
                return;
            }

            if (distToPlayer <= attackRange)
                ChangeState(MonsterState.Attack);
            else if (distToPlayer <= chaseRange)
                ChangeState(MonsterState.Chase);

            return;
        }

        if (hearsPlayer)
        {
            canDetectPlayer = true;
            lastKnownPlayerPos = player.position;
            lostPlayerTimer = 0f;

            if (currentState != MonsterState.Investigate)
            {
                voicePositionUpdateTimer = 0f;
                ChangeState(MonsterState.Investigate);
            }
            else
            {
                voicePositionUpdateTimer += Time.deltaTime;
                if (voicePositionUpdateTimer >= voicePositionUpdateInterval)
                {
                    voicePositionUpdateTimer = 0f;
                    agent.SetDestination(lastKnownPlayerPos);
                }
            }

            return;
        }

        canDetectPlayer = false;
        currentVoiceDetectionRange = 0f;

        // 놓친 상태에서도 Chase를 유지한 채 lastKnownPlayerPos를 계속 추적
        if (currentState == MonsterState.Chase || currentState == MonsterState.Attack)
        {
            lostPlayerTimer += Time.deltaTime;

            if (lostPlayerTimer >= lostPlayerTime)
            {
                lostPlayerTimer = 0f;
                ChangeState(MonsterState.Missing);
            }
            // 유예 시간 이내에는 Chase를 유지하며 마지막 확인 위치를 추적합니다.
        }
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
        // PlayerHiding 컴포넌트로 숨기 상태 확인
        FirstPersonController playerController = player.GetComponentInParent<FirstPersonController>();
        return playerController != null && playerController.IsHiding;
    }

    bool IsPlayerCrouching()
    {
        FirstPersonController playerController = player.GetComponentInParent<FirstPersonController>();
        return playerController != null && playerController.CurrentState is PlayerCrouchingState;
    }

    bool CheckPlayerVisibility()
    {
        float distToPlayer = Vector3.Distance(transform.position, player.position);
        if (distToPlayer > chaseRange) return false;

        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, dirToPlayer);
        if (angle > fieldOfView * 0.5f) return false;

        if (Physics.Raycast(transform.position + Vector3.up, dirToPlayer, distToPlayer, obstacleMask))
            return false;

        return true;
    }
    bool CheckSoundDetection()
    {
        currentVoiceDetectionRange = 0f;
        if (playerSoundEmitter == null || !playerSoundEmitter.IsMicActive) return false;

        float volume = playerSoundEmitter.CurrentVolume;
        if (volume < minimumVoiceVolume) return false;

        float normalizedVolume = Mathf.InverseLerp(minimumVoiceVolume, 1f, volume);
        float volumeFactor = Mathf.Pow(normalizedVolume, voiceRangeExponent);
        currentVoiceDetectionRange = Mathf.Lerp(
                minimumVoiceDetectionRange,
                soundDetectionRange,
                volumeFactor);

        float distToPlayer = Vector3.Distance(transform.position, player.position);
        return distToPlayer <= currentVoiceDetectionRange;
    }

    private void OnWorldNoiseHeard(Vector3 noisePosition, float noiseRadius)
    {
        if (agent == null || !agent.isOnNavMesh) return;

        float audibleRange = Mathf.Min(noiseRadius, soundDetectionRange);
        if (Vector3.Distance(transform.position, noisePosition) > audibleRange) return;

        lastKnownPlayerPos = noisePosition;
        investigateTimer = 0f;

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
        if (!agent.pathPending && agent.hasPath && agent.remainingDistance < 0.5f)
            ChangeState(MonsterState.Idle);
    }

    void HandleChase()
    {
        if (canDetectPlayer)
        {
            // 시야 또는 소리로 실제 감지되는 동안에만 현재 위치를 갱신합니다.
            lastKnownPlayerPos = player.position;
            lostPlayerUpdateTimer = 0f;
        }
        else
        {
            // 감지하지 못한 동안에는 마지막으로 확인한 위치만 추적합니다.
            lostPlayerUpdateTimer += Time.deltaTime;
            if (lostPlayerUpdateTimer >= lostPlayerUpdateInterval)
                lostPlayerUpdateTimer = 0f;
        }

        chaseNavigator.MoveTowards(lastKnownPlayerPos);
        RotateTowardsMovement();
        CheckIfStuck();
    }
    void HandleAttack()
    {
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

        // 숨기 상태가 해제되면 즉시 Chase로 전환
        if (!IsPlayerHiding() && canSeePlayer)
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

    // 특정 위치 근처에서 랜덤 목적지 설정 (재사용 가능하도록 분리)
    void SetRandomDestinationNear(Vector3 center, float radius)
    {
        for (int i = 0; i < 30; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * radius;
            Vector3 randomPos = center + new Vector3(randomCircle.x, 0, randomCircle.y);

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPos, out hit, 5f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                return;
            }
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
                SetRandomDestination();
            }
            lastPosition = transform.position;
            stuckTimer = 0f;
        }
    }
    void ChangeState(MonsterState newState)
    {
        if (currentState == newState) return;
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
                SetRandomDestination();
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
                animator.SetBool(IsAttacking, true);
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
        animator.SetBool(IsAttacking, false);
        animator.SetBool(IsMissing, false);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (currentVoiceDetectionRange > 0f)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, currentVoiceDetectionRange);
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
