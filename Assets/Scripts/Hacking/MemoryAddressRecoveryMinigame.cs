using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Unlock-door module: match each lost address pattern (first + last digit) to a candidate card, in order.
public sealed class MemoryAddressRecoveryMinigame : HackingMinigameBase
{
    [Header("Memory Sequence")]
    [SerializeField, Min(1)] private int addressCount = 3;
    [SerializeField, Min(0f)] private float dumpDuration = 1.2f;

    [Header("Presentation")]
    [SerializeField] private TMP_FontAsset terminalFont;

    private readonly List<string> originalSequence = new();
    private readonly List<string> candidates = new();
    private readonly List<Image> slotPanels = new();
    private readonly List<TMP_Text> slotLabels = new();
    private readonly List<Image> cardPanels = new();
    private readonly List<TMP_Text> cardLabels = new();
    private readonly List<bool> cardUsed = new();

    private TMP_Text instructionText;
    private TMP_Text dumpText;
    private int selection;
    private int nextAddress;
    private bool acceptingInput;
    private bool finished;

    public override string ControlHint => TerminalScreenController.KeyHints(
        ("A/D", "선택"), ("E", "확정"), ("클릭", "바로 선택"), ("BACKSPACE", "메뉴로"));

    public override void Begin(ConnectionDevice device, TerminalCommands command)
    {
        GenerateUniqueAddresses();
        BuildInterface();
        MinigameTutorial.ShowBlocking(new TutorialCard
        {
            Id = "terminal.address",
            Role = "해커 • 문 잠금 해제",
            Title = "주소 복구",
            Goal = "위쪽 '복구 순서'대로 맞는 주소 카드를 골라 문 잠금을 풉니다.",
            Steps = new[]
            {
                ("", "위쪽 슬롯에 첫 글자와 마지막 글자만 남은 주소가 있어요. 예: 0x8??6"),
                ("", "아래 카드 중 첫 글자와 마지막 글자가 같은 주소를 찾으세요. (주황색으로 강조)"),
                ("A / D  +  E", "키보드로 고르거나, 마우스로 카드를 바로 클릭하세요."),
                ("", "틀려도 벌점은 없어요. 슬롯 1부터 순서대로!")
            }
        }, (RectTransform)transform, () => StartCoroutine(DumpAndStart()));
    }

    public override void End()
    {
        StopAllCoroutines();
    }

    private void Update()
    {
        AnimateCards();
        if (!acceptingInput || finished)
            return;

        if (TerminalKeyboardInput.LeftPressed)
            MoveSelection(-1);
        else if (TerminalKeyboardInput.RightPressed)
            MoveSelection(1);
        else if (TerminalKeyboardInput.ConfirmPressed)
            Submit(selection);
    }

    private IEnumerator DumpAndStart()
    {
        UiSfx.Play(UiCue.MemoryShuffle);
        for (float t = 0f; t < dumpDuration; t += 0.08f)
        {
            dumpText.text = $"> 메모리 덤프 재구성 중... {Mathf.RoundToInt(t / dumpDuration * 100f):00}%\n> {Random.Range(0x1000, 0xFFFF):X4} {Random.Range(0x1000, 0xFFFF):X4} {Random.Range(0x1000, 0xFFFF):X4}";
            yield return new WaitForSecondsRealtime(0.08f);
        }
        dumpText.text = "> 주소 테이블 복구 준비 완료";
        acceptingInput = true;
        RefreshSlots();
        Select(0, false);
    }

    private void Submit(int index)
    {
        if (!acceptingInput || finished || cardUsed[index])
            return;

        if (candidates[index] != originalSequence[nextAddress])
        {
            UiSfx.Play(UiCue.CardWrong);
            StartCoroutine(Shake(cardPanels[index].rectTransform));
            SetInstruction($"다른 주소예요! 첫 글자 <color=#{Amber}>{originalSequence[nextAddress][2]}</color>, 마지막 글자 <color=#{Amber}>{originalSequence[nextAddress][5]}</color>를 찾으세요", true);
            return;
        }

        UiSfx.Play(UiCue.CardCorrect, 1f, 1f + nextAddress * 0.12f);
        cardUsed[index] = true;
        nextAddress++;
        RefreshSlots();

        if (nextAddress < originalSequence.Count)
        {
            SelectNextAvailable();
            return;
        }

        finished = true;
        acceptingInput = false;
        SetInstruction("메모리 복구 완료 — 잠금 해제!", false);
        StartCoroutine(ReportSuccessAfterDelay());
    }

    private IEnumerator ReportSuccessAfterDelay()
    {
        yield return new WaitForSecondsRealtime(0.8f);
        ReportSuccess();
    }

    private void GenerateUniqueAddresses()
    {
        originalSequence.Clear();
        HashSet<string> signatures = new();
        while (originalSequence.Count < addressCount)
        {
            string address = $"0x{Random.Range(0x1000, 0x10000):X4}";
            if (signatures.Add(Signature(address)))
                originalSequence.Add(address);
        }

        candidates.Clear();
        candidates.AddRange(originalSequence);
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int swap = Random.Range(0, i + 1);
            (candidates[i], candidates[swap]) = (candidates[swap], candidates[i]);
        }
        if (candidates.Count > 1 && candidates[0] == originalSequence[0])
            (candidates[0], candidates[1]) = (candidates[1], candidates[0]);
    }

    private static string Amber => DefragUiTheme.Hex(RuntimeUi.Theme.info);

    private static string Signature(string address) => $"{address[2]}{address[5]}";

    private static string Emphasize(string address, bool hideMiddle) =>
        $"0x<color=#{Amber}>{address[2]}</color>{(hideMiddle ? $"<color=#{DefragUiTheme.Hex(RuntimeUi.Theme.dim)}>??</color>" : address.Substring(3, 2))}<color=#{Amber}>{address[5]}</color>";

    private void BuildInterface()
    {
        DefragUiTheme theme = RuntimeUi.Theme;

        TMP_Text slotTitle = CreateText("Slot Title", 22f, TextAlignmentOptions.MidlineLeft, transform);
        RuntimeUi.Place(slotTitle.rectTransform, new Vector2(0.02f, 0.9f), new Vector2(0.98f, 1f));
        slotTitle.color = theme.dim;
        slotTitle.text = "복구 순서  //  RESTORE ORDER";

        RectTransform slotRow = CreateRect("Slots", transform);
        RuntimeUi.Place(slotRow, new Vector2(0.02f, 0.62f), new Vector2(0.98f, 0.9f));
        for (int i = 0; i < originalSequence.Count; i++)
        {
            Image slot = RuntimeUi.FramedPanel($"Slot {i + 1}", slotRow, theme.panel, 14f);
            float width = 1f / originalSequence.Count;
            RuntimeUi.Place(slot.rectTransform, new Vector2(i * width + 0.01f, 0f), new Vector2((i + 1) * width - 0.01f, 1f));
            TMP_Text label = CreateText("Label", 40f, TextAlignmentOptions.Center, slot.transform);
            RuntimeUi.Stretch(label.rectTransform, 8f);
            slotPanels.Add(slot);
            slotLabels.Add(label);
        }

        instructionText = CreateText("Instruction", 28f, TextAlignmentOptions.Center, transform);
        RuntimeUi.Place(instructionText.rectTransform, new Vector2(0.02f, 0.47f), new Vector2(0.98f, 0.61f));
        instructionText.enableAutoSizing = true;
        instructionText.fontSizeMin = 16f;
        instructionText.fontSizeMax = 28f;

        RectTransform cardRow = CreateRect("Candidates", transform);
        RuntimeUi.Place(cardRow, new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.45f));
        for (int i = 0; i < candidates.Count; i++)
        {
            int index = i;
            GameObject cardObject = new($"Card {i + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
            cardObject.transform.SetParent(cardRow, false);
            float width = 1f / candidates.Count;
            RuntimeUi.Place((RectTransform)cardObject.transform, new Vector2(i * width + 0.015f, 0f), new Vector2((i + 1) * width - 0.015f, 1f));
            Image panel = cardObject.GetComponent<Image>();
            panel.color = theme.panelRaised;
            RuntimeUi.AddCornerBrackets(panel.rectTransform, 16f, 3f, theme.edge);
            Button button = cardObject.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => { Select(index); Submit(index); });
            cardObject.AddComponent<MenuHoverSelect>().Hovered = () => { if (acceptingInput && selection != index) Select(index); };

            TMP_Text label = CreateText("Address", 50f, TextAlignmentOptions.Center, cardObject.transform);
            RuntimeUi.Stretch(label.rectTransform, 6f);
            label.text = Emphasize(candidates[i], false);
            cardPanels.Add(panel);
            cardLabels.Add(label);
            cardUsed.Add(false);
        }

        dumpText = CreateText("Dump", 19f, TextAlignmentOptions.BottomLeft, transform);
        RuntimeUi.Place(dumpText.rectTransform, new Vector2(0.02f, 0f), new Vector2(0.98f, 0.12f));
        dumpText.color = theme.dim;
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        for (int i = 0; i < slotLabels.Count; i++)
        {
            bool done = i < nextAddress;
            bool current = i == nextAddress;
            slotLabels[i].text = done
                ? $"<size=55%><color=#{DefragUiTheme.Hex(theme.accent)}>슬롯 {i + 1}  완료</color></size>\n{originalSequence[i]}"
                : $"<size=55%>{(current ? "→ " : "")}슬롯 {i + 1}</size>\n{Emphasize(originalSequence[i], true)}";
            slotLabels[i].color = done ? theme.accent : current ? theme.highlight : theme.dim;
            slotPanels[i].color = current ? new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.12f) : theme.panel;
        }

        if (nextAddress < originalSequence.Count)
            SetInstruction($"슬롯 {nextAddress + 1}: 첫 글자 <color=#{Amber}>{originalSequence[nextAddress][2]}</color>, 마지막 글자 <color=#{Amber}>{originalSequence[nextAddress][5]}</color>인 카드를 고르세요", false);
    }

    private void SetInstruction(string text, bool error)
    {
        instructionText.text = text;
        instructionText.color = error ? RuntimeUi.Theme.danger : RuntimeUi.Theme.text;
    }

    private void SelectNextAvailable()
    {
        for (int offset = 1; offset <= cardPanels.Count; offset++)
        {
            int index = (selection + offset) % cardPanels.Count;
            if (!cardUsed[index])
            {
                Select(index, false);
                return;
            }
        }
    }

    private void MoveSelection(int direction)
    {
        for (int offset = 1; offset <= cardPanels.Count; offset++)
        {
            int index = (selection + direction * offset + cardPanels.Count * 2) % cardPanels.Count;
            if (cardUsed[index])
                continue;
            Select(index);
            return;
        }
    }

    private void Select(int index, bool playSound = true)
    {
        selection = (index + cardPanels.Count) % cardPanels.Count;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(cardPanels[selection].gameObject);
        if (playSound)
            UiSfx.Play(UiCue.CardMove);
    }

    private void AnimateCards()
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        for (int i = 0; i < cardPanels.Count; i++)
        {
            bool used = cardUsed[i];
            bool selected = acceptingInput && i == selection && !used;
            cardPanels[i].color = used
                ? new Color(0f, 0f, 0f, 0.35f)
                : selected ? new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.3f) : theme.panelRaised;
            cardLabels[i].alpha = used ? 0.25f : 1f;
            float scale = selected ? 1.05f + 0.01f * Mathf.Sin(Time.unscaledTime * 6f) : 1f;
            cardPanels[i].rectTransform.localScale = Vector3.one * scale;
        }
    }

    private static IEnumerator Shake(RectTransform target)
    {
        Vector2 origin = target.anchoredPosition;
        for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
        {
            target.anchoredPosition = origin + Vector2.right * Mathf.Sin(t * 90f) * 14f * (1f - t / 0.3f);
            yield return null;
        }
        target.anchoredPosition = origin;
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
