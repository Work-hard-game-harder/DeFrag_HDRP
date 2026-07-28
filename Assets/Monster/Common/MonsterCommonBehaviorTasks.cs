using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace DeFrag.Monsters.Common
{
    public static class MonsterSimulationAuthority
    {
        public static bool HasServerAuthority()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager == null || !manager.IsListening || manager.IsServer;
        }
    }

    public static class MonsterNavMeshUtility
    {
        public static bool IsAgentReady(NavMeshAgent agent)
        {
            return agent != null && agent.enabled && agent.isOnNavMesh;
        }

        public static bool TryPlaceAgentOnNavMesh(NavMeshAgent agent, float maxDistance)
        {
            if (IsAgentReady(agent))
                return true;

            if (agent == null || !agent.enabled)
                return false;

            if (!NavMesh.SamplePosition(
                    agent.transform.position,
                    out NavMeshHit hit,
                    Mathf.Max(0.1f, maxDistance),
                    agent.areaMask))
            {
                return false;
            }

            return agent.Warp(hit.position) && agent.isOnNavMesh;
        }

        public static bool TryFindRandomReachablePosition(
            NavMeshAgent agent,
            Vector3 center,
            float radius,
            int attempts,
            float sampleDistance,
            NavMeshPath path,
            out Vector3 destination)
        {
            destination = center;
            if (!IsAgentReady(agent) || path == null)
                return false;

            int safeAttempts = Mathf.Max(1, attempts);
            float safeRadius = Mathf.Max(0.1f, radius);
            float safeSampleDistance = Mathf.Max(0.1f, sampleDistance);

            for (int i = 0; i < safeAttempts; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * safeRadius;
                Vector3 candidate = center + new Vector3(randomCircle.x, 0f, randomCircle.y);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, safeSampleDistance, agent.areaMask))
                    continue;

                if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                    continue;

                destination = hit.position;
                return true;
            }

            return false;
        }

        public static void RotateTowardsMovement(
            Transform movingTransform,
            NavMeshAgent agent,
            float rotationSpeed)
        {
            if (movingTransform == null || !IsAgentReady(agent))
                return;

            Vector3 direction = agent.desiredVelocity;
            if (direction.sqrMagnitude <= 0.01f)
                direction = agent.velocity;

            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.01f)
                return;

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            movingTransform.rotation = Quaternion.Slerp(
                movingTransform.rotation,
                targetRotation,
                Mathf.Max(0f, rotationSpeed) * Time.deltaTime);
        }
    }

    public static class MonsterPerceptionUtility
    {
        public static bool IsWithinDistance(Vector3 origin, Vector3 target, float distance)
        {
            float safeDistance = Mathf.Max(0f, distance);
            return (target - origin).sqrMagnitude <= safeDistance * safeDistance;
        }

        public static bool CanSeeTarget(
            Transform observer,
            Transform target,
            float viewDistance,
            float fieldOfView,
            LayerMask obstacleMask,
            float eyeHeight,
            float targetHeightOffset)
        {
            if (observer == null || target == null)
                return false;

            Vector3 origin = observer.position + Vector3.up * eyeHeight;
            Vector3 targetPoint = target.position + Vector3.up * targetHeightOffset;
            Vector3 toTarget = targetPoint - origin;
            float distance = toTarget.magnitude;

            if (distance <= Mathf.Epsilon || distance > Mathf.Max(0f, viewDistance))
                return false;

            if (Vector3.Angle(observer.forward, toTarget) > Mathf.Clamp(fieldOfView, 0f, 360f) * 0.5f)
                return false;

            return !Physics.Raycast(
                origin,
                toTarget / distance,
                distance,
                obstacleMask,
                QueryTriggerInteraction.Ignore);
        }
    }

    public static class MonsterAnimatorUtility
    {
        public static Animator FindAnimator(GameObject owner, GameObject overrideObject = null)
        {
            GameObject root = overrideObject != null ? overrideObject : owner;
            if (root == null)
                return null;

            Animator animator = root.GetComponent<Animator>();
            return animator != null ? animator : root.GetComponentInChildren<Animator>(true);
        }

        public static bool HasBoolParameter(Animator animator, int parameterHash)
        {
            if (animator == null)
                return false;

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == parameterHash && parameter.type == AnimatorControllerParameterType.Bool)
                    return true;
            }

            return false;
        }
    }
}

namespace DeFrag.Monsters.Common.BehaviorDesignerTasks
{
    using DeFrag.Monsters.Common;

    [TaskCategory("Monster/Common")]
    [TaskDescription("공유 Target이 지정된 거리 안에 있는지 검사합니다.")]
    public class WithinTargetDistance : Conditional
    {
        public SharedTransform target;
        public SharedFloat distance = 1.5f;

        public override TaskStatus OnUpdate()
        {
            if (target.Value == null)
                return TaskStatus.Failure;

            return MonsterPerceptionUtility.IsWithinDistance(transform.position, target.Value.position, distance.Value)
                ? TaskStatus.Success
                : TaskStatus.Failure;
        }
    }

    [TaskCategory("Monster/Common")]
    [TaskDescription("거리, 시야각, 장애물 마스크를 이용해 공유 Target을 검사합니다.")]
    public class CanSeeTarget : Conditional
    {
        public SharedTransform target;
        public SharedFloat viewDistance = 20f;
        public SharedFloat fieldOfView = 120f;
        public SharedLayerMask obstacleMask;
        public SharedFloat eyeHeight = 1.5f;
        public SharedFloat targetHeightOffset = 1f;

        public override TaskStatus OnUpdate()
        {
            return MonsterPerceptionUtility.CanSeeTarget(
                    transform,
                    target.Value,
                    viewDistance.Value,
                    fieldOfView.Value,
                    obstacleMask.Value,
                    eyeHeight.Value,
                    targetHeightOffset.Value)
                ? TaskStatus.Success
                : TaskStatus.Failure;
        }
    }

    [TaskCategory("Monster/Common")]
    [TaskDescription("기존 ChaseDetourNavigator를 이용해 공유 Target을 추격합니다.")]
    public class ChaseTargetWithDetour : Action
    {
        public SharedTransform target;
        public SharedFloat stoppingDistance = 1.5f;
        public SharedFloat rotationSpeed = 10f;
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

            if (MonsterNavMeshUtility.IsAgentReady(agent))
                agent.isStopped = false;
        }

        public override TaskStatus OnUpdate()
        {
            if (!MonsterSimulationAuthority.HasServerAuthority())
                return TaskStatus.Running;

            Transform currentTarget = target.Value;
            if (currentTarget == null || !MonsterNavMeshUtility.IsAgentReady(agent) || navigator == null)
                return TaskStatus.Failure;

            if (MonsterPerceptionUtility.IsWithinDistance(
                    transform.position,
                    currentTarget.position,
                    stoppingDistance.Value))
            {
                return TaskStatus.Success;
            }

            navigator.MoveTowards(currentTarget.position);
            MonsterNavMeshUtility.RotateTowardsMovement(transform, agent, rotationSpeed.Value);
            return TaskStatus.Running;
        }

        public override void OnEnd()
        {
            navigator?.Reset();
            if (agent != null)
                agent.stoppingDistance = originalStoppingDistance;
        }
    }

    [TaskCategory("Monster/Common")]
    [TaskDescription("현재 위치 주변의 NavMesh에서 도달 가능한 랜덤 목적지를 선택합니다.")]
    public class SetRandomNavMeshDestination : Action
    {
        public SharedVector3 destination;
        public SharedFloat radius = 20f;
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
            if (!MonsterSimulationAuthority.HasServerAuthority())
                return TaskStatus.Running;

            if (!MonsterNavMeshUtility.TryPlaceAgentOnNavMesh(agent, sampleDistance.Value))
                return TaskStatus.Failure;

            bool found = MonsterNavMeshUtility.TryFindRandomReachablePosition(
                    agent,
                    transform.position,
                    radius.Value,
                    sampleAttempts.Value,
                    sampleDistance.Value,
                    path,
                    out Vector3 result);

            if (found)
                destination.Value = result;

            return found ? TaskStatus.Success : TaskStatus.Failure;
        }
    }

    [TaskCategory("Monster/Common")]
    [TaskDescription("공유 NavMesh 목적지까지 이동하고 회전 및 끼임을 검사합니다.")]
    public class MoveToNavMeshDestination : Action
    {
        public SharedVector3 destination;
        public SharedFloat moveSpeed = 2f;
        public SharedFloat stoppingDistance = 0.5f;
        public SharedFloat rotationSpeed = 10f;
        public SharedFloat stuckCheckInterval = 1f;
        public SharedFloat stuckThreshold = 0.1f;

        private NavMeshAgent agent;
        private Vector3 lastPosition;
        private float stuckTimer;
        private float originalSpeed;
        private float originalStoppingDistance;

        public override void OnAwake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        public override void OnStart()
        {
            if (!MonsterNavMeshUtility.IsAgentReady(agent))
                return;

            originalSpeed = agent.speed;
            originalStoppingDistance = agent.stoppingDistance;
            agent.speed = Mathf.Max(0f, moveSpeed.Value);
            agent.stoppingDistance = Mathf.Max(0f, stoppingDistance.Value);
            agent.isStopped = false;
            agent.SetDestination(destination.Value);
            lastPosition = transform.position;
            stuckTimer = 0f;
        }

        public override TaskStatus OnUpdate()
        {
            if (!MonsterSimulationAuthority.HasServerAuthority())
                return TaskStatus.Running;

            if (!MonsterNavMeshUtility.IsAgentReady(agent))
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
            stuckTimer = 0f;
            if (!MonsterNavMeshUtility.IsAgentReady(agent))
                return;

            agent.ResetPath();
            agent.speed = originalSpeed;
            agent.stoppingDistance = originalStoppingDistance;
        }

        private void RotateTowardsMovement()
        {
            MonsterNavMeshUtility.RotateTowardsMovement(transform, agent, rotationSpeed.Value);
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
    }
}
