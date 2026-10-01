using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local-only camcorder viewfinder drawn over the item camera view: brackets, reticle,
/// REC + timecode, battery segments, mode (NORMAL / IR NIGHT), exposure meter, key hints,
/// shutter flash and a short boot animation when the camera is raised.
/// </summary>
public sealed class CameraViewfinderHud : MonoBehaviour
{
    private const float BootDuration = 0.45f;
    private const int BatterySegments = 5;

    private static readonly Color HudWhite = new(1f, 1f, 1f, 0.94f);
    private static readonly Color RecRed = new(1f, 0.24f, 0.18f, 1f);
    private static readonly Color IrGreen = new(0.28f, 1f, 0.76f, 0.96f);
    private static readonly Color RailColor = new(0.015f, 0.035f, 0.04f, 0.62f);

    private CameraItem item;
    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform frame;
    private RawImage noise;
    private Image irTint, flash, recDot;
    private Image[] battery;
    private readonly List<Image> themedDecorations = new();
    private RectTransform meterMarker;
    private TMP_Text rec, timecode, batteryLabel, mode, exposure, device, hints, toast, lowBattery;
    private Texture2D noiseTexture;
    private float shownAt = -10f;
    private float recordSeconds;
    private float flashAlpha;
    private float toastUntil;
    private bool visible;

    public void Initialize(CameraItem cameraItem, int sortingOrder)
    {
        item = cameraItem;
        if (canvas == null) Build(sortingOrder);
        if (item != null) item.PhotoTaken += OnPhotoTaken;
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (item != null) item.PhotoTaken -= OnPhotoTaken;
        if (canvas != null) Destroy(canvas.gameObject);
        if (noiseTexture != null) Destroy(noiseTexture);
    }

    public void Show(bool boot)
    {
        SetVisible(true);
        shownAt = boot ? Time.unscaledTime : Time.unscaledTime - BootDuration;
    }

    public void Hide() => SetVisible(false);

    private void SetVisible(bool value)
    {
        visible = value;
        if (canvas != null) canvas.enabled = value;
    }

    private void OnPhotoTaken()
    {
        flashAlpha = 0.85f;
        toastUntil = Time.unscaledTime + 1.1f;
        toast.text = "■ CAPTURED!";
    }

    private void Update()
    {
        if (!visible || item == null) return;
        float now = Time.unscaledTime;
        float boot = Mathf.Clamp01((now - shownAt) / BootDuration);
        recordSeconds += Time.unscaledDeltaTime;

        bool ir = item.CurrentMode == CameraItem.CameraMode.Infrared;
        Color hud = ir ? IrGreen : HudWhite;
        ApplyTheme(hud);
        CameraBattery cell = item.Battery;
        float charge = cell != null ? cell.ChargeRatio : 1f;

        // Boot: brackets spring out, flicker, static clears.
        float spring = 1f - Mathf.Pow(1f - boot, 3f);
        frame.localScale = Vector3.one * Mathf.Lerp(0.62f, 1f, spring);
        group.alpha = boot < 1f ? (Random.value < 0.25f ? 0.35f : Mathf.Lerp(0.4f, 1f, boot)) : 1f;
        Color noiseColor = noise.color;
        noiseColor.a = Mathf.Lerp(0.55f, 0.05f, boot) + (ir ? 0.04f : 0f);
        noise.color = noiseColor;
        noise.uvRect = new Rect(Random.value, Random.value, 2.4f, 1.35f);

        irTint.color = new Color(0.15f, 1f, 0.2f, ir ? 0.12f : 0f);

        bool blink = Mathf.Repeat(now, 1f) < 0.6f;
        recDot.color = blink && boot > 0.6f ? RecRed : new Color(RecRed.r, RecRed.g, RecRed.b, 0.15f);
        rec.color = hud;
        int frames = (int)(recordSeconds * 30f) % 30;
        int total = (int)recordSeconds;
        timecode.text = $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}:{frames:00}";
        timecode.color = hud;

        int lit = Mathf.CeilToInt(charge * BatterySegments);
        bool low = charge < 0.2f;
        for (int i = 0; i < battery.Length; i++)
        {
            Color on = low ? RecRed : hud;
            battery[i].color = i < lit ? (low && !blink ? new Color(on.r, on.g, on.b, 0.25f) : on)
                : new Color(hud.r, hud.g, hud.b, 0.15f);
        }
        batteryLabel.text = $"BATT {Mathf.RoundToInt(charge * 100f):00}%";
        batteryLabel.color = low ? RecRed : hud;

        mode.text = ir ? "MODE  <b>IR NIGHT</b>" : "MODE  <b>NORMAL</b>";
        mode.color = hud;
        exposure.color = new Color(hud.r, hud.g, hud.b, 0.7f);
        device.color = new Color(hud.r, hud.g, hud.b, 0.55f);
        hints.color = new Color(hud.r, hud.g, hud.b, 0.55f);
        float drift = Mathf.PerlinNoise(now * 0.7f, 0.3f) * 2f - 1f;
        meterMarker.anchoredPosition = new Vector2(drift * 150f, meterMarker.anchoredPosition.y);

        lowBattery.gameObject.SetActive(ir && low && blink);
        flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.unscaledDeltaTime * 3.5f);
        flash.color = new Color(1f, 1f, 1f, flashAlpha);
        toast.gameObject.SetActive(now < toastUntil);
        toast.color = hud;
    }

    // ───────────────────────────── Construction ─────────────────────────────

    private void Build(int sortingOrder)
    {
        canvas = RuntimeUi.Canvas("Camera Viewfinder HUD", null, sortingOrder);
        canvas.GetComponent<GraphicRaycaster>().enabled = false;
        Transform root = canvas.transform;
        group = canvas.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        irTint = RuntimeUi.Panel("IR Tint", root, Color.clear);
        RuntimeUi.Stretch(irTint.rectTransform);

        noiseTexture = BuildNoiseTexture();
        noise = new GameObject("Sensor Noise", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        noise.transform.SetParent(root, false);
        noise.texture = noiseTexture;
        noise.raycastTarget = false;
        noise.color = new Color(1f, 1f, 1f, 0.05f);
        RuntimeUi.Stretch(noise.rectTransform);
        RuntimeUi.Scanlines(root, 0.07f);
        RawImage vignette = new GameObject("Vignette", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        vignette.transform.SetParent(root, false);
        vignette.texture = BuildVignetteTexture();
        vignette.color = new Color(0f, 0f, 0f, 0.75f);
        vignette.raycastTarget = false;
        RuntimeUi.Stretch(vignette.rectTransform);

        AddStatusRails(root);

        frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        frame.SetParent(root, false);
        RuntimeUi.Stretch(frame, 56f);
        AddBrackets(frame, 86f, 3f);
        AddReticle(root);

        // Top-left: REC + timecode
        recDot = RuntimeUi.Panel("Rec Dot", frame, RecRed);
        Anchor(recDot.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -34f), new Vector2(16f, 16f));
        recDot.sprite = CircleSprite();
        rec = Label("REC", frame, 24f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(50f, -34f), new Vector2(90f, 34f));
        rec.text = "LIVE";
        timecode = Label("Timecode", frame, 24f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(122f, -34f), new Vector2(250f, 34f));

        // Top-right: battery
        battery = new Image[BatterySegments];
        for (int i = 0; i < BatterySegments; i++)
        {
            battery[i] = RuntimeUi.Panel($"Cell {i}", frame, HudWhite);
            Anchor(battery[i].rectTransform, new Vector2(1f, 1f), new Vector2(-174f + i * 21f, -34f), new Vector2(15f, 20f));
        }
        batteryLabel = Label("Battery", frame, 21f, TextAlignmentOptions.MidlineRight, new Vector2(1f, 1f), new Vector2(-212f, -34f), new Vector2(150f, 30f));
        batteryLabel.rectTransform.pivot = new Vector2(1f, 0.5f);

        // Bottom-left: mode + exposure + device
        mode = Label("Mode", frame, 25f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(30f, 74f), new Vector2(440f, 34f));
        exposure = Label("Exposure", frame, 19f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(30f, 46f), new Vector2(440f, 26f));
        exposure.text = "ISO 3200  ·  F2.8  ·  1/60  ·  AWB";
        device = Label("Device", frame, 17f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(30f, 21f), new Vector2(440f, 24f));
        device.text = "NVCAM-01  /  FIELD UNIT";

        // Bottom-right: key hints
        hints = Label("Hints", frame, 18f, TextAlignmentOptions.MidlineRight, new Vector2(1f, 0f), new Vector2(-30f, 28f), new Vector2(660f, 28f));
        hints.rectTransform.pivot = new Vector2(1f, 0.5f);
        hints.text = "RMB  IR MODE     LMB  CAPTURE     C  LOWER";

        // Bottom-centre exposure meter
        RectTransform meter = new GameObject("Meter", typeof(RectTransform)).GetComponent<RectTransform>();
        meter.SetParent(frame, false);
        Anchor(meter, new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(300f, 26f));
        for (int i = -3; i <= 3; i++)
        {
            Image tick = RuntimeUi.Panel($"Tick {i}", meter, new Color(1f, 1f, 1f, 0.55f));
            Anchor(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(i * 42f, 0f), new Vector2(i == 0 ? 3f : 2f, i == 0 ? 18f : 10f));
            themedDecorations.Add(tick);
        }
        Image marker = RuntimeUi.Panel("Marker", meter, HudWhite);
        themedDecorations.Add(marker);
        meterMarker = marker.rectTransform;
        Anchor(meterMarker, new Vector2(0.5f, 0.5f), new Vector2(0f, -16f), new Vector2(10f, 10f));

        toast = Label("Toast", root, 28f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, -152f), new Vector2(620f, 44f));
        lowBattery = Label("Low Battery", root, 34f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 145f), new Vector2(620f, 52f));
        lowBattery.text = "LOW BATTERY";
        lowBattery.color = RecRed;

        flash = RuntimeUi.Panel("Shutter Flash", root, Color.clear);
        RuntimeUi.Stretch(flash.rectTransform);
    }

    private void AddStatusRails(Transform root)
    {
        Image top = RuntimeUi.Panel("Top Status Rail", root, RailColor);
        RectTransform topRect = top.rectTransform;
        topRect.anchorMin = new Vector2(0f, 1f);
        topRect.anchorMax = Vector2.one;
        topRect.pivot = new Vector2(0.5f, 1f);
        topRect.anchoredPosition = Vector2.zero;
        topRect.sizeDelta = new Vector2(0f, 94f);

        Image bottom = RuntimeUi.Panel("Bottom Status Rail", root, RailColor);
        RectTransform bottomRect = bottom.rectTransform;
        bottomRect.anchorMin = Vector2.zero;
        bottomRect.anchorMax = new Vector2(1f, 0f);
        bottomRect.pivot = new Vector2(0.5f, 0f);
        bottomRect.anchoredPosition = Vector2.zero;
        bottomRect.sizeDelta = new Vector2(0f, 112f);

        Image topLine = RuntimeUi.Panel("Top Accent", root, new Color(HudWhite.r, HudWhite.g, HudWhite.b, 0.28f));
        themedDecorations.Add(topLine);
        RectTransform topLineRect = topLine.rectTransform;
        topLineRect.anchorMin = new Vector2(0f, 1f);
        topLineRect.anchorMax = Vector2.one;
        topLineRect.pivot = new Vector2(0.5f, 1f);
        topLineRect.anchoredPosition = new Vector2(0f, -94f);
        topLineRect.sizeDelta = new Vector2(0f, 2f);

        Image bottomLine = RuntimeUi.Panel("Bottom Accent", root, new Color(HudWhite.r, HudWhite.g, HudWhite.b, 0.22f));
        themedDecorations.Add(bottomLine);
        RectTransform bottomLineRect = bottomLine.rectTransform;
        bottomLineRect.anchorMin = Vector2.zero;
        bottomLineRect.anchorMax = new Vector2(1f, 0f);
        bottomLineRect.pivot = new Vector2(0.5f, 0f);
        bottomLineRect.anchoredPosition = new Vector2(0f, 112f);
        bottomLineRect.sizeDelta = new Vector2(0f, 2f);
    }

    private static TMP_Text Label(string name, Transform parent, float size, TextAlignmentOptions alignment,
        Vector2 anchor, Vector2 position, Vector2 sizeDelta)
    {
        TMP_Text text = RuntimeUi.Text(name, parent, size, alignment, null, HudWhite);
        text.richText = true;
        Anchor(text.rectTransform, anchor, position, sizeDelta);
        text.rectTransform.pivot = new Vector2(alignment == TextAlignmentOptions.MidlineRight ? 1f : alignment == TextAlignmentOptions.Center ? 0.5f : 0f, 0.5f);
        return text;
    }

    private void ApplyTheme(Color theme)
    {
        foreach (Image decoration in themedDecorations)
        {
            if (decoration == null)
                continue;

            Color color = decoration.color;
            color.r = theme.r;
            color.g = theme.g;
            color.b = theme.b;
            decoration.color = color;
        }
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private void AddBrackets(RectTransform target, float length, float thickness)
    {
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 anchor = new(corner % 2, corner / 2);
            Vector2 inward = new(anchor.x > 0.5f ? -1f : 1f, anchor.y > 0.5f ? -1f : 1f);
            Image horizontal = RuntimeUi.Panel("Bracket H", target, HudWhite);
            themedDecorations.Add(horizontal);
            Anchor(horizontal.rectTransform, anchor, new Vector2(inward.x * length * 0.5f, inward.y * thickness * 0.5f), new Vector2(length, thickness));
            Image vertical = RuntimeUi.Panel("Bracket V", target, HudWhite);
            themedDecorations.Add(vertical);
            Anchor(vertical.rectTransform, anchor, new Vector2(inward.x * thickness * 0.5f, inward.y * length * 0.5f), new Vector2(thickness, length));
        }
    }

    private void AddReticle(Transform parent)
    {
        Color color = new(1f, 1f, 1f, 0.7f);
        (Vector2 pos, Vector2 size)[] arms =
        {
            (new Vector2(-34f, 0f), new Vector2(30f, 2f)), (new Vector2(34f, 0f), new Vector2(30f, 2f)),
            (new Vector2(0f, -34f), new Vector2(2f, 30f)), (new Vector2(0f, 34f), new Vector2(2f, 30f))
        };
        foreach (var arm in arms)
        {
            Image line = RuntimeUi.Panel("Reticle", parent, color);
            themedDecorations.Add(line);
            Anchor(line.rectTransform, new Vector2(0.5f, 0.5f), arm.pos, arm.size);
        }
        Image dot = RuntimeUi.Panel("Reticle Dot", parent, color);
        themedDecorations.Add(dot);
        Anchor(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5f, 5f));
    }

    private static Sprite circle;
    private static Sprite CircleSprite()
    {
        if (circle != null) return circle;
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * size * 0.5f);
            texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(size * 0.5f - d)));
        }
        texture.Apply();
        circle = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f);
        return circle;
    }

    private static Texture2D BuildNoiseTexture()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Viewfinder Noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point
        };
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = Random.value;
            pixels[i] = new Color(v, v, v, Random.value * 0.8f);
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static Texture2D BuildVignetteTexture()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (size - 1f) * 2f - 1f, v = y / (size - 1f) * 2f - 1f;
            pixels[y * size + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0.55f, 1.35f, Mathf.Sqrt(u * u * 0.7f + v * v)));
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
