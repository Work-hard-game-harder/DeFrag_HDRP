using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class DownloadDataTypingMinigame : HackingMinigameBase
{
    private static readonly Color Green = new(0.1f, 1f, 0.2f);
    private static readonly Color MutedGreen = new(0.02f, 0.48f, 0.09f);
    private static readonly Color ErrorRed = new(1f, 0.12f, 0.08f);

    [Header("Command Generation")]
    [SerializeField] private DownloadCommandWordLibrary wordLibrary;
    [SerializeField, Min(1)] private int authenticationRounds = 3;

    private ConnectionDevice device;
    private CooperativeTerminalHintRelay hintRelay;
    private TMP_Text log;
    private TMP_Text target;
    private TMP_Text progress;
    private TMP_InputField input;
    private DownloadCommand currentCommand;
    private int currentRound;
    private bool acceptingInput;

    public override bool ConsumesTextInput => true;
    public override string ControlHint => "[ENTER] TRANSMIT    [ESC] ABORT";

    public override void Begin(ConnectionDevice terminal, TerminalCommands command)
    {
        device = terminal;
        hintRelay = Camera.main.GetComponentInParent<CooperativeTerminalHintRelay>();
        BuildInterface();
        log.text =
            $"> EXEC DOWNLOAD_DATA_ARCHIVE_{device.ArchiveNumber:00}\n" +
            "> ESTABLISHING REMOTE AUTHENTICATION...\n" +
            "> THREE UNCORRUPTED COMMANDS REQUIRED";
        StartRound();
    }

    public override void End()
    {
        StopAllCoroutines();
        hintRelay?.HideForTeammate();
    }

    private void StartRound()
    {
        currentCommand = wordLibrary.CreateCommand(device.ArchiveNumber);
        currentRound++;

        int hiddenTokenIndex = Random.Range(1, 3);
        string hiddenToken = currentCommand.TokenAt(hiddenTokenIndex);
        target.text = currentCommand.ObscuredText(hiddenTokenIndex);
        progress.text =
            $"AUTHENTICATION SEQUENCE {currentRound:00}/{authenticationRounds:00}\n" +
            "REMOTE FRAGMENT REQUIRED";
        input.text = string.Empty;
        acceptingInput = true;
        input.interactable = true;
        input.ActivateInputField();
        EventSystem.current.SetSelectedGameObject(input.gameObject);

        hintRelay?.ShowForTeammate(
            $"DOWNLOAD_DATA_ARCHIVE_{device.ArchiveNumber:00}",
            hiddenToken);
    }

    private void Submit(string submitted)
    {
        if (!acceptingInput)
            return;

        string normalized = submitted.Trim().ToUpperInvariant();
        if (normalized != currentCommand.FullText)
        {
            log.text += $"\n> ERROR: CHECKSUM MISMATCH [{currentRound:00}]";
            input.text = string.Empty;
            input.ActivateInputField();
            return;
        }

        acceptingInput = false;
        input.interactable = false;
        hintRelay?.HideForTeammate();
        log.text += $"\n> ACCEPTED: {currentCommand.FullText}";

        if (currentRound >= authenticationRounds)
            StartCoroutine(CompleteAfterDelay());
        else
            StartCoroutine(NextRoundAfterDelay());
    }

    private IEnumerator NextRoundAfterDelay()
    {
        progress.text = "FRAGMENT VERIFIED // ADVANCING";
        yield return new WaitForSecondsRealtime(0.65f);
        StartRound();
    }

    private IEnumerator CompleteAfterDelay()
    {
        progress.text = "ARCHIVE TRANSFER COMPLETE";
        yield return new WaitForSecondsRealtime(0.9f);
        ReportSuccess();
    }

    private void BuildInterface()
    {
        RectTransform root = (RectTransform)transform;

        log = CreateText("System Log", 20f, TextAlignmentOptions.TopLeft);
        Place(log.rectTransform, new Vector2(0f, 0.62f), Vector2.one,
            new Vector2(10f, 0f), new Vector2(-10f, 0f));
        log.color = MutedGreen;

        progress = CreateText("Progress", 23f, TextAlignmentOptions.BottomLeft);
        Place(progress.rectTransform, new Vector2(0f, 0.46f), new Vector2(1f, 0.62f),
            new Vector2(10f, 0f), new Vector2(-10f, 0f));

        target = CreateText("Obscured Command", 30f, TextAlignmentOptions.Center);
        Place(target.rectTransform, new Vector2(0f, 0.25f), new Vector2(1f, 0.46f),
            new Vector2(10f, 0f), new Vector2(-10f, 0f));
        target.enableAutoSizing = true;
        target.fontSizeMin = 18f;
        target.fontSizeMax = 30f;

        GameObject inputObject = new(
            "Command Input",
            typeof(RectTransform),
            typeof(Image),
            typeof(TMP_InputField));
        inputObject.transform.SetParent(root, false);
        RectTransform inputRect = (RectTransform)inputObject.transform;
        Place(inputRect, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.22f),
            Vector2.zero, Vector2.zero);
        inputObject.GetComponent<Image>().color = new Color(0f, 0.12f, 0.02f, 0.88f);

        TMP_Text inputText = CreateText(
            "Text",
            25f,
            TextAlignmentOptions.MidlineLeft,
            inputObject.transform);
        Stretch(inputText.rectTransform, new Vector2(18f, 6f), new Vector2(-18f, -6f));

        TMP_Text placeholder = CreateText(
            "Placeholder",
            25f,
            TextAlignmentOptions.MidlineLeft,
            inputObject.transform);
        Stretch(placeholder.rectTransform, new Vector2(18f, 6f), new Vector2(-18f, -6f));
        placeholder.text = "> TYPE COMPLETE COMMAND";
        placeholder.color = new Color(Green.r, Green.g, Green.b, 0.35f);

        input = inputObject.GetComponent<TMP_InputField>();
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 96;
        input.caretColor = Green;
        input.selectionColor = new Color(0.1f, 1f, 0.2f, 0.3f);
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
