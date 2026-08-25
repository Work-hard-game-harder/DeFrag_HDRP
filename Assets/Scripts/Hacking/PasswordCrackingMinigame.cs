using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class PasswordCrackingMinigame : HackingMinigameBase
{
    private static readonly string[] Words =
    {
        "CISTERN", "WIRETAP", "HILLTOP", "WYOMING",
        "DEPOSIT", "CAUSTIC", "SPRYER", "LETTERS"
    };

    private static readonly Color Green = new(0.1f, 1f, 0.2f);
    private readonly List<Button> buttons = new();

    private TMP_Text feedback;
    private string password;
    private int attempts;
    private int selection;
    private bool finished;
    private TerminalSfxPlayer terminalSfx;

    public override void Begin(ConnectionDevice device, TerminalCommands command)
    {
        terminalSfx = device.TerminalSfx;
        BuildInterface();
        password = Words[Random.Range(0, Words.Length)];
        attempts = 4;
        feedback.text = $"{TerminalCommandLabel.Get(command)} // ATTEMPTS: {attempts}";
        Select(0);
    }

    private void Update()
    {
        if (finished)
            return;

        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            Select(selection - 1);
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            Select(selection + 1);
        else if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return))
            buttons[selection].onClick.Invoke();
    }

    public override void End()
    {
        StopAllCoroutines();
    }

    private void BuildInterface()
    {
        VerticalLayoutGroup layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;

        TMP_Text code = CreateText("Code", 20f);
        code.text = "0x100  {UF:M}  WIRETAP   #<=L;  CISTERN\n" +
                    "0x110  ?B+@:-  HILLTOP   %CZ<<  CAUSTIC\n" +
                    "0x120  [<EW$  DEPOSIT   YN]}O  LETTERS";
        code.color = new Color(0.02f, 0.45f, 0.08f);
        code.gameObject.AddComponent<LayoutElement>().preferredHeight = 85f;

        feedback = CreateText("Feedback", 22f);
        feedback.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

        foreach (string word in Words)
        {
            string candidate = word;
            AddButton(candidate, () => Submit(candidate));
        }
    }

    private void Submit(string candidate)
    {
        if (candidate == password)
        {
            finished = true;
            feedback.text = "PASSWORD ACCEPTED";
            SetButtonsEnabled(false);
            StartCoroutine(ReportAfterDelay(true));
            return;
        }

        attempts--;
        terminalSfx?.PlayIncorrectAnswer();
        feedback.text = attempts > 0
            ? $"INVALID: {candidate}   MATCH {Similarity(candidate, password)}/{password.Length}   ATTEMPTS: {attempts}"
            : "PASSWORD REJECTED";

        if (attempts == 0)
        {
            finished = true;
            SetButtonsEnabled(false);
            StartCoroutine(ReportAfterDelay(false));
        }
    }

    private IEnumerator ReportAfterDelay(bool succeeded)
    {
        yield return new WaitForSecondsRealtime(1f);
        if (succeeded)
            ReportSuccess();
        else
            ReportFailure();
    }

    private void AddButton(string label, UnityEngine.Events.UnityAction action)
    {
        GameObject row = new(label, typeof(RectTransform), typeof(LayoutElement));
        row.transform.SetParent(transform, false);
        row.GetComponent<LayoutElement>().preferredHeight = 42f;

        GameObject background = new("Selection", typeof(RectTransform), typeof(Image), typeof(Button));
        background.transform.SetParent(row.transform, false);
        Stretch((RectTransform)background.transform, Vector2.zero, Vector2.zero);

        Button button = background.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0f, 0.1f, 0.02f, 0.8f);
        colors.highlightedColor = new Color(0.02f, 0.35f, 0.06f, 0.95f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.08f, 0.6f, 0.13f, 1f);
        button.colors = colors;
        button.onClick.AddListener(action);

        TMP_Text text = CreateText("Label", 23f, row.transform);
        text.text = label;
        Stretch(text.rectTransform, new Vector2(16f, 0f), new Vector2(-8f, 0f));
        text.outlineColor = Color.black;
        text.outlineWidth = 0.18f;
        buttons.Add(button);
    }

    private TMP_Text CreateText(string name, float size, Transform parent = null)
    {
        GameObject child = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        child.transform.SetParent(parent == null ? transform : parent, false);
        TMP_Text text = child.GetComponent<TMP_Text>();
        text.fontSize = size;
        text.color = Green;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        return text;
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

    private static int Similarity(string left, string right)
    {
        int matches = 0;
        for (int i = 0; i < left.Length; i++)
            if (left[i] == right[i]) matches++;
        return matches;
    }

    private static void Stretch(RectTransform rect, Vector2 minOffset, Vector2 maxOffset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = minOffset;
        rect.offsetMax = maxOffset;
    }
}
