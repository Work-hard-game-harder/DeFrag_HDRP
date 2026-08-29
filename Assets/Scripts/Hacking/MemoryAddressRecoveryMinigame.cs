using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MemoryAddressRecoveryMinigame : HackingMinigameBase
{
    private static readonly Color Green = new(0.1f, 1f, 0.2f);
    private static readonly Color DimGreen = new(0.02f, 0.45f, 0.08f);
    private static readonly Color ErrorRed = new(1f, 0.1f, 0.08f);
    private static readonly Color AcceptedWhite = new(0.8f, 1f, 0.82f);
    private static readonly Color BrightSelection = new(0.12f, 1f, 0.22f, 1f);
    private static readonly Color NormalButton = new(0f, 0.1f, 0.02f, 0.95f);
    private static readonly Color CompletedButton = new(0.02f, 0.24f, 0.07f, 0.9f);

    [Header("Memory Sequence")]
    [SerializeField, Min(1)] private int addressCount = 4;
    [SerializeField, Min(0.25f)] private float revealDuration = 2.4f;
    [SerializeField, Min(0f)] private float shufflePause = 0.35f;

    [Header("Presentation")]
    [SerializeField] private TMP_FontAsset terminalFont;

    private readonly List<string> originalSequence = new();
    private readonly List<string> displayedAddresses = new();
    private readonly List<Button> addressButtons = new();
    private readonly List<TMP_Text> addressLabels = new();

    private TMP_Text dumpText;
    private TMP_Text instructionText;
    private TMP_Text restoredText;
    private int selection;
    private int nextAddress;
    private bool acceptingInput;
    private bool finished;
    private TerminalSfxPlayer terminalSfx;

    public override string ControlHint =>
        "[A/D] SELECT    [E/ENTER] CONFIRM    [BACKSPACE] RETURN";

    public override void Begin(ConnectionDevice device, TerminalCommands command)
    {
        terminalSfx = device.TerminalSfx;
        BuildInterface();
        GenerateUniqueAddresses();
        StartCoroutine(RevealAndShuffle());
    }

    public override void End()
    {
        StopAllCoroutines();
    }

    private void Update()
    {
        if (!acceptingInput || finished)
            return;

        if (TerminalKeyboardInput.LeftPressed)
            MoveSelection(-1);
        else if (TerminalKeyboardInput.RightPressed)
            MoveSelection(1);
        else if (TerminalKeyboardInput.ConfirmPressed)
            addressButtons[selection].onClick.Invoke();
    }

    private IEnumerator RevealAndShuffle()
    {
        acceptingInput = false;
        dumpText.text = BuildDump(originalSequence);
        instructionText.text =
            "휘발성 메모리 덤프 감지\n" +
            "현재 실행 순서를 확인하십시오";
        nextAddress = 0;
        UpdateRestoredSequence();

        yield return new WaitForSecondsRealtime(Mathf.Max(4.5f, revealDuration));

        dumpText.text = "> MEMORY SIGNAL LOST\n> RECONSTRUCTING ADDRESS TABLE...";
        yield return new WaitForSecondsRealtime(shufflePause);

        List<string> shuffled = new(originalSequence);
        Shuffle(shuffled);
        if (MatchesOriginal(shuffled))
            SwapFirstTwo(shuffled);

        displayedAddresses.Clear();
        for (int i = 0; i < addressButtons.Count; i++)
        {
            string address = shuffled[i];
            int buttonIndex = i;
            displayedAddresses.Add(address);
            addressLabels[i].text = address;
            addressLabels[i].color = Green;
            addressButtons[i].onClick.RemoveAllListeners();
            addressButtons[i].onClick.AddListener(() => Submit(buttonIndex, address));
            addressButtons[i].interactable = true;
        }

        UpdateRecoveryGuide();
        acceptingInput = true;
        Select(0);
    }

    private void Submit(int buttonIndex, string address)
    {
        if (!acceptingInput || finished)
            return;

        if (address != originalSequence[nextAddress])
        {
            terminalSfx?.PlayIncorrectAnswer();
            acceptingInput = false;
            instructionText.color = ErrorRed;
            instructionText.text =
                "주소 불일치 // 밝은 초록색 선택을 확인하세요\n" +
                $"현재 단서: {BuildSignature(originalSequence[nextAddress])}";
            StartCoroutine(ResumeAfterWrongSelection());
            return;
        }

        nextAddress++;
        UpdateRestoredSequence();
        DisableButton(buttonIndex);

        if (nextAddress < originalSequence.Count)
        {
            UpdateRecoveryGuide();
            SelectNextInteractable();
            return;
        }

        finished = true;
        acceptingInput = false;
        instructionText.color = AcceptedWhite;
        instructionText.text = "메모리 복구 완료 // 접근 승인";
        StartCoroutine(ReportSuccessAfterDelay());
    }

    private IEnumerator ResumeAfterWrongSelection()
    {
        yield return new WaitForSecondsRealtime(0.65f);
        instructionText.color = Green;
        UpdateRecoveryGuide();
        acceptingInput = true;
    }

    private IEnumerator ReportSuccessAfterDelay()
    {
        yield return new WaitForSecondsRealtime(0.75f);
        ReportSuccess();
    }

    private void GenerateUniqueAddresses()
    {
        originalSequence.Clear();
        HashSet<int> generated = new();
        HashSet<string> signatures = new();

        while (originalSequence.Count < addressCount)
        {
            int value = Random.Range(0x1000, 0x10000);
            string address = $"0x{value:X4}";
            if (generated.Add(value) && signatures.Add(BuildSignature(address)))
                originalSequence.Add(address);
        }
    }

    private void BuildInterface()
    {
        dumpText = CreateText("Memory Dump", 23f, TextAlignmentOptions.TopLeft);
        Place(dumpText.rectTransform, new Vector2(0f, 0.58f), Vector2.one,
            new Vector2(12f, 0f), new Vector2(-12f, 0f));
        dumpText.color = DimGreen;

        instructionText = CreateText("Instruction", 26f, TextAlignmentOptions.Center);
        Place(instructionText.rectTransform, new Vector2(0f, 0.42f), new Vector2(1f, 0.58f),
            new Vector2(8f, 0f), new Vector2(-8f, 0f));

        RectTransform buttonRow = CreateRect("Address Row", transform);
        Place(buttonRow, new Vector2(0.03f, 0.19f), new Vector2(0.97f, 0.4f),
            Vector2.zero, Vector2.zero);
        HorizontalLayoutGroup layout = buttonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = true;

        for (int i = 0; i < addressCount; i++)
            CreateAddressButton(buttonRow);

        restoredText = CreateText("Restored Sequence", 22f, TextAlignmentOptions.Center);
        Place(restoredText.rectTransform, new Vector2(0f, 0.02f), new Vector2(1f, 0.17f),
            new Vector2(8f, 0f), new Vector2(-8f, 0f));
    }

    private void CreateAddressButton(Transform parent)
    {
        GameObject buttonObject = new(
            "Memory Address",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        buttonObject.GetComponent<Image>().color = new Color(0f, 0.12f, 0.02f, 0.9f);

        Button button = buttonObject.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.interactable = false;

        TMP_Text label = CreateText(
            "Address",
            25f,
            TextAlignmentOptions.Center,
            buttonObject.transform);
        Stretch(label.rectTransform, new Vector2(6f, 4f), new Vector2(-6f, -4f));
        label.text = "0x----";
        addressButtons.Add(button);
        addressLabels.Add(label);
    }

    private void UpdateRestoredSequence()
    {
        string[] slots = new string[originalSequence.Count];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = i < nextAddress ? originalSequence[i] : "--";
        restoredText.text =
            $"RESTORED {nextAddress:00}/{originalSequence.Count:00}: " +
            $"[ {string.Join(" ] [ ", slots)} ]";
    }

    private void DisableButton(int index)
    {
        addressButtons[index].interactable = false;
        addressLabels[index].color = AcceptedWhite;
        RefreshSelectionVisual();
    }

    private void SelectNextInteractable()
    {
        for (int offset = 1; offset <= addressButtons.Count; offset++)
        {
            int index = (selection + offset) % addressButtons.Count;
            if (addressButtons[index].interactable)
            {
                Select(index);
                return;
            }
        }
    }

    private void MoveSelection(int direction)
    {
        for (int offset = 1; offset <= addressButtons.Count; offset++)
        {
            int index =
                (selection + direction * offset + addressButtons.Count * 2) %
                addressButtons.Count;
            if (!addressButtons[index].interactable)
                continue;

            Select(index);
            return;
        }
    }

    private void Select(int index)
    {
        selection = (index + addressButtons.Count) % addressButtons.Count;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(addressButtons[selection].gameObject);
        RefreshSelectionVisual();
    }

    private static string BuildDump(IReadOnlyList<string> addresses)
    {
        string result = "> READ MAGLOCK_EXECUTION_TABLE\n";
        for (int i = 0; i < addresses.Count; i++)
            result += $"  [{i + 1:00}]  {addresses[i]}\n";
        return result;
    }

    private void UpdateRecoveryGuide()
    {
        dumpText.text = BuildRecoveryGuide(originalSequence, nextAddress);
        instructionText.text =
            $"현재 목표: 슬롯 {nextAddress + 1:00}  " +
            $"{BuildSignature(originalSequence[nextAddress])}\n" +
            "밝은 초록색 주소 중 보이는 숫자가 같은 것을 선택하세요";
    }

    private static string BuildRecoveryGuide(IReadOnlyList<string> addresses, int currentSlot)
    {
        string result =
            "> 주소 복구 방법\n" +
            "> '?'는 손실된 숫자입니다. 보이는 숫자만 비교하세요.\n" +
            "> 예시: 0x8??6  ->  0x8066\n" +
            "> 슬롯 01부터 순서대로 복구하세요.\n";

        for (int i = 0; i < addresses.Count; i++)
        {
            string marker = i == currentSlot ? ">>" : "  ";
            string state = i < currentSlot ? "완료" : BuildSignature(addresses[i]);
            result += $"{marker} 슬롯 {i + 1:00}  {state}\n";
        }

        return result;
    }

    private static string BuildSignature(string address)
    {
        return $"0x{address[2]}??{address[5]}";
    }

    private void RefreshSelectionVisual()
    {
        if (displayedAddresses.Count != addressLabels.Count)
            return;

        for (int i = 0; i < addressLabels.Count; i++)
        {
            string address = displayedAddresses[i];
            bool enabled = addressButtons[i].interactable;
            bool selected = i == selection && enabled;
            addressButtons[i].image.color = !enabled
                ? CompletedButton
                : selected ? BrightSelection : NormalButton;
            addressLabels[i].color = selected
                ? Color.black
                : enabled ? Green : AcceptedWhite;
            addressLabels[i].text = selected ? $"> {address} <" : address;
        }
    }

    private static void Shuffle<T>(IList<T> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            (values[i], values[swapIndex]) = (values[swapIndex], values[i]);
        }
    }

    private bool MatchesOriginal(IReadOnlyList<string> shuffled)
    {
        for (int i = 0; i < shuffled.Count; i++)
            if (shuffled[i] != originalSequence[i])
                return false;
        return true;
    }

    private static void SwapFirstTwo(IList<string> addresses)
    {
        if (addresses.Count > 1)
            (addresses[0], addresses[1]) = (addresses[1], addresses[0]);
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

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
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
