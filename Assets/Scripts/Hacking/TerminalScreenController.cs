using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class TerminalScreenController : MonoBehaviour
{
    private static readonly Color TerminalGreen = new(0.1f, 1f, 0.2f);
    private static readonly Color DeniedRed = new(1f, 0.08f, 0.08f);

    private readonly List<Button> buttons = new();
    private ConnectionDevice device;
    private RectTransform content;
    private VerticalLayoutGroup menuLayout;
    private TMP_Text header;
    private TMP_Text status;
    private TMP_Text deniedMessage;
    private HackingMinigameBase activeMinigame;
    private TerminalCommands activeCommand;
    private System.Action closeRequested;
    private Coroutine deniedRoutine;
    private TerminalSfxPlayer terminalSfx;
    private int selection;

    public void Initialize(ConnectionDevice terminal, System.Action onClose)
    {
        device = terminal;
        terminalSfx = terminal.TerminalSfx;
        closeRequested = onClose;
        BuildFrame();
        ShowMenu();
        terminalSfx?.PlaySessionOpened();
    }

    private void Update()
    {
        if (activeMinigame != null &&
            ((activeMinigame.ConsumesTextInput && Input.GetKeyDown(KeyCode.Escape)) ||
             (!activeMinigame.ConsumesTextInput && Input.GetKeyDown(KeyCode.Backspace))))
        {
            terminalSfx?.PlayMenuBack();
            CancelMinigame();
            return;
        }

        if (activeMinigame != null || buttons.Count == 0)
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
        menuLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        menuLayout.spacing = 14f;
        menuLayout.childControlHeight = false;
        menuLayout.childControlWidth = true;
        menuLayout.childForceExpandHeight = false;

        status = CreateText("Status", screen, 23f, TextAlignmentOptions.BottomLeft);
        Place(status.rectTransform, Vector2.zero, new Vector2(1f, 0.12f),
            new Vector2(35f, 20f), new Vector2(-35f, -10f));

        deniedMessage = CreateText("Denied Access", screen, 52f, TextAlignmentOptions.Center);
        Place(deniedMessage.rectTransform, new Vector2(0.18f, 0.38f), new Vector2(0.82f, 0.62f),
            Vector2.zero, Vector2.zero);
        deniedMessage.color = DeniedRed;
        deniedMessage.text = "DENIED ACCESS";
        deniedMessage.gameObject.SetActive(false);
    }

    private void ShowMenu()
    {
        ClearContent();
        menuLayout.enabled = true;
        header.text = $"DEFRAG SECURE LINK // {device.DisplayName}\nACCESS LEVEL: ROOT";
        status.text = "[W/S] SELECT    [E] EXECUTE";

        AddCommand(TerminalCommands.UnlockDoor);
        AddCommand(TerminalCommands.DownloadData);
        AddCommand(TerminalCommands.ConnectServer);
        AddButton("> EXIT TERMINAL", ExitTerminal);
        Select(0, false);
    }

    private void AddCommand(TerminalCommands command)
    {
        HackingMinigameBase minigame = device.GetMinigame(command);
        string minigameName = minigame == null ? "NO MODULE" : minigame.DisplayName;
        AddButton($"> {TerminalCommandLabel.Get(command)}  //  {minigameName}", () => Execute(command));
    }

    private void Execute(TerminalCommands command)
    {
        terminalSfx?.PlayMenuSelected();
        HackingMinigameBase prefab = device.GetMinigame(command);
        if (!device.IsCommandEnabled(command) || prefab == null || device.IsCompleted(command))
        {
            ShowDeniedAccess();
            return;
        }

        ClearContent();
        menuLayout.enabled = false;
        activeCommand = command;
        activeMinigame = Instantiate(prefab, content);
        activeMinigame.Succeeded += CompleteMinigame;
        activeMinigame.Failed += FailMinigame;
        activeMinigame.Cancelled += CancelMinigame;
        header.text = $"{device.DisplayName} // {TerminalCommandLabel.Get(command)}";
        status.text = activeMinigame.ControlHint;
        activeMinigame.Begin(device, command);
    }

    private void ShowDeniedAccess()
    {
        terminalSfx?.PlayIncorrectAnswer();
        if (deniedRoutine != null)
            StopCoroutine(deniedRoutine);
        deniedRoutine = StartCoroutine(BlinkDeniedAccess());
    }

    private void CompleteMinigame()
    {
        bool closeTerminal = activeMinigame.CloseTerminalOnSuccess;
        device.RequestCommandCompletion(activeCommand);
        if (closeTerminal)
        {
            DestroyMinigame();
            closeRequested();
            return;
        }

        FinishMinigame($"{TerminalCommandLabel.Get(activeCommand)} // COMPLETE");
    }

    private void FailMinigame()
    {
        FinishMinigame("ACCESS DENIED // SESSION RESET");
    }

    private void CancelMinigame()
    {
        DestroyMinigame();
        ShowMenu();
    }

    private void ExitTerminal()
    {
        terminalSfx?.PlayMenuBack();
        closeRequested();
    }

    private void FinishMinigame(string message)
    {
        DestroyMinigame();
        status.text = message;
        StartCoroutine(ReturnToMenu());
    }

    private void DestroyMinigame()
    {
        activeMinigame.Succeeded -= CompleteMinigame;
        activeMinigame.Failed -= FailMinigame;
        activeMinigame.Cancelled -= CancelMinigame;
        activeMinigame.End();
        Destroy(activeMinigame.gameObject);
        activeMinigame = null;
    }

    private IEnumerator ReturnToMenu()
    {
        yield return new WaitForSecondsRealtime(1.2f);
        ShowMenu();
    }

    private IEnumerator BlinkDeniedAccess()
    {
        for (int i = 0; i < 6; i++)
        {
            deniedMessage.gameObject.SetActive(i % 2 == 0);
            yield return new WaitForSecondsRealtime(0.18f);
        }

        deniedMessage.gameObject.SetActive(false);
        deniedRoutine = null;
    }

    private void AddButton(string label, System.Action action)
    {
        GameObject row = new(label, typeof(RectTransform), typeof(LayoutElement));
        row.transform.SetParent(content, false);
        row.GetComponent<LayoutElement>().preferredHeight = 48f;

        GameObject background = new("Selection", typeof(RectTransform), typeof(Image), typeof(Button));
        background.transform.SetParent(row.transform, false);
        Stretch((RectTransform)background.transform, Vector2.zero, Vector2.zero);

        Button button = background.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0f, 0.12f, 0.02f, 0.75f);
        colors.highlightedColor = new Color(0.02f, 0.35f, 0.06f, 0.9f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.08f, 0.6f, 0.13f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => action());

        TMP_Text text = CreateText("Label", row.transform, 25f, TextAlignmentOptions.MidlineLeft);
        text.text = label;
        Stretch(text.rectTransform, new Vector2(18f, 0f), new Vector2(-10f, 0f));
        text.outlineColor = Color.black;
        text.outlineWidth = 0.18f;
        buttons.Add(button);
    }

    private void Select(int index, bool playSound = true)
    {
        selection = (index + buttons.Count) % buttons.Count;
        EventSystem.current.SetSelectedGameObject(buttons[selection].gameObject);
        if (playSound)
            terminalSfx?.PlayMenuSelected();
    }

    private void ClearContent()
    {
        foreach (Transform child in content)
            Destroy(child.gameObject);
        buttons.Clear();
        selection = 0;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        float size,
        TextAlignmentOptions alignment)
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
