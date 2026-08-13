using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Video;

[DisallowMultipleComponent]
public sealed class HintCameraPresentation : MonoBehaviour
{
    public enum PresentationType
    {
        Timed,
        InteractiveDesktop
    }

    [Header("Mode")]
    [SerializeField] private PresentationType presentationType;

    [Header("Camera")]
    [SerializeField] private Camera presentationCamera;
    [Min(0f)] [SerializeField] private float blendDuration = 0.65f;
    [SerializeField] private AnimationCurve blendCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Timed Presentation")]
    [Min(0f)] [SerializeField] private float displayDuration = 3f;
    [SerializeField] private bool allowEscapeToSkip = true;

    [Header("Optional Video")]
    [Tooltip("When assigned in Timed mode, the video plays once after the camera blend and ends the presentation automatically.")]
    [SerializeField] private VideoPlayer videoPlayer;
    [Tooltip("Renderer containing the monitor material. When empty, the VideoPlayer target renderer or this object's Renderer is used.")]
    [SerializeField] private Renderer videoRenderer;
    [Min(0)] [SerializeField] private int videoMaterialIndex;
    [SerializeField] private string videoTextureProperty = "_BaseColorMap";
    [SerializeField] private bool applyVideoToEmission = true;
    [SerializeField] private string videoEmissionTextureProperty = "_EmissiveColorMap";

    [Header("Interactive Desktop")]
    [Tooltip("Canvas containing the monitor desktop, icons and windows.")]
    [SerializeField] private GameObject desktopRoot;

    private PlayerInteraction playerInteraction;
    private StarterAssets.PersonController movement;
    private CameraViewSwitcher viewSwitcher;
    private Camera playerCamera;
    private AudioListener presentationListener;
    private Coroutine sessionRoutine;
    private Vector3 presentationRestPosition;
    private Quaternion presentationRestRotation;
    private bool originalPresentationObjectActive;
    private bool originalPresentationCameraEnabled;
    private bool originalPresentationListenerEnabled;
    private bool originalCursorVisible;
    private CursorLockMode originalCursorLockMode;
    private bool active;
    private bool returning;
    private RenderTexture videoRenderTexture;
    private MaterialPropertyBlock originalVideoProperties;
    private MaterialPropertyBlock videoProperties;
    private bool videoOutputConfigured;

    public bool IsActive => active;

    private void Awake()
    {
        if (desktopRoot != null) desktopRoot.SetActive(false);
        ConfigureVideoOutput();
    }

    public void Begin(PlayerInteraction player)
    {
        ConfigureVideoOutput();
        if (active || player == null || presentationCamera == null) return;
        if (!GameplayInputGate.TryAcquire(this)) return;

        playerInteraction = player;
        playerCamera = player.GetComponent<Camera>();
        movement = player.GetComponentInParent<StarterAssets.PersonController>(true);
        viewSwitcher = player.GetComponentInParent<CameraViewSwitcher>(true);
        if (playerCamera == null)
        {
            GameplayInputGate.Release(this);
            ClearReferences();
            return;
        }

        presentationListener = presentationCamera.GetComponent<AudioListener>();
        presentationRestPosition = presentationCamera.transform.position;
        presentationRestRotation = presentationCamera.transform.rotation;
        originalPresentationObjectActive = presentationCamera.gameObject.activeSelf;
        originalPresentationCameraEnabled = presentationCamera.enabled;
        originalPresentationListenerEnabled =
            presentationListener != null && presentationListener.enabled;
        originalCursorVisible = Cursor.visible;
        originalCursorLockMode = Cursor.lockState;

        CopyCameraRenderingSettings(playerCamera, presentationCamera);
        player.CloseAllUI();
        player.TogglePlayerControl(false);
        if (movement != null) movement.enabled = false;
        viewSwitcher?.SetInteractionLocked(true);

        presentationCamera.gameObject.SetActive(true);
        presentationCamera.transform.SetPositionAndRotation(
            playerCamera.transform.position, playerCamera.transform.rotation);
        presentationCamera.enabled = true;
        if (presentationListener != null) presentationListener.enabled = false;
        playerCamera.enabled = false;

        active = true;
        returning = false;
        sessionRoutine = StartCoroutine(EnterRoutine());
    }

    private IEnumerator EnterRoutine()
    {
        yield return BlendTo(
            presentationRestPosition,
            presentationRestRotation,
            blendDuration);

        if (!active) yield break;

        if (presentationType == PresentationType.InteractiveDesktop)
        {
            if (desktopRoot != null) desktopRoot.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            sessionRoutine = null;
            yield break;
        }

        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= HandleVideoFinished;
            videoPlayer.loopPointReached += HandleVideoFinished;
            videoPlayer.isLooping = false;
            videoPlayer.Stop();
            videoPlayer.time = 0d;
            videoPlayer.Play();
            sessionRoutine = null;
            yield break;
        }

        yield return new WaitForSecondsRealtime(displayDuration);
        if (active) End();
    }

    private void HandleVideoFinished(VideoPlayer source)
    {
        if (active && !returning)
            End();
    }

    private void ConfigureVideoOutput()
    {
        if (videoOutputConfigured) return;

        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();
        if (videoPlayer == null) return;

        if (videoRenderer == null)
            videoRenderer = videoPlayer.targetMaterialRenderer;
        if (videoRenderer == null)
            videoRenderer = GetComponent<Renderer>();
        if (videoRenderer == null)
        {
            Debug.LogWarning("[HintCameraPresentation] Video renderer is not assigned.", this);
            return;
        }

        Material[] materials = videoRenderer.sharedMaterials;
        if (videoMaterialIndex < 0 || videoMaterialIndex >= materials.Length)
        {
            Debug.LogWarning(
                $"[HintCameraPresentation] Video material index {videoMaterialIndex} is outside the renderer's {materials.Length} material slots.",
                this);
            return;
        }

        videoRenderTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32)
        {
            name = $"{name}_VideoOutput",
            useMipMap = false,
            autoGenerateMips = false
        };
        videoRenderTexture.Create();

        originalVideoProperties = new MaterialPropertyBlock();
        videoRenderer.GetPropertyBlock(originalVideoProperties, videoMaterialIndex);
        videoProperties = new MaterialPropertyBlock();
        videoRenderer.GetPropertyBlock(videoProperties, videoMaterialIndex);
        videoProperties.SetTexture(videoTextureProperty, videoRenderTexture);
        if (applyVideoToEmission && !string.IsNullOrWhiteSpace(videoEmissionTextureProperty))
        {
            videoProperties.SetTexture(videoEmissionTextureProperty, videoRenderTexture);
            videoProperties.SetColor("_EmissiveColor", Color.white);
        }
        videoRenderer.SetPropertyBlock(videoProperties, videoMaterialIndex);

        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = videoRenderTexture;
        videoOutputConfigured = true;
    }

    private void Update()
    {
        if (!active || returning || !Input.GetKeyDown(KeyCode.Escape)) return;

        bool canExit = presentationType == PresentationType.InteractiveDesktop ||
                       allowEscapeToSkip;
        if (!canExit) return;

        GameplayInputGate.ConsumeEscape(this);
        End();
    }

    public void End()
    {
        if (!active || returning) return;
        returning = true;
        if (sessionRoutine != null) StopCoroutine(sessionRoutine);
        sessionRoutine = StartCoroutine(ExitRoutine());
    }

    private IEnumerator ExitRoutine()
    {
        if (desktopRoot != null) desktopRoot.SetActive(false);
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        yield return BlendTo(
            playerCamera.transform.position,
            playerCamera.transform.rotation,
            blendDuration);

        RestoreImmediately();
    }

    private IEnumerator BlendTo(Vector3 endPosition, Quaternion endRotation, float duration)
    {
        Vector3 startPosition = presentationCamera.transform.position;
        Quaternion startRotation = presentationCamera.transform.rotation;
        if (duration <= 0f)
        {
            presentationCamera.transform.SetPositionAndRotation(endPosition, endRotation);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration && active)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float t = blendCurve != null ? blendCurve.Evaluate(normalized) : normalized;
            presentationCamera.transform.position =
                Vector3.LerpUnclamped(startPosition, endPosition, t);
            presentationCamera.transform.rotation =
                Quaternion.SlerpUnclamped(startRotation, endRotation, t);
            yield return null;
        }

        if (active)
            presentationCamera.transform.SetPositionAndRotation(endPosition, endRotation);
    }

    private void RestoreImmediately()
    {
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= HandleVideoFinished;
            videoPlayer.Stop();
            videoPlayer.time = 0d;
        }

        if (playerCamera != null) playerCamera.enabled = true;
        if (presentationCamera != null)
        {
            presentationCamera.transform.SetPositionAndRotation(
                presentationRestPosition, presentationRestRotation);
            presentationCamera.enabled = originalPresentationCameraEnabled;
            if (presentationListener != null)
                presentationListener.enabled = originalPresentationListenerEnabled;
            presentationCamera.gameObject.SetActive(originalPresentationObjectActive);
        }

        if (desktopRoot != null) desktopRoot.SetActive(false);
        Cursor.visible = originalCursorVisible;
        Cursor.lockState = originalCursorLockMode;
        if (movement != null) movement.enabled = true;
        playerInteraction?.TogglePlayerControl(true);
        viewSwitcher?.SetInteractionLocked(false);
        GameplayInputGate.Release(this);

        active = false;
        returning = false;
        sessionRoutine = null;
        ClearReferences();
    }

    private static void CopyCameraRenderingSettings(Camera source, Camera target)
    {
        target.allowHDR = source.allowHDR;
        target.allowMSAA = source.allowMSAA;

        HDAdditionalCameraData sourceData = source.GetComponent<HDAdditionalCameraData>();
        HDAdditionalCameraData targetData = target.GetComponent<HDAdditionalCameraData>();
        if (sourceData == null || targetData == null) return;

        targetData.volumeLayerMask = sourceData.volumeLayerMask;
        targetData.antialiasing = sourceData.antialiasing;
        targetData.SMAAQuality = sourceData.SMAAQuality;
        targetData.dithering = sourceData.dithering;
        targetData.stopNaNs = sourceData.stopNaNs;
    }

    private void ClearReferences()
    {
        playerInteraction = null;
        movement = null;
        viewSwitcher = null;
        playerCamera = null;
        presentationListener = null;
    }

    private void OnDisable()
    {
        if (active) RestoreImmediately();
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= HandleVideoFinished;
            if (videoPlayer.targetTexture == videoRenderTexture)
                videoPlayer.targetTexture = null;
        }

        if (videoOutputConfigured && videoRenderer != null)
            videoRenderer.SetPropertyBlock(originalVideoProperties, videoMaterialIndex);

        if (videoRenderTexture != null)
        {
            videoRenderTexture.Release();
            Destroy(videoRenderTexture);
        }
    }
}
