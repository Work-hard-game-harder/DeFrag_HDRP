using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum KeypadSlotState { Empty, Cursor, Filled, Correct, Wrong, Granted }

/// <summary>
/// The keypad's smoked-glass display: bracketed code cells, a system line and a status line.
/// Drawn by <see cref="OffscreenUiSurface"/> into a texture that the device glass shows.
/// </summary>
public sealed class ElevatorKeypadDisplay : IDisposable
{
    private const float IdleInterval = 0.5f;
    private static readonly Vector2Int Size = new(640, 436);

    private readonly OffscreenUiSurface surface;
    private readonly List<(RectTransform rect, Image fill, Outline frame, TMP_Text glyph)> slots = new();
    private readonly TMP_Text header;
    private readonly TMP_Text lockLabel;
    private readonly TMP_Text system;
    private readonly TMP_Text status;
    private readonly TMP_Text[] brackets = new TMP_Text[2];
    private readonly Image flash;
    private readonly Color accent;
    private readonly Color correct;
    private readonly Color wrong;
    private float flashAmount;
    private Color flashColor;
    private float shake;
    private float nextIdleRender;
    private KeypadSlotState[] states = Array.Empty<KeypadSlotState>();

    public Texture Texture => surface.Texture;

    public ElevatorKeypadDisplay(TMP_FontAsset font, int slotCount, Color accent, Color correct, Color wrong, string systemLine)
    {
        this.accent = accent;
        this.correct = correct;
        this.wrong = wrong;
        surface = new OffscreenUiSurface("Elevator Keypad Display", Size, 24f);
        RectTransform root = surface.Root;

        Image background = RuntimeUi.Panel("Glass", root, new Color(0.012f, 0.02f, 0.026f));
        RuntimeUi.Stretch(background.rectTransform);
        // A faint diagonal sheen, like light on smoked glass.
        Image sheen = RuntimeUi.Panel("Sheen", root, new Color(1f, 1f, 1f, 0.035f));
        RuntimeUi.Place(sheen.rectTransform, new Vector2(0.52f, -0.2f), new Vector2(0.82f, 1.2f));
        sheen.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -18f);
        RuntimeUi.Scanlines(root, 0.06f);

        Color dimAccent = new(accent.r, accent.g, accent.b, 0.7f);
        header = Label(root, font, 26f, TextAlignmentOptions.MidlineLeft, dimAccent, new Vector2(0.07f, 0.84f), new Vector2(0.7f, 0.95f));
        header.text = "B1F  ELEVATOR ACCESS";
        lockLabel = Label(root, font, 24f, TextAlignmentOptions.MidlineRight, dimAccent, new Vector2(0.55f, 0.84f), new Vector2(0.93f, 0.95f));
        lockLabel.text = "SECURE";

        float cellWidth = 70f, gap = 10f, total = slotCount * cellWidth + (slotCount - 1) * gap;
        float left = (Size.x - total) * 0.5f;
        for (int i = 0; i < slotCount; i++)
        {
            Image fill = RuntimeUi.Panel($"Cell {i + 1}", root, Color.clear);
            RectTransform rect = fill.rectTransform;
            rect.anchorMin = new Vector2((left + i * (cellWidth + gap)) / Size.x, 0.42f);
            rect.anchorMax = new Vector2((left + i * (cellWidth + gap) + cellWidth) / Size.x, 0.7f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Outline frame = fill.gameObject.AddComponent<Outline>();
            frame.effectDistance = new Vector2(2f, -2f);
            TMP_Text glyph = Label(rect, font, 92f, TextAlignmentOptions.Center, accent, Vector2.zero, Vector2.one);
            slots.Add((rect, fill, frame, glyph));
        }
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? left - 34f : left + total + 6f;
            brackets[side] = Label(root, font, 150f, TextAlignmentOptions.Center, accent,
                new Vector2(x / Size.x, 0.36f), new Vector2((x + 28f) / Size.x, 0.76f));
            brackets[side].text = side == 0 ? "[" : "]";
        }

        system = Label(root, font, 22f, TextAlignmentOptions.Center, dimAccent, new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.39f));
        system.text = systemLine;
        system.textWrappingMode = TextWrappingModes.NoWrap;
        system.enableAutoSizing = true;
        system.fontSizeMin = 12f;
        system.fontSizeMax = 22f;
        Image rule = RuntimeUi.Panel("Rule", root, new Color(accent.r, accent.g, accent.b, 0.25f));
        RuntimeUi.Place(rule.rectTransform, new Vector2(0.07f, 0.22f), new Vector2(0.93f, 0.225f));
        status = Label(root, font, 26f, TextAlignmentOptions.Center, accent, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.2f));
        status.enableAutoSizing = true;
        status.textWrappingMode = TextWrappingModes.NoWrap;
        status.fontSizeMin = 14f;
        status.fontSizeMax = 28f;

        flash = RuntimeUi.Panel("Flash", root, Color.clear);
        RuntimeUi.Stretch(flash.rectTransform);
        SetSlots(string.Empty, slotCount, new KeypadSlotState[slotCount]);
    }

    private static TMP_Text Label(Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions alignment, Color color, Vector2 min, Vector2 max)
    {
        TMP_Text text = RuntimeUi.Text("Label", parent, size, alignment, font, color);
        RuntimeUi.Place(text.rectTransform, min, max);
        return text;
    }

    public void SetSlots(string entry, int length, KeypadSlotState[] slotStates)
    {
        states = slotStates;
        bool allGranted = slotStates.Length > 0;
        for (int i = 0; i < slots.Count; i++)
        {
            var (_, fill, frame, glyph) = slots[i];
            KeypadSlotState state = i < slotStates.Length ? slotStates[i] : KeypadSlotState.Empty;
            if (state != KeypadSlotState.Granted) allGranted = false;
            glyph.text = i < entry.Length ? entry[i].ToString() : state == KeypadSlotState.Cursor ? "_" : string.Empty;
            Color tint = state switch
            {
                KeypadSlotState.Correct or KeypadSlotState.Granted => correct,
                KeypadSlotState.Wrong => wrong,
                _ => accent
            };
            bool judged = state is KeypadSlotState.Correct or KeypadSlotState.Wrong or KeypadSlotState.Granted;
            fill.color = judged ? new Color(tint.r, tint.g, tint.b, 0.3f) : new Color(accent.r, accent.g, accent.b, state == KeypadSlotState.Empty ? 0.06f : 0.12f);
            frame.effectColor = new Color(tint.r, tint.g, tint.b, judged ? 0.95f : state == KeypadSlotState.Empty ? 0.25f : 0.6f);
            glyph.color = judged ? Color.Lerp(tint, Color.white, 0.25f) : accent;
        }
        foreach (TMP_Text bracket in brackets) bracket.color = allGranted ? correct : accent;
    }

    public void SetStatus(string text, Color color)
    {
        status.text = text;
        status.color = color;
    }

    public void SetHeader(string text) => header.text = text;

    public void SetLock(string text, Color color)
    {
        lockLabel.text = text;
        lockLabel.color = color;
    }

    public void Flash(Color color)
    {
        flashColor = color;
        flashAmount = 1f;
    }

    public void Shake() => shake = 1f;

    /// <summary>Renders at full rate while in use and a couple of times a second otherwise.</summary>
    public void Tick(bool active)
    {
        float dt = Time.unscaledDeltaTime;
        flashAmount = Mathf.MoveTowards(flashAmount, 0f, dt * 2.5f);
        flash.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashAmount * 0.35f);
        shake = Mathf.MoveTowards(shake, 0f, dt * 2.8f);

        bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f;
        for (int i = 0; i < slots.Count; i++)
        {
            var (rect, _, _, glyph) = slots[i];
            rect.anchoredPosition = new Vector2(shake > 0f ? Mathf.Sin(Time.unscaledTime * 70f + i) * 9f * shake : 0f, 0f);
            if (i < states.Length && states[i] == KeypadSlotState.Cursor) glyph.alpha = blink ? 1f : 0.15f;
        }

        if (!active && flashAmount <= 0f && shake <= 0f)
        {
            if (Time.unscaledTime < nextIdleRender) return;
            nextIdleRender = Time.unscaledTime + IdleInterval;
            surface.Tick(true);
            return;
        }
        surface.Tick();
    }

    public void Dispose() => surface.Dispose();
}
