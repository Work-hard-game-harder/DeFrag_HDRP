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

    private void Awake()
    {
        foreach (DesktopWindowBinding binding in windows)
        {
            if (binding == null) continue;
            DesktopWindowBinding captured = binding;
            captured.desktopIcon?.onClick.AddListener(() => Open(captured));
            captured.closeButton?.onClick.AddListener(() => Close(captured));
            if (captured.window != null) captured.window.SetActive(false);
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
        if (selected?.window == null) return;
        selected.window.SetActive(true);
        selected.window.transform.SetAsLastSibling();
    }

    private static void Close(DesktopWindowBinding selected)
    {
        if (selected?.window != null) selected.window.SetActive(false);
    }
}
