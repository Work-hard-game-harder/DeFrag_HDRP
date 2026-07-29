using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 현재 실행 중 생성된 워키토키 녹음을 몬스터 AudioSource에서 바로 사용할 수 있게 보관합니다.
/// </summary>
public static class WalkieTalkieRecordingLibrary
{
    private static readonly List<AudioClip> clips = new List<AudioClip>();
    private static readonly List<string> filePaths = new List<string>();

    public static IReadOnlyList<AudioClip> Clips => clips;
    public static IReadOnlyList<string> FilePaths => filePaths;
    public static AudioClip LatestClip => clips.Count > 0 ? clips[clips.Count - 1] : null;
    public static event Action<AudioClip, string> RecordingAdded;

    public static void Register(AudioClip clip, string filePath)
    {
        if (clip == null) return;

        string normalizedPath = WalkieTalkieRecordingStorage.NormalizePath(filePath);
        int existingIndex = FindPathIndex(normalizedPath);
        if (existingIndex >= 0)
        {
            AudioClip previousClip = clips[existingIndex];
            clips[existingIndex] = clip;
            if (previousClip != null && previousClip != clip)
                UnityEngine.Object.Destroy(previousClip);
            RecordingAdded?.Invoke(clip, normalizedPath);
            return;
        }

        clips.Add(clip);
        filePaths.Add(normalizedPath);
        RecordingAdded?.Invoke(clip, normalizedPath);
    }

    public static bool Unregister(string filePath, bool destroyClip)
    {
        int index = FindPathIndex(WalkieTalkieRecordingStorage.NormalizePath(filePath));
        if (index < 0)
            return false;

        AudioClip clip = clips[index];
        clips.RemoveAt(index);
        filePaths.RemoveAt(index);

        if (destroyClip && clip != null)
            UnityEngine.Object.Destroy(clip);

        return true;
    }

    public static AudioClip GetRandomClip()
    {
        return clips.Count > 0 ? clips[UnityEngine.Random.Range(0, clips.Count)] : null;
    }

    public static bool PlayLatestOn(AudioSource audioSource)
    {
        AudioClip clip = LatestClip;
        if (audioSource == null || clip == null) return false;

        audioSource.clip = clip;
        audioSource.Play();
        return true;
    }

    private static int FindPathIndex(string normalizedPath)
    {
        for (int i = 0; i < filePaths.Count; i++)
        {
            if (string.Equals(filePaths[i], normalizedPath, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
