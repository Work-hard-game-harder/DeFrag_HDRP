using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class PatrolRobotAI : MonoBehaviour
{
    public enum State { Patrol, Inspect, Alert, Chase }
    public State currentState = State.Patrol;

    [Header("Vision Settings")]
    public Transform player;
    public float viewDistance = 15f; 
    public float viewAngle = 120f;   
    public Transform eyeLocation;    
    public LayerMask obstacleMask;   

    [Header("Random Patrol Settings")]
    public float patrolRadius = 20f;       // 로봇이 한 번에 탐색할 최대 반경 (너무 크면 멀리 감)
    public float minPatrolDistance = 7f;   // 최소 이동 거리 (제자리걸음 방지용, 적당히 먼 곳)

    [Header("Vision Settings")]
    public Light visionLight; // 이 변수를 추가! (인스펙터에서 아까 만든 Spot Light를 끌어다 넣으세요)
    
    private NavMeshAgent agent;
    private bool isInspecting = false;
    private Vector3 lastKnownPlayerPos;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        SetRandomPatrolDestination(); // 시작하자마자 랜덤한 곳으로 출발
    }

    void Update()
    {
        if (!agent.isOnNavMesh || !agent.enabled) 
            return;
        // 1단계: 플레이어 감지 (항상 최신 결과 유지)
        bool seesPlayer = CanSeePlayer();

        // 2단계: 강력한 상태 전환 논리 (detection 결과에 따라 '즉시' 전환)
        // [규칙 1] 플레이어를 발견하면, '어떤 상태든' 즉시 추격(빨간불)으로 전환합니다.
        if (seesPlayer && currentState != State.Chase)
        {
            StartChase();
            return; // 전환 완료, 행동은 다음 프레임부터 실행
        }
        // [규칙 2] 플레이어를 추격 중이다가 놓치면, 즉시 경계(주황불)로 전환합니다.
        else if (!seesPlayer && currentState == State.Chase)
        {
            StopChase();
            return; // 전환 완료, 행동은 다음 프레임부터 실행
        }

        // 3단계: 상태별 고유 행동 (전환 논리는 여기 포함 안 함)
        switch (currentState)
        {
            case State.Patrol:
                visionLight.color = Color.yellow; // 노란불
                agent.isStopped = false; // 이동 보장
                
                // 순찰 지점 도착 시 두리번거리기
                if (!agent.pathPending && agent.remainingDistance < 0.5f && !isInspecting)
                {
                    StartCoroutine(InspectRoutine()); 
                }
                break;

            case State.Inspect:
                visionLight.color = new Color(1f, 0.5f, 0f); // 주황불
                // 코루틴 행동은 InspectRoutine() 내부에서 처리 (전환은 2단계에서 이미 수행됨)
                break;

            case State.Alert:
                visionLight.color = new Color(1f, 0.5f, 0f); // 주황불
                agent.isStopped = false; // 이동 보장

                // 수색 실패 시 다시 순찰 복귀 (이동 논리는 StopChase()에 구현)
                if (!agent.pathPending && agent.remainingDistance < 1f)
                {
                    Debug.Log("플레이어 수색 실패. 다시 순찰 모드로 복귀합니다.");
                    currentState = State.Patrol;
                    SetRandomPatrolDestination(); // 새로운 순찰 지점으로 이동
                }
                break;

            case State.Chase:
                visionLight.color = Color.red; // 빨간불
                agent.isStopped = false; // 이동 보장

                // 플레이어를 향해 무조건 이동
                lastKnownPlayerPos = player.position; // 마지막 위치 업데이트 (다음 Alert를 위해)
                agent.SetDestination(player.position);
                break;
        }
    }

    // NavMesh 위에서 무작위 목적지를 찾는 핵심 로직
    // NavMesh 위에서 무작위 목적지를 찾는 핵심 로직 (수정본)
    void SetRandomPatrolDestination()
    {
        bool foundPoint = false;

        for (int i = 0; i < 30; i++)
        {
            // 수정 1: 공중이 아닌 바닥 평면(XZ축) 기준으로만 랜덤 좌표를 생성합니다.
            Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
            Vector3 randomPos = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);

            // 수정 2: 최소 이동 거리보다 가까우면 무시하고 다시 뽑습니다.
            if (Vector3.Distance(transform.position, randomPos) < minPatrolDistance) continue;

            // 수정 3: 좌표 근처의 파란색 바닥(NavMesh)을 찾는 탐색 범위를 5.0f로 넉넉하게 늘립니다.
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPos, out hit, 5.0f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                foundPoint = true;
                break; // 목적지를 찾았으니 반복문 탈출!
            }
        }

        // 무한 두리번거림 방지용 안전장치
        // 만약 구석에 갇히거나 맵이 좁아서 30번 시도했는데도 적절한 곳을 못 찾았다면,
        // 일단 로봇이 바라보는 정면 앞쪽 3m 지점으로 강제로 걸어가게 만듭니다.
        if (!foundPoint)
        {
            agent.SetDestination(transform.position + transform.forward * 3f);
        }
    }

    // 시야 감지 로직
    // 수정된 시각 감지 로직 (더욱 정밀하고 버그 없는 버전)
    // 수정된 시각 감지 로직 (디버그 모드 + 정밀 타격)
    bool CanSeePlayer()
    {
        Vector3 targetPos = player.position + Vector3.up * 1.0f; // 가슴 높이 조준
        Vector3 dirToPlayer = (targetPos - eyeLocation.position).normalized;
        float angleToPlayer = Vector3.Angle(transform.forward, dirToPlayer);

        if (angleToPlayer < viewAngle / 2f) 
        {
            float distToPlayer = Vector3.Distance(eyeLocation.position, targetPos);
            if (distToPlayer <= viewDistance)
            {
                // [엑스레이] 씬(Scene) 화면에서 로봇의 눈에서 나가는 레이저를 초록색 선으로 보여줍니다.
                Debug.DrawRay(eyeLocation.position, dirToPlayer * distToPlayer, Color.green);

                RaycastHit hit;
                if (Physics.Raycast(eyeLocation.position, dirToPlayer, out hit, distToPlayer))
                {
                    // [해결 1] 만약 레이저가 로봇 자기 자신의 몸통을 때렸다면? -> 무시!
                    if (hit.collider.transform.root == this.transform) return false;

                    // [추적기] 도대체 레이저가 '무엇'에 부딪혔는지 콘솔에 이름을 띄웁니다.
                    Debug.Log("로봇 시야에 걸린 물체: " + hit.collider.name + " (태그: " + hit.collider.tag + ")");

                    // [해결 2] 플레이어 본체뿐만 아니라, 자식 콜라이더(팔, 다리, 가방 등)에 맞아도 인식하게 만듭니다.
                    if (hit.collider.CompareTag("Player") || hit.transform.root.CompareTag("Player"))
                    {
                        return true; 
                    }
                }
            }
        }
        return false;
    }

    // 두리번거리는 코루틴
    IEnumerator InspectRoutine()
    {
        currentState = State.Inspect;
        isInspecting = true;
        agent.isStopped = true; 

        Quaternion startRot = transform.rotation;
        yield return new WaitForSeconds(1f); 

        yield return StartCoroutine(SmoothRotate(startRot * Quaternion.Euler(0, -130, 0), 1.5f));
        yield return new WaitForSeconds(0.5f);

        yield return StartCoroutine(SmoothRotate(startRot, 1f));
        yield return new WaitForSeconds(0.5f);

        yield return StartCoroutine(SmoothRotate(startRot * Quaternion.Euler(0, 130, 0), 1.5f));
        yield return new WaitForSeconds(0.5f);

        yield return StartCoroutine(SmoothRotate(startRot, 1f));
        
        agent.isStopped = false;
        isInspecting = false;
        currentState = State.Patrol;
        
        SetRandomPatrolDestination(); // 두리번거리기 끝난 후 새로운 무작위 목적지로!
    }

    IEnumerator SmoothRotate(Quaternion targetRot, float duration)
    {
        float time = 0;
        Quaternion start = transform.rotation;
        while (time < duration)
        {
            transform.rotation = Quaternion.Slerp(start, targetRot, time / duration);
            time += Time.deltaTime;
            yield return null;
        }
        transform.rotation = targetRot;
    }

    void StartChase()
    {
        // 1. 코루틴이 충돌하지 않도록 중지 (가장 중요)
        if(isInspecting) { StopAllCoroutines(); isInspecting = false; }
        
        currentState = State.Chase; // 추격 모드(빨간불)로 전환
        Debug.Log("삐빅! 플레이어 발견! 추격합니다.");
        agent.isStopped = false; // 이동 재개
    }
    
    public void ReceiveCCTVReport(Vector3 targetLocation)
    {
        StopAllCoroutines();
        isInspecting = false;
        agent.isStopped = false;
        
        currentState = State.Alert; 
        lastKnownPlayerPos = targetLocation;
        agent.SetDestination(targetLocation);
        Debug.Log("CCTV 보고 접수! 해당 위치로 이동합니다.");
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            Debug.Log("Game Over! 플레이어가 로봇에게 잡혔습니다.");
        }
    }
    // 수정된 StopChase 함수 (플레이어를 놓쳤을 때)
    void StopChase()
    {
        currentState = State.Alert; // 경계 모드(주황불)로 전환
        Debug.Log("플레이어를 놓쳤습니다. 마지막으로 본 위치를 수색합니다.");

        // 안전장치: lastKnownPlayerPos가 유효한지 확인하고, 유효하지 않다면 일단 현재 위치를 수색합니다.
        // (Vector3.zero는 이상한 곳의 전형적인 예시)
        if (lastKnownPlayerPos == Vector3.zero || Vector3.Distance(transform.position, lastKnownPlayerPos) < 1f)
        {
            // 근처 NavMesh 위의 랜덤한 지점으로 목적지 설정
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position + transform.forward * 3f, out hit, 5.0f, NavMesh.AllAreas))
            {
                lastKnownPlayerPos = hit.position;
            }
            else
            {
                lastKnownPlayerPos = transform.position; // 그마저도 안 되면 제자리 수색
            }
        }
        
        agent.SetDestination(lastKnownPlayerPos); // 안전하게 검증된 위치로 이동
    }
}