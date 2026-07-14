using EasyPeasyFirstPersonController;
using UnityEngine;
using UnityEngine.AI;

public class MonsterAI : MonoBehaviour
{
    public enum MonsterState { Idle, Search, Chase, Attack, Investigate }
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

    [Header("Stuck Settings")]
    public float stuckCheckInterval = 1f;
    public float stuckThreshold = 0.1f;

    [Header("Sound Detection Settings")]
    public float soundDetectionRange = 25f;

    [Header("Animation")]
    public Animator animator;

    // 애니메이터 파라미터 캐싱
    private static readonly int IsIdle = Animator.StringToHash("isIdle");
    private static readonly int IsWalking = Animator.StringToHash("isWalking");
    private static readonly int IsRunning = Animator.StringToHash("isRunning");
    private static readonly int IsAttacking = Animator.StringToHash("isAttacking");

    private NavMeshAgent agent;
    private float idleTimer = 0f;
    private float idleDuration = 0f;
    private float lostPlayerTimer = 0f;
    private float investigateTimer = 0f;
    private float stuckTimer = 0f;
    private Vector3 lastKnownPlayerPos;
    private Vector3 lastPosition;
    private bool canSeePlayer = false;
    private SoundEmitter playerSoundEmitter;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        lastPosition = transform.position;
        if (player != null)
            playerSoundEmitter = player.GetComponentInChildren<SoundEmitter>();
        ChangeState(MonsterState.Search);
    }

    void Update()
    {
        if (player == null || !agent.isOnNavMesh) return;

        canSeePlayer = CheckPlayerVisibility();
        float distToPlayer = Vector3.Distance(transform.position, player.position);

        UpdateStateMachine(distToPlayer);
        ExecuteCurrentState();
    }

    void UpdateStateMachine(float distToPlayer)
    {
        bool hearsPlayer = CheckSoundDetection();

        if (canSeePlayer || hearsPlayer)
        {
            lastKnownPlayerPos = player.position;
            lostPlayerTimer = 0f;

            if (IsPlayerHiding())
            {
                if (currentState != MonsterState.Investigate)
                    ChangeState(MonsterState.Investigate);
                return;
            }

            if (distToPlayer <= attackRange)
                ChangeState(MonsterState.Attack);
            else if (distToPlayer <= chaseRange || hearsPlayer) // 소리 들리면 Chase
                ChangeState(MonsterState.Chase);
        }
        else
        {
            if (currentState == MonsterState.Chase || currentState == MonsterState.Attack)
            {
                lostPlayerTimer += Time.deltaTime;
                if (lostPlayerTimer >= lostPlayerTime)
                {
                    lostPlayerTimer = 0f;
                    ChangeState(MonsterState.Search);
                }
            }
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
        }
    }

    // 플레이어 숨기 상태 체크
    bool IsPlayerHiding()
    {
        // PlayerHiding 컴포넌트로 숨기 상태 확인
        FirstPersonController playerController = player.GetComponentInParent<FirstPersonController>();
        return playerController != null && playerController.IsHiding;
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
        if (playerSoundEmitter == null || !playerSoundEmitter.IsMicActive) return false;

        float distToPlayer = Vector3.Distance(transform.position, player.position);

        // 플레이어의 현재 소리 범위 안에 몬스터가 있는지 체크
        return distToPlayer <= Mathf.Min(playerSoundEmitter.CurrentSoundRange, soundDetectionRange);
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
        Vector3 destination = canSeePlayer ? player.position : lastKnownPlayerPos;
        agent.SetDestination(destination);
        RotateTowardsTarget(destination);
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
        }
    }

    void SetAllAnimationsFalse()
    {
        animator.SetBool(IsIdle, false);
        animator.SetBool(IsWalking, false);
        animator.SetBool(IsRunning, false);
        animator.SetBool(IsAttacking, false);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

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