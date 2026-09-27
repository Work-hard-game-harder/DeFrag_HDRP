using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class TutorialCard
{
    public string Id;
    public string Title;
    public string Role;
    public string Goal;
    public (string keys, string text)[] Steps = Array.Empty<(string, string)>();
}

// One-time role briefings. Seen ids reset when the local network session ends, so each new team sees them again.
public static class MinigameTutorial
{
    private static readonly HashSet<string> Seen = new(StringComparer.Ordinal);
    private static NetworkManager hookedManager;

    public static bool HasSeen(string id) => Seen.Contains(id);

    // Statics survive play-mode entry when domain reload is disabled, so start every run with a clean slate.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        Seen.Clear();
        hookedManager = null;
    }

    // Shows the card inside parent and calls onClosed once dismissed (immediately if already seen).
    public static void ShowBlocking(TutorialCard card, RectTransform parent, Action onClosed)
    {
        if (!TryClaim(card) || parent == null)
        {
            onClosed?.Invoke();
            return;
        }
        TutorialCardView.CreateBlocking(card, parent, onClosed);
    }

    // Modal card on its own screen overlay, for stations that have no HUD canvas of their own.
    public static void ShowBlockingOverlay(TutorialCard card, Action onClosed)
    {
        if (!TryClaim(card))
        {
            onClosed?.Invoke();
            return;
        }
        Canvas canvas = RuntimeUi.Canvas("Tutorial Overlay Canvas", null, 180);
        TutorialCardView.CreateBlocking(card, (RectTransform)canvas.transform, () =>
        {
            UnityEngine.Object.Destroy(canvas.gameObject);
            onClosed?.Invoke();
        });
    }

    // Non-modal briefing for a player who keeps moving; closes on Enter or after a reading delay.
    public static void ShowFloating(TutorialCard card, float autoCloseSeconds = 16f)
    {
        if (TryClaim(card))
            TutorialCardView.CreateFloating(card, autoCloseSeconds);
    }

    private static bool TryClaim(TutorialCard card)
    {
        EnsureResetHook();
        return card != null && !string.IsNullOrEmpty(card.Id) && Seen.Add(card.Id);
    }

    private static void EnsureResetHook()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager == hookedManager)
            return;
        hookedManager = manager;
        manager.OnClientStopped += _ => Seen.Clear();
    }
}

public sealed class TutorialCardView : MonoBehaviour, IPointerClickHandler
{
    private const float FadeSeconds = 0.18f;

    private CanvasGroup group;
    private Action onClosed;
    private bool blocking;
    private bool closing;
    private float autoCloseAt = float.PositiveInfinity;
    private float openedAt;

    public static void CreateBlocking(TutorialCard card, RectTransform parent, Action onClosed)
    {
        Image backdrop = RuntimeUi.Panel("Tutorial Backdrop", parent, new Color(0f, 0f, 0f, 0.72f));
        backdrop.raycastTarget = true;
        RuntimeUi.Stretch(backdrop.rectTransform);
        backdrop.transform.SetAsLastSibling();

        TutorialCardView view = backdrop.gameObject.AddComponent<TutorialCardView>();
        view.blocking = true;
        view.onClosed = onClosed;
        view.Build(backdrop.rectTransform, card, new Vector2(0.1f, 0.08f), new Vector2(0.9f, 0.92f), "[SPACE] 확인하고 시작");
    }

    public static void CreateFloating(TutorialCard card, float autoCloseSeconds)
    {
        Canvas canvas = RuntimeUi.Canvas("Tutorial Floating Canvas", null, 175);
        GameObject root = new("Tutorial Floating", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        RuntimeUi.Stretch((RectTransform)root.transform);
        TutorialCardView view = root.AddComponent<TutorialCardView>();
        view.autoCloseAt = Time.unscaledTime + autoCloseSeconds;
        view.onClosed = () => Destroy(canvas.gameObject);
        view.Build((RectTransform)root.transform, card, new Vector2(0.27f, 0.56f), new Vector2(0.73f, 0.96f), "[ENTER] 닫기");
    }

    private void Build(RectTransform root, TutorialCard card, Vector2 min, Vector2 max, string footerHint)
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = blocking;

        Image panel = RuntimeUi.FramedPanel("Tutorial Card", root, theme.panelRaised, 28f);
        RuntimeUi.Place(panel.rectTransform, min, max);
        RuntimeUi.Scanlines(panel.transform, 0.05f);

        Image band = RuntimeUi.Panel("Role Band", panel.transform, new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.16f));
        RuntimeUi.Place(band.rectTransform, new Vector2(0f, 0.8f), new Vector2(1f, 1f));

        TMP_Text role = RuntimeUi.Text("Role", panel.transform, 24f, TextAlignmentOptions.TopLeft, null, theme.info);
        RuntimeUi.Place(role.rectTransform, new Vector2(0.05f, 0.9f), new Vector2(0.95f, 0.975f));
        role.text = $"튜토리얼  //  {card.Role}";

        TMP_Text title = RuntimeUi.Text("Title", panel.transform, 44f, TextAlignmentOptions.MidlineLeft, null, theme.highlight);
        RuntimeUi.Place(title.rectTransform, new Vector2(0.05f, 0.81f), new Vector2(0.95f, 0.91f));
        title.enableAutoSizing = true;
        title.fontSizeMin = 22f;
        title.fontSizeMax = 44f;
        title.text = card.Title;

        TMP_Text goal = RuntimeUi.Text("Goal", panel.transform, 27f, TextAlignmentOptions.TopLeft, null, theme.accent);
        RuntimeUi.Place(goal.rectTransform, new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.78f));
        goal.enableAutoSizing = true;
        goal.fontSizeMin = 16f;
        goal.fontSizeMax = 27f;
        goal.text = $"목표: {card.Goal}";

        int count = Mathf.Max(1, card.Steps.Length);
        float top = 0.64f, bottom = 0.15f;
        float rowHeight = (top - bottom) / count;
        for (int i = 0; i < card.Steps.Length; i++)
        {
            float rowTop = top - i * rowHeight;
            BuildStep(panel.rectTransform, i + 1, card.Steps[i], new Vector2(0.05f, rowTop - rowHeight + 0.01f), new Vector2(0.95f, rowTop - 0.01f));
        }

        TMP_Text footer = RuntimeUi.Text("Footer", panel.transform, 24f, TextAlignmentOptions.Center, null, theme.highlight);
        RuntimeUi.Place(footer.rectTransform, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.12f));
        footer.text = footerHint;
        footer.gameObject.AddComponent<TutorialBlink>();

        openedAt = Time.unscaledTime;
        UiSfx.Play(UiCue.TutorialOpen);
        StartCoroutine(Fade(0f, 1f));
    }

    private static void BuildStep(RectTransform parent, int number, (string keys, string text) step, Vector2 min, Vector2 max)
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        Image row = RuntimeUi.Panel($"Step {number}", parent, new Color(0f, 0f, 0f, 0.28f));
        RuntimeUi.Place(row.rectTransform, min, max);

        TMP_Text index = RuntimeUi.Text("Number", row.transform, 30f, TextAlignmentOptions.Center, null, theme.dim);
        RuntimeUi.Place(index.rectTransform, new Vector2(0f, 0f), new Vector2(0.07f, 1f));
        index.text = number.ToString();

        bool hasKeys = !string.IsNullOrEmpty(step.keys);
        if (hasKeys)
        {
            Image keyBox = RuntimeUi.Panel("Keys", row.transform, theme.panelRaised);
            RuntimeUi.Place(keyBox.rectTransform, new Vector2(0.08f, 0.14f), new Vector2(0.3f, 0.86f));
            Outline outline = keyBox.gameObject.AddComponent<Outline>();
            outline.effectColor = theme.edge;
            outline.effectDistance = new Vector2(2f, -2f);
            TMP_Text keys = RuntimeUi.Text("Label", keyBox.transform, 26f, TextAlignmentOptions.Center, null, theme.highlight);
            RuntimeUi.Stretch(keys.rectTransform, 4f);
            keys.enableAutoSizing = true;
            keys.fontSizeMin = 14f;
            keys.fontSizeMax = 26f;
            keys.text = step.keys;
        }

        TMP_Text text = RuntimeUi.Text("Text", row.transform, 25f, TextAlignmentOptions.MidlineLeft, null, theme.text);
        RuntimeUi.Place(text.rectTransform, new Vector2(hasKeys ? 0.33f : 0.09f, 0.05f), new Vector2(0.98f, 0.95f));
        text.enableAutoSizing = true;
        text.fontSizeMin = 14f;
        text.fontSizeMax = 25f;
        text.text = step.text;
    }

    private void Update()
    {
        if (closing)
            return;
        // Ignore the key press that opened the station so it cannot instantly dismiss the card.
        if (Time.unscaledTime - openedAt < 0.35f)
            return;
        if (Time.unscaledTime >= autoCloseAt || DismissPressed())
            Close();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (blocking && !closing && Time.unscaledTime - openedAt >= 0.35f)
            Close();
    }

    private bool DismissPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return blocking ? Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) : Input.GetKeyDown(KeyCode.Return);
        bool enter = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
        return blocking ? enter || keyboard.spaceKey.wasPressedThisFrame : enter;
    }

    private void Close()
    {
        closing = true;
        UiSfx.Play(UiCue.TutorialClose);
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        yield return Fade(1f, 0f);
        // Resume on the next frame so the dismiss key is not also read by the minigame.
        yield return null;
        Action callback = onClosed;
        onClosed = null;
        Destroy(gameObject);
        callback?.Invoke();
    }

    private IEnumerator Fade(float from, float to)
    {
        for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / FadeSeconds);
            yield return null;
        }
        group.alpha = to;
    }
}

public sealed class TutorialBlink : MonoBehaviour
{
    private TMP_Text text;

    private void Awake() => text = GetComponent<TMP_Text>();

    private void Update()
    {
        if (text != null)
            text.alpha = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.6f));
    }
}
