using System;
using Unity.Netcode;
using UnityEngine;

namespace DeFrag.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerStamina : MonoBehaviour, ISprintGate
    {
        [Header("Capacity")]
        [Min(0.01f)] [SerializeField] private float maximumStamina = 10f;
        [Min(0.01f)] [SerializeField] private float sprintDurationAtFullStamina = 10f;

        [Header("Recovery")]
        [Min(0f)] [SerializeField] private float recoveryDelay = 3f;
        [Min(0.01f)] [SerializeField] private float fullRecoveryDuration = 20f;
        [Range(0f, 1f)] [SerializeField] private float exhaustedSprintThreshold = 1f / 3f;

        public event Action Exhausted;
        public event Action<float, float> StaminaChanged;
        public event Action<bool> SprintStateChanged;

        public float CurrentStamina { get; private set; }
        public float MaximumStamina => maximumStamina;
        public bool IsExhausted { get; private set; }
        public bool IsSprinting => isSprinting;
        public bool CanSprint => isLocalOwner && !IsExhausted && CurrentStamina > 0f;

        private NetworkObject networkObject;
        private bool isLocalOwner;
        private bool isSprinting;
        private float recoveryDelayRemaining;

        private void Awake()
        {
            networkObject = GetComponent<NetworkObject>();
            CurrentStamina = maximumStamina;
        }

        private void OnEnable()
        {
            RefreshOwnership();
        }

        private void Update()
        {
            RefreshOwnership();
            if (!isLocalOwner)
                return;

            if (isSprinting && !IsExhausted)
            {
                float drainPerSecond = maximumStamina / sprintDurationAtFullStamina;
                SetCurrentStamina(CurrentStamina - drainPerSecond * Time.deltaTime);

                if (CurrentStamina <= 0f)
                    EnterExhaustedState();

                return;
            }

            if (recoveryDelayRemaining > 0f)
            {
                recoveryDelayRemaining -= Time.deltaTime;
                return;
            }

            if (CurrentStamina < maximumStamina)
            {
                float recoveryPerSecond = maximumStamina / fullRecoveryDuration;
                SetCurrentStamina(CurrentStamina + recoveryPerSecond * Time.deltaTime);
            }

            if (IsExhausted && CurrentStamina >= maximumStamina * exhaustedSprintThreshold)
                IsExhausted = false;
        }

        public void SetSprinting(bool isSprinting)
        {
            bool nextState = isSprinting && CanSprint;
            if (this.isSprinting == nextState)
                return;

            this.isSprinting = nextState;
            SprintStateChanged?.Invoke(this.isSprinting);
        }

        private void EnterExhaustedState()
        {
            IsExhausted = true;
            SetSprinting(false);
            recoveryDelayRemaining = recoveryDelay;
            Exhausted?.Invoke();
        }

        private void SetCurrentStamina(float value)
        {
            float clampedValue = Mathf.Clamp(value, 0f, maximumStamina);
            if (Mathf.Approximately(CurrentStamina, clampedValue))
                return;

            CurrentStamina = clampedValue;
            StaminaChanged?.Invoke(CurrentStamina, maximumStamina);
        }

        private void RefreshOwnership()
        {
            isLocalOwner = networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;
            if (!isLocalOwner)
                SetSprinting(false);
        }

        private void OnValidate()
        {
            maximumStamina = Mathf.Max(0.01f, maximumStamina);
            sprintDurationAtFullStamina = Mathf.Max(0.01f, sprintDurationAtFullStamina);
            fullRecoveryDuration = Mathf.Max(0.01f, fullRecoveryDuration);
        }
    }
}
