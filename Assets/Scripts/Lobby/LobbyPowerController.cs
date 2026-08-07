using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace DeFrag.Lobby
{
    public enum LobbyPowerState
    {
        FullPower,
        EmergencyPower,
        PowerOff
    }

    [DisallowMultipleComponent]
    public sealed class LobbyPowerController : MonoBehaviour
    {
        [Header("Power roots")]
        [SerializeField] private GameObject fullPower;
        [SerializeField] private GameObject emergencyPower;
        [SerializeField] private GameObject powerOff;
        [SerializeField] private LobbyPowerState initialState = LobbyPowerState.FullPower;

        [Header("Warning flicker")]
        [Tooltip("FullPower 아래에서 실제로 깜빡일 일부 조명 오브젝트만 지정합니다.")]
        [SerializeField] private GameObject[] flickerTargets;
        [Min(1)] [SerializeField] private int flickerCount = 3;
        [Min(0f)] [SerializeField] private float firstOffDuration = 0.8f;
        [Min(0f)] [SerializeField] private float onDuration = 0.65f;
        [Min(0f)] [SerializeField] private float offDuration = 0.65f;

        [Header("Events")]
        [SerializeField] private UnityEvent onEmergencyPowerStarted;

        private Coroutine flickerRoutine;

        public LobbyPowerState CurrentState { get; private set; }

        private void Awake()
        {
            ApplyState(initialState);
        }

        public void PlayHintWarning(bool switchToEmergencyAfterFlicker)
        {
            if (CurrentState != LobbyPowerState.FullPower)
                return;

            if (flickerRoutine != null)
                StopCoroutine(flickerRoutine);

            SetFlickerTargetsActive(true);
            flickerRoutine = StartCoroutine(
                FlickerRoutine(switchToEmergencyAfterFlicker));
        }

        public void SetFullPower() => ApplyState(LobbyPowerState.FullPower);
        public void SetEmergencyPower() => ApplyState(LobbyPowerState.EmergencyPower);
        public void SetPowerOff() => ApplyState(LobbyPowerState.PowerOff);

        private IEnumerator FlickerRoutine(bool switchToEmergencyAfterFlicker)
        {
            SetFlickerTargetsActive(false);
            yield return new WaitForSeconds(firstOffDuration);

            for (int i = 0; i < flickerCount; i++)
            {
                SetFlickerTargetsActive(true);
                yield return new WaitForSeconds(onDuration);
                SetFlickerTargetsActive(false);
                yield return new WaitForSeconds(offDuration);
            }

            SetFlickerTargetsActive(true);
            flickerRoutine = null;

            if (switchToEmergencyAfterFlicker)
                ApplyState(LobbyPowerState.EmergencyPower);
        }

        private void ApplyState(LobbyPowerState state)
        {
            if (flickerRoutine != null)
            {
                StopCoroutine(flickerRoutine);
                flickerRoutine = null;
            }

            SetFlickerTargetsActive(true);
            CurrentState = state;

            if (fullPower != null)
                fullPower.SetActive(state == LobbyPowerState.FullPower);
            if (emergencyPower != null)
                emergencyPower.SetActive(state == LobbyPowerState.EmergencyPower);
            if (powerOff != null)
                powerOff.SetActive(state == LobbyPowerState.PowerOff);

            if (state == LobbyPowerState.EmergencyPower)
                onEmergencyPowerStarted?.Invoke();
        }

        private void SetFlickerTargetsActive(bool active)
        {
            foreach (GameObject target in flickerTargets)
            {
                if (target != null)
                    target.SetActive(active);
            }
        }
    }
}
