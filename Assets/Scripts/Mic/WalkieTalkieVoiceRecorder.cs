using System;
using System.Collections.Generic;
using System.IO;
using EasyPeasyFirstPersonController;
using UnityEngine;

/// <summary>
/// Records one push-to-talk transmission to a WAV file, capped at five seconds.
/// It reads the microphone that is already managed by SoundEmitter or
/// StableMicrophoneInput and never opens a duplicate microphone capture.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class WalkieTalkieVoiceRecorder : MonoBehaviour
{
    [Header("Recording")]
    [SerializeField, Range(2f, 5f)] private float minRecordingSeconds = 2f;
    [SerializeField, Range(2f, 5f)] private float maxRecordingSeconds = 5f;
    [SerializeField, Min(0f)] private float silencePeakThreshold = 0.0005f;
    [SerializeField, Min(0f)] private float silenceRmsThreshold = 0.002f;

    [Header("Storage")]
    [SerializeField] private string folderName = WalkieTalkieRecordingStorage.DefaultFolderName;
    [SerializeField, Min(1)] private int maxStoredRecordings = 30;

    private readonly List<float> recordedSamples = new List<float>(240000);

    private WalkieTalkieController walkieTalkieController;
    private SoundEmitter soundEmitter;
    private AudioClip recordingClip;
    private string recordingDevice;
    private StableMicrophoneInput stableInput;
    private string outputDirectory;
    private int readFramePosition;
    private int stableReadPosition;
    private int sampleRate;
    private int channelCount;
    private int maxSampleCount;
    private float recordingStartedAt;
    private float recordingDurationLimit;
    private float peakAmplitude;
    private double squaredAmplitudeSum;
    private int measuredSampleCount;
    private bool isRecording;
    private bool useStableInput;
    private bool isBound;

    private void Awake()
    {
        soundEmitter = GetComponentInChildren<SoundEmitter>(true);
        outputDirectory = WalkieTalkieRecordingStorage.GetDirectoryPath(folderName);
        Directory.CreateDirectory(outputDirectory);
        Debug.Log($"[WalkieRecorder] Ready. Output: {outputDirectory}");
    }

    private void OnEnable()
    {
        TryBindWalkieTalkie();
    }

    private void Start()
    {
        TryBindWalkieTalkie();
    }

    private void LateUpdate()
    {
        TryBindWalkieTalkie();
        if (!isRecording)
            return;

        CaptureAvailableSamples();

        if (Time.unscaledTime - recordingStartedAt >= recordingDurationLimit ||
            recordedSamples.Count >= maxSampleCount)
        {
            FinishAndSave();
        }
    }

    private void TryBindWalkieTalkie()
    {
        if (isBound)
            return;

        if (walkieTalkieController == null)
            walkieTalkieController = GetComponent<WalkieTalkieController>();
        if (walkieTalkieController == null)
            return;

        walkieTalkieController.TransmissionStarted += BeginRecording;
        walkieTalkieController.TransmissionStopped += FinishAndSave;
        isBound = true;
    }

    private void BeginRecording()
    {
        if (isRecording)
            return;

        if (soundEmitter == null)
            soundEmitter = GetComponentInChildren<SoundEmitter>(true);

        recordingClip = soundEmitter != null ? soundEmitter.MicrophoneClip : null;
        recordingDevice = soundEmitter != null ? soundEmitter.MicrophoneDevice : null;
        useStableInput = false;

        if (recordingClip == null || string.IsNullOrEmpty(recordingDevice))
        {
            stableInput = SettingManager.Instance != null
                ? SettingManager.Instance.MicrophoneInput
                : FindAnyObjectByType<StableMicrophoneInput>();

            if (stableInput == null || !stableInput.IsRecording || stableInput.BufferLength == 0)
            {
                Debug.LogWarning("[WalkieRecorder] No active microphone buffer was found.");
                return;
            }

            useStableInput = true;
            sampleRate = AudioSettings.outputSampleRate;
            channelCount = 1;
            stableReadPosition = stableInput.WritePos;
        }
        else
        {
            int micPosition = Microphone.GetPosition(recordingDevice);
            if (micPosition < 0)
                return;

            sampleRate = recordingClip.frequency;
            channelCount = Mathf.Max(1, recordingClip.channels);
            readFramePosition = micPosition;
        }

        recordingDurationLimit = Mathf.Clamp(maxRecordingSeconds, 2f, 5f);
        maxSampleCount = Mathf.CeilToInt(
            sampleRate * channelCount * recordingDurationLimit);

        recordedSamples.Clear();
        if (recordedSamples.Capacity < maxSampleCount)
            recordedSamples.Capacity = maxSampleCount;

        recordingStartedAt = Time.unscaledTime;
        peakAmplitude = 0f;
        squaredAmplitudeSum = 0d;
        measuredSampleCount = 0;
        isRecording = true;
        Debug.Log("[WalkieRecorder] Recording started.");
    }

    private void CaptureAvailableSamples()
    {
        if (useStableInput)
        {
            CaptureStableInputSamples();
            return;
        }

        if (recordingClip == null || string.IsNullOrEmpty(recordingDevice))
            return;

        int micPosition = Microphone.GetPosition(recordingDevice);
        if (micPosition < 0)
            return;

        int availableFrames = micPosition - readFramePosition;
        if (availableFrames < 0)
            availableFrames += recordingClip.samples;

        int remainingFrames =
            (maxSampleCount - recordedSamples.Count) / channelCount;
        int framesToRead = Mathf.Min(availableFrames, remainingFrames);
        if (framesToRead <= 0)
            return;

        float[] samples = new float[framesToRead * channelCount];
        if (!recordingClip.GetData(samples, readFramePosition))
            return;

        AddSamples(samples);
        readFramePosition =
            (readFramePosition + framesToRead) % recordingClip.samples;
    }

    private void CaptureStableInputSamples()
    {
        if (stableInput == null || stableInput.CircularBuffer == null)
            return;

        float[] buffer = stableInput.CircularBuffer;
        int writePosition = stableInput.WritePos;
        int available = writePosition - stableReadPosition;
        if (available < 0)
            available += buffer.Length;

        int count = Mathf.Min(
            available,
            maxSampleCount - recordedSamples.Count);

        for (int i = 0; i < count; i++)
        {
            float sample = buffer[stableReadPosition];
            recordedSamples.Add(sample);
            MeasureSample(sample);
            stableReadPosition = (stableReadPosition + 1) % buffer.Length;
        }
    }

    private void AddSamples(float[] samples)
    {
        foreach (float sample in samples)
        {
            recordedSamples.Add(sample);
            MeasureSample(sample);
        }
    }

    private void FinishAndSave()
    {
        if (!isRecording)
            return;

        CaptureAvailableSamples();
        isRecording = false;

        float duration = sampleRate > 0 && channelCount > 0
            ? (float)recordedSamples.Count / (sampleRate * channelCount)
            : 0f;

        float requiredDuration = Mathf.Clamp(
            minRecordingSeconds,
            2f,
            Mathf.Clamp(maxRecordingSeconds, 2f, 5f));
        if (duration < requiredDuration)
        {
            Debug.Log(
                $"[WalkieRecorder] Recording was too short ({duration:F2}s < {requiredDuration:F2}s); no file was created.");
            ResetRecordingState();
            return;
        }

        float rmsAmplitude = measuredSampleCount > 0
            ? Mathf.Sqrt((float)(squaredAmplitudeSum / measuredSampleCount))
            : 0f;

        if (recordedSamples.Count == 0 ||
            peakAmplitude < silencePeakThreshold ||
            rmsAmplitude < silenceRmsThreshold)
        {
            Debug.Log(
                $"[WalkieRecorder] Input was too quiet (Peak {peakAmplitude:F4}, RMS {rmsAmplitude:F4}); no file was created.");
            ResetRecordingState();
            return;
        }

        float gain = SettingManager.Instance != null
            ? SettingManager.Instance.MicGain
            : 1f;
        byte[] waveData = CreateWaveFile(
            recordedSamples,
            sampleRate,
            channelCount,
            gain);

        string fileName = $"walkie_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav";
        string filePath = Path.Combine(outputDirectory, fileName);

        try
        {
            File.WriteAllBytes(filePath, waveData);
            EnforceRecordingLimit();

            AudioClip clip = CreateRuntimeAudioClip(filePath, gain);
            WalkieTalkieRecordingLibrary.Register(clip, filePath);

            Debug.Log(
                $"[WalkieRecorder] Saved {duration:F2}s recording " +
                $"(Peak {peakAmplitude:F4}, RMS {rmsAmplitude:F4}): {filePath}");
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"[WalkieRecorder] Failed to save recording: {exception.Message}");
        }

        ResetRecordingState();
    }

    private void EnforceRecordingLimit()
    {
        WalkieTalkieRecordingStorage.EnforceRecordingLimit(
            outputDirectory,
            Mathf.Max(1, maxStoredRecordings),
            removedPath => WalkieTalkieRecordingLibrary.Unregister(removedPath, true));
    }

    private AudioClip CreateRuntimeAudioClip(string filePath, float gain)
    {
        string clipName = Path.GetFileNameWithoutExtension(filePath);
        AudioClip runtimeClip = AudioClip.Create(
            clipName,
            recordedSamples.Count / channelCount,
            channelCount,
            sampleRate,
            false);

        float[] processedSamples = new float[recordedSamples.Count];
        for (int i = 0; i < processedSamples.Length; i++)
            processedSamples[i] = (float)Math.Tanh(recordedSamples[i] * gain);

        runtimeClip.SetData(processedSamples, 0);
        return runtimeClip;
    }

    private static byte[] CreateWaveFile(
        List<float> samples,
        int rate,
        int channels,
        float gain)
    {
        const short bitsPerSample = 16;
        int dataSize = samples.Count * sizeof(short);

        using MemoryStream stream = new MemoryStream(44 + dataSize);
        using BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * channels * sizeof(short));
        writer.Write((short)(channels * sizeof(short)));
        writer.Write(bitsPerSample);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (float sample in samples)
        {
            float limited = (float)Math.Tanh(sample * gain);
            writer.Write((short)Mathf.RoundToInt(limited * short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }

    private void ResetRecordingState()
    {
        recordedSamples.Clear();
        recordingClip = null;
        recordingDevice = null;
        stableInput = null;
        useStableInput = false;
        peakAmplitude = 0f;
        squaredAmplitudeSum = 0d;
        measuredSampleCount = 0;
    }

    private void MeasureSample(float sample)
    {
        peakAmplitude = Mathf.Max(peakAmplitude, Mathf.Abs(sample));
        squaredAmplitudeSum += sample * sample;
        measuredSampleCount++;
    }

    private void OnValidate()
    {
        maxRecordingSeconds = Mathf.Clamp(maxRecordingSeconds, 2f, 5f);
        minRecordingSeconds = Mathf.Clamp(minRecordingSeconds, 2f, maxRecordingSeconds);
        silencePeakThreshold = Mathf.Max(0f, silencePeakThreshold);
        silenceRmsThreshold = Mathf.Max(0f, silenceRmsThreshold);
        maxStoredRecordings = Mathf.Max(1, maxStoredRecordings);
    }

    private void OnDisable()
    {
        if (isBound && walkieTalkieController != null)
        {
            walkieTalkieController.TransmissionStarted -= BeginRecording;
            walkieTalkieController.TransmissionStopped -= FinishAndSave;
            isBound = false;
        }

        if (isRecording)
            FinishAndSave();
    }
}
