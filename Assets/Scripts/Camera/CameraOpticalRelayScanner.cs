using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CameraItem))]
public sealed class CameraOpticalRelayScanner : MonoBehaviour
{
    private static readonly Color IrGreen = new(0.15f, 1f, 0.38f);
    private static readonly Color Warning = new(1f, 0.25f, 0.12f);

    [Header("Optical Lock")]
    [SerializeField] private Camera scanCamera;
    [SerializeField, Min(0.05f)] private float lockDuration = 0.7f;
    [SerializeField] private LayerMask scanMask = ~0;

    [Header("Target Frequency Finder")]
    [SerializeField, Min(0.1f)] private float frequencyNearDistance = 2f;
    [SerializeField, Min(0.2f)] private float frequencyFarDistance = 60f;
    [SerializeField, Min(0.1f)] private float minimumFrequency = 0.5f;
    [SerializeField, Min(0.1f)] private float maximumFrequency = 9f;

    [Header("HUD Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lockAcquiredClip;
    [SerializeField] private AudioClip acceptedClip;
    [SerializeField] private AudioClip rejectedClip;

    private CameraItem cameraItem;
    private ConnectServerCoordinator coordinator;
    private OpticalRelayNode aimedRelay;
    private OpticalRelayNode lockedRelay;
    private float lockProgress;
    private bool lockSoundPlayed;
    private Canvas canvas;
    private TMP_Text targetText;
    private TMP_Text scanText;
    private TMP_Text frequencyText;
    private Image lockFill;
    private readonly Image[] signalBars = new Image[5];
    private GameObject wordGrid;
    private readonly TMP_Text[] wordLabels = new TMP_Text[4];
    private string privateWordList;
    private string transientStatus;
    private float transientUntil;

    private void Awake()
    {
        cameraItem = GetComponent<CameraItem>();
        if (GetComponent<CameraFuelSignalPresenter>() == null)
            gameObject.AddComponent<CameraFuelSignalPresenter>();
        if (scanCamera == null)
            scanCamera = GetComponent<Camera>();
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        cameraItem.PhotoTaken += OnPhotoTaken;
        cameraItem.ModeChanged += OnModeChanged;
        cameraItem.ViewActiveChanged += OnViewActiveChanged;
        ConnectServerCoordinator.LocalInstanceAvailable += BindCoordinator;
        BindCoordinator(ConnectServerCoordinator.LocalInstance);
    }

    private void OnDisable()
    {
        cameraItem.PhotoTaken -= OnPhotoTaken;
        cameraItem.ModeChanged -= OnModeChanged;
        cameraItem.ViewActiveChanged -= OnViewActiveChanged;
        ConnectServerCoordinator.LocalInstanceAvailable -= BindCoordinator;
        BindCoordinator(null);
        SetHudVisible(false);
    }

    private void Update()
    {
        if (!ShouldScan())
        {
            ResetLock();
            SetHudVisible(false);
            return;
        }

        EnsureHud();
        SetHudVisible(true);
        RefreshAimAndLock();
        RefreshHud();
    }

    private bool ShouldScan()
    {
        if (coordinator == null || !coordinator.IsSpawned || scanCamera == null ||
            !scanCamera.enabled || !cameraItem.IsEquipped || !cameraItem.IsViewActive ||
            cameraItem.CurrentMode != CameraItem.CameraMode.Infrared)
            return false;

        ConnectServerUplinkPhase phase = coordinator.Phase;
        if (phase != ConnectServerUplinkPhase.AwaitingOpticalScan &&
            phase != ConnectServerUplinkPhase.AwaitingVerification)
            return false;

        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && manager.IsListening &&
               manager.LocalClientId != coordinator.TerminalOperatorClientId;
    }

    private void RefreshAimAndLock()
    {
        aimedRelay = null;
        Ray ray = scanCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (Physics.Raycast(
                ray,
                out RaycastHit hit,
                100f,
                scanMask,
                QueryTriggerInteraction.Collide))
        {
            OpticalRelayNode relay = hit.collider.GetComponentInParent<OpticalRelayNode>();
            if (relay != null && relay.Selectable && relay.OwnsCollider(hit.collider) &&
                relay.IsInFrontAndRange(ray.origin, relay.IdentificationDistance))
                aimedRelay = relay;
        }

        bool canLock = aimedRelay != null &&
                       coordinator.Phase == ConnectServerUplinkPhase.AwaitingOpticalScan &&
                       aimedRelay.IsInFrontAndRange(ray.origin, aimedRelay.CaptureDistance);
        if (!canLock)
        {
            ResetLock();
            return;
        }

        if (lockedRelay != aimedRelay)
        {
            lockedRelay = aimedRelay;
            lockProgress = 0f;
            lockSoundPlayed = false;
        }

        lockProgress = Mathf.Min(1f, lockProgress + Time.unscaledDeltaTime / lockDuration);
        if (lockProgress >= 1f && !lockSoundPlayed)
        {
            lockSoundPlayed = true;
            Play(lockAcquiredClip);
        }
    }

    private void OnPhotoTaken()
    {
        if (!ShouldScan() || coordinator.Phase != ConnectServerUplinkPhase.AwaitingOpticalScan)
            return;

        if (lockedRelay == null || lockProgress < 1f)
        {
            ShowTransient("NO OPTICAL LOCK", false);
            return;
        }

        coordinator.SubmitPhoto(
            lockedRelay,
            scanCamera.transform.position,
            scanCamera.transform.forward);
        ShowTransient("VALIDATING CAPTURE...", true);
    }

    private void BindCoordinator(ConnectServerCoordinator value)
    {
        if (coordinator == value)
            return;
        if (coordinator != null)
            coordinator.LocalPhotoResolved -= OnPhotoResolved;
        coordinator = value;
        if (coordinator != null)
            coordinator.LocalPhotoResolved += OnPhotoResolved;
        privateWordList = string.Empty;
        ResetLock();
    }

    private void OnPhotoResolved(bool success, string relayId, string message)
    {
        if (success)
        {
            privateWordList = message;
            PopulateWordGrid(message);
            ShowTransient($"{relayId} // CAPTURE ACCEPTED", true);
            Play(acceptedClip);
        }
        else
        {
            privateWordList = string.Empty;
            SetWordGridVisible(false);
            ShowTransient($"{relayId} // {message}", false);
            Play(rejectedClip);
        }
    }

    private void RefreshHud()
    {
        float timeLeft = Mathf.Max(0f, (float)(coordinator.Deadline - coordinator.ServerTime));
        targetText.text =
            $"UPLINK OPTICAL CHANNEL\n" +
            "REMOTE TARGET ASSIGNED // AWAIT OPERATOR CALLSIGN\n" +
            $"ROUND {coordinator.CompletedRounds + 1:00}/{coordinator.RequiredRounds:00}    " +
            $"TRACE {coordinator.Trace:00}%    {timeLeft:00.0}s";

        bool showFrequency = coordinator.Phase == ConnectServerUplinkPhase.AwaitingOpticalScan;
        RefreshTargetFrequency(showFrequency);
        bool showWordGrid = !string.IsNullOrEmpty(privateWordList) &&
                            coordinator.Phase == ConnectServerUplinkPhase.AwaitingVerification;
        SetWordGridVisible(showWordGrid);

        if (Time.unscaledTime < transientUntil)
        {
            scanText.text = transientStatus;
        }
        else if (!string.IsNullOrEmpty(privateWordList) &&
                 coordinator.Phase == ConnectServerUplinkPhase.AwaitingVerification)
        {
            scanText.text = "WORD GRID DECODED // REPORT THE REQUESTED NUMBER";
        }
        else if (aimedRelay == null)
        {
            scanText.text = "SIGNAL SEARCHING...";
        }
        else if (lockedRelay == aimedRelay && lockProgress >= 1f)
        {
            scanText.text = $"{aimedRelay.RelayId}  //  OPTICAL LOCK\n[LMB] CAPTURE";
        }
        else
        {
            float distance = Vector3.Distance(scanCamera.transform.position, aimedRelay.ScanAnchor.position);
            scanText.text = $"{aimedRelay.RelayId}  //  {distance:0.0}m\nHOLD CENTER TO LOCK";
        }

        lockFill.fillAmount = lockProgress;
        lockFill.color = lockProgress >= 1f ? IrGreen : new Color(1f, 0.75f, 0.1f, 0.85f);
    }

    private void EnsureHud()
    {
        if (canvas != null)
            return;

        GameObject canvasObject = new(
            "Optical Relay Scanner HUD",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 135;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject panel = new("Scanner Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = new Vector2(0.29f, 0.73f);
        panelRect.anchorMax = new Vector2(0.71f, 0.96f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0.06f, 0.025f, 0.78f);

        targetText = CreateText("Target", panel.transform, 22f, TextAlignmentOptions.TopLeft);
        Place(targetText.rectTransform, new Vector2(0.04f, 0.46f), new Vector2(0.96f, 0.94f));
        scanText = CreateText("Scan Status", panel.transform, 25f, TextAlignmentOptions.Center);
        Place(scanText.rectTransform, new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.48f));

        GameObject bar = new("Lock Bar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(panel.transform, false);
        RectTransform barRect = (RectTransform)bar.transform;
        Place(barRect, new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.105f));
        bar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        GameObject fill = new("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bar.transform, false);
        RectTransform fillRect = (RectTransform)fill.transform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);
        lockFill = fill.GetComponent<Image>();
        lockFill.type = Image.Type.Filled;
        lockFill.fillMethod = Image.FillMethod.Horizontal;
        lockFill.fillAmount = 0f;

        CreateWordGrid(canvasObject.transform);
        CreateFrequencyDisplay(canvasObject.transform);
    }

    private void CreateWordGrid(Transform parent)
    {
        wordGrid = new GameObject(
            "Decoded Word Grid",
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline));
        wordGrid.transform.SetParent(parent, false);
        RectTransform gridRect = (RectTransform)wordGrid.transform;
        Place(gridRect, new Vector2(0.22f, 0.23f), new Vector2(0.78f, 0.68f));
        wordGrid.GetComponent<Image>().color = new Color(0f, 0.025f, 0.01f, 0.34f);
        Outline outline = wordGrid.GetComponent<Outline>();
        outline.effectColor = new Color(IrGreen.r, IrGreen.g, IrGreen.b, 0.95f);
        outline.effectDistance = new Vector2(3f, -3f);

        CreateWordLabel(0, gridRect, new Vector2(0f, 0.5f), new Vector2(0.5f, 1f),
            TextAlignmentOptions.TopLeft);
        CreateWordLabel(1, gridRect, new Vector2(0.5f, 0.5f), Vector2.one,
            TextAlignmentOptions.TopRight);
        CreateWordLabel(2, gridRect, Vector2.zero, new Vector2(0.5f, 0.5f),
            TextAlignmentOptions.BottomLeft);
        CreateWordLabel(3, gridRect, new Vector2(0.5f, 0f), new Vector2(1f, 0.5f),
            TextAlignmentOptions.BottomRight);
        wordGrid.SetActive(false);
    }

    private void CreateWordLabel(
        int index,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextAlignmentOptions alignment)
    {
        TMP_Text label = CreateText($"Word {index + 1:00}", parent, 38f, alignment);
        Place(label.rectTransform, anchorMin, anchorMax);
        label.rectTransform.offsetMin = new Vector2(28f, 24f);
        label.rectTransform.offsetMax = new Vector2(-28f, -24f);
        wordLabels[index] = label;
    }

    private void CreateFrequencyDisplay(Transform parent)
    {
        GameObject display = new(
            "Target Frequency",
            typeof(RectTransform),
            typeof(Image));
        display.transform.SetParent(parent, false);
        RectTransform displayRect = (RectTransform)display.transform;
        Place(displayRect, new Vector2(0.33f, 0.11f), new Vector2(0.67f, 0.19f));
        display.GetComponent<Image>().color = new Color(0f, 0.04f, 0.015f, 0.82f);

        frequencyText = CreateText(
            "Frequency Text",
            display.transform,
            23f,
            TextAlignmentOptions.MidlineLeft);
        Place(frequencyText.rectTransform, Vector2.zero, new Vector2(0.68f, 1f));
        frequencyText.rectTransform.offsetMin = new Vector2(24f, 4f);
        frequencyText.rectTransform.offsetMax = new Vector2(-8f, -4f);

        CreateSignalBars(display.transform);
    }

    private void CreateSignalBars(Transform parent)
    {
        const float startX = 0.70f;
        const float barWidth = 0.042f;
        const float gap = 0.012f;
        for (int i = 0; i < signalBars.Length; i++)
        {
            GameObject bar = new($"Signal Bar {i + 1}", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)bar.transform;
            float x = startX + i * (barWidth + gap);
            float height = Mathf.Lerp(0.2f, 0.82f, i / (signalBars.Length - 1f));
            rect.anchorMin = new Vector2(x, 0.09f);
            rect.anchorMax = new Vector2(x + barWidth, 0.09f + height);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            signalBars[i] = bar.GetComponent<Image>();
            signalBars[i].color = new Color(IrGreen.r, IrGreen.g, IrGreen.b, 0.14f);
        }
    }

    private void PopulateWordGrid(string message)
    {
        string[] lines = message.Split('\n');
        for (int i = 0; i < wordLabels.Length; i++)
        {
            if (wordLabels[i] == null)
                continue;

            string line = i < lines.Length ? lines[i].Trim() : $"[{i + 1:00}] ---";
            int separator = line.IndexOf(' ');
            string number = separator > 0 ? line.Substring(0, separator) : $"[{i + 1:00}]";
            string word = separator > 0 ? line.Substring(separator + 1).Trim() : "---";
            wordLabels[i].text =
                $"<color=#35FF70>{number}</color>\n" +
                $"<color=#FFFFFF>{word}</color>";
        }
    }

    private void RefreshTargetFrequency(bool visible)
    {
        if (frequencyText == null)
            return;

        frequencyText.transform.parent.gameObject.SetActive(visible);
        if (!visible)
            return;

        if (!coordinator.TryGetRelay(coordinator.TargetRelayId, out OpticalRelayNode target))
        {
            frequencyText.text = "TARGET FREQUENCY // SIGNAL LOST";
            return;
        }

        float distance = Vector3.Distance(
            scanCamera.transform.position,
            target.ScanAnchor.position);
        float far = Mathf.Max(frequencyNearDistance + 0.1f, frequencyFarDistance);
        float proximity = 1f - Mathf.InverseLerp(frequencyNearDistance, far, distance);
        float frequency = Mathf.Lerp(minimumFrequency, maximumFrequency, proximity);
        int activeBars = proximity <= 0.01f
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(proximity * signalBars.Length), 1, signalBars.Length);
        float wave = Mathf.Sin(Time.unscaledTime * frequency * Mathf.PI * 2f) * 0.5f + 0.5f;
        frequencyText.text = $"TARGET SIGNAL\n{frequency:00.0} Hz";
        for (int i = 0; i < signalBars.Length; i++)
        {
            if (signalBars[i] == null)
                continue;

            bool active = i < activeBars;
            signalBars[i].color = new Color(
                IrGreen.r,
                IrGreen.g,
                IrGreen.b,
                active ? Mathf.Lerp(0.72f, 1f, wave) : 0.14f);
        }
    }

    private void SetWordGridVisible(bool visible)
    {
        if (wordGrid != null && wordGrid.activeSelf != visible)
            wordGrid.SetActive(visible);
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        float size,
        TextAlignmentOptions alignment)
    {
        GameObject child = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        child.transform.SetParent(parent, false);
        TMP_Text text = child.GetComponent<TMP_Text>();
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.color = IrGreen;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void Place(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void ShowTransient(string message, bool positive)
    {
        transientStatus = positive ? message : $"<color=#FF3A20>{message}</color>";
        transientUntil = Time.unscaledTime + 1.5f;
    }

    private void ResetLock()
    {
        aimedRelay = null;
        lockedRelay = null;
        lockProgress = 0f;
        lockSoundPlayed = false;
        if (lockFill != null)
            lockFill.fillAmount = 0f;
    }

    private void OnModeChanged(CameraItem.CameraMode _) => ResetLock();
    private void OnViewActiveChanged(bool _) => ResetLock();
    private void SetHudVisible(bool visible)
    {
        if (canvas != null && canvas.gameObject.activeSelf != visible)
            canvas.gameObject.SetActive(visible);
    }

    private void Play(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }
}
