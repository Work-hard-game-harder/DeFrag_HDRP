using System;
using UnityEngine;

/// <summary>
/// 마이크 원형 버퍼를 일정한 지연 거리에서 재생합니다.
/// 언더런/오버런 시 읽기 위치를 복구하고 소프트 리미터로 클리핑을 억제합니다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class StablePlayerVoice : MonoBehaviour
{
    public StableMicrophoneInput micSource;

    [Tooltip("마이크 본체의 헤드폰 단자로 직접 모니터링할 때는 꺼 두세요.")]
    [SerializeField] private bool monitorLocally = false;
    [SerializeField, Range(512, 8192)] private int targetLatencySamples = 2048;

    private AudioSource audioSource;
    private StableMicrophoneInput cachedSource;
    private int readPos;
    private bool readPositionInitialized;
    private volatile float cachedGain = 1f;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = true;
        audioSource.playOnAwake = false;

        if (monitorLocally)
        {
            int sampleRate = AudioSettings.outputSampleRate;
            audioSource.clip = AudioClip.Create("Microphone Stream Driver", sampleRate, 1, sampleRate, false);
            audioSource.Play();
        }
    }

    private void Update()
    {
        SettingManager manager = SettingManager.Instance;
        cachedGain = manager != null ? manager.MicGain : 1f;

        if (cachedSource != micSource)
        {
            cachedSource = micSource;
            readPositionInitialized = false;
        }
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!monitorLocally)
        {
            Array.Clear(data, 0, data.Length);
            return;
        }

        StableMicrophoneInput source = cachedSource;
        float[] buffer = source != null ? source.CircularBuffer : null;
        if (buffer == null || buffer.Length == 0 || channels <= 0)
        {
            Array.Clear(data, 0, data.Length);
            return;
        }

        int writePos = source.WritePos;
        int frameCount = data.Length / channels;
        int latency = Mathf.Clamp(targetLatencySamples, frameCount + 128, buffer.Length / 2);

        if (!readPositionInitialized)
        {
            readPos = Wrap(writePos - latency, buffer.Length);
            readPositionInitialized = true;
        }

        int bufferedSamples = Distance(readPos, writePos, buffer.Length);
        if (bufferedSamples < frameCount + 64 || bufferedSamples > latency + frameCount * 4)
            readPos = Wrap(writePos - latency, buffer.Length);

        float gain = cachedGain;
        for (int frame = 0; frame < frameCount; frame++)
        {
            float amplified = buffer[readPos] * gain;
            float sample = (float)Math.Tanh(amplified); // 하드 클리핑 대신 부드럽게 피크 제한
            readPos = (readPos + 1) % buffer.Length;

            int outputIndex = frame * channels;
            for (int channel = 0; channel < channels; channel++)
                data[outputIndex + channel] = sample;
        }
    }

    private static int Distance(int from, int to, int length)
    {
        int distance = to - from;
        return distance >= 0 ? distance : distance + length;
    }

    private static int Wrap(int value, int length)
    {
        value %= length;
        return value >= 0 ? value : value + length;
    }
}
