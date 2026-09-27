using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class TerminalScreenController : MonoBehaviour
{
    private readonly List<Button> buttons = new();
    private readonly List<Image> buttonBackgrounds = new();
    private readonly List<Image> buttonAccents = new();
    private readonly List<TMP_Text> buttonLabels = new();
    private readonly List<TMP_Text> buttonDetails = new();
    private ConnectionDevice device;
    private RectTransform content;
    private VerticalLayoutGroup menuLayout;
    private TMP_Text header;
    private TMP_Text subHeader;
    private TMP_Text clock;
    private TMP_Text status;
    private TMP_Text deniedMessage;
    private HackingMinigameBase activeMinigame;
    private TerminalCommands activeCommand;
    private System.Action closeRequested;
    private Coroutine deniedRoutine;
    private TerminalSfxPlayer terminalSfx;
    private int selection;
    private bool preserveWorldTransientOnDestroy;

    public void Initialize(ConnectionDevice terminal, System.Action onClose)
    {
        device = terminal;
        terminalSfx = terminal.TerminalSfx;
        closeRequested = onClose;
        BuildFrame();
        ShowMenu();
        UiSfx.Play(UiCue.TerminalBoot);
        terminalSfx?.PlaySessionOpened();
        device.PublishWorldScreen(TerminalWorldPhase.Menu);
    }

    private void Update()
    {
        if (clock != null)
            clock.text = $"SYS {System.DateTime.Now:HH:mm:ss}\n<size=70%>LINK STABLE</size>";

        if (activeMinigame != null &&
            ((activeMinigame.ConsumesTextInput && TerminalKeyboardInput.EscapePressed) ||
             (!activeMinigame.ConsumesTextInput && TerminalKeyboardInput.BackPressed)))
        {
            UiSfx.Play(UiCue.MenuBack);
            CancelMinigame();
            return;
        }

        if (activeMinigame != null || buttons.Count == 0)
            return;

        if (TerminalKeyboardInput.UpPressed)
            Select(selection - 1);
        else if (TerminalKeyboardInput.DownPressed)
            Select(selection + 1);
        else if (TerminalKeyboardInput.ConfirmPressed)
            buttons[selection].onClick.Invoke();
    }

    private void BuildFrame()
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        RectTransform root = GetComponent<RectTransform>();
        Stretch(root, Vector2.zero, Vector2.zero);
        gameObject.AddComponent<Image>().color = theme.backdrop;

        Image screenPanel = RuntimeUi.FramedPanel("Terminal Screen", root, new Color(0.004f, 0.022f, 0.022f, 0.99f), 34f);
        RectTransform screen = screenPanel.rectTransform;
        Stretch(screen, new Vector2(90f, 60f), new Vector2(-90f, -60f));

        Image headerBand = RuntimeUi.Panel("Header Band", screen, new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.08f));
        RuntimeUi.Place(headerBand.rectTransform, new Vector2(0f, 0.855f), Vector2.one);
        Image headerRule = RuntimeUi.Panel("Header Rule", screen, theme.edge);
        RuntimeUi.Place(headerRule.rectTransform, new Vector2(0.02f, 0.853f), new Vector2(0.98f, 0.856f));

        subHeader = CreateText("Sub Header", screen, 20f, TextAlignmentOptions.TopLeft);
        Place(subHeader.rectTransform, new Vector2(0f, 0.935f), new Vector2(0.7f, 1f),
            new Vector2(38f, 0f), new Vector2(0f, -14f));
        subHeader.color = theme.dim;
        subHeader.text = "DEFRAG // SECURE HACKING LINK";

        header = CreateText("Header", screen, 36f, TextAlignmentOptions.MidlineLeft);
        Place(header.rectTransform, new Vector2(0f, 0.86f), new Vector2(0.75f, 0.94f),
            new Vector2(38f, 0f), Vector2.zero);
        header.enableAutoSizing = true;
        header.fontSizeMin = 20f;
        header.fontSizeMax = 36f;

        clock = CreateText("Clock", screen, 24f, TextAlignmentOptions.MidlineRight);
        Place(clock.rectTransform, new Vector2(0.7f, 0.86f), new Vector2(1f, 0.99f),
            Vector2.zero, new Vector2(-38f, 0f));
        clock.color = theme.accent;

        content = CreateRect("Content", screen);
        Place(content, new Vector2(0f, 0.1f), new Vector2(1f, 0.84f),
            new Vector2(50f, 12f), new Vector2(-50f, -12f));
        menuLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        menuLayout.spacing = 12f;
        menuLayout.childControlHeight = false;
        menuLayout.childControlWidth = true;
        menuLayout.childForceExpandHeight = false;

        Image footerRule = RuntimeUi.Panel("Footer Rule", screen, new Color(theme.edge.r, theme.edge.g, theme.edge.b, 0.3f));
        RuntimeUi.Place(footerRule.rectTransform, new Vector2(0.02f, 0.098f), new Vector2(0.98f, 0.1f));
        status = CreateText("Status", screen, 22f, TextAlignmentOptions.MidlineLeft);
        Place(status.rectTransform, Vector2.zero, new Vector2(1f, 0.095f),
            new Vector2(38f, 6f), new Vector2(-38f, -6f));
        status.color = theme.text;

        RuntimeUi.Scanlines(screen, 0.06f);

        deniedMessage = CreateText("Denied Access", screen, 56f, TextAlignmentOptions.Center);
        Place(deniedMessage.rectTransform, new Vector2(0.18f, 0.38f), new Vector2(0.82f, 0.62f),
            Vector2.zero, Vector2.zero);
        deniedMessage.color = theme.danger;
        deniedMessage.text = "접근 거부\n<size=45%>ACCESS DENIED // 지금은 사용할 수 없는 명령입니다</size>";
        deniedMessage.gameObject.SetActive(false);
    }

    private void ShowMenu()
    {
        ClearContent();
        menuLayout.enabled = true;
        header.text = $"{device.DisplayName}  <size=60%><color=#{DefragUiTheme.Hex(RuntimeUi.Theme.dim)}>// ROOT ACCESS</color></size>";
        status.text = KeyHints(("W/S", "선택"), ("E", "실행"));

        AddCommand(TerminalCommands.UnlockDoor);
        AddCommand(TerminalCommands.DownloadData);
        AddCommand(TerminalCommands.ConnectServer);
        AddButton("터미널 종료", "EXIT TERMINAL", ExitTerminal);
        Select(0, false);
    }

    public static string KeyHints(params (string key, string label)[] hints)
    {
        string accent = DefragUiTheme.Hex(RuntimeUi.Theme.highlight);
        string dim = DefragUiTheme.Hex(RuntimeUi.Theme.dim);
        var parts = new List<string>(hints.Length);
        foreach ((string key, string label) in hints)
            parts.Add($"<color=#{accent}>[{key}]</color> <color=#{dim}>{label}</color>");
        return string.Join("     ", parts);
    }

    private static string KoreanLabel(TerminalCommands command) => command switch
    {
        TerminalCommands.UnlockDoor => "문 잠금 해제",
        TerminalCommands.DownloadData => "데이터 다운로드",
        TerminalCommands.ConnectServer => "서버 연결",
        _ => command.ToString()
    };

    private void AddCommand(TerminalCommands command)
    {
        HackingMinigameBase minigame = device.GetMinigame(command);
        bool available = minigame != null && device.IsCommandEnabled(command);
        string state = device.IsCompleted(command) ? "완료" : available ? minigame.DisplayName : "잠김";
        AddButton(KoreanLabel(command), $"{TerminalCommandLabel.Get(command)}  //  {state}", () => Execute(command));
    }

    private void Execute(TerminalCommands command)
    {
        HackingMinigameBase prefab = device.GetMinigame(command);
        if (!device.IsCommandEnabled(command) || prefab == null || device.IsCompleted(command))
        {
            ShowDeniedAccess();
            return;
        }

        UiSfx.Play(UiCue.MenuConfirm);
        ClearContent();
        menuLayout.enabled = false;
        activeCommand = command;
        activeMinigame = Instantiate(prefab, content);
        activeMinigame.Succeeded += CompleteMinigame;
        activeMinigame.Failed += FailMinigame;
        activeMinigame.Cancelled += CancelMinigame;
        header.text = $"{KoreanLabel(command)}  <size=60%><color=#{DefragUiTheme.Hex(RuntimeUi.Theme.dim)}>// {device.DisplayName}</color></size>";
        status.text = activeMinigame.ControlHint;
        activeMinigame.Begin(device, command);
        device.PublishWorldScreen(TerminalWorldPhase.Running, command);
    }

    private void ShowDeniedAccess()
    {
        UiSfx.Play(UiCue.AccessDenied);
        if (deniedRoutine != null)
            StopCoroutine(deniedRoutine);
        deniedRoutine = StartCoroutine(BlinkDeniedAccess());
    }

    private void CompleteMinigame()
    {
        UiSfx.Play(UiCue.TaskSuccess);
        terminalSfx?.PlayMinigameSuccess();
        device.PublishWorldScreen(TerminalWorldPhase.Success, activeCommand);
        preserveWorldTransientOnDestroy = true;
        bool closeTerminal = activeMinigame.CloseTerminalOnSuccess;
        device.RequestCommandCompletion(activeCommand);
        if (closeTerminal)
        {
            DestroyMinigame();
            closeRequested();
            return;
        }

        FinishMinigame($"<color=#{DefragUiTheme.Hex(RuntimeUi.Theme.accent)}>{KoreanLabel(activeCommand)} 완료</color>");
    }

    private void FailMinigame()
    {
        UiSfx.Play(UiCue.TaskFail);
        device.PublishWorldScreen(TerminalWorldPhase.Failure, activeCommand);
        FinishMinigame($"<color=#{DefragUiTheme.Hex(RuntimeUi.Theme.danger)}>실패 // 메뉴에서 다시 시도하세요</color>");
    }

    private void CancelMinigame()
    {
        DestroyMinigame();
        ShowMenu();
        device.PublishWorldScreen(TerminalWorldPhase.Menu);
    }

    private void ExitTerminal()
    {
        UiSfx.Play(UiCue.TerminalClose);
        device.PublishWorldScreen(TerminalWorldPhase.Idle);
        closeRequested();
    }

    private void OnDestroy()
    {
        if (!preserveWorldTransientOnDestroy && device != null)
            device.PublishWorldScreen(TerminalWorldPhase.Idle);
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

    private void AddButton(string label, string detail, System.Action action)
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        GameObject row = new(label, typeof(RectTransform), typeof(LayoutElement));
        row.transform.SetParent(content, false);
        row.GetComponent<LayoutElement>().preferredHeight = 74f;

        GameObject background = new("Selection", typeof(RectTransform), typeof(Image), typeof(Button));
        background.transform.SetParent(row.transform, false);
        Stretch((RectTransform)background.transform, Vector2.zero, Vector2.zero);

        Button button = background.GetComponent<Button>();
        Image backgroundImage = background.GetComponent<Image>();
        backgroundImage.color = theme.panel;
        button.targetGraphic = backgroundImage;
        button.transition = Selectable.Transition.None;
        int index = buttons.Count;
        button.onClick.AddListener(() => action());
        MenuHoverSelect hover = background.AddComponent<MenuHoverSelect>();
        hover.Hovered = () => { if (selection != index) Select(index); };

        Image accent = RuntimeUi.Panel("Accent", background.transform, theme.accent);
        accent.rectTransform.anchorMin = Vector2.zero;
        accent.rectTransform.anchorMax = new Vector2(0f, 1f);
        accent.rectTransform.sizeDelta = new Vector2(6f, 0f);
        accent.rectTransform.anchoredPosition = new Vector2(3f, 0f);

        TMP_Text text = CreateText("Label", background.transform, 29f, TextAlignmentOptions.MidlineLeft);
        text.text = label;
        Place(text.rectTransform, Vector2.zero, new Vector2(0.45f, 1f), new Vector2(28f, 0f), Vector2.zero);

        TMP_Text detailText = CreateText("Detail", background.transform, 21f, TextAlignmentOptions.MidlineRight);
        detailText.text = detail;
        Place(detailText.rectTransform, new Vector2(0.45f, 0f), Vector2.one, Vector2.zero, new Vector2(-24f, 0f));

        buttons.Add(button);
        buttonBackgrounds.Add(backgroundImage);
        buttonAccents.Add(accent);
        buttonLabels.Add(text);
        buttonDetails.Add(detailText);
    }

    private void Select(int index, bool playSound = true)
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        selection = (index + buttons.Count) % buttons.Count;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(buttons[selection].gameObject);
        for (int i = 0; i < buttonBackgrounds.Count; i++)
        {
            bool selected = i == selection;
            if (buttonBackgrounds[i] != null)
                buttonBackgrounds[i].color = selected ? new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.22f) : theme.panel;
            if (buttonAccents[i] != null)
                buttonAccents[i].enabled = selected;
            if (buttonLabels[i] != null)
                buttonLabels[i].color = selected ? theme.highlight : theme.text;
            if (buttonDetails[i] != null)
                buttonDetails[i].color = selected ? theme.accent : theme.dim;
        }
        if (playSound)
            UiSfx.Play(UiCue.MenuMove);
    }

    private void ClearContent()
    {
        foreach (Transform child in content)
            Destroy(child.gameObject);
        buttons.Clear();
        buttonBackgrounds.Clear();
        buttonAccents.Clear();
        buttonLabels.Clear();
        buttonDetails.Clear();
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
        if (RuntimeUi.Theme.font != null)
            text.font = RuntimeUi.Theme.font;
        text.fontSize = size;
        text.color = RuntimeUi.Theme.text;
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

public sealed class MenuHoverSelect : MonoBehaviour, IPointerEnterHandler
{
    public System.Action Hovered;

    public void OnPointerEnter(PointerEventData eventData) => Hovered?.Invoke();
}

/// <summary>
/// Keyboard navigation used only while the local terminal UI owns gameplay input.
/// It lives beside the terminal controller so Unity compiles the adapter together
/// with the already-imported terminal source file.
/// </summary>
public static class TerminalKeyboardInput
{
    public static bool UpPressed => WasPressed(KeyCode.W, KeyCode.UpArrow);
    public static bool DownPressed => WasPressed(KeyCode.S, KeyCode.DownArrow);
    public static bool LeftPressed => WasPressed(KeyCode.A, KeyCode.LeftArrow);
    public static bool RightPressed => WasPressed(KeyCode.D, KeyCode.RightArrow);
    public static bool ConfirmPressed => WasPressed(KeyCode.E, KeyCode.Return, KeyCode.KeypadEnter);
    public static bool BackPressed => WasPressed(KeyCode.Backspace);
    public static bool EscapePressed => WasPressed(KeyCode.Escape);
    public static bool TabPressed => WasPressed(KeyCode.Tab);

    public static bool TryGetRhythmKeyPressed(out char pressedKey)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.qKey.wasPressedThisFrame) { pressedKey = 'Q'; return true; }
            if (keyboard.wKey.wasPressedThisFrame) { pressedKey = 'W'; return true; }
            if (keyboard.eKey.wasPressedThisFrame) { pressedKey = 'E'; return true; }
            if (keyboard.rKey.wasPressedThisFrame) { pressedKey = 'R'; return true; }
            if (keyboard.aKey.wasPressedThisFrame) { pressedKey = 'A'; return true; }
            if (keyboard.sKey.wasPressedThisFrame) { pressedKey = 'S'; return true; }
            if (keyboard.dKey.wasPressedThisFrame) { pressedKey = 'D'; return true; }
            if (keyboard.fKey.wasPressedThisFrame) { pressedKey = 'F'; return true; }
        }
#else
        if (Input.GetKeyDown(KeyCode.Q)) { pressedKey = 'Q'; return true; }
        if (Input.GetKeyDown(KeyCode.W)) { pressedKey = 'W'; return true; }
        if (Input.GetKeyDown(KeyCode.E)) { pressedKey = 'E'; return true; }
        if (Input.GetKeyDown(KeyCode.R)) { pressedKey = 'R'; return true; }
        if (Input.GetKeyDown(KeyCode.A)) { pressedKey = 'A'; return true; }
        if (Input.GetKeyDown(KeyCode.S)) { pressedKey = 'S'; return true; }
        if (Input.GetKeyDown(KeyCode.D)) { pressedKey = 'D'; return true; }
        if (Input.GetKeyDown(KeyCode.F)) { pressedKey = 'F'; return true; }
#endif
        pressedKey = '\0';
        return false;
    }

    private static bool WasPressed(params KeyCode[] legacyKeys)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        foreach (KeyCode legacyKey in legacyKeys)
        {
            if (GetKeyControl(keyboard, legacyKey)?.wasPressedThisFrame == true)
                return true;
        }

        return false;
#else
        foreach (KeyCode key in legacyKeys)
        {
            if (Input.GetKeyDown(key))
                return true;
        }

        return false;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static UnityEngine.InputSystem.Controls.KeyControl GetKeyControl(
        Keyboard keyboard,
        KeyCode key)
    {
        return key switch
        {
            KeyCode.W => keyboard.wKey,
            KeyCode.S => keyboard.sKey,
            KeyCode.A => keyboard.aKey,
            KeyCode.D => keyboard.dKey,
            KeyCode.UpArrow => keyboard.upArrowKey,
            KeyCode.DownArrow => keyboard.downArrowKey,
            KeyCode.LeftArrow => keyboard.leftArrowKey,
            KeyCode.RightArrow => keyboard.rightArrowKey,
            KeyCode.E => keyboard.eKey,
            KeyCode.Return => keyboard.enterKey,
            KeyCode.KeypadEnter => keyboard.numpadEnterKey,
            KeyCode.Backspace => keyboard.backspaceKey,
            KeyCode.Escape => keyboard.escapeKey,
            KeyCode.Tab => keyboard.tabKey,
            _ => null
        };
    }
#endif
}
