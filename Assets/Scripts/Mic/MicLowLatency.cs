using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class MicLowLatency : MonoBehaviour
{
    public string[] Devices => Microphone.devices;
    public int CurrentDeviceIndex { get; private set; } = 0;
    public float[] CircularBuffer { get; private set; }
    public int BufferLength => CircularBuffer.Length;
    public int WritePos { get; private set; } = 0;

    [SerializeField] int bufferSizeSamples = 48000; // 1초 @48k
    [SerializeField] int micBufferSeconds = 1;

    private AudioClip micClip;

    void Awake()
    {
        CircularBuffer = new float[bufferSizeSamples];
    }

    public void RefreshDevices()
    {
        // Microphone.devices는 런타임에 갱신됨
    }

    public void StartMic(int deviceIndex)
    {
        if (Microphone.devices.Length == 0) return;
        deviceIndex = Mathf.Clamp(deviceIndex, 0, Microphone.devices.Length - 1);
        CurrentDeviceIndex = deviceIndex;
        string dev = Microphone.devices[deviceIndex];

        if (Microphone.IsRecording(dev)) Microphone.End(dev);
        micClip = Microphone.Start(dev, true, micBufferSeconds, AudioSettings.outputSampleRate);
        StartCoroutine(WaitAndBegin(dev));
    }

    IEnumerator WaitAndBegin(string dev)
    {
        while (Microphone.GetPosition(dev) <= 0) yield return null;
        StartCoroutine(CopyMicToCircularBuffer(dev));
    }

    IEnumerator CopyMicToCircularBuffer(string dev)
    {
        int sampleRate = AudioSettings.outputSampleRate;
        float[] temp = new float[1024];
        while (Microphone.IsRecording(dev))
        {
            int micPos = Microphone.GetPosition(dev);
            int toRead = Mathf.Min(temp.Length, micClip.samples);
            int start = (micPos - toRead + micClip.samples) % micClip.samples;
            micClip.GetData(temp, start);

            // write into circular buffer (no locks; simple wrap)
            for (int i = 0; i < toRead; i++)
            {
                CircularBuffer[WritePos] = temp[i];
                WritePos = (WritePos + 1) % CircularBuffer.Length;
            }
            yield return new WaitForSecondsRealtime(0.01f);
        }
    }

    public void StopMic()
    {
        if (Microphone.devices.Length > 0)
        {
            string dev = Microphone.devices[CurrentDeviceIndex];
            if (Microphone.IsRecording(dev)) Microphone.End(dev);
        }
    }
}
