using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared lightweight console framing; decorative graphics never intercept input.</summary>
public static class OperationPanelStyle
{
    public static readonly Color Surface = new(0.025f, 0.065f, 0.085f, 0.97f);
    public static readonly Color Accent = new(0.3f, 0.95f, 0.8f, 1f);
    public static readonly Color InputSurface = new(0.065f, 0.16f, 0.19f, 1f);

    public static void Frame(GameObject panel)
    {
        Image background = panel.GetComponent<Image>();
        if (background != null) background.color = Surface;
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(0.3f, 0.75f, 0.7f, 0.45f);
        outline.effectDistance = new Vector2(2f, -2f);
        Rule(panel.transform, new Vector2(0.025f, 0.985f), new Vector2(0.15f, 0.99f), Accent);
        Rule(panel.transform, new Vector2(0.025f, 0.835f), new Vector2(0.975f, 0.838f), new Color(0.3f, 0.75f, 0.7f, 0.3f));
    }

    public static void Input(GameObject panel)
    {
        panel.GetComponent<Image>().color = InputSurface;
        Rule(panel.transform, Vector2.zero, new Vector2(0.004f, 1f), Accent);
    }

    private static void Rule(Transform parent, Vector2 min, Vector2 max, Color color)
    {
        GameObject rule = new("Console accent", typeof(RectTransform), typeof(Image));
        rule.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)rule.transform;
        rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Image image = rule.GetComponent<Image>();
        image.color = color; image.raycastTarget = false;
    }
}
