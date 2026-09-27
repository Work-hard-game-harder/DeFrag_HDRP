using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CameraItem))]
public sealed class CameraOpticalRelayScanner : MonoBehaviour
{
    private static Color IrGreen => RuntimeUi.Theme.accent;
    private const float LockTickInterval = 0.14f;

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
    private LocalSignalAudio signalAudio;
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
    private float nextLockTick;

    private void Awake()
    {
        cameraItem = GetComponent<CameraItem>();
        signalAudio = GetComponent<LocalSignalAudio>();
        if (signalAudio == null) signalAudio = gameObject.AddComponent<LocalSignalAudio>();
        if (GetComponent<CameraFuelSignalPresenter>() == null)
            gameObject.AddComponent<CameraFuelSignalPresenter>();
        if (GetComponent<ConnectServerPartnerHud>() == null)
            gameObject.AddComponent<ConnectServerPartnerHud>();
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
        if (lockProgress < 1f && Time.unscaledTime >= nextLockTick)
        {
            nextLockTick = Time.unscaledTime + LockTickInterval;
            UiSfx.Play(UiCue.LockTick, 0.8f, 0.8f + lockProgress * 0.6f);
        }
        if (lockProgress >= 1f && !lockSoundPlayed)
        {
            lockSoundPlayed = true;
            UiSfx.Play(UiCue.LockAcquired);
            Play(lockAcquiredClip);
        }
    }

    private void OnPhotoTaken()
    {
        if (!ShouldScan() || coordinator.Phase != ConnectServerUplinkPhase.AwaitingOpticalScan)
            return;

        if (lockedRelay == null || lockProgress < 1f)
        {
            ShowTransient("아직 조준이 안 됐어요 — 가운데에 맞추고 잠시 기다리세요", false);
            UiSfx.Play(UiCue.MenuBack);
            return;
        }

        coordinator.SubmitPhoto(
            lockedRelay,
            scanCamera.transform.position,
            scanCamera.transform.forward);
        ShowTransient("사진 전송 중...", true);
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
        privateWordList = string.Empty;
        SetWordGridVisible(false);
        string shortId = ConnectServerRadarContent.ShortId(relayId);
        if (success)
        {
            ShowTransient($"{shortId} 촬영 성공! 회로 데이터를 받으세요", true);
            UiSfx.Play(UiCue.CaptureAccepted);
            Play(acceptedClip);
        }
        else
        {
            ShowTransient(message.Contains("WRONG")
                ? $"{shortId}는 목표가 아니에요! 경보가 울렸어요"
                : "촬영 실패 — 더 가까이, 정면에서 찍어보세요", false);
            UiSfx.Play(UiCue.CaptureRejected);
            Play(rejectedClip);
        }
    }

    private void RefreshHud()
    {
        float timeLeft = Mathf.Max(0f, (float)(coordinator.Deadline - coordinator.ServerTime));
        string dim = DefragUiTheme.Hex(RuntimeUi.Theme.dim);
        targetText.text =
            $"IR 광학 스캐너  <color=#{dim}>// 해커가 불러주는 릴레이를 찾으세요</color>\n" +
            $"업링크 {coordinator.CompletedRounds + 1}/{coordinator.RequiredRounds}    " +
            $"추적도 {coordinator.Trace:0}%    남은 시간 {timeLeft:0}초";

        bool showFrequency = coordinator.Phase == ConnectServerUplinkPhase.AwaitingOpticalScan;
        RefreshTargetFrequency(showFrequency);
        bool showWordGrid = !string.IsNullOrEmpty(privateWordList) &&
                            coordinator.Phase == ConnectServerUplinkPhase.AwaitingVerification;
        SetWordGridVisible(showWordGrid);

        if (Time.unscaledTime < transientUntil)
        {
            scanText.text = transientStatus;
        }
        else if (coordinator.Phase == ConnectServerUplinkPhase.AwaitingVerification)
        {
            scanText.text = "회로 데이터 연결됨 — 해커에게 칸 좌표를 불러주세요";
        }
        else if (aimedRelay == null)
        {
            scanText.text = "릴레이 탐색 중... 아래 신호 막대를 따라가세요";
        }
        else if (lockedRelay == aimedRelay && lockProgress >= 1f)
        {
            scanText.text = $"<size=130%>{ConnectServerRadarContent.ShortId(aimedRelay.RelayId)}</size>  조준 완료\n<color=#{DefragUiTheme.Hex(RuntimeUi.Theme.highlight)}>[좌클릭] 촬영</color>";
        }
        else
        {
            float distance = Vector3.Distance(scanCamera.transform.position, aimedRelay.ScanAnchor.position);
            bool tooFar = distance > aimedRelay.CaptureDistance;
            scanText.text = $"<size=130%>{ConnectServerRadarContent.ShortId(aimedRelay.RelayId)}</size>  {distance:0.0}m\n" +
                            (tooFar ? $"더 가까이 ({aimedRelay.CaptureDistance:0}m 이내)" : "가운데에 맞추고 잠시 유지");
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

        Image panelImage = RuntimeUi.FramedPanel("Scanner Panel", canvasObject.transform,
            new Color(RuntimeUi.Theme.panel.r, RuntimeUi.Theme.panel.g, RuntimeUi.Theme.panel.b, 0.8f), 18f);
        RuntimeUi.Place(panelImage.rectTransform, new Vector2(0.29f, 0.73f), new Vector2(0.71f, 0.96f));
        GameObject panel = panelImage.gameObject;

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
        Image displayImage = RuntimeUi.FramedPanel("Target Frequency", parent,
            new Color(RuntimeUi.Theme.panel.r, RuntimeUi.Theme.panel.g, RuntimeUi.Theme.panel.b, 0.82f), 14f);
        Place(displayImage.rectTransform, new Vector2(0.33f, 0.11f), new Vector2(0.67f, 0.19f));
        GameObject display = displayImage.gameObject;

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
            frequencyText.text = "목표 신호 없음";
            return;
        }

        float distance = Vector3.Distance(
            scanCamera.transform.position,
            target.ScanAnchor.position);
        float far = Mathf.Max(frequencyNearDistance + 0.1f, frequencyFarDistance);
        float proximity = 1f - Mathf.InverseLerp(frequencyNearDistance, far, distance);
        signalAudio.Report(proximity);
        float frequency = Mathf.Lerp(minimumFrequency, maximumFrequency, proximity);
        int activeBars = proximity <= 0.01f
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(proximity * signalBars.Length), 1, signalBars.Length);
        float wave = Mathf.Sin(Time.unscaledTime * frequency * Mathf.PI * 2f) * 0.5f + 0.5f;
        frequencyText.text = $"목표 릴레이 신호\n강도  {activeBars} / {signalBars.Length}";
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
        return RuntimeUi.Text(name, parent, size, alignment, null, IrGreen);
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
        transientStatus = positive ? message : $"<color=#{DefragUiTheme.Hex(RuntimeUi.Theme.danger)}>{message}</color>";
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
