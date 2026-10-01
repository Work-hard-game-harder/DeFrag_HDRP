using System;
using System.Collections.Generic;
using StarterAssets;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// LobbyF opening cinematic. Every peer plays it locally when the scene starts: AI-generated shots
/// (Artlist / Veo) are cut together on a timeline with a film grade, letterbox, sound, subtitles and a
/// white NEXUS title card, then the view fades into gameplay. While it runs the local player is frozen,
/// gameplay input is gated and the game's own audio is paused. Nothing here is networked.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyIntroCinematic : MonoBehaviour
{
    [Serializable]
    public sealed class Shot
    {
        public VideoClip clip;
        [Tooltip("Used instead of a clip: a still frame animated by the push-in (and optional light flicker).")]
        public Texture2D still;
        [Tooltip("Play the clip's own sound track (e.g. generated foley).")]
        public bool playClipAudio;
        [Range(0f, 1f)] public float clipAudioVolume = 1f;
        [Tooltip("Still shots only: brightness flicker strength, like a failing street light.")]
        [Range(0f, 0.5f)] public float flicker;
        [Tooltip("Timeline second at which the shot starts (and fades in).")]
        [Min(0f)] public float start;
        [Tooltip("Timeline second at which the next shot has fully replaced this one. The last frame holds if the clip ends earlier.")]
        [Min(0f)] public float end;
        [Range(0.25f, 2f)] public float playbackSpeed = 1f;
        [Min(0f)] public float fadeIn = 0.6f;
        [Tooltip("Slow push-in: image scale at the shot's start and end.")]
        public Vector2 scale = new(1.02f, 1.08f);
    }

    [Serializable]
    public sealed class SoundCue
    {
        public AudioClip clip;
        [Min(0f)] public float at;
        [Range(0f, 1f)] public float volume = 1f;
        public bool loop;
        [Min(0f)] public float fadeIn;
        [Tooltip("Second at which the cue starts fading out; 0 = play to the end.")]
        [Min(0f)] public float fadeOutAt;
        [Min(0.01f)] public float fadeOutDuration = 1f;
    }

    [Serializable]
    public sealed class SubtitleLine
    {
        public string speaker;
        [TextArea] public string text;
        [Min(0f)] public float start;
        [Min(0f)] public float end;
    }

    public static bool IsPlaying { get; private set; }
    public static event Action Finished;

    // Enter Play Mode Options에서 도메인 리로드를 꺼도 이전 실행의 정적 상태가 남지 않게 합니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsPlaying = false;
        Finished = null;
    }

    [Header("Playback")]
    [SerializeField] private bool playOnStart = true;
    [Tooltip("Hold Space or Enter this long to skip. 0 disables skipping.")]
    [Min(0f)] [SerializeField] private float holdToSkipSeconds = 1.2f;
    [Tooltip("If the videos are not ready by then, the cinematic is skipped instead of blocking the game.")]
    [Min(1f)] [SerializeField] private float prepareTimeout = 10f;

    [Header("Timeline")]
    [SerializeField] private List<Shot> shots = new();
    [SerializeField] private List<SoundCue> sounds = new();
    [SerializeField] private List<SubtitleLine> subtitles = new();
    [Tooltip("Small location caption typed in at the start.")]
    [SerializeField] private string openingCaption = "02:13 AM  ·  NEXUS DATA SCIENCES HQ";
    [SerializeField] private Vector2 openingCaptionTime = new(0.8f, 4.2f);
    [Tooltip("White flash into the title card: start of the fade to white, and when the title fades out.")]
    [SerializeField] private Vector2 titleTime = new(17.2f, 21.2f);
    [SerializeField] private string titleText = "NEXUS";
    [SerializeField] private string titleSubtext = "DATA SCIENCES  ·  1F LOBBY";
    [Tooltip("Second at which the view starts fading into gameplay.")]
    [Min(0f)] [SerializeField] private float revealAt = 21.8f;
    [Min(0.05f)] [SerializeField] private float revealDuration = 1.2f;

    [Header("Look")]
    [SerializeField] private Shader gradeShader;
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("Letterbox target aspect (2.39 = anamorphic scope).")]
    [Min(1f)] [SerializeField] private float letterboxAspect = 2.39f;
    [Tooltip("자막 박스(32000) 등 다른 오버레이보다 위에 그립니다.")]
    [SerializeField] private int sortingOrder = 32500;
    [Range(0f, 0.3f)] [SerializeField] private float grain = 0.07f;
    [Range(0f, 1f)] [SerializeField] private float vignette = 0.4f;
    [Range(0.5f, 1.5f)] [SerializeField] private float contrast = 1.12f;
    [Range(0f, 1.5f)] [SerializeField] private float saturation = 0.86f;

    private enum Phase { Idle, Preparing, Playing, Done }

    private sealed class ShotView
    {
        public Shot Shot;
        public VideoPlayer Player;
        public AudioSource Audio;
        public RenderTexture Texture;
        public RawImage Image;
        public Material Material;
        public bool Started;
    }

    private sealed class SoundView
    {
        public SoundCue Cue;
        public AudioSource Source;
        public bool Started;
    }

    private readonly List<ShotView> shotViews = new();
    private readonly List<SoundView> soundViews = new();
    private Phase phase = Phase.Idle;
    private float clock;
    private float prepareDeadline;
    private float skipHeld;
    private bool skipping;
    private float skipFrom;
    private bool listenerWasPaused;

    private GameObject root;
    private CanvasGroup screenGroup;
    private Image black;
    private Image white;
    private RectTransform topBar;
    private RectTransform bottomBar;
    private TMP_Text caption;
    private TMP_Text subtitle;
    private CanvasGroup titleGroup;
    private TMP_Text title;
    private TMP_Text skipHint;
    private PersonController lockedPlayer;
    private bool lockedPlayerWasEnabled;

    private void Start()
    {
        // 씬 리로드를 끈 플레이 진입에서는 이전 실행의 phase가 남아 있을 수 있습니다.
        if (phase != Phase.Idle)
        {
            Teardown();
            phase = Phase.Idle;
            skipping = false;
            skipHeld = 0f;
        }
        if (playOnStart) Begin();
    }

    /// <summary>Starts the cinematic for the local player. Ignored if it is already running.</summary>
    public void Begin()
    {
        if (phase != Phase.Idle) return;
        if (shots.Count == 0 || shots.TrueForAll(s => s.clip == null && s.still == null))
        {
            phase = Phase.Done;
            return;
        }
        IsPlaying = true;
        listenerWasPaused = AudioListener.pause;
        AudioListener.pause = true;
        BuildScreen();
        foreach (Shot shot in shots)
            if (shot.clip != null || shot.still != null) shotViews.Add(CreateShot(shot));
        foreach (SoundCue cue in sounds)
            if (cue.clip != null) soundViews.Add(CreateSound(cue));
        prepareDeadline = Time.realtimeSinceStartup + prepareTimeout;
        phase = Phase.Preparing;
    }

    private void Update()
    {
        if (phase is Phase.Idle or Phase.Done) return;
        HoldLocalPlayer();

        if (phase == Phase.Preparing)
        {
            if (shotViews.TrueForAll(v => v.Player == null || v.Player.isPrepared))
            {
                phase = Phase.Playing;
                clock = 0f;
            }
            else if (Time.realtimeSinceStartup > prepareDeadline)
            {
                Debug.LogWarning("[LobbyIntroCinematic] Videos were not ready in time; skipping the cinematic.", this);
                Finish();
            }
            return;
        }

        clock += Time.unscaledDeltaTime;
        UpdateSkip();
        float t = skipping ? Mathf.Max(clock, revealAt) : clock;
        PoseShots(t);
        PoseSounds(t);
        PoseOverlay(t);
        if (t >= revealAt + revealDuration) Finish();
    }

    // ---------------- timeline ----------------

    private void PoseShots(float t)
    {
        foreach (ShotView view in shotViews)
        {
            Shot shot = view.Shot;
            if (!view.Started && t >= shot.start)
            {
                view.Started = true;
                if (view.Player != null)
                {
                    view.Player.playbackSpeed = shot.playbackSpeed;
                    view.Player.Play();
                }
            }
            float life = Mathf.InverseLerp(shot.start, Mathf.Max(shot.start + 0.01f, shot.end), t);
            float alpha = t < shot.start ? 0f : Smooth(Mathf.Clamp01((t - shot.start) / Mathf.Max(0.01f, shot.fadeIn)));
            view.Image.color = new Color(1f, 1f, 1f, alpha);
            float scale = Mathf.Lerp(shot.scale.x, shot.scale.y, life);
            view.Image.rectTransform.localScale = new Vector3(scale, scale, 1f);
            view.Material.SetFloat(GrainSeedId, Mathf.Floor(Time.unscaledTime * 24f) % 64f * 7.31f);
            if (shot.flicker > 0f)
            {
                // Irregular failing-light flicker: mostly steady with occasional dips.
                float n = Mathf.PerlinNoise(Time.unscaledTime * 9f, shot.start);
                float dip = n < 0.28f ? (0.28f - n) / 0.28f : 0f;
                view.Material.SetFloat(ExposureId, 1f - shot.flicker * dip);
            }
            if (view.Audio != null) view.Audio.volume = shot.clipAudioVolume * (skipping ? 0f : 1f);
        }
    }

    private void PoseSounds(float t)
    {
        foreach (SoundView view in soundViews)
        {
            SoundCue cue = view.Cue;
            if (!view.Started && t >= cue.at && !skipping)
            {
                view.Started = true;
                view.Source.Play();
            }
            if (!view.Started) continue;
            float gain = cue.fadeIn > 0f ? Mathf.Clamp01((t - cue.at) / cue.fadeIn) : 1f;
            if (cue.fadeOutAt > 0f) gain *= 1f - Mathf.Clamp01((t - cue.fadeOutAt) / cue.fadeOutDuration);
            if (skipping) gain *= 1f - Mathf.Clamp01((clock - skipFrom) / 0.5f);
            view.Source.volume = cue.volume * gain;
        }
    }

    private void PoseOverlay(float t)
    {
        // Letterbox slides in over the first second and out with the reveal.
        float barsIn = Smooth(Mathf.Clamp01(t / 1.2f)) * (1f - Smooth(Mathf.Clamp01((t - revealAt) / revealDuration)));
        float bar = BarFraction() * barsIn;
        topBar.anchorMin = new Vector2(0f, 1f - bar);
        bottomBar.anchorMax = new Vector2(1f, bar);

        caption.alpha = Window(t, openingCaptionTime.x, openingCaptionTime.x + 0.4f, openingCaptionTime.y - 0.6f, openingCaptionTime.y);
        int typed = Mathf.Clamp(Mathf.FloorToInt((t - openingCaptionTime.x) * 26f), 0, openingCaption.Length);
        caption.maxVisibleCharacters = typed;

        SubtitleLine line = subtitles.Find(s => t >= s.start && t < s.end);
        if (line != null)
        {
            subtitle.text = string.IsNullOrWhiteSpace(line.speaker)
                ? line.text
                : $"<color=#8FD8FF>{line.speaker}</color>   {line.text}";
            subtitle.alpha = Window(t, line.start, line.start + 0.25f, line.end - 0.3f, line.end);
        }
        else subtitle.alpha = 0f;

        // White flash into the title card, which then dissolves to black before gameplay.
        float whiteIn = Smooth(Mathf.Clamp01((t - titleTime.x) / 0.9f));
        float whiteOut = Smooth(Mathf.Clamp01((t - titleTime.y) / 0.6f));
        white.color = new Color(white.color.r, white.color.g, white.color.b, whiteIn * (1f - whiteOut));
        titleGroup.alpha = Window(t, titleTime.x + 0.9f, titleTime.x + 1.6f, titleTime.y - 0.5f, titleTime.y);
        title.characterSpacing = Mathf.Lerp(60f, 34f, Smooth(Mathf.Clamp01((t - titleTime.x - 0.9f) / 3f)));

        screenGroup.alpha = 1f - Smooth(Mathf.Clamp01((t - revealAt) / revealDuration));
        bool showVideo = t < titleTime.y + 0.6f;
        foreach (ShotView view in shotViews) view.Image.enabled = showVideo;
    }

    private void UpdateSkip()
    {
        if (holdToSkipSeconds <= 0f || skipping)
        {
            skipHint.alpha = 0f;
            return;
        }
        bool held = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
        skipHeld = held ? skipHeld + Time.unscaledDeltaTime : Mathf.MoveTowards(skipHeld, 0f, Time.unscaledDeltaTime * 2f);
        float progress = Mathf.Clamp01(skipHeld / holdToSkipSeconds);
        skipHint.alpha = clock > 1.5f && clock < revealAt ? Mathf.Lerp(0.35f, 1f, progress) * (1f - titleGroup.alpha) : 0f;
        skipHint.text = progress > 0f ? $"SKIP  {new string('■', Mathf.CeilToInt(progress * 8f))}" : "HOLD SPACE TO SKIP";
        if (progress >= 1f)
        {
            skipping = true;
            skipFrom = clock;
            clock = Mathf.Max(clock, revealAt);
        }
    }

    // ---------------- local player lock ----------------

    private void HoldLocalPlayer()
    {
        GameplayInputGate.TryAcquire(this);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        PersonController player = FindLocalPlayer();
        if (player == lockedPlayer) return;
        ReleaseLocalPlayer();
        lockedPlayer = player;
        if (lockedPlayer == null) return;
        lockedPlayerWasEnabled = lockedPlayer.enabled;
        lockedPlayer.enabled = false;
    }

    private static PersonController FindLocalPlayer()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
        {
            NetworkObject playerObject = manager.IsClient ? manager.LocalClient?.PlayerObject : null;
            return playerObject != null ? playerObject.GetComponent<PersonController>() : null;
        }
        return FindAnyObjectByType<PersonController>();
    }

    private void ReleaseLocalPlayer()
    {
        if (lockedPlayer != null) lockedPlayer.enabled = lockedPlayerWasEnabled;
        lockedPlayer = null;
    }

    private void Finish()
    {
        if (phase == Phase.Done) return;
        phase = Phase.Done;
        ReleaseLocalPlayer();
        GameplayInputGate.Release(this);
        AudioListener.pause = listenerWasPaused;
        Teardown();
        IsPlaying = false;
        Finished?.Invoke();
    }

    private void OnDisable()
    {
        if (phase is Phase.Preparing or Phase.Playing) Finish();
    }

    private void OnDestroy()
    {
        if (IsPlaying && phase != Phase.Done) Finish();
    }

    // ---------------- construction ----------------

    private static readonly int GrainSeedId = Shader.PropertyToID("_GrainSeed");
    private static readonly int ExposureId = Shader.PropertyToID("_Exposure");

    private void BuildScreen()
    {
        root = new GameObject("Lobby Intro Cinematic (Local)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        screenGroup = root.GetComponent<CanvasGroup>();
        screenGroup.blocksRaycasts = true;

        black = Panel("Black", root.transform, Color.black);
        Stretch(black.rectTransform);
    }

    private ShotView CreateShot(Shot shot)
    {
        var view = new ShotView { Shot = shot };
        int width, height;
        Texture source;
        if (shot.clip != null)
        {
            width = (int)Mathf.Max(16, shot.clip.width);
            height = (int)Mathf.Max(16, shot.clip.height);
            view.Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = $"Intro {shot.clip.name}" };
            view.Texture.Create();
            source = view.Texture;

            view.Player = root.AddComponent<VideoPlayer>();
            view.Player.playOnAwake = false;
            view.Player.source = VideoSource.VideoClip;
            view.Player.clip = shot.clip;
            view.Player.renderMode = VideoRenderMode.RenderTexture;
            view.Player.targetTexture = view.Texture;
            if (shot.playClipAudio && shot.clip.audioTrackCount > 0)
            {
                // Routed through an AudioSource so it keeps playing while the game's audio is paused.
                view.Audio = root.AddComponent<AudioSource>();
                view.Audio.playOnAwake = false;
                view.Audio.spatialBlend = 0f;
                view.Audio.ignoreListenerPause = true;
                view.Audio.volume = shot.clipAudioVolume;
                view.Player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                view.Player.controlledAudioTrackCount = 1;
                view.Player.EnableAudioTrack(0, true);
                view.Player.SetTargetAudioSource(0, view.Audio);
            }
            else view.Player.audioOutputMode = VideoAudioOutputMode.None;
            view.Player.isLooping = false;
            view.Player.skipOnDrop = false;
            view.Player.waitForFirstFrame = true;
            view.Player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            view.Player.Prepare();
        }
        else
        {
            width = Mathf.Max(16, shot.still.width);
            height = Mathf.Max(16, shot.still.height);
            source = shot.still;
        }

        var image = new GameObject($"Shot {(shot.clip != null ? shot.clip.name : shot.still.name)}", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        image.transform.SetParent(root.transform, false);
        view.Image = image.GetComponent<RawImage>();
        view.Image.texture = source;
        view.Image.raycastTarget = false;
        view.Image.color = new Color(1f, 1f, 1f, 0f);
        Stretch(view.Image.rectTransform);
        var fitter = image.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = (float)width / height;

        Shader shader = gradeShader != null ? gradeShader : Shader.Find("Hidden/DeFrag/CinematicGrade");
        view.Material = new Material(shader) { name = "Intro Grade" };
        view.Material.SetFloat("_Grain", grain);
        view.Material.SetFloat("_Vignette", vignette);
        view.Material.SetFloat("_Contrast", contrast);
        view.Material.SetFloat("_Saturation", saturation);
        view.Material.SetFloat("_Aspect", (float)Screen.width / Mathf.Max(1, Screen.height));
        view.Image.material = view.Material;

        // Overlay layers stay above every shot.
        if (topBar != null) PushOverlaysToFront();
        else BuildOverlays();
        return view;
    }

    private void BuildOverlays()
    {
        topBar = Panel("Letterbox Top", root.transform, Color.black).rectTransform;
        topBar.anchorMin = new Vector2(0f, 1f);
        topBar.anchorMax = Vector2.one;
        topBar.offsetMin = topBar.offsetMax = Vector2.zero;
        bottomBar = Panel("Letterbox Bottom", root.transform, Color.black).rectTransform;
        bottomBar.anchorMin = Vector2.zero;
        bottomBar.anchorMax = new Vector2(1f, 0f);
        bottomBar.offsetMin = bottomBar.offsetMax = Vector2.zero;

        caption = Label("Caption", root.transform, 26f, TextAlignmentOptions.TopLeft, new Color(0.85f, 0.93f, 1f, 1f), 10f);
        caption.text = openingCaption;
        var captionRect = caption.rectTransform;
        captionRect.anchorMin = captionRect.anchorMax = new Vector2(0f, 1f);
        captionRect.pivot = new Vector2(0f, 1f);
        captionRect.anchoredPosition = new Vector2(96f, -(BarFraction() * 1080f) - 40f);
        captionRect.sizeDelta = new Vector2(1400f, 50f);

        subtitle = Label("Subtitle", root.transform, 36f, TextAlignmentOptions.Center, Color.white, 2f);
        var subtitleRect = subtitle.rectTransform;
        subtitleRect.anchorMin = new Vector2(0.1f, 0f);
        subtitleRect.anchorMax = new Vector2(0.9f, 0f);
        subtitleRect.pivot = new Vector2(0.5f, 0.5f);
        subtitleRect.anchoredPosition = new Vector2(0f, Mathf.Max(60f, BarFraction() * 1080f * 0.5f));
        subtitleRect.sizeDelta = new Vector2(0f, 60f);
        subtitle.fontStyle = FontStyles.Normal;

        white = Panel("White", root.transform, new Color(0.965f, 0.973f, 0.98f, 0f));
        Stretch(white.rectTransform);

        var titleRoot = new GameObject("Title Card", typeof(RectTransform), typeof(CanvasGroup));
        titleRoot.transform.SetParent(root.transform, false);
        Stretch((RectTransform)titleRoot.transform);
        titleGroup = titleRoot.GetComponent<CanvasGroup>();
        titleGroup.alpha = 0f;
        Color ink = new(0.15f, 0.19f, 0.23f);
        title = Label("Title", titleRoot.transform, 150f, TextAlignmentOptions.Center, ink, 34f);
        title.text = titleText;
        Place(title.rectTransform, new Vector2(0f, 0.48f), new Vector2(1f, 0.68f));
        TMP_Text sub = Label("Subtitle", titleRoot.transform, 34f, TextAlignmentOptions.Center, new Color(0.36f, 0.41f, 0.47f), 26f);
        sub.text = titleSubtext;
        Place(sub.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 0.46f));
        Image rule = Panel("Rule", titleRoot.transform, new Color(0.23f, 0.62f, 0.88f));
        rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(0.5f, 0.47f);
        rule.rectTransform.sizeDelta = new Vector2(520f, 3f);

        skipHint = Label("Skip Hint", root.transform, 20f, TextAlignmentOptions.BottomRight, new Color(1f, 1f, 1f, 1f), 8f);
        var skipRect = skipHint.rectTransform;
        skipRect.anchorMin = skipRect.anchorMax = new Vector2(1f, 0f);
        skipRect.pivot = new Vector2(1f, 0f);
        skipRect.anchoredPosition = new Vector2(-40f, 24f);
        skipRect.sizeDelta = new Vector2(500f, 30f);
        skipHint.alpha = 0f;
    }

    private void PushOverlaysToFront()
    {
        topBar.SetAsLastSibling();
        bottomBar.SetAsLastSibling();
        caption.transform.SetAsLastSibling();
        subtitle.transform.SetAsLastSibling();
        white.transform.SetAsLastSibling();
        titleGroup.transform.SetAsLastSibling();
        skipHint.transform.SetAsLastSibling();
    }

    private SoundView CreateSound(SoundCue cue)
    {
        var source = root.AddComponent<AudioSource>();
        source.clip = cue.clip;
        source.loop = cue.loop;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
        source.volume = 0f;
        return new SoundView { Cue = cue, Source = source };
    }

    private void Teardown()
    {
        foreach (ShotView view in shotViews)
        {
            if (view.Player != null) view.Player.Stop();
            if (view.Audio != null) view.Audio.Stop();
            if (view.Texture != null)
            {
                view.Texture.Release();
                Destroy(view.Texture);
            }
            if (view.Material != null) Destroy(view.Material);
        }
        shotViews.Clear();
        soundViews.Clear();
        if (root != null) Destroy(root);
    }

    // ---------------- helpers ----------------

    private float BarFraction()
    {
        float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        return Mathf.Clamp01((1f - screenAspect / letterboxAspect) * 0.5f);
    }

    private static float Smooth(float x) => x * x * (3f - 2f * x);

    private static float Window(float t, float inStart, float inEnd, float outStart, float outEnd) =>
        Smooth(Mathf.Clamp01((t - inStart) / Mathf.Max(0.01f, inEnd - inStart))) *
        (1f - Smooth(Mathf.Clamp01((t - outStart) / Mathf.Max(0.01f, outEnd - outStart))));

    private static Image Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TMP_Text Label(string name, Transform parent, float size, TextAlignmentOptions alignment, Color color, float spacing)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.characterSpacing = spacing;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
