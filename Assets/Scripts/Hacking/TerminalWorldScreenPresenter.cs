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
    private RenderTexture texture;
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

    private void Update()
    {
        if (returnToIdleAt > 0f && Time.unscaledTime >= returnToIdleAt)
        {
            returnToIdleAt = 0f;
            SetState(TerminalWorldPhase.Idle, TerminalCommands.None);
        }
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
        displayCamera.targetTexture = texture;

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
        header = CreateText("Header", canvasObject.transform, 32, TextAlignmentOptions.TopLeft,
            new Vector2(0.055f, 0.78f), new Vector2(0.945f, 0.94f));
        body = CreateText("Activity", canvasObject.transform, 42, TextAlignmentOptions.Center,
            new Vector2(0.08f, 0.2f), new Vector2(0.92f, 0.76f));
        footer = CreateText("Footer", canvasObject.transform, 21, TextAlignmentOptions.BottomLeft,
            new Vector2(0.055f, 0.04f), new Vector2(0.945f, 0.14f));

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
        if (texture != null) { texture.Release(); Destroy(texture); }
    }
}
