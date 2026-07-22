using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class TerminalScreenController : MonoBehaviour
{
    private static readonly string[] Words =
    {
        "CISTERN", "WIRETAP", "HILLTOP", "WYOMING",
        "DEPOSIT", "CAUSTIC", "SPRYER", "LETTERS"
    };

    private static readonly Color TerminalGreen = new(0.1f, 1f, 0.2f);
    private static readonly Color DimGreen = new(0.03f, 0.28f, 0.06f);

    private readonly List<Button> buttons = new();
    private ConnectionDevice device;
    private RectTransform content;
    private TMP_Text header;
    private TMP_Text status;
    private System.Action closeRequested;
    private string password;
    private TerminalCommands activeCommand;
    private int attempts;
    private int selection;

    public void Initialize(ConnectionDevice terminal, System.Action onClose)
    {
        device = terminal;
        closeRequested = onClose;
        BuildFrame();
        ShowMenu();
    }

    private void Update()
    {
        if (buttons.Count == 0)
            return;

        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            Select(selection - 1);
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            Select(selection + 1);
        else if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return))
            buttons[selection].onClick.Invoke();
    }

    private void BuildFrame()
    {
        RectTransform root = GetComponent<RectTransform>();
        Stretch(root, Vector2.zero, Vector2.zero);
        gameObject.AddComponent<Image>().color = new Color(0.035f, 0.04f, 0.035f, 0.98f);

        RectTransform screen = CreateRect("Terminal Screen", root);
        Stretch(screen, new Vector2(105f, 75f), new Vector2(-105f, -75f));
        screen.gameObject.AddComponent<Image>().color = Color.black;

        header = CreateText("Header", screen, 31f, TextAlignmentOptions.TopLeft);
        Place(header.rectTransform, new Vector2(0f, 0.84f), Vector2.one,
            new Vector2(35f, 10f), new Vector2(-35f, -20f));

        content = CreateRect("Content", screen);
        Place(content, new Vector2(0f, 0.12f), new Vector2(1f, 0.84f),
            new Vector2(65f, 15f), new Vector2(-65f, -15f));
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        status = CreateText("Status", screen, 23f, TextAlignmentOptions.BottomLeft);
        Place(status.rectTransform, Vector2.zero, new Vector2(1f, 0.12f),
            new Vector2(35f, 20f), new Vector2(-35f, -10f));
    }

    private void ShowMenu()
    {
        ClearContent();
        header.text = $"DEFRAG SECURE LINK // {device.DisplayName}\nACCESS LEVEL: ROOT";
        status.text = "[W/S] SELECT    [E] EXECUTE    [ESC] DISCONNECT";

        AddCommand(TerminalCommands.UnlockDoor);
        AddCommand(TerminalCommands.DownloadData);
        AddCommand(TerminalCommands.ConnectServer);
        AddButton("DISCONNECT", closeRequested);
        Select(0);
    }

    private void AddCommand(TerminalCommands command)
    {
        if ((device.AvailableCommands & command) == 0)
            return;

        AddButton($"> {TerminalCommandLabel.Get(command)}", () => BeginPasswordCrack(command));
    }

    private void BeginPasswordCrack(TerminalCommands command)
    {
        ClearContent();
        activeCommand = command;
        attempts = 4;
        password = Words[Random.Range(0, Words.Length)];
        header.text = $"{device.DisplayName} // {TerminalCommandLabel.Get(command)}\nPASSWORD REQUIRED";
        status.text = $"ATTEMPTS REMAINING: {attempts}";

        TMP_Text code = CreateText("Code", content, 20f, TextAlignmentOptions.TopLeft);
        code.text = "0x100  {UF:M}  WIRETAP   #<=L;  CISTERN\n" +
                    "0x110  ?B+@:-  HILLTOP   %CZ<<  CAUSTIC\n" +
                    "0x120  [<EW$  DEPOSIT   YN]}O  LETTERS";
        code.color = DimGreen;
        code.gameObject.AddComponent<LayoutElement>().preferredHeight = 100f;

        foreach (string word in Words)
        {
            string candidate = word;
            AddButton(candidate, () => Submit(candidate));
        }

        AddButton("< RETURN", ShowMenu);
        Select(0);
    }

    private void Submit(string candidate)
    {
        if (candidate == password)
        {
            device.RequestCommandCompletion(activeCommand);
            status.text = $"ACCESS GRANTED // {TerminalCommandLabel.Get(activeCommand)} COMPLETE";
            SetButtonsEnabled(false);
            StartCoroutine(ReturnToMenu());
            return;
        }

        attempts--;
        status.text = attempts > 0
            ? $"INVALID RESPONSE: {candidate}    MATCH {Similarity(candidate, password)}/{password.Length}    ATTEMPTS: {attempts}"
            : "ACCESS DENIED // SESSION RESET";

        if (attempts == 0)
        {
            SetButtonsEnabled(false);
            StartCoroutine(ReturnToMenu());
        }
    }

    private IEnumerator ReturnToMenu()
    {
        yield return new WaitForSecondsRealtime(1.3f);
        ShowMenu();
    }

    private void AddButton(string label, System.Action action)
    {
        GameObject buttonObject = new(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(content, false);
        buttonObject.GetComponent<LayoutElement>().preferredHeight = 48f;
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0f, 0.12f, 0.02f, 0.75f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0f, 0.12f, 0.02f, 0.75f);
        colors.highlightedColor = new Color(0.02f, 0.35f, 0.06f, 0.9f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.08f, 0.6f, 0.13f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => action());

        TMP_Text text = CreateText("Label", buttonObject.transform, 25f, TextAlignmentOptions.MidlineLeft);
        Stretch(text.rectTransform, new Vector2(18f, 0f), new Vector2(-10f, 0f));
        buttons.Add(button);
    }

    private void Select(int index)
    {
        selection = (index + buttons.Count) % buttons.Count;
        EventSystem.current.SetSelectedGameObject(buttons[selection].gameObject);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        foreach (Button button in buttons)
            button.interactable = enabled;
    }

    private void ClearContent()
    {
        foreach (Transform child in content)
            Destroy(child.gameObject);
        buttons.Clear();
        selection = 0;
    }

    private static int Similarity(string left, string right)
    {
        int matches = 0;
        for (int i = 0; i < left.Length; i++)
            if (left[i] == right[i]) matches++;
        return matches;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    private static TMP_Text CreateText(string name, Transform parent, float size, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(name, parent);
        TMP_Text text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = TerminalGreen;
        text.alignment = alignment;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 minOffset, Vector2 maxOffset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = minOffset;
        rect.offsetMax = maxOffset;
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
