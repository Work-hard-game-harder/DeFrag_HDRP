using UnityEngine;

public sealed class CameraViewSwitcher : MonoBehaviour
{
    [Header("Player View")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener playerAudioListener;

    [Header("Item Camera View")]
    [SerializeField] private Camera itemCamera;
    [SerializeField] private AudioListener itemAudioListener;
    [SerializeField] private CameraItem cameraItem;
    [SerializeField] private Canvas cameraOverlayCanvas;

    private bool localPresentationEnabled = true;

    public bool IsCameraEquipped { get; private set; }
    public bool IsCameraViewActive { get; private set; }
    public Camera ActiveCamera =>
        IsCameraViewActive && itemCamera != null ? itemCamera : playerCamera;

    private void Awake()
    {
        SetCameraViewActive(false);
    }

    private void OnEnable()
    {
        if (cameraItem != null)
            cameraItem.ViewActiveChanged += SetCameraViewActive;
    }

    private void OnDisable()
    {
        if (cameraItem != null)
            cameraItem.ViewActiveChanged -= SetCameraViewActive;

        SetCameraViewActive(false);
    }

    private void LateUpdate()
    {
        if (IsCameraViewActive)
            SynchronizeItemCameraPose();
    }

    public void SetCameraEquipped(bool equipped)
    {
        IsCameraEquipped = equipped;

        if (cameraItem != null)
            cameraItem.SetEquipped(equipped);

        if (!equipped)
            SetCameraViewActive(false);
    }

    public void BindBattery(CameraBattery battery)
    {
        if (cameraItem != null && battery != null)
            cameraItem.Bind(battery);
    }

    public void Configure(
        Camera sourceCamera,
        AudioListener sourceAudioListener,
        Camera equipmentCamera,
        AudioListener equipmentAudioListener,
        CameraItem equipmentCameraItem,
        Canvas overlayCanvas)
    {
        playerCamera = sourceCamera;
        playerAudioListener = sourceAudioListener;
        itemCamera = equipmentCamera;
        itemAudioListener = equipmentAudioListener;
        cameraItem = equipmentCameraItem;
        cameraOverlayCanvas = overlayCanvas;
    }

    public void SetLocalPresentationEnabled(bool enabled)
    {
        localPresentationEnabled = enabled;

        if (cameraItem != null)
            cameraItem.enabled = enabled;

        SetCameraViewActive(false);
    }

    private void SetCameraViewActive(bool active)
    {
        // 카메라 아이템을 장착하지 않았다면 ItemCam을 켤 수 없다.
        active &= IsCameraEquipped;

        IsCameraViewActive = active;

        if (active)
            SynchronizeItemCameraPose();

        if (playerCamera != null)
            playerCamera.enabled = localPresentationEnabled && !active;

        if (playerAudioListener != null)
            playerAudioListener.enabled = localPresentationEnabled && !active;

        if (itemCamera != null)
            itemCamera.enabled = localPresentationEnabled && active;

        if (itemAudioListener != null)
            itemAudioListener.enabled = localPresentationEnabled && active;

        if (cameraOverlayCanvas != null)
            cameraOverlayCanvas.enabled = localPresentationEnabled && active;
    }

    private void SynchronizeItemCameraPose()
    {
        if (playerCamera == null || itemCamera == null)
            return;

        Transform source = playerCamera.transform;
        Transform target = itemCamera.transform;

        target.SetPositionAndRotation(source.position, source.rotation);
        itemCamera.fieldOfView = playerCamera.fieldOfView;
    }
}
