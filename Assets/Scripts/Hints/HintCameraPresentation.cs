using System.Collections;
using System;
using Unity.Collections;
using Unity.Netcode;
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
    [Tooltip("Material asset that receives the video. Selecting it automatically resolves the renderer material slot.")]
    [SerializeField] private Material videoTargetMaterial;
    [Min(0), Tooltip("Resolved renderer slot. Normally select Video Target Material instead.")]
    [SerializeField] private int videoMaterialIndex;
    [TextArea(3, 6), Tooltip("Read-only reference information refreshed from the assigned VideoPlayer clip.")]
    [SerializeField] private string videoClipInformation;
    [SerializeField] private string videoTextureProperty = "_BaseColorMap";
    [Tooltip("Fits the selected material slot's actual UV bounds to the complete video frame without modifying the source mesh.")]
    [SerializeField] private bool autoFitVideoUvToMaterialSlot;
    [Tooltip("Centered crop applied after UV auto-fit. For example, (0.1, 0.1) removes 10% from every horizontal and vertical edge so the video fills the screen.")]
    [SerializeField] private Vector2 videoCropPerSide;
    [Tooltip("Decodes a standard landscape video, then rotates it on the GPU for monitors whose FBX UVs are rotated.")]
    [SerializeField] private bool rotateVideoCounterClockwiseForMonitor;
    [SerializeField] private Shader videoRotationShader;
    [SerializeField] private bool applyVideoToEmission = true;
    [SerializeField] private string videoEmissionTextureProperty = "_EmissiveColorMap";
    [Min(1f)] [SerializeField] private float videoPrepareTimeout = 20f;
    [Tooltip("Starts this monitor video through the server so every client sees the same broadcast without entering the camera view.")]
    [SerializeField] private bool synchronizeVideoPlayback;
    [SerializeField] private string sharedBroadcastId = "TV";
    [Tooltip("Debug only: prepares and loops the monitor video as soon as Play Mode starts, without interaction.")]
    [SerializeField] private bool debugAutoPlayVideoOnStart;

    [Header("Interactive Desktop")]
    [Tooltip("Canvas containing the monitor desktop, icons and windows.")]
    [SerializeField] private GameObject desktopRoot;

    private PlayerInteraction playerInteraction;
    private StarterAssets.PersonController movement;
    private CameraViewSwitcher viewSwitcher;
    private Camera playerCamera;
    private AudioListener presentationListener;
    private Coroutine sessionRoutine;
    private Coroutine debugVideoRoutine;
    private Coroutine sharedVideoRoutine;
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
    private RenderTexture videoDecodeTexture;
    private Material videoRotationMaterial;
    private long lastRotatedVideoFrame = -2;
    private MaterialPropertyBlock originalVideoProperties;
    private MaterialPropertyBlock videoProperties;
    private bool videoOutputConfigured;
    private bool videoPrepareFailed;
    private string videoPrepareError;
    private bool driveVideoExternalClock;
    private bool externalVideoClockLoops;
    private double videoExternalClock;
    private bool sharedVideoActive;

    public bool IsActive => active;

    private void Awake()
    {
        if (desktopRoot != null) desktopRoot.SetActive(false);
        RefreshVideoInspectorInformation();
        ConfigureVideoOutput();
        if (synchronizeVideoPlayback && !debugAutoPlayVideoOnStart &&
            videoOutputConfigured && videoRenderer != null)
        {
            videoRenderer.SetPropertyBlock(originalVideoProperties, videoMaterialIndex);
        }
    }

    private void Start()
    {
        if (debugAutoPlayVideoOnStart && videoPlayer != null)
            debugVideoRoutine = StartCoroutine(PrepareAndLoopDebugVideo());
    }

    public void Begin(PlayerInteraction player)
    {
        ConfigureVideoOutput();
        if (active || player == null || presentationCamera == null) return;

        if (synchronizeVideoPlayback && videoPlayer != null)
        {
            float duration = videoPlayer.clip != null
                ? (float)videoPlayer.clip.length
                : displayDuration;
            HintConfirmationTracker.Instance?.RequestSharedBroadcast(
                sharedBroadcastId, duration, this);
        }

        if (!GameplayInputGate.TryAcquire(this)) return;

        playerInteraction = player;
        playerCamera = player.GetComponent<Camera>();
        if (playerCamera == null)
            playerCamera = player.GetComponentInChildren<Camera>(true);
        if (playerCamera == null)
            playerCamera = player.GetComponentInParent<Camera>();
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
            if (synchronizeVideoPlayback)
            {
                float requestWait = 0f;
                while (active && sharedVideoRoutine == null && requestWait < 2f)
                {
                    requestWait += Time.unscaledDeltaTime;
                    yield return null;
                }

                while (active && sharedVideoActive)
                    yield return null;

                sessionRoutine = null;
                if (active) End();
                yield break;
            }

            if (debugAutoPlayVideoOnStart)
            {
                if (!videoPlayer.isPlaying && debugVideoRoutine == null)
                    debugVideoRoutine = StartCoroutine(PrepareAndLoopDebugVideo());

                sessionRoutine = null;
                yield break;
            }

            yield return PrepareAndPlayVideo();
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

    private IEnumerator PrepareAndPlayVideo()
    {
        videoPlayer.loopPointReached -= HandleVideoFinished;
        videoPlayer.loopPointReached += HandleVideoFinished;
        videoPlayer.errorReceived -= HandleVideoError;
        videoPlayer.errorReceived += HandleVideoError;
        videoPlayer.isLooping = false;
        videoPlayer.Stop();
        videoPrepareFailed = false;
        videoPrepareError = string.Empty;
        videoPlayer.Prepare();

        float elapsed = 0f;
        while (!videoPlayer.isPrepared && !videoPrepareFailed && elapsed < videoPrepareTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (videoPrepareFailed || !videoPlayer.isPrepared)
        {
            string clipName = videoPlayer.clip != null ? videoPlayer.clip.name : videoPlayer.url;
            string reason = videoPrepareFailed
                ? videoPrepareError
                : $"prepare timed out after {videoPrepareTimeout:0.0} seconds";
            Debug.LogError(
                $"[HintCameraPresentation] Could not prepare video '{clipName}': {reason}",
                this);
            sessionRoutine = null;
            End();
            yield break;
        }

        videoPlayer.Play();
    }

    private IEnumerator PrepareAndLoopDebugVideo()
    {
        ConfigureVideoOutput();
        if (videoPlayer == null || !videoOutputConfigured)
        {
            Debug.LogError(
                "[HintCameraPresentation] Debug autoplay could not start because the video output is not configured.",
                this);
            debugVideoRoutine = null;
            yield break;
        }

        videoPlayer.loopPointReached -= HandleVideoFinished;
        videoPlayer.errorReceived -= HandleVideoError;
        videoPlayer.errorReceived += HandleVideoError;
        videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        videoPlayer.timeReference = VideoTimeReference.InternalTime;
        videoPlayer.skipOnDrop = true;
        videoPlayer.isLooping = true;
        videoPlayer.Stop();
        driveVideoExternalClock = false;
        externalVideoClockLoops = true;
        videoExternalClock = 0d;
        videoPrepareFailed = false;
        videoPrepareError = string.Empty;

        string clipName = videoPlayer.clip != null ? videoPlayer.clip.name : videoPlayer.url;
        Debug.Log($"[HintCameraPresentation] Debug autoplay preparing '{clipName}'.", this);
        videoPlayer.Prepare();

        float elapsed = 0f;
        while (!videoPlayer.isPrepared && !videoPrepareFailed && elapsed < videoPrepareTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (videoPrepareFailed || !videoPlayer.isPrepared)
        {
            string reason = videoPrepareFailed
                ? videoPrepareError
                : $"prepare timed out after {videoPrepareTimeout:0.0} seconds";
            Debug.LogError(
                $"[HintCameraPresentation] Debug autoplay failed for '{clipName}': {reason}",
                this);
            debugVideoRoutine = null;
            yield break;
        }

        driveVideoExternalClock = true;
        videoPlayer.Play();
        Debug.Log(
            $"[HintCameraPresentation] Debug autoplay started '{clipName}' and will loop continuously.",
            this);

        long previousFrame = videoPlayer.frame;
        double previousTime = videoPlayer.time;
        int consecutiveStallChecks = 0;
        while (debugAutoPlayVideoOnStart && videoPlayer != null)
        {
            yield return new WaitForSecondsRealtime(1f);

            long currentFrame = videoPlayer.frame;
            double currentTime = videoPlayer.time;
            bool playbackAdvanced = currentFrame != previousFrame ||
                                    Math.Abs(currentTime - previousTime) > 0.01d;

            if (currentFrame < 0 || !playbackAdvanced)
            {
                consecutiveStallChecks++;
            }
            else
            {
                consecutiveStallChecks = 0;
            }

            if (consecutiveStallChecks >= 3)
            {
                Debug.LogWarning(
                    $"[HintCameraPresentation] Debug autoplay stall detected at " +
                    $"time={currentTime:0.000}, frame={currentFrame}, expectedClock={videoExternalClock:0.000}, " +
                    $"isPlaying={videoPlayer.isPlaying}. Seeking to the expected playback time.",
                    this);
                if (videoPlayer.canSetTime)
                    videoPlayer.time = videoExternalClock;
                if (!videoPlayer.isPlaying)
                    videoPlayer.Play();
                lastRotatedVideoFrame = -2;
                consecutiveStallChecks = 0;
            }

            previousFrame = videoPlayer.frame;
            previousTime = videoPlayer.time;
        }

        debugVideoRoutine = null;
    }

    public void PlaySharedNetworkBroadcast(double serverStartTime, float duration)
    {
        if (!synchronizeVideoPlayback || videoPlayer == null || duration <= 0f)
            return;

        if (debugVideoRoutine != null)
        {
            StopCoroutine(debugVideoRoutine);
            debugVideoRoutine = null;
            driveVideoExternalClock = false;
        }

        if (sharedVideoRoutine != null)
            StopCoroutine(sharedVideoRoutine);
        sharedVideoRoutine = StartCoroutine(
            PlaySharedNetworkBroadcastRoutine(serverStartTime, duration));
    }

    private IEnumerator PlaySharedNetworkBroadcastRoutine(
        double serverStartTime,
        float duration)
    {
        ConfigureVideoOutput();
        if (!videoOutputConfigured) yield break;

        videoRenderer.SetPropertyBlock(videoProperties, videoMaterialIndex);
        videoPlayer.errorReceived -= HandleVideoError;
        videoPlayer.errorReceived += HandleVideoError;
        videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        videoPlayer.timeReference = VideoTimeReference.InternalTime;
        videoPlayer.skipOnDrop = true;
        videoPlayer.isLooping = false;
        videoPlayer.Stop();
        videoPrepareFailed = false;
        videoPrepareError = string.Empty;
        sharedVideoActive = true;
        driveVideoExternalClock = false;
        videoPlayer.Prepare();

        float prepareElapsed = 0f;
        while (!videoPlayer.isPrepared && !videoPrepareFailed &&
               prepareElapsed < videoPrepareTimeout)
        {
            prepareElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!videoPlayer.isPrepared || videoPrepareFailed)
        {
            Debug.LogError(
                $"[HintCameraPresentation] Shared broadcast prepare failed: {videoPrepareError}",
                this);
            sharedVideoActive = false;
            sharedVideoRoutine = null;
            videoRenderer.SetPropertyBlock(originalVideoProperties, videoMaterialIndex);
            yield break;
        }

        double now = GetSynchronizedServerTime();
        videoExternalClock = Math.Max(0d, now - serverStartTime);
        if (videoExternalClock >= duration)
        {
            sharedVideoActive = false;
            sharedVideoRoutine = null;
            videoRenderer.SetPropertyBlock(originalVideoProperties, videoMaterialIndex);
            yield break;
        }

        externalVideoClockLoops = false;
        if (videoPlayer.canSetTime)
            videoPlayer.time = videoExternalClock;
        lastRotatedVideoFrame = -2;
        driveVideoExternalClock = true;
        videoPlayer.Play();

        while (sharedVideoActive && videoExternalClock < duration)
            yield return null;

        driveVideoExternalClock = false;
        videoPlayer.Stop();
        videoRenderer.SetPropertyBlock(originalVideoProperties, videoMaterialIndex);
        sharedVideoActive = false;
        sharedVideoRoutine = null;
    }

    private static double GetSynchronizedServerTime()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening
            ? networkManager.ServerTime.Time
            : Time.unscaledTimeAsDouble;
    }

    private void HandleVideoError(VideoPlayer source, string message)
    {
        videoPrepareFailed = true;
        videoPrepareError = message;
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
        if (videoTargetMaterial != null)
        {
            int selectedIndex = Array.IndexOf(materials, videoTargetMaterial);
            if (selectedIndex >= 0) videoMaterialIndex = selectedIndex;
        }
        if (videoMaterialIndex < 0 || videoMaterialIndex >= materials.Length)
        {
            Debug.LogWarning(
                $"[HintCameraPresentation] Video material index {videoMaterialIndex} is outside the renderer's {materials.Length} material slots.",
                this);
            return;
        }
        videoTargetMaterial = materials[videoMaterialIndex];

        VideoClip assignedClip = videoPlayer.clip;
        int decodeWidth = assignedClip != null && assignedClip.width > 0 ? (int)assignedClip.width : 1280;
        int decodeHeight = assignedClip != null && assignedClip.height > 0 ? (int)assignedClip.height : 720;
        bool rotateOutput = rotateVideoCounterClockwiseForMonitor && videoRotationShader != null;
        int outputWidth = rotateOutput ? decodeHeight : decodeWidth;
        int outputHeight = rotateOutput ? decodeWidth : decodeHeight;
        videoRenderTexture = new RenderTexture(outputWidth, outputHeight, 0, RenderTextureFormat.ARGB32)
        {
            name = $"{name}_VideoOutput",
            useMipMap = false,
            autoGenerateMips = false
        };
        videoRenderTexture.Create();

        if (rotateVideoCounterClockwiseForMonitor && videoRotationShader == null)
        {
            Debug.LogWarning(
                "[HintCameraPresentation] Video rotation is enabled but no rotation shader is assigned.",
                this);
        }

        if (rotateOutput)
        {
            videoDecodeTexture = new RenderTexture(
                decodeWidth,
                decodeHeight,
                0,
                RenderTextureFormat.ARGB32)
            {
                name = $"{name}_VideoDecode",
                useMipMap = false,
                autoGenerateMips = false
            };
            videoDecodeTexture.Create();
            videoRotationMaterial = new Material(videoRotationShader)
            {
                name = $"{name}_VideoRotation"
            };
        }

        originalVideoProperties = new MaterialPropertyBlock();
        videoRenderer.GetPropertyBlock(originalVideoProperties, videoMaterialIndex);
        videoProperties = new MaterialPropertyBlock();
        videoRenderer.GetPropertyBlock(videoProperties, videoMaterialIndex);
        videoProperties.SetTexture(videoTextureProperty, videoRenderTexture);
        Vector4 videoUvTransform = new Vector4(1f, 1f, 0f, 0f);
        if (autoFitVideoUvToMaterialSlot)
            TryCalculateVideoUvTransform(out videoUvTransform);
        ApplyCenteredVideoCrop(ref videoUvTransform);
        videoProperties.SetVector($"{videoTextureProperty}_ST", videoUvTransform);
        // HDRP/Lit multiplies the Base Map by _BaseColor. A black monitor tint
        // would therefore multiply every video pixel to black.
        videoProperties.SetColor("_BaseColor", Color.white);
        if (applyVideoToEmission && !string.IsNullOrWhiteSpace(videoEmissionTextureProperty))
        {
            videoProperties.SetTexture(videoEmissionTextureProperty, videoRenderTexture);
            videoProperties.SetColor("_EmissiveColor", Color.white);
        }
        else
        {
            videoProperties.SetColor("_EmissiveColor", Color.black);
        }
        videoRenderer.SetPropertyBlock(videoProperties, videoMaterialIndex);

        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = rotateOutput ? videoDecodeTexture : videoRenderTexture;
        videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        videoPlayer.skipOnDrop = true;
        videoPlayer.waitForFirstFrame = false;
        videoOutputConfigured = true;
    }

    private void ApplyCenteredVideoCrop(ref Vector4 uvTransform)
    {
        float cropX = Mathf.Clamp(videoCropPerSide.x, 0f, 0.45f);
        float cropY = Mathf.Clamp(videoCropPerSide.y, 0f, 0.45f);
        float visibleWidth = 1f - cropX * 2f;
        float visibleHeight = 1f - cropY * 2f;

        uvTransform.z = uvTransform.z * visibleWidth + cropX;
        uvTransform.w = uvTransform.w * visibleHeight + cropY;
        uvTransform.x *= visibleWidth;
        uvTransform.y *= visibleHeight;

        if (cropX > 0f || cropY > 0f)
        {
            Debug.Log(
                $"[HintCameraPresentation] Centered video crop applied: " +
                $"perSide=({cropX:0.###}, {cropY:0.###}), finalST={uvTransform}.",
                this);
        }
    }

    private bool TryCalculateVideoUvTransform(out Vector4 uvTransform)
    {
        uvTransform = new Vector4(1f, 1f, 0f, 0f);
        MeshFilter meshFilter = videoRenderer != null
            ? videoRenderer.GetComponent<MeshFilter>()
            : null;
        Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (mesh == null || videoMaterialIndex < 0 || videoMaterialIndex >= mesh.subMeshCount)
        {
            Debug.LogWarning(
                "[HintCameraPresentation] Video UV auto-fit could not find the selected material submesh.",
                this);
            return false;
        }

        try
        {
            using Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            Mesh.MeshData meshData = meshDataArray[0];
            if (!meshData.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))
            {
                Debug.LogWarning(
                    "[HintCameraPresentation] Video UV auto-fit found no UV0 data.",
                    this);
                return false;
            }

            using NativeArray<Vector2> uvs = new NativeArray<Vector2>(
                meshData.vertexCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            meshData.GetUVs(0, uvs);

            UnityEngine.Rendering.SubMeshDescriptor subMesh =
                meshData.GetSubMesh(videoMaterialIndex);
            using NativeArray<int> indices = new NativeArray<int>(
                subMesh.indexCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            meshData.GetIndices(indices, videoMaterialIndex, true);

            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < indices.Length; i++)
            {
                int vertexIndex = indices[i];
                if (vertexIndex < 0 || vertexIndex >= uvs.Length) continue;
                Vector2 uv = uvs[vertexIndex];
                minimum = Vector2.Min(minimum, uv);
                maximum = Vector2.Max(maximum, uv);
            }

            Vector2 size = maximum - minimum;
            if (size.x <= Mathf.Epsilon || size.y <= Mathf.Epsilon ||
                !float.IsFinite(size.x) || !float.IsFinite(size.y))
            {
                Debug.LogWarning(
                    "[HintCameraPresentation] Video UV auto-fit found invalid UV bounds.",
                    this);
                return false;
            }

            Vector2 scale = new Vector2(1f / size.x, 1f / size.y);
            Vector2 offset = new Vector2(-minimum.x * scale.x, -minimum.y * scale.y);
            uvTransform = new Vector4(scale.x, scale.y, offset.x, offset.y);
            Debug.Log(
                $"[HintCameraPresentation] Video UV auto-fit: " +
                $"min={minimum}, max={maximum}, ST={uvTransform}.",
                this);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[HintCameraPresentation] Video UV auto-fit failed: {exception.Message}",
                this);
            return false;
        }
    }

    private void OnValidate()
    {
        videoOutputConfigured = false;
        RefreshVideoInspectorInformation();
        ResolveVideoMaterialSelection();
    }

    private void ResolveVideoMaterialSelection()
    {
        if (videoRenderer == null || videoTargetMaterial == null) return;
        Material[] materials = videoRenderer.sharedMaterials;
        int selectedIndex = Array.IndexOf(materials, videoTargetMaterial);
        if (selectedIndex >= 0) videoMaterialIndex = selectedIndex;
    }

    private void RefreshVideoInspectorInformation()
    {
        VideoClip clip = videoPlayer != null ? videoPlayer.clip : null;
        if (clip == null)
        {
            videoClipInformation = "No VideoClip assigned.";
            return;
        }

        videoClipInformation =
            $"Clip: {clip.name}\n" +
            $"Resolution: {clip.width} x {clip.height}\n" +
            $"Frame Rate: {clip.frameRate:0.###} fps\n" +
            $"Duration: {clip.length:0.###} sec";
    }

    private void Update()
    {
        DriveDebugVideoClock();
        RefreshRotatedVideoOutput();

        if (!active || returning || !Input.GetKeyDown(KeyCode.Escape)) return;

        bool canExit = presentationType == PresentationType.InteractiveDesktop ||
                       allowEscapeToSkip;
        if (!canExit) return;

        GameplayInputGate.ConsumeEscape(this);
        End();
    }

    private void LateUpdate()
    {
        // Some player/settings components also manage the cursor.  The desktop
        // owns local input for the whole interactive session, so assert its
        // cursor state after those components have updated.  This component is
        // only activated by the interacting local player's Begin call.
        if (!active || returning || presentationType != PresentationType.InteractiveDesktop)
            return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void DriveDebugVideoClock()
    {
        if (!driveVideoExternalClock || videoPlayer == null || !videoPlayer.isPrepared)
            return;

        videoExternalClock += Time.unscaledDeltaTime;
        double duration = videoPlayer.clip != null ? videoPlayer.clip.length : 0d;
        if (externalVideoClockLoops && duration > 0d && videoExternalClock >= duration)
            videoExternalClock %= duration;

    }

    private void RefreshRotatedVideoOutput()
    {
        if (videoPlayer == null || videoDecodeTexture == null ||
            videoRenderTexture == null || videoRotationMaterial == null)
            return;

        long currentFrame = videoPlayer.frame;
        if (currentFrame < 0 || currentFrame == lastRotatedVideoFrame) return;

        Graphics.Blit(videoDecodeTexture, videoRenderTexture, videoRotationMaterial);
        lastRotatedVideoFrame = currentFrame;
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
        if (videoPlayer != null && !debugAutoPlayVideoOnStart && !sharedVideoActive)
        {
            videoPlayer.loopPointReached -= HandleVideoFinished;
            videoPlayer.errorReceived -= HandleVideoError;
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
        driveVideoExternalClock = false;

        if (debugVideoRoutine != null)
        {
            StopCoroutine(debugVideoRoutine);
            debugVideoRoutine = null;
        }

        if (sharedVideoRoutine != null)
        {
            StopCoroutine(sharedVideoRoutine);
            sharedVideoRoutine = null;
        }

        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= HandleVideoFinished;
            videoPlayer.errorReceived -= HandleVideoError;
            if (videoPlayer.targetTexture == videoRenderTexture ||
                videoPlayer.targetTexture == videoDecodeTexture)
                videoPlayer.targetTexture = null;
        }

        if (videoOutputConfigured && videoRenderer != null)
            videoRenderer.SetPropertyBlock(originalVideoProperties, videoMaterialIndex);

        if (videoRenderTexture != null)
        {
            videoRenderTexture.Release();
            Destroy(videoRenderTexture);
        }

        if (videoDecodeTexture != null)
        {
            videoDecodeTexture.Release();
            Destroy(videoDecodeTexture);
        }

        if (videoRotationMaterial != null)
            Destroy(videoRotationMaterial);
    }
}
