using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EasyPeasyFirstPersonController;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 워키토키를 든 상태에서 좌클릭으로 말한 한 구간을 최대 5초 WAV 파일로 저장합니다.
/// 기존 SoundEmitter가 실제로 연 마이크 클립을 읽어 별도의 Microphone.Start 충돌을 만들지 않습니다.
/// </summary>
[DefaultExecutionOrder(1000)]
public class WalkieTalkieVoiceRecorder : MonoBehaviour
{
    [SerializeField, Range(1f, 5f)] private float maxRecordingSeconds = 5f;
    [SerializeField, Min(0f)] private float silencePeakThreshold = 0.0005f;
    [SerializeField] private string folderName = "WalkieTalkieRecordings";

    private static readonly FieldInfo MicClipField = typeof(SoundEmitter).GetField(
        "micInput", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MicDeviceField = typeof(SoundEmitter).GetField(
        "micDevice", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly List<float> recordedSamples = new List<float>(240000);

    private FirstPersonController controller;
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

    private void Awake()
    {
        controller = GetComponent<FirstPersonController>();
        soundEmitter = GetComponentInChildren<SoundEmitter>(true);
#if UNITY_EDITOR
        outputDirectory = Path.Combine(Application.dataPath, folderName);
#else
        outputDirectory = Path.Combine(Application.persistentDataPath, folderName);
#endif
        Directory.CreateDirectory(outputDirectory);
        Debug.Log($"[WalkieRecorder] 준비 완료. 저장 경로: {outputDirectory}");
    }

    private void LateUpdate()
    {
        bool isHoldingWalkieTalkie = controller != null &&
                                    controller.CurrentState is PlayerWakieTakieState;

        if (isHoldingWalkieTalkie && Input.GetMouseButtonDown(0))
            BeginRecording();

        if (!isRecording) return;

        if (Input.GetMouseButtonUp(0) || !isHoldingWalkieTalkie)
        {
            FinishAndSave();
            return;
        }

        CaptureAvailableSamples();

        if (Time.unscaledTime - recordingStartedAt >= recordingDurationLimit ||
            recordedSamples.Count >= maxSampleCount)
            FinishAndSave();
    }

    private void BeginRecording()
    {
        if (isRecording) return;

        if (soundEmitter == null)
            soundEmitter = GetComponentInChildren<SoundEmitter>(true);

        recordingClip = soundEmitter != null ? MicClipField?.GetValue(soundEmitter) as AudioClip : null;
        recordingDevice = soundEmitter != null ? MicDeviceField?.GetValue(soundEmitter) as string : null;
        useStableInput = false;

        if (recordingClip == null || string.IsNullOrEmpty(recordingDevice))
        {
            stableInput = SettingManager.Instance != null
                ? SettingManager.Instance.MicrophoneInput
                : FindAnyObjectByType<StableMicrophoneInput>();

            if (stableInput == null || !stableInput.IsRecording || stableInput.BufferLength == 0)
            {
                Debug.LogWarning("[WalkieRecorder] 워키토키와 지속 마이크 입력을 모두 찾지 못했습니다.");
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
            if (micPosition < 0) return;

            sampleRate = recordingClip.frequency;
            channelCount = Mathf.Max(1, recordingClip.channels);
            readFramePosition = micPosition;
        }

        recordingDurationLimit = Mathf.Clamp(maxRecordingSeconds, 1f, 5f);
        maxSampleCount = Mathf.CeilToInt(sampleRate * channelCount * recordingDurationLimit);
        recordedSamples.Clear();
        if (recordedSamples.Capacity < maxSampleCount)
            recordedSamples.Capacity = maxSampleCount;

        recordingStartedAt = Time.unscaledTime;
        peakAmplitude = 0f;
        isRecording = true;
        Debug.Log("[WalkieRecorder] 녹음 시작");
    }

    private void CaptureAvailableSamples()
    {
        if (useStableInput)
        {
            CaptureStableInputSamples();
            return;
        }

        if (recordingClip == null || string.IsNullOrEmpty(recordingDevice)) return;

        int micPosition = Microphone.GetPosition(recordingDevice);
        if (micPosition < 0) return;

        int availableFrames = micPosition - readFramePosition;
        if (availableFrames < 0)
            availableFrames += recordingClip.samples;

        int remainingFrames = (maxSampleCount - recordedSamples.Count) / channelCount;
        int framesToRead = Mathf.Min(availableFrames, remainingFrames);
        if (framesToRead <= 0) return;

        float[] samples = new float[framesToRead * channelCount];
        if (!recordingClip.GetData(samples, readFramePosition)) return;

        for (int i = 0; i < samples.Length; i++)
        {
            recordedSamples.Add(samples[i]);
            peakAmplitude = Mathf.Max(peakAmplitude, Mathf.Abs(samples[i]));
        }

        readFramePosition = (readFramePosition + framesToRead) % recordingClip.samples;
    }

    private void CaptureStableInputSamples()
    {
        if (stableInput == null || stableInput.CircularBuffer == null) return;

        float[] buffer = stableInput.CircularBuffer;
        int writePosition = stableInput.WritePos;
        int available = writePosition - stableReadPosition;
        if (available < 0) available += buffer.Length;

        int count = Mathf.Min(available, maxSampleCount - recordedSamples.Count);
        for (int i = 0; i < count; i++)
        {
            float sample = buffer[stableReadPosition];
            recordedSamples.Add(sample);
            peakAmplitude = Mathf.Max(peakAmplitude, Mathf.Abs(sample));
            stableReadPosition = (stableReadPosition + 1) % buffer.Length;
        }
    }

    private void FinishAndSave()
    {
        if (!isRecording) return;

        // Mouse Up에서 Microphone.End가 먼저 호출되므로 마지막 Update까지 확보된 샘플을 저장합니다.
        isRecording = false;

        if (recordedSamples.Count == 0 || peakAmplitude < silencePeakThreshold)
        {
            Debug.Log("[WalkieRecorder] 음성이 감지되지 않아 파일을 저장하지 않았습니다.");
            ResetRecordingState();
            return;
        }

        float gain = SettingManager.Instance != null ? SettingManager.Instance.MicGain : 1f;
        byte[] waveData = CreateWaveFile(recordedSamples, sampleRate, channelCount, gain);
        string fileName = $"walkie_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav";
        string filePath = Path.Combine(outputDirectory, fileName);
        try
        {
            File.WriteAllBytes(filePath, waveData);
            AudioClip clip = ImportOrCreateAudioClip(filePath, fileName, gain);
            WalkieTalkieRecordingLibrary.Register(clip, filePath);

            float duration = (float)recordedSamples.Count / (sampleRate * channelCount);
            Debug.Log($"[WalkieRecorder] {duration:F2}초 음성 저장 완료: {filePath}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[WalkieRecorder] 음성 파일 저장 실패: {exception.Message}");
        }
        ResetRecordingState();
    }

    private AudioClip ImportOrCreateAudioClip(string filePath, string fileName, float gain)
    {
#if UNITY_EDITOR
        string assetPath = $"Assets/{folderName}/{fileName}";
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
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

    private static byte[] CreateWaveFile(List<float> samples, int rate, int channels, float gain)
    {
        const short bitsPerSample = 16;
        int dataSize = samples.Count * sizeof(short);

        using (MemoryStream stream = new MemoryStream(44 + dataSize))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
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

            for (int i = 0; i < samples.Count; i++)
            {
                float limited = (float)Math.Tanh(samples[i] * gain);
                writer.Write((short)Mathf.RoundToInt(limited * short.MaxValue));
            }

            writer.Flush();
            return stream.ToArray();
        }
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
        if (isRecording)
            FinishAndSave();
    }
}
