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
        SetCursorForUi(true);
        IsActive = true;
        activeMicrophoneStatusFont = device.MicrophoneStatusFont;

        EnsureSessionCanvas();
        GameObject screen = new GameObject("Terminal Interface", typeof(RectTransform));
        screen.transform.SetParent(sessionCanvas.transform, false);
        terminalScreen = screen.AddComponent<TerminalScreenController>();
        terminalScreen.Initialize(device, End);
        EnsureMicrophoneStatus();
        microphoneStatus.transform.SetAsLastSibling();
        BeginTerminalVoice();
    }

    public void End()
    {
        if (!IsActive) return;

        EndTerminalVoice();
        DestroyTerminalScreen();
        RestoreLocalHeldVisual();
        SetGameplayEnabled(true);
        SetCursorForUi(false);
        GameplayInputGate.Release(this);
        IsActive = false;
    }

    private void LateUpdate()
    {
        // Unity may recapture the pointer when focus moves between Multiplayer Play
        // Mode windows. This local modal session owns the cursor until it ends.
        if (IsActive)
            SetCursorForUi(true);
    }

    private void OnDestroy()
    {
        if (!IsActive) return;
        EndTerminalVoice();
        RestoreLocalHeldVisual();
        SetGameplayEnabled(true);
        SetCursorForUi(false);
        GameplayInputGate.Release(this);
    }

    private void OnDisable()
    {
        if (IsActive)
            End();
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
    }

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
        SetMicrophoneStatus(false);
    }

    private void BeginTerminalVoice()
    {
        if (networkVoice == null)
        {
            microphoneStatus.text = "● 마이크 연결 안 됨";
            microphoneStatus.color = MicrophoneUnavailableColor;
            return;
        }

        networkVoice.TerminalTransmissionChanged -= SetMicrophoneStatus;
        networkVoice.TerminalTransmissionChanged += SetMicrophoneStatus;
        networkVoice.SetTerminalSessionActive(true);
        SetMicrophoneStatus(networkVoice.IsTerminalTransmitting);
    }

    private void EndTerminalVoice()
    {
        if (networkVoice != null)
        {
            networkVoice.TerminalTransmissionChanged -= SetMicrophoneStatus;
            networkVoice.SetTerminalSessionActive(false);
        }

        if (microphoneStatus != null)
            microphoneStatus.gameObject.SetActive(false);
    }

    private void SetMicrophoneStatus(bool transmitting)
    {
        if (microphoneStatus == null)
            return;

        microphoneStatus.text = transmitting
            ? "● 마이크 송신 중"
            : "● 마이크 대기 중";
        microphoneStatus.color = transmitting
            ? MicrophoneTransmittingColor
            : MicrophoneReadyColor;
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
