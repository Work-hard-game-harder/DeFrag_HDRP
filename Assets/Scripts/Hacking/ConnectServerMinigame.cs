using System.Collections;
using System.Collections.Generic;
using DeFrag.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ConnectServerMinigame : HackingMinigameBase
{
    private static readonly Color Green = new(0.1f, 1f, 0.2f);
    private static readonly Color DimGreen = new(0.02f, 0.48f, 0.09f);
    private static readonly Color ErrorRed = new(1f, 0.1f, 0.08f);

    [Header("Authentication")]
    [SerializeField, Min(1)] private int authenticationRounds = 3;
    [SerializeField, Min(1f)] private float roundTimeLimit = 12f;
    [SerializeField] private List<string> authenticationTokens = new()
    {
        "CIPHER", "HANDSHAKE", "INTEGRITY", "MAINFRAME",
        "PROTOCOL", "SECURITY", "UPLINK", "VALIDATE"
    };

    [Header("Failure Consequences")]
    [SerializeField, Min(0f)] private float monsterAlertRadius = 45f;
    [SerializeField, Range(0f, 1f)] private float glitchIntensity = 1f;
    [SerializeField, Min(0f)] private float glitchDuration = 1.2f;

    private ConnectionDevice device;
    private CooperativeTerminalHintRelay hintRelay;
    private TvMonsterProximityGlitch localGlitch;
    private TMP_Text log;
    private TMP_Text challenge;
    private TMP_Text timer;
    private TMP_InputField input;
    private string expectedResponse;
    private int currentRound;
    private float remainingTime;
    private bool acceptingInput;
    private bool finished;

    public override bool ConsumesTextInput => true;
    public override bool CloseTerminalOnSuccess => true;
    public override string ControlHint => "[ENTER] AUTHENTICATE    [ESC] ABORT";

    public override void Begin(ConnectionDevice terminal, TerminalCommands command)
    {
        device = terminal;
        hintRelay = Camera.main.GetComponentInParent<CooperativeTerminalHintRelay>();
        localGlitch = Camera.main.GetComponentInParent<TvMonsterProximityGlitch>();
        BuildInterface();
        log.text =
            "> EXEC CONNECT_SERVER\n" +
            "> REMOTE OPERATOR AUTHENTICATION REQUIRED\n" +
            "> FAILURE WILL EXPOSE TERMINAL LOCATION";
        StartRound();
    }

    private void Update()
    {
        if (!acceptingInput || finished)
            return;

        remainingTime -= Time.unscaledDeltaTime;
        timer.text = $"AUTH WINDOW: {remainingTime:00.0}s";
        if (remainingTime <= 0f)
            FailAuthentication("AUTHENTICATION TIMEOUT");
    }

    public override void End()
    {
        StopAllCoroutines();
        hintRelay?.HideForTeammate();
    }

    private void StartRound()
    {
        currentRound++;
        string token = authenticationTokens[Random.Range(0, authenticationTokens.Count)]
            .Trim()
            .Replace(' ', '_')
            .ToUpperInvariant();
        int code = Random.Range(0, 100);
        expectedResponse = $"UPLINK_{token}_{code:00}";
        remainingTime = roundTimeLimit;
        acceptingInput = true;

        challenge.color = Green;
        challenge.text =
            $"AUTHENTICATION NODE {currentRound:00}/{authenticationRounds:00}\n" +
            "AWAITING REMOTE OPERATOR KEY";
        input.text = string.Empty;
        input.interactable = true;
        input.ActivateInputField();
        EventSystem.current.SetSelectedGameObject(input.gameObject);

        hintRelay?.ShowForTeammate(
            $"CONNECT_SERVER // NODE {currentRound:00}",
            expectedResponse,
            "AUTH KEY");
    }

    private void Submit(string submitted)
    {
        if (!acceptingInput || finished)
            return;

        if (submitted.Trim().ToUpperInvariant() != expectedResponse)
        {
            FailAuthentication("INVALID REMOTE KEY");
            return;
        }

        acceptingInput = false;
        input.interactable = false;
        hintRelay?.HideForTeammate();
        log.text += $"\n> NODE {currentRound:00} ACCEPTED";

        if (currentRound >= authenticationRounds)
        {
            finished = true;
            challenge.text = "SERVER CONNECTION ESTABLISHED";
            StartCoroutine(CompleteAfterDelay());
        }
        else
        {
            challenge.text = "KEY ACCEPTED // NEXT NODE";
            StartCoroutine(NextRoundAfterDelay());
        }
    }

    private void FailAuthentication(string reason)
    {
        acceptingInput = false;
        hintRelay?.HideForTeammate();
        hintRelay?.ReportTerminalFailure(device.transform.position, monsterAlertRadius);
        localGlitch?.PlayFailureBurst(glitchIntensity, glitchDuration);
        challenge.color = ErrorRed;
        challenge.text = $"{reason}\nLOCATION SIGNATURE BROADCAST";
        log.text += $"\n> ERROR: {reason}";
        input.text = string.Empty;
        input.interactable = false;
        StartCoroutine(RestartAfterFailure());
    }

    private IEnumerator RestartAfterFailure()
    {
        yield return new WaitForSecondsRealtime(1.1f);
        currentRound--;
        StartRound();
    }

    private IEnumerator NextRoundAfterDelay()
    {
        yield return new WaitForSecondsRealtime(0.65f);
        StartRound();
    }

    private IEnumerator CompleteAfterDelay()
    {
        yield return new WaitForSecondsRealtime(0.9f);
        ReportSuccess();
    }

    private void BuildInterface()
    {
        log = CreateText("System Log", 20f, TextAlignmentOptions.TopLeft);
        Place(log.rectTransform, new Vector2(0f, 0.62f), Vector2.one,
            new Vector2(10f, 0f), new Vector2(-10f, 0f));
        log.color = DimGreen;

        challenge = CreateText("Challenge", 27f, TextAlignmentOptions.Center);
        Place(challenge.rectTransform, new Vector2(0f, 0.35f), new Vector2(1f, 0.62f),
            new Vector2(10f, 0f), new Vector2(-10f, 0f));

        timer = CreateText("Timer", 22f, TextAlignmentOptions.Center);
        Place(timer.rectTransform, new Vector2(0f, 0.25f), new Vector2(1f, 0.35f),
            Vector2.zero, Vector2.zero);

        GameObject inputObject = new(
            "Authentication Input",
            typeof(RectTransform),
            typeof(Image),
            typeof(TMP_InputField));
        inputObject.transform.SetParent(transform, false);
        Place((RectTransform)inputObject.transform,
            new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.22f),
            Vector2.zero, Vector2.zero);
        inputObject.GetComponent<Image>().color = new Color(0f, 0.12f, 0.02f, 0.88f);

        TMP_Text inputText = CreateText(
            "Text", 25f, TextAlignmentOptions.MidlineLeft, inputObject.transform);
        Stretch(inputText.rectTransform, new Vector2(18f, 6f), new Vector2(-18f, -6f));
        TMP_Text placeholder = CreateText(
            "Placeholder", 25f, TextAlignmentOptions.MidlineLeft, inputObject.transform);
        Stretch(placeholder.rectTransform, new Vector2(18f, 6f), new Vector2(-18f, -6f));
        placeholder.text = "> ENTER REMOTE KEY";
        placeholder.color = new Color(Green.r, Green.g, Green.b, 0.35f);

        input = inputObject.GetComponent<TMP_InputField>();
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 64;
        input.caretColor = Green;
        input.onValueChanged.AddListener(ForceUppercase);
        input.onSubmit.AddListener(Submit);
    }

    private void ForceUppercase(string value)
    {
        string uppercase = value.ToUpperInvariant();
        if (value == uppercase)
            return;

        int caret = input.caretPosition;
        input.SetTextWithoutNotify(uppercase);
        input.caretPosition = Mathf.Min(caret, uppercase.Length);
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
        text.fontSize = size;
        text.color = Green;
        text.alignment = alignment;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }

    private static void Place(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 min,
        Vector2 max)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }
}
