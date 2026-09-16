using System;
using UnityEngine;
using UnityEngine.Audio;

namespace DeFrag.Player
{
    /// <summary>
    /// 네트워크로 받은 mono PCM16 패킷을 작은 지터 버퍼에 쌓아 재생합니다.
    /// 스트리밍 AudioClip의 PCMReaderCallback을 사용하므로 PlayerVoice의
    /// OnAudioFilterRead 및 다른 AudioSource와 충돌하지 않습니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class NetworkPcmPlayback : MonoBehaviour
    {
        private readonly object bufferLock = new();

        private AudioSource audioSource;
        private AudioClip streamDriver;
        private float[] ringBuffer;
        private int readPosition;
        private int writePosition;
        private int bufferedSamples;
        private int outputSampleRate;
        private int startupSamples;
        private bool playbackPrimed;

        public void Initialize(
            float volume,
            int startupBufferMilliseconds,
            int maxBufferedMilliseconds,
            AudioMixerGroup outputMixerGroup,
            float spatialBlend,
            float minDistance,
            float maxDistance)
        {
            outputSampleRate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            startupSamples = Mathf.Max(
                1,
                outputSampleRate * Mathf.Max(0, startupBufferMilliseconds) / 1000);

            int capacity = outputSampleRate * Mathf.Max(100, maxBufferedMilliseconds) / 1000;
            capacity = Mathf.Max(capacity, startupSamples * 2);

            lock (bufferLock)
            {
                ringBuffer = new float[capacity];
                readPosition = 0;
                writePosition = 0;
                bufferedSamples = 0;
                playbackPrimed = false;
            }

            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.priority = 0;
            audioSource.spatialBlend = Mathf.Clamp01(spatialBlend);
            audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            audioSource.minDistance = Mathf.Max(0.01f, minDistance);
            audioSource.maxDistance = Mathf.Max(audioSource.minDistance, maxDistance);
            audioSource.dopplerLevel = 0f;
            audioSource.volume = Mathf.Clamp01(volume);
            audioSource.outputAudioMixerGroup = outputMixerGroup;

            if (streamDriver != null)
                Destroy(streamDriver);

            streamDriver = AudioClip.Create(
                "Network Walkie Voice Driver",
                outputSampleRate,
                1,
                outputSampleRate,
                true,
                FillStreamData,
                OnStreamPositionChanged);
            audioSource.clip = streamDriver;
            audioSource.Play();
        }

        public void EnqueuePcm16(byte[] packet, int packetSampleRate)
        {
            if (packet == null || packet.Length < 2 || ringBuffer == null ||
                packetSampleRate <= 0 || outputSampleRate <= 0)
            {
                return;
            }

            int sourceSampleCount = packet.Length / 2;
            int outputCount = Mathf.Max(
                1,
                Mathf.RoundToInt(sourceSampleCount * (float)outputSampleRate / packetSampleRate));

            lock (bufferLock)
            {
                for (int outputIndex = 0; outputIndex < outputCount; outputIndex++)
                {
                    float sourcePosition = outputIndex * (float)packetSampleRate / outputSampleRate;
                    int leftIndex = Mathf.Min((int)sourcePosition, sourceSampleCount - 1);
                    int rightIndex = Mathf.Min(leftIndex + 1, sourceSampleCount - 1);
                    float interpolation = sourcePosition - leftIndex;

                    float left = DecodeSample(packet, leftIndex);
                    float right = DecodeSample(packet, rightIndex);
                    EnqueueSample(Mathf.LerpUnclamped(left, right, interpolation));
                }
            }
        }

        public void StopAndClear()
        {
            lock (bufferLock)
            {
                readPosition = 0;
                writePosition = 0;
                bufferedSamples = 0;
                playbackPrimed = false;
                if (ringBuffer != null)
                    Array.Clear(ringBuffer, 0, ringBuffer.Length);
            }
        }

        private void EnqueueSample(float sample)
        {
            if (bufferedSamples == ringBuffer.Length)
            {
                // 네트워크가 순간적으로 밀리면 오래된 음성을 버려 지연이 계속 늘어나는 것을 막습니다.
                readPosition = (readPosition + 1) % ringBuffer.Length;
                bufferedSamples--;
            }

            ringBuffer[writePosition] = sample;
            writePosition = (writePosition + 1) % ringBuffer.Length;
            bufferedSamples++;
        }

        private void FillStreamData(float[] data)
        {
            if (data == null)
                return;

            lock (bufferLock)
            {
                if (ringBuffer == null)
                {
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                if (!playbackPrimed)
                {
                    if (bufferedSamples < startupSamples)
                    {
                        Array.Clear(data, 0, data.Length);
                        return;
                    }

                    playbackPrimed = true;
                }

                for (int sampleIndex = 0; sampleIndex < data.Length; sampleIndex++)
                {
                    float sample = 0f;
                    if (bufferedSamples > 0)
                    {
                        sample = ringBuffer[readPosition];
                        readPosition = (readPosition + 1) % ringBuffer.Length;
                        bufferedSamples--;
                    }
                    else
                    {
                        playbackPrimed = false;
                    }

                    data[sampleIndex] = sample;
                }
            }
        }

        private static void OnStreamPositionChanged(int position)
        {
            // 루프 경계에서 호출되지만 네트워크 링 버퍼의 읽기 위치에는 영향을 주지 않습니다.
        }

        private static float DecodeSample(byte[] packet, int sampleIndex)
        {
            int byteIndex = sampleIndex * 2;
            short value = (short)(packet[byteIndex] | (packet[byteIndex + 1] << 8));
            return value / 32768f;
        }

        private void OnDestroy()
        {
            if (streamDriver != null)
                Destroy(streamDriver);
        }
    }
}
