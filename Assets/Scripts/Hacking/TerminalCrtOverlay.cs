using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen CRT power-on / power-off effect used when the operator dives into or out of a
/// terminal. Local presentation only; sits above the terminal session canvas.
/// </summary>
public sealed class TerminalCrtOverlay
{
    private static readonly Color Beam = new(0.75f, 1f, 0.92f, 1f);
    private readonly Canvas canvas;
    private readonly CanvasGroup group;
    private readonly RectTransform top, bottom, line;
    private readonly Image flash, lineImage;

    private TerminalCrtOverlay()
    {
        canvas = RuntimeUi.Canvas("Terminal CRT Transition", null, 400);
        canvas.GetComponent<GraphicRaycaster>().enabled = false;
        group = canvas.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        top = Bar("Top", new Vector2(0f, 1f));
        bottom = Bar("Bottom", new Vector2(0f, 0f));
        flash = RuntimeUi.Panel("Flash", canvas.transform, Color.clear);
        RuntimeUi.Stretch(flash.rectTransform);
        lineImage = RuntimeUi.Panel("Beam", canvas.transform, Beam);
        line = lineImage.rectTransform;
        line.anchorMin = new Vector2(0f, 0.5f);
        line.anchorMax = new Vector2(1f, 0.5f);
        line.sizeDelta = new Vector2(0f, 6f);
        RuntimeUi.Scanlines(canvas.transform, 0.18f);
        SetCover(0f);
        lineImage.color = Color.clear;
    }

    public static TerminalCrtOverlay Create() => new();

    /// <summary>From a black screen, a bright beam opens vertically to reveal what is behind.</summary>
    public IEnumerator PowerOn(float duration)
    {
        group.alpha = 1f;
        line.anchorMin = new Vector2(0f, 0.5f);
        line.anchorMax = new Vector2(1f, 0.5f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            float open = 1f - Mathf.Pow(1f - t, 3f);
            SetCover(1f - open);
            line.sizeDelta = new Vector2(0f, Mathf.Lerp(6f, 1080f, open));
            lineImage.color = new Color(Beam.r, Beam.g, Beam.b, Mathf.Lerp(0.95f, 0f, open));
            flash.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 0f, t));
            yield return null;
        }
        SetCover(0f);
        lineImage.color = Color.clear;
        flash.color = Color.clear;
    }

    /// <summary>The picture collapses to a horizontal beam, then to a dot, leaving black.</summary>
    public IEnumerator PowerOff(float duration)
    {
        group.alpha = 1f;
        float squash = duration * 0.6f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / squash)
        {
            float e = t * t;
            SetCover(e);
            line.sizeDelta = new Vector2(0f, Mathf.Lerp(1080f, 6f, e));
            lineImage.color = new Color(Beam.r, Beam.g, Beam.b, Mathf.Lerp(0.2f, 1f, e));
            yield return null;
        }
        SetCover(1f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / (duration - squash))
        {
            line.anchorMin = new Vector2(Mathf.Lerp(0f, 0.498f, t * t), 0.5f);
            line.anchorMax = new Vector2(Mathf.Lerp(1f, 0.502f, t * t), 0.5f);
            lineImage.color = new Color(Beam.r, Beam.g, Beam.b, 1f - t * t);
            yield return null;
        }
        lineImage.color = Color.clear;
    }

    public IEnumerator FadeOut(float duration)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            group.alpha = 1f - t;
            yield return null;
        }
        group.alpha = 0f;
    }

    public void Destroy()
    {
        if (canvas != null) Object.Destroy(canvas.gameObject);
    }

    private RectTransform Bar(string name, Vector2 anchor)
    {
        Image image = RuntimeUi.Panel(name, canvas.transform, Color.black);
        return image.rectTransform;
    }

    /// <param name="cover">0 = fully open, 1 = the two black halves meet in the middle.</param>
    private void SetCover(float cover)
    {
        float half = Mathf.Clamp01(cover) * 0.5f;
        top.anchorMin = new Vector2(0f, 1f - half - (cover >= 1f ? 0.002f : 0f));
        top.anchorMax = Vector2.one;
        bottom.anchorMin = Vector2.zero;
        bottom.anchorMax = new Vector2(1f, half + (cover >= 1f ? 0.002f : 0f));
        top.offsetMin = top.offsetMax = bottom.offsetMin = bottom.offsetMax = Vector2.zero;
    }
}
