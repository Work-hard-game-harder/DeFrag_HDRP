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

    [Header("Presentation")]
    [Tooltip("Explicit font for runtime-created terminal labels and input text.")]
    [SerializeField] private TMP_FontAsset terminalFont;

    private ConnectionDevice device;
    private CooperativeTerminalHintRelay hintRelay;
    private TvMonsterProximityGlitch localGlitch;
    private TMP_Text log;
    private TMP_Text challenge;
    private TMP_Text timer;
    private TMP_InputField input;
    private TerminalSfxPlayer terminalSfx;
    private string expectedResponse;
    private int currentRound;
    private float remainingTime;
    private bool acceptingInput;
    private bool finished;
    private ConnectServerCoordinator coordinator;
    private bool opticalRelayMode;
    private ConnectServerUplinkPhase displayedPhase;
    private bool hasDisplayedPhase;

    public override bool ConsumesTextInput => true;
    public override bool CloseTerminalOnSuccess => true;
    public override string ControlHint => "[ENTER] AUTHENTICATE    [ESC] ABORT";

    public override void Begin(ConnectionDevice terminal, TerminalCommands command)
    {
        device = terminal;
        terminalSfx = terminal.TerminalSfx;
        if (Camera.main != null)
        {
            hintRelay = Camera.main.GetComponentInParent<CooperativeTerminalHintRelay>();
            localGlitch = Camera.main.GetComponentInParent<TvMonsterProximityGlitch>();
        }
        BuildInterface();

        ConnectServerTerminalLink link = terminal.GetComponent<ConnectServerTerminalLink>();
        coordinator = link != null ? link.Coordinator : null;
        opticalRelayMode = coordinator != null && coordinator.IsSpawned;
        if (opticalRelayMode)
        {
            coordinator.LocalVerificationResolved += OnVerificationResolved;
            log.text =
                "> EXEC CONNECT_SERVER\n" +
                "> OPTICAL RELAY HANDSHAKE REQUIRED\n" +
                "> SECOND OPERATOR: IR CAMERA REQUIRED";
            acceptingInput = false;
            input.interactable = false;
            coordinator.RequestStartOrResume();
            RefreshOpticalInterface();
            return;
        }

        log.text =
            "> EXEC CONNECT_SERVER\n" +
            "> REMOTE OPERATOR AUTHENTICATION REQUIRED\n" +
            "> FAILURE WILL EXPOSE TERMINAL LOCATION";
        StartRound();
    }

    private void Update()
    {
        if (opticalRelayMode)
        {
            UpdateOpticalMode();
            return;
        }

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
        if (coordinator != null)
        {
            coordinator.LocalVerificationResolved -= OnVerificationResolved;
            if (opticalRelayMode && !finished)
                coordinator.RequestSuspend();
        }
    }

    private void UpdateOpticalMode()
    {
        if (coordinator == null || !coordinator.IsSpawned || finished)
            return;

        if (Input.GetKeyDown(KeyCode.Tab) &&
            coordinator.Phase == ConnectServerUplinkPhase.AwaitingVerification)
            TryAutocompleteVerifyCommand();

        RefreshOpticalInterface();
    }

    private void RefreshOpticalInterface()
    {
        if (coordinator == null)
            return;

        ConnectServerUplinkPhase phase = coordinator.Phase;
        float timeLeft = Mathf.Max(0f, (float)(coordinator.Deadline - coordinator.ServerTime));
        timer.text = phase == ConnectServerUplinkPhase.AwaitingOpticalScan ||
                     phase == ConnectServerUplinkPhase.AwaitingVerification
            ? $"UPLINK WINDOW: {timeLeft:00.0}s    TRACE: {coordinator.Trace:00}%"
            : $"TRACE: {coordinator.Trace:00}%";

        if (!hasDisplayedPhase || phase != displayedPhase)
        {
            hasDisplayedPhase = true;
            displayedPhase = phase;
            AppendPhaseLog(phase);
        }

        switch (phase)
        {
            case ConnectServerUplinkPhase.Idle:
                challenge.color = DimGreen;
                challenge.text = "REQUESTING SERVER HANDSHAKE...";
                SetInputEnabled(false);
                break;
            case ConnectServerUplinkPhase.Connecting:
                challenge.color = Green;
                challenge.text = "CONNECTING TO OPTICAL RELAY NETWORK...";
                SetInputEnabled(false);
                break;
            case ConnectServerUplinkPhase.AwaitingOpticalScan:
                challenge.color = Green;
                challenge.text =
                    $"RELAY {coordinator.CompletedRounds + 1:00}/{coordinator.RequiredRounds:00}\n" +
                    $"TARGET: {coordinator.TargetRelayId}\n" +
                    $"SECTOR: {coordinator.TargetSector}\n" +
                    "WAITING FOR IR CAMERA CAPTURE";
                SetInputEnabled(false);
                break;
            case ConnectServerUplinkPhase.AwaitingVerification:
                challenge.color = Green;
                challenge.text =
                    $"{coordinator.TargetRelayId} OPTICAL LOCK ACCEPTED\n" +
                    "ENTER TEAMMATE AUTH WORD\n" +
                    "FORMAT: VERIFY [WORD]";
                SetInputEnabled(true);
                break;
            case ConnectServerUplinkPhase.Suspended:
                challenge.color = DimGreen;
                challenge.text = "UPLINK SESSION SUSPENDED";
                SetInputEnabled(false);
                break;
            case ConnectServerUplinkPhase.Completed:
                challenge.color = Green;
                challenge.text = "SERVER CONNECTION ESTABLISHED";
                SetInputEnabled(false);
                finished = true;
                StartCoroutine(CompleteAfterDelay());
                break;
            case ConnectServerUplinkPhase.Failed:
                challenge.color = ErrorRed;
                challenge.text = "TRACE LIMIT EXCEEDED\nUPLINK TERMINATED";
                SetInputEnabled(false);
                finished = true;
                StartCoroutine(FailOpticalAfterDelay());
                break;
        }
    }

    private void SetInputEnabled(bool enabled)
    {
        if (acceptingInput == enabled && input.interactable == enabled)
            return;

        acceptingInput = enabled;
        input.interactable = enabled;
        if (!enabled)
        {
            input.SetTextWithoutNotify(string.Empty);
            return;
        }

        input.ActivateInputField();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(input.gameObject);
    }

    private void AppendPhaseLog(ConnectServerUplinkPhase phase)
    {
        string message = phase switch
        {
            ConnectServerUplinkPhase.Connecting => "NEGOTIATING RELAY ROUTE",
            ConnectServerUplinkPhase.AwaitingOpticalScan =>
                $"ROUTE ISSUED: {coordinator.TargetRelayId} / {coordinator.TargetSector}",
            ConnectServerUplinkPhase.AwaitingVerification => "OPTICAL CAPTURE ACCEPTED",
            ConnectServerUplinkPhase.Suspended => "SESSION SUSPENDED",
            ConnectServerUplinkPhase.Completed => "ALL RELAYS VERIFIED",
            ConnectServerUplinkPhase.Failed => "TRACE LIMIT EXCEEDED",
            _ => string.Empty
        };
        if (!string.IsNullOrEmpty(message))
            log.text += $"\n> {message}";
    }

    private void TryAutocompleteVerifyCommand()
    {
        string value = input.text.Trim().ToUpperInvariant();
        if (value.Length < 2 || !"VERIFY".StartsWith(value))
            return;

        input.SetTextWithoutNotify("VERIFY ");
        input.caretPosition = input.text.Length;
        input.ActivateInputField();
    }

    private void OnVerificationResolved(bool success, string message)
    {
        log.text += $"\n> {message}";
        if (!success)
            terminalSfx?.PlayIncorrectAnswer();
        input.SetTextWithoutNotify(string.Empty);
        if (!success && coordinator != null &&
            coordinator.Phase == ConnectServerUplinkPhase.AwaitingVerification)
            SetInputEnabled(true);
    }

    private IEnumerator FailOpticalAfterDelay()
    {
        yield return new WaitForSecondsRealtime(1.1f);
        ReportFailure();
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

        if (opticalRelayMode)
        {
            acceptingInput = false;
            input.interactable = false;
            coordinator.SubmitVerification(submitted);
            return;
        }

        if (submitted.Trim().ToUpperInvariant() != expectedResponse)
        {
            terminalSfx?.PlayIncorrectAnswer();
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
        placeholder.text = "> ENGLISH INPUT ONLY // VERIFY [WORD]";
        placeholder.color = new Color(Green.r, Green.g, Green.b, 0.35f);

        input = inputObject.GetComponent<TMP_InputField>();
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 64;
        input.caretColor = Green;
        input.selectionColor = new Color(0.1f, 1f, 0.2f, 0.3f);
        input.onValidateInput = ValidateCommandCharacter;
        input.onValueChanged.AddListener(ForceUppercase);
        input.onSubmit.AddListener(Submit);
        terminalSfx?.BindTyping(input);
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
        if (terminalFont != null)
            text.font = terminalFont;
        text.fontSize = size;
        text.color = Green;
        text.alignment = alignment;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        return text;
    }

    private static char ValidateCommandCharacter(string _, int __, char character)
    {
        char uppercase = char.ToUpperInvariant(character);
        if ((uppercase >= 'A' && uppercase <= 'Z') ||
            (uppercase >= '0' && uppercase <= '9') ||
            uppercase == ' ' || uppercase == '_')
            return uppercase;

        return '\0';
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
