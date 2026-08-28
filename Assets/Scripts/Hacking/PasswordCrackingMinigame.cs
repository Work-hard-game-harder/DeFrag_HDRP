using System.Collections;
using System.Text;
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
    private static readonly char[] RhythmKeys = { 'Q', 'W', 'E', 'R', 'A', 'S', 'D', 'F' };

    private static readonly Color Green = new(0.1f, 1f, 0.2f, 1f);
    private static readonly Color DimGreen = new(0.02f, 0.42f, 0.08f, 1f);
    private static readonly Color PerfectGreen = new(0.65f, 1f, 0.7f, 1f);
    private static readonly Color ErrorRed = new(1f, 0.08f, 0.06f, 1f);

    [Header("Rhythm Rules")]
    [SerializeField, Min(1)] private int requiredHits = 4;
    [SerializeField, Min(1)] private int allowedMisses = 3;
    [SerializeField] private Vector2 approachDurationRange = new(0.95f, 1.45f);
    [SerializeField] private Vector2 noteGapRange = new(0.22f, 0.5f);
    [SerializeField, Range(0.03f, 0.2f)] private float goodScaleTolerance = 0.1f;
    [SerializeField, Range(0.01f, 0.1f)] private float perfectScaleTolerance = 0.035f;
    [SerializeField, Min(1.1f)] private float approachStartScale = 2.35f;

    [Header("Presentation")]
    [SerializeField] private TMP_FontAsset terminalFont;

    private RectTransform targetCircle;
    private RectTransform approachCircle;
    private Image targetRing;
    private Image approachRing;
    private TMP_Text keyText;
    private TMP_Text instructionText;
    private TMP_Text judgementText;
    private TMP_Text progressText;
    private TerminalSfxPlayer terminalSfx;

    private char expectedKey;
    private char previousKey;
    private float noteElapsed;
    private float noteHitTime;
    private float currentApproachScale;
    private int successfulHits;
    private int misses;
    private bool noteActive;
    private bool finished;

    public override string ControlHint =>
        "[Q/W/E/R/A/S/D/F] HIT    [BACKSPACE] RETURN";

    public override void Begin(ConnectionDevice device, TerminalCommands command)
    {
        terminalSfx = device.TerminalSfx;
        BuildInterface();
        UpdateProgress();
        StartCoroutine(BeginSequence());
    }

    public override void End()
    {
        StopAllCoroutines();
    }

    private void Update()
    {
        if (!noteActive || finished)
            return;

        noteElapsed += Time.unscaledDeltaTime;
        UpdateApproachCircle();

        if (TerminalKeyboardInput.TryGetRhythmKeyPressed(out char pressedKey))
        {
            JudgeInput(pressedKey);
            return;
        }

        if (currentApproachScale < 1f - goodScaleTolerance)
            ResolveMiss("놓침");
    }

    private IEnumerator BeginSequence()
    {
        instructionText.text =
            "바깥 원이 가운데 원과 겹칠 때\n" +
            "원 안에 표시된 키를 누르세요";
        judgementText.color = Green;
        judgementText.text = "준비";
        yield return new WaitForSecondsRealtime(0.9f);
        SpawnNextNote();
    }

    private void SpawnNextNote()
    {
        if (finished)
            return;

        expectedKey = ChooseNextKey();
        previousKey = expectedKey;
        noteHitTime = Random.Range(
            Mathf.Min(approachDurationRange.x, approachDurationRange.y),
            Mathf.Max(approachDurationRange.x, approachDurationRange.y));
        noteHitTime = Mathf.Max(0.35f, noteHitTime);
        noteElapsed = 0f;
        noteActive = true;

        keyText.text = expectedKey.ToString();
        keyText.color = Green;
        judgementText.text = "신호 접근 중";
        judgementText.color = DimGreen;
        targetRing.color = Green;
        targetCircle.localScale = Vector3.one;
        currentApproachScale = approachStartScale;
        approachCircle.localScale = Vector3.one * currentApproachScale;
        approachRing.color = PerfectGreen;
        approachRing.gameObject.SetActive(true);
    }

    private void UpdateApproachCircle()
    {
        if (noteElapsed <= noteHitTime)
        {
            float progress = Mathf.Clamp01(noteElapsed / noteHitTime);
            currentApproachScale = Mathf.Lerp(approachStartScale, 1f, progress);
        }
        else
        {
            float scalePerSecond = (approachStartScale - 1f) / noteHitTime;
            currentApproachScale = 1f - (noteElapsed - noteHitTime) * scalePerSecond;
        }

        approachCircle.localScale = Vector3.one * currentApproachScale;

        float scaleDifference = Mathf.Abs(currentApproachScale - 1f);
        approachRing.color = scaleDifference <= goodScaleTolerance
            ? PerfectGreen
            : Green;
    }

    private void JudgeInput(char pressedKey)
    {
        if (pressedKey != expectedKey)
        {
            ResolveMiss($"잘못된 키: {pressedKey}");
            return;
        }

        float scaleDifference = Mathf.Abs(currentApproachScale - 1f);
        if (scaleDifference > goodScaleTolerance)
        {
            ResolveMiss(currentApproachScale > 1f ? "너무 빠름" : "너무 늦음");
            return;
        }

        ResolveHit(scaleDifference <= perfectScaleTolerance);
    }

    private void ResolveHit(bool perfect)
    {
        noteActive = false;
        approachRing.gameObject.SetActive(false);
        successfulHits++;
        terminalSfx?.PlayMenuSelected();

        judgementText.color = PerfectGreen;
        judgementText.text = perfect ? "PERFECT" : "GOOD";
        targetRing.color = PerfectGreen;
        targetCircle.localScale = Vector3.one * 1.08f;
        keyText.color = Color.black;
        UpdateProgress();

        if (successfulHits >= requiredHits)
        {
            finished = true;
            instructionText.text = "신호 동기화 완료 // 접근 승인";
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
        terminalSfx?.PlayIncorrectAnswer();

        judgementText.color = ErrorRed;
        judgementText.text = reason;
        targetRing.color = ErrorRed;
        keyText.color = ErrorRed;
        UpdateProgress();

        if (misses >= allowedMisses)
        {
            finished = true;
            instructionText.text = "동기화 실패 // 다시 시도하십시오";
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
        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, delay));
        SpawnNextNote();
    }

    private IEnumerator ReportAfterDelay(bool succeeded)
    {
        yield return new WaitForSecondsRealtime(0.85f);
        if (succeeded)
            ReportSuccess();
        else
            ReportFailure();
    }

    private char ChooseNextKey()
    {
        char nextKey = RhythmKeys[Random.Range(0, RhythmKeys.Length)];
        if (RhythmKeys.Length <= 1 || nextKey != previousKey)
            return nextKey;

        int currentIndex = System.Array.IndexOf(RhythmKeys, nextKey);
        int offset = Random.Range(1, RhythmKeys.Length);
        return RhythmKeys[(currentIndex + offset) % RhythmKeys.Length];
    }

    private void UpdateProgress()
    {
        StringBuilder markers = new();
        for (int i = 0; i < requiredHits; i++)
            markers.Append(i < successfulHits ? "[O] " : "[-] ");

        progressText.text =
            $"동기화  {markers}    오류 {misses}/{allowedMisses}";
    }

    private void BuildInterface()
    {
        instructionText = CreateText("Instruction", 22f, TextAlignmentOptions.Center);
        Place(instructionText.rectTransform,
            new Vector2(0f, 0.78f), Vector2.one,
            new Vector2(10f, 0f), new Vector2(-10f, 0f));

        RectTransform playField = CreateRect("Rhythm Play Field", transform);
        Place(playField,
            new Vector2(0f, 0.18f), new Vector2(1f, 0.78f),
            Vector2.zero, Vector2.zero);

        targetCircle = CreateRing(
            "Target Circle", playField, new Vector2(210f, 210f), Green, out targetRing);
        approachCircle = CreateRing(
            "Approach Circle", playField, new Vector2(210f, 210f), PerfectGreen, out approachRing);

        keyText = CreateText("Required Key", 76f, TextAlignmentOptions.Center, playField);
        Center(keyText.rectTransform, new Vector2(180f, 150f));
        keyText.text = "-";

        judgementText = CreateText("Judgement", 28f, TextAlignmentOptions.Center, playField);
        Center(judgementText.rectTransform, new Vector2(360f, 60f));
        judgementText.rectTransform.anchoredPosition = new Vector2(0f, -145f);

        progressText = CreateText("Progress", 21f, TextAlignmentOptions.Center);
        Place(progressText.rectTransform,
            Vector2.zero, new Vector2(1f, 0.18f),
            new Vector2(10f, 0f), new Vector2(-10f, 0f));

        approachRing.gameObject.SetActive(false);
    }

    private RectTransform CreateRing(
        string name,
        Transform parent,
        Vector2 size,
        Color color,
        out Image ring)
    {
        RectTransform rect = CreateRect(name, parent);
        Center(rect, size);
        ring = rect.gameObject.AddComponent<Image>();
        ring.sprite = RuntimeRingSprite.Get();
        ring.type = Image.Type.Simple;
        ring.preserveAspect = true;
        ring.color = color;
        ring.raycastTarget = false;
        return rect;
    }

    private TMP_Text CreateText(
        string name,
        float size,
        TextAlignmentOptions alignment,
        Transform parent = null)
    {
        GameObject child = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        child.transform.SetParent(parent == null ? transform : parent, false);
        TMP_Text text = child.GetComponent<TMP_Text>();
        if (terminalFont != null)
            text.font = terminalFont;
        text.fontSize = size;
        text.color = Green;
        text.fontStyle = FontStyles.Bold;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    private static void Center(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    private static void Place(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 minOffset,
        Vector2 maxOffset)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = minOffset;
        rect.offsetMax = maxOffset;
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
