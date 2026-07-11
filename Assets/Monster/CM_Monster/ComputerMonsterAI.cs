using UnityEngine;
using UnityEngine.AI;

public class ComputerMonsterAI : MonoBehaviour
{
    public enum MonsterState { Idle, Search, Chase, Attack }
    public MonsterState currentState = MonsterState.Search;

    [Header("Detection Settings")]
    public Transform player;
    public float chaseRange = 10f;
    public float attackRange = 2f;
    public float attackStopDistance = 1.5f;
    public float searchRadius = 20f;

    [Header("Speed Settings")]
    public float walkSpeed = 2f;
    public float runSpeed = 5f;

    [Header("Idle Settings")]
    public float minIdleTime = 2f;  // 최소 대기 시간
    public float maxIdleTime = 5f;  // 최대 대기 시간

    [Header("Rotation Settings")]
    public float rotationSpeed = 10f;

    [Header("Animation")]
    public Animator animator;

    private NavMeshAgent agent;
    private float idleTimer = 0f;
    private float idleDuration = 0f;
    private bool isIdling = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        SetRandomDestination();
    }

    void Update()
    {
        if (player == null) return;

        // NavMesh 가드
        if (!agent.isOnNavMesh) return;

        float distToPlayer = Vector3.Distance(transform.position, player.position);

        if (distToPlayer <= attackRange)
        {
            isIdling = false;
            ChangeState(MonsterState.Attack);
        }
        else if (distToPlayer <= chaseRange)
        {
            isIdling = false;
            ChangeState(MonsterState.Chase);
        }
        else
        {
            // Chase/Attack 상태에서 플레이어를 놓치면 Search로
            if (currentState == MonsterState.Chase || currentState == MonsterState.Attack)
            {
                ChangeState(MonsterState.Search);
            }
            else if (currentState == MonsterState.Search)
            {
                HandleSearch();
            }
            else if (currentState == MonsterState.Idle)
            {
                HandleIdle();
            }
        }

        if (currentState == MonsterState.Chase)
            HandleChase();
        else if (currentState == MonsterState.Attack)
            HandleAttack();
    }

    void ChangeState(MonsterState newState)
    {
        if (currentState == newState) return;
        currentState = newState;

        switch (currentState)
        {
            case MonsterState.Idle:
                agent.speed = 0f;
                agent.ResetPath();
                animator.SetBool("isWalking", false);
                animator.SetBool("isRunning", false);
                animator.SetBool("isAttacking", false);
                animator.SetBool("isIdle", true);

                // 랜덤 대기 시간 설정
                idleDuration = Random.Range(minIdleTime, maxIdleTime);
                idleTimer = 0f;
                isIdling = true;
                break;

            case MonsterState.Search:
                agent.speed = walkSpeed;
                animator.SetBool("isIdle", false);
                animator.SetBool("isRunning", false);
                animator.SetBool("isAttacking", false);
                animator.SetBool("isWalking", true);
                SetRandomDestination();
                break;

            case MonsterState.Chase:
                agent.speed = runSpeed;
                animator.SetBool("isIdle", false);
                animator.SetBool("isWalking", false);
                animator.SetBool("isAttacking", false);
                animator.SetBool("isRunning", true);
                break;

            case MonsterState.Attack:
                agent.speed = 0f;
                agent.ResetPath();
                animator.SetBool("isIdle", false);
                animator.SetBool("isWalking", false);
                animator.SetBool("isRunning", false);
                animator.SetBool("isAttacking", true);
                break;
        }
    }

    void HandleSearch()
    {
        // 이동 방향으로 회전
        if (agent.velocity.magnitude > 0.1f)
        {
            Vector3 moveDir = agent.velocity.normalized;
            moveDir.y = 0;
            if (moveDir != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
            }
        }

        if (!agent.pathPending && agent.hasPath && agent.remainingDistance < 0.5f)
        {
            Debug.Log("목적지 도착 → Idle 전환");
            ChangeState(MonsterState.Idle);
        }
    }

    void HandleIdle()
    {
        idleTimer += Time.deltaTime;
        Debug.Log($"Idle 타이머: {idleTimer:F1} / {idleDuration:F1}");

        if (idleTimer >= idleDuration)
        {
            Debug.Log("Idle 종료 → Search 전환");
            ChangeState(MonsterState.Search);
        }
    }

    void HandleChase()
    {
        agent.SetDestination(player.position);

        // 플레이어 방향으로 회전
        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        dirToPlayer.y = 0;
        if (dirToPlayer != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(dirToPlayer);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
        }
    }

    void HandleAttack()
    {
        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        dirToPlayer.y = 0;

        float distToPlayer = Vector3.Distance(transform.position, player.position);

        // 유지 거리보다 가까우면 뒤로 물러남
        if (distToPlayer < attackStopDistance)
        {
            Vector3 moveBack = transform.position - dirToPlayer * agent.speed * Time.deltaTime;
            agent.Move(moveBack - transform.position);
        }
        // 유지 거리보다 멀면 가까이 이동
        else if (distToPlayer > attackRange)
        {
            agent.SetDestination(player.position);
        }
        else
        {
            agent.ResetPath(); // 적정 거리면 멈춤
        }

        // 플레이어 방향으로 회전
        if (dirToPlayer != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(dirToPlayer);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }
    }

    void SetRandomDestination()
    {
        for (int i = 0; i < 30; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * searchRadius;
            Vector3 randomPos = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPos, out hit, 5f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                return;
            }
        }

        agent.SetDestination(transform.position + transform.forward * 3f);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}