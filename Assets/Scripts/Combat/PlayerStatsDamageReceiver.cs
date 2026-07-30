using UnityEngine;

namespace DeFrag.Combat
{
    /// <summary>
    /// 현재 비네트워크 PlayerStats를 공용 데미지 계약에 연결합니다.
    /// 체력 감소와 사망 처리는 항상 PlayerStats.TakeDamage에 위임합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStatsDamageReceiver : MonoBehaviour, IPlayerDamageReceiver
    {
        [SerializeField] private PlayerStats playerStats;

        public bool IsAlive => playerStats != null && !playerStats.IsDead;

        private void Awake()
        {
            ResolvePlayerStats();
        }

        private void Reset()
        {
            ResolvePlayerStats();
        }

        public void Bind(PlayerStats stats)
        {
            playerStats = stats;
        }

        public bool TryApplyDamage(in DamageRequest request)
        {
            if (!IsAlive || request.Amount <= 0)
                return false;

            return playerStats.TakeDamage(request.Amount);
        }

        private void ResolvePlayerStats()
        {
            if (playerStats == null)
                playerStats = GetComponent<PlayerStats>();
        }
    }

    /// <summary>
    /// Collider의 부모 플레이어에서 네트워크/로컬 데미지 수신기를 찾습니다.
    /// 별도 네트워크 수신기가 없다면 현재 PlayerStats 어댑터를 자동 설치합니다.
    /// </summary>
    public static class PlayerDamageReceiverResolver
    {
        public static bool TryResolve(
            Collider targetCollider,
            out PlayerStats playerStats,
            out IPlayerDamageReceiver receiver)
        {
            playerStats = null;
            receiver = null;

            if (targetCollider == null)
                return false;

            // 자식 Collider에 맞더라도 플레이어 루트의 PlayerStats를 기준으로 합니다.
            playerStats = targetCollider.GetComponentInParent<PlayerStats>();
            if (playerStats == null)
                return false;

            PlayerStatsDamageReceiver localFallback = null;
            MonoBehaviour[] behaviours = playerStats.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IPlayerDamageReceiver candidate)
                {
                    // 추후 서버 권한 NetworkHealth 수신기가 추가되면 기존 로컬 어댑터보다
                    // 우선 선택하여 공격 측 코드를 수정하지 않고 네트워크 체력으로 전환합니다.
                    if (candidate is PlayerStatsDamageReceiver fallbackCandidate)
                    {
                        localFallback = fallbackCandidate;
                        continue;
                    }

                    receiver = candidate;
                    return receiver.IsAlive;
                }
            }

            // 호스트 시험용 기본 어댑터입니다. 추후 네트워크 수신 컴포넌트가
            // 프리팹에 붙으면 위 분기에서 우선 선택되어 이 경로는 실행되지 않습니다.
            PlayerStatsDamageReceiver fallback = localFallback;
            if (fallback == null)
                fallback = playerStats.gameObject.AddComponent<PlayerStatsDamageReceiver>();

            fallback.Bind(playerStats);
            receiver = fallback;
            return receiver.IsAlive;
        }
    }
}
