using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Game-start prologue ("1막 - PROLOGUE: 넥서스의 흥망"). Once per launch, right after the Unity splash,
/// the first build scene (MainLobby) is covered by a full-screen overlay that plays the pre-rendered
/// prologue movie, then fades away to reveal the lobby. Hold Space (or Enter) to skip.
/// Purely local presentation: nothing here is networked and no scene has to reference it.
/// The movie is rendered by Tools/Claude/render_prologue.py into Resources/Prologue.
/// </summary>
[DisallowMultipleComponent]
public sealed class GamePrologueCinematic : MonoBehaviour
{
    private const string ClipResource = "Prologue/Prologue_Cinematic";
    private const int SortingOrder = 32700;
    private const float HoldToSkipSeconds = 1.2f;
    private const float PrepareTimeout = 8f;
    private const float FadeOutSeconds = 0.8f;
    private const float StallSeconds = 2f;
    private const float SkipBarWidth = 260f;

#if UNITY_EDITOR
    /// <summary>EditorPrefs key: play the prologue when entering Play Mode in the first build scene.</summary>
    public const string PlayInEditorPrefKey = "DeFrag.Prologue.PlayInEditor";
#endif

    public static bool IsPlaying { get; private set; }
    public static event Action Finished;

    private static bool shownThisLaunch;

    // Enter Play Mode Options에서 도메인 리로드를 꺼도 이전 실행의 정적 상태가 남지 않게 합니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        shownThisLaunch = false;
        IsPlaying = false;
        Finished = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void PlayOnLaunch()
    {
        if (shownThisLaunch) return;
        shownThisLaunch = true;
        // Only when the game starts from its first scene (the main lobby), not when testing other scenes.
        if (SceneManager.GetActiveScene().buildIndex != 0) return;
#if UNITY_EDITOR
        if (!UnityEditor.EditorPrefs.GetBool(PlayInEditorPrefKey, false)) return;
#endif
        var clip = Resources.Load<VideoClip>(ClipResource);
        if (clip == null)
        {
            Debug.LogWarning("[GamePrologueCinematic] Resources/" + ClipResource + " is missing; skipping the prologue.");
            return;
        }
        var host = new GameObject("Game Prologue Cinematic");
        DontDestroyOnLoad(host);
        host.AddComponent<GamePrologueCinematic>().Play(clip);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>Test entry: plays the prologue now, in any scene.</summary>
    public static GamePrologueCinematic PlayForTesting()
    {
        var clip = Resources.Load<VideoClip>(ClipResource);
        if (clip == null) return null;
        var host = new GameObject("Game Prologue Cinematic (Test)");
        DontDestroyOnLoad(host);
        var cinematic = host.AddComponent<GamePrologueCinematic>();
        cinematic.Play(clip);
        return cinematic;
    }

    public double TimeForTesting => player != null ? player.time : -1;
    public long FrameForTesting => player != null ? player.frame : -1;
    public bool AudioPlayingForTesting => audioSource != null && audioSource.isPlaying;
#endif

    private enum Phase { Preparing, Playing, FadingOut, Done }

    private Phase phase = Phase.Done;
    private VideoPlayer player;
    private AudioSource audioSource;
    private RenderTexture texture;
    private CanvasGroup group;
    private RawImage image;
    private TMP_Text skipHint;
    private Image skipBar;
    private float prepareDeadline;
    private float skipHeld;
    private float fadeStart;
    private bool listenerWasPaused;
    private long lastFrame = -1;
    private float lastFrameTime;

    private void Play(VideoClip clip)
    {
        IsPlaying = true;
        listenerWasPaused = AudioListener.pause;
        AudioListener.pause = true; // the lobby's own music waits; the prologue sound ignores the pause
        BuildScreen();

        texture = new RenderTexture((int)clip.width, (int)clip.height, 0, RenderTextureFormat.ARGB32) { name = "Prologue" };
        texture.Create();
        image.texture = texture;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.ignoreListenerPause = true;

        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.source = VideoSource.VideoClip;
        player.clip = clip;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = texture;
        player.isLooping = false;
        player.waitForFirstFrame = true;
        player.skipOnDrop = true; // keep picture and sound in sync
        player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        if (clip.audioTrackCount > 0)
        {
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, true);
            player.SetTargetAudioSource(0, audioSource);
        }
        else player.audioOutputMode = VideoAudioOutputMode.None;
        player.loopPointReached += _ => BeginFadeOut();
        player.errorReceived += (_, message) =>
        {
            Debug.LogWarning("[GamePrologueCinematic] " + message);
            BeginFadeOut();
        };
        player.Prepare();
        prepareDeadline = Time.realtimeSinceStartup + PrepareTimeout;
        phase = Phase.Preparing;
    }

    private void Update()
    {
        switch (phase)
        {
            case Phase.Preparing:
                if (player.isPrepared)
                {
                    player.Play();
                    phase = Phase.Playing;
                    lastFrameTime = Time.unscaledTime;
                }
                else if (Time.realtimeSinceStartup > prepareDeadline)
                {
                    Debug.LogWarning("[GamePrologueCinematic] The prologue was not ready in time; skipping it.");
                    BeginFadeOut();
                }
                UpdateSkip();
                break;
            case Phase.Playing:
                UpdateSkip();
                WatchForStall();
                break;
            case Phase.FadingOut:
                float k = Mathf.Clamp01((Time.unscaledTime - fadeStart) / FadeOutSeconds);
                group.alpha = 1f - k * k * (3f - 2f * k);
                audioSource.volume = 1f - k;
                if (k >= 1f) Finish();
                break;
        }
    }

    private void UpdateSkip()
    {
        bool held = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
        skipHeld = held
            ? skipHeld + Time.unscaledDeltaTime
            : Mathf.MoveTowards(skipHeld, 0f, Time.unscaledDeltaTime * 2f);
        float progress = Mathf.Clamp01(skipHeld / HoldToSkipSeconds);
        skipHint.alpha = Mathf.Lerp(0.45f, 1f, progress);
        skipBar.rectTransform.sizeDelta = new Vector2(SkipBarWidth * progress, 3f);
        if (progress >= 1f) BeginFadeOut();
    }

    /// <summary>A decoder that fell behind can sit on one frame; nudge it back to the playback clock.</summary>
    private void WatchForStall()
    {
        if (!player.isPlaying) return;
        if (player.frame != lastFrame)
        {
            lastFrame = player.frame;
            lastFrameTime = Time.unscaledTime;
        }
        else if (Time.unscaledTime - lastFrameTime > StallSeconds)
        {
            lastFrameTime = Time.unscaledTime;
            player.time = player.clockTime;
        }
    }

    private void BeginFadeOut()
    {
        if (phase is Phase.FadingOut or Phase.Done) return;
        phase = Phase.FadingOut;
        fadeStart = Time.unscaledTime;
        skipHint.alpha = 0f;
        skipBar.enabled = false;
    }

    private void Finish()
    {
        if (phase == Phase.Done) return;
        phase = Phase.Done;
        if (player != null) player.Stop();
        AudioListener.pause = listenerWasPaused;
        IsPlaying = false;
        Finished?.Invoke();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }
        if (phase != Phase.Done)
        {
            AudioListener.pause = listenerWasPaused;
            IsPlaying = false;
        }
    }

    // ---------------- screen ----------------

    private void BuildScreen()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true; // the lobby underneath cannot be clicked while the prologue runs

        Image black = NewChild<Image>("Black");
        black.color = Color.black;
        black.raycastTarget = true;
        Stretch(black.rectTransform);

        image = NewChild<RawImage>("Movie");
        image.raycastTarget = false;
        Stretch(image.rectTransform);
        var fitter = image.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 16f / 9f;

        skipHint = NewChild<TextMeshProUGUI>("Skip Hint");
        DefragUiTheme theme = DefragUiTheme.Current;
        if (theme != null && theme.font != null) skipHint.font = theme.font;
        skipHint.text = "HOLD SPACE TO SKIP";
        skipHint.fontSize = 20f;
        skipHint.characterSpacing = 8f;
        skipHint.alignment = TextAlignmentOptions.BottomRight;
        skipHint.color = new Color(1f, 1f, 1f, 0.8f);
        skipHint.raycastTarget = false;
        RectTransform hintRect = skipHint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(1f, 0f);
        hintRect.anchoredPosition = new Vector2(-48f, 22f);
        hintRect.sizeDelta = new Vector2(520f, 30f);

        skipBar = NewChild<Image>("Skip Progress");
        skipBar.color = new Color(1f, 1f, 1f, 0.85f);
        skipBar.raycastTarget = false;
        RectTransform barRect = skipBar.rectTransform;
        barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(1f, 0f);
        barRect.anchoredPosition = new Vector2(-48f, 14f);
        barRect.sizeDelta = new Vector2(0f, 3f);
    }

    private T NewChild<T>(string childName) where T : Component
    {
        var child = new GameObject(childName, typeof(RectTransform));
        child.transform.SetParent(transform, false);
        return child.AddComponent<T>();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
