using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CameraShutterFlash : MonoBehaviour
{
    [Header("Flash")]
    [SerializeField] private Color flashColor = new(1f, 1f, 1f, 0.9f);
    [SerializeField, Min(0f)] private float attackDuration = 0.025f;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.14f;

    private Canvas overlayCanvas;
    private Image flashImage;
    private Coroutine flashRoutine;

    public void Initialize(Canvas canvas)
    {
        if (overlayCanvas == canvas && flashImage != null)
            return;

        overlayCanvas = canvas;
        EnsureOverlay();
    }

    public void Play()
    {
        if (overlayCanvas == null || !overlayCanvas.enabled)
            return;

        EnsureOverlay();
        if (flashImage == null)
            return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(Flash());
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas == null || flashImage != null)
            return;

        GameObject overlay = new("Shutter Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlay.layer = overlayCanvas.gameObject.layer;
        overlay.transform.SetParent(overlayCanvas.transform, false);

        RectTransform rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();

        flashImage = overlay.GetComponent<Image>();
        flashImage.raycastTarget = false;
        flashImage.color = WithAlpha(flashColor, 0f);
    }

    private IEnumerator Flash()
    {
        flashImage.transform.SetAsLastSibling();

        if (attackDuration > 0f)
        {
            for (float elapsed = 0f; elapsed < attackDuration; elapsed += Time.unscaledDeltaTime)
            {
                float alpha = Mathf.Lerp(0f, flashColor.a, elapsed / attackDuration);
                flashImage.color = WithAlpha(flashColor, alpha);
                yield return null;
            }
        }

        flashImage.color = flashColor;

        for (float elapsed = 0f; elapsed < fadeDuration; elapsed += Time.unscaledDeltaTime)
        {
            float alpha = Mathf.Lerp(flashColor.a, 0f, elapsed / fadeDuration);
            flashImage.color = WithAlpha(flashColor, alpha);
            yield return null;
        }

        flashImage.color = WithAlpha(flashColor, 0f);
        flashRoutine = null;
    }

    private void OnDisable()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        if (flashImage != null)
            flashImage.color = WithAlpha(flashColor, 0f);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
