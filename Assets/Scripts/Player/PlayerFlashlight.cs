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

        public event Action<bool> StateChanged;
        public bool IsOn { get; private set; }

        private NetworkObject networkObject;

        private bool CanReadLocalInput =>
            networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;

        private void Awake()
        {
            networkObject = GetComponent<NetworkObject>();
            SetState(startsOn);
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
            IsOn = isOn;

            if (spotLight != null)
                spotLight.enabled = isOn;

            StateChanged?.Invoke(isOn);
        }
    }
}
