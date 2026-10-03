using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DeFrag.B1F
{
    /// <summary>
    /// B1F 월드 지도를 기존 Interaction HUD의 홀드 상호작용으로 열고,
    /// 해당 로컬 플레이어의 우클릭으로만 닫습니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class B1FMapInteractable : MonoBehaviour, IInteractable
    {
        [Header("Interaction")]
        [SerializeField] private string interactionText = "지도 확인 (E 꾹 누르기)";

        [Header("Local Map UI")]
        [SerializeField] private GameObject mapPanel;

        [Header("Local Map Subtitle")]
        [Tooltip("지도를 처음 열 때 지도와 동시에 재생할 자막 Trigger입니다.")]
        [SerializeField] private SubtitleTrigger subtitleOnFirstOpen;
        [Tooltip("이 퀘스트가 자막 공개 대기 중일 때만 지도 자막을 재생합니다. 비워두면 제한하지 않습니다.")]
        [SerializeField] private string requiredPendingQuestId;

        private PlayerInteraction activePlayer;
        private PersonController movement;
        private PlayerInput playerInput;
        private StarterAssetsInputs inputs;
        private CameraViewSwitcher viewSwitcher;
        private bool movementWasEnabled;
        private bool playerInputWasEnabled;
        private bool isOpen;
        private bool subtitlePresented;

        public string GetInteractionText() => interactionText;

        public bool IsHoldInteraction() => true;

        private void Awake()
        {
            if (mapPanel != null)
                mapPanel.SetActive(false);
        }

        private void Update()
        {
            if (isOpen && ClosePressedThisFrame())
                CloseMap();
        }

        private static bool ClosePressedThisFrame()
        {
            return Mouse.current != null
                ? Mouse.current.rightButton.wasPressedThisFrame
                : Input.GetMouseButtonDown(1);
        }

        public void Interact(PlayerInteraction player)
        {
            if (isOpen || player == null || mapPanel == null)
                return;

            if (!GameplayInputGate.TryAcquire(this))
                return;

            activePlayer = player;
            ResolveLocalPlayerComponents(player);

            activePlayer.CloseAllUI();
            activePlayer.TogglePlayerControl(false);
            viewSwitcher?.SetInteractionLocked(true);

            movementWasEnabled = movement != null && movement.enabled;
            playerInputWasEnabled = playerInput != null && playerInput.enabled;

            if (inputs != null)
            {
                inputs.MoveInput(Vector2.zero);
                inputs.LookInput(Vector2.zero);
                inputs.JumpInput(false);
                inputs.SprintInput(false);
            }

            if (movement != null)
                movement.enabled = false;
            if (playerInput != null)
                playerInput.enabled = false;

            mapPanel.SetActive(true);
            isOpen = true;

            if (!subtitlePresented && subtitleOnFirstOpen != null && CanPresentMapSubtitle())
            {
                subtitlePresented = true;
                subtitleOnFirstOpen.PlaySubtitleFromInteract();
            }
        }

        private bool CanPresentMapSubtitle()
        {
            if (string.IsNullOrWhiteSpace(requiredPendingQuestId))
                return true;

            return QuestManager.Instance != null &&
                   QuestManager.Instance.IsQuestPending(requiredPendingQuestId.Trim());
        }

        private void ResolveLocalPlayerComponents(PlayerInteraction player)
        {
            Transform playerRoot = player.transform.root;
            movement = playerRoot.GetComponentInChildren<PersonController>(true);
            playerInput = playerRoot.GetComponentInChildren<PlayerInput>(true);
            inputs = playerRoot.GetComponentInChildren<StarterAssetsInputs>(true);
            viewSwitcher = playerRoot.GetComponentInChildren<CameraViewSwitcher>(true);
        }

        public void CloseMap()
        {
            if (!isOpen)
                return;

            if (mapPanel != null)
                mapPanel.SetActive(false);

            viewSwitcher?.SetInteractionLocked(false);

            if (playerInput != null)
                playerInput.enabled = playerInputWasEnabled;
            if (movement != null)
                movement.enabled = movementWasEnabled;

            activePlayer?.TogglePlayerControl(true);
            GameplayInputGate.Release(this);

            activePlayer = null;
            movement = null;
            playerInput = null;
            inputs = null;
            viewSwitcher = null;
            isOpen = false;
        }

        private void OnDisable()
        {
            CloseMap();
        }

        private void OnDestroy()
        {
            CloseMap();
        }
    }
}
