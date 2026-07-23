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
        [SerializeField] private GameObject pickupHint;
        [SerializeField] private Animator walkieTalkieAnimator;
        [SerializeField] private SoundEmitter soundEmitter;
        [SerializeField] private MicVolumeUI micVolumeUI;

        [Header("Animation Parameters")]
        [SerializeField] private string talkingParameter = "isTalking";
        [SerializeField] private string playbackSpeedParameter = "speed";
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

        private FirstPersonController playerController;
        private NetworkObject networkObject;
        private int talkingParameterId;
        private int playbackSpeedParameterId;

        private bool CanReadLocalInput =>
            networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;

        private void Awake()
        {
            playerController = GetComponent<FirstPersonController>();
            networkObject = GetComponentInParent<NetworkObject>();
            CacheAnimationParameters();
            ResolveReferences();

            HasWalkieTalkie = startsWithWalkieTalkie;
            SetEquipped(startsWithWalkieTalkie && startsEquipped);
        }

        private void Update()
        {
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
            FirstPersonController controller,
            GameObject visual,
            GameObject hint,
            Animator animator,
            bool hasWalkieTalkie)
        {
            playerController = controller != null ? controller : playerController;
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

            if (playerController != null)
                playerController.hasWakieTakie = hasWalkieTalkie;

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

        private void CacheAnimationParameters()
        {
            talkingParameterId = Animator.StringToHash(talkingParameter);
            playbackSpeedParameterId = Animator.StringToHash(playbackSpeedParameter);
        }

        private void ApplyEquippedPresentation()
        {
            if (walkieTalkieVisual != null)
                walkieTalkieVisual.SetActive(IsEquipped);

            if (!CanReadLocalInput)
                return;

            if (IsEquipped)
                micVolumeUI?.ShowUI();
            else
                micVolumeUI?.HideUI();

            if (!IsEquipped)
                ApplyTransmissionPresentation(false);
        }

        private void ApplyTransmissionPresentation(bool transmitting)
        {
            if (walkieTalkieAnimator != null)
            {
                walkieTalkieAnimator.SetFloat(
                    playbackSpeedParameterId,
                    transmitting ? talkingPlaybackSpeed : idlePlaybackSpeed);
                walkieTalkieAnimator.SetBool(talkingParameterId, transmitting);
            }
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
