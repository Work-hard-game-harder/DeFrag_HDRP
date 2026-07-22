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

    [Header("Battery")]
    [SerializeField] private Image batteryBar;
    [SerializeField, UnityEngine.Min(0f)] private float batteryDrainRate = 0.33f;

    private CameraBattery battery;
    private CameraMode currentMode;

    private void Awake()
    {
        battery = GetComponent<CameraBattery>();
        SetMode(CameraMode.Normal);
    }

    public void Bind(CameraBattery sharedBattery)
    {
        battery = sharedBattery;
    }

    private void Update()
    {
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
        audioSource.PlayOneShot(clickSound);
    }

    private void SetMode(CameraMode mode)
    {
        currentMode = mode;
        volume.profile = mode == CameraMode.Infrared
            ? infraredProfile
            : normalProfile;
    }

    private void TakePhoto()
    {
        audioSource.PlayOneShot(shutterSound);
        Debug.Log("Photo Taken!");
    }
}
