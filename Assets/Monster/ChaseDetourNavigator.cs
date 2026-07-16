using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Chase 상태에서 목표(플레이어)까지의 NavMesh 경로가 벽 등으로 인해
/// 끊겨있거나(PathPartial/PathInvalid) 물리적으로 막혀 진행이 안 될 때,
/// 목표 방향 주변에서 실제로 도달 가능한 우회 지점을 찾아주는 역할만 담당하는 클래스.
///
/// MonsterAI는 이 클래스의 존재를 통해서만 목적지를 요청하고,
/// "어떻게 우회할지"에 대한 세부 구현은 알 필요가 없다 (책임 분리).
/// </summary>
public class ChaseDetourNavigator
{
    private readonly NavMeshAgent agent;
    private readonly int sampleCount;
    private readonly float detourSampleRadius;
    private readonly float recheckInterval;

    private readonly NavMeshPath scratchPath;
    private float recheckTimer;
    private bool usingDetour;
    private Vector3 currentDetourPoint;
    private Vector3 lastRequestedTarget;

    /// <param name="agent">몬스터의 NavMeshAgent</param>
    /// <param name="sampleCount">목표 주변에서 우회 지점을 몇 방향으로 탐색할지</param>
    /// <param name="detourSampleRadius">우회 지점을 탐색할 반경</param>
    /// <param name="recheckInterval">경로 유효성을 재검사하는 주기(초). 매 프레임 CalculatePath를 호출하면 비용이 크므로 주기적으로 검사</param>
    public ChaseDetourNavigator(NavMeshAgent agent, int sampleCount = 8, float detourSampleRadius = 6f, float recheckInterval = 0.4f)
    {
        this.agent = agent;
        this.sampleCount = Mathf.Max(4, sampleCount);
        this.detourSampleRadius = detourSampleRadius;
        this.recheckInterval = recheckInterval;
        scratchPath = new NavMeshPath();
    }

    /// <summary>
    /// 매 프레임 호출. target으로 향하되, 필요시 자동으로 우회 지점을 경유하게 한다.
    /// </summary>
    public void MoveTowards(Vector3 target)
    {
        // 목표 지점이 바뀌면 즉시 재평가 (플레이어가 계속 움직이므로)
        bool targetChanged = Vector3.Distance(target, lastRequestedTarget) > 0.5f;
        lastRequestedTarget = target;

        recheckTimer -= Time.deltaTime;
        if (recheckTimer > 0f && !targetChanged)
        {
            // 우회 중이면서 우회 지점에 거의 도달했다면, 다시 원래 목표로 재시도
            if (usingDetour && HasArrivedAt(currentDetourPoint))
                recheckTimer = 0f;
            else
                return; // 아직 재검사 시점이 아니면 기존 목적지 유지
        }
        recheckTimer = recheckInterval;

        if (IsPathReachable(target))
        {
            usingDetour = false;
            agent.SetDestination(target);
            return;
        }

        // 직접 경로가 막혀있음 -> 우회 지점 탐색
        if (TryFindDetourPoint(target, out Vector3 detourPoint))
        {
            usingDetour = true;
            currentDetourPoint = detourPoint;
            agent.SetDestination(detourPoint);
        }
        else
        {
            // 우회 지점을 못 찾으면 최소한 direct destination이라도 시도 (NavMeshAgent가 partial path로 최대한 접근)
            usingDetour = false;
            agent.SetDestination(target);
        }
    }

    public void Reset()
    {
        usingDetour = false;
        recheckTimer = 0f;
        lastRequestedTarget = Vector3.positiveInfinity;
    }

    private bool HasArrivedAt(Vector3 point)
    {
        return !agent.pathPending && agent.remainingDistance < 0.5f;
    }

    private bool IsPathReachable(Vector3 target)
    {
        if (!agent.CalculatePath(target, scratchPath))
            return false;
        return scratchPath.status == NavMeshPathStatus.PathComplete;
    }

    /// <summary>
    /// target을 중심으로 sampleCount 방향을 돌며, agent가 완전한 경로로 도달 가능한
    /// NavMesh 위 지점 중 target과 가장 가까운 지점을 반환한다.
    /// (하드코딩된 좌표 없이, agent의 현재 위치와 target을 기준으로 동적으로 탐색)
    /// </summary>
    private bool TryFindDetourPoint(Vector3 target, out Vector3 result)
    {
        result = target;
        float bestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < sampleCount; i++)
        {
            float angle = (360f / sampleCount) * i;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * detourSampleRadius;
            Vector3 candidate = target + offset;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, detourSampleRadius, NavMesh.AllAreas))
                continue;

            if (!agent.CalculatePath(hit.position, scratchPath))
                continue;
            if (scratchPath.status != NavMeshPathStatus.PathComplete)
                continue;

            float distToTarget = Vector3.Distance(hit.position, target);
            if (distToTarget < bestDistance)
            {
                bestDistance = distToTarget;
                result = hit.position;
                found = true;
            }
        }

        return found;
    }
}