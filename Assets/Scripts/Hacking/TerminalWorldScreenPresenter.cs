using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum TerminalWorldPhase : byte
{
    Idle,
    Menu,
    Running,
    Success,
    Failure
}

/// <summary>Draws replicated terminal activity into the monitor material on each client.</summary>
[DisallowMultipleComponent]
public sealed class TerminalWorldScreenPresenter : MonoBehaviour
{
    private static readonly int BaseMap = Shader.PropertyToID("_BaseColorMap");
    private static readonly int MainTex = Shader.PropertyToID("_MainTex");
    private static readonly int EmissiveMap = Shader.PropertyToID("_EmissiveColorMap");

    [SerializeField] private Renderer screenRenderer;
    [SerializeField, Min(0)] private int materialElement = 3;
    [SerializeField] private Vector2Int resolution = new(1024, 576);
    [Tooltip("화면 유리 메시의 UV 방향 보정(도). -1이면 자동: 키오스크 유리(ConnectionDevice_glass)는 270.")]
    [SerializeField] private int contentRotation = -1;
    private RenderTexture texture;
    private RectTransform statusView;
    private RectTransform mirrorView;
    private RawImage mirrorImage;
    private TMP_Text mirrorTag;
    private Material runtimeScreenMaterial;
    private Material originalScreenMaterial;
    private TMP_Text header;
    private TMP_Text body;
    private TMP_Text footer;
    private Camera displayCamera;
    private float returnToIdleAt;
    private string terminalName;

    public void Initialize(string displayName)
    {
        terminalName = displayName;
        FindScreenRenderer();
        if (screenRenderer == null) return;
        BuildDisplay();
        SetState(TerminalWorldPhase.Idle, TerminalCommands.None);
    }

    public void SetState(TerminalWorldPhase phase, TerminalCommands command)
    {
        if (body == null) return;
        string label = command == TerminalCommands.None
            ? ""
            : TerminalCommandLabel.Get(command);
        header.text = $"DEFRAG // {terminalName}";
        footer.text = "SECURE LOCAL LINK  •  STANDBY CHANNEL";
        body.color = phase == TerminalWorldPhase.Failure
            ? new Color(1f, 0.22f, 0.12f)
            : new Color(0.45f, 1f, 0.82f);
        body.text = phase switch
        {
            TerminalWorldPhase.Menu => "OPERATOR CONNECTED\n\nSELECTING COMMAND...",
            TerminalWorldPhase.Running => $"COMMAND ACTIVE\n\n> {label}\n\nPROCESSING SECURE REQUEST",
            TerminalWorldPhase.Success => $"{label}\n\nTRANSFER COMPLETE",
            TerminalWorldPhase.Failure => $"{label}\n\nACCESS DENIED // RETRY REQUIRED",
            _ => "SYSTEM READY\n\nAWAITING HACKING PAD"
        };
        returnToIdleAt = phase is TerminalWorldPhase.Success or TerminalWorldPhase.Failure
            ? Time.unscaledTime + 2.5f
            : 0f;
        RenderOnce();
    }

    private int startupRenders = 3;

    private void Update()
    {
        // The first render happens in Awake, before HDRP has drawn a frame; repeat it briefly.
        if (startupRenders > 0 && !showingMirror)
        {
            startupRenders--;
            RenderOnce();
        }

        if (returnToIdleAt > 0f && Time.unscaledTime >= returnToIdleAt)
        {
            returnToIdleAt = 0f;
            SetState(TerminalWorldPhase.Idle, TerminalCommands.None);
        }

        if (showingMirror && Time.unscaledTime >= mirrorUntil)
        {
            showingMirror = false;
            statusView.gameObject.SetActive(true);
            mirrorView.gameObject.SetActive(false);
            RenderOnce();
        }
    }

    // ── Live mirror of the operator's screen (received from the network) ──
    private const float MirrorHoldSeconds = 1.5f;
    private Texture2D mirrorTexture;
    private bool showingMirror;
    private float mirrorUntil;

    /// <summary>Shows one JPEG frame of the operator's terminal UI on this monitor.</summary>
    public void ShowMirrorFrame(byte[] jpeg)
    {
        if (mirrorImage == null || jpeg == null || jpeg.Length == 0) return;
        mirrorTexture ??= new Texture2D(2, 2, TextureFormat.RGB24, false) { name = $"{terminalName} Mirror" };
        if (!mirrorTexture.LoadImage(jpeg, false)) return;
        mirrorImage.texture = mirrorTexture;
        if (!showingMirror)
        {
            statusView.gameObject.SetActive(false);
            mirrorView.gameObject.SetActive(true);
        }
        showingMirror = true;
        mirrorUntil = Time.unscaledTime + MirrorHoldSeconds;
        mirrorTag.text = (int)(Time.unscaledTime * 2f) % 2 == 0 ? "● LIVE  //  OPERATOR VIEW" : "  LIVE  //  OPERATOR VIEW";
        RenderOnce();
    }

    /// <summary>
    /// World-space pose of the monitor glass: centre, facing direction (toward the viewer),
    /// screen up, and width/height. Uses the screen material's sub-mesh bounds.
    /// </summary>
    public bool TryGetScreenPose(Vector3 viewer, out Vector3 center, out Vector3 normal, out Vector3 up, out Vector2 size)
    {
        center = normal = up = Vector3.zero;
        size = Vector2.zero;
        if (screenRenderer == null) return false;
        MeshFilter filter = screenRenderer.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null) return false;
        Bounds local = materialElement < mesh.subMeshCount ? mesh.GetSubMesh(materialElement).bounds : mesh.bounds;
        Transform t = screenRenderer.transform;
        center = t.TransformPoint(local.center);
        Vector3[] axes =
        {
            t.TransformVector(new Vector3(local.size.x, 0f, 0f)),
            t.TransformVector(new Vector3(0f, local.size.y, 0f)),
            t.TransformVector(new Vector3(0f, 0f, local.size.z))
        };
        // Monitors can be tilted (kiosk / lectern style), so the glass is a plane spanned by one
        // horizontal "width" axis and the diagonal of the other two bounds axes.
        int vertical = 0;
        for (int i = 1; i < 3; i++)
            if (Mathf.Abs(Vector3.Dot(axes[i].normalized, Vector3.up)) > Mathf.Abs(Vector3.Dot(axes[vertical].normalized, Vector3.up)))
                vertical = i;
        Vector3 toScreen = Vector3.ProjectOnPlane(center - viewer, Vector3.up).normalized;
        int a = (vertical + 1) % 3, b = (vertical + 2) % 3;
        int width = Mathf.Abs(Vector3.Dot(axes[a].normalized, toScreen)) <= Mathf.Abs(Vector3.Dot(axes[b].normalized, toScreen)) ? a : b;
        int depth = width == a ? b : a;

        Vector3 widthDir = axes[width].normalized;
        Vector3 bestDiagonal = axes[vertical];
        normal = Vector3.zero;
        foreach (float sign in new[] { 1f, -1f })
        {
            Vector3 diagonal = axes[vertical] + sign * axes[depth];
            Vector3 candidate = Vector3.Cross(widthDir, diagonal.normalized).normalized;
            if (Vector3.Dot(candidate, viewer - center) < 0f) candidate = -candidate;
            if (normal == Vector3.zero || candidate.y > normal.y)
            {
                normal = candidate;
                bestDiagonal = diagonal;
            }
        }

        up = Vector3.ProjectOnPlane(bestDiagonal, normal).normalized;
        if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
        size = new Vector2(axes[width].magnitude, bestDiagonal.magnitude);
        return size.x > 0.01f && size.y > 0.01f;
    }

    private void FindScreenRenderer()
    {
        if (screenRenderer != null) return;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer candidate in renderers)
        {
            Material[] materials = candidate.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null ||
                    (materials[i].name.IndexOf("ConnectionDevice_glass", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                     materials[i].name.IndexOf("Monitor_glass", System.StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                screenRenderer = candidate;
                materialElement = i;
                return;
            }
        }
        foreach (Renderer candidate in renderers)
        {
            if (candidate.sharedMaterials.Length <= materialElement) continue;
            screenRenderer = candidate;
            return;
        }
        Debug.LogWarning($"[Terminal World Screen] No renderer with material Element {materialElement} was found.", this);
    }

    private void BuildDisplay()
    {
        resolution.x = Mathf.Max(320, resolution.x);
        resolution.y = Mathf.Max(180, resolution.y);
        texture = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32)
        {
            name = $"{terminalName} World Screen",
            useMipMap = true,
            autoGenerateMips = true
        };
        texture.Create();

        GameObject cameraObject = new("World Screen Camera", typeof(Camera));
        cameraObject.transform.SetParent(transform, false);
        displayCamera = cameraObject.GetComponent<Camera>();
        displayCamera.clearFlags = CameraClearFlags.SolidColor;
        displayCamera.backgroundColor = new Color(0.005f, 0.025f, 0.035f);
        displayCamera.cullingMask = 1 << 5;
        // The canvas sits 0.1 in front of this camera; the default near plane (0.3) clipped it away.
        displayCamera.nearClipPlane = 0.02f;
        displayCamera.farClipPlane = 1f;
        displayCamera.targetTexture = texture;
        var hdData = cameraObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hdData.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;
        hdData.backgroundColorHDR = displayCamera.backgroundColor;
        hdData.volumeLayerMask = 0;
        hdData.customRenderingSettings = true;
        hdData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess] = true;
        hdData.renderingPathCustomFrameSettings.SetEnabled(UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess, false);

        GameObject canvasObject = new("World Screen Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = displayCamera;
        canvas.planeDistance = 0.1f;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = resolution;
        SetLayer(canvasObject.transform, 5);

        Image background = canvasObject.AddComponent<Image>();
        background.color = new Color(0.01f, 0.055f, 0.07f);
        background.raycastTarget = false;

        // Content is laid out in the glass's own orientation (some meshes map the UVs sideways).
        int rotation = contentRotation >= 0 ? contentRotation : AutoRotation();
        bool quarterTurn = rotation % 180 != 0;
        RectTransform content = NewRect("Content", canvasObject.transform);
        content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
        content.sizeDelta = quarterTurn ? new Vector2(resolution.y, resolution.x) : (Vector2)resolution;
        content.localRotation = Quaternion.Euler(0f, 0f, rotation);

        statusView = NewRect("Status", content);
        Stretch(statusView);
        header = CreateText("Header", statusView, quarterTurn ? 30 : 32, TextAlignmentOptions.TopLeft,
            new Vector2(0.055f, 0.8f), new Vector2(0.945f, 0.95f));
        body = CreateText("Activity", statusView, quarterTurn ? 40 : 42, TextAlignmentOptions.Center,
            new Vector2(0.08f, 0.2f), new Vector2(0.92f, 0.78f));
        footer = CreateText("Footer", statusView, 20, TextAlignmentOptions.BottomLeft,
            new Vector2(0.055f, 0.03f), new Vector2(0.945f, 0.14f));
        Image rule = NewRect("Rule", statusView).gameObject.AddComponent<Image>();
        rule.color = new Color(0.45f, 1f, 0.82f, 0.35f);
        rule.raycastTarget = false;
        rule.rectTransform.anchorMin = new Vector2(0.055f, 0.78f);
        rule.rectTransform.anchorMax = new Vector2(0.945f, 0.785f);
        rule.rectTransform.offsetMin = rule.rectTransform.offsetMax = Vector2.zero;

        mirrorView = NewRect("Mirror", content);
        Stretch(mirrorView);
        mirrorImage = NewRect("Operator View", mirrorView).gameObject.AddComponent<RawImage>();
        mirrorImage.raycastTarget = false;
        mirrorImage.rectTransform.anchorMin = new Vector2(0.02f, 0.02f);
        mirrorImage.rectTransform.anchorMax = new Vector2(0.98f, 0.9f);
        mirrorImage.rectTransform.offsetMin = mirrorImage.rectTransform.offsetMax = Vector2.zero;
        var fitter = mirrorImage.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 16f / 9f;
        mirrorTag = CreateText("Live Tag", mirrorView, 22, TextAlignmentOptions.TopLeft,
            new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.99f));
        mirrorTag.color = new Color(1f, 0.35f, 0.3f);
        mirrorView.gameObject.SetActive(false);
        SetLayer(canvasObject.transform, 5);

        Material[] materials = screenRenderer.sharedMaterials;
        if (materialElement < 0 || materialElement >= materials.Length) return;
        originalScreenMaterial = materials[materialElement];
        runtimeScreenMaterial = new Material(materials[materialElement])
        {
            name = $"{terminalName} Runtime Display"
        };
        runtimeScreenMaterial.SetTexture(BaseMap, texture);
        runtimeScreenMaterial.SetTexture(MainTex, texture);
        runtimeScreenMaterial.SetTexture(EmissiveMap, texture);
        if (runtimeScreenMaterial.HasProperty("_BaseColor"))
            runtimeScreenMaterial.SetColor("_BaseColor", Color.white);
        if (runtimeScreenMaterial.HasProperty("_EmissiveColor"))
            runtimeScreenMaterial.SetColor("_EmissiveColor", Color.white * 1.5f);
        runtimeScreenMaterial.EnableKeyword("_EMISSIVE_COLOR_MAP");
        materials[materialElement] = runtimeScreenMaterial;
        screenRenderer.sharedMaterials = materials;
        displayCamera.enabled = false;
    }

    private void RenderOnce()
    {
        if (displayCamera == null) return;
        Canvas.ForceUpdateCanvases();
        displayCamera.Render();
    }

    private static TMP_Text CreateText(string name, Transform parent, float size,
        TextAlignmentOptions alignment, Vector2 min, Vector2 max)
    {
        GameObject item = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);
        SetLayer(item.transform, 5);
        TMP_Text text = item.GetComponent<TMP_Text>();
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color(0.45f, 1f, 0.82f);
        text.alignment = alignment;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = min;
        text.rectTransform.anchorMax = max;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        return text;
    }

    private int AutoRotation()
    {
        Material[] materials = screenRenderer.sharedMaterials;
        bool kiosk = materialElement < materials.Length && materials[materialElement] != null &&
                     materials[materialElement].name.IndexOf("ConnectionDevice_glass", System.StringComparison.OrdinalIgnoreCase) >= 0;
        return kiosk ? 270 : 0;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void SetLayer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root) SetLayer(child, layer);
    }

    private void OnDestroy()
    {
        if (screenRenderer != null && originalScreenMaterial != null)
        {
            Material[] materials = screenRenderer.sharedMaterials;
            if (materialElement >= 0 && materialElement < materials.Length)
            {
                materials[materialElement] = originalScreenMaterial;
                screenRenderer.sharedMaterials = materials;
            }
        }
        if (runtimeScreenMaterial != null) Destroy(runtimeScreenMaterial);
        if (mirrorTexture != null) Destroy(mirrorTexture);
        if (texture != null) { texture.Release(); Destroy(texture); }
    }
}
