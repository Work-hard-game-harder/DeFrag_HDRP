using UnityEngine;

namespace DeFrag.Combat
{
    /// <summary>
    /// 공격자가 체력 구현을 직접 알지 않도록 전달하는 불변 데미지 정보입니다.
    /// 추후 네트워크 수신기는 이 정보를 서버 검증에 사용할 수 있습니다.
    /// </summary>
    public readonly struct DamageRequest
    {
        public DamageRequest(int amount, GameObject source, Vector3 hitPoint, int attackCycleId)
        {
            Amount = Mathf.Max(0, amount);
            Source = source;
            HitPoint = hitPoint;
            AttackCycleId = attackCycleId;
        }

        public int Amount { get; }
        public GameObject Source { get; }
        public Vector3 HitPoint { get; }
        public int AttackCycleId { get; }
    }

    public interface IPlayerDamageReceiver
    {
        bool IsAlive { get; }
        bool TryApplyDamage(in DamageRequest request);
    }
}
