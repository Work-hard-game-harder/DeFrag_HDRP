using System.Collections;
using System.Collections.Generic;
using DeFrag.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ConnectServerMinigame : HackingMinigameBase
{
    private static Color Green => RuntimeUi.Theme.text;
    private static Color DimGreen => RuntimeUi.Theme.dim;
    private static Color ErrorRed => RuntimeUi.Theme.danger;

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

    [Header("Optical Uplink")]
    [Tooltip("회로 단계에서 해커 화면에 목표 윤곽이 보조로 나타나기까지의 시간(초).")]
    [SerializeField, Min(0f)] private float circuitAssistDelay = 25f;

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
    private TMP_Text inputPlaceholder;
    private string expectedResponse;
    private int currentRound;
    private float remainingTime;
    private bool acceptingInput;
    private bool finished;

    private ConnectServerCoordinator coordinator;
    private bool opticalRelayMode;
    private ConnectServerUplinkPhase displayedPhase;
    private bool hasDisplayedPhase;
    private ConnectServerCircuitView circuitView;
    private int displayedCircuitSeed;
    private RectTransform leftArea;
    private FacilityRadarView radar;
    private TMP_Text roundText;
    private TMP_Text targetText;
    private TMP_Text instructionText;
    private RectTransform timerFill;
    private TMP_Text timerLabel;
    private RectTransform traceFill;
    private TMP_Text traceLabel;
    private readonly List<string> logLines = new();
    private float timeLimitCache = -1f;
    private ConnectServerUplinkPhase timeLimitPhase;

    public override bool ConsumesTextInput => true;
    public override bool CloseTerminalOnSuccess => true;
    public override string ControlHint => opticalRelayMode
        ? TerminalScreenController.KeyHints(("마우스", "모듈 드래그"), ("Q/E", "회전"), ("ENTER", "전송"), ("ESC", "메뉴로"))
        : TerminalScreenController.KeyHints(("ENTER", "전송"), ("ESC", "메뉴로"));

    public override void Begin(ConnectionDevice terminal, TerminalCommands command)
    {
        device = terminal;
        if (Camera.main != null)
        {
            hintRelay = Camera.main.GetComponentInParent<CooperativeTerminalHintRelay>();
            localGlitch = Camera.main.GetComponentInParent<TvMonsterProximityGlitch>();
        }

        ConnectServerTerminalLink link = terminal.GetComponent<ConnectServerTerminalLink>();
        coordinator = link != null ? link.Coordinator : null;
        opticalRelayMode = coordinator != null && coordinator.IsSpawned;
        if (!opticalRelayMode)
        {
            BuildInterface();
            log.text =
                "> EXEC CONNECT_SERVER\n" +
                "> REMOTE OPERATOR AUTHENTICATION REQUIRED\n" +
                "> FAILURE WILL EXPOSE TERMINAL LOCATION";
            StartRound();
            return;
        }

        BuildOpticalInterface();
        coordinator.LocalVerificationResolved += OnVerificationResolved;
        AppendLog("CONNECT_SERVER 실행 // 광학 릴레이 핸드셰이크 필요");
        MinigameTutorial.ShowBlocking(new TutorialCard
        {
            Id = "connect.hacker",
            Role = "해커 • 서버 연결",
            Title = "원격 광학 연결",
            Goal = $"동료와 함께 릴레이 {coordinator.RequiredRounds}곳을 연결합니다.",
            Steps = new[]
            {
                ("", "왼쪽 지도에서 <color=#FFB347>주황 원</color>이 목표 릴레이예요. 초록 화살표는 동료입니다."),
                ("무전", "동료를 목표 릴레이까지 길 안내하세요. 빨간 점(괴물)도 알려주세요!"),
                ("", "동료가 IR 카메라로 릴레이를 찍으면, 회로 조각이 이 화면에 도착합니다."),
                ("", "시간이 끝나거나 엉뚱한 릴레이를 찍으면 추적도(빨간 게이지)가 올라가요.")
            }
        }, (RectTransform)transform, () =>
        {
            if (coordinator != null && !finished)
                coordinator.RequestStartOrResume();
        });
        RefreshOpticalInterface();
    }

    private void Update()
    {
        if (opticalRelayMode)
        {
            if (coordinator != null && coordinator.IsSpawned && !finished)
                RefreshOpticalInterface();
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
        RemoveCircuitView();
        hintRelay?.HideForTeammate();
        if (coordinator != null)
        {
            coordinator.LocalVerificationResolved -= OnVerificationResolved;
            if (opticalRelayMode && !finished)
                coordinator.RequestSuspend();
        }
    }

    private void BuildOpticalInterface()
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        leftArea = RuntimeUi.Panel("Left Area", transform, Color.clear).rectTransform;
        RuntimeUi.Place(leftArea, new Vector2(0f, 0f), new Vector2(0.6f, 1f));
        radar = FacilityRadarView.Create(leftArea, FacilityRadarSettings.Default, terminalFont, new ConnectServerRadarContent(coordinator));

        Image side = RuntimeUi.FramedPanel("Status", transform, theme.panel, 18f);
        RuntimeUi.Place(side.rectTransform, new Vector2(0.62f, 0f), new Vector2(1f, 1f));

        roundText = Label(side.transform, 24f, TextAlignmentOptions.MidlineLeft, new Vector2(0.06f, 0.88f), new Vector2(0.94f, 0.97f));
        targetText = Label(side.transform, 58f, TextAlignmentOptions.Center, new Vector2(0.04f, 0.68f), new Vector2(0.96f, 0.87f));
        targetText.color = theme.info;
        instructionText = Label(side.transform, 24f, TextAlignmentOptions.Top, new Vector2(0.06f, 0.43f), new Vector2(0.94f, 0.67f));
        instructionText.enableAutoSizing = true;
        instructionText.fontSizeMin = 15f;
        instructionText.fontSizeMax = 24f;

        timerLabel = Label(side.transform, 20f, TextAlignmentOptions.MidlineLeft, new Vector2(0.06f, 0.36f), new Vector2(0.94f, 0.42f));
        timerFill = Gauge(side.transform, new Vector2(0.06f, 0.325f), new Vector2(0.94f, 0.355f), theme.accent);
        traceLabel = Label(side.transform, 20f, TextAlignmentOptions.MidlineLeft, new Vector2(0.06f, 0.26f), new Vector2(0.94f, 0.32f));
        traceFill = Gauge(side.transform, new Vector2(0.06f, 0.225f), new Vector2(0.94f, 0.255f), theme.danger);

        log = Label(side.transform, 17f, TextAlignmentOptions.BottomLeft, new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.2f));
        log.color = theme.dim;
    }

    private TMP_Text Label(Transform parent, float size, TextAlignmentOptions alignment, Vector2 min, Vector2 max)
    {
        TMP_Text label = RuntimeUi.Text("Label", parent, size, alignment, terminalFont, RuntimeUi.Theme.text);
        RuntimeUi.Place(label.rectTransform, min, max);
        return label;
    }

    private static RectTransform Gauge(Transform parent, Vector2 min, Vector2 max, Color color)
    {
        Image track = RuntimeUi.Panel("Gauge", parent, new Color(0f, 0f, 0f, 0.6f));
        RuntimeUi.Place(track.rectTransform, min, max);
        Image fill = RuntimeUi.Panel("Fill", track.transform, color);
        RuntimeUi.Place(fill.rectTransform, Vector2.zero, Vector2.one);
        return fill.rectTransform;
    }

    private static void SetGauge(RectTransform fill, float value) =>
        fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);

    private void RefreshOpticalInterface()
    {
        if (coordinator == null)
            return;

        DefragUiTheme theme = RuntimeUi.Theme;
        ConnectServerUplinkPhase phase = coordinator.Phase;
        bool timed = phase == ConnectServerUplinkPhase.AwaitingOpticalScan || phase == ConnectServerUplinkPhase.AwaitingVerification;
        float timeLeft = Mathf.Max(0f, (float)(coordinator.Deadline - coordinator.ServerTime));

        var pips = new System.Text.StringBuilder();
        for (int i = 0; i < coordinator.RequiredRounds; i++)
            pips.Append(i < coordinator.CompletedRounds ? $"<color=#{DefragUiTheme.Hex(theme.accent)}>■</color> " : $"<color=#{DefragUiTheme.Hex(theme.dim)}>■</color> ");
        roundText.text = $"업링크  {pips}";
        targetText.text = timed ? ConnectServerRadarContent.ShortId(coordinator.TargetRelayId) : "----";
        timerLabel.text = timed ? $"남은 시간  {timeLeft:0}초" : "남은 시간  --";
        SetGauge(timerFill, timed ? timeLeft / TimeLimitFor(phase) : 0f);
        traceLabel.text = $"추적도  {coordinator.Trace:0}%   <size=80%><color=#{DefragUiTheme.Hex(theme.dim)}>100%가 되면 연결이 끊겨요</color></size>";
        SetGauge(traceFill, coordinator.Trace / 100f);

        if (!hasDisplayedPhase || phase != displayedPhase)
        {
            hasDisplayedPhase = true;
            displayedPhase = phase;
            OnPhaseEntered(phase);
        }

        if (phase == ConnectServerUplinkPhase.AwaitingVerification)
            EnsureCircuitView();
    }

    // The coordinator exposes only the deadline, so the first observed remaining time per phase becomes the bar's full length.
    private float TimeLimitFor(ConnectServerUplinkPhase phase)
    {
        if (timeLimitCache < 0f || phase != timeLimitPhase)
        {
            timeLimitPhase = phase;
            timeLimitCache = Mathf.Max(1f, (float)(coordinator.Deadline - coordinator.ServerTime));
        }
        return timeLimitCache;
    }

    private void OnPhaseEntered(ConnectServerUplinkPhase phase)
    {
        timeLimitCache = -1f;
        switch (phase)
        {
            case ConnectServerUplinkPhase.Idle:
            case ConnectServerUplinkPhase.Connecting:
                instructionText.text = "릴레이 경로 협상 중...";
                break;
            case ConnectServerUplinkPhase.AwaitingOpticalScan:
                RemoveCircuitView();
                radar.gameObject.SetActive(true);
                instructionText.text = "동료를 <color=#FFB347>주황 원</color>의 목표 릴레이로 안내하세요.\n동료가 IR 카메라로 촬영하면 다음 단계!";
                AppendLog($"목표 지정: {coordinator.TargetRelayId}");
                UiSfx.Play(UiCue.KnobZone);
                break;
            case ConnectServerUplinkPhase.AwaitingVerification:
                instructionText.text = "동료가 불러주는 좌표(예: B2, C3)대로\n모듈을 놓고 전송하세요.";
                AppendLog("광학 캡처 성공 // 회로 데이터 수신");
                UiSfx.Play(UiCue.CaptureAccepted);
                break;
            case ConnectServerUplinkPhase.Suspended:
                instructionText.text = "세션 일시정지";
                break;
            case ConnectServerUplinkPhase.Completed:
                instructionText.text = $"<color=#{DefragUiTheme.Hex(RuntimeUi.Theme.accent)}>서버 연결 완료!</color>";
                AppendLog("모든 릴레이 검증 완료");
                finished = true;
                StartCoroutine(CompleteAfterDelay());
                break;
            case ConnectServerUplinkPhase.Failed:
                instructionText.text = $"<color=#{DefragUiTheme.Hex(RuntimeUi.Theme.danger)}>추적 한도 초과 — 연결이 끊겼어요</color>";
                AppendLog("TRACE LIMIT EXCEEDED");
                finished = true;
                StartCoroutine(FailOpticalAfterDelay());
                break;
        }
    }

    private void AppendLog(string line)
    {
        logLines.Add($"> {line}");
        while (logLines.Count > 4)
            logLines.RemoveAt(0);
        log.text = string.Join("\n", logLines);
    }

    private void OnVerificationResolved(bool success, string message)
    {
        AppendLog(success ? "회로 검증 성공" : "배치가 달라요 — 동료와 좌표를 다시 맞춰보세요");
        if (success)
        {
            UiSfx.Play(UiCue.RoundClear);
            RemoveCircuitView();
        }
        else
        {
            circuitView?.NotifyRejected();
        }
    }

    private void EnsureCircuitView()
    {
        int seed = coordinator.CircuitSeed;
        if (seed == 0 || displayedCircuitSeed == seed) return;
        RemoveCircuitView();
        displayedCircuitSeed = seed;
        radar.gameObject.SetActive(false);
        GameObject viewObject = new("Circuit Shape Puzzle", typeof(RectTransform), typeof(ConnectServerCircuitView));
        viewObject.transform.SetParent(leftArea, false);
        circuitView = viewObject.GetComponent<ConnectServerCircuitView>();
        circuitView.Begin(seed, coordinator.CompletedRounds + 1, circuitAssistDelay, SubmitCircuitSolution);
        MinigameTutorial.ShowBlocking(new TutorialCard
        {
            Id = "connect.hacker.circuit",
            Role = "해커 • 회로 복원",
            Title = "말로 맞추는 회로",
            Goal = "동료만 볼 수 있는 모양대로 모듈을 배치합니다.",
            Steps = new[]
            {
                ("무전", "동료에게 \"칠해진 칸 좌표 불러줘!\"라고 하세요. (예: B2, B3, C3)"),
                ("드래그", "모듈을 끌어 해당 칸에 놓고, Q/E로 회전할 수 있어요."),
                ("ENTER", "모두 놓았으면 전송! 틀리면 추적도가 조금 오르고 다시 할 수 있어요."),
                ("", "한참 어려우면 주황 윤곽이 보조로 나타나요.")
            }
        }, leftArea, null);
    }

    private void SubmitCircuitSolution(string placements)
    {
        if (coordinator != null && coordinator.Phase == ConnectServerUplinkPhase.AwaitingVerification)
            coordinator.SubmitCircuitSolution(placements);
    }

    private void RemoveCircuitView()
    {
        if (circuitView != null) Destroy(circuitView.gameObject);
        circuitView = null;
        displayedCircuitSeed = 0;
        if (radar != null) radar.gameObject.SetActive(true);
    }

    private IEnumerator FailOpticalAfterDelay()
    {
        yield return new WaitForSecondsRealtime(1.4f);
        ReportFailure();
    }

    private IEnumerator CompleteAfterDelay()
    {
        yield return new WaitForSecondsRealtime(1.1f);
        ReportSuccess();
    }

    // ---- Legacy text authentication, used only when no optical uplink coordinator is spawned. ----

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
        if (inputPlaceholder != null)
            inputPlaceholder.text = "> TYPE COMPLETE AUTHENTICATION KEY";
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
            UiSfx.Play(UiCue.CardWrong);
            FailAuthentication("INVALID REMOTE KEY");
            return;
        }

        acceptingInput = false;
        input.interactable = false;
        UiSfx.Play(UiCue.RoundClear);
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
        inputObject.GetComponent<Image>().color = RuntimeUi.Theme.panelRaised;
        OperationPanelStyle.Input(inputObject);

        TMP_Text inputText = CreateText(
            "Text", 25f, TextAlignmentOptions.MidlineLeft, inputObject.transform);
        Stretch(inputText.rectTransform, new Vector2(18f, 6f), new Vector2(-18f, -6f));
        inputPlaceholder = CreateText(
            "Placeholder", 25f, TextAlignmentOptions.MidlineLeft, inputObject.transform);
        Stretch(inputPlaceholder.rectTransform, new Vector2(18f, 6f), new Vector2(-18f, -6f));
        inputPlaceholder.text = "> INPUT LOCKED // INITIALIZING";
        inputPlaceholder.color = new Color(Green.r, Green.g, Green.b, 0.5f);

        input = inputObject.GetComponent<TMP_InputField>();
        input.textComponent = inputText;
        input.placeholder = inputPlaceholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 64;
        input.caretColor = Green;
        input.selectionColor = new Color(0.1f, 1f, 0.2f, 0.3f);
        input.onValidateInput = ValidateCommandCharacter;
        input.onValueChanged.AddListener(ForceUppercase);
        input.onSubmit.AddListener(Submit);
        device.TerminalSfx?.BindTyping(input);
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
        Transform parent = null) =>
        RuntimeUi.Text(name, parent == null ? transform : parent, size, alignment, terminalFont, Green);

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
