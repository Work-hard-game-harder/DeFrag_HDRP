using System;
using Unity.Netcode;
using UnityEngine;

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
            if (CanReadLocalInput && !SettingManager.IsGamePaused &&
                !GameplayInputGate.IsBlocked && Input.GetKeyDown(toggleKey))
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
