using System;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

/// <summary>
/// A runtime-built UI canvas rendered into a RenderTexture by its own camera, far away from
/// the playable level. Used for device screens (hacking pad, terminal monitors). Rendering is
/// manual and rate limited; post-processing is disabled so UI colours stay exactly as authored.
/// </summary>
public sealed class OffscreenUiSurface : IDisposable
{
    private const int UiLayer = 5;
    private static int nextSlot;

    private readonly GameObject stage;
    private readonly Camera renderCamera;
    private readonly float interval;
    private float nextRenderAt;

    public RenderTexture Texture { get; }
    public Canvas Canvas { get; }
    public RectTransform Root => (RectTransform)Canvas.transform;

    public OffscreenUiSurface(string name, Vector2Int size, float framesPerSecond)
    {
        interval = 1f / Mathf.Max(0.5f, framesPerSecond);
        Texture = new RenderTexture(Mathf.Max(64, size.x), Mathf.Max(64, size.y), 0, RenderTextureFormat.ARGB32)
        {
            name = name,
            useMipMap = true,
            autoGenerateMips = true
        };
        Texture.Create();

        stage = new GameObject($"{name} (Offscreen UI)") { hideFlags = HideFlags.DontSave };
        stage.transform.position = new Vector3(0f, -10000f - 20f * nextSlot++, 0f);
        UnityEngine.Object.DontDestroyOnLoad(stage);

        renderCamera = new GameObject("Render Camera", typeof(Camera)).GetComponent<Camera>();
        renderCamera.transform.SetParent(stage.transform, false);
        renderCamera.clearFlags = CameraClearFlags.SolidColor;
        renderCamera.backgroundColor = Color.black;
        renderCamera.cullingMask = 1 << UiLayer;
        renderCamera.nearClipPlane = 0.05f;
        renderCamera.farClipPlane = 2f;
        renderCamera.targetTexture = Texture;
        renderCamera.enabled = false;
        ConfigureHdrpCamera(renderCamera);

        var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(stage.transform, false);
        canvasObject.layer = UiLayer;
        Canvas = canvasObject.GetComponent<Canvas>();
        Canvas.renderMode = RenderMode.ScreenSpaceCamera;
        Canvas.worldCamera = renderCamera;
        Canvas.planeDistance = 0.5f;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = size;
        scaler.matchWidthOrHeight = 0.5f;
    }

    /// <summary>Renders when the frame interval has elapsed (or immediately when forced).</summary>
    public bool Tick(bool force = false)
    {
        if (renderCamera == null || (!force && Time.unscaledTime < nextRenderAt)) return false;
        nextRenderAt = Time.unscaledTime + interval;
        SetLayerRecursively(Canvas.transform);
        Canvas.ForceUpdateCanvases();
        renderCamera.Render();
        return true;
    }

    public void Dispose()
    {
        if (stage != null) UnityEngine.Object.Destroy(stage);
        if (Texture != null)
        {
            Texture.Release();
            UnityEngine.Object.Destroy(Texture);
        }
    }

    private static void ConfigureHdrpCamera(Camera camera)
    {
        var data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
        data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        data.backgroundColorHDR = Color.black;
        data.volumeLayerMask = 0;
        data.customRenderingSettings = true;
        data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
        data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);
    }

    private static void SetLayerRecursively(Transform root)
    {
        if (root.gameObject.layer != UiLayer) root.gameObject.layer = UiLayer;
        foreach (Transform child in root) SetLayerRecursively(child);
    }
}
