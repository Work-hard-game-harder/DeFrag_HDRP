using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class SubtitlesScript : MonoBehaviour
{
    private sealed class PlaybackRequest
    {
        public string[] Subtitles;
        public SubtitleColorOverride[] ColorOverrides;
        public SubtitleAudioOverride[] AudioOverrides;
        public Action OnFinished;
        public Action OnStarted;
        public int PauseAfterIndex = -1;
        public Action<Action> PlayInterlude;
    }

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
    private int activePauseAfterIndex = -1;
    private Action<Action> activeInterlude;
    private bool interludePlayed;
    private bool interludePending;
    private int playbackVersion;
    private Color defaultSubtitleColor;
    private readonly Queue<PlaybackRequest> playbackQueue = new();

    public bool IsPlaying => subtitles != null;

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
        PlaySubtitles(
            newSubtitles,
            colorOverrides,
            audioOverrides,
            callback,
            null);
    }

    public void PlaySubtitles(
        string[] newSubtitles,
        SubtitleColorOverride[] colorOverrides,
        SubtitleAudioOverride[] audioOverrides,
        Action callback,
        Action onStarted)
    {
        if (newSubtitles == null || newSubtitles.Length == 0)
        {
            onStarted?.Invoke();
            callback?.Invoke();
            return;
        }

        PlaybackRequest request = new()
        {
            Subtitles = newSubtitles,
            ColorOverrides = colorOverrides,
            AudioOverrides = audioOverrides,
            OnFinished = callback,
            OnStarted = onStarted
        };

        if (IsPlaying || playbackQueue.Count > 0)
        {
            playbackQueue.Enqueue(request);
            TryStartNextQueuedPlayback();
            return;
        }

        StartPlayback(request);
    }

    /// <summary>
    /// 하나의 자막 목록을 유지한 채 지정한 Element 뒤에서 로컬 연출을 실행하고,
    /// 연출이 resume 콜백을 호출하면 다음 Element부터 계속 재생합니다.
    /// </summary>
    public void PlaySubtitlesWithInterlude(
        string[] newSubtitles,
        SubtitleColorOverride[] colorOverrides,
        SubtitleAudioOverride[] audioOverrides,
        int pauseAfterIndex,
        Action<Action> playInterlude,
        Action callback,
        Action onStarted)
    {
        if (newSubtitles == null || newSubtitles.Length == 0)
        {
            onStarted?.Invoke();
            callback?.Invoke();
            return;
        }

        PlaybackRequest request = new()
        {
            Subtitles = newSubtitles,
            ColorOverrides = colorOverrides,
            AudioOverrides = audioOverrides,
            OnFinished = callback,
            OnStarted = onStarted,
            PauseAfterIndex = pauseAfterIndex,
            PlayInterlude = playInterlude
        };

        if (IsPlaying || playbackQueue.Count > 0)
        {
            playbackQueue.Enqueue(request);
            TryStartNextQueuedPlayback();
            return;
        }

        StartPlayback(request);
    }

    private void StartPlayback(PlaybackRequest request)
    {
        subtitles = request.Subtitles;
        activeColorOverrides = request.ColorOverrides;
        activeAudioOverrides = request.AudioOverrides;
        activePauseAfterIndex = request.PauseAfterIndex;
        activeInterlude = request.PlayInterlude;
        interludePlayed = false;
        interludePending = false;
        playbackVersion++;

        index = 0;
        onFinished = request.OnFinished;

        subtitlesText.text = string.Empty;
        subtitlesPanel.SetActive(true);

        ApplyCurrentSubtitleColor();
        request.OnStarted?.Invoke();

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
        activePauseAfterIndex = -1;
        activeInterlude = null;
        interludePlayed = false;
        interludePending = false;
        playbackVersion++;
        playbackQueue.Clear();
    }

    private void NextSubtitle()
    {
        if (TryStartInterlude())
            return;

        AdvanceAfterCurrentSubtitle();
    }

    private bool TryStartInterlude()
    {
        if (interludePlayed || interludePending || activeInterlude == null ||
            index != activePauseAfterIndex)
            return false;

        interludePlayed = true;
        interludePending = true;
        ignoreClick = true;
        StopTypewriterSound();
        subtitlesText.text = string.Empty;

        int version = playbackVersion;
        activeInterlude.Invoke(() => ResumeAfterInterlude(version));
        return true;
    }

    private void ResumeAfterInterlude(int version)
    {
        if (version != playbackVersion || !interludePending || subtitles == null)
            return;

        interludePending = false;
        ignoreClick = false;
        AdvanceAfterCurrentSubtitle();
        StartCoroutine(IgnoreClickThisFrame());
    }

    private void AdvanceAfterCurrentSubtitle()
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
        // 이미 대기 중인 자막이 있으면 비활성화로 큐를 지우지 않고 바로 이어간다.
        if (playbackQueue.Count == 0)
            subtitlesPanel.SetActive(false);

        finishedCallback?.Invoke();
        subtitlesText.color = defaultSubtitleColor;
        activeColorOverrides = null;
        activeAudioOverrides = null;
        activePauseAfterIndex = -1;
        activeInterlude = null;
        interludePlayed = false;
        interludePending = false;

        TryStartNextQueuedPlayback();
    }

    private void TryStartNextQueuedPlayback()
    {
        if (IsPlaying || playbackQueue.Count == 0 || !isActiveAndEnabled)
            return;

        StartPlayback(playbackQueue.Dequeue());
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
