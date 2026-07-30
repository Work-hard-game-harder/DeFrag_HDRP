using UnityEngine;

namespace DeFrag.Combat
{
    /// <summary>
    /// Animator가 자식 오브젝트에 있을 때 Animation Event를 루트 Hitbox로 전달합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterAttackAnimationRelay : MonoBehaviour
    {
        [SerializeField] private MonsterAttackHitbox attackHitbox;

        private void Awake()
        {
            if (attackHitbox == null)
                attackHitbox = GetComponentInParent<MonsterAttackHitbox>();
        }

        public void AnimationEvent_BeginAttackCycle()
        {
            attackHitbox?.BeginAttackCycle();
        }

        public void AnimationEvent_ResolveAttackHit()
        {
            attackHitbox?.ResolveAttackHit();
        }

        public void AnimationEvent_EndAttackCycle()
        {
            attackHitbox?.EndAttackCycle();
        }
    }
}
