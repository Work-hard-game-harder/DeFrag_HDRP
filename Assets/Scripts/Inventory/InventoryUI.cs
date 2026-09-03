using UnityEngine;
using UnityEngine.InputSystem;
using EasyPeasyFirstPersonController;
using Unity.Netcode;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour
{
    public GameObject inventoryPanel;
    public int SelectedIndex { get; private set; }

    [Header("Quick Slots")]
    [SerializeField] private GameObject quickSlotsPanel;
    public InventorySlot[] quickSlots;
    private EquipmentController equipmentController;
    private CameraViewSwitcher cameraViewSwitcher;
    private bool inventoryUiHiddenByCamera;
    private bool quickSlotsWereActive;
    private bool inventoryPanelWasActive;

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

            cameraViewSwitcher =
                playerCamera.transform.root.GetComponentInChildren<CameraViewSwitcher>(true);
            if (cameraViewSwitcher != null)
                cameraViewSwitcher.CameraViewActiveChanged += HandleCameraViewActiveChanged;
        }

        SelectSlot(0);
        UpdateUI();
    }

    private void Update()
    {
        if (GameplayInputGate.IsBlocked)
            return;

        if (cameraViewSwitcher != null && cameraViewSwitcher.IsCameraViewActive)
            return;

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

    private void OnDestroy()
    {
        if (cameraViewSwitcher != null)
            cameraViewSwitcher.CameraViewActiveChanged -= HandleCameraViewActiveChanged;
    }

    private void HandleCameraViewActiveChanged(bool active)
    {
        if (active)
        {
            if (inventoryUiHiddenByCamera)
                return;

            inventoryUiHiddenByCamera = true;
            quickSlotsWereActive = quickSlotsPanel != null && quickSlotsPanel.activeSelf;
            inventoryPanelWasActive = inventoryPanel != null && inventoryPanel.activeSelf;

            quickSlotsPanel?.SetActive(false);
            if (inventoryPanel != quickSlotsPanel)
                inventoryPanel?.SetActive(false);
            return;
        }

        if (!inventoryUiHiddenByCamera)
            return;

        inventoryUiHiddenByCamera = false;
        quickSlotsPanel?.SetActive(quickSlotsWereActive);
        if (inventoryPanel != quickSlotsPanel)
            inventoryPanel?.SetActive(inventoryPanelWasActive);
    }
}

[RequireComponent(typeof(CameraBattery), typeof(HackingSessionController))]
public sealed class EquipmentController : MonoBehaviour
{
    private InventoryUI inventoryUI;
    private Transform handPoint;
    private ItemData equippedData;
    private GameObject heldVisual;
    private WalkieTalkieController walkieTalkieController;
    private StarterAssets.PersonController playerController;
    private CameraBattery cameraBattery;
    private CameraViewSwitcher cameraViewSwitcher;
    private ulong equippedNetworkObjectId;
    private ulong batteryStateNetworkObjectId;
    private readonly Dictionary<ulong, float> localCameraBatteryRatios = new();

    public ItemData EquippedData => equippedData;
    public GameObject HeldVisual => heldVisual;

    private void Awake()
    {
        walkieTalkieController = transform.root.GetComponentInChildren<WalkieTalkieController>(true);
        playerController = transform.root.GetComponent<StarterAssets.PersonController>();
        cameraBattery = GetComponent<CameraBattery>();
        cameraBattery.ChargeChanged += HandleCameraChargeChanged;
        EnsureHandPoint(transform);
    }

    private void OnDestroy()
    {
        if (cameraBattery != null)
            cameraBattery.ChargeChanged -= HandleCameraChargeChanged;
    }

    private void Update()
    {
        if (heldVisual == null) return;

        bool walkieTalkieVisible = walkieTalkieController != null &&
                                   walkieTalkieController.IsEquipped;
        bool cameraViewActive = cameraViewSwitcher != null &&
                                cameraViewSwitcher.IsCameraViewActive;
        bool shouldShowHeldVisual = !walkieTalkieVisible && !cameraViewActive;

        if (heldVisual.activeSelf != shouldShowHeldVisual)
            heldVisual.SetActive(shouldShowHeldVisual);
    }

    public void Configure(InventoryUI ui, Transform cameraTransform)
    {
        inventoryUI = ui;
        walkieTalkieController = cameraTransform.root.GetComponentInChildren<WalkieTalkieController>(true);
        playerController = cameraTransform.root.GetComponent<StarterAssets.PersonController>();
        cameraViewSwitcher = cameraTransform.root.GetComponentInChildren<CameraViewSwitcher>(true);
        cameraViewSwitcher?.BindBattery(cameraBattery);
        EnsureHandPoint(cameraTransform);
        RefreshSelectedItem();
    }

    public void RefreshSelectedItem()
    {
        RemoveReleasedCameraBatteryStates();

        InventoryInfo selectedItem = inventoryUI?.GetSelectedItem();
        ItemData selectedData = selectedItem?.itemData;
        ulong selectedNetworkObjectId = 0;
        InventoryManager.Instance?.TryGetNetworkObjectId(
            selectedItem, out selectedNetworkObjectId);

        playerController?.SetInventorySelectedItem(selectedData, selectedNetworkObjectId);

        if (selectedData == equippedData &&
            selectedNetworkObjectId == equippedNetworkObjectId &&
            (selectedData == null || heldVisual != null))
            return;

        Equip(selectedData, selectedNetworkObjectId);
    }

    public bool TryUseEquippedItem()
    {
        InventoryInfo selected = inventoryUI?.GetSelectedItem();

        if (selected?.itemData is not BatteryItemData battery)
            return false;

        if (InventoryManager.Instance == null ||
            !InventoryManager.Instance.ContainsItemOfType<CameraItemData>())
            return false;

        if (!cameraBattery.TryRecharge(battery.RechargeRatio))
            return false;

        InventoryManager.Instance.RemoveItem(selected);
        return true;
    }

    private void Equip(ItemData data, ulong networkObjectId)
    {
        ClearHeldVisual();
        equippedData = data;
        equippedNetworkObjectId = networkObjectId;
        cameraViewSwitcher?.SetCameraEquipped(data is CameraItemData);

        if (data is CameraItemData && networkObjectId != 0)
            RestoreNetworkCameraBattery(networkObjectId);

        if (data == null || handPoint == null) return;

        bool usingWorldPrefab = data.heldPrefab == null;
        GameObject visualPrefab = usingWorldPrefab ? data.itemPrefab : data.heldPrefab;
        if (visualPrefab == null)
        {
            Debug.LogWarning($"[Equipment] '{data.itemName}'??Held Prefab ?먮뒗 Item Prefab???놁뒿?덈떎.");
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

    private void RestoreNetworkCameraBattery(ulong networkObjectId)
    {
        batteryStateNetworkObjectId = networkObjectId;

        if (localCameraBatteryRatios.TryGetValue(
                networkObjectId, out float cachedRatio))
        {
            cameraBattery.SetChargeRatio(cachedRatio);
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(
                networkObjectId, out NetworkObject itemObject) ||
            !itemObject.TryGetComponent(out NetworkWorldItem networkItem))
        {
            Debug.LogWarning(
                $"[Equipment] NVCam 네트워크 오브젝트 {networkObjectId}의 배터리 상태를 찾지 못했습니다.",
                this);
            return;
        }

        cameraBattery.SetChargeRatio(networkItem.CameraBatteryRatio);
    }

    private void HandleCameraChargeChanged(float ratio)
    {
        if (batteryStateNetworkObjectId != 0)
            localCameraBatteryRatios[batteryStateNetworkObjectId] = ratio;
    }

    private void RemoveReleasedCameraBatteryStates()
    {
        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null || localCameraBatteryRatios.Count == 0)
            return;

        List<ulong> releasedIds = null;
        foreach (ulong networkObjectId in localCameraBatteryRatios.Keys)
        {
            if (inventory.ContainsNetworkObjectId(networkObjectId))
                continue;

            releasedIds ??= new List<ulong>();
            releasedIds.Add(networkObjectId);
        }

        if (releasedIds == null)
            return;

        foreach (ulong networkObjectId in releasedIds)
            localCameraBatteryRatios.Remove(networkObjectId);

        if (batteryStateNetworkObjectId != 0 &&
            !inventory.ContainsNetworkObjectId(batteryStateNetworkObjectId))
        {
            batteryStateNetworkObjectId = 0;
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

        // 손에 보이는 프리팹은 시야를 렌더링하지 않는다. 실제 카메라 기능은
        // 플레이어 CameraParent 아래의 ItemCam 한 곳에서만 담당한다.
        foreach (Camera heldCamera in visual.GetComponentsInChildren<Camera>(true))
            heldCamera.enabled = false;

        foreach (AudioListener heldListener in visual.GetComponentsInChildren<AudioListener>(true))
            heldListener.enabled = false;

        foreach (CameraItem heldCameraItem in visual.GetComponentsInChildren<CameraItem>(true))
            heldCameraItem.enabled = false;
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
