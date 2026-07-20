using System.Collections;
using UnityEngine;

/// <summary>
/// 마이크 클립에서 아직 읽지 않은 샘플만 순서대로 원형 버퍼에 복사합니다.
/// 이전 구현처럼 같은 1024 샘플을 반복 복사하지 않아 음성 중첩과 찢어짐을 방지합니다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class StableMicrophoneInput : MonoBehaviour
{
    public string[] Devices => Microphone.devices;
    public int CurrentDeviceIndex { get; private set; }
    public float[] CircularBuffer { get; private set; }
    public int BufferLength => CircularBuffer != null ? CircularBuffer.Length : 0;
    public int WritePos => writePos;
    public bool IsRecording => !string.IsNullOrEmpty(activeDevice) && Microphone.IsRecording(activeDevice);

    [SerializeField, Min(4096)] private int bufferSizeSamples = 96000;
    [SerializeField, Range(64, 1024)] private int transferChunkSize = 256;
    [SerializeField, Min(1)] private int micBufferSeconds = 1;

    private AudioClip micClip;
    private string activeDevice;
    private Coroutine startCoroutine;
    private Coroutine copyCoroutine;
    private volatile int writePos;

    private void Awake()
    {
        CircularBuffer = new float[Mathf.Max(4096, bufferSizeSamples)];
    }

    public void StartMic(int deviceIndex)
    {
        StopMic();

        string[] devices = Microphone.devices;
        if (devices.Length == 0) return;

        CurrentDeviceIndex = Mathf.Clamp(deviceIndex, 0, devices.Length - 1);
        activeDevice = devices[CurrentDeviceIndex];
        writePos = 0;
        System.Array.Clear(CircularBuffer, 0, CircularBuffer.Length);

        micClip = Microphone.Start(activeDevice, true, micBufferSeconds, AudioSettings.outputSampleRate);
        if (micClip != null)
            startCoroutine = StartCoroutine(WaitForMicrophone(activeDevice, micClip));
    }

    private IEnumerator WaitForMicrophone(string device, AudioClip clip)
    {
        float timeout = Time.realtimeSinceStartup + 3f;
        while (device == activeDevice && Microphone.GetPosition(device) <= 0 &&
               Time.realtimeSinceStartup < timeout)
            yield return null;

        startCoroutine = null;
        if (device != activeDevice || clip != micClip || Microphone.GetPosition(device) <= 0)
            yield break;

        copyCoroutine = StartCoroutine(CopyNewSamples(device, clip));
    }

    private IEnumerator CopyNewSamples(string device, AudioClip clip)
    {
        int chunkSize = Mathf.Clamp(transferChunkSize, 64, 1024);
        float[] chunk = new float[chunkSize];
        int readPosition = Microphone.GetPosition(device);

        while (device == activeDevice && clip == micClip && Microphone.IsRecording(device))
        {
            int micPosition = Microphone.GetPosition(device);
            int available = micPosition - readPosition;
            if (available < 0)
                available += clip.samples;

            while (available >= chunkSize)
            {
                // Looping AudioClip은 끝을 넘는 GetData 요청을 클립 처음부터 이어서 읽습니다.
                if (!clip.GetData(chunk, readPosition))
                    break;

                int localWritePos = writePos;
                for (int i = 0; i < chunk.Length; i++)
                {
                    CircularBuffer[localWritePos] = chunk[i];
                    localWritePos = (localWritePos + 1) % CircularBuffer.Length;
                }

                // 완성된 청크를 쓴 뒤 위치를 공개해 오디오 스레드가 반쪽짜리 데이터를 읽지 않게 합니다.
                writePos = localWritePos;
                readPosition = (readPosition + chunkSize) % clip.samples;
                available -= chunkSize;
            }

            yield return null;
        }

        copyCoroutine = null;
    }

    public void StopMic()
    {
        if (startCoroutine != null)
        {
            StopCoroutine(startCoroutine);
            startCoroutine = null;
        }

        if (copyCoroutine != null)
        {
            StopCoroutine(copyCoroutine);
            copyCoroutine = null;
        }

        if (!string.IsNullOrEmpty(activeDevice) && Microphone.IsRecording(activeDevice))
            Microphone.End(activeDevice);

        activeDevice = null;
        micClip = null;
    }

    private void OnDisable()
    {
        StopMic();
    }
}
