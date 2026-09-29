using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Glowing screen on the hacking pad model. The local player's first-person pad shows a live
/// "H-PAD OS" (nearest terminal, signal, objective, monster interference); every other copy
/// (on the floor, in a teammate's hand) shows a shared standby screen. Presentation only.
/// </summary>
[DisallowMultipleComponent]
public sealed class HackingPadScreen : MonoBehaviour
{
    // Screen rectangle measured on the "hackingpad" mesh (mesh-local units, +Y faces the user, +Z = antenna edge).
    [SerializeField] private Vector3 screenCenter = new(0.0134f, 0.0131f, -0.0284f);
    [SerializeField] private Vector2 screenSize = new(0.150f, 0.101f);
    [SerializeField] private Color glowTint = new(0.35f, 1f, 0.85f);
    [SerializeField, Min(0f)] private float firstPersonGlow = 3.2f;
    [SerializeField, Min(0f)] private float ambientGlow = 1.6f;
    [SerializeField, Min(0f)] private float lightIntensity = 30f;
    [SerializeField, Min(0.1f)] private float lightRange = 1.4f;
    [Tooltip("이 거리 안이면 터미널 연결 가능으로 표시합니다 (PlayerInteraction 사거리와 맞춤).")]
    [SerializeField, Min(0.5f)] private float linkRange = 6f;
    [SerializeField, Min(1f)] private float scanRange = 60f;
    [SerializeField, Min(1f)] private float interferenceRange = 14f;

    private MeshRenderer host;
    private MeshRenderer screen;
    private Light glow;
    private Material material;
    private bool firstPerson;
    private PadOsView liveView;

    private void Start()
    {
        host = GetComponent<MeshRenderer>();
        firstPerson = GetComponentInParent<EquipmentController>() != null;

        Texture texture;
        if (firstPerson)
        {
            liveView = new PadOsView(linkRange, scanRange, interferenceRange);
            texture = liveView.Texture;
        }
        else
        {
            texture = PadStandbyView.Acquire();
        }

        screen = DeviceScreenQuad.Create(transform, "H-PAD Screen", screenCenter, Vector3.up, Vector3.forward, screenSize, texture);
        material = screen.sharedMaterial;
        DeviceScreenQuad.SetGlow(material, glowTint, firstPerson ? firstPersonGlow : ambientGlow);

        var lightObject = new GameObject("H-PAD Glow");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = screenCenter + Vector3.up * 0.05f;
        glow = lightObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = glowTint;
        glow.lightUnit = LightUnit.Lumen;
        glow.intensity = lightIntensity;
        glow.range = lightRange;
        glow.shadows = LightShadows.None;
    }

    private void LateUpdate()
    {
        if (screen == null) return;
        // NetworkWorldItem hides the pad by disabling its own renderer; follow it.
        bool visible = host == null || host.enabled;
        if (screen.enabled != visible) screen.enabled = visible;
        if (glow.enabled != visible) glow.enabled = visible;
        if (!visible) return;

        float pulse = 1f;
        if (liveView != null)
        {
            liveView.Update(transform.position);
            pulse = liveView.GlowMultiplier;
        }
        else
        {
            PadStandbyView.Tick();
            pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.7f);
        }

        DeviceScreenQuad.SetGlow(material, glowTint, (firstPerson ? firstPersonGlow : ambientGlow) * pulse);
        glow.intensity = lightIntensity * pulse;
    }

    private void OnDestroy()
    {
        liveView?.Dispose();
        if (!firstPerson && screen != null) PadStandbyView.Release();
        if (material != null) Destroy(material);
    }

    // ─────────────────────────── Live first-person OS ───────────────────────────

    private sealed class PadOsView
    {
        private const int BarCount = 28;
        private readonly OffscreenUiSurface surface;
        private readonly float linkRange, scanRange, interferenceRange;
        private readonly TMP_Text clock, nodeName, nodeMeta, status, hint, objective, warning;
        private readonly Image[] signalBars = new Image[5];
        private readonly Image[] wave = new Image[BarCount];
        private readonly RectTransform content;
        private readonly Image warningBand;
        private readonly List<MonsterAI> monsters = new();
        private float nextMonsterScan;
        private float interference;
        private bool wasInRange;

        public Texture Texture => surface.Texture;
        public float GlowMultiplier { get; private set; } = 1f;

        public PadOsView(float linkRange, float scanRange, float interferenceRange)
        {
            this.linkRange = linkRange;
            this.scanRange = scanRange;
            this.interferenceRange = interferenceRange;
            surface = new OffscreenUiSurface("H-PAD OS", new Vector2Int(640, 430), 12f);
            DefragUiTheme theme = RuntimeUi.Theme;
            Transform root = surface.Root;

            Image background = RuntimeUi.Panel("Background", root, new Color(0.006f, 0.035f, 0.04f, 1f));
            RuntimeUi.Stretch(background.rectTransform);
            content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(root, false);
            RuntimeUi.Stretch(content);

            // Header
            Image header = RuntimeUi.Panel("Header", content, new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.12f));
            RuntimeUi.Place(header.rectTransform, new Vector2(0f, 0.86f), new Vector2(1f, 1f));
            TMP_Text brand = Text("Brand", header.transform, 26, TextAlignmentOptions.MidlineLeft, theme.highlight);
            RuntimeUi.Stretch(brand.rectTransform, 14f);
            brand.text = $"H-PAD <color=#{DefragUiTheme.Hex(theme.dim)}>// DEFRAG OS 2.1</color>";
            clock = Text("Clock", header.transform, 24, TextAlignmentOptions.MidlineRight, theme.text);
            RuntimeUi.Stretch(clock.rectTransform, 14f);

            // Node block
            TMP_Text label = Text("Label", content, 18, TextAlignmentOptions.TopLeft, theme.dim);
            RuntimeUi.Place(label.rectTransform, new Vector2(0.05f, 0.72f), new Vector2(0.7f, 0.83f));
            label.text = "NEAREST NODE";
            nodeName = Text("Node", content, 44, TextAlignmentOptions.TopLeft, theme.highlight);
            RuntimeUi.Place(nodeName.rectTransform, new Vector2(0.05f, 0.56f), new Vector2(0.75f, 0.74f));
            nodeMeta = Text("Meta", content, 22, TextAlignmentOptions.TopLeft, theme.text);
            RuntimeUi.Place(nodeMeta.rectTransform, new Vector2(0.05f, 0.47f), new Vector2(0.75f, 0.57f));

            for (int i = 0; i < signalBars.Length; i++)
            {
                Image bar = RuntimeUi.Panel($"Signal {i}", content, theme.dim);
                RectTransform rect = bar.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.8f + i * 0.032f, 0.6f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(12f, 14f + i * 12f);
                signalBars[i] = bar;
            }

            // Scan waveform
            for (int i = 0; i < BarCount; i++)
            {
                Image bar = RuntimeUi.Panel($"Wave {i}", content, theme.accent);
                RectTransform rect = bar.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.05f + i * (0.9f / BarCount), 0.37f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(12f, 6f);
                wave[i] = bar;
            }

            status = Text("Status", content, 30, TextAlignmentOptions.MidlineLeft, theme.accent);
            RuntimeUi.Place(status.rectTransform, new Vector2(0.05f, 0.19f), new Vector2(0.95f, 0.3f));
            hint = Text("Hint", content, 19, TextAlignmentOptions.MidlineLeft, theme.dim);
            RuntimeUi.Place(hint.rectTransform, new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.2f));

            Image divider = RuntimeUi.Panel("Divider", content, new Color(theme.edge.r, theme.edge.g, theme.edge.b, 0.35f));
            RuntimeUi.Place(divider.rectTransform, new Vector2(0.04f, 0.105f), new Vector2(0.96f, 0.11f));
            objective = Text("Objective", content, 20, TextAlignmentOptions.MidlineLeft, theme.text);
            RuntimeUi.Place(objective.rectTransform, new Vector2(0.05f, 0.01f), new Vector2(0.95f, 0.1f));
            objective.overflowMode = TextOverflowModes.Ellipsis;

            warningBand = RuntimeUi.Panel("Warning Band", root, new Color(theme.danger.r, theme.danger.g, theme.danger.b, 0f));
            RuntimeUi.Place(warningBand.rectTransform, new Vector2(0f, 0.42f), new Vector2(1f, 0.58f));
            warning = Text("Warning", warningBand.transform, 30, TextAlignmentOptions.Center, theme.highlight);
            RuntimeUi.Stretch(warning.rectTransform);
            warning.text = "!! SIGNAL INTERFERENCE !!";

            RuntimeUi.Scanlines(root, 0.12f);
            surface.Tick(true);
        }

        public void Update(Vector3 padPosition)
        {
            if (!surface.Tick()) return;
            DefragUiTheme theme = RuntimeUi.Theme;
            float time = Time.time;

            // Nearest terminal
            ConnectionDevice nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (ConnectionDevice device in ConnectionDevice.All)
            {
                if (device == null || !device.isActiveAndEnabled) continue;
                Collider body = device.GetComponent<Collider>();
                Vector3 point = body != null ? body.ClosestPoint(padPosition) : device.transform.position;
                float distance = Vector3.Distance(padPosition, point);
                if (distance < nearestDistance) { nearestDistance = distance; nearest = device; }
            }

            bool found = nearest != null && nearestDistance <= scanRange;
            float strength = found ? Mathf.Clamp01(1f - nearestDistance / scanRange) : 0f;
            bool inRange = found && nearestDistance <= linkRange;
            if (inRange && !wasInRange) UiSfx.Play(UiCue.LockAcquired, 0.35f);
            wasInRange = inRange;

            clock.text = $"{(int)(time / 60f) % 60:00}:{(int)time % 60:00}  <color=#{DefragUiTheme.Hex(theme.accent)}>PWR ████</color>";
            nodeName.text = found ? nearest.DisplayName : "NO NODE";
            nodeMeta.text = found ? $"DIST {nearestDistance:00.0}m   CH-{Mathf.Abs(nearest.TerminalId.GetHashCode()) % 90 + 10}" : "SCANNING FACILITY BAND";
            int lit = Mathf.CeilToInt(strength * signalBars.Length);
            for (int i = 0; i < signalBars.Length; i++)
                signalBars[i].color = i < lit ? (inRange ? theme.highlight : theme.accent) : new Color(theme.dim.r, theme.dim.g, theme.dim.b, 0.35f);

            for (int i = 0; i < wave.Length; i++)
            {
                float n = Mathf.PerlinNoise(i * 0.35f, time * (1.5f + strength * 4f));
                float height = 4f + n * (8f + strength * 70f) * (0.4f + 0.6f * Mathf.Abs(Mathf.Sin(i * 0.4f + time * 3f)));
                wave[i].rectTransform.sizeDelta = new Vector2(12f, height);
                wave[i].color = inRange ? theme.highlight : theme.accent;
            }

            if (inRange)
            {
                bool on = Mathf.PingPong(time * 2f, 1f) > 0.3f;
                status.text = on ? "● LINK READY" : "  LINK READY";
                status.color = theme.highlight;
                hint.text = "패드를 들고(우클릭) 터미널을 향해 E 길게";
            }
            else
            {
                status.text = "SEARCHING" + new string('.', 1 + (int)(time * 2f) % 3);
                status.color = theme.accent;
                hint.text = found ? "신호가 강해지는 쪽으로 이동" : "";
            }

            QuestManager quests = QuestManager.Instance;
            string title = quests != null && !quests.IsWaitingForSubtitleReveal && quests.CurrentStep != null
                ? quests.CurrentStep.questTitle
                : "--";
            bool cursor = Mathf.PingPong(time * 2.2f, 1f) > 0.5f;
            objective.text = $"<color=#{DefragUiTheme.Hex(theme.dim)}>OBJ ></color> {title}{(cursor ? "█" : "")}";

            // Monster interference: glitch offset, red band, flickering glow.
            float targetInterference = NearestMonsterFactor(padPosition);
            interference = Mathf.MoveTowards(interference, targetInterference, 0.35f);
            bool glitch = interference > 0.05f && Random.value < interference;
            content.anchoredPosition = glitch ? new Vector2(Random.Range(-18f, 18f) * interference, Random.Range(-6f, 6f)) : Vector2.zero;
            Color band = warningBand.color;
            band.a = interference > 0.35f && Mathf.PingPong(time * 3f, 1f) > 0.4f ? 0.55f * interference : 0f;
            warningBand.color = band;
            warning.gameObject.SetActive(band.a > 0f);

            GlowMultiplier = (inRange ? 1.15f + 0.25f * Mathf.Sin(time * 6f) : 1f) *
                             (glitch ? Random.Range(0.25f, 1.1f) : 1f);
        }

        private float NearestMonsterFactor(Vector3 position)
        {
            if (Time.unscaledTime >= nextMonsterScan)
            {
                nextMonsterScan = Time.unscaledTime + 1.5f;
                monsters.Clear();
                monsters.AddRange(Object.FindObjectsByType<MonsterAI>(FindObjectsInactive.Exclude));
            }

            float strongest = 0f;
            foreach (MonsterAI monster in monsters)
            {
                if (monster == null) continue;
                float distance = Vector3.Distance(position, monster.transform.position);
                strongest = Mathf.Max(strongest, Mathf.InverseLerp(interferenceRange, interferenceRange * 0.25f, distance));
            }
            return strongest;
        }

        private static TMP_Text Text(string name, Transform parent, float size, TextAlignmentOptions alignment, Color color)
        {
            TMP_Text text = RuntimeUi.Text(name, parent, size, alignment, null, color);
            text.richText = true;
            return text;
        }

        public void Dispose() => surface.Dispose();
    }

    // ─────────────────────────── Shared standby screen ───────────────────────────

    private static class PadStandbyView
    {
        private static OffscreenUiSurface surface;
        private static TMP_Text pulse;
        private static int users;

        public static Texture Acquire()
        {
            users++;
            if (surface != null) return surface.Texture;
            DefragUiTheme theme = RuntimeUi.Theme;
            surface = new OffscreenUiSurface("H-PAD Standby", new Vector2Int(320, 216), 3f);
            Image background = RuntimeUi.Panel("Background", surface.Root, new Color(0.006f, 0.035f, 0.04f, 1f));
            RuntimeUi.Stretch(background.rectTransform);
            TMP_Text logo = RuntimeUi.Text("Logo", surface.Root, 40, TextAlignmentOptions.Center, null, theme.highlight);
            RuntimeUi.Place(logo.rectTransform, new Vector2(0f, 0.45f), new Vector2(1f, 0.8f));
            logo.text = "H-PAD";
            pulse = RuntimeUi.Text("Standby", surface.Root, 18, TextAlignmentOptions.Center, null, theme.accent);
            RuntimeUi.Place(pulse.rectTransform, new Vector2(0f, 0.2f), new Vector2(1f, 0.42f));
            RuntimeUi.Scanlines(surface.Root, 0.12f);
            surface.Tick(true);
            return surface.Texture;
        }

        public static void Tick()
        {
            if (surface == null || !surface.Tick()) return;
            pulse.text = ((int)(Time.time * 1.5f) % 2 == 0) ? "● STANDBY" : "  STANDBY";
        }

        public static void Release()
        {
            if (--users > 0 || surface == null) return;
            surface.Dispose();
            surface = null;
        }
    }
}
