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

    public bool IsCameraEquipped { get; private set; }
    public bool IsCameraViewActive { get; private set; }

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

    private void SetCameraViewActive(bool active)
    {
        // 카메라 아이템을 장착하지 않았다면 ItemCam을 켤 수 없다.
        active &= IsCameraEquipped;

        IsCameraViewActive = active;

        if (playerCamera != null)
            playerCamera.enabled = !active;

        if (playerAudioListener != null)
            playerAudioListener.enabled = !active;

        if (itemCamera != null)
            itemCamera.enabled = active;

        if (itemAudioListener != null)
            itemAudioListener.enabled = active;
    }
}
