using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;

[RequireComponent(typeof(CameraBattery))]
public sealed class CameraItem : MonoBehaviour
{
    public enum CameraMode { Normal, Infrared }

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip clickSound;
    [SerializeField] private AudioClip shutterSound;

    [Header("View")]
    [SerializeField] private PostProcessProfile normalProfile;
    [SerializeField] private PostProcessProfile infraredProfile;
    [SerializeField] private PostProcessVolume volume;
    [SerializeField] private NightVisionController nightVisionController;

    [Header("Usage")]
    [Tooltip("CameraTest처럼 인벤토리를 거치지 않는 테스트 씬에서만 사용합니다.")]
    [SerializeField] private bool startsEquipped;
    [SerializeField] private KeyCode viewToggleKey = KeyCode.C;

    [Header("Battery")]
    [SerializeField] private Image batteryBar;
    [SerializeField, UnityEngine.Min(0f)] private float batteryDrainRate = 0.33f;

    private CameraBattery battery;
    private CameraMode currentMode;
    private bool isEquipped;
    private bool isViewActive;

    public bool IsEquipped => isEquipped;
    public bool IsViewActive => isViewActive;
    public CameraMode CurrentMode => currentMode;

    public event Action<CameraMode> ModeChanged;
    public event Action PhotoTaken;
    public event Action<bool> ViewActiveChanged;

    private void Awake()
    {
        battery = GetComponent<CameraBattery>();
        isEquipped = startsEquipped;
        SetMode(CameraMode.Normal);
    }

    public void Bind(CameraBattery sharedBattery)
    {
        battery = sharedBattery;
    }

    private void Update()
    {
        if (GameplayInputGate.IsBlocked)
            return;

        if (!isEquipped)
            return;

        if (Input.GetKeyDown(viewToggleKey))
            SetViewActive(!isViewActive);

        if (!isViewActive)
            return;

        if (Input.GetMouseButtonDown(1))
            ToggleMode();

        if (Input.GetMouseButtonDown(0))
            TakePhoto();

        if (currentMode == CameraMode.Infrared)
        {
            battery.Drain(batteryDrainRate * Time.deltaTime);
            if (battery.IsEmpty)
                SetMode(CameraMode.Normal);
        }

        if (batteryBar != null)
        {
            batteryBar.fillAmount = battery.ChargeRatio;
            batteryBar.color = Color.Lerp(Color.red, Color.green, battery.ChargeRatio);
        }
    }

    private void ToggleMode()
    {
        if (currentMode == CameraMode.Normal && battery.IsEmpty)
            return;

        SetMode(currentMode == CameraMode.Normal
            ? CameraMode.Infrared
            : CameraMode.Normal);
        if (audioSource != null && clickSound != null)
            audioSource.PlayOneShot(clickSound);
    }

    private void SetMode(CameraMode mode)
    {
        currentMode = mode;

        if (volume != null)
        {
            volume.profile = mode == CameraMode.Infrared
                ? infraredProfile
                : normalProfile;
        }

        if (nightVisionController != null)
            nightVisionController.SetNightVisionActive(mode == CameraMode.Infrared);

        ModeChanged?.Invoke(currentMode);
    }

    private void TakePhoto()
    {
        if (audioSource != null && shutterSound != null)
            audioSource.PlayOneShot(shutterSound);

        PhotoTaken?.Invoke();
        Debug.Log("Photo Taken!");
    }

    public void SetEquipped(bool equipped)
    {
        isEquipped = equipped;
        if (!isEquipped)
            SetViewActive(false);
    }

    public void SetViewActive(bool active)
    {
        if (active && !isEquipped)
            return;

        if (isViewActive == active)
            return;

        isViewActive = active;
        if (!isViewActive)
            SetMode(CameraMode.Normal);

        ViewActiveChanged?.Invoke(isViewActive);
    }
}
