using System;
using StarterAssets;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Local full-screen cinematic that covers a scene load. It survives the load (DontDestroyOnLoad) and
/// only fades away once the movie is over (or skipped) AND the destination scene is loaded with this
/// peer's player spawned in it. Until then it holds on black with a loading line.
/// A separate soundtrack AudioClip, when given, is the playback clock: the picture is re-seeked to it
/// after loading hitches (the VideoPlayer's own audio output stutters and stalls the picture there).
/// </summary>
public sealed class CinematicLoadingOverlay : MonoBehaviour
{
    private const int SortingOrder = 32600; // above scene cinematics (32500), below the prologue (32700)
    private const float FadeInSeconds = 0.3f;
    private const float FadeOutSeconds = 0.8f;
    private const float PrepareTimeout = 15f;
    private const float PlayerSpawnTimeout = 15f;
    private const float SafetyMarginSeconds = 90f;
    private const float HoldToSkipSeconds = 1.2f;
    private const float MaxDriftSeconds = 0.2f;
    private const float SkipBarWidth = 260f;

    public static bool IsPlaying => active != null;
    public static string Destination => active != null ? active.destination : null;
    public static event Action Finished;

    private static CinematicLoadingOverlay active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active = null;
        Finished = null;
    }

    private enum Phase { Preparing, Playing, Holding, FadingOut, Done }

    private Phase phase;
    private SceneTransitionCinematicLibrary.Entry entry;
    private string destination;
    private VideoPlayer video;
    private AudioSource soundtrack;
    private AudioListener fallbackListener;
    private RenderTexture texture;
    private CanvasGroup group;
    private RawImage movie;
    private CanvasGroup placeholderGroup;
    private TMP_Text status;
    private TMP_Text skipHint;
    private Image skipBar;
    private float startedAt;
    private float playStartedAt;
    private float fadeStart;
    private float hardDeadline;
    private float sceneReadyAt = -1f;
    private float skipHeld;
    private long lastFrame = -1;
    private float lastFrameTime;
    private bool wasNetworked;
    private bool listenerWasPaused;
    private PersonController lockedPlayer;
    private bool lockedPlayerWasEnabled;

    /// <summary>Starts covering the load of <paramref name="scene"/>. Ignored while one is already running.</summary>
    public static void Begin(SceneTransitionCinematicLibrary.Entry cinematic, string scene)
    {
        if (active != null || cinematic == null) return;
        var root = new GameObject("Cinematic loading", typeof(RectTransform));
        DontDestroyOnLoad(root);
        active = root.AddComponent<CinematicLoadingOverlay>();
        active.Initialize(cinematic, scene);
    }

    public static void Begin(VideoClip clip, string scene) =>
        Begin(new SceneTransitionCinematicLibrary.Entry { destinationScene = scene, video = clip }, scene);

    public static void Cancel() { if (active != null) active.Finish(); }

    private void Initialize(SceneTransitionCinematicLibrary.Entry cinematic, string scene)
    {
        entry = cinematic;
        destination = scene;
        startedAt = Time.unscaledTime;
        wasNetworked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        listenerWasPaused = AudioListener.pause;
        AudioListener.pause = true; // the old scene's sounds stop; the cinematic sound ignores the pause
        SceneManager.sceneLoaded += OnSceneLoaded;
        BuildScreen();

        fallbackListener = gameObject.AddComponent<AudioListener>();
        fallbackListener.enabled = false;
        soundtrack = gameObject.AddComponent<AudioSource>();
        soundtrack.playOnAwake = false;
        soundtrack.spatialBlend = 0f;
        soundtrack.ignoreListenerPause = true;
        soundtrack.clip = entry.soundtrack;
        if (entry.soundtrack != null) entry.soundtrack.LoadAudioData();

        float length = entry.placeholderSeconds;
        if (entry.video != null)
        {
            length = (float)entry.video.length;
            texture = new RenderTexture((int)Mathf.Max(16, entry.video.width), (int)Mathf.Max(16, entry.video.height), 0,
                RenderTextureFormat.ARGB32) { name = "Transition Cinematic" };
            texture.Create();
            movie.texture = texture;
            movie.GetComponent<AspectRatioFitter>().aspectRatio = (float)texture.width / texture.height;

            video = gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.source = VideoSource.VideoClip;
            video.clip = entry.video;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = texture;
            video.isLooping = false;
            video.waitForFirstFrame = true;
            video.skipOnDrop = false;
            video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            video.audioOutputMode = entry.soundtrack != null || entry.video.audioTrackCount == 0
                ? VideoAudioOutputMode.None
                : VideoAudioOutputMode.Direct;
            video.loopPointReached += _ => EndPlayback();
            video.errorReceived += (_, message) =>
            {
                Debug.LogWarning($"[CinematicLoadingOverlay] {message}");
                EndPlayback();
            };
            video.Prepare();
        }
        hardDeadline = startedAt + length + SafetyMarginSeconds;
        phase = Phase.Preparing;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (string.Equals(scene.name, destination, StringComparison.OrdinalIgnoreCase)) sceneReadyAt = Time.unscaledTime;
    }

    private void Update()
    {
        HoldLocalPlayer();
        UpdateFallbackListener();
        if (wasNetworked && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
        {
            // The session ended mid-transition (disconnect, return to menu): get out of the way.
            Finish();
            return;
        }

        float elapsed = Time.unscaledTime - startedAt;
        if (phase != Phase.FadingOut) group.alpha = Mathf.Clamp01(elapsed / FadeInSeconds);
        if (Time.unscaledTime > hardDeadline && phase < Phase.FadingOut) BeginFadeOut();

        switch (phase)
        {
            case Phase.Preparing:
                UpdateSkip();
                if (phase != Phase.Preparing) break;
                if (video == null)
                {
                    StartPlayback();
                }
                else if (video.isPrepared && (soundtrack.clip == null || soundtrack.clip.loadState != AudioDataLoadState.Loading))
                {
                    StartPlayback();
                }
                else if (elapsed > PrepareTimeout)
                {
                    Debug.LogWarning("[CinematicLoadingOverlay] The cinematic was not ready in time; holding on black.", this);
                    EndPlayback();
                }
                break;
            case Phase.Playing:
                UpdateSkip();
                if (phase != Phase.Playing) break;
                if (video != null) KeepInSync();
                else
                {
                    float t = Time.unscaledTime - playStartedAt;
                    placeholderGroup.alpha = Window(t, 0f, 0.6f, entry.placeholderSeconds - 0.6f, entry.placeholderSeconds);
                    if (t >= entry.placeholderSeconds) EndPlayback();
                }
                break;
            case Phase.Holding:
                status.alpha = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
                if (DestinationReady()) BeginFadeOut();
                break;
            case Phase.FadingOut:
                float k = Mathf.Clamp01((Time.unscaledTime - fadeStart) / FadeOutSeconds);
                group.alpha = 1f - k * k * (3f - 2f * k);
                soundtrack.volume = 1f - k;
                if (k >= 1f) Finish();
                break;
        }
    }

    private void StartPlayback()
    {
        phase = Phase.Playing;
        playStartedAt = Time.unscaledTime;
        lastFrameTime = Time.unscaledTime;
        if (soundtrack.clip != null && soundtrack.clip.loadState != AudioDataLoadState.Loaded)
        {
            Debug.LogWarning("[CinematicLoadingOverlay] The soundtrack failed to load; playing the picture alone.", this);
            soundtrack.clip = null;
        }
        if (video != null)
        {
            movie.enabled = true;
            video.Play();
            if (soundtrack.clip != null) soundtrack.Play();
        }
    }

    private void EndPlayback()
    {
        if (phase >= Phase.Holding) return;
        if (video != null) video.Pause();
        phase = Phase.Holding;
        skipHint.alpha = 0f;
        skipBar.enabled = false;
        placeholderGroup.alpha = 0f;
        // The last frame is black in our films; hide it anyway so a skipped movie does not freeze on screen.
        movie.enabled = false;
        if (soundtrack.isPlaying) soundtrack.Stop();
        status.enabled = true;
        if (DestinationReady()) BeginFadeOut();
    }

    private bool DestinationReady()
    {
        if (sceneReadyAt < 0f) return false;
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsClient) return true;
        // Wait for this peer's player to be spawned in the new scene, or the reveal shows no camera.
        NetworkObject player = manager.LocalClient?.PlayerObject;
        bool spawned = player != null && player.IsSpawned &&
            string.Equals(player.gameObject.scene.name, destination, StringComparison.OrdinalIgnoreCase);
        return spawned || Time.unscaledTime - sceneReadyAt > PlayerSpawnTimeout;
    }

    private void BeginFadeOut()
    {
        if (phase >= Phase.FadingOut) return;
        if (phase < Phase.Holding) EndPlayback();
        if (phase >= Phase.FadingOut) return;
        phase = Phase.FadingOut;
        fadeStart = Time.unscaledTime;
        status.enabled = false;
    }

    /// <summary>The soundtrack is the clock: re-seek the picture when it drifts or stalls after a hitch.</summary>
    private void KeepInSync()
    {
        if (video.frame != lastFrame)
        {
            lastFrame = video.frame;
            lastFrameTime = Time.unscaledTime;
        }
        if (soundtrack.clip == null) return;
        if (!soundtrack.isPlaying)
        {
            // The soundtrack ran out: the movie is over even if the picture lags behind.
            if (soundtrack.time <= 0f || soundtrack.time >= soundtrack.clip.length - 0.05f) EndPlayback();
            return;
        }
        double drift = video.time - soundtrack.time;
        bool stalled = Time.unscaledTime - lastFrameTime > 0.5f;
        if (Math.Abs(drift) > MaxDriftSeconds || stalled)
        {
            lastFrameTime = Time.unscaledTime;
            video.time = soundtrack.time + 0.05;
        }
    }

    private void UpdateSkip()
    {
        if (!entry.allowSkip)
        {
            skipHint.alpha = 0f;
            return;
        }
        bool held = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
        skipHeld = held ? skipHeld + Time.unscaledDeltaTime : Mathf.MoveTowards(skipHeld, 0f, Time.unscaledDeltaTime * 2f);
        float progress = Mathf.Clamp01(skipHeld / HoldToSkipSeconds);
        skipHint.alpha = Time.unscaledTime - startedAt > 1.5f ? Mathf.Lerp(0.4f, 1f, progress) : 0f;
        skipBar.rectTransform.sizeDelta = new Vector2(SkipBarWidth * progress, 3f);
        if (progress >= 1f) EndPlayback();
    }

    // ---------------- local player / audio ----------------

    private void HoldLocalPlayer()
    {
        GameplayInputGate.TryAcquire(this);
        NetworkManager manager = NetworkManager.Singleton;
        NetworkObject playerObject = manager != null && manager.IsListening && manager.IsClient ? manager.LocalClient?.PlayerObject : null;
        PersonController player = playerObject != null ? playerObject.GetComponent<PersonController>() : null;
        if (player == lockedPlayer) return;
        if (lockedPlayer != null) lockedPlayer.enabled = lockedPlayerWasEnabled;
        lockedPlayer = player;
        if (lockedPlayer == null) return;
        lockedPlayerWasEnabled = lockedPlayer.enabled;
        lockedPlayer.enabled = false;
    }

    /// <summary>The old scene's listener dies with it before the new player spawns; keep the sound audible.</summary>
    private void UpdateFallbackListener()
    {
        bool other = false;
        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (listener != fallbackListener && listener.isActiveAndEnabled) { other = true; break; }
        fallbackListener.enabled = !other;
    }

    private void Finish()
    {
        if (phase == Phase.Done) return;
        phase = Phase.Done;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        phase = Phase.Done;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameplayInputGate.Release(this);
        if (lockedPlayer != null) lockedPlayer.enabled = lockedPlayerWasEnabled;
        if (video != null) video.Stop();
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }
        AudioListener.pause = listenerWasPaused;
        if (active != this) return;
        active = null;
        Finished?.Invoke();
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
        group.blocksRaycasts = true;
        group.alpha = 0f;

        Image black = NewChild<Image>("Black", transform);
        black.color = Color.black;
        black.raycastTarget = true;
        Stretch(black.rectTransform);

        movie = NewChild<RawImage>("Movie", transform);
        movie.raycastTarget = false;
        movie.enabled = false;
        Stretch(movie.rectTransform);
        var fitter = movie.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 16f / 9f;

        var placeholder = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasGroup));
        placeholder.transform.SetParent(transform, false);
        Stretch((RectTransform)placeholder.transform);
        placeholderGroup = placeholder.GetComponent<CanvasGroup>();
        placeholderGroup.alpha = 0f;
        TMP_Text title = Label("Title", placeholder.transform, 120f, TextAlignmentOptions.Center, new Color(0.9f, 0.92f, 0.94f), 30f);
        title.text = entry.placeholderTitle;
        Place(title.rectTransform, new Vector2(0f, 0.48f), new Vector2(1f, 0.66f));
        TMP_Text sub = Label("Subtitle", placeholder.transform, 30f, TextAlignmentOptions.Center, new Color(0.55f, 0.58f, 0.62f), 20f);
        sub.text = entry.placeholderSubtitle;
        Place(sub.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 0.46f));
        Image rule = NewChild<Image>("Rule", placeholder.transform);
        rule.color = new Color(0.85f, 0.12f, 0.12f);
        rule.raycastTarget = false;
        rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(0.5f, 0.47f);
        rule.rectTransform.sizeDelta = new Vector2(480f, 3f);

        status = Label("Loading", transform, 22f, TextAlignmentOptions.BottomLeft, new Color(0.62f, 1f, 0.8f), 10f);
        status.text = "LOADING NEXT FLOOR...";
        status.enabled = false;
        RectTransform statusRect = status.rectTransform;
        statusRect.anchorMin = statusRect.anchorMax = statusRect.pivot = Vector2.zero;
        statusRect.anchoredPosition = new Vector2(56f, 40f);
        statusRect.sizeDelta = new Vector2(800f, 40f);

        skipHint = Label("Skip Hint", transform, 20f, TextAlignmentOptions.BottomRight, new Color(1f, 1f, 1f, 0.85f), 8f);
        skipHint.text = "HOLD SPACE TO SKIP";
        skipHint.alpha = 0f;
        RectTransform hintRect = skipHint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(1f, 0f);
        hintRect.anchoredPosition = new Vector2(-48f, 22f);
        hintRect.sizeDelta = new Vector2(520f, 30f);

        skipBar = NewChild<Image>("Skip Progress", transform);
        skipBar.color = new Color(1f, 1f, 1f, 0.85f);
        skipBar.raycastTarget = false;
        RectTransform barRect = skipBar.rectTransform;
        barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(1f, 0f);
        barRect.anchoredPosition = new Vector2(-48f, 14f);
        barRect.sizeDelta = new Vector2(0f, 3f);
    }

    private static TMP_Text Label(string name, Transform parent, float size, TextAlignmentOptions alignment, Color color, float spacing)
    {
        TMP_Text text = NewChild<TextMeshProUGUI>(name, parent);
        DefragUiTheme theme = DefragUiTheme.Current;
        if (theme != null && theme.font != null) text.font = theme.font;
        text.fontSize = size;
        text.characterSpacing = spacing;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static T NewChild<T>(string childName, Transform parent) where T : Component
    {
        var child = new GameObject(childName, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.AddComponent<T>();
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

    private static float Window(float t, float inStart, float inEnd, float outStart, float outEnd) =>
        Mathf.Clamp01(Mathf.Min(Mathf.InverseLerp(inStart, inEnd, t), 1f - Mathf.InverseLerp(outStart, outEnd, t)));
}
