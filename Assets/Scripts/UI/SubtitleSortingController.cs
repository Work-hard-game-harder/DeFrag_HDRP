using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 씬 배치 및 런타임 생성 자막을 Inventory UI보다 위에 표시합니다.
/// Pause/Setting 패널의 최상위 순서(32766/32767)는 침범하지 않습니다.
/// </summary>
public sealed class SubtitleSortingController : MonoBehaviour
{
    private const int SubtitleSortingOrder = 32000;
    private const float SearchInterval = 0.5f;

    private readonly Dictionary<SubtitlesScript, Canvas> subtitleCanvases = new Dictionary<SubtitlesScript, Canvas>();
    private float nextSearchTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (FindAnyObjectByType<SubtitleSortingController>() != null) return;

        GameObject controller = new GameObject(nameof(SubtitleSortingController));
        DontDestroyOnLoad(controller);
        controller.AddComponent<SubtitleSortingController>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextSearchTime) return;

        nextSearchTime = Time.unscaledTime + SearchInterval;
        ConfigureAllSubtitles();
    }

    private void ConfigureAllSubtitles()
    {
        SubtitlesScript[] subtitles = FindObjectsByType<SubtitlesScript>(FindObjectsInactive.Include);
        foreach (SubtitlesScript subtitle in subtitles)
        {
            if (subtitle == null) continue;

            if (!subtitleCanvases.ContainsKey(subtitle))
                subtitleCanvases.Add(subtitle, Configure(subtitle));

            if (subtitle.subtitlesPanel != null && subtitle.subtitlesPanel.activeInHierarchy)
            {
                Canvas canvas = subtitleCanvases[subtitle];
                if (canvas != null)
                {
                    canvas.sortingLayerID = GetHighestSortingLayerId();
                    canvas.sortingOrder = SubtitleSortingOrder;
                }
                subtitle.subtitlesPanel.transform.SetAsLastSibling();
            }
        }

        List<SubtitlesScript> destroyed = null;
        foreach (KeyValuePair<SubtitlesScript, Canvas> entry in subtitleCanvases)
        {
            if (entry.Key != null) continue;
            destroyed ??= new List<SubtitlesScript>();
            destroyed.Add(entry.Key);
        }
        if (destroyed != null)
            foreach (SubtitlesScript subtitle in destroyed)
                subtitleCanvases.Remove(subtitle);
    }

    private static Canvas Configure(SubtitlesScript subtitle)
    {
        GameObject target = subtitle.subtitlesPanel != null
            ? subtitle.subtitlesPanel
            : subtitle.gameObject;

        Canvas sourceCanvas = target.GetComponentInParent<Canvas>();
        CanvasScaler sourceScaler = sourceCanvas != null ? sourceCanvas.GetComponent<CanvasScaler>() : null;

        GameObject overlayObject = new GameObject(
            $"{target.name} Top Overlay",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        overlayObject.layer = target.layer;
        if (sourceCanvas != null)
            overlayObject.transform.SetParent(sourceCanvas.transform.parent, false);

        Canvas canvas = overlayObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingLayerID = GetHighestSortingLayerId();
        canvas.sortingOrder = SubtitleSortingOrder;

        CanvasScaler scaler = overlayObject.GetComponent<CanvasScaler>();
        if (sourceScaler != null)
        {
            scaler.uiScaleMode = sourceScaler.uiScaleMode;
            scaler.referenceResolution = sourceScaler.referenceResolution;
            scaler.screenMatchMode = sourceScaler.screenMatchMode;
            scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
            scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
        }
        else
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        target.transform.SetParent(overlayObject.transform, false);

        Canvas nestedCanvas = target.GetComponent<Canvas>();
        if (nestedCanvas != null)
        {
            nestedCanvas.overrideSorting = false;
            nestedCanvas.sortingOrder = 0;
        }

        return canvas;
    }

    private static int GetHighestSortingLayerId()
    {
        SortingLayer[] layers = SortingLayer.layers;
        int highestId = 0;
        int highestValue = int.MinValue;

        foreach (SortingLayer layer in layers)
        {
            int value = SortingLayer.GetLayerValueFromID(layer.id);
            if (value <= highestValue) continue;
            highestValue = value;
            highestId = layer.id;
        }

        return highestId;
    }
}
