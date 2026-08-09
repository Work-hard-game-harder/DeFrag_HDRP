using UnityEngine;
using StarterAssets;

namespace EasyPeasyFirstPersonController
{
    public class SoundEmitter : MonoBehaviour
    {
        [Header("Sound Settings")]
        public float soundBaseRange = 15f;
        public float soundRangeMultiplier = 10f;
        public float micSensitivity = 100f;
        public float noiseThreshold = 0.01f;

        [Header("Continuous Voice Detection")]
        [Tooltip("워키토키 사용 여부와 관계없이 플레이어 목소리를 계속 감지합니다.")]
        [SerializeField] private bool listenContinuously = true;

        [Header("Network Voice Relay")]
        [Tooltip("소유 클라이언트가 서버에 마이크 레벨을 전송하는 간격입니다.")]
        [SerializeField, Min(0.05f)] private float networkPublishInterval = 0.1f;
        [Tooltip("원격 음성 갱신이 끊겼을 때 서버가 음성을 비활성화하는 시간입니다.")]
        [SerializeField, Min(0.1f)] private float networkSilenceTimeout = 0.35f;

        private AudioClip micInput;
        private string micDevice;
        private bool ownsMicrophoneCapture;
        private bool isWalkieTransmitting;
        private PersonController networkController;
        private float nextNetworkPublishTime;
        private float lastNetworkVoiceUpdateTime;

        public bool IsMicActive { get; private set; }
        public float CurrentVolume { get; private set; }
        public bool IsWalkieTransmitting => isWalkieTransmitting;
        public AudioClip MicrophoneClip => micInput;
        public string MicrophoneDevice => micDevice;

        public float CurrentSoundRange =>
            IsMicActive ? soundBaseRange + CurrentVolume * soundRangeMultiplier : 0f;

        private void Start()
        {
            networkController = GetComponentInParent<PersonController>();
            if (!HasLocalMicrophoneAuthority())
                return;

            InitializeMicrophone();

            // 일반 게임에서는 SettingManager의 지속 캡처 버퍼를 공유합니다.
            // SettingManager가 없는 독립 테스트 씬에서만 직접 마이크를 엽니다.
            if (listenContinuously && SettingManager.Instance == null)
                StartOwnedMicrophone();
        }

        private void Update()
        {
            if (!HasLocalMicrophoneAuthority())
            {
                ExpireStaleNetworkVoice();
                return;

            }

            if (!TryUpdateFromManagedMicrophone())
            {
                IsMicActive = ownsMicrophoneCapture &&
                              !string.IsNullOrEmpty(micDevice) &&
                              Microphone.IsRecording(micDevice);

                if (IsMicActive)
                    UpdateOwnedMicrophoneVolume();
                else
                    CurrentVolume = 0f;
            }

            PublishVoiceLevelToServer();
        }

        public void ApplyNetworkVoiceLevel(bool isActive, float normalizedVolume)
        {
            if (HasLocalMicrophoneAuthority())
                return;

            IsMicActive = isActive;
            CurrentVolume = isActive ? Mathf.Clamp01(normalizedVolume) : 0f;
            lastNetworkVoiceUpdateTime = Time.unscaledTime;
        }

        private void InitializeMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[SoundEmitter] 사용할 수 있는 마이크가 없습니다.");
                return;
            }

            string selectedMic = SettingManager.Instance != null
                ? SettingManager.Instance.SelectedMic
                : null;
            micDevice = !string.IsNullOrEmpty(selectedMic)
                ? selectedMic
                : Microphone.devices[0];
        }

        // 기존 워키토키 상태 코드와의 호환용 API입니다.
        public void StartMic()
        {
            isWalkieTransmitting = true;

            // 지속 캡처가 장치를 관리하면 동일한 물리 마이크를 다시 열지 않습니다.
            if (HasManagedMicrophone())
                return;

            StartOwnedMicrophone();
        }

        // 워키토키 송신 종료와 주변 음성 감지는 별개로 처리합니다.
        public void StopMic()
        {
            isWalkieTransmitting = false;

            if (!listenContinuously)
                StopOwnedMicrophone();
        }

        private bool TryUpdateFromManagedMicrophone()
        {
            SettingManager manager = SettingManager.Instance;
            StableMicrophoneInput input = manager != null ? manager.MicrophoneInput : null;
            if (input == null || !input.IsRecording)
                return false;

            IsMicActive = true;
            CurrentVolume = manager.MicInputLevel < noiseThreshold
                ? 0f
                : Mathf.Clamp01(manager.MicInputLevel);
            return true;
        }

        private bool HasManagedMicrophone()
        {
            SettingManager manager = SettingManager.Instance;

            // 장치 재연결 중에도 SettingManager가 장치 소유자이므로 중복으로 열지 않습니다.
            return manager != null && manager.MicrophoneInput != null;
        }

        private void StartOwnedMicrophone()
        {
            if (ownsMicrophoneCapture)
                return;

            if (string.IsNullOrEmpty(micDevice))
                InitializeMicrophone();
            if (string.IsNullOrEmpty(micDevice))
                return;

            micInput = Microphone.Start(micDevice, true, 1, AudioSettings.outputSampleRate);
            ownsMicrophoneCapture = micInput != null;
            IsMicActive = ownsMicrophoneCapture;
        }

        private void StopOwnedMicrophone()
        {
            if (ownsMicrophoneCapture && !string.IsNullOrEmpty(micDevice) && Microphone.IsRecording(micDevice))
                Microphone.End(micDevice);

            ownsMicrophoneCapture = false;
            micInput = null;
            IsMicActive = false;
            CurrentVolume = 0f;
        }

        private bool HasLocalMicrophoneAuthority()
        {
            return networkController == null ||
                   !networkController.IsSpawned ||
                   networkController.IsOwner;
        }

        private void PublishVoiceLevelToServer()
        {
            if (networkController == null || !networkController.IsSpawned ||
                !networkController.IsOwner || Time.unscaledTime < nextNetworkPublishTime)
            {
                return;
            }

            nextNetworkPublishTime = Time.unscaledTime + networkPublishInterval;
            networkController.SubmitLocalVoiceLevel(IsMicActive, CurrentVolume);
        }

        private void ExpireStaleNetworkVoice()
        {
            if (networkController == null || !networkController.IsServer || !IsMicActive)
                return;

            if (Time.unscaledTime - lastNetworkVoiceUpdateTime < networkSilenceTimeout)
                return;

            IsMicActive = false;
            CurrentVolume = 0f;
        }

        private void UpdateOwnedMicrophoneVolume()
        {
            if (micInput == null)
                return;

            const int sampleCount = 256;
            int micPosition = Microphone.GetPosition(micDevice) - sampleCount;
            if (micPosition < 0)
                return;

            float[] samples = new float[sampleCount];
            micInput.GetData(samples, micPosition);

            float sum = 0f;
            foreach (float sample in samples)
                sum += sample * sample;

            float rmsVolume = Mathf.Sqrt(sum / samples.Length) * micSensitivity;
            CurrentVolume = rmsVolume < noiseThreshold ? 0f : Mathf.Clamp01(rmsVolume);
        }

        private void OnDisable()
        {
            StopOwnedMicrophone();
        }

        private void OnValidate()
        {
            networkPublishInterval = Mathf.Max(0.05f, networkPublishInterval);
            networkSilenceTimeout = Mathf.Max(0.1f, networkSilenceTimeout);
        }

        private void OnDrawGizmos()
        {
            if (!IsMicActive)
                return;

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, CurrentSoundRange);
        }
    }
}
