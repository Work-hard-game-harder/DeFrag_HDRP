using UnityEngine;

namespace DeFrag.B1F
{
    /// <summary>
    /// Swaps a working door for its fractured version without deactivating the door's
    /// NetworkObjects or scripts. Colliders that block the passage are disabled when breached;
    /// wall colliders stay so the room keeps its shape.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BreachableDoor : MonoBehaviour
    {
        [Tooltip("살아 있는 문(패널, 프레임, 벽, 스위치)의 렌더러. 파손되면 숨깁니다.")]
        [SerializeField] private Renderer[] intactRenderers;
        [Tooltip("파손 후 통로를 막으면 안 되는 콜라이더 (문 패널).")]
        [SerializeField] private Collider[] passageColliders;
        [Tooltip("파손 후 멈춰야 하는 컴포넌트 (자동문 센서, NavMesh 장애물 등).")]
        [SerializeField] private Behaviour[] intactBehaviours;
        [Tooltip("파손된 문 비주얼 루트 (NetworkObject가 없어야 합니다).")]
        [SerializeField] private GameObject brokenVisual;
        [Tooltip("컷씬에서 흔들리고 찌그러지는 대역 패널. 게임플레이 중에는 꺼져 있습니다.")]
        [SerializeField] private Transform standInPanel;
        [Tooltip("대역 패널이 가릴 살아 있는 문 패널 렌더러.")]
        [SerializeField] private Renderer livePanelRenderer;

        private bool standInActive;
        private Vector3 standInPosition;
        private Quaternion standInRotation;

        public bool IsBreached { get; private set; }
        public Transform StandInPanel => standInPanel;
        public GameObject BrokenVisual => brokenVisual;

        private void Awake()
        {
            if (standInPanel != null)
            {
                standInPosition = standInPanel.localPosition;
                standInRotation = standInPanel.localRotation;
                standInPanel.gameObject.SetActive(false);
            }
            if (brokenVisual != null) brokenVisual.SetActive(IsBreached);
        }

        /// <summary>Persistent world state, driven by the authoritative story stage.</summary>
        public void SetBreached(bool breached)
        {
            IsBreached = breached;
            ApplyVisuals(breached);
            foreach (Collider blocker in passageColliders)
                if (blocker != null) blocker.enabled = !breached;
            foreach (Behaviour behaviour in intactBehaviours)
                if (behaviour != null) behaviour.enabled = !breached;
        }

        /// <summary>Local presentation: hide the live panel behind a stand-in that can be shaken.</summary>
        public void SetStandInActive(bool active)
        {
            standInActive = active && standInPanel != null && !IsBreached;
            if (standInPanel != null)
            {
                standInPanel.localPosition = standInPosition;
                standInPanel.localRotation = standInRotation;
                standInPanel.gameObject.SetActive(standInActive);
            }
            if (livePanelRenderer != null) livePanelRenderer.enabled = !standInActive && !IsBreached;
        }

        /// <summary>Local presentation: show the fracture now, before the story stage catches up.</summary>
        public void ShowBrokenLocally()
        {
            if (standInPanel != null) standInPanel.gameObject.SetActive(false);
            standInActive = false;
            ApplyVisuals(true);
        }

        /// <summary>Return to whatever the authoritative state says.</summary>
        public void RestoreFromState()
        {
            SetStandInActive(false);
            ApplyVisuals(IsBreached);
        }

        private void ApplyVisuals(bool broken)
        {
            foreach (Renderer target in intactRenderers)
                if (target != null) target.enabled = !broken;
            if (livePanelRenderer != null) livePanelRenderer.enabled = !broken && !standInActive;
            if (brokenVisual != null) brokenVisual.SetActive(broken);
        }
    }
}
