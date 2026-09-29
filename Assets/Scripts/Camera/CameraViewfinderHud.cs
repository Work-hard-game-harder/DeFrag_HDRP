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

    private static readonly Color HudWhite = new(0.9f, 0.97f, 0.95f, 0.92f);
    private static readonly Color RecRed = new(1f, 0.2f, 0.16f, 1f);
    private static readonly Color IrGreen = new(0.45f, 1f, 0.4f, 0.95f);

    private CameraItem item;
    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform frame;
    private RawImage noise;
    private Image irTint, flash, recDot;
    private Image[] battery;
    private RectTransform meterMarker;
    private TMP_Text rec, timecode, batteryLabel, mode, exposure, device, hints, toast, lowBattery;
    private Texture2D noiseTexture;
    private float shownAt = -10f;
    private float recordSeconds;
    private float flashAlpha;
    private float toastUntil;
    private int photoCount;
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
        photoCount++;
        flashAlpha = 0.85f;
        toastUntil = Time.unscaledTime + 1.1f;
        toast.text = $"■ CAPTURED   IMG {photoCount:0000}";
    }

    private void Update()
    {
        if (!visible || item == null) return;
        float now = Time.unscaledTime;
        float boot = Mathf.Clamp01((now - shownAt) / BootDuration);
        recordSeconds += Time.unscaledDeltaTime;

        bool ir = item.CurrentMode == CameraItem.CameraMode.Infrared;
        Color hud = ir ? IrGreen : HudWhite;
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

        frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        frame.SetParent(root, false);
        RuntimeUi.Stretch(frame, 70f);
        AddBrackets(frame, 110f, 4f);
        AddReticle(root);

        // Top-left: REC + timecode
        recDot = RuntimeUi.Panel("Rec Dot", frame, RecRed);
        Anchor(recDot.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -44f), new Vector2(22f, 22f));
        recDot.sprite = CircleSprite();
        rec = Label("REC", frame, 34f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(66f, -44f), new Vector2(120f, 40f));
        rec.text = "REC";
        timecode = Label("Timecode", frame, 30f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(160f, -44f), new Vector2(260f, 40f));

        // Top-right: battery
        battery = new Image[BatterySegments];
        for (int i = 0; i < BatterySegments; i++)
        {
            battery[i] = RuntimeUi.Panel($"Cell {i}", frame, HudWhite);
            Anchor(battery[i].rectTransform, new Vector2(1f, 1f), new Vector2(-210f + i * 26f, -44f), new Vector2(20f, 26f));
        }
        batteryLabel = Label("Battery", frame, 24f, TextAlignmentOptions.MidlineRight, new Vector2(1f, 1f), new Vector2(-250f, -44f), new Vector2(170f, 34f));
        batteryLabel.rectTransform.pivot = new Vector2(1f, 0.5f);

        // Bottom-left: mode + exposure + device
        mode = Label("Mode", frame, 30f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(40f, 96f), new Vector2(520f, 40f));
        exposure = Label("Exposure", frame, 22f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(40f, 62f), new Vector2(520f, 30f));
        exposure.text = "ISO 3200   F2.8   1/60   AWB";
        device = Label("Device", frame, 20f, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(40f, 32f), new Vector2(520f, 28f));
        device.text = "NVCAM-01  //  DEFRAG FIELD UNIT";

        // Bottom-right: key hints
        hints = Label("Hints", frame, 22f, TextAlignmentOptions.MidlineRight, new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(700f, 30f));
        hints.rectTransform.pivot = new Vector2(1f, 0.5f);
        hints.text = "[우클릭] IR 전환    [좌클릭] 촬영    [C] 내리기";

        // Bottom-centre exposure meter
        RectTransform meter = new GameObject("Meter", typeof(RectTransform)).GetComponent<RectTransform>();
        meter.SetParent(frame, false);
        Anchor(meter, new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(340f, 30f));
        for (int i = -3; i <= 3; i++)
        {
            Image tick = RuntimeUi.Panel($"Tick {i}", meter, new Color(1f, 1f, 1f, 0.55f));
            Anchor(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(i * 50f, 0f), new Vector2(i == 0 ? 4f : 2f, i == 0 ? 22f : 12f));
        }
        Image marker = RuntimeUi.Panel("Marker", meter, HudWhite);
        meterMarker = marker.rectTransform;
        Anchor(meterMarker, new Vector2(0.5f, 0.5f), new Vector2(0f, -16f), new Vector2(10f, 10f));

        toast = Label("Toast", root, 34f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(700f, 50f));
        lowBattery = Label("Low Battery", root, 40f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(700f, 60f));
        lowBattery.text = "LOW BATTERY";
        lowBattery.color = RecRed;

        flash = RuntimeUi.Panel("Shutter Flash", root, Color.clear);
        RuntimeUi.Stretch(flash.rectTransform);
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

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void AddBrackets(RectTransform target, float length, float thickness)
    {
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 anchor = new(corner % 2, corner / 2);
            Vector2 inward = new(anchor.x > 0.5f ? -1f : 1f, anchor.y > 0.5f ? -1f : 1f);
            Image horizontal = RuntimeUi.Panel("Bracket H", target, HudWhite);
            Anchor(horizontal.rectTransform, anchor, new Vector2(inward.x * length * 0.5f, inward.y * thickness * 0.5f), new Vector2(length, thickness));
            Image vertical = RuntimeUi.Panel("Bracket V", target, HudWhite);
            Anchor(vertical.rectTransform, anchor, new Vector2(inward.x * thickness * 0.5f, inward.y * length * 0.5f), new Vector2(thickness, length));
        }
    }

    private static void AddReticle(Transform parent)
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
            Anchor(line.rectTransform, new Vector2(0.5f, 0.5f), arm.pos, arm.size);
        }
        Image dot = RuntimeUi.Panel("Reticle Dot", parent, color);
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
