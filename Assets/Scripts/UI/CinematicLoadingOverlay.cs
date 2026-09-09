using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

public sealed class CinematicLoadingOverlay : MonoBehaviour
{
    private static CinematicLoadingOverlay active;
    private VideoPlayer video;
    private RenderTexture texture;
    private string destination;
    private bool videoFinished;
    private bool sceneReady;
    private TMP_Text status;
    private float prepareDeadline;
    private float playbackDeadline;
    private StarterAssets.PersonController lockedPlayer;
    private bool previousEnabled;

    public static void Begin(VideoClip clip, string scene)
    {
        if (active != null) return;
        GameObject root = new("Cinematic loading", typeof(CinematicLoadingOverlay));
        DontDestroyOnLoad(root);
        active = root.GetComponent<CinematicLoadingOverlay>();
        active.Initialize(clip, scene);
    }

    public static void Cancel() { if (active != null) Destroy(active.gameObject); }

    private void Initialize(VideoClip clip, string scene)
    {
        destination = scene;
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameObject screen = new("Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(Image));
        screen.transform.SetParent(transform, false);
        Canvas canvas = screen.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        screen.GetComponent<Image>().color = Color.black;
        GameObject display = new("Video", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        display.transform.SetParent(screen.transform, false);
        RectTransform rect = (RectTransform)display.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        display.GetComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        display.GetComponent<AspectRatioFitter>().aspectRatio = clip.height > 0 ? (float)clip.width / clip.height : 16f / 9f;
        float aspect = display.GetComponent<AspectRatioFitter>().aspectRatio;
        texture = new RenderTexture(1920, Mathf.Max(1, Mathf.RoundToInt(1920f / aspect)), 0);
        texture.Create();
        display.GetComponent<RawImage>().texture = texture;
        video = gameObject.AddComponent<VideoPlayer>();
        video.playOnAwake = false;
        video.source = VideoSource.VideoClip;
        video.clip = clip;
        video.renderMode = VideoRenderMode.RenderTexture;
        video.targetTexture = texture;
        video.audioOutputMode = VideoAudioOutputMode.Direct;
        video.isLooping = false;
        video.prepareCompleted += OnPrepared;
        video.loopPointReached += OnFinished;
        video.errorReceived += OnError;
        GameObject label = new("Loading status", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(screen.transform, false);
        status = label.GetComponent<TMP_Text>();
        status.fontSize = 24; status.alignment = TextAlignmentOptions.Center;
        status.text = "PREPARING TRANSMISSION...";
        status.rectTransform.anchorMin = new Vector2(0.1f, 0.02f);
        status.rectTransform.anchorMax = new Vector2(0.9f, 0.12f);
        status.rectTransform.offsetMin = status.rectTransform.offsetMax = Vector2.zero;
        prepareDeadline = Time.realtimeSinceStartup + 20f;
        playbackDeadline = Time.realtimeSinceStartup + (float)clip.length + 50f;
        video.Prepare();
    }

    private void OnPrepared(VideoPlayer player) { status.text = ""; player.Play(); }
    private void OnFinished(VideoPlayer player) { videoFinished = true; }
    private void OnError(VideoPlayer player, string error)
    {
        Debug.LogWarning($"Cinematic playback failed: {error}");
        videoFinished = true;
    }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { if (scene.name == destination) sceneReady = true; }
    private void Update()
    {
        GameplayInputGate.TryAcquire(this);
        var manager = Unity.Netcode.NetworkManager.Singleton;
        var playerObject = manager != null && manager.IsClient ? manager.LocalClient?.PlayerObject : null;
        var player = playerObject != null ? playerObject.GetComponent<StarterAssets.PersonController>() : null;
        if (player != lockedPlayer)
        {
            if (lockedPlayer != null) lockedPlayer.enabled = previousEnabled;
            lockedPlayer = player;
            if (lockedPlayer != null) { previousEnabled = lockedPlayer.enabled; lockedPlayer.enabled = false; }
        }
        if (!video.isPrepared && Time.realtimeSinceStartup > prepareDeadline) videoFinished = true;
        if (Time.realtimeSinceStartup > playbackDeadline) videoFinished = true;
        if (!videoFinished) return;
        if (sceneReady) Destroy(gameObject);
        else status.text = "LOADING NEXT FLOOR...";
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameplayInputGate.Release(this);
        if (lockedPlayer != null) lockedPlayer.enabled = previousEnabled;
        if (video != null) video.Stop();
        if (texture != null) { texture.Release(); Destroy(texture); }
        if (active == this) active = null;
    }
}
