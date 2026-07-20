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

        int existingIndex = filePaths.IndexOf(filePath);
        if (existingIndex >= 0)
        {
            clips[existingIndex] = clip;
            RecordingAdded?.Invoke(clip, filePath);
            return;
        }

        clips.Add(clip);
        filePaths.Add(filePath);
        RecordingAdded?.Invoke(clip, filePath);
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
}
