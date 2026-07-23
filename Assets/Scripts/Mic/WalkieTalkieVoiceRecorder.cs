using System;
using System.Collections.Generic;
using System.IO;
using EasyPeasyFirstPersonController;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
    [SerializeField, Range(1f, 5f)] private float maxRecordingSeconds = 5f;
    [SerializeField, Min(0f)] private float silencePeakThreshold = 0.0005f;

    [Header("Storage")]
    [SerializeField] private string folderName = "WalkieTalkieRecordings";

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
    private bool isRecording;
    private bool useStableInput;
    private bool isBound;

    private void Awake()
    {
        soundEmitter = GetComponentInChildren<SoundEmitter>(true);
#if UNITY_EDITOR
        outputDirectory = Path.Combine(Application.dataPath, folderName);
#else
        outputDirectory = Path.Combine(Application.persistentDataPath, folderName);
#endif
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

        recordingDurationLimit = Mathf.Clamp(maxRecordingSeconds, 1f, 5f);
        maxSampleCount = Mathf.CeilToInt(
            sampleRate * channelCount * recordingDurationLimit);

        recordedSamples.Clear();
        if (recordedSamples.Capacity < maxSampleCount)
            recordedSamples.Capacity = maxSampleCount;

        recordingStartedAt = Time.unscaledTime;
        peakAmplitude = 0f;
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
            peakAmplitude = Mathf.Max(peakAmplitude, Mathf.Abs(sample));
            stableReadPosition = (stableReadPosition + 1) % buffer.Length;
        }
    }

    private void AddSamples(float[] samples)
    {
        foreach (float sample in samples)
        {
            recordedSamples.Add(sample);
            peakAmplitude = Mathf.Max(peakAmplitude, Mathf.Abs(sample));
        }
    }

    private void FinishAndSave()
    {
        if (!isRecording)
            return;

        CaptureAvailableSamples();
        isRecording = false;

        if (recordedSamples.Count == 0 || peakAmplitude < silencePeakThreshold)
        {
            Debug.Log("[WalkieRecorder] Silence detected; no file was created.");
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
            AudioClip clip = ImportOrCreateAudioClip(
                filePath,
                fileName,
                gain);
            WalkieTalkieRecordingLibrary.Register(clip, filePath);

            float duration =
                (float)recordedSamples.Count / (sampleRate * channelCount);
            Debug.Log(
                $"[WalkieRecorder] Saved {duration:F2}s recording: {filePath}");
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"[WalkieRecorder] Failed to save recording: {exception.Message}");
        }

        ResetRecordingState();
    }

    private AudioClip ImportOrCreateAudioClip(
        string filePath,
        string fileName,
        float gain)
    {
#if UNITY_EDITOR
        string assetPath = $"Assets/{folderName}/{fileName}";
        AssetDatabase.ImportAsset(
            assetPath,
            ImportAssetOptions.ForceSynchronousImport |
            ImportAssetOptions.ForceUpdate);
        AudioClip importedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        if (importedClip != null)
            return importedClip;
#endif

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
