using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MonitorDesktopUI : MonoBehaviour
{
    [Serializable]
    private sealed class DesktopWindowBinding
    {
        public string name;
        public Button desktopIcon;
        public GameObject window;
        public Button closeButton;
    }

    [SerializeField] private DesktopWindowBinding[] windows;

    private Canvas desktopCanvas;
    private int buttonHandledFrame = -1;

    private void Awake()
    {
        desktopCanvas = GetComponent<Canvas>();
        foreach (DesktopWindowBinding binding in windows)
        {
            if (binding == null) continue;
            DesktopWindowBinding captured = binding;
            ConfigureButton(captured.desktopIcon, () =>
            {
                buttonHandledFrame = Time.frameCount;
                Open(captured);
            });
            ConfigureButton(captured.closeButton, () => Close(captured));
            if (captured.window != null) captured.window.SetActive(false);
        }
    }

    private void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        // Buttons are the primary path. This rect-based fallback handles scene
        // UI hierarchies where another transparent Graphic consumes the raycast.
        StartCoroutine(ResolveIconClickAtEndOfFrame(Input.mousePosition));
    }

    private System.Collections.IEnumerator ResolveIconClickAtEndOfFrame(Vector2 screenPosition)
    {
        yield return new WaitForEndOfFrame();
        if (buttonHandledFrame == Time.frameCount) yield break;

        Camera eventCamera = desktopCanvas != null &&
                             desktopCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? desktopCanvas.worldCamera
            : null;

        foreach (DesktopWindowBinding binding in windows)
        {
            if (binding?.desktopIcon == null ||
                !binding.desktopIcon.gameObject.activeInHierarchy)
                continue;

            RectTransform iconRect = binding.desktopIcon.transform as RectTransform;
            if (iconRect != null && RectTransformUtility.RectangleContainsScreenPoint(
                    iconRect, screenPosition, eventCamera))
            {
                Debug.Log(
                    $"[MonitorDesktopUI] Rect fallback received click for '{binding.name}'.",
                    binding.desktopIcon);
                Open(binding);
                yield break;
            }
        }
    }

    private void OnEnable()
    {
        CloseAllWindows();
    }

    public void CloseAllWindows()
    {
        foreach (DesktopWindowBinding binding in windows)
        {
            if (binding?.window != null) binding.window.SetActive(false);
        }
    }

    private void Open(DesktopWindowBinding selected)
    {
        if (selected?.window == null)
        {
            Debug.LogError("[MonitorDesktopUI] Window binding has no target window.", this);
            return;
        }

        CloseAllWindows();
        selected.window.SetActive(true);
        selected.window.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        Debug.Log(
            $"[MonitorDesktopUI] Opened '{selected.name}' -> " +
            $"{selected.window.name}, active={selected.window.activeInHierarchy}, " +
            $"sibling={selected.window.transform.GetSiblingIndex()}.",
            selected.window);
    }

    private static void Close(DesktopWindowBinding selected)
    {
        if (selected?.window != null) selected.window.SetActive(false);
    }

    private static void ConfigureButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;

        // Scene-authored SetActive calls and runtime bindings previously ran
        // together. Keep one deterministic path and make the Button itself the
        // only raycast receiver inside the icon hierarchy.
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
        if (button.targetGraphic != null) button.targetGraphic.raycastTarget = true;

        Graphic[] childGraphics = button.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in childGraphics)
            if (graphic != null && graphic != button.targetGraphic)
                graphic.raycastTarget = false;
    }
}
