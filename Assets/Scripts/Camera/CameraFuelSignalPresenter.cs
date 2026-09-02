using DeFrag.B1F;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CameraItem))]
public sealed class CameraFuelSignalPresenter : MonoBehaviour
{
    private static readonly Color SignalGreen = new(0.15f, 1f, 0.38f, 1f);

    [Header("Fuel Signal Range")]
    [SerializeField, Min(0.1f)] private float nearDistance = 2f;
    [SerializeField, Min(1f)] private float farDistance = 60f;
    [SerializeField, Range(3, 7)] private int barCount = 5;

    private CameraItem cameraItem;
    private Camera viewCamera;
    private GeneratorBController generator;
    private Canvas canvas;
    private TMP_Text label;
    private Image[] bars;

    private void Awake()
    {
        cameraItem = GetComponent<CameraItem>();
        viewCamera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        GeneratorBController.LocalInstanceAvailable += BindGenerator;
        BindGenerator(GeneratorBController.LocalInstance);
    }

    private void OnDisable()
    {
        GeneratorBController.LocalInstanceAvailable -= BindGenerator;
        BindGenerator(null);
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (canvas != null)
            Destroy(canvas.gameObject);
    }

    private void Update()
    {
        bool visible = ShouldShow();
        if (!visible)
        {
            SetVisible(false);
            return;
        }

        EnsureHud();
        SetVisible(true);
        RefreshSignal();
    }

    private bool ShouldShow() =>
        generator != null && generator.IsSpawned && generator.FuelSignalVisible &&
        generator.FuelCan != null && viewCamera != null && viewCamera.enabled &&
        cameraItem.IsEquipped && cameraItem.IsViewActive &&
        cameraItem.CurrentMode == CameraItem.CameraMode.Infrared;

    private void BindGenerator(GeneratorBController value) => generator = value;

    private void RefreshSignal()
    {
        Vector3 target = generator.FuelCan.SignalAnchor.position;
        float distance = Vector3.Distance(viewCamera.transform.position, target);
        float far = Mathf.Max(nearDistance + 0.1f, farDistance);
        float strength = 1f - Mathf.InverseLerp(nearDistance, far, distance);
        int activeBars = strength <= 0.01f
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(strength * bars.Length), 1, bars.Length);
        float pulse = Mathf.Sin(Time.unscaledTime * Mathf.Lerp(2f, 10f, strength)) *
                      0.5f + 0.5f;

        label.text = $"FUEL_B SIGNAL  //  {distance:0.0}m";
        for (int i = 0; i < bars.Length; i++)
        {
            bool active = i < activeBars;
            bars[i].color = new Color(
                SignalGreen.r,
                SignalGreen.g,
                SignalGreen.b,
                active ? Mathf.Lerp(0.72f, 1f, pulse) : 0.13f);
        }
    }

    private void EnsureHud()
    {
        if (canvas != null)
            return;

        GameObject canvasObject = new(
            "Fuel Signal HUD",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 134;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject display = new("Fuel Frequency", typeof(RectTransform), typeof(Image));
        display.transform.SetParent(canvasObject.transform, false);
        RectTransform displayRect = (RectTransform)display.transform;
        displayRect.anchorMin = new Vector2(0.34f, 0.11f);
        displayRect.anchorMax = new Vector2(0.66f, 0.19f);
        displayRect.offsetMin = Vector2.zero;
        displayRect.offsetMax = Vector2.zero;
        display.GetComponent<Image>().color = new Color(0f, 0.04f, 0.015f, 0.84f);

        GameObject textObject = new(
            "Fuel Signal Label",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(display.transform, false);
        label = textObject.GetComponent<TMP_Text>();
        label.fontSize = 23f;
        label.fontStyle = FontStyles.Bold;
        label.color = SignalGreen;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        RectTransform textRect = label.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = new Vector2(0.66f, 1f);
        textRect.offsetMin = new Vector2(24f, 4f);
        textRect.offsetMax = new Vector2(-8f, -4f);

        bars = new Image[Mathf.Max(3, barCount)];
        const float startX = 0.69f;
        float availableWidth = 0.28f;
        float gap = 0.012f;
        float width = (availableWidth - gap * (bars.Length - 1)) / bars.Length;
        for (int i = 0; i < bars.Length; i++)
        {
            GameObject bar = new(
                $"Fuel Signal Bar {i + 1}",
                typeof(RectTransform),
                typeof(Image));
            bar.transform.SetParent(display.transform, false);
            RectTransform rect = (RectTransform)bar.transform;
            float x = startX + i * (width + gap);
            float height = Mathf.Lerp(0.2f, 0.82f, i / (bars.Length - 1f));
            rect.anchorMin = new Vector2(x, 0.09f);
            rect.anchorMax = new Vector2(x + width, 0.09f + height);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            bars[i] = bar.GetComponent<Image>();
            bars[i].color = new Color(
                SignalGreen.r, SignalGreen.g, SignalGreen.b, 0.13f);
        }
    }

    private void SetVisible(bool visible)
    {
        if (canvas != null && canvas.gameObject.activeSelf != visible)
            canvas.gameObject.SetActive(visible);
    }
}
