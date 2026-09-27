using DeFrag.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owner-only presentation while hidden in a locker: vignette, breath meter, key hints,
/// heartbeat / breathing that follow the monster's proximity, and the lurk tension bed.
/// Gameplay state lives in LockerHiding / MonsterLockerHunter; this only reads it.
/// </summary>
public sealed class LockerHidingPresentation : MonoBehaviour
{
    private const float ThreatFarDistance = 13f;
    private const float ThreatNearDistance = 1.8f;
    private const float HintDistance = 5f;

    private LockerLocalSession session;
    private LockerHiding locker;
    private Canvas canvas;
    private RawImage vignette;
    private Image flash;
    private Image breathFill;
    private TMP_Text hint;
    private CanvasGroup hudGroup;
    private AudioSource heartbeat, breathing, tension;
    private float nextBeatAt;
    private float threat;
    private float tensionWeight;
    private float flashAlpha;
    private LockerPhase lastPhase;
    private static Texture2D vignetteTexture;

    public void Begin(LockerLocalSession owner, Camera view)
    {
        session = owner;
        locker = owner.Locker;
        lastPhase = locker.Phase;
        BuildHud();
        heartbeat = CreateSource("Locker Heartbeat", null, false);
        breathing = CreateSource("Locker Breathing", locker.BreathingLoopClip, true);
        tension = CreateSource("Locker Tension", locker.LurkTensionClip, true);
    }

    public void OnGasp()
    {
        flashAlpha = Mathf.Max(flashAlpha, 0.25f);
        FindGlitch()?.PlayFailureBurst(0.45f, 0.35f);
    }

    public void End()
    {
        if (canvas != null) Destroy(canvas.gameObject);
        foreach (AudioSource source in new[] { heartbeat, breathing, tension })
            if (source != null) Destroy(source.gameObject);
    }

    private void Update()
    {
        if (locker == null) return;
        MonsterLockerHunter hunter = NearestHunter(out float distance);
        bool targeted = hunter != null && hunter.TargetLockerId == locker.NetworkObjectId &&
                        hunter.Stage != LockerHuntStage.None;
        float proximity = hunter != null ? Mathf.InverseLerp(ThreatFarDistance, ThreatNearDistance, distance) : 0f;
        float targetThreat = Mathf.Max(proximity, targeted ? 0.45f + 0.55f * hunter.Suspicion01 : 0f);
        threat = Mathf.MoveTowards(threat, targetThreat, Time.deltaTime * 0.8f);

        LockerPhase phase = locker.Phase;
        if (phase == LockerPhase.ForcedOpen && lastPhase != LockerPhase.ForcedOpen)
        {
            flashAlpha = 0.9f;
            FindGlitch()?.PlayFailureBurst(1f, 0.9f);
        }
        lastPhase = phase;

        UpdateAudio(targeted);
        UpdateHud(phase, distance);
    }

    private void UpdateAudio(bool targeted)
    {
        // Heartbeat: one "lub-dub" per beat, faster and louder with the threat.
        if (locker.HeartbeatClip != null && Time.time >= nextBeatAt)
        {
            heartbeat.pitch = Mathf.Lerp(0.95f, 1.1f, threat);
            heartbeat.PlayOneShot(locker.HeartbeatClip, Mathf.Lerp(0.18f, 1f, threat));
            nextBeatAt = Time.time + Mathf.Lerp(1.15f, 0.42f, threat);
        }

        // Own breathing: audible panic while not holding breath, silent while holding.
        float breathTarget = session.IsHoldingBreath ? 0f : Mathf.Lerp(0.12f, 0.55f, threat);
        breathing.volume = Mathf.MoveTowards(breathing.volume, breathTarget, Time.deltaTime * 2.5f);

        tensionWeight = Mathf.MoveTowards(tensionWeight, targeted ? 1f : 0f, Time.deltaTime * (targeted ? 0.8f : 0.4f));
        tension.volume = tensionWeight * 0.8f;
    }

    private void UpdateHud(LockerPhase phase, float monsterDistance)
    {
        bool hidden = phase == LockerPhase.Hidden;
        hudGroup.alpha = Mathf.MoveTowards(hudGroup.alpha, hidden ? 1f : 0f, Time.deltaTime * 3f);

        float breath = session.BreathNormalized;
        breathFill.fillAmount = breath;
        DefragUiTheme theme = RuntimeUi.Theme;
        breathFill.color = session.IsBreathLockedOut ? theme.danger
            : breath < 0.3f ? Color.Lerp(theme.danger, theme.info, breath / 0.3f) : theme.accent;

        float vignetteStrength = Mathf.Lerp(0.55f, 0.8f, threat) + (session.IsHoldingBreath ? 0.15f : 0f);
        Color vignetteColor = vignette.color;
        vignetteColor.a = Mathf.MoveTowards(vignetteColor.a, hidden ? vignetteStrength : 0.4f, Time.deltaTime * 2f);
        vignette.color = vignetteColor;

        flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.deltaTime * 1.6f);
        flash.color = new Color(0.55f, 0f, 0f, flashAlpha);

        bool monsterClose = monsterDistance <= HintDistance;
        string breathKey = session.IsBreathLockedOut
            ? $"<color=#{DefragUiTheme.Hex(theme.danger)}>숨이 차다...</color>"
            : session.IsHoldingBreath
                ? $"<color=#{DefragUiTheme.Hex(theme.info)}>[SPACE] 숨 참는 중</color>"
                : monsterClose && Mathf.PingPong(Time.time * 2.5f, 1f) > 0.35f
                    ? $"<color=#{DefragUiTheme.Hex(theme.danger)}>[SPACE] 숨 참기!</color>"
                    : "[SPACE] 숨 참기";
        hint.text = $"{breathKey}      <color=#{DefragUiTheme.Hex(theme.dim)}>마우스 둘러보기   [E] 나가기</color>";
    }

    private void BuildHud()
    {
        canvas = RuntimeUi.Canvas("Locker Hiding HUD", null, 30500);
        vignette = new GameObject("Vignette", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        vignette.transform.SetParent(canvas.transform, false);
        vignette.texture = vignetteTexture ??= BuildVignetteTexture();
        vignette.color = new Color(0f, 0f, 0f, 0f);
        vignette.raycastTarget = false;
        RuntimeUi.Stretch(vignette.rectTransform);

        flash = RuntimeUi.Panel("Flash", canvas.transform, Color.clear);
        RuntimeUi.Stretch(flash.rectTransform);

        var hudObject = new GameObject("Hud", typeof(RectTransform), typeof(CanvasGroup));
        hudObject.transform.SetParent(canvas.transform, false);
        hudGroup = hudObject.GetComponent<CanvasGroup>();
        hudGroup.alpha = 0f;
        RuntimeUi.Stretch((RectTransform)hudObject.transform);

        Image track = RuntimeUi.Panel("Breath Track", hudObject.transform, new Color(0f, 0f, 0f, 0.55f));
        RuntimeUi.PlaceCentered(track.rectTransform, new Vector2(0.5f, 0.1f), new Vector2(360f, 8f));
        breathFill = RuntimeUi.Panel("Breath Fill", track.transform, RuntimeUi.Theme.accent);
        breathFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
        breathFill.type = Image.Type.Filled;
        breathFill.fillMethod = Image.FillMethod.Horizontal;
        RuntimeUi.Stretch(breathFill.rectTransform);

        hint = RuntimeUi.Text("Hint", hudObject.transform, 26f, TextAlignmentOptions.Center);
        RuntimeUi.PlaceCentered(hint.rectTransform, new Vector2(0.5f, 0.1f), new Vector2(1200f, 40f));
        hint.rectTransform.anchoredPosition = new Vector2(0f, -30f);
        hint.richText = true;
    }

    private AudioSource CreateSource(string name, AudioClip loop, bool looping)
    {
        var sourceObject = new GameObject(name);
        sourceObject.transform.SetParent(transform, false);
        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.playOnAwake = false;
        source.loop = looping;
        source.volume = 0f;
        if (looping && loop != null)
        {
            source.clip = loop;
            source.time = Random.Range(0f, loop.length * 0.5f);
            source.Play();
        }
        return source;
    }

    private MonsterLockerHunter NearestHunter(out float distance)
    {
        distance = float.MaxValue;
        MonsterLockerHunter nearest = null;
        Vector3 position = locker.DoorCenter;
        foreach (MonsterLockerHunter hunter in MonsterLockerHunter.Active)
        {
            if (hunter == null) continue;
            float d = Vector3.Distance(position, hunter.transform.position);
            if (d < distance) { distance = d; nearest = hunter; }
        }
        return nearest;
    }

    private TvMonsterProximityGlitch FindGlitch() => GetComponentInChildren<TvMonsterProximityGlitch>(true);

    private static Texture2D BuildVignetteTexture()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "LockerVignette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (size - 1f) * 2f - 1f, v = y / (size - 1f) * 2f - 1f;
            float r = Mathf.Sqrt(u * u * 0.8f + v * v);
            pixels[y * size + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0.35f, 1.25f, r));
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
