using System;
using BehaviorDesigner.Runtime;
using DeFrag.Combat;
using Unity.Netcode;
using UnityEngine;

namespace DeFrag.Monsters.Common
{
    /// <summary>
    /// 몬스터가 사용할 플레이어 타깃을 서버의 실제 PlayerObject 목록에서 선택합니다.
    /// 네트워크 세션이 없을 때는 기존 Inspector 타깃을 변경하지 않습니다.
    /// </summary>
    public interface IMonsterPlayerTargetReceiver
    {
        void SetPlayerTarget(Transform target);
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    public sealed class NetworkMonsterPlayerTargetResolver : MonoBehaviour
    {
        [Header("Behavior Designer (Optional)")]
        [SerializeField] private BehaviorTree behaviorTree;
        [SerializeField] private string targetVariableName = "PlayerTarget";

        [Header("Target Selection")]
        [Min(0.05f)]
        [SerializeField] private float refreshInterval = 0.2f;

        private SharedTransform behaviorTarget;
        private IMonsterPlayerTargetReceiver[] targetReceivers =
            Array.Empty<IMonsterPlayerTargetReceiver>();
        private Transform currentTarget;
        private bool hasAppliedNetworkTarget;
        private float nextRefreshTime;

        public Transform CurrentTarget => currentTarget;
        public event Action<Transform> TargetChanged;

        private void Awake()
        {
            ResolveBindings();
        }

        private void OnEnable()
        {
            nextRefreshTime = 0f;
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;

            // 단독 실행에서는 B1F의 player 필드나 B2F의 SharedTransform처럼
            // Inspector에 연결된 기존 테스트 타깃을 그대로 유지합니다.
            if (manager == null || !manager.IsListening || !manager.IsServer)
                return;

            if (Time.time < nextRefreshTime)
                return;

            nextRefreshTime = Time.time + refreshInterval;

            // Behavior Tree가 런타임 Installer에서 늦게 만들어지는 경우를 지원합니다.
            if (behaviorTarget == null && behaviorTree != null)
                ResolveBehaviorTarget();

            if (currentTarget != null && !IsLivingPlayer(manager, currentTarget))
                SetTarget(null);
        }

        public bool TryAcquireNearestLivingPlayer(out Transform target)
        {
            target = null;
            if (!TryGetAuthoritativeManager(out NetworkManager manager))
                return false;

            target = FindNearestLivingPlayer(manager);
            if (target == null)
                return false;

            SetTarget(target);
            return true;
        }

        public bool TryAcquirePlayerInRange(float range, out Transform target)
        {
            target = null;
            if (!TryGetAuthoritativeManager(out NetworkManager manager))
                return false;

            float safeRange = Mathf.Max(0f, range);
            float rangeSqr = safeRange * safeRange;
            float nearestSqrDistance = float.MaxValue;

            foreach (NetworkClient client in manager.ConnectedClients.Values)
            {
                if (!TryGetLivingPlayer(client, out Transform candidate))
                    continue;

                float sqrDistance = (candidate.position - transform.position).sqrMagnitude;
                if (sqrDistance > rangeSqr || sqrDistance >= nearestSqrDistance)
                    continue;

                nearestSqrDistance = sqrDistance;
                target = candidate;
            }

            if (target == null)
                return false;

            SetTarget(target);
            return true;
        }

        public bool TryAcquireVisiblePlayer(
            float viewDistance,
            float fieldOfView,
            LayerMask obstacleMask,
            float eyeHeight,
            float targetHeightOffset,
            out Transform target)
        {
            target = null;
            if (!TryGetAuthoritativeManager(out NetworkManager manager))
                return false;

            // 현재 추적 대상이 계속 보인다면 유지하여 두 플레이어 사이에서
            // 타깃이 매 갱신마다 흔들리는 현상을 방지합니다.
            if (IsLivingPlayer(manager, currentTarget) &&
                MonsterPerceptionUtility.CanSeeTarget(
                    transform,
                    currentTarget,
                    viewDistance,
                    fieldOfView,
                    obstacleMask,
                    eyeHeight,
                    targetHeightOffset))
            {
                target = currentTarget;
                return true;
            }

            float nearestSqrDistance = float.MaxValue;
            foreach (NetworkClient client in manager.ConnectedClients.Values)
            {
                if (!TryGetLivingPlayer(client, out Transform candidate) ||
                    !MonsterPerceptionUtility.CanSeeTarget(
                        transform,
                        candidate,
                        viewDistance,
                        fieldOfView,
                        obstacleMask,
                        eyeHeight,
                        targetHeightOffset))
                {
                    continue;
                }

                float sqrDistance = (candidate.position - transform.position).sqrMagnitude;
                if (sqrDistance >= nearestSqrDistance)
                    continue;

                nearestSqrDistance = sqrDistance;
                target = candidate;
            }

            if (target == null)
                return false;

            SetTarget(target);
            return true;
        }

        public bool TrySetCurrentTarget(Transform candidate, out Transform playerRoot)
        {
            playerRoot = null;
            if (!TryGetAuthoritativeManager(out NetworkManager manager) || candidate == null)
                return false;

            foreach (NetworkClient client in manager.ConnectedClients.Values)
            {
                if (!TryGetLivingPlayer(client, out Transform root))
                    continue;

                if (candidate != root && !candidate.IsChildOf(root))
                    continue;

                playerRoot = root;
                SetTarget(root);
                return true;
            }

            return false;
        }

        public void BindBehaviorTree(BehaviorTree tree)
        {
            behaviorTree = tree;
            ResolveBehaviorTarget();

            if (behaviorTarget != null)
                behaviorTarget.Value = currentTarget;
        }

        public void RefreshNow()
        {
            nextRefreshTime = 0f;
        }

        private void ResolveBindings()
        {
            if (behaviorTree == null)
                behaviorTree = GetComponent<BehaviorTree>();

            ResolveBehaviorTarget();

            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            int receiverCount = 0;
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IMonsterPlayerTargetReceiver)
                    receiverCount++;
            }

            if (receiverCount == 0)
            {
                targetReceivers = Array.Empty<IMonsterPlayerTargetReceiver>();
                return;
            }

            targetReceivers = new IMonsterPlayerTargetReceiver[receiverCount];
            int index = 0;
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IMonsterPlayerTargetReceiver receiver)
                    targetReceivers[index++] = receiver;
            }
        }

        private void ResolveBehaviorTarget()
        {
            behaviorTarget = null;
            if (behaviorTree == null || string.IsNullOrWhiteSpace(targetVariableName))
                return;

            behaviorTarget = behaviorTree.GetVariable(targetVariableName) as SharedTransform;
        }

        private Transform FindNearestLivingPlayer(NetworkManager manager)
        {
            Transform nearest = null;
            float nearestSqrDistance = float.MaxValue;

            foreach (NetworkClient client in manager.ConnectedClients.Values)
            {
                if (!TryGetLivingPlayer(client, out Transform candidate))
                    continue;

                float sqrDistance =
                    (candidate.position - transform.position).sqrMagnitude;
                if (sqrDistance >= nearestSqrDistance)
                    continue;

                nearestSqrDistance = sqrDistance;
                nearest = candidate;
            }

            return nearest;
        }

        private static bool TryGetLivingPlayer(NetworkClient client, out Transform target)
        {
            target = null;
            NetworkObject playerObject = client?.PlayerObject;
            if (playerObject == null || !playerObject.IsSpawned)
                return false;

            PlayerStats playerStats = playerObject.GetComponent<PlayerStats>();
            if (playerStats == null || playerStats.IsDead)
                return false;

            NetworkPlayerHealth networkHealth = playerObject.GetComponent<NetworkPlayerHealth>();
            if (networkHealth != null && !networkHealth.IsAlive)
                return false;

            target = playerObject.transform;
            return true;
        }

        private static bool IsLivingPlayer(NetworkManager manager, Transform candidate)
        {
            if (candidate == null)
                return false;

            foreach (NetworkClient client in manager.ConnectedClients.Values)
            {
                if (TryGetLivingPlayer(client, out Transform target) && target == candidate)
                    return true;
            }

            return false;
        }

        private static bool TryGetAuthoritativeManager(out NetworkManager manager)
        {
            manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && manager.IsServer;
        }

        private void SetTarget(Transform target)
        {
            if (hasAppliedNetworkTarget && currentTarget == target)
                return;

            hasAppliedNetworkTarget = true;
            currentTarget = target;

            if (behaviorTarget != null)
                behaviorTarget.Value = target;

            foreach (IMonsterPlayerTargetReceiver receiver in targetReceivers)
                receiver.SetPlayerTarget(target);

            TargetChanged?.Invoke(target);
        }

        private void OnValidate()
        {
            refreshInterval = Mathf.Max(0.05f, refreshInterval);
            if (string.IsNullOrWhiteSpace(targetVariableName))
                targetVariableName = "PlayerTarget";
        }
    }
}
