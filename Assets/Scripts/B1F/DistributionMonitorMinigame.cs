using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.B1F
{
    // Hacker terminal view of Distribution Box A: live switch telemetry against the target, and the knob's timing zone.
    public sealed class DistributionMonitorMinigame : HackingMinigameBase
    {
        private const float RequestInterval = 1.5f;

        private static readonly TutorialCard HackerCard = new()
        {
            Id = "distA.hacker",
            Role = "해커 • 배전함 원격 모니터",
            Title = "정답은 당신 화면에만 있다",
            Goal = "동료가 배전함 스위치를 정답대로 맞추도록 무전으로 지시합니다.",
            Steps = new[]
            {
                ("", "각 스위치의 <color=#FFB347>목표(OFF/ON)</color>와 동료가 지금 맞춘 상태가 실시간으로 보여요."),
                ("무전", "빨간 줄이 틀린 스위치예요. \"3번 ON으로!\"처럼 번호로 알려주세요."),
                ("", "5개가 모두 초록이 되면 다음 뱅크로 자동으로 넘어가요. (A → B → C)"),
                ("", "마지막엔 메인 노브! 초록 구간 숫자를 불러주면 동료가 타이밍을 맞춰요.")
            }
        };

        private static readonly TutorialCard KnobCard = new()
        {
            Id = "distA.knob.hacker",
            Role = "해커 • 메인 노브",
            Title = "숫자 구간을 불러라",
            Goal = "동료는 움직이는 바늘만 보고, 당신은 성공 구간만 봅니다.",
            Steps = new[]
            {
                ("무전", "\"6에서 8 사이!\"처럼 초록 구간의 숫자를 불러주세요."),
                ("", "성공할 때마다 구간이 바뀌어요. 새 숫자를 다시 알려주세요. (3번 성공)")
            }
        };

        [Header("Presentation")]
        [SerializeField] private TMP_FontAsset terminalFont;

        private DistributionBoxController box;
        private readonly List<Image> rowPanels = new();
        private readonly List<TMP_Text> rowTargets = new();
        private readonly List<TMP_Text> rowCurrents = new();
        private readonly List<TMP_Text> rowStates = new();
        private readonly bool[] rowMatched = new bool[5];
        private readonly TMP_Text[] bankTabs = new TMP_Text[3];
        private GameObject bankView;
        private GameObject knobView;
        private TMP_Text matchCounter;
        private TMP_Text headline;
        private TMP_Text partnerText;
        private TMP_Text knobRange;
        private TMP_Text knobRound;
        private RectTransform knobZone;
        private TMP_Text knobFeedback;
        private float knobFeedbackUntil;

        private bool started;
        private bool finished;
        private bool hasTarget;
        private ushort targetMask;
        private int targetBank = -1;
        private bool hasZone;
        private DistributionPuzzlePhase lastPhase;
        private float nextRequest;

        public override string ControlHint => TerminalScreenController.KeyHints(("무전", "동료에게 번호로 지시"), ("BACKSPACE", "메뉴로"));

        public override void Begin(ConnectionDevice device, TerminalCommands command)
        {
            box = device.GetComponent<B1FDistributionTerminalAdapter>()?.Box;
            BuildInterface();
            DistributionBoxController.LocalHintReceived += OnHintReceived;
            DistributionBoxController.LocalKnobZoneReceived += OnKnobZone;
            DistributionBoxController.LocalKnobResolved += OnKnobResolved;
            MinigameTutorial.ShowBlocking(HackerCard, (RectTransform)transform, () => started = true);
        }

        public override void End()
        {
            StopAllCoroutines();
            DistributionBoxController.LocalHintReceived -= OnHintReceived;
            DistributionBoxController.LocalKnobZoneReceived -= OnKnobZone;
            DistributionBoxController.LocalKnobResolved -= OnKnobResolved;
        }

        private void Update()
        {
            if (!started || finished)
                return;
            if (box == null || !box.IsSpawned)
            {
                headline.text = "배전함 신호를 찾을 수 없습니다";
                return;
            }
            if (!box.IsCompleted && !box.CanUseNow)
            {
                headline.text = "지금은 배전함을 조작할 수 없는 상태예요";
                return;
            }

            DistributionPuzzlePhase phase = box.Phase;
            if (phase != lastPhase)
                OnPhaseChanged(lastPhase, phase);
            lastPhase = phase;

            RefreshTabs(phase);
            RefreshPartner();

            if (phase == DistributionPuzzlePhase.Completed)
            {
                finished = true;
                headline.text = $"<color=#{DefragUiTheme.Hex(RuntimeUi.Theme.accent)}>비상 전력 복구 완료!</color>";
                UiSfx.Play(UiCue.BankComplete);
                StartCoroutine(ReportAfterDelay());
                return;
            }

            bool knob = phase == DistributionPuzzlePhase.MainKnob;
            bankView.SetActive(!knob);
            knobView.SetActive(knob);
            if (knob)
            {
                if (!MinigameTutorial.HasSeen(KnobCard.Id))
                    MinigameTutorial.ShowBlocking(KnobCard, (RectTransform)transform, null);
                RefreshKnob();
                if (!hasZone) RequestSync();
                return;
            }

            int bank = DistributionOperatorHud.BankIndexFor(phase);
            bool waiting = phase is DistributionPuzzlePhase.WaitingForBankAData or DistributionPuzzlePhase.WaitingForBankBData or DistributionPuzzlePhase.WaitingForBankCData;
            if (waiting || !hasTarget || targetBank != bank)
            {
                headline.text = $"뱅크 {(char)('A' + bank)} 회로 데이터 요청 중...";
                RequestSync();
                SetRowsIdle();
                return;
            }
            RefreshBank(bank);
        }

        private void RequestSync()
        {
            if (Time.unscaledTime < nextRequest)
                return;
            nextRequest = Time.unscaledTime + RequestInterval;
            box.RequestHintSessionFromLocalPlayer();
        }

        private void OnPhaseChanged(DistributionPuzzlePhase previous, DistributionPuzzlePhase next)
        {
            bool bankFinished = previous is DistributionPuzzlePhase.BankA or DistributionPuzzlePhase.BankB or DistributionPuzzlePhase.BankC;
            if (bankFinished && next != previous)
            {
                UiSfx.Play(UiCue.BankComplete);
                hasTarget = false;
            }
            if (next == DistributionPuzzlePhase.MainKnob)
                hasZone = false;
        }

        private void OnHintReceived(ushort mask, int bank)
        {
            targetMask = mask;
            targetBank = bank;
            hasTarget = true;
            for (int i = 0; i < rowMatched.Length; i++)
                rowMatched[i] = false;
            UiSfx.Play(UiCue.KnobZone);
        }

        private void OnKnobZone(float center, float width, int round, int total)
        {
            hasZone = true;
            float low = Mathf.Clamp(Mathf.Round((center - width * 0.5f) * 20f) / 2f, 0f, 10f);
            float high = Mathf.Clamp(Mathf.Round((center + width * 0.5f) * 20f) / 2f, 0f, 10f);
            knobRange.text = $"{low:0.#}  ~  {high:0.#}";
            knobRound.text = $"메인 노브  {round} / {total}";
            knobZone.anchorMin = new Vector2(Mathf.Clamp01(center - width * 0.5f), 0f);
            knobZone.anchorMax = new Vector2(Mathf.Clamp01(center + width * 0.5f), 1f);
            knobZone.offsetMin = knobZone.offsetMax = Vector2.zero;
            UiSfx.Play(UiCue.KnobZone);
        }

        private void OnKnobResolved(bool success)
        {
            knobFeedback.text = success ? "성공! 새 구간을 불러주세요" : "빗나갔어요 — 같은 구간으로 다시!";
            knobFeedback.color = success ? RuntimeUi.Theme.accent : RuntimeUi.Theme.danger;
            knobFeedbackUntil = Time.unscaledTime + 1.6f;
            UiSfx.Play(success ? UiCue.RoundClear : UiCue.RhythmMiss);
        }

        private void RefreshBank(int bank)
        {
            DefragUiTheme theme = RuntimeUi.Theme;
            ushort current = box.CurrentSwitchMask;
            int matches = 0;
            for (int i = 0; i < 5; i++)
            {
                int bit = bank * 5 + i;
                bool wantOn = (targetMask & (1 << bit)) != 0;
                bool isOn = (current & (1 << bit)) != 0;
                bool matched = wantOn == isOn;
                if (matched) matches++;
                if (matched != rowMatched[i])
                {
                    UiSfx.Play(matched ? UiCue.MonitorMatch : UiCue.MonitorMismatch, 1f, 1f + i * 0.08f);
                    rowMatched[i] = matched;
                }

                rowTargets[i].text = wantOn ? "목표  <color=#FFB347>ON</color>" : "목표  <color=#FFB347>OFF</color>";
                rowCurrents[i].text = isOn ? "지금 ON" : "지금 OFF";
                rowStates[i].text = matched ? "맞음" : (wantOn ? "→ ON으로" : "→ OFF로");
                rowStates[i].color = matched ? theme.accent : theme.danger;
                rowPanels[i].color = matched
                    ? new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.16f)
                    : new Color(theme.danger.r, theme.danger.g, theme.danger.b, 0.14f + 0.06f * Mathf.Sin(Time.unscaledTime * 5f));
            }
            matchCounter.text = $"일치  {matches} / 5";
            matchCounter.color = matches == 5 ? theme.accent : theme.text;
            headline.text = $"뱅크 {(char)('A' + bank)}  <size=70%><color=#{DefragUiTheme.Hex(theme.dim)}>// 빨간 줄을 동료에게 알려주세요</color></size>";
        }

        private void SetRowsIdle()
        {
            for (int i = 0; i < rowPanels.Count; i++)
            {
                rowTargets[i].text = "목표  --";
                rowCurrents[i].text = "";
                rowStates[i].text = "";
                rowPanels[i].color = RuntimeUi.Theme.panel;
            }
            matchCounter.text = "";
        }

        private void RefreshKnob()
        {
            headline.text = "메인 노브 동기화";
            if (!hasZone)
                knobRange.text = "구간 수신 대기";
            knobFeedback.gameObject.SetActive(Time.unscaledTime < knobFeedbackUntil);
        }

        private void RefreshTabs(DistributionPuzzlePhase phase)
        {
            DefragUiTheme theme = RuntimeUi.Theme;
            int active = DistributionOperatorHud.BankIndexFor(phase);
            bool knobOrDone = phase is DistributionPuzzlePhase.MainKnob or DistributionPuzzlePhase.Completed;
            for (int i = 0; i < bankTabs.Length; i++)
            {
                bool done = knobOrDone || (active >= 0 && i < active);
                bankTabs[i].text = done ? $"{(char)('A' + i)}  완료" : $"뱅크 {(char)('A' + i)}";
                bankTabs[i].color = done ? theme.accent : i == active ? theme.highlight : theme.dim;
            }
        }

        private void RefreshPartner()
        {
            DefragUiTheme theme = RuntimeUi.Theme;
            partnerText.text = box.HasOperator
                ? $"<color=#{DefragUiTheme.Hex(theme.accent)}>● 동료가 배전함을 조작 중</color>"
                : $"<color=#{DefragUiTheme.Hex(theme.info)}>● 동료가 아직 배전함을 열지 않았어요</color>\n<size=80%>배전함 앞에서 E를 길게 누르라고 알려주세요</size>";
        }

        private IEnumerator ReportAfterDelay()
        {
            yield return new WaitForSecondsRealtime(1.4f);
            ReportSuccess();
        }

        private void BuildInterface()
        {
            DefragUiTheme theme = RuntimeUi.Theme;
            Image main = RuntimeUi.FramedPanel("Monitor", transform, theme.panel, 18f);
            RuntimeUi.Place(main.rectTransform, Vector2.zero, new Vector2(0.66f, 1f));

            headline = Text(main.transform, 30f, TextAlignmentOptions.MidlineLeft, new Vector2(0.04f, 0.88f), new Vector2(0.96f, 0.98f));
            for (int i = 0; i < bankTabs.Length; i++)
                bankTabs[i] = Text(main.transform, 22f, TextAlignmentOptions.Center, new Vector2(0.04f + i * 0.2f, 0.8f), new Vector2(0.22f + i * 0.2f, 0.87f));
            matchCounter = Text(main.transform, 28f, TextAlignmentOptions.MidlineRight, new Vector2(0.66f, 0.8f), new Vector2(0.96f, 0.87f));

            bankView = RuntimeUi.Panel("Bank View", main.transform, Color.clear).gameObject;
            RuntimeUi.Place((RectTransform)bankView.transform, new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.78f));
            for (int i = 0; i < 5; i++)
            {
                Image row = RuntimeUi.Panel($"Switch Row {i + 1}", bankView.transform, theme.panel);
                RuntimeUi.Place(row.rectTransform, new Vector2(0f, 1f - (i + 1) * 0.2f + 0.01f), new Vector2(1f, 1f - i * 0.2f - 0.01f));
                TMP_Text number = Text(row.transform, 44f, TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(0.12f, 1f));
                number.text = (i + 1).ToString();
                number.color = theme.info;
                rowTargets.Add(Text(row.transform, 30f, TextAlignmentOptions.MidlineLeft, new Vector2(0.14f, 0f), new Vector2(0.44f, 1f)));
                TMP_Text currentText = Text(row.transform, 22f, TextAlignmentOptions.MidlineLeft, new Vector2(0.45f, 0f), new Vector2(0.68f, 1f));
                currentText.color = theme.dim;
                rowCurrents.Add(currentText);
                rowStates.Add(Text(row.transform, 30f, TextAlignmentOptions.MidlineRight, new Vector2(0.68f, 0f), new Vector2(0.97f, 1f)));
                rowPanels.Add(row);
            }

            knobView = RuntimeUi.Panel("Knob View", main.transform, Color.clear).gameObject;
            RuntimeUi.Place((RectTransform)knobView.transform, new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.78f));
            knobRound = Text(knobView.transform, 26f, TextAlignmentOptions.Center, new Vector2(0f, 0.84f), new Vector2(1f, 0.98f));
            TMP_Text rangeTitle = Text(knobView.transform, 24f, TextAlignmentOptions.Center, new Vector2(0f, 0.7f), new Vector2(1f, 0.82f));
            rangeTitle.text = "동료에게 불러줄 성공 구간";
            rangeTitle.color = theme.dim;
            knobRange = Text(knobView.transform, 96f, TextAlignmentOptions.Center, new Vector2(0f, 0.4f), new Vector2(1f, 0.7f));
            knobRange.color = theme.accent;

            Image track = RuntimeUi.Panel("Zone Track", knobView.transform, new Color(0f, 0f, 0f, 0.6f));
            RuntimeUi.Place(track.rectTransform, new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.34f));
            knobZone = RuntimeUi.Panel("Zone", track.transform, new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.7f)).rectTransform;
            for (int tick = 0; tick <= 10; tick++)
            {
                Image mark = RuntimeUi.Panel($"Tick {tick}", track.transform, new Color(1f, 1f, 1f, 0.3f));
                mark.rectTransform.anchorMin = new Vector2(tick / 10f, 0f);
                mark.rectTransform.anchorMax = new Vector2(tick / 10f, 1f);
                mark.rectTransform.sizeDelta = new Vector2(2f, 0f);
                TMP_Text label = RuntimeUi.Text($"Tick {tick}", track.transform, 22f, TextAlignmentOptions.Center, terminalFont, theme.info);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(tick / 10f, 0f);
                label.rectTransform.pivot = new Vector2(0.5f, 1f);
                label.rectTransform.sizeDelta = new Vector2(44f, 30f);
                label.rectTransform.anchoredPosition = new Vector2(0f, -4f);
                label.text = tick.ToString();
            }
            knobFeedback = Text(knobView.transform, 28f, TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(1f, 0.12f));
            knobView.SetActive(false);

            Image side = RuntimeUi.FramedPanel("Side", transform, theme.panel, 18f);
            RuntimeUi.Place(side.rectTransform, new Vector2(0.68f, 0f), Vector2.one);
            TMP_Text sideTitle = Text(side.transform, 24f, TextAlignmentOptions.MidlineLeft, new Vector2(0.06f, 0.88f), new Vector2(0.94f, 0.98f));
            sideTitle.text = "동료 상태";
            sideTitle.color = theme.dim;
            partnerText = Text(side.transform, 22f, TextAlignmentOptions.TopLeft, new Vector2(0.06f, 0.62f), new Vector2(0.94f, 0.86f));
            partnerText.enableAutoSizing = true;
            partnerText.fontSizeMin = 14f;
            partnerText.fontSizeMax = 22f;
            TMP_Text guide = Text(side.transform, 20f, TextAlignmentOptions.TopLeft, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.58f));
            guide.enableAutoSizing = true;
            guide.fontSizeMin = 13f;
            guide.fontSizeMax = 20f;
            guide.color = theme.text;
            guide.text =
                "배전함 스위치는\n<color=#FFB347>왼쪽 OFF / 오른쪽 ON</color>\n으로 밀어요.\n\n" +
                "동료 화면에도 같은\n번호 1~5가 떠 있어요.\n\n" +
                "\"2번 ON, 4번 OFF!\"\n처럼 짧게 말해주세요.";
            SetRowsIdle();
        }

        private TMP_Text Text(Transform parent, float size, TextAlignmentOptions alignment, Vector2 min, Vector2 max)
        {
            TMP_Text text = RuntimeUi.Text("Label", parent, size, alignment, terminalFont, RuntimeUi.Theme.text);
            RuntimeUi.Place(text.rectTransform, min, max);
            return text;
        }
    }
}
