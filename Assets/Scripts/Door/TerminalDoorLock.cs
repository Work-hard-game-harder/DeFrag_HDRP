using UnityEngine;
using UnityEngine.AI;

namespace DeFrag.Doors
{
    [RequireComponent(typeof(VerticalDoorMotor))]
    public sealed class TerminalDoorLock : MonoBehaviour
    {
        [Header("Monster Navigation")]
        [Tooltip("잠긴 동안 TV몬스터의 NavMesh 경로를 막습니다. 비어 있으면 문 BoxCollider에 맞춰 자동 생성합니다.")]
        [SerializeField] private NavMeshObstacle lockedNavigationBlocker;

        private VerticalDoorMotor door;

        public bool IsUnlocked { get; private set; }

        private void Awake()
        {
            door = GetComponent<VerticalDoorMotor>();
            door.SetAccessLocked(!IsUnlocked);
            EnsureNavigationBlocker();
            SetNavigationBlocked(!IsUnlocked);
        }

        public void Unlock()
        {
            if (IsUnlocked)
                return;

            IsUnlocked = true;
            door.SetAccessLocked(false);
            SetNavigationBlocked(false);
            door.Open();
        }

        private void EnsureNavigationBlocker()
        {
            if (lockedNavigationBlocker == null)
                lockedNavigationBlocker = GetComponent<NavMeshObstacle>();

            if (lockedNavigationBlocker == null)
                lockedNavigationBlocker = gameObject.AddComponent<NavMeshObstacle>();

            if (TryGetComponent(out BoxCollider boxCollider))
            {
                lockedNavigationBlocker.shape = NavMeshObstacleShape.Box;
                lockedNavigationBlocker.center = boxCollider.center;
                lockedNavigationBlocker.size = boxCollider.size;
            }

            lockedNavigationBlocker.carving = true;
            lockedNavigationBlocker.carveOnlyStationary = true;
        }

        private void SetNavigationBlocked(bool blocked)
        {
            if (lockedNavigationBlocker != null)
                lockedNavigationBlocker.enabled = blocked;
        }
    }
}
