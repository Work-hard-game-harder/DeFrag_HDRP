using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몬스터가 추적 대상(플레이어)과의 거리가 너무 벌어졌을 때,
/// 대상 근처의 NavMesh 위 지점으로 순간이동(Warp)시켜 따라잡게 하는 역할만 담당하는 클래스.
///
/// MonsterAI는 "지금 캐치업이 필요한 상태인지"만 판단해서 이 클래스에 위임하고,
/// 실제 위치 샘플링/쿨다운 관리는 이 클래스가 캡슐화한다 (책임 분리).
/// </summary>
public class CatchUpNavigator
{
    private readonly NavMeshAgent agent;
    private readonly float triggerDistance;
    private readonly float searchRadius;
    private readonly float cooldown;
    private readonly int sampleAttempts;

    private float cooldownTimer;

    /// <param name="agent">몬스터의 NavMeshAgent</param>
    /// <param name="triggerDistance">이 거리를 넘으면 캐치업을 시도</param>
    /// <param name="searchRadius">대상 주변 이 반경 안에서 착지 지점을 탐색</param>
    /// <param name="cooldown">연속 텔레포트 방지용 쿨다운(초)</param>
    /// <param name="sampleAttempts">착지 지점을 몇 번까지 재시도해서 탐색할지</param>
    public CatchUpNavigator(NavMeshAgent agent, float triggerDistance, float searchRadius, float cooldown, int sampleAttempts = 12)
    {
        this.agent = agent;
        this.triggerDistance = triggerDistance;
        this.searchRadius = searchRadius;
        this.cooldown = cooldown;
        this.sampleAttempts = Mathf.Max(1, sampleAttempts);
    }

    /// <summary>
    /// 매 프레임 호출. 조건을 만족하면 내부적으로 Warp를 실행하고 true를 반환한다.
    /// </summary>
    /// <param name="currentPosition">몬스터의 현재 위치</param>
    /// <param name="targetPosition">추적 대상(플레이어)의 현재 위치</param>
    /// <param name="isPursuing">지금 추적 관련 상태(Chase/Attack/Investigate 등)인지</param>
    public bool TryCatchUp(Vector3 currentPosition, Vector3 targetPosition, bool isPursuing)
    {
        cooldownTimer -= Time.deltaTime;

        if (!isPursuing) return false;
        if (cooldownTimer > 0f) return false;

        float distance = Vector3.Distance(currentPosition, targetPosition);
        if (distance < triggerDistance) return false;

        if (!TryFindPointNear(targetPosition, out Vector3 warpPoint))
            return false;

        agent.Warp(warpPoint);
        cooldownTimer = cooldown;
        return true;
    }

    public void ResetCooldown()
    {
        cooldownTimer = 0f;
    }

    private bool TryFindPointNear(Vector3 center, out Vector3 result)
    {
        result = center;

        for (int i = 0; i < sampleAttempts; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * searchRadius;
            Vector3 candidate = center + new Vector3(randomCircle.x, 0f, randomCircle.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, searchRadius, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }

        return false;
    }
}