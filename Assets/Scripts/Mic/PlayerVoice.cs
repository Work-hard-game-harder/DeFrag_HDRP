using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class PlayerVoice : MonoBehaviour
{
    public MicLowLatency micSource; // SettingManager에서 할당
    private AudioSource audioSource;
    private int readPos = 0;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = true;
        audioSource.loop = true;
        // audioSource.clip는 필요 없음; OnAudioFilterRead로 직접 출력
    }

    // OnAudioFilterRead는 오디오 스레드에서 호출됨
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (micSource == null || micSource.CircularBuffer == null)
        {
            // silence
            for (int i = 0; i < data.Length; i++) data[i] = 0f;
            return;
        }

        float[] buf = micSource.CircularBuffer;
        int bufLen = buf.Length;

        for (int i = 0; i < data.Length; i += channels)
        {
            // read one mono sample from circular buffer
            float sample = buf[readPos];
            readPos = (readPos + 1) % bufLen;

            // write to all channels
            for (int c = 0; c < channels; c++)
                data[i + c] = sample;
        }
    }
}
