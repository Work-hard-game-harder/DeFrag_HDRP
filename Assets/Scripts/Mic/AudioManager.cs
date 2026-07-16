// AudioManager.cs
using System.Collections.Generic;
using UnityEngine;
using static SettingManager;

[System.Serializable]
public class SFXClip
{
    public string sfxName;
    public AudioClip clip;
}

[System.Serializable]
public class SceneBGM
{
    public string sceneName;
    public AudioClip clip;
}
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private List<SceneBGM> sceneBGMList;
    [SerializeField] private List<SFXClip> sfxClipList;
    private void Start()
    {

        if (SettingManager.Instance != null)
        {
            SetBGMVolume(SettingManager.Instance.BGM);
            SetSFXVolume(SettingManager.Instance.SFX);
        }
    }
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // BGM
    public void PlayBGMForScene(string sceneName)
    {
        SceneBGM matched = sceneBGMList.Find(s => s.sceneName == sceneName);
        if (matched == null || matched.clip == null) { bgmSource.Stop(); return; }
        if (bgmSource.clip == matched.clip && bgmSource.isPlaying) return;

        bgmSource.clip = matched.clip;
        bgmSource.loop = true;
        bgmSource.Play();
    }

    public void SetBGMVolume(float value)
    {
        if (bgmSource != null) bgmSource.volume = value;
    }

    public void StopBGM()
    {
        if (bgmSource != null) bgmSource.Stop();
    }

    public void ResumeBGM()
    {
        if (bgmSource != null) bgmSource.Play();
    }
    // SFX
    // AudioManager.cs
    public void PlaySFX(string sfxName)
    {
        SFXClip matched = sfxClipList.Find(s => s.sfxName == sfxName);
        if (matched == null || matched.clip == null) return;
        sfxSource.PlayOneShot(matched.clip, 0.5f); // ← 1f로 고정, 볼륨은 sfxSource.volume이 담당
    }

    public void SetSFXVolume(float value)
    {
        if (sfxSource != null) sfxSource.volume = value;
    }
}