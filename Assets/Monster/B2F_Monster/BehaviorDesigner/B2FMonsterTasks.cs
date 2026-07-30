using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using DeFrag.Monsters.B2F;
using DeFrag.Monsters.Common;
using DeFrag.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace DeFrag.Monsters.B2F.BehaviorDesignerTasks
{
    internal static class B2FMonsterTaskUtility
    {
        public static bool HasSimulationAuthority()
        {
            return MonsterSimulationAuthority.HasServerAuthority();
        }

        public static bool IsAgentReady(NavMeshAgent agent)
        {
            return MonsterNavMeshUtility.IsAgentReady(agent);
        }

        public static bool TryPlaceAgentOnNavMesh(NavMeshAgent agent, float maxDistance)
        {
            return MonsterNavMeshUtility.TryPlaceAgentOnNavMesh(agent, maxDistance);
        }

        public static Animator FindAnimator(GameObject owner, GameObject overrideObject)
        {
            return MonsterAnimatorUtility.FindAnimator(owner, overrideObject);
        }

        public static bool HasBoolParameter(Animator animator, int parameterHash)
        {
            return MonsterAnimatorUtility.HasBoolParameter(animator, parameterHash);
        }
    }

    [TaskCategory("B2F Monster")]
    [TaskDescription("PlayerTarget이 attackRange 안에 있는지 검사합니다.")]
    public sealed class WithinAttackRange : Conditional
    {
        public SharedTransform playerTarget;
        public SharedFloat attackRange = 1.5f;

        public override TaskStatus OnUpdate()
        {
            if (playerTarget.Value == null || attackRange.Value < 0f)
                return TaskStatus.Failure;

            float sqrDistance = (playerTarget.Value.position - transform.position).sqrMagnitude;
            return sqrDistance <= attackRange.Value * attackRange.Value
                ? TaskStatus.Success
                : TaskStatus.Failure;
        }
    }

    [TaskCategory("B2F Monster")]
    [TaskDescription("거리, 시야각, 장애물 레이캐스트를 이용해 플레이어를 감지합니다.")]
    public sealed class CanSeePlayer : Conditional
    {
        public SharedTransform playerTarget;
        public SharedFloat viewDistance = 20f;
        public SharedFloat fieldOfView = 120f;
        public SharedLayerMask obstacleMask;
        public SharedFloat eyeHeight = 1.5f;
        public SharedFloat targetHeightOffset = 1f;

        public override TaskStatus OnUpdate()
        {
            Transform target = playerTarget.Value;
            if (target == null)
                return TaskStatus.Failure;

            Vector3 origin = transform.position + Vector3.up * eyeHeight.Value;
            Vector3 targetPoint = target.position + Vector3.up * targetHeightOffset.Value;
            Vector3 toTarget = targetPoint - origin;
            float distance = toTarget.magnitude;

            if (distance > viewDistance.Value || distance <= Mathf.Epsilon)
                return TaskStatus.Failure;

            if (Vector3.Angle(transform.forward, toTarget) > fieldOfView.Value * 0.5f)
                return TaskStatus.Failure;

            bool blocked = Physics.Raycast(
                origin,
                toTarget / distance,
                distance,
                obstacleMask.Value,
                QueryTriggerInteraction.Ignore);

            return blocked ? TaskStatus.Failure : TaskStatus.Success;
        }
    }

    [TaskCategory("B2F Monster")]
    [TaskDescription("기존 ChaseDetourNavigator를 이용해 플레이어를 추격합니다.")]
    public sealed class ChaseWithDetour : Action
    {
        public SharedTransform playerTarget;
        public SharedFloat stoppingDistance = 1.5f;
        public SharedInt detourSampleCount = 8;
        public SharedFloat detourSampleRadius = 6f;
        public SharedFloat detourRecheckInterval = 0.4f;

        private NavMeshAgent agent;
        private ChaseDetourNavigator navigator;
        private float originalStoppingDistance;

        public override void OnAwake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        public override void OnStart()
        {
            if (agent == null)
                return;

            originalStoppingDistance = agent.stoppingDistance;
            agent.stoppingDistance = Mathf.Max(0f, stoppingDistance.Value);
            navigator = new ChaseDetourNavigator(
                agent,
                Mathf.Max(4, detourSampleCount.Value),
                Mathf.Max(0.1f, detourSampleRadius.Value),
                Mathf.Max(0.05f, detourRecheckInterval.Value));

            if (B2FMonsterTaskUtility.IsAgentReady(agent))
                agent.isStopped = false;
        }

        public override TaskStatus OnUpdate()
        {
            if (!B2FMonsterTaskUtility.HasSimulationAuthority())
                return TaskStatus.Running;

            Transform target = playerTarget.Value;
            if (target == null || !B2FMonsterTaskUtility.IsAgentReady(agent) || navigator == null)
                return TaskStatus.Failure;

            if ((target.position - transform.position).sqrMagnitude <= stoppingDistance.Value * stoppingDistance.Value)
                return TaskStatus.Success;

            navigator.MoveTowards(target.position);
            return TaskStatus.Running;
        }

        public override void OnEnd()
        {
            navigator?.Reset();
            if (agent != null)
                agent.stoppingDistance = originalStoppingDistance;
        }
    }

    [TaskCategory("B2F Monster")]
    [TaskDescription("현재 위치 주변의 NavMesh에서 도달 가능한 랜덤 순찰 목적지를 선택합니다.")]
    public sealed class SetRandomPatrolDestination : Action
    {
        public SharedVector3 patrolDestination;
        public SharedFloat patrolRadius = 20f;
        public SharedInt sampleAttempts = 30;
        public SharedFloat sampleDistance = 5f;

        private NavMeshAgent agent;
        private NavMeshPath path;

        public override void OnAwake()
        {
            agent = GetComponent<NavMeshAgent>();
            path = new NavMeshPath();
        }

        public override TaskStatus OnUpdate()
        {
            if (!B2FMonsterTaskUtility.HasSimulationAuthority())
                return TaskStatus.Running;

            if (!B2FMonsterTaskUtility.TryPlaceAgentOnNavMesh(agent, sampleDistance.Value))
                return TaskStatus.Failure;

            int attempts = Mathf.Max(1, sampleAttempts.Value);
            float radius = Mathf.Max(0.1f, patrolRadius.Value);
            float maxSampleDistance = Mathf.Max(0.1f, sampleDistance.Value);

            for (int i = 0; i < attempts; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * radius;
                Vector3 candidate = transform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, maxSampleDistance, agent.areaMask))
                    continue;

                if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                    continue;

                patrolDestination.Value = hit.position;
                return TaskStatus.Success;
            }

            return TaskStatus.Failure;
        }
    }

    [TaskCategory("B2F Monster")]
    [TaskDescription("랜덤 순찰 목적지까지 이동하며 방향 회전과 끼임 감지를 처리합니다.")]
    public sealed class MoveToPatrolDestination : Action
    {
        public SharedVector3 patrolDestination;
        public SharedFloat walkSpeed = 2f;
        public SharedFloat stoppingDistance = 0.5f;
        public SharedFloat rotationSpeed = 10f;
        public SharedFloat stuckCheckInterval = 1f;
        public SharedFloat stuckThreshold = 0.1f;
        public SharedString idleParameter = "isIdle";
        public SharedString walkingParameter = "isWalking";

        private NavMeshAgent agent;
        private Animator animator;
        private Vector3 lastPosition;
        private float stuckTimer;
        private float originalSpeed;
        private float originalStoppingDistance;
        private int idleHash;
        private int walkingHash;
        private bool controlsIdle;
        private bool controlsWalking;

        public override void OnAwake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = B2FMonsterTaskUtility.FindAnimator(gameObject, null);
        }

        public override void OnStart()
        {
            if (!B2FMonsterTaskUtility.IsAgentReady(agent))
                return;

            originalSpeed = agent.speed;
            originalStoppingDistance = agent.stoppingDistance;
            agent.speed = Mathf.Max(0f, walkSpeed.Value);
            agent.stoppingDistance = Mathf.Max(0f, stoppingDistance.Value);
            agent.isStopped = false;
            agent.SetDestination(patrolDestination.Value);

            lastPosition = transform.position;
            stuckTimer = 0f;
            CacheAnimationParameters();
            SetPatrolAnimation(false, true);
        }

        public override TaskStatus OnUpdate()
        {
            if (!B2FMonsterTaskUtility.HasSimulationAuthority())
                return TaskStatus.Running;

            if (!B2FMonsterTaskUtility.IsAgentReady(agent))
                return TaskStatus.Failure;

            RotateTowardsMovement();

            if (IsStuck())
            {
                agent.ResetPath();
                return TaskStatus.Failure;
            }

            if (agent.pathPending)
                return TaskStatus.Running;

            if (!agent.hasPath)
                return TaskStatus.Failure;

            if (agent.remainingDistance <= agent.stoppingDistance)
            {
                agent.ResetPath();
                return TaskStatus.Success;
            }

            return TaskStatus.Running;
        }

        public override void OnEnd()
        {
            SetPatrolAnimation(false, false);
            stuckTimer = 0f;

            if (B2FMonsterTaskUtility.IsAgentReady(agent))
            {
                agent.ResetPath();
                agent.speed = originalSpeed;
                agent.stoppingDistance = originalStoppingDistance;
            }
        }

        private void RotateTowardsMovement()
        {
            if (agent.velocity.sqrMagnitude <= 0.01f)
                return;

            Vector3 direction = agent.velocity.normalized;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.01f)
                return;

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Mathf.Max(0f, rotationSpeed.Value) * Time.deltaTime);
        }

        private bool IsStuck()
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer < Mathf.Max(0.05f, stuckCheckInterval.Value))
                return false;

            float movedDistance = Vector3.Distance(transform.position, lastPosition);
            lastPosition = transform.position;
            stuckTimer = 0f;
            return agent.hasPath && movedDistance < Mathf.Max(0f, stuckThreshold.Value);
        }

        private void CacheAnimationParameters()
        {
            idleHash = Animator.StringToHash(idleParameter.Value ?? string.Empty);
            walkingHash = Animator.StringToHash(walkingParameter.Value ?? string.Empty);
            controlsIdle = B2FMonsterTaskUtility.HasBoolParameter(animator, idleHash);
            controlsWalking = B2FMonsterTaskUtility.HasBoolParameter(animator, walkingHash);
        }

        private void SetPatrolAnimation(bool idle, bool walking)
        {
            if (controlsIdle)
                animator.SetBool(idleHash, idle);
            if (controlsWalking)
                animator.SetBool(walkingHash, walking);
        }
    }

    [TaskCategory("B2F Monster")]
    [TaskDescription("B2F 순찰 이동 애니메이션으로 전환합니다.")]
    public sealed class SetPatrolMovingAnimation : Action
    {
        public SharedString idleParameter = "isIdle";
        public SharedString walkingParameter = "isWalking";

        private Animator animator;

        public override void OnAwake()
        {
            animator = B2FMonsterTaskUtility.FindAnimator(gameObject, null);
        }

        public override TaskStatus OnUpdate()
        {
            if (animator == null)
                return TaskStatus.Success;

            int idleHash = Animator.StringToHash(idleParameter.Value ?? string.Empty);
            int walkingHash = Animator.StringToHash(walkingParameter.Value ?? string.Empty);

            if (B2FMonsterTaskUtility.HasBoolParameter(animator, idleHash))
                animator.SetBool(idleHash, false);
            if (B2FMonsterTaskUtility.HasBoolParameter(animator, walkingHash))
                animator.SetBool(walkingHash, true);

            return TaskStatus.Success;
        }
    }

    [TaskCategory("B2F Monster")]
    [TaskDescription("순찰 목적지에 도착한 뒤 Idle 애니메이션으로 전환합니다.")]
    public sealed class SetPatrolIdleAnimation : Action
    {
        public SharedString idleParameter = "isIdle";
        public SharedString walkingParameter = "isWalking";

        private Animator animator;

        public override void OnAwake()
        {
            animator = B2FMonsterTaskUtility.FindAnimator(gameObject, null);
        }

        public override TaskStatus OnUpdate()
        {
            if (animator == null)
                return TaskStatus.Success;

            int idleHash = Animator.StringToHash(idleParameter.Value ?? string.Empty);
            int walkingHash = Animator.StringToHash(walkingParameter.Value ?? string.Empty);

            if (B2FMonsterTaskUtility.HasBoolParameter(animator, walkingHash))
                animator.SetBool(walkingHash, false);
            if (B2FMonsterTaskUtility.HasBoolParameter(animator, idleHash))
                animator.SetBool(idleHash, true);

            return TaskStatus.Success;
        }
    }

    [TaskCategory("B2F Monster")]
    [TaskDescription("녹음 음성 목록에서 한 클립을 골라 재생하고 말하기 애니메이션을 제어합니다.")]
    public sealed class PlayMimicVoice : Action
    {
        // Preserved so existing serialized Behavior Trees remain compatible.
        // Runtime recordings are owned by B2FMonsterVoiceMimic.
        public SharedAudioClipList mimicVoiceClips;
        public SharedGameObject audioSourceObject;
        public SharedGameObject animatorObject;
        public SharedString talkingParameter = "isTalking";
        public SharedFloat volume = 1f;

        private B2FMonsterVoiceMimic mimicController;
        private Animator animator;
        private int talkingHash;
        private bool controlsTalkingParameter;
        private bool playbackStarted;

        public override void OnStart()
        {
            GameObject controllerObject =
                audioSourceObject.Value != null ? audioSourceObject.Value : gameObject;
            mimicController = controllerObject.GetComponent<B2FMonsterVoiceMimic>();
            animator = B2FMonsterTaskUtility.FindAnimator(gameObject, animatorObject.Value);
            talkingHash = Animator.StringToHash(talkingParameter.Value ?? string.Empty);
            controlsTalkingParameter = B2FMonsterTaskUtility.HasBoolParameter(animator, talkingHash);

            playbackStarted =
                mimicController != null && mimicController.TryStartNextPlayback(volume.Value);

            if (!playbackStarted)
            {
                mimicController?.RefreshMimicList();
                return;
            }

            if (controlsTalkingParameter)
                animator.SetBool(talkingHash, true);
        }

        public override TaskStatus OnUpdate()
        {
            if (!playbackStarted || mimicController == null)
                return TaskStatus.Success;

            if (!mimicController.IsActivePlaybackComplete)
                return TaskStatus.Running;

            mimicController.CompleteActivePlayback();
            playbackStarted = false;
            SetTalking(false);
            return TaskStatus.Success;
        }

        public override void OnEnd()
        {
            if (playbackStarted && mimicController != null)
                mimicController.CancelActivePlayback();

            playbackStarted = false;
            SetTalking(false);
        }

        private void SetTalking(bool value)
        {
            if (controlsTalkingParameter && animator != null)
                animator.SetBool(talkingHash, value);
        }
    }

    [TaskCategory("B2F Monster")]
    [TaskDescription("공격 애니메이션을 시작하고 서버 권한에서 플레이어에게 피해를 적용합니다.")]
    public sealed class AttackPlayer : Action
    {
        public SharedTransform playerTarget;
        public SharedString attackTrigger = "Attack";
        public SharedFloat damageDelay = 0.35f;
        public SharedFloat attackDuration = 1f;

        private NavMeshAgent agent;
        private Animator animator;
        private MonsterAttackHitbox attackHitbox;
        private float startTime;
        private bool hitCheckRequested;
        private bool wasStopped;

        public override void OnAwake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = B2FMonsterTaskUtility.FindAnimator(gameObject, null);
            attackHitbox = GetComponent<MonsterAttackHitbox>();

            // 기존에 생성된 Behavior Tree/프리팹도 즉시 동작하도록 하는 마이그레이션 폴백입니다.
            // 새로 트리를 생성할 때는 Builder가 같은 컴포넌트를 미리 추가합니다.
            if (attackHitbox == null)
            {
                attackHitbox = gameObject.AddComponent<MonsterAttackHitbox>();
                attackHitbox.ConfigureSphere(10, 1.5f);
            }
        }

        public override void OnStart()
        {
            startTime = Time.time;
            hitCheckRequested = false;
            attackHitbox?.BeginAttackCycle();

            if (B2FMonsterTaskUtility.IsAgentReady(agent))
            {
                wasStopped = agent.isStopped;
                agent.isStopped = true;
            }

            if (animator != null && !string.IsNullOrWhiteSpace(attackTrigger.Value))
                animator.SetTrigger(attackTrigger.Value);
        }

        public override TaskStatus OnUpdate()
        {
            Transform target = playerTarget.Value;
            if (target == null)
                return TaskStatus.Failure;

            if (!hitCheckRequested && Time.time >= startTime + Mathf.Max(0f, damageDelay.Value))
            {
                hitCheckRequested = true;
                attackHitbox?.ResolveAttackHit();
            }

            return Time.time >= startTime + Mathf.Max(damageDelay.Value, attackDuration.Value)
                ? TaskStatus.Success
                : TaskStatus.Running;
        }

        public override void OnEnd()
        {
            attackHitbox?.EndAttackCycle();

            if (B2FMonsterTaskUtility.IsAgentReady(agent))
                agent.isStopped = wasStopped;
        }
    }
}
