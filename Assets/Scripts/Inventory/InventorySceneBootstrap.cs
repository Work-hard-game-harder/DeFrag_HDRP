using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Gives gameplay scenes the shared four-slot inventory when they do not have
/// a hand-authored InventoryManager yet. Existing scene setups are left intact.
/// </summary>
public static class InventorySceneBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= InstallIfMissing;
        SceneManager.sceneLoaded += InstallIfMissing;
    }

    private static void InstallIfMissing(Scene scene, LoadSceneMode mode)
    {
        if (Object.FindAnyObjectByType<InventoryManager>() != null) return;
        // Do not show the gameplay quick bar in title/lobby/menu scenes.
        if (Object.FindAnyObjectByType<PlayerInteraction>(FindObjectsInactive.Include) == null) return;

        GameObject system = new GameObject("Runtime Inventory System");
        InventoryManager manager = system.AddComponent<InventoryManager>();
        InventoryUI ui = system.AddComponent<InventoryUI>();
        manager.maxSlots = 4;
        manager.inventoryUI = ui;

        Canvas canvas = CreateCanvas(system.transform);
        GameObject panel = CreateQuickSlotsPanel(canvas.transform);
        ui.inventoryPanel = panel;
        ui.SetQuickSlotsPanel(panel);
        ui.quickSlots = CreateSlots(panel.transform, manager.maxSlots);
    }

    private static Canvas CreateCanvas(Transform parent)
    {
        GameObject canvasObject = new GameObject("InventoryCanvas", typeof(RectTransform));
        canvasObject.transform.SetParent(parent, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }

        return canvas;
    }

    private static GameObject CreateQuickSlotsPanel(Transform parent)
    {
        GameObject panel = new GameObject("QuickSlots", typeof(RectTransform));
        panel.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 35f);
        rect.sizeDelta = new Vector2(360f, 82f);

        HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return panel;
    }

    private static InventorySlot[] CreateSlots(Transform parent, int count)
    {
        InventorySlot[] slots = new InventorySlot[count];
        for (int i = 0; i < count; i++)
        {
            GameObject slotObject = new GameObject($"Slot {i + 1}", typeof(RectTransform));
            slotObject.transform.SetParent(parent, false);
            ((RectTransform)slotObject.transform).sizeDelta = new Vector2(80f, 80f);

            Image background = slotObject.AddComponent<Image>();
            background.color = new Color(0.08f, 0.08f, 0.08f, 0.82f);
            Button button = slotObject.AddComponent<Button>();
            button.targetGraphic = background;

            Image icon = CreateIcon(slotObject.transform);
            TMP_Text countText = CreateCountText(slotObject.transform);
            TMP_Text numberText = CreateNumberText(slotObject.transform, i + 1);
            numberText.raycastTarget = false;

            InventorySlot slot = slotObject.AddComponent<InventorySlot>();
            slot.Configure(icon, countText);
            button.onClick.AddListener(slot.UseItem);
            slots[i] = slot;
        }
        return slots;
    }

    private static Image CreateIcon(Transform parent)
    {
        GameObject child = new GameObject("Icon", typeof(RectTransform));
        child.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)child.transform;
        rect.anchorMin = new Vector2(0.12f, 0.12f);
        rect.anchorMax = new Vector2(0.88f, 0.88f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Image image = child.AddComponent<Image>();
        image.preserveAspect = true;
        image.color = new Color(1f, 1f, 1f, 0f);
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CreateCountText(Transform parent)
    {
        TMP_Text text = CreateText(parent, "Count", TextAlignmentOptions.BottomRight, 23f);
        text.rectTransform.offsetMin = new Vector2(4f, 2f);
        text.rectTransform.offsetMax = new Vector2(-5f, -3f);
        text.gameObject.SetActive(false);
        return text;
    }

    private static TMP_Text CreateNumberText(Transform parent, int number)
    {
        TMP_Text text = CreateText(parent, "Shortcut", TextAlignmentOptions.TopLeft, 17f);
        text.text = number.ToString();
        text.color = new Color(1f, 1f, 1f, 0.8f);
        text.rectTransform.offsetMin = new Vector2(6f, 3f);
        text.rectTransform.offsetMax = new Vector2(-3f, -4f);
        return text;
    }

    private static TMP_Text CreateText(Transform parent, string name, TextAlignmentOptions alignment, float size)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)child.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        TMP_Text text = child.AddComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        return text;
    }
}
