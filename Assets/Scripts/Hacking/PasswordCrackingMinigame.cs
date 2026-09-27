using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Short osu-style rhythm check used by the Unlock Door command.
/// It owns only local presentation/input; the terminal reports the final result
/// through HackingMinigameBase so shared door state remains server-authoritative.
/// </summary>
public sealed class PasswordCrackingMinigame : HackingMinigameBase
{
    private static readonly char[] LaneKeys = { 'A', 'S', 'D', 'F' };
    private static readonly float[] LanePitches = { 1f, 1.26f, 1.5f, 2f };

    [Header("Rhythm Rules")]
    [SerializeField, Min(1)] private int requiredHits = 4;
    [SerializeField, Min(1)] private int allowedMisses = 5;
    [SerializeField] private Vector2 approachDurationRange = new(1.3f, 1.7f);
    [SerializeField] private Vector2 noteGapRange = new(0.35f, 0.6f);
    [SerializeField, Range(0.03f, 0.3f)] private float goodScaleTolerance = 0.17f;
    [SerializeField, Range(0.01f, 0.15f)] private float perfectScaleTolerance = 0.06f;
    [SerializeField, Min(1.1f)] private float approachStartScale = 2.35f;

    [Header("Presentation")]
    [SerializeField] private TMP_FontAsset terminalFont;

    private RectTransform targetCircle;
    private RectTransform approachCircle;
    private Image targetRing;
    private Image approachRing;
    private Image targetGlow;
    private TMP_Text keyText;
    private TMP_Text judgementText;
    private TMP_Text progressText;
    private readonly Image[] laneCaps = new Image[4];
    private readonly TMP_Text[] laneLabels = new TMP_Text[4];
    private readonly float[] laneFlashUntil = new float[4];

    private int expectedLane = -1;
    private int previousLane = -1;
    private float noteElapsed;
    private float noteHitTime;
    private float currentApproachScale;
    private float judgementPopAt;
    private int successfulHits;
    private int misses;
    private bool noteActive;
    private bool finished;

    public override string ControlHint =>
        TerminalScreenController.KeyHints(("A S D F", "원이 겹칠 때 누르기"), ("BACKSPACE", "메뉴로"));

    public override void Begin(ConnectionDevice device, TerminalCommands command)
    {
        BuildInterface();
        UpdateProgress();
        MinigameTutorial.ShowBlocking(new TutorialCard
        {
            Id = "terminal.rhythm",
            Role = "해커 • 문 잠금 해제",
            Title = "신호 동기화 (리듬)",
            Goal = $"타이밍에 맞춰 {requiredHits}번 성공하면 문이 열립니다.",
            Steps = new[]
            {
                ("", "가운데 원 안에 누를 키(A, S, D, F 중 하나)가 표시됩니다."),
                ("", "바깥 원이 줄어들어 가운데 원과 겹치는 순간 — 원이 밝게 빛날 때"),
                ("A  S  D  F", "표시된 키를 누르세요. 아래 키 안내가 같이 빛납니다."),
                ("", $"실수는 {allowedMisses}번까지 괜찮아요. 천천히 해도 됩니다!")
            }
        }, (RectTransform)transform, () => StartCoroutine(BeginSequence()));
    }

    public override void End()
    {
        StopAllCoroutines();
    }

    private void Update()
    {
        AnimateLanes();
        AnimateJudgement();
        if (!noteActive || finished)
            return;

        noteElapsed += Time.unscaledDeltaTime;
        UpdateApproachCircle();

        if (TerminalKeyboardInput.TryGetRhythmKeyPressed(out char pressedKey))
        {
            int lane = System.Array.IndexOf(LaneKeys, pressedKey);
            if (lane >= 0)
            {
                laneFlashUntil[lane] = Time.unscaledTime + 0.12f;
                JudgeInput(lane);
                return;
            }
        }

        if (currentApproachScale < 1f - goodScaleTolerance)
            ResolveMiss("놓쳤어요");
    }

    private IEnumerator BeginSequence()
    {
        for (int beat = 3; beat >= 1; beat--)
        {
            ShowJudgement(beat.ToString(), RuntimeUi.Theme.highlight);
            UiSfx.Play(UiCue.RhythmCountIn, 1f, beat == 1 ? 1.5f : 1f);
            yield return new WaitForSecondsRealtime(0.45f);
        }
        SpawnNextNote();
    }

    private void SpawnNextNote()
    {
        if (finished)
            return;

        do expectedLane = Random.Range(0, LaneKeys.Length);
        while (expectedLane == previousLane);
        previousLane = expectedLane;
        noteHitTime = Mathf.Max(0.5f, Random.Range(
            Mathf.Min(approachDurationRange.x, approachDurationRange.y),
            Mathf.Max(approachDurationRange.x, approachDurationRange.y)));
        noteElapsed = 0f;
        noteActive = true;

        keyText.text = LaneKeys[expectedLane].ToString();
        keyText.color = RuntimeUi.Theme.highlight;
        ShowJudgement("타이밍을 기다려요", RuntimeUi.Theme.dim, false);
        targetRing.color = RuntimeUi.Theme.accent;
        currentApproachScale = approachStartScale;
        approachCircle.localScale = Vector3.one * currentApproachScale;
        approachRing.gameObject.SetActive(true);
    }

    private void UpdateApproachCircle()
    {
        float scalePerSecond = (approachStartScale - 1f) / noteHitTime;
        currentApproachScale = noteElapsed <= noteHitTime
            ? Mathf.Lerp(approachStartScale, 1f, noteElapsed / noteHitTime)
            : 1f - (noteElapsed - noteHitTime) * scalePerSecond;
        approachCircle.localScale = Vector3.one * currentApproachScale;

        bool inWindow = Mathf.Abs(currentApproachScale - 1f) <= goodScaleTolerance;
        approachRing.color = inWindow ? RuntimeUi.Theme.highlight : RuntimeUi.Theme.info;
        targetGlow.color = new Color(RuntimeUi.Theme.accent.r, RuntimeUi.Theme.accent.g, RuntimeUi.Theme.accent.b, inWindow ? 0.45f : 0.08f);
        if (inWindow && judgementText.text != "지금!")
            ShowJudgement("지금!", RuntimeUi.Theme.highlight);
    }

    private void JudgeInput(int lane)
    {
        if (lane != expectedLane)
        {
            ResolveMiss($"{LaneKeys[lane]}가 아니라 {LaneKeys[expectedLane]}!");
            return;
        }

        float difference = Mathf.Abs(currentApproachScale - 1f);
        if (difference > goodScaleTolerance)
        {
            ResolveMiss(currentApproachScale > 1f ? "조금 빨라요" : "조금 늦어요");
            return;
        }

        ResolveHit(difference <= perfectScaleTolerance);
    }

    private void ResolveHit(bool perfect)
    {
        noteActive = false;
        approachRing.gameObject.SetActive(false);
        successfulHits++;
        UiSfx.Play(perfect ? UiCue.RhythmPerfect : UiCue.RhythmGood, 1f, LanePitches[expectedLane]);

        ShowJudgement(perfect ? "PERFECT!" : "GOOD", RuntimeUi.Theme.accent);
        targetRing.color = RuntimeUi.Theme.highlight;
        keyText.color = RuntimeUi.Theme.accent;
        UpdateProgress();

        if (successfulHits >= requiredHits)
        {
            finished = true;
            ShowJudgement("동기화 완료 — 문이 열립니다", RuntimeUi.Theme.highlight);
            StartCoroutine(ReportAfterDelay(true));
            return;
        }

        StartCoroutine(QueueNextNote());
    }

    private void ResolveMiss(string reason)
    {
        if (!noteActive)
            return;

        noteActive = false;
        approachRing.gameObject.SetActive(false);
        misses++;
        UiSfx.Play(UiCue.RhythmMiss);

        ShowJudgement(reason, RuntimeUi.Theme.danger);
        targetRing.color = RuntimeUi.Theme.danger;
        keyText.color = RuntimeUi.Theme.danger;
        UpdateProgress();

        if (misses >= allowedMisses)
        {
            finished = true;
            ShowJudgement("동기화 실패 — 다시 시도하세요", RuntimeUi.Theme.danger);
            StartCoroutine(ReportAfterDelay(false));
            return;
        }

        StartCoroutine(QueueNextNote());
    }

    private IEnumerator QueueNextNote()
    {
        float delay = Random.Range(
            Mathf.Min(noteGapRange.x, noteGapRange.y),
            Mathf.Max(noteGapRange.x, noteGapRange.y));
        yield return new WaitForSecondsRealtime(Mathf.Max(0.15f, delay));
        SpawnNextNote();
    }

    private IEnumerator ReportAfterDelay(bool succeeded)
    {
        yield return new WaitForSecondsRealtime(0.9f);
        if (succeeded)
            ReportSuccess();
        else
            ReportFailure();
    }

    private void UpdateProgress()
    {
        string hit = DefragUiTheme.Hex(RuntimeUi.Theme.accent);
        string empty = DefragUiTheme.Hex(RuntimeUi.Theme.dim);
        string danger = DefragUiTheme.Hex(RuntimeUi.Theme.danger);
        var builder = new System.Text.StringBuilder("성공  ");
        for (int i = 0; i < requiredHits; i++)
            builder.Append(i < successfulHits ? $"<color=#{hit}>■</color> " : $"<color=#{empty}>■</color> ");
        builder.Append("     여유  ");
        for (int i = 0; i < allowedMisses; i++)
            builder.Append(i < allowedMisses - misses ? $"<color=#{danger}>●</color> " : $"<color=#{empty}>○</color> ");
        progressText.text = builder.ToString();
    }

    private void ShowJudgement(string text, Color color, bool pop = true)
    {
        judgementText.text = text;
        judgementText.color = color;
        if (pop)
            judgementPopAt = Time.unscaledTime;
    }

    private void AnimateJudgement()
    {
        float t = Mathf.Clamp01((Time.unscaledTime - judgementPopAt) / 0.18f);
        judgementText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, t);
    }

    private void AnimateLanes()
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        for (int i = 0; i < laneCaps.Length; i++)
        {
            bool expected = noteActive && i == expectedLane;
            bool flashing = Time.unscaledTime < laneFlashUntil[i];
            laneCaps[i].color = flashing ? theme.highlight : expected ? new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.55f) : theme.panelRaised;
            laneLabels[i].color = flashing ? theme.panel : expected ? theme.highlight : theme.dim;
            laneCaps[i].rectTransform.localScale = Vector3.one * (expected ? 1.08f : 1f);
        }
    }

    private void BuildInterface()
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        progressText = CreateText("Progress", 26f, TextAlignmentOptions.Center, transform);
        RuntimeUi.Place(progressText.rectTransform, new Vector2(0f, 0.88f), Vector2.one);

        RectTransform playField = CreateRect("Rhythm Play Field", transform);
        RuntimeUi.Place(playField, new Vector2(0f, 0.3f), new Vector2(1f, 0.88f));

        Image glow = RuntimeUi.Panel("Target Glow", playField, Color.clear, RuntimeUiSprites.SoftGlow);
        RuntimeUi.PlaceCentered(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(420f, 420f));
        targetGlow = glow;

        targetCircle = CreateRing("Target Circle", playField, new Vector2(230f, 230f), theme.accent, out targetRing);
        approachCircle = CreateRing("Approach Circle", playField, new Vector2(230f, 230f), theme.info, out approachRing);

        keyText = CreateText("Required Key", 110f, TextAlignmentOptions.Center, playField);
        RuntimeUi.PlaceCentered(keyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(200f, 170f));
        keyText.text = "•";

        judgementText = CreateText("Judgement", 34f, TextAlignmentOptions.Center, transform);
        RuntimeUi.Place(judgementText.rectTransform, new Vector2(0f, 0.2f), new Vector2(1f, 0.3f));

        RectTransform lanes = CreateRect("Lane Keys", transform);
        RuntimeUi.Place(lanes, new Vector2(0.2f, 0.02f), new Vector2(0.8f, 0.19f));
        for (int i = 0; i < LaneKeys.Length; i++)
        {
            laneLabels[i] = RuntimeUi.KeyCap(lanes, LaneKeys[i].ToString(), new Vector2(110f, 96f), out laneCaps[i]);
            RectTransform rect = laneCaps[i].rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2((i + 0.5f) / LaneKeys.Length, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }

        approachRing.gameObject.SetActive(false);
    }

    private RectTransform CreateRing(string name, Transform parent, Vector2 size, Color color, out Image ring)
    {
        RectTransform rect = CreateRect(name, parent);
        RuntimeUi.PlaceCentered(rect, new Vector2(0.5f, 0.5f), size);
        ring = rect.gameObject.AddComponent<Image>();
        ring.sprite = RuntimeRingSprite.Get();
        ring.preserveAspect = true;
        ring.color = color;
        ring.raycastTarget = false;
        return rect;
    }

    private TMP_Text CreateText(string name, float size, TextAlignmentOptions alignment, Transform parent) =>
        RuntimeUi.Text(name, parent, size, alignment, terminalFont, RuntimeUi.Theme.text);

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }
}

/// <summary>Creates one cached antialiased ring sprite for the runtime UI.</summary>
public static class RuntimeRingSprite
{
    private const int TextureSize = 256;
    private static Sprite cachedSprite;

    public static Sprite Get()
    {
        if (cachedSprite != null)
            return cachedSprite;

        Texture2D texture = new(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = "Runtime Rhythm Ring",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[TextureSize * TextureSize];
        float center = (TextureSize - 1) * 0.5f;
        float radius = TextureSize * 0.5f;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float normalizedDistance =
                    Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / radius;
                float outerEdge = 1f - Mathf.SmoothStep(0.982f, 0.995f, normalizedDistance);
                float innerEdge = Mathf.SmoothStep(0.875f, 0.888f, normalizedDistance);
                float alpha = outerEdge * innerEdge;
                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        cachedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, TextureSize, TextureSize),
            new Vector2(0.5f, 0.5f),
            TextureSize);
        cachedSprite.name = "Runtime Rhythm Ring Sprite";
        cachedSprite.hideFlags = HideFlags.HideAndDontSave;
        return cachedSprite;
    }
}
