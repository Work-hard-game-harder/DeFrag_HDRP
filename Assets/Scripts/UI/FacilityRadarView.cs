using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[Serializable]
public struct FacilityRadarSettings
{
    [Tooltip("평면도로 그릴 NavMesh 높이 범위(월드 Y). 다른 층이 섞이면 좁히세요.")]
    public Vector2 floorHeightRange;
    public int resolution;
    public float gridSpacing;
    [Tooltip("몬스터 위치가 레이더에 찍히는 주기(초).")]
    public float sonarInterval;
    public float monsterWarningDistance;
    public float decoySampleRadius;

    public static FacilityRadarSettings Default => new()
    {
        floorHeightRange = new Vector2(-3f, 4f),
        resolution = 512,
        gridSpacing = 5f,
        sonarInterval = 3f,
        monsterWarningDistance = 14f,
        decoySampleRadius = 4f
    };
}

public struct RadarMarker
{
    public Vector3 Position;
    public float RadiusMeters;
    public Color Color;
    public string Label;
    public bool Pulse;
}

public interface IFacilityRadarContent
{
    void CollectMarkers(List<RadarMarker> markers, IReadOnlyList<Vector3> playerPositions);
}

// Blueprint radar: players live, monsters by periodic sonar, plus caller-supplied markers.
public sealed class FacilityRadarView : MonoBehaviour, IPointerClickHandler
{
    private static readonly Color SelfColor = new(0.35f, 0.75f, 1f, 1f);
    private static readonly Color PartnerColor = new(0.35f, 1f, 0.45f, 1f);
    private static readonly Color MonsterColor = new(1f, 0.16f, 0.12f, 1f);
    private static readonly Color SweepColor = new(0.3f, 1f, 0.9f, 1f);

    private const float MonsterRefreshSeconds = 2f;

    private FacilityRadarSettings settings;
    private IFacilityRadarContent content;
    private TMP_FontAsset font;
    private FacilityBlueprint blueprint;
    private RectTransform mapRect;
    private RectTransform blipLayer;
    private TMP_Text warningText;
    private AudioSource audioSource;

    private readonly List<Image> blipPool = new();
    private readonly List<TMP_Text> labelPool = new();
    private readonly List<RadarMarker> markers = new();
    private readonly List<Vector3> playerPositions = new();
    private readonly List<Vector3> sonarContacts = new();
    private readonly List<(Vector3 position, Color color, float startTime)> pulses = new();
    private MonsterAI[] monsters = Array.Empty<MonsterAI>();
    private float nextMonsterRefresh;
    private float lastSonarTime = float.NegativeInfinity;
    private Vector3 sonarOrigin;
    private int blipCursor;
    private int labelCursor;

    public event Action<Vector3> MapClicked;
    public bool MonsterNearby { get; private set; }

    public static FacilityRadarView Create(
        RectTransform parent, FacilityRadarSettings settings, TMP_FontAsset font, IFacilityRadarContent content)
    {
        GameObject root = new("Facility Radar", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(parent, false);
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = new Color(0f, 0.015f, 0.02f, 0.95f);

        FacilityRadarView view = root.AddComponent<FacilityRadarView>();
        view.settings = settings;
        view.font = font;
        view.content = content;
        view.Build(rootRect);
        return view;
    }

    public void ShowPulse(Vector3 worldPosition, Color color) =>
        pulses.Add((worldPosition, color, Time.unscaledTime));

    private void Build(RectTransform root)
    {
        blueprint = FacilityBlueprint.GetOrBuild(settings.floorHeightRange, settings.resolution, settings.gridSpacing);

        GameObject map = new("Blueprint", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        map.transform.SetParent(root, false);
        mapRect = (RectTransform)map.transform;
        mapRect.anchorMin = new Vector2(0.02f, 0.02f);
        mapRect.anchorMax = new Vector2(0.98f, 0.98f);
        mapRect.offsetMin = mapRect.offsetMax = Vector2.zero;
        RawImage raw = map.GetComponent<RawImage>();
        raw.texture = blueprint.Texture;
        AspectRatioFitter fitter = map.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 1f;

        GameObject layer = new("Blips", typeof(RectTransform));
        layer.transform.SetParent(mapRect, false);
        blipLayer = (RectTransform)layer.transform;
        blipLayer.anchorMin = Vector2.zero;
        blipLayer.anchorMax = Vector2.one;
        blipLayer.offsetMin = blipLayer.offsetMax = Vector2.zero;

        warningText = CreateLabel(root, 26f);
        RectTransform warningRect = warningText.rectTransform;
        warningRect.anchorMin = new Vector2(0.03f, 0.9f);
        warningRect.anchorMax = new Vector2(0.97f, 0.985f);
        warningRect.offsetMin = warningRect.offsetMax = Vector2.zero;
        warningText.alignment = TextAlignmentOptions.Center;
        warningText.color = MonsterColor;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
    }

    private void Update()
    {
        blipCursor = 0;
        labelCursor = 0;

        CollectPlayers(out Vector3? localPosition);
        UpdateSonar(localPosition);

        markers.Clear();
        content?.CollectMarkers(markers, playerPositions);
        foreach (RadarMarker marker in markers)
            DrawMarker(marker);

        DrawSonar();
        DrawPulses();
        HideUnused();
    }

    private void CollectPlayers(out Vector3? localPosition)
    {
        localPosition = null;
        playerPositions.Clear();
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.SpawnManager == null)
            return;

        foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjectsList)
        {
            if (networkObject == null || !networkObject.IsPlayerObject)
                continue;
            Transform body = networkObject.transform;
            bool isLocal = networkObject.OwnerClientId == manager.LocalClientId;
            playerPositions.Add(body.position);
            if (isLocal)
                localPosition = body.position;

            Image arrow = NextBlip(RuntimeUiSprites.Arrow, isLocal ? SelfColor : PartnerColor, isLocal ? 22f : 30f);
            Place(arrow.rectTransform, body.position);
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -body.eulerAngles.y);
            TMP_Text label = NextLabel(isLocal ? "나" : "동료", isLocal ? SelfColor : PartnerColor);
            Place(label.rectTransform, body.position);
            label.rectTransform.anchoredPosition = new Vector2(0f, 24f);
        }
    }

    private void UpdateSonar(Vector3? localPosition)
    {
        if (Time.unscaledTime >= nextMonsterRefresh)
        {
            monsters = FindObjectsByType<MonsterAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            nextMonsterRefresh = Time.unscaledTime + MonsterRefreshSeconds;
        }

        if (Time.unscaledTime - lastSonarTime < settings.sonarInterval)
            return;

        lastSonarTime = Time.unscaledTime;
        sonarOrigin = localPosition ?? Vector3.zero;
        sonarContacts.Clear();
        MonsterNearby = false;
        foreach (MonsterAI monster in monsters)
        {
            if (monster == null) continue;
            Vector3 position = monster.transform.position;
            sonarContacts.Add(position);
            foreach (Vector3 player in playerPositions)
                if ((player - position).sqrMagnitude <= settings.monsterWarningDistance * settings.monsterWarningDistance)
                    MonsterNearby = true;
        }

        audioSource.PlayOneShot(ProceduralSfx.RadarPing, 0.5f);
        if (MonsterNearby)
            audioSource.PlayOneShot(ProceduralSfx.WarningBeep, 0.8f);
    }

    private void DrawSonar()
    {
        float age = Time.unscaledTime - lastSonarTime;
        float fade = 1f - Mathf.Clamp01(age / Mathf.Max(0.1f, settings.sonarInterval)) * 0.8f;

        float sweepRadius = age * 60f;
        if (age < 1.2f)
        {
            Image sweep = NextBlip(RuntimeUiSprites.Ring, WithAlpha(SweepColor, 0.35f * (1f - age / 1.2f)), 0f);
            Place(sweep.rectTransform, sonarOrigin);
            SetWorldSize(sweep.rectTransform, sweepRadius * 2f);
        }

        foreach (Vector3 contact in sonarContacts)
        {
            Image glow = NextBlip(RuntimeUiSprites.SoftGlow, WithAlpha(MonsterColor, 0.55f * fade), 54f);
            Place(glow.rectTransform, contact);
            Image dot = NextBlip(RuntimeUiSprites.Disc, WithAlpha(MonsterColor, fade), 18f);
            Place(dot.rectTransform, contact);
            TMP_Text label = NextLabel("괴물", WithAlpha(MonsterColor, fade));
            Place(label.rectTransform, contact);
            label.rectTransform.anchoredPosition = new Vector2(0f, -24f);
        }

        bool flash = Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.4f;
        warningText.text = MonsterNearby && flash ? "[!] 괴물 근접! 동료에게 알리세요 [!]" : string.Empty;
    }

    private void DrawMarker(RadarMarker marker)
    {
        float pulse = marker.Pulse ? 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 5f) : 1f;
        if (marker.RadiusMeters > 0f)
        {
            Image zone = NextBlip(RuntimeUiSprites.SoftGlow, WithAlpha(marker.Color, 0.35f * pulse), 0f);
            Place(zone.rectTransform, marker.Position);
            SetWorldSize(zone.rectTransform, marker.RadiusMeters * 2f);
            Image edge = NextBlip(RuntimeUiSprites.Ring, WithAlpha(marker.Color, 0.6f * pulse), 0f);
            Place(edge.rectTransform, marker.Position);
            SetWorldSize(edge.rectTransform, marker.RadiusMeters * 2f);
        }
        else
        {
            Image dot = NextBlip(RuntimeUiSprites.Disc, WithAlpha(marker.Color, pulse), 16f);
            Place(dot.rectTransform, marker.Position);
        }

        if (!string.IsNullOrEmpty(marker.Label))
        {
            TMP_Text label = NextLabel(marker.Label, marker.Color);
            Place(label.rectTransform, marker.Position);
        }
    }

    private void DrawPulses()
    {
        for (int i = pulses.Count - 1; i >= 0; i--)
        {
            float age = Time.unscaledTime - pulses[i].startTime;
            if (age > 2.5f)
            {
                pulses.RemoveAt(i);
                continue;
            }
            float t = age / 2.5f;
            Image ring = NextBlip(RuntimeUiSprites.Ring, WithAlpha(pulses[i].color, 1f - t), 0f);
            Place(ring.rectTransform, pulses[i].position);
            SetWorldSize(ring.rectTransform, Mathf.Lerp(4f, 40f, t));
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                mapRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            return;

        Rect rect = mapRect.rect;
        Vector2 uv = new((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
        if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
            return;

        float height = playerPositions.Count > 0 ? playerPositions[0].y : 0f;
        MapClicked?.Invoke(blueprint.UvToWorld(uv, height));
    }

    private Image NextBlip(Sprite sprite, Color color, float pixelSize)
    {
        if (blipCursor >= blipPool.Count)
        {
            GameObject blip = new("Blip", typeof(RectTransform), typeof(Image));
            blip.transform.SetParent(blipLayer, false);
            Image created = blip.GetComponent<Image>();
            created.raycastTarget = false;
            blipPool.Add(created);
        }

        Image image = blipPool[blipCursor++];
        image.gameObject.SetActive(true);
        image.sprite = sprite;
        image.color = color;
        image.rectTransform.localRotation = Quaternion.identity;
        image.rectTransform.sizeDelta = new Vector2(pixelSize, pixelSize);
        image.transform.SetAsLastSibling();
        return image;
    }

    private TMP_Text NextLabel(string text, Color color)
    {
        if (labelCursor >= labelPool.Count)
        {
            TMP_Text created = CreateLabel(blipLayer, 17f);
            created.alignment = TextAlignmentOptions.Center;
            created.rectTransform.sizeDelta = new Vector2(160f, 26f);
            labelPool.Add(created);
        }

        TMP_Text label = labelPool[labelCursor++];
        label.gameObject.SetActive(true);
        label.text = text;
        label.color = color;
        label.transform.SetAsLastSibling();
        return label;
    }

    private void HideUnused()
    {
        for (int i = blipCursor; i < blipPool.Count; i++)
            blipPool[i].gameObject.SetActive(false);
        for (int i = labelCursor; i < labelPool.Count; i++)
            labelPool[i].gameObject.SetActive(false);
    }

    private void Place(RectTransform target, Vector3 world)
    {
        Vector2 uv = blueprint.WorldToUv(world);
        target.anchorMin = target.anchorMax = uv;
        target.anchoredPosition = Vector2.zero;
    }

    private void SetWorldSize(RectTransform target, float meters)
    {
        float pixels = blueprint.WorldToUvDistance(meters) * mapRect.rect.width;
        target.sizeDelta = new Vector2(pixels, pixels);
    }

    private TMP_Text CreateLabel(Transform parent, float size)
    {
        GameObject labelObject = new("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(parent, false);
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        if (font != null)
            label.font = font;
        label.fontSize = size;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }

    private static Color WithAlpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);
}
