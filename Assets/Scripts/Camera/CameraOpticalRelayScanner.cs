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
    private Image lockFill;
    private string privateAuthorizationWord;
    private string transientStatus;
    private float transientUntil;

    private void Awake()
    {
        cameraItem = GetComponent<CameraItem>();
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
        privateAuthorizationWord = string.Empty;
        ResetLock();
    }

    private void OnPhotoResolved(bool success, string relayId, string message)
    {
        if (success)
        {
            privateAuthorizationWord = message;
            ShowTransient($"{relayId} // CAPTURE ACCEPTED", true);
            Play(acceptedClip);
        }
        else
        {
            privateAuthorizationWord = string.Empty;
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

        if (Time.unscaledTime < transientUntil)
        {
            scanText.text = transientStatus;
        }
        else if (!string.IsNullOrEmpty(privateAuthorizationWord) &&
                 coordinator.Phase == ConnectServerUplinkPhase.AwaitingVerification)
        {
            scanText.text =
                $"AUTH WORD: <color=#FFFFFF>{privateAuthorizationWord}</color>\n" +
                "READ IT TO THE TERMINAL OPERATOR";
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
