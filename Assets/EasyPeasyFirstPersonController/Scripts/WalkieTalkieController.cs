using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyPeasyFirstPersonController
{
    /// <summary>
    /// Controls walkie-talkie possession, presentation, and push-to-talk without
    /// replacing the player's locomotion state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WalkieTalkieController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private KeyCode equipToggleKey = KeyCode.R;
        [SerializeField, Min(0)] private int transmitMouseButton;
        [SerializeField] private bool blockTransmissionOverUI = true;

        [Header("References")]
        [SerializeField] private GameObject walkieTalkieVisual;
        [Tooltip("네트워크 플레이어처럼 프리팹 내부에 1인칭 모델이 없을 때 소유자에게만 생성할 프리팹입니다.")]
        [SerializeField] private GameObject walkieTalkieVisualPrefab;
        [Tooltip("생성된 1인칭 워키토키 모델의 부모입니다. 비어 있으면 소유자 카메라를 사용합니다.")]
        [SerializeField] private Transform visualParent;
        [SerializeField] private GameObject pickupHint;
        [SerializeField] private Animator walkieTalkieAnimator;
        [SerializeField] private SoundEmitter soundEmitter;
        [SerializeField] private MicVolumeUI micVolumeUI;

        [Header("Animation Parameters")]
        [SerializeField] private string talkingParameter = "isTalking";
        [SerializeField] private string playbackSpeedParameter = "Speed";
        [SerializeField] private float talkingPlaybackSpeed = 1f;
        [SerializeField] private float idlePlaybackSpeed = -1f;

        [Header("Initial State")]
        [SerializeField] private bool startsWithWalkieTalkie;
        [SerializeField] private bool startsEquipped;

        public event Action<bool> EquippedChanged;
        public event Action TransmissionStarted;
        public event Action TransmissionStopped;

        public bool HasWalkieTalkie { get; private set; }
        public bool IsEquipped { get; private set; }
        public bool IsTransmitting { get; private set; }
        public bool IsLocalOwner => CanReadLocalInput;

        private NetworkObject networkObject;
        private int talkingParameterId;
        private int playbackSpeedParameterId;

        private bool CanReadLocalInput =>
            networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;

        private void Awake()
        {
            networkObject = GetComponentInParent<NetworkObject>();
            CacheAnimationParameters();
            ResolveReferences();

            HasWalkieTalkie = startsWithWalkieTalkie;
            SetEquipped(startsWithWalkieTalkie && startsEquipped);
        }

        private void Start()
        {
            EnsureLocalVisual();
            ResolveReferences();
            ApplyEquippedPresentation();
        }

        private void Update()
        {
            if (GameplayInputGate.IsBlocked)
                return;

            if (!CanReadLocalInput)
                return;

            if (SettingManager.IsGamePaused || GameState.isCutscene)
            {
                EndTransmission();
                return;
            }

            if (Input.GetKeyDown(equipToggleKey) && HasWalkieTalkie)
                SetEquipped(!IsEquipped);

            if (pickupHint != null && pickupHint.activeSelf &&
                Input.GetMouseButtonDown(transmitMouseButton))
            {
                pickupHint.SetActive(false);
            }

            if (!IsEquipped)
                return;

            if (Input.GetMouseButtonDown(transmitMouseButton) && CanStartTransmission())
                BeginTransmission();

            if (Input.GetMouseButtonUp(transmitMouseButton))
                EndTransmission();
        }

        public void Configure(
            GameObject visual,
            GameObject hint,
            Animator animator,
            bool hasWalkieTalkie)
        {
            if (visual != null) walkieTalkieVisual = visual;
            if (hint != null) pickupHint = hint;
            if (animator != null) walkieTalkieAnimator = animator;

            ResolveReferences();
            SetPossession(hasWalkieTalkie || HasWalkieTalkie, false);
        }

        public void Acquire()
        {
            SetPossession(true, true);
        }

        /// <summary>
        /// Networking code can call this after synchronizing possession.
        /// Local input is still read only by the owning player.
        /// </summary>
        public void SetPossession(bool hasWalkieTalkie, bool showPickupHint)
        {
            HasWalkieTalkie = hasWalkieTalkie;
            startsWithWalkieTalkie = hasWalkieTalkie;

            if (hasWalkieTalkie)
                EnsureLocalVisual();

            if (pickupHint != null)
                pickupHint.SetActive(hasWalkieTalkie && showPickupHint);

            if (!hasWalkieTalkie)
                SetEquipped(false);
        }

        /// <summary>
        /// Networking or replay presentation can call this without simulating input.
        /// </summary>
        public void SetEquipped(bool equipped)
        {
            bool nextEquipped = HasWalkieTalkie && equipped;
            if (IsEquipped == nextEquipped)
            {
                ApplyEquippedPresentation();
                return;
            }

            if (!nextEquipped)
                EndTransmission();

            IsEquipped = nextEquipped;
            ApplyEquippedPresentation();
            EquippedChanged?.Invoke(IsEquipped);
        }

        public bool BeginTransmission()
        {
            if (!CanReadLocalInput || !IsEquipped || IsTransmitting)
                return false;

            ResolveReferences();
            IsTransmitting = true;

            soundEmitter?.StartMic();
            ApplyTransmissionPresentation(true);
            TransmissionStarted?.Invoke();
            return true;
        }

        public void EndTransmission()
        {
            if (!IsTransmitting)
                return;

            // Let recording listeners consume their final buffered samples before
            // SoundEmitter may close a microphone that it owns.
            IsTransmitting = false;
            TransmissionStopped?.Invoke();
            soundEmitter?.StopMic();
            ApplyTransmissionPresentation(false);
        }

        private bool CanStartTransmission()
        {
            // A locked gameplay cursor commonly sits over crosshair/HUD graphics.
            // Those graphics must not block push-to-talk during normal gameplay.
            if (Cursor.lockState == CursorLockMode.Locked && !Cursor.visible)
                return true;

            if (!blockTransmissionOverUI || EventSystem.current == null)
                return true;

            return !EventSystem.current.IsPointerOverGameObject();
        }

        private void ResolveReferences()
        {
            if (walkieTalkieAnimator == null && walkieTalkieVisual != null)
                walkieTalkieAnimator = walkieTalkieVisual.GetComponentInChildren<Animator>(true);

            if (soundEmitter == null)
                soundEmitter = GetComponentInChildren<SoundEmitter>(true);

            if (micVolumeUI == null && CanReadLocalInput)
                micVolumeUI = FindAnyObjectByType<MicVolumeUI>();
        }

        private void EnsureLocalVisual()
        {
            if (walkieTalkieVisual != null || walkieTalkieVisualPrefab == null || !CanReadLocalInput)
                return;

            Transform parent = visualParent;
            if (parent == null)
            {
                Camera ownerCamera = GetComponentInChildren<Camera>(true);
                if (ownerCamera != null)
                    parent = ownerCamera.transform;
            }

            if (parent == null)
                return;

            walkieTalkieVisual = Instantiate(walkieTalkieVisualPrefab, parent, false);
            walkieTalkieVisual.name = walkieTalkieVisualPrefab.name;
            SetLayerRecursively(walkieTalkieVisual, LayerMask.NameToLayer("Ignore Raycast"));

            foreach (Collider visualCollider in walkieTalkieVisual.GetComponentsInChildren<Collider>(true))
                visualCollider.enabled = false;

            walkieTalkieVisual.SetActive(false);
            walkieTalkieAnimator = walkieTalkieVisual.GetComponentInChildren<Animator>(true);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null || layer < 0)
                return;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        private void CacheAnimationParameters()
        {
            talkingParameterId = Animator.StringToHash(talkingParameter);
            playbackSpeedParameterId = Animator.StringToHash(playbackSpeedParameter);
        }

        private void ApplyEquippedPresentation()
        {
            // Animator가 재생 가능한 상태일 때 전송 파라미터를 먼저 초기화합니다.
            // 비주얼을 먼저 끄면 비활성 Animator에 SetBool/SetFloat을 호출하게 됩니다.
            if (!IsEquipped)
                ApplyTransmissionPresentation(false);

            if (walkieTalkieVisual != null)
                walkieTalkieVisual.SetActive(IsEquipped);

            if (!CanReadLocalInput)
                return;

            if (IsEquipped)
                micVolumeUI?.ShowUI();
            else
                micVolumeUI?.HideUI();

        }

        private void ApplyTransmissionPresentation(bool transmitting)
        {
            if (walkieTalkieAnimator == null ||
                walkieTalkieAnimator.runtimeAnimatorController == null ||
                !walkieTalkieAnimator.isActiveAndEnabled ||
                !walkieTalkieAnimator.gameObject.activeInHierarchy)
            {
                return;
            }

            walkieTalkieAnimator.SetFloat(
                playbackSpeedParameterId,
                transmitting ? talkingPlaybackSpeed : idlePlaybackSpeed);
            walkieTalkieAnimator.SetBool(talkingParameterId, transmitting);
        }

        private void OnDisable()
        {
            EndTransmission();

            if (walkieTalkieVisual != null)
                walkieTalkieVisual.SetActive(false);

            if (CanReadLocalInput)
                micVolumeUI?.HideUI();
        }
    }
}
