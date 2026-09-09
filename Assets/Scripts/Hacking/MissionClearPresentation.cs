using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local-only presentation started on every client by the synchronized terminal completion.</summary>
public sealed class MissionClearPresentation : MonoBehaviour
{
    private static MissionClearPresentation instance;
    private RectTransform banner;
    private CanvasGroup canvasGroup;
    private Coroutine animationRoutine;

    public static void Show()
    {
        if (instance == null)
        {
            GameObject root = new("Mission Clear Presentation");
            instance = root.AddComponent<MissionClearPresentation>();
        }
        instance.Play();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        Build();
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>().enabled = false;

        GameObject panel = new("Banner", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        panel.transform.SetParent(transform, false);
        banner = (RectTransform)panel.transform;
        banner.anchorMin = banner.anchorMax = new Vector2(0.5f, 0.5f);
        banner.sizeDelta = new Vector2(1120f, 170f);
        banner.localRotation = Quaternion.Euler(0f, 0f, -7f);
        panel.GetComponent<Image>().color = new Color(0.01f, 0.08f, 0.07f, 0.94f);
        canvasGroup = panel.GetComponent<CanvasGroup>();

        GameObject accent = new("Accent", typeof(RectTransform), typeof(Image));
        accent.transform.SetParent(panel.transform, false);
        RectTransform accentRect = (RectTransform)accent.transform;
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0.025f, 1f);
        accentRect.offsetMin = accentRect.offsetMax = Vector2.zero;
        accent.GetComponent<Image>().color = new Color(0.15f, 1f, 0.55f, 1f);

        GameObject labelObject = new("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(panel.transform, false);
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.text = "MISSION CLEAR";
        label.fontSize = 82f;
        label.fontStyle = FontStyles.Bold | FontStyles.Italic;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.55f, 1f, 0.78f);
        label.characterSpacing = 8f;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(45f, 0f);
        labelRect.offsetMax = new Vector2(-20f, 0f);
        panel.SetActive(false);
    }

    private void Play()
    {
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        banner.gameObject.SetActive(true);
        canvasGroup.alpha = 1f;
        yield return Move(new Vector2(-1550f, 0f), Vector2.zero, 0.42f, false);
        yield return new WaitForSecondsRealtime(1.1f);
        yield return Move(Vector2.zero, new Vector2(1550f, 0f), 0.48f, true);
        banner.gameObject.SetActive(false);
        animationRoutine = null;
    }

    private IEnumerator Move(Vector2 from, Vector2 to, float duration, bool fade)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            banner.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
            if (fade) canvasGroup.alpha = 1f - t;
            yield return null;
        }
        banner.anchoredPosition = to;
    }
}
