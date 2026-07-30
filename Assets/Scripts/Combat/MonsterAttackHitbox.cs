using System.Collections.Generic;
using DeFrag.Monsters.Common;
using UnityEngine;

namespace DeFrag.Combat
{
    public enum MonsterHitboxShape
    {
        Sphere,
        Box,
        Capsule
    }

    /// <summary>
    /// 모든 몬스터가 공유하는 서버 권한 공격 판정입니다.
    /// 한 공격 cycle에서 물리 판정과 데미지 적용을 각각 한 번만 허용합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterAttackHitbox : MonoBehaviour
    {
        private const int HitBufferSize = 32;

        [Header("Damage")]
        [SerializeField, Min(0)] private int damage = 10;
        [SerializeField] private LayerMask targetLayers = ~0;
        [SerializeField] private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Collide;

        [Header("Hitbox")]
        [SerializeField] private MonsterHitboxShape shape = MonsterHitboxShape.Sphere;
        [SerializeField] private Transform hitboxOrigin;
        [SerializeField] private Vector3 localCenter = Vector3.zero;
        [SerializeField, Min(0.01f)] private float sphereRadius = 1.5f;
        [SerializeField] private Vector3 boxSize = new Vector3(3f, 2f, 3f);
        [SerializeField, Min(0.01f)] private float capsuleRadius = 1f;
        [SerializeField, Min(0.02f)] private float capsuleHeight = 2f;

        [Header("Contact Mode (Optional)")]
        [Tooltip("애니메이션이 없는 접촉형 몬스터만 활성화합니다.")]
        [SerializeField] private bool resolveOnContactEnter;
        [SerializeField, Min(0f)] private float contactCooldown = 1f;

        [Header("Debug")]
        [SerializeField] private Color gizmoColor = new Color(1f, 0.15f, 0.1f, 0.25f);

        private readonly Collider[] hitBuffer = new Collider[HitBufferSize];
        private readonly HashSet<PlayerStats> uniquePlayers = new HashSet<PlayerStats>();
        private int attackCycleId;
        private bool cycleActive;
        private bool hitCheckConsumed;
        private float nextContactTime;

        public int Damage => damage;
        public int AttackCycleId => attackCycleId;
        public bool IsAttackCycleActive => cycleActive;
        public bool HasResolvedCurrentCycle => hitCheckConsumed;

        public void ConfigureSphere(int amount, float radius)
        {
            damage = Mathf.Max(0, amount);
            shape = MonsterHitboxShape.Sphere;
            sphereRadius = Mathf.Max(0.01f, radius);
        }

        public int BeginAttackCycle()
        {
            attackCycleId++;
            cycleActive = true;
            hitCheckConsumed = false;
            return attackCycleId;
        }

        public void EndAttackCycle()
        {
            cycleActive = false;
            hitCheckConsumed = false;
        }

        /// <summary>
        /// Animation Event 또는 Behavior Designer의 실제 타격 시점에서 호출합니다.
        /// 클라이언트에서도 Event가 발생할 수 있지만 데미지는 서버/호스트만 확정합니다.
        /// </summary>
        public bool ResolveAttackHit()
        {
            if (!cycleActive || hitCheckConsumed)
                return false;

            // 판정 호출 자체를 한 번만 소비합니다. 빗나간 공격도 같은 cycle에서
            // 뒤늦게 재판정하지 않아 프레임 반복 데미지를 막습니다.
            hitCheckConsumed = true;

            if (!MonsterSimulationAuthority.HasServerAuthority())
                return false;

            int hitCount = CollectOverlaps();
            return TryDamageClosestPlayer(hitCount);
        }

        public void AnimationEvent_ResolveAttackHit()
        {
            ResolveAttackHit();
        }

        private int CollectOverlaps()
        {
            Transform origin = hitboxOrigin != null ? hitboxOrigin : transform;
            Vector3 center = origin.position + origin.rotation * localCenter;

            switch (shape)
            {
                case MonsterHitboxShape.Box:
                    return Physics.OverlapBoxNonAlloc(
                        center,
                        boxSize * 0.5f,
                        hitBuffer,
                        origin.rotation,
                        targetLayers,
                        triggerInteraction);

                case MonsterHitboxShape.Capsule:
                    GetCapsulePoints(origin, center, out Vector3 top, out Vector3 bottom, out float radius);
                    return Physics.OverlapCapsuleNonAlloc(
                        top,
                        bottom,
                        radius,
                        hitBuffer,
                        targetLayers,
                        triggerInteraction);

                default:
                    return Physics.OverlapSphereNonAlloc(
                        center,
                        sphereRadius,
                        hitBuffer,
                        targetLayers,
                        triggerInteraction);
            }
        }

        private bool TryDamageClosestPlayer(int hitCount)
        {
            uniquePlayers.Clear();
            IPlayerDamageReceiver closestReceiver = null;
            Vector3 closestPoint = transform.position;
            float closestSqrDistance = float.PositiveInfinity;
            Vector3 origin = GetWorldCenter();

            int safeCount = Mathf.Min(hitCount, hitBuffer.Length);
            for (int i = 0; i < safeCount; i++)
            {
                Collider hit = hitBuffer[i];
                hitBuffer[i] = null;

                if (!PlayerDamageReceiverResolver.TryResolve(
                        hit,
                        out PlayerStats stats,
                        out IPlayerDamageReceiver receiver) ||
                    !uniquePlayers.Add(stats) ||
                    !receiver.IsAlive)
                {
                    continue;
                }

                Vector3 point = hit.ClosestPoint(origin);
                float sqrDistance = (point - origin).sqrMagnitude;
                if (sqrDistance >= closestSqrDistance)
                    continue;

                closestSqrDistance = sqrDistance;
                closestReceiver = receiver;
                closestPoint = point;
            }

            if (closestReceiver == null)
                return false;

            var request = new DamageRequest(damage, gameObject, closestPoint, attackCycleId);
            return closestReceiver.TryApplyDamage(request);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!resolveOnContactEnter || collision == null)
                return;

            TryResolveContact(collision.collider);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!resolveOnContactEnter)
                return;

            TryResolveContact(other);
        }

        private void TryResolveContact(Collider targetCollider)
        {
            if (!MonsterSimulationAuthority.HasServerAuthority() || Time.time < nextContactTime)
                return;

            if (!PlayerDamageReceiverResolver.TryResolve(
                    targetCollider,
                    out _,
                    out IPlayerDamageReceiver receiver) ||
                !receiver.IsAlive)
            {
                return;
            }

            nextContactTime = Time.time + contactCooldown;
            BeginAttackCycle();
            hitCheckConsumed = true;

            Vector3 hitPoint = targetCollider.ClosestPoint(GetWorldCenter());
            receiver.TryApplyDamage(new DamageRequest(damage, gameObject, hitPoint, attackCycleId));
            EndAttackCycle();
        }

        private Vector3 GetWorldCenter()
        {
            Transform origin = hitboxOrigin != null ? hitboxOrigin : transform;
            return origin.position + origin.rotation * localCenter;
        }

        private void GetCapsulePoints(
            Transform origin,
            Vector3 center,
            out Vector3 top,
            out Vector3 bottom,
            out float radius)
        {
            radius = capsuleRadius;
            float safeHeight = Mathf.Max(radius * 2f, capsuleHeight);
            float offset = Mathf.Max(0f, safeHeight * 0.5f - radius);
            Vector3 direction = origin.up * offset;
            top = center + direction;
            bottom = center - direction;
        }

        private void OnValidate()
        {
            damage = Mathf.Max(0, damage);
            sphereRadius = Mathf.Max(0.01f, sphereRadius);
            boxSize = new Vector3(
                Mathf.Max(0.01f, boxSize.x),
                Mathf.Max(0.01f, boxSize.y),
                Mathf.Max(0.01f, boxSize.z));
            capsuleRadius = Mathf.Max(0.01f, capsuleRadius);
            capsuleHeight = Mathf.Max(capsuleRadius * 2f, capsuleHeight);
            contactCooldown = Mathf.Max(0f, contactCooldown);
        }

        private void OnDrawGizmosSelected()
        {
            Transform origin = hitboxOrigin != null ? hitboxOrigin : transform;
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.color = gizmoColor;
            // 범위 값은 몬스터 모델의 Transform scale과 무관한 월드 단위입니다.
            Gizmos.matrix = Matrix4x4.TRS(origin.position, origin.rotation, Vector3.one);

            switch (shape)
            {
                case MonsterHitboxShape.Box:
                    Gizmos.DrawCube(localCenter, boxSize);
                    break;
                case MonsterHitboxShape.Capsule:
                    // Capsule은 중심 구체와 상하 범위 선으로 간단히 시각화합니다.
                    Gizmos.DrawWireSphere(localCenter + Vector3.up * (capsuleHeight * 0.5f - capsuleRadius), capsuleRadius);
                    Gizmos.DrawWireSphere(localCenter - Vector3.up * (capsuleHeight * 0.5f - capsuleRadius), capsuleRadius);
                    Gizmos.DrawLine(
                        localCenter + new Vector3(capsuleRadius, capsuleHeight * 0.5f - capsuleRadius, 0f),
                        localCenter + new Vector3(capsuleRadius, -capsuleHeight * 0.5f + capsuleRadius, 0f));
                    Gizmos.DrawLine(
                        localCenter + new Vector3(-capsuleRadius, capsuleHeight * 0.5f - capsuleRadius, 0f),
                        localCenter + new Vector3(-capsuleRadius, -capsuleHeight * 0.5f + capsuleRadius, 0f));
                    break;
                default:
                    Gizmos.DrawSphere(localCenter, sphereRadius);
                    break;
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
