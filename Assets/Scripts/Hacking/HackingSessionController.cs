using EasyPeasyFirstPersonController;
using DeFrag.Player;
using DeFrag.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns one local player's hacking session. It never scales or moves the
/// network-visible held item. Only the local first-person visual is hidden.
/// </summary>
public sealed class HackingSessionController : MonoBehaviour
{
    private StarterAssets.PersonController movement;
    private PlayerInteraction interaction;
    private InventoryUI inventoryUI;
    private PlayerItemDropper itemDropper;
    private HackingPadHeldController heldController;
    private Renderer[] localRenderers;
    private bool[] rendererStates;
    private Canvas sessionCanvas;
    private TerminalScreenController terminalScreen;
    private NetworkWalkieTalkieVoice networkVoice;
    private TMP_Text microphoneStatus;
    private TMP_FontAsset activeMicrophoneStatusFont;

    private static readonly Color MicrophoneReadyColor = new(0.55f, 0.62f, 0.58f);
    private static readonly Color MicrophoneTransmittingColor = new(0.2f, 1f, 0.45f);
    private static readonly Color MicrophoneUnavailableColor = new(1f, 0.25f, 0.2f);

    // Dive into / out of the monitor (local presentation only).
    private const float DiveDuration = 0.55f;
    private const float CrtOnDuration = 0.3f;
    private const float CrtOffDuration = 0.26f;
    private const float PullOutDuration = 0.42f;
    private const float DiveFovScale = 0.85f;

    private Camera sessionCamera;
    private Vector3 cameraLocalPosition;
    private Quaternion cameraLocalRotation;
    private float cameraFieldOfView;
    private bool cameraPoseStored;
    private Coroutine sequence;
    private bool ending;
    private TerminalCrtOverlay crt;
    private TerminalScreenStreamer streamer;
    private AudioSource sfx;

    public bool IsActive { get; private set; }

    public void Begin(
        GameObject localHeldPad,
        ConnectionDevice device)
    {
        if (IsActive || localHeldPad == null || device == null) return;
        if (!GameplayInputGate.TryAcquire(this))
        {
            Debug.LogWarning("[HackingSession] Another modal interaction is already using local input.", this);
            return;
        }

        CacheLocalPlayerComponents();
        HideLocalHeldVisual(localHeldPad);
        SetGameplayEnabled(false);
        IsActive = true;
        ending = false;
        activeMicrophoneStatusFont = device.MicrophoneStatusFont;
        sequence = StartCoroutine(DiveIn(device));
    }

    public void End()
    {
        if (!IsActive || ending) return;
        ending = true;
        if (sequence != null) StopCoroutine(sequence);
        sequence = StartCoroutine(PullOut());
    }

    private System.Collections.IEnumerator DiveIn(ConnectionDevice device)
    {
        StoreCameraPose();
        PlaySfx("DeviceSfx/Terminal_DiveIn", 0.9f);

        if (sessionCamera != null && device.WorldScreen != null &&
            device.WorldScreen.TryGetScreenPose(transform.position, out Vector3 center, out Vector3 normal,
                out Vector3 up, out Vector2 size))
        {
            float fov = cameraFieldOfView * DiveFovScale;
            float vertical = fov * 0.5f * Mathf.Deg2Rad;
            float horizontal = Mathf.Atan(Mathf.Tan(vertical) * sessionCamera.aspect);
            float distance = Mathf.Min(size.y * 0.5f / Mathf.Tan(vertical), size.x * 0.5f / Mathf.Tan(horizontal)) * 0.72f;
            distance = Mathf.Max(distance, sessionCamera.nearClipPlane * 4f);
            Vector3 targetPosition = center + normal * distance;
            Quaternion targetRotation = Quaternion.LookRotation(-normal, up);
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;

            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / DiveDuration)
            {
                // Turn toward the screen first, then get pulled in faster and faster.
                float look = 1f - Mathf.Pow(1f - Mathf.Clamp01(t * 1.6f), 3f);
                float pull = Mathf.Pow(t, 2.4f);
                transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, targetPosition, pull),
                    Quaternion.Slerp(startRotation, targetRotation, look) *
                        Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI) * 2.5f));
                sessionCamera.fieldOfView = Mathf.Lerp(cameraFieldOfView, fov, pull);
                yield return null;
            }
            transform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        crt = TerminalCrtOverlay.Create();
        OpenTerminalInterface(device);
        PlaySfx("DeviceSfx/Terminal_CrtOn", 0.85f);
        yield return crt.PowerOn(CrtOnDuration);
        crt.Destroy();
        crt = null;
        sequence = null;
    }

    private void OpenTerminalInterface(ConnectionDevice device)
    {
        SetCursorForUi(true);
        EnsureSessionCanvas();
        sessionBackdrop.SetActive(true);
        GameObject screen = new GameObject("Terminal Interface", typeof(RectTransform));
        screen.transform.SetParent(sessionCanvas.transform, false);
        terminalScreen = screen.AddComponent<TerminalScreenController>();
        terminalScreen.Initialize(device, End);
        EnsureMicrophoneStatus();
        microphoneStatus.transform.SetAsLastSibling();
        BeginTerminalVoice();
        streamer = TerminalScreenStreamer.Begin(sessionCanvas.gameObject, device.TerminalId);
    }

    private System.Collections.IEnumerator PullOut()
    {
        crt ??= TerminalCrtOverlay.Create();
        PlaySfx("DeviceSfx/Terminal_CrtOff", 0.85f);
        yield return crt.PowerOff(CrtOffDuration);

        CloseTerminalInterface();
        SetCursorForUi(false);

        if (cameraPoseStored && sessionCamera != null)
        {
            Transform parent = transform.parent;
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            float startFov = sessionCamera.fieldOfView;
            StartCoroutine(crt.FadeOut(PullOutDuration * 0.5f));
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / PullOutDuration)
            {
                float e = 1f - Mathf.Pow(1f - t, 3f);
                Vector3 homePosition = parent != null ? parent.TransformPoint(cameraLocalPosition) : cameraLocalPosition;
                Quaternion homeRotation = parent != null ? parent.rotation * cameraLocalRotation : cameraLocalRotation;
                transform.SetPositionAndRotation(Vector3.Lerp(startPosition, homePosition, e),
                    Quaternion.Slerp(startRotation, homeRotation, e));
                sessionCamera.fieldOfView = Mathf.Lerp(startFov, cameraFieldOfView, e);
                yield return null;
            }
        }

        sequence = null;
        FinishSession();
    }

    /// <summary>Restores everything immediately (also used when the component is disabled mid-sequence).</summary>
    private void FinishSession()
    {
        CloseTerminalInterface();
        RestoreCameraPose();
        crt?.Destroy();
        crt = null;
        RestoreLocalHeldVisual();
        SetGameplayEnabled(true);
        SetCursorForUi(false);
        GameplayInputGate.Release(this);
        IsActive = false;
        ending = false;
    }

    private void CloseTerminalInterface()
    {
        if (streamer != null) { Destroy(streamer); streamer = null; }
        if (sessionBackdrop != null) sessionBackdrop.SetActive(false);
        EndTerminalVoice();
        DestroyTerminalScreen();
    }

    private void StoreCameraPose()
    {
        sessionCamera = GetComponent<Camera>();
        cameraLocalPosition = transform.localPosition;
        cameraLocalRotation = transform.localRotation;
        cameraFieldOfView = sessionCamera != null ? sessionCamera.fieldOfView : 60f;
        cameraPoseStored = true;
    }

    private void RestoreCameraPose()
    {
        if (!cameraPoseStored) return;
        transform.localPosition = cameraLocalPosition;
        transform.localRotation = cameraLocalRotation;
        if (sessionCamera != null) sessionCamera.fieldOfView = cameraFieldOfView;
        cameraPoseStored = false;
    }

    private void PlaySfx(string resource, float volume)
    {
        AudioClip clip = Resources.Load<AudioClip>(resource);
        if (clip == null) return;
        if (sfx == null)
        {
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
        }
        sfx.PlayOneShot(clip, volume);
    }

    private void LateUpdate()
    {
        // Unity may recapture the pointer when focus moves between Multiplayer Play
        // Mode windows. This local modal session owns the cursor until it ends.
        if (IsActive && !ending && terminalScreen != null)
            SetCursorForUi(true);
    }

    private void OnDestroy()
    {
        if (!IsActive) return;
        FinishSession();
    }

    private void OnDisable()
    {
        if (IsActive)
        {
            if (sequence != null) StopCoroutine(sequence);
            sequence = null;
            FinishSession();
        }
        else
            GameplayInputGate.Release(this);
    }

    private void CacheLocalPlayerComponents()
    {
        Transform playerRoot = transform.root;
        movement = playerRoot.GetComponentInChildren<StarterAssets.PersonController>(true);
        interaction = playerRoot.GetComponentInChildren<PlayerInteraction>(true);
        inventoryUI = FindAnyObjectByType<InventoryUI>();
        itemDropper = GetComponent<PlayerItemDropper>();
        networkVoice = playerRoot.GetComponentInChildren<NetworkWalkieTalkieVoice>(true);
    }

    private void HideLocalHeldVisual(GameObject localHeldPad)
    {
        heldController = localHeldPad.GetComponent<HackingPadHeldController>();
        if (heldController != null) heldController.SetFocusLocked(true);

        localRenderers = localHeldPad.GetComponentsInChildren<Renderer>(true);
        rendererStates = new bool[localRenderers.Length];
        for (int i = 0; i < localRenderers.Length; i++)
        {
            rendererStates[i] = localRenderers[i].enabled;
            localRenderers[i].enabled = false;
        }
    }

    private void RestoreLocalHeldVisual()
    {
        if (localRenderers != null && rendererStates != null)
        {
            int count = Mathf.Min(localRenderers.Length, rendererStates.Length);
            for (int i = 0; i < count; i++)
            {
                if (localRenderers[i] != null)
                    localRenderers[i].enabled = rendererStates[i];
            }
        }

        if (heldController != null) heldController.SetFocusLocked(false);
        localRenderers = null;
        rendererStates = null;
        heldController = null;
    }

    private void EnsureSessionCanvas()
    {
        if (sessionCanvas != null) return;

        GameObject canvasObject = new GameObject("Local Hacking Session Canvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        sessionCanvas = canvasObject.AddComponent<Canvas>();
        sessionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        sessionCanvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        ResponsiveCanvasUtility.Configure(scaler);
        canvasObject.AddComponent<GraphicRaycaster>();

        // "Inside the monitor": the world behind the terminal panel is replaced by glass darkness.
        Image backdrop = RuntimeUi.Panel("Terminal Backdrop", canvasObject.transform, new Color(0.004f, 0.022f, 0.026f, 0.94f));
        RuntimeUi.Stretch(backdrop.rectTransform);
        RuntimeUi.Scanlines(backdrop.transform, 0.1f);
        sessionBackdrop = backdrop.gameObject;
        sessionBackdrop.transform.SetAsFirstSibling();
        sessionBackdrop.SetActive(false);
    }

    private GameObject sessionBackdrop;

    private void EnsureMicrophoneStatus()
    {
        if (microphoneStatus == null)
        {
            GameObject statusObject = new GameObject(
                "Terminal Microphone Status",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            statusObject.transform.SetParent(sessionCanvas.transform, false);

            RectTransform rect = (RectTransform)statusObject.transform;
            rect.anchorMin = new Vector2(0.35f, 0.91f);
            rect.anchorMax = new Vector2(0.60f, 0.98f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            microphoneStatus = statusObject.GetComponent<TMP_Text>();
            if (activeMicrophoneStatusFont != null)
                microphoneStatus.font = activeMicrophoneStatusFont;
            microphoneStatus.alignment = TextAlignmentOptions.TopRight;
            microphoneStatus.fontSize = 24f;
            microphoneStatus.fontStyle = FontStyles.Bold;
            microphoneStatus.raycastTarget = false;
        }

        if (activeMicrophoneStatusFont != null && microphoneStatus.font != activeMicrophoneStatusFont)
            microphoneStatus.font = activeMicrophoneStatusFont;

        microphoneStatus.gameObject.SetActive(true);
        SetMicrophoneStatus(TerminalVoiceStatus.Unavailable);
    }

    private void BeginTerminalVoice()
    {
        if (networkVoice == null)
        {
            SetMicrophoneStatus(TerminalVoiceStatus.Unavailable);
            return;
        }

        networkVoice.TerminalVoiceStatusChanged -= SetMicrophoneStatus;
        networkVoice.TerminalVoiceStatusChanged += SetMicrophoneStatus;
        networkVoice.SetTerminalSessionActive(true);
        SetMicrophoneStatus(networkVoice.CurrentTerminalVoiceStatus);
    }

    private void EndTerminalVoice()
    {
        if (networkVoice != null)
        {
            networkVoice.TerminalVoiceStatusChanged -= SetMicrophoneStatus;
            networkVoice.SetTerminalSessionActive(false);
        }

        if (microphoneStatus != null)
            microphoneStatus.gameObject.SetActive(false);
    }

    private void SetMicrophoneStatus(TerminalVoiceStatus status)
    {
        if (microphoneStatus == null)
            return;

        switch (status)
        {
            case TerminalVoiceStatus.Transmitting:
                microphoneStatus.text = "● 마이크 송신 중";
                microphoneStatus.color = MicrophoneTransmittingColor;
                break;
            case TerminalVoiceStatus.Ready:
                microphoneStatus.text = "● 마이크 대기 중";
                microphoneStatus.color = MicrophoneReadyColor;
                break;
            default:
                microphoneStatus.text = "● 마이크 송신 불가";
                microphoneStatus.color = MicrophoneUnavailableColor;
                break;
        }
    }

    private void DestroyTerminalScreen()
    {
        if (terminalScreen == null) return;
        Destroy(terminalScreen.gameObject);
        terminalScreen = null;
    }

    private void SetGameplayEnabled(bool enabled)
    {
        if (movement != null) movement.enabled = enabled;
        if (interaction != null) interaction.enabled = enabled;
        if (inventoryUI != null) inventoryUI.enabled = enabled;
        if (itemDropper != null) itemDropper.enabled = enabled;
    }

    private static void SetCursorForUi(bool enabled)
    {
        Cursor.lockState = enabled ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = enabled;
    }
}
