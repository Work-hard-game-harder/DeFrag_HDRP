using System;
using TMPro;
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

    [Serializable]
    private sealed class DesktopFileBinding
    {
        public string name;
        public Button icon;
        public string title;
        [TextArea(4, 16)] public string body;
    }

    [SerializeField] private DesktopWindowBinding[] windows;

    [Header("File Viewer")]
    [Tooltip("창 안의 파일 아이콘(예: 휴지통 속 문서). 누르면 뷰어 창이 그 창 위에 열린다.")]
    [SerializeField] private DesktopFileBinding[] files;
    [SerializeField] private GameObject fileViewerWindow;
    [SerializeField] private TMP_Text fileViewerTitle;
    [SerializeField] private TMP_Text fileViewerBody;
    [SerializeField] private Button fileViewerCloseButton;

    private Canvas desktopCanvas;
    private DesktopFileBinding openFile;
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

        if (files != null)
        {
            foreach (DesktopFileBinding file in files)
            {
                if (file == null) continue;
                DesktopFileBinding captured = file;
                ConfigureButton(captured.icon, () =>
                {
                    buttonHandledFrame = Time.frameCount;
                    OpenFile(captured);
                });
            }
        }
        ConfigureButton(fileViewerCloseButton, CloseFileViewer);
        CloseFileViewer();
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

        if (TryResolveFileClick(screenPosition, eventCamera)) yield break;

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
        CloseFileViewer();
    }

    private bool TryResolveFileClick(Vector2 screenPosition, Camera eventCamera)
    {
        if (files == null) return false;

        // Clicks on the open viewer belong to the viewer, not to icons hidden behind it.
        if (fileViewerWindow != null && fileViewerWindow.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)fileViewerWindow.transform, screenPosition, eventCamera))
            return true;

        foreach (DesktopFileBinding file in files)
        {
            if (file?.icon == null || !file.icon.gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)file.icon.transform, screenPosition, eventCamera))
            {
                OpenFile(file);
                return true;
            }
        }
        return false;
    }

    private void OpenFile(DesktopFileBinding file)
    {
        if (fileViewerWindow == null)
        {
            Debug.LogError("[MonitorDesktopUI] File viewer window is not assigned.", this);
            return;
        }

        // The rect fallback (mouse down) and the Button (mouse up) can both open the same file.
        if (openFile == file && fileViewerWindow.activeSelf) return;
        openFile = file;
        if (fileViewerTitle != null) fileViewerTitle.text = string.IsNullOrEmpty(file.title) ? file.name : file.title;
        if (fileViewerBody != null) fileViewerBody.text = file.body;
        fileViewerWindow.SetActive(true);
        fileViewerWindow.transform.SetAsLastSibling();
        UiSfx.Play(UiCue.MenuConfirm, 0.45f);
    }

    private void CloseFileViewer()
    {
        openFile = null;
        if (fileViewerWindow != null) fileViewerWindow.SetActive(false);
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
