using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DeFrag.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerFlashlight : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private KeyCode toggleKey = KeyCode.T;

        [Header("Light")]
        [SerializeField] private Light spotLight;
        [SerializeField] private bool startsOn;

        [Header("SFX")]
        [Tooltip("비어 있으면 PlayerVoice가 있는 루트가 아닌 Spot Light 오브젝트에 자동 생성합니다.")]
        [SerializeField] private AudioSource flashlightSfxSource;
        [SerializeField] private AudioClip turnOnClip;
        [SerializeField] private AudioClip turnOffClip;
        [Range(0f, 1f)] [SerializeField] private float turnOnVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float turnOffVolume = 1f;

        public event Action<bool> StateChanged;
        public bool IsOn { get; private set; }

        private NetworkObject networkObject;
        private bool initialized;

        private bool CanReadLocalInput =>
            networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;

        private void Awake()
        {
            networkObject = GetComponent<NetworkObject>();
            EnsureAudioSource();
            ApplyState(startsOn, false);
            initialized = true;
        }

        private void Update()
        {
            bool togglePressed = Keyboard.current != null
                ? Keyboard.current.tKey.wasPressedThisFrame
                : Input.GetKeyDown(toggleKey);
            if (!togglePressed)
                return;

            if (!CanReadLocalInput)
            {
                Debug.LogWarning(
                    $"[PlayerFlashlight] T ignored: local ownership is false. " +
                    $"Spawned={networkObject != null && networkObject.IsSpawned}, " +
                    $"Owner={networkObject != null && networkObject.IsOwner}.",
                    this);
                return;
            }

            if (SettingManager.IsGamePaused)
                return;

            if (GameplayInputGate.IsBlocked)
            {
                Debug.LogWarning(
                    $"[PlayerFlashlight] T blocked by {GameplayInputGate.BlockingOwnerName}.",
                    this);
                return;
            }

            SetState(!IsOn);
        }

        /// <summary>
        /// Networking can call this method after replicating the flashlight state.
        /// </summary>
        public void SetState(bool isOn)
        {
            ApplyState(isOn, initialized);
        }

        private void ApplyState(bool isOn, bool playSfx)
        {
            bool changed = IsOn != isOn;
            IsOn = isOn;

            if (spotLight != null)
                spotLight.enabled = isOn;

            if (changed && playSfx)
                PlayToggleSfx(isOn);

            StateChanged?.Invoke(isOn);
        }

        private void EnsureAudioSource()
        {
            if (flashlightSfxSource != null || spotLight == null)
                return;

            flashlightSfxSource = spotLight.GetComponent<AudioSource>();
            if (flashlightSfxSource == null)
                flashlightSfxSource = spotLight.gameObject.AddComponent<AudioSource>();

            flashlightSfxSource.playOnAwake = false;
            flashlightSfxSource.loop = false;
            flashlightSfxSource.spatialBlend = 1f;
            flashlightSfxSource.dopplerLevel = 0f;
            flashlightSfxSource.rolloffMode = AudioRolloffMode.Linear;
            flashlightSfxSource.minDistance = 0.5f;
            flashlightSfxSource.maxDistance = 12f;
        }

        private void PlayToggleSfx(bool isOn)
        {
            if (flashlightSfxSource == null)
                return;

            AudioClip clip = isOn ? turnOnClip : turnOffClip;
            if (clip == null)
                return;

            flashlightSfxSource.PlayOneShot(
                clip,
                isOn ? turnOnVolume : turnOffVolume);
        }
    }
}
