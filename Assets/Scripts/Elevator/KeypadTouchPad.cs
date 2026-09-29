using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The frosted-glass touch surface: a backlit gradient with a faint hexagon grid, a slowly
/// breathing centre ring, and expanding ripples wherever the finger lands. Rendered into a texture.
/// </summary>
public sealed class KeypadTouchPad : IDisposable
{
    private const float IdleInterval = 0.4f;
    private const int RippleCount = 8;
    private static readonly Vector2Int Size = new(448, 520);

    private struct Ripple
    {
        public Image Ring;
        public Image Dot;
        public float StartedAt;
        public float Duration;
        public float Scale;
        public Color Color;
    }

    private readonly OffscreenUiSurface surface;
    private readonly Texture2D hexTexture;
    private readonly Texture2D gradientTexture;
    private readonly Image breathing;
    private readonly Ripple[] ripples = new Ripple[RippleCount];
    private readonly Color accent;
    private int nextRipple;
    private float nextIdleRender;

    public Texture Texture => surface.Texture;

    public KeypadTouchPad(Color accent)
    {
        this.accent = accent;
        surface = new OffscreenUiSurface("Elevator Keypad Touch Pad", Size, 30f);
        RectTransform root = surface.Root;

        gradientTexture = BuildGradient();
        RawImage glass = new GameObject("Frosted Glass", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        glass.transform.SetParent(root, false);
        glass.texture = gradientTexture;
        RuntimeUi.Stretch(glass.rectTransform);

        hexTexture = BuildHexTile(96);
        RawImage grid = new GameObject("Hex Grid", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        grid.transform.SetParent(root, false);
        grid.texture = hexTexture;
        grid.color = new Color(accent.r * 0.55f, accent.g * 0.85f, accent.b * 0.95f, 0.85f);
        grid.uvRect = new Rect(0f, 0f, Size.x / 96f, Size.y / (96f * 0.57735f));
        RuntimeUi.Stretch(grid.rectTransform);

        breathing = RuntimeUi.Panel("Centre Ring", root, new Color(accent.r, accent.g, accent.b, 0.3f), RuntimeUiSprites.Ring);
        RuntimeUi.PlaceCentered(breathing.rectTransform, new Vector2(0.5f, 0.55f), new Vector2(170f, 170f));

        for (int i = 0; i < RippleCount; i++)
        {
            Image ring = RuntimeUi.Panel($"Ripple {i}", root, Color.clear, RuntimeUiSprites.Ring);
            Image dot = RuntimeUi.Panel($"Touch {i}", root, Color.clear, RuntimeUiSprites.SoftGlow);
            ripples[i] = new Ripple { Ring = ring, Dot = dot, StartedAt = -10f };
        }
    }

    /// <summary>Starts a ripple at a normalised pad position (0..1, origin bottom-left as the viewer sees it).</summary>
    public void Touch(Vector2 uv, Color color, float scale = 1f, float duration = 0.7f)
    {
        ref Ripple ripple = ref ripples[nextRipple];
        nextRipple = (nextRipple + 1) % RippleCount;
        ripple.StartedAt = Time.unscaledTime;
        ripple.Duration = duration;
        ripple.Scale = scale;
        ripple.Color = color;
        foreach (Image image in new[] { ripple.Ring, ripple.Dot })
        {
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = uv;
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    public void Tick(bool active)
    {
        float now = Time.unscaledTime;
        float breathe = 0.5f + 0.5f * Mathf.Sin(now * 1.8f);
        breathing.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(150f, 190f, breathe);
        breathing.color = new Color(accent.r, accent.g, accent.b, active ? 0.18f + 0.12f * breathe : 0.12f);

        bool animating = false;
        for (int i = 0; i < RippleCount; i++)
        {
            Ripple ripple = ripples[i];
            float t = (now - ripple.StartedAt) / Mathf.Max(0.05f, ripple.Duration);
            if (t < 0f || t > 1f)
            {
                ripple.Ring.color = Color.clear;
                ripple.Dot.color = Color.clear;
                continue;
            }
            animating = true;
            float grow = 1f - Mathf.Pow(1f - t, 3f);
            ripple.Ring.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(40f, 260f * ripple.Scale, grow);
            ripple.Ring.color = new Color(ripple.Color.r, ripple.Color.g, ripple.Color.b, (1f - t) * 0.95f);
            ripple.Dot.rectTransform.sizeDelta = Vector2.one * 120f * ripple.Scale;
            ripple.Dot.color = new Color(ripple.Color.r, ripple.Color.g, ripple.Color.b, Mathf.Clamp01(1f - t * 2.2f) * 0.9f);
        }

        if (!active && !animating)
        {
            if (now < nextIdleRender) return;
            nextIdleRender = now + IdleInterval;
            surface.Tick(true);
            return;
        }
        surface.Tick();
    }

    public void Dispose()
    {
        surface.Dispose();
        if (hexTexture != null) UnityEngine.Object.Destroy(hexTexture);
        if (gradientTexture != null) UnityEngine.Object.Destroy(gradientTexture);
    }

    // Soft, backlit frosted glass: brighter in the middle, cool cyan toward the bottom.
    private static Texture2D BuildGradient()
    {
        const int w = 64, h = 128;
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Keypad Frost" };
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (w - 1f) - 0.5f, v = y / (h - 1f);
                float glow = Mathf.Exp(-(u * u * 5f + (v - 0.55f) * (v - 0.55f) * 4f));
                Color top = new(0.82f, 0.9f, 0.93f), bottom = new(0.6f, 0.84f, 0.9f);
                Color c = Color.Lerp(bottom, top, v) + new Color(0.1f, 0.12f, 0.12f) * glow;
                pixels[y * w + x] = c;
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    // One tile of a flat-topped hexagon line grid (thin anti-aliased edges, transparent inside).
    private static Texture2D BuildHexTile(int size)
    {
        int width = size, height = Mathf.RoundToInt(size * 0.57735f); // tile = 3R x sqrt(3)R
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Keypad Hex" };
        var pixels = new Color32[width * height];
        float r = size / 3f;
        Vector2[] centres =
        {
            new(0f, 0f), new(width, 0f), new(0f, height), new(width, height), new(width * 0.5f, height * 0.5f),
            new(-width * 0.5f, height * 0.5f), new(width * 1.5f, height * 0.5f)
        };
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float best = float.MaxValue;
                foreach (Vector2 c in centres)
                {
                    Vector2 d = new(Mathf.Abs(x - c.x), Mathf.Abs(y - c.y));
                    // Distance to a flat-topped hexagon edge.
                    float hex = Mathf.Max(d.y, d.x * 0.866f + d.y * 0.5f) - r * 0.866f;
                    best = Mathf.Min(best, Mathf.Abs(hex));
                }
                float line = Mathf.Clamp01(1.4f - best);
                pixels[y * width + x] = new Color(1f, 1f, 1f, line);
            }
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }
}
