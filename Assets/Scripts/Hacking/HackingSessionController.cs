using EasyPeasyFirstPersonController;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns one local player's hacking session. It never scales or moves the
/// network-visible held item. Only the local first-person visual is hidden.
/// </summary>
public sealed class HackingSessionController : MonoBehaviour
{
    private FirstPersonController movement;
    private PlayerInteraction interaction;
    private InventoryUI inventoryUI;
    private PlayerItemDropper itemDropper;
    private HackingPadHeldController heldController;
    private Renderer[] localRenderers;
    private bool[] rendererStates;
    private Canvas sessionCanvas;
    private TerminalScreenController terminalScreen;

    public bool IsActive { get; private set; }

    private void Update()
    {
        if (IsActive && Input.GetKeyDown(KeyCode.Escape))
            End();
    }

    public void Begin(
        GameObject localHeldPad,
        ConnectionDevice device)
    {
        if (IsActive || localHeldPad == null || device == null) return;

        CacheLocalPlayerComponents();
        HideLocalHeldVisual(localHeldPad);
        SetGameplayEnabled(false);
        SetCursorForUi(true);
        IsActive = true;

        EnsureSessionCanvas();
        GameObject screen = new GameObject("Terminal Interface", typeof(RectTransform));
        screen.transform.SetParent(sessionCanvas.transform, false);
        terminalScreen = screen.AddComponent<TerminalScreenController>();
        terminalScreen.Initialize(device, End);
    }

    public void End()
    {
        if (!IsActive) return;

        DestroyTerminalScreen();
        RestoreLocalHeldVisual();
        SetGameplayEnabled(true);
        SetCursorForUi(false);
        IsActive = false;
    }

    private void OnDestroy()
    {
        if (!IsActive) return;
        RestoreLocalHeldVisual();
        SetGameplayEnabled(true);
        SetCursorForUi(false);
    }

    private void CacheLocalPlayerComponents()
    {
        Transform playerRoot = transform.root;
        movement = playerRoot.GetComponentInChildren<FirstPersonController>(true);
        interaction = playerRoot.GetComponentInChildren<PlayerInteraction>(true);
        inventoryUI = FindAnyObjectByType<InventoryUI>();
        itemDropper = GetComponent<PlayerItemDropper>();
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
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObject.AddComponent<GraphicRaycaster>();
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
