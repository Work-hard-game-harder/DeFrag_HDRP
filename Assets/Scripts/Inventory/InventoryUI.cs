using UnityEngine;
using UnityEngine.InputSystem;
using EasyPeasyFirstPersonController;

public class InventoryUI : MonoBehaviour
{
    public GameObject inventoryPanel;
    public int SelectedIndex { get; private set; }

    [Header("Quick Slots")]
    [SerializeField] private GameObject quickSlotsPanel;
    public InventorySlot[] quickSlots;
    private EquipmentController equipmentController;

    public void SetQuickSlotsPanel(GameObject panel)
    {
        quickSlotsPanel = panel;
    }

    private void Start()
    {
        if (quickSlotsPanel != null) quickSlotsPanel.SetActive(true);

        Camera playerCamera = Camera.main;
        if (playerCamera != null)
        {
            PlayerItemDropper dropper = playerCamera.GetComponent<PlayerItemDropper>();
            if (dropper == null) dropper = playerCamera.gameObject.AddComponent<PlayerItemDropper>();
            dropper.Configure(this, playerCamera.transform);

            equipmentController = playerCamera.GetComponent<EquipmentController>();
            if (equipmentController == null)
                equipmentController = playerCamera.gameObject.AddComponent<EquipmentController>();
            equipmentController.Configure(this, playerCamera.transform);
        }

        SelectSlot(0);
        UpdateUI();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame && quickSlotsPanel != null)
            quickSlotsPanel.SetActive(!quickSlotsPanel.activeSelf);

        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame) SelectSlot(0);
            if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame) SelectSlot(1);
            if (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame) SelectSlot(2);
            if (Keyboard.current.digit4Key.wasPressedThisFrame || Keyboard.current.numpad4Key.wasPressedThisFrame) SelectSlot(3);
        }

        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (scroll > 0.01f) SelectRelativeSlot(-1);
            else if (scroll < -0.01f) SelectRelativeSlot(1);
        }
    }

    public void SelectSlot(int index)
    {
        if (quickSlots == null || quickSlots.Length == 0)
        {
            SelectedIndex = 0;
            return;
        }

        SelectedIndex = Mathf.Clamp(index, 0, quickSlots.Length - 1);
        for (int i = 0; i < quickSlots.Length; i++)
            quickSlots[i]?.SetSelected(i == SelectedIndex);

        equipmentController?.RefreshSelectedItem();
    }

    public void SelectSlot(InventorySlot slot)
    {
        if (slot == null || quickSlots == null) return;

        for (int i = 0; i < quickSlots.Length; i++)
        {
            if (quickSlots[i] == slot)
            {
                SelectSlot(i);
                return;
            }
        }
    }

    private void SelectRelativeSlot(int direction)
    {
        if (InventoryManager.Instance == null || quickSlots == null) return;

        int occupiedSlots = Mathf.Min(InventoryManager.Instance.items.Count, quickSlots.Length);
        if (occupiedSlots == 0) return;

        int current = Mathf.Clamp(SelectedIndex, 0, occupiedSlots - 1);
        SelectSlot((current + direction + occupiedSlots) % occupiedSlots);
    }

    public void UpdateUI()
    {
        if (quickSlots == null) return;

        foreach (InventorySlot slot in quickSlots) slot?.Clear();
        if (InventoryManager.Instance == null) return;

        for (int i = 0; i < InventoryManager.Instance.items.Count && i < quickSlots.Length; i++)
            quickSlots[i]?.SetItem(InventoryManager.Instance.items[i]);

        SelectSlot(SelectedIndex);
    }

    public InventoryInfo GetSelectedItem()
    {
        if (InventoryManager.Instance == null) return null;
        var items = InventoryManager.Instance.items;
        return SelectedIndex >= 0 && SelectedIndex < items.Count ? items[SelectedIndex] : null;
    }
}

[RequireComponent(typeof(CameraBattery), typeof(HackingSessionController))]
public sealed class EquipmentController : MonoBehaviour
{
    private InventoryUI inventoryUI;
    private Transform handPoint;
    private ItemData equippedData;
    private GameObject heldVisual;
    private FirstPersonController playerController;
    private CameraBattery cameraBattery;

    public ItemData EquippedData => equippedData;
    public GameObject HeldVisual => heldVisual;

    private void Awake()
    {
        playerController = transform.root.GetComponentInChildren<FirstPersonController>(true);
        cameraBattery = GetComponent<CameraBattery>();
        EnsureHandPoint(transform);
    }

    private void Update()
    {
        if (heldVisual == null) return;

        bool walkieTalkieVisible = playerController != null
            && playerController.wakieTakie != null
            && playerController.wakieTakie.activeSelf;

        if (heldVisual.activeSelf == walkieTalkieVisible)
            heldVisual.SetActive(!walkieTalkieVisible);
    }

    public void Configure(InventoryUI ui, Transform cameraTransform)
    {
        inventoryUI = ui;
        playerController = cameraTransform.root.GetComponentInChildren<FirstPersonController>(true);
        EnsureHandPoint(cameraTransform);
        RefreshSelectedItem();
    }

    public void RefreshSelectedItem()
    {
        ItemData selectedData = inventoryUI?.GetSelectedItem()?.itemData;
        if (selectedData == equippedData && (selectedData == null || heldVisual != null)) return;

        Equip(selectedData);
    }

    public bool TryUseEquippedItem()
    {
        InventoryInfo selected = inventoryUI.GetSelectedItem();

        if (selected.itemData is not BatteryItemData battery)
            return false;

        if (!InventoryManager.Instance.ContainsItemOfType<CameraItemData>())
            return false;

        if (!cameraBattery.TryRecharge(battery.RechargeRatio))
            return false;

        InventoryManager.Instance.RemoveItem(selected);
        return true;
    }

    private void Equip(ItemData data)
    {
        ClearHeldVisual();
        equippedData = data;

        if (data == null || handPoint == null) return;

        bool usingWorldPrefab = data.heldPrefab == null;
        GameObject visualPrefab = usingWorldPrefab ? data.itemPrefab : data.heldPrefab;
        if (visualPrefab == null)
        {
            Debug.LogWarning($"[Equipment] '{data.itemName}'에 Held Prefab 또는 Item Prefab이 없습니다.");
            return;
        }

        heldVisual = Instantiate(visualPrefab, handPoint, false);
        heldVisual.name = $"Held_{data.itemName}";
        heldVisual.transform.localPosition = data.heldLocalPosition;

        if (usingWorldPrefab)
        {
            heldVisual.transform.localRotation = Quaternion.Euler(data.heldLocalEulerAngles)
                * heldVisual.transform.localRotation;
            heldVisual.transform.localScale = Vector3.Scale(
                heldVisual.transform.localScale,
                data.heldLocalScale);
        }
        else
        {
            heldVisual.transform.localRotation = Quaternion.Euler(data.heldLocalEulerAngles);
            heldVisual.transform.localScale = data.heldLocalScale;
        }

        PrepareAsHeldVisual(heldVisual);

        if (data is CameraItemData)
            heldVisual.GetComponentInChildren<CameraItem>(true).Bind(cameraBattery);

        if (data.supportsCloseInspection)
        {
            HackingPadHeldController heldController =
                heldVisual.GetComponent<HackingPadHeldController>();
            if (heldController == null)
                heldController = heldVisual.AddComponent<HackingPadHeldController>();

            // Capture only after the equipment pose has been fully applied.
            heldController.CaptureNormalPose();
        }
    }

    private void EnsureHandPoint(Transform cameraTransform)
    {
        if (handPoint != null || cameraTransform == null) return;

        Transform existing = cameraTransform.Find("InventoryHandPoint");
        if (existing != null)
        {
            handPoint = existing;
            return;
        }

        GameObject point = new GameObject("InventoryHandPoint");
        handPoint = point.transform;
        handPoint.SetParent(cameraTransform, false);
    }

    private static void PrepareAsHeldVisual(GameObject visual)
    {
        SetLayerRecursively(visual, LayerMask.NameToLayer("Ignore Raycast"));

        foreach (Collider itemCollider in visual.GetComponentsInChildren<Collider>(true))
            itemCollider.enabled = false;

        foreach (Rigidbody rb in visual.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        foreach (GetItem pickup in visual.GetComponentsInChildren<GetItem>(true))
            pickup.enabled = false;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null || layer < 0) return;

        target.layer = layer;
        foreach (Transform child in target.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private void ClearHeldVisual()
    {
        if (heldVisual != null) Destroy(heldVisual);
        heldVisual = null;
    }
}
