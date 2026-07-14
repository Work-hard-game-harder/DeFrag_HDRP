using UnityEngine;

namespace EasyPeasyFirstPersonController
{
    public class SoundEmitter : MonoBehaviour
    {
        [Header("Sound Settings")]
        public float soundBaseRange = 15f;          // 기본 소리 범위
        public float soundRangeMultiplier = 10f;    // 볼륨에 따른 범위 배수
        public float micSensitivity = 100f;         // 마이크 감도
        public float noiseThreshold = 0.01f;        // 노이즈 필터 (이 이하는 무시)

        private AudioClip micInput;
        private string micDevice;

        public bool IsMicActive { get; private set; } = false;
        public float CurrentVolume { get; private set; } = 0f;

        // 현재 소리 감지 범위 (볼륨에 따라 동적으로 변함)
        public float CurrentSoundRange =>
            IsMicActive ? soundBaseRange + (CurrentVolume * soundRangeMultiplier) : 0f;

        void Start()
        {
            InitializeMicrophone();
        }

        void Update()
        {
            if (IsMicActive)
                UpdateVolume();
        }

        void InitializeMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("마이크 없음");
                return;
            }
            micDevice = Microphone.devices[0];
        }

        public void StartMic()
        {
            if (micDevice == null) return;
            micInput = Microphone.Start(micDevice, true, 1, AudioSettings.outputSampleRate);
            IsMicActive = true;
        }

        public void StopMic()
        {
            if (micDevice == null) return;
            Microphone.End(micDevice);
            IsMicActive = false;
            CurrentVolume = 0f;
        }

        void UpdateVolume()
        {
            if (micInput == null) return;

            float[] samples = new float[256];
            int micPosition = Microphone.GetPosition(micDevice) - 256;
            if (micPosition < 0) return;

            micInput.GetData(samples, micPosition);

            // RMS(평균 제곱근)로 볼륨 측정
            float sum = 0f;
            foreach (float sample in samples)
                sum += sample * sample;

            float rmsVolume = Mathf.Sqrt(sum / samples.Length) * micSensitivity;

            // 노이즈 필터
            CurrentVolume = rmsVolume < noiseThreshold ? 0f : Mathf.Clamp01(rmsVolume);
        }

        void OnDrawGizmos()
        {
            if (!IsMicActive) return;

            // 현재 소리 범위 시각화
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, CurrentSoundRange);
        }
    }
}