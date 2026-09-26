using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class SubtitlesScript : MonoBehaviour
{
    public static event Action PlaybackStarted;

    public TextMeshProUGUI subtitlesText;
    public GameObject subtitlesPanel;
    public float subtitlesSpeed;

    [Header("Typewriter Audio")]
    [SerializeField] private AudioSource typewriterSource;
    [SerializeField] private AudioClip[] typewriterClips;
    [Range(0f, 1f)] [SerializeField] private float typewriterVolume = 0.65f;
    [SerializeField] private Vector2 typewriterPitchRange = new Vector2(0.96f, 1.04f);

    private Action onFinished;
    private string[] subtitles;
    private int index;
    private bool ignoreClick;
    private bool ownsCutsceneLock;
    private SubtitleColorOverride[] activeColorOverrides;
    private SubtitleAudioOverride[] activeAudioOverrides;
    private Color defaultSubtitleColor;

    private void Start()
    {
        EnsureTypewriterSource();
        defaultSubtitleColor = subtitlesText.color;
        subtitlesText.text = string.Empty;
        subtitlesPanel.SetActive(false);
    }

    private void EnsureTypewriterSource()
    {
        if (typewriterSource == null)
            typewriterSource = gameObject.AddComponent<AudioSource>();

        typewriterSource.playOnAwake = false;
        typewriterSource.loop = false;
        typewriterSource.spatialBlend = 0f;
        typewriterSource.dopplerLevel = 0f;
    }

    private void Update()
    {
        if (subtitles == null || ignoreClick)
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        if (subtitlesText.text == subtitles[index])
        {
            NextSubtitle();
        }
        else
        {
            StopAllCoroutines();
            StopTypewriterSound();
            subtitlesText.text = subtitles[index];
        }
    }

    // 색상/사운드 Override를 사용하지 않는 기존 호출부와의 호환성을 유지합니다.
    public void PlaySubtitles(string[] newSubtitles, Action callback = null)
    {
        PlaySubtitles(newSubtitles, null, null, callback);
    }

    public void PlaySubtitles(
        string[] newSubtitles,
        SubtitleColorOverride[] colorOverrides,
        SubtitleAudioOverride[] audioOverrides,
        Action callback = null)
    {
        if (newSubtitles == null || newSubtitles.Length == 0)
        {
            callback?.Invoke();
            return;
        }

        subtitles = newSubtitles;
        activeColorOverrides = colorOverrides;
        activeAudioOverrides = audioOverrides;

        index = 0;
        onFinished = callback;

        subtitlesText.text = string.Empty;
        subtitlesPanel.SetActive(true);

        ApplyCurrentSubtitleColor();

        AcquireCutsceneLock();
        PlaybackStarted?.Invoke();

        StopAllCoroutines();
        StopTypewriterSound();
        StartCoroutine(TypeLine());
        StartCoroutine(IgnoreClickThisFrame());
    }

    private IEnumerator IgnoreClickThisFrame()
    {
        ignoreClick = true;
        yield return null;
        ignoreClick = false;
    }

    private IEnumerator TypeLine()
    {
        StartTypewriterSound();
        foreach (char character in subtitles[index])
        {
            subtitlesText.text += character;
            yield return new WaitForSeconds(subtitlesSpeed);
        }
        StopTypewriterSound();
    }

    private void ApplyCurrentSubtitleColor()
    {
        subtitlesText.color = defaultSubtitleColor;

        if (activeColorOverrides == null)
            return;

        foreach (SubtitleColorOverride colorOverride in activeColorOverrides)
        {
            if (colorOverride != null &&
                colorOverride.subtitleIndex == index)
            {
                subtitlesText.color = colorOverride.color;
                return;
            }
        }
    }
    private AudioClip GetCurrentSubtitleAudioOverride()
    {
        if (activeAudioOverrides == null)
            return null;

        foreach (SubtitleAudioOverride audioOverride in activeAudioOverrides)
        {
            if (audioOverride != null &&
                audioOverride.subtitleIndex == index)
            {
                return audioOverride.audioClip;
            }
        }

        return null;
    }

    private void StartTypewriterSound()
    {
        if (typewriterSource == null)
            return;

        AudioClip clip = GetCurrentSubtitleAudioOverride();

        if (clip == null)
        {
            if (typewriterClips == null || typewriterClips.Length == 0)
                return;

            clip = typewriterClips[
                UnityEngine.Random.Range(0, typewriterClips.Length)];
        }

        typewriterSource.Stop();
        typewriterSource.pitch = UnityEngine.Random.Range(
            typewriterPitchRange.x,
            typewriterPitchRange.y);
        typewriterSource.clip = clip;
        typewriterSource.volume = typewriterVolume;
        typewriterSource.loop = true;
        typewriterSource.Play();
    }

    private void StopTypewriterSound()
    {
        if (typewriterSource == null)
            return;

        typewriterSource.Stop();
        typewriterSource.loop = false;
        typewriterSource.clip = null;
    }

    private void OnValidate()
    {
        if (typewriterPitchRange.x > typewriterPitchRange.y)
            typewriterPitchRange = new Vector2(
                typewriterPitchRange.y,
                typewriterPitchRange.x);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        StopTypewriterSound();
        ReleaseCutsceneLock();
        subtitles = null;
        onFinished = null;
        ignoreClick = false;
        subtitlesText.color = defaultSubtitleColor;
        activeColorOverrides = null;
        activeAudioOverrides = null;
    }

    private void NextSubtitle()
    {
        if (index < subtitles.Length - 1)
        {
            index++;
            subtitlesText.text = string.Empty;

            ApplyCurrentSubtitleColor();

            StartCoroutine(TypeLine());
            return;
        }

        Action finishedCallback = onFinished;
        onFinished = null;
        subtitles = null;

        ReleaseCutsceneLock();
        StopTypewriterSound();
        // subtitlesPanel과 이 컴포넌트가 같은 GameObject에 있을 수 있다.
        // SetActive(false)가 OnDisable을 즉시 호출하기 전에 콜백을 보관해야 한다.
        subtitlesPanel.SetActive(false);

        finishedCallback?.Invoke();
        subtitlesText.color = defaultSubtitleColor;
        activeColorOverrides = null;
        activeAudioOverrides = null;
    }

    private void AcquireCutsceneLock()
    {
        ownsCutsceneLock = true;
        GameState.isCutscene = true;
    }

    private void ReleaseCutsceneLock()
    {
        if (!ownsCutsceneLock)
            return;

        ownsCutsceneLock = false;
        GameState.isCutscene = false;
    }
}
