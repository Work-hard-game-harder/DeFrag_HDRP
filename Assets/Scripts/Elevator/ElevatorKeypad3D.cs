using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A sleek white-lab electronic access panel: glossy white shell with a silver bezel and cyan LED
/// edges, a smoked-glass code display, a frosted touch pad and a fingerprint ring. Input comes from
/// the keyboard; for every typed character the local player's hand taps the pad and a ripple spreads
/// where the fingertip lands. The display colours each code slot after a wrong entry.
/// Presentation only: <see cref="ElevatorPanel"/> still owns the code, the alarm and the scene change.
/// Place this object on the wall plate with its blue axis (forward) pointing out of the wall.
/// </summary>
public sealed class ElevatorKeypad3D : MonoBehaviour
{
    [Header("Size (metres, face space: x = viewer's right)")]
    [SerializeField] private Vector2 panelSize = new(0.32f, 0.6f);
    [SerializeField, Min(0.005f)] private float cornerRadius = 0.045f;
    [SerializeField, Min(0.005f)] private float bodyDepth = 0.024f;
    [SerializeField] private Rect displayArea = new(-0.125f, 0.08f, 0.25f, 0.17f);
    [SerializeField] private Rect touchArea = new(-0.125f, -0.23f, 0.25f, 0.29f);
    [SerializeField] private Vector2 fingerprintCentre = new(0f, -0.262f);
    [SerializeField, Min(0.005f)] private float fingerprintDiameter = 0.03f;

    [Header("Look")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Color shellColor = new(0.93f, 0.94f, 0.95f);
    [SerializeField] private Color bezelColor = new(0.78f, 0.8f, 0.83f);
    [SerializeField] private Color accent = new(0.3f, 0.93f, 1f);
    [SerializeField] private Color correctColor = new(0.35f, 1f, 0.55f);
    [SerializeField] private Color wrongColor = new(1f, 0.25f, 0.22f);
    [SerializeField, Min(0f)] private float ledGlow = 2.2f;
    [SerializeField, Min(0f)] private float displayGlow = 1.8f;
    [SerializeField, Min(0f)] private float padGlow = 0.32f;
    [SerializeField] private string systemLine = "SYSTEM: ACTIVE  |  LEVEL: SECURE  |  ENTRANCE: B1F";

    [Header("Hand")]
    [Tooltip("검지 끝이 원점, +Z가 가리키는 방향인 손 프리팹 (Art/LobbyF/Keypad/Keypad_Hand).")]
    [SerializeField] private GameObject handPrefab;
    [SerializeField, Min(0.05f)] private float handScale = 0.6f;

    [Header("Camera focus")]
    [SerializeField, Range(0.3f, 1f)] private float screenFill = 0.86f;
    [SerializeField, Range(0.5f, 1f)] private float fieldOfViewScale = 0.9f;
    [SerializeField, Min(0.05f)] private float flyInSeconds = 0.6f;
    [SerializeField, Min(0.05f)] private float flyOutSeconds = 0.45f;

    [Header("Messages")]
    [SerializeField] private string promptText = "코드를 입력하세요  |  ENTER 확인  |  ESC 닫기";
    [SerializeField] private string idleText = "TOUCH TO AUTHENTICATE";

    [Header("Code guide")]
    [Tooltip("각 코드 칸 위에 표시할 이름. 코드가 무엇으로 이루어졌는지 알려 준다.")]
    [SerializeField] private string[] slotLabels = { "ID", "ZONE", "CH", "CH", "LOG", "LOG" };
    [Tooltip("오답이 누적되면 가장 왼쪽의 틀린 칸에 해당하는 안내를 SYSTEM 줄에 띄운다. 칸 순서와 같다.")]
    [SerializeField, TextArea(1, 2)] private string[] slotHints =
    {
        "ID → 바닥에 떨어진 사원증의 번호",
        "ZONE → 근무표에서 그 사원의 담당 구역",
        "CH → 회의실 TV에 '그것'이 비친 채널 번호 (앞)",
        "CH → 회의실 TV에 '그것'이 비친 채널 번호 (뒤)",
        "LOG → OFFICE 2 화이트보드 마지막 기록 + 팀장 PC 메모장",
        "LOG → OFFICE 2 화이트보드 마지막 기록 + 팀장 PC 메모장"
    };
    [Tooltip("이 횟수만큼 틀리면 안내를 보여 준다. 0이면 끈다.")]
    [SerializeField, Min(0)] private int hintAfterWrongAttempts = 2;
    [SerializeField] private Color hintColor = new(1f, 0.78f, 0.32f);

    private ElevatorKeypadDisplay display;
    private KeypadTouchPad pad;
    private KeypadTouchHand hand;
    private DeviceFocusCamera focus;
    private Material[] ownedMaterials = Array.Empty<Material>();
    private Material ledMaterial;
    private Material fingerprintMaterial;
    private Texture2D fingerprintTexture;
    private bool built;

    private string entry = string.Empty;
    private int codeLength = 6;
    private string judgedEntry = string.Empty;
    private bool[] judgedSlots = Array.Empty<bool>();
    private bool granted;
    private float messageUntil;
    private float alertUntil;
    private Color alertColor;
    private float fingerprintPulse;
    private int wrongAttempts;

    public bool IsFocused => focus != null && focus.IsFocused;

    // ───────────────────────── Public API (called by ElevatorPanel) ─────────────────────────

    public bool BeginFocus(Camera playerCamera, int length)
    {
        EnsureBuilt();
        if (playerCamera == null) return false;
        focus?.RestoreImmediately();

        codeLength = Mathf.Max(1, length);
        granted = false;
        float fov = playerCamera.fieldOfView * fieldOfViewScale;
        Vector3 faceCenter = transform.TransformPoint(new Vector3(0f, 0f, bodyDepth));
        float distance = DeviceFocusCamera.FitDistance(playerCamera, fov, panelSize, screenFill);
        var pose = new Pose(faceCenter + transform.forward * distance, Quaternion.LookRotation(-transform.forward, transform.up));
        focus = DeviceFocusCamera.Begin(playerCamera, pose, fov, flyInSeconds);
        focus.Returned += OnFocusReturned;

        UiSfx.Play(UiCue.TerminalBoot, 0.7f);
        ShowPrompt();
        pad.Touch(new Vector2(0.5f, 0.55f), accent, 1.6f, 1.1f);
        hand?.Show(transform, SurfacePoint(touchArea.center + new Vector2(0.03f, -0.06f)));
        return true;
    }

    public void EndFocus()
    {
        if (focus == null) return;
        UiSfx.Play(UiCue.TerminalClose, 0.6f);
        hand?.Hide();
        focus.Release(flyOutSeconds);
    }

    public void SetEntry(string value, int length)
    {
        entry = value ?? string.Empty;
        codeLength = Mathf.Max(1, length);
        RefreshSlots();
    }

    public void PressCharacter(char character)
    {
        char c = char.ToUpperInvariant(character);
        Vector2 uv = CharacterSpot(c);
        TapAt(uv, () =>
        {
            pad.Touch(uv, accent);
            UiSfx.Play(UiCue.KeyType, 0.8f, 0.9f + 0.2f * uv.x);
        });
    }

    public void PressBackspace()
    {
        Vector2 uv = new(0.18f, 0.12f);
        TapAt(uv, () =>
        {
            pad.Touch(uv, new Color(1f, 0.75f, 0.35f), 0.8f, 0.5f);
            UiSfx.Play(UiCue.MenuBack, 0.55f);
        });
    }

    public void PressSubmit()
    {
        Vector3 point = transform.TransformPoint(FaceToLocal(fingerprintCentre, bodyDepth + 0.0015f));
        Action land = () =>
        {
            fingerprintPulse = 1f;
            UiSfx.Play(UiCue.MenuConfirm, 0.7f);
            focus?.Nudge(point, 1.3f);
        };
        if (hand != null) hand.Tap(point, land); else land();
    }

    /// <summary>Wrong code: colour every slot green/red and sound the denial.</summary>
    public void ShowDenied(string submitted, bool[] slotCorrect, string message)
    {
        judgedEntry = submitted ?? string.Empty;
        judgedSlots = slotCorrect ?? Array.Empty<bool>();
        RefreshSlots();
        display.Flash(wrongColor);
        display.Shake();
        ShowMessage(message, wrongColor, 4.5f);
        Alert(wrongColor, "ALARM", 4.5f);
        pad.Touch(new Vector2(0.5f, 0.5f), wrongColor, 2.4f, 1f);
        UiSfx.Play(UiCue.AccessDenied, 0.9f);
        focus?.Shake(0.55f);
        wrongAttempts++;
        ShowSlotHint();
    }

    /// <summary>After repeated misses, point at the clue behind the left-most wrong slot.</summary>
    private void ShowSlotHint()
    {
        if (hintAfterWrongAttempts <= 0 || wrongAttempts < hintAfterWrongAttempts || slotHints == null) return;
        int slot = Array.IndexOf(judgedSlots, false);
        if (slot < 0 || slot >= slotHints.Length || string.IsNullOrWhiteSpace(slotHints[slot])) return;
        display.SetSystemLine($"HINT {slot + 1}/{codeLength}  {slotHints[slot]}", hintColor);
    }

    public void ShowGranted(string message)
    {
        granted = true;
        RefreshSlots();
        display.Flash(correctColor);
        display.SetHeader("ACCESS GRANTED");
        ShowMessage(message, correctColor, 10f);
        Alert(correctColor, "UNLOCKED", 10f);
        pad.Touch(new Vector2(0.5f, 0.5f), correctColor, 2.6f, 1.2f);
        UiSfx.Play(UiCue.TaskSuccess, 0.9f);
    }

    public void ShowMessage(string message, bool warning)
    {
        ShowMessage(message, warning ? new Color(1f, 0.75f, 0.3f) : accent, 2.5f);
        if (!warning) return;
        display.Shake();
        UiSfx.Play(UiCue.CardWrong, 0.7f);
    }

    // ───────────────────────── Build ─────────────────────────

    private void Start()
    {
        // Scene reload is disabled in this project, so per-run counters must be reset here.
        wrongAttempts = 0;
        EnsureBuilt();
    }

    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        Material bezel = Lit("Keypad Bezel", bezelColor, 1f, 0.82f);
        Material shell = Lit("Keypad Shell", shellColor, 0f, 0.93f);
        ledMaterial = Lit("Keypad LED", accent * 0.4f, 0f, 0.9f);
        DeviceScreenQuad.SetGlow(ledMaterial, accent, ledGlow);

        AddSlab("Bezel", Vector2.zero, panelSize, cornerRadius, 0f, bodyDepth - 0.004f, 0.004f, bezel);
        AddSlab("Shell", Vector2.zero, panelSize - Vector2.one * 0.012f, cornerRadius - 0.006f, 0f, bodyDepth, 0.005f, shell);
        // Cyan light strips along both long edges, between the shell and the bezel.
        float stripX = panelSize.x * 0.5f - 0.0035f, stripHeight = panelSize.y - cornerRadius * 2.2f;
        foreach (float x in new[] { -stripX, stripX })
            AddSlab("LED Strip", new Vector2(x, 0f), new Vector2(0.0025f, stripHeight), 0.00124f, 0f, bodyDepth - 0.002f, 0f, ledMaterial);

        display = new ElevatorKeypadDisplay(font, 6, accent, correctColor, wrongColor, systemLine, slotLabels);
        Material displayMaterial = DeviceScreenQuad.CreateMaterial(display.Texture, "Keypad Display");
        SetSurface(displayMaterial, new Color(0.08f, 0.09f, 0.1f), 0f, 0.95f);
        DeviceScreenQuad.SetGlow(displayMaterial, Color.white, displayGlow);
        AddSlab("Display Glass", displayArea.center, displayArea.size, 0.018f, bodyDepth, 0.0014f, 0.0008f, displayMaterial);

        pad = new KeypadTouchPad(accent);
        Material padMaterial = DeviceScreenQuad.CreateMaterial(pad.Texture, "Keypad Touch Pad");
        SetSurface(padMaterial, Color.white, 0f, 0.7f);
        DeviceScreenQuad.SetGlow(padMaterial, Color.white, padGlow);
        AddSlab("Touch Glass", touchArea.center, touchArea.size, 0.018f, bodyDepth, 0.0014f, 0.0008f, padMaterial);

        fingerprintTexture = BuildFingerprintTexture(128);
        fingerprintMaterial = DeviceScreenQuad.CreateMaterial(fingerprintTexture, "Keypad Fingerprint");
        SetSurface(fingerprintMaterial, Color.white, 0.6f, 0.85f);
        AddSlab("Fingerprint Ring", fingerprintCentre, Vector2.one * fingerprintDiameter, fingerprintDiameter * 0.5f,
            bodyDepth, 0.0025f, 0.001f, fingerprintMaterial);

        ownedMaterials = new[] { bezel, shell, ledMaterial, displayMaterial, padMaterial, fingerprintMaterial };
        if (handPrefab != null) hand = new KeypadTouchHand(handPrefab, transform, handScale);
        ShowIdle();
    }

    private void AddSlab(string name, Vector2 center, Vector2 size, float radius, float z, float depth, float bevel, Material material)
    {
        var slab = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        slab.transform.SetParent(transform, false);
        slab.transform.localPosition = FaceToLocal(center, z);
        slab.GetComponent<MeshFilter>().sharedMesh = RoundedSlabMesh.Build(name, size, radius, depth, bevel);
        var renderer = slab.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    // Dark sensor glass with a bright outer ring and faint concentric ridges (albedo and glow map).
    private static Texture2D BuildFingerprintTexture(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Keypad Fingerprint", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.07f);
                float ridges = r < 0.62f ? 0.18f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(r * 70f), 6f) : 0f;
                float value = Mathf.Max(ring, ridges) + (r < 0.95f ? 0.04f : 0.35f);
                pixels[y * size + x] = new Color(value, value, value, 1f);
            }
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    private static Material Lit(string name, Color color, float metallic, float smoothness)
    {
        Material material = DeviceScreenQuad.CreateMaterial(null, name);
        SetSurface(material, color, metallic, smoothness);
        DeviceScreenQuad.SetGlow(material, Color.black, 0f);
        return material;
    }

    private static void SetSurface(Material material, Color baseColor, float metallic, float smoothness)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
    }

    // ───────────────────────── Runtime ─────────────────────────

    private void Update()
    {
        if (!built) return;
        bool active = focus != null && focus.IsActive;
        float dt = Time.unscaledDeltaTime;
        hand?.Update(dt);

        if (messageUntil > 0f && Time.unscaledTime > messageUntil)
        {
            messageUntil = 0f;
            if (active) ShowPrompt(); else ShowIdle();
        }
        if (alertUntil > 0f && Time.unscaledTime > alertUntil)
        {
            alertUntil = 0f;
            display.SetLock("SECURE", new Color(accent.r, accent.g, accent.b, 0.7f));
        }

        // LEDs breathe, and flash the alert colour after a verdict.
        bool alerting = Time.unscaledTime < alertUntil;
        float breathe = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 1.6f);
        DeviceScreenQuad.SetGlow(ledMaterial, alerting ? alertColor : accent,
            ledGlow * (alerting ? 1.2f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f) : breathe));
        fingerprintPulse = Mathf.MoveTowards(fingerprintPulse, 0f, dt * 2f);
        DeviceScreenQuad.SetGlow(fingerprintMaterial, alerting ? alertColor : accent, 1.2f + fingerprintPulse * 4f);

        display.Tick(active);
        pad.Tick(active);
    }

    private void LateUpdate() => focus?.Update(Time.unscaledDeltaTime);

    private void TapAt(Vector2 padUv, Action onContact)
    {
        Vector3 point = SurfacePoint(new Vector2(touchArea.xMin + padUv.x * touchArea.width, touchArea.yMin + padUv.y * touchArea.height));
        Action land = () =>
        {
            onContact();
            focus?.Nudge(point, 1f);
        };
        if (hand != null) hand.Tap(point, land); else land();
    }

    // Each character has its own spot on an invisible 4×5 grid, so typing looks like real keying.
    private static Vector2 CharacterSpot(char c)
    {
        int index = c >= '0' && c <= '9' ? c - '0' : c >= 'A' && c <= 'Z' ? 10 + (c - 'A') : 0;
        int cell = index % 20;
        int column = cell % 4, row = cell / 4;
        float jitter = ((index * 37) % 7 - 3) * 0.012f;
        return new Vector2(0.17f + column * 0.22f + jitter, 0.86f - row * 0.17f - jitter * 0.5f);
    }

    private Vector3 SurfacePoint(Vector2 face) => transform.TransformPoint(FaceToLocal(face, bodyDepth + 0.0014f));

    private void RefreshSlots()
    {
        var states = new KeypadSlotState[codeLength];
        for (int i = 0; i < codeLength; i++)
        {
            if (granted) { states[i] = KeypadSlotState.Granted; continue; }
            if (i < entry.Length)
            {
                // A verdict sticks to a slot until that character is changed.
                bool judged = i < judgedEntry.Length && i < judgedSlots.Length && entry[i] == judgedEntry[i];
                states[i] = judged ? (judgedSlots[i] ? KeypadSlotState.Correct : KeypadSlotState.Wrong) : KeypadSlotState.Filled;
            }
            else states[i] = i == entry.Length && focus != null && focus.IsActive ? KeypadSlotState.Cursor : KeypadSlotState.Empty;
        }
        display.SetSlots(entry, codeLength, states);
    }

    private void ShowPrompt()
    {
        if (display == null) return;
        display.SetHeader("B1F  ELEVATOR ACCESS");
        display.SetStatus(promptText, new Color(0.75f, 0.92f, 0.96f));
        RefreshSlots();
    }

    private void ShowIdle()
    {
        if (display == null) return;
        display.SetHeader("B1F  ELEVATOR ACCESS");
        display.SetStatus(idleText, new Color(accent.r, accent.g, accent.b, 0.8f));
        RefreshSlots();
    }

    private void ShowMessage(string message, Color color, float seconds)
    {
        display.SetStatus(message, color);
        messageUntil = Time.unscaledTime + seconds;
    }

    private void Alert(Color color, string label, float seconds)
    {
        alertColor = color;
        alertUntil = Time.unscaledTime + seconds;
        display.SetLock(label, color);
    }

    private void OnFocusReturned()
    {
        if (focus != null) focus.Returned -= OnFocusReturned;
        focus = null;
        judgedEntry = string.Empty;
        judgedSlots = Array.Empty<bool>();
        entry = string.Empty;
        ShowIdle();
    }

    /// <summary>Face space (x = viewer's right, y = up) to this object's local space (+Z out of the wall).</summary>
    private static Vector3 FaceToLocal(Vector2 face, float z) => new(-face.x, face.y, z);

    private void OnDisable()
    {
        // Leaving the scene mid-focus (e.g. the elevator loads B1F) must hand the camera back.
        focus?.RestoreImmediately();
    }

    private void OnDestroy()
    {
        focus?.RestoreImmediately();
        hand?.Dispose();
        display?.Dispose();
        pad?.Dispose();
        foreach (Material material in ownedMaterials) if (material != null) Destroy(material);
        if (fingerprintTexture != null) Destroy(fingerprintTexture);
    }

    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(accent.r, accent.g, accent.b, 0.6f);
        Gizmos.DrawWireCube(new Vector3(0f, 0f, bodyDepth * 0.5f), new Vector3(panelSize.x, panelSize.y, bodyDepth));
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.15f);
    }
}
