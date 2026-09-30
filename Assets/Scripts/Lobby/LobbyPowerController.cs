using System.Collections;
using System;
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

        [Header("SFX")]
        [Tooltip("비어 있으면 이 오브젝트에 2D AudioSource를 자동 생성합니다.")]
        [SerializeField] private AudioSource powerSfxSource;
        [SerializeField] private AudioClip flickerClip;
        [SerializeField] private AudioClip powerDownClip;
        [Range(0f, 1f)] [SerializeField] private float flickerVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float powerDownVolume = 1f;

        [Header("Events")]
        [SerializeField] private UnityEvent onEmergencyPowerStarted;

        private Coroutine flickerRoutine;
        private bool initialized;

        public LobbyPowerState CurrentState { get; private set; }
        public event Action EmergencyPowerStarted;

        private void Awake()
        {
            EnsureAudioSource();
            ApplyState(initialState);
            initialized = true;
        }

        public void PlayHintWarning(bool switchToEmergencyAfterFlicker)
        {
            if (CurrentState != LobbyPowerState.FullPower)
                return;

            if (flickerRoutine != null)
                StopCoroutine(flickerRoutine);

            SetFlickerTargetsActive(true);
            PlayFlickerSfx();
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
            LobbyPowerState previousState = CurrentState;
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

            if (initialized && state != previousState && state != LobbyPowerState.FullPower)
                PlayPowerDownSfx();

            if (state == LobbyPowerState.EmergencyPower)
            {
                onEmergencyPowerStarted?.Invoke();
                EmergencyPowerStarted?.Invoke();
            }
        }

        private void SetFlickerTargetsActive(bool active)
        {
            foreach (GameObject target in flickerTargets)
            {
                if (target != null)
                    target.SetActive(active);
            }
        }

        private void EnsureAudioSource()
        {
            if (powerSfxSource == null)
                powerSfxSource = GetComponent<AudioSource>();
            if (powerSfxSource == null)
                powerSfxSource = gameObject.AddComponent<AudioSource>();

            powerSfxSource.playOnAwake = false;
            powerSfxSource.loop = false;
            powerSfxSource.spatialBlend = 0f;
            powerSfxSource.dopplerLevel = 0f;
        }

        private void PlayFlickerSfx()
        {
            if (powerSfxSource == null || flickerClip == null)
                return;

            // 겹친 점멸 요청은 기존 소리를 중첩하지 않고 새 점멸부터 다시 들려준다.
            powerSfxSource.Stop();
            powerSfxSource.PlayOneShot(flickerClip, flickerVolume);
        }

        private void PlayPowerDownSfx()
        {
            if (powerSfxSource == null || powerDownClip == null)
                return;

            // 최종 소등음이 점멸음 뒤에 명확히 들리도록 점멸음을 정리한다.
            powerSfxSource.Stop();
            powerSfxSource.PlayOneShot(powerDownClip, powerDownVolume);
        }
    }
}
