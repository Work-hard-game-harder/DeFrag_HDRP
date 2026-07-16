using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;

public class CameraItem : MonoBehaviour
{
    public enum CameraMode { Normal, Infrared }
    public CameraMode currentMode = CameraMode.Normal;

    public AudioSource audioSource;
    public AudioClip clickSound;   // 모드 전환 사운드
    public AudioClip shutterSound; // 촬영 사운드

    private bool isActive = false; // G키로 활성화 여부
    private Camera cameraView;

    public PostProcessProfile normalProfile;
    public PostProcessProfile infraredProfile;
    public PostProcessVolume volume;

    [Header("Battery Settings")]
    [SerializeField] private Image batteryBar;   // UI 게이지바 연결
    [SerializeField] private float maxBatteryLife = 100f;    // 최대 배터리 용량
    [SerializeField] public float batteryLife = 100f;   // 배터리 잔량
    [SerializeField] public float batteryDrainRate = 0.33f; // 초당 소모량

    private bool isInfrared = false;

    void Start()
    {
        cameraView = GetComponent<Camera>();
        // cameraView.enabled = false; // 처음엔 꺼둠
    }

    void Update()
    {
        // G키로 카메라 활성화
        /* if (Input.GetKeyDown(KeyCode.G))
        {
            isActive = !isActive;
            cameraView.enabled = isActive;
        }

        if (!isActive) return;

        */

        // 모드 전환 (우클릭)
        if (Input.GetMouseButtonDown(1))
        {
            ToggleMode();
        }

        // 촬영 (좌클릭)
        if (Input.GetMouseButtonDown(0))
        {
            TakePhoto();
        }

        // 적외선 모드일 때 배터리 소모
        if (currentMode == CameraMode.Infrared)
        {
            batteryLife -= batteryDrainRate * Time.deltaTime;
            if (batteryLife <= 0)
            {
                batteryLife = 0;
                currentMode = CameraMode.Normal; // 배터리 다 닳으면 일반 모드로 강제 전환
            }
        }

        // 배터리 UI 업데이트
        if (batteryBar != null)
        {
            float batteryLevel = batteryLife / maxBatteryLife;
            batteryBar.fillAmount = batteryLevel;
            batteryBar.color = Color.Lerp(Color.red, Color.green, batteryLevel);
        }
    }

    void ToggleMode()
    {
        currentMode = (currentMode == CameraMode.Normal) ? CameraMode.Infrared : CameraMode.Normal;
        audioSource.PlayOneShot(clickSound);
        // 적외선 모드일 때 화면 효과 적용

        if (currentMode == CameraMode.Infrared)
        {
            volume.profile = infraredProfile;
            Debug.Log("Infrared Mode");
        }
        else
        {
            volume.profile = normalProfile;
            Debug.Log("Normal Mode");
        }
    }

    void TakePhoto()
    {
        audioSource.PlayOneShot(shutterSound);
        // 여기에 이벤트 트리거 작성
        Debug.Log("Photo Taken!");
    }
}
