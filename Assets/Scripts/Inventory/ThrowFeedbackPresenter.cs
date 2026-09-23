using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>Short local-player feedback that does not acquire the cutscene/input lock.</summary>
public sealed class ThrowFeedbackPresenter : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float displayDuration = 1.6f;
    private TextMeshProUGUI label;
    private Coroutine hideRoutine;

    public void Show(string message)
    {
        EnsureLabel();
        label.text = message;
        label.gameObject.SetActive(true);
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private void EnsureLabel()
    {
        if (label != null) return;

        GameObject canvasObject = new("Throw Feedback Canvas", typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        Canvas feedbackCanvas = canvasObject.GetComponent<Canvas>();
        feedbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        feedbackCanvas.sortingOrder = 31990;

        GameObject textObject = new("Throw Feedback", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(canvasObject.transform, false);
        label = textObject.GetComponent<TextMeshProUGUI>();
        // The regular subtitle font is known to contain the Korean glyphs used here.
        // Do not copy an arbitrary TMP label (many HUD fonts are Latin-only).
        SubtitlesScript subtitles = FindAnyObjectByType<SubtitlesScript>(FindObjectsInactive.Include);
        if (subtitles != null && subtitles.subtitlesText != null && subtitles.subtitlesText.font != null)
        {
            label.font = subtitles.subtitlesText.font;
            label.fontSharedMaterial = subtitles.subtitlesText.fontSharedMaterial;
        }
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 30f;
        label.color = Color.white;
        label.raycastTarget = false;
        label.outlineWidth = 0.18f;

        RectTransform rect = label.rectTransform;
        rect.anchorMin = new Vector2(0.2f, 0.15f);
        rect.anchorMax = new Vector2(0.8f, 0.25f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        label.gameObject.SetActive(false);
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSecondsRealtime(displayDuration);
        if (label != null) label.gameObject.SetActive(false);
        hideRoutine = null;
    }
}
