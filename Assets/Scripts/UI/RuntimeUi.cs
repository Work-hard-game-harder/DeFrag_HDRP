using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Minimal builders for code-constructed HUDs, styled from DefragUiTheme.
public static class RuntimeUi
{
    private static Texture2D scanlineTexture;

    public static DefragUiTheme Theme => DefragUiTheme.Current;

    public static Canvas Canvas(string name, Transform parent, int sortingOrder)
    {
        GameObject canvasObject = new(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(parent, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    public static Image Panel(string name, Transform parent, Color color, Sprite sprite = null)
    {
        GameObject panel = new(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        Image image = panel.GetComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    public static TMP_Text Text(string name, Transform parent, float size, TextAlignmentOptions alignment,
        TMP_FontAsset font, Color color)
    {
        GameObject target = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        target.transform.SetParent(parent, false);
        TMP_Text text = target.GetComponent<TMP_Text>();
        TMP_FontAsset resolved = font != null ? font : Theme.font;
        if (resolved != null)
            text.font = resolved;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    public static TMP_Text Text(string name, Transform parent, float size, TextAlignmentOptions alignment) =>
        Text(name, parent, size, alignment, null, Theme.text);

    public static void Place(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static void PlaceCentered(RectTransform rect, Vector2 anchor, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    public static void Stretch(RectTransform rect, float padding = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    // Panel with a hairline border and bracketed corners; decorative children never block input.
    public static Image FramedPanel(string name, Transform parent, Color? fill = null, float bracket = 22f)
    {
        Image panel = Panel(name, parent, fill ?? Theme.panel);
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(Theme.edge.r, Theme.edge.g, Theme.edge.b, 0.35f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        AddCornerBrackets(panel.rectTransform, bracket, 3f, Theme.edge);
        return panel;
    }

    public static void AddCornerBrackets(RectTransform target, float length, float thickness, Color color)
    {
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 anchor = new(corner % 2, corner / 2);
            Vector2 inward = new(anchor.x > 0.5f ? -1f : 1f, anchor.y > 0.5f ? -1f : 1f);
            Bar(target, anchor, new Vector2(length, thickness), new Vector2(inward.x * length * 0.5f, inward.y * thickness * 0.5f), color);
            Bar(target, anchor, new Vector2(thickness, length), new Vector2(inward.x * thickness * 0.5f, inward.y * length * 0.5f), color);
        }
    }

    private static void Bar(RectTransform parent, Vector2 anchor, Vector2 size, Vector2 offset, Color color)
    {
        Image bar = Panel("Bracket", parent, color);
        RectTransform rect = bar.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
    }

    public static TMP_Text KeyCap(Transform parent, string label, Vector2 size, out Image background)
    {
        background = Panel($"Key {label}", parent, Theme.panelRaised);
        background.rectTransform.sizeDelta = size;
        Outline outline = background.gameObject.AddComponent<Outline>();
        outline.effectColor = Theme.edge;
        outline.effectDistance = new Vector2(2f, -2f);
        TMP_Text text = Text("Label", background.transform, size.y * 0.5f, TextAlignmentOptions.Center, null, Theme.highlight);
        Stretch(text.rectTransform, 2f);
        text.text = label;
        text.enableAutoSizing = true;
        text.fontSizeMin = 12f;
        text.fontSizeMax = size.y * 0.55f;
        return text;
    }

    // Faint CRT scanlines over a panel for a consistent terminal look.
    public static RawImage Scanlines(Transform parent, float opacity = 0.05f)
    {
        scanlineTexture ??= BuildScanlineTexture();
        GameObject lines = new("Scanlines", typeof(RectTransform), typeof(RawImage));
        lines.transform.SetParent(parent, false);
        RawImage image = lines.GetComponent<RawImage>();
        image.texture = scanlineTexture;
        image.color = new Color(1f, 1f, 1f, opacity);
        image.raycastTarget = false;
        image.uvRect = new Rect(0f, 0f, 1f, 160f);
        Stretch(image.rectTransform);
        return image;
    }

    // Soft sine bands instead of hard pixel rows, so non-integer screen scaling never produces moire.
    private static Texture2D BuildScanlineTexture()
    {
        const int height = 16;
        Texture2D texture = new(1, height, TextureFormat.RGBA32, false)
        {
            name = "RuntimeUi_Scanlines",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };
        Color[] pixels = new Color[height];
        for (int y = 0; y < height; y++)
            pixels[y] = new Color(0f, 0f, 0f, 0.5f + 0.5f * Mathf.Sin(y / (float)height * Mathf.PI * 2f));
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
