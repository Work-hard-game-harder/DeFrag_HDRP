using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MicVolumeUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject micUIPanel;        // 워키토키 들고 있을 때만 표시
    [SerializeField] private Image[] volumeBars;           // 볼륨 바 배열 (Inspector에서 연결)
    [SerializeField] private Image micIcon;                // 마이크 아이콘
    [SerializeField] private TextMeshProUGUI statusText;   // 송신중 / 대기중

    [Header("Volume Bar Colors")]
    [SerializeField] private Color inactiveColor = new Color(0.2f, 0.2f, 0.2f);
    [SerializeField] private Color lowColor = Color.green;
    [SerializeField] private Color midColor = Color.yellow;
    [SerializeField] private Color highColor = Color.red;

    [Header("Mic Settings")]
    [SerializeField] private float micSensitivity = 100f;
    [SerializeField] private float noiseThreshold = 0.01f;
    [SerializeField] private float smoothSpeed = 10f;      // 볼륨 바 부드럽게 변화

    // 상수
    private const int SAMPLE_SIZE = 256;
    private const float LOW_THRESHOLD = 0.4f;
    private const float MID_THRESHOLD = 0.7f;

    private AudioClip micClip;
    private string currentMicDevice;
    private bool isMicActive = false;
    private float currentVolume = 0f;
    private float smoothVolume = 0f;

    public float CurrentVolume => smoothVolume;
    public bool IsMicActive => isMicActive;

    void Start()
    {
        if (micUIPanel != null)
            micUIPanel.SetActive(false);
    }

    void Update()
    {
        if (!isMicActive) return;

        UpdateVolume();
        UpdateUI();
    }

    // 워키토키 들었을 때 호출
    public void ShowUI()
    {
        if (micUIPanel != null)
            micUIPanel.SetActive(true);
    }

    // 워키토키 내렸을 때 호출
    public void HideUI()
    {
        if (micUIPanel != null)
            micUIPanel.SetActive(false);

        StopMic();
    }

    // 우스 클릭 시 마이크 시작
    public void StartMic()
    {
        // SettingManager에서 선택된 마이크 사용
        string selectedMic = SettingManager.Instance != null
            ? SettingManager.Instance.SelectedMic
            : null;

        if (string.IsNullOrEmpty(selectedMic) && Microphone.devices.Length > 0)
            selectedMic = Microphone.devices[0];

        if (string.IsNullOrEmpty(selectedMic))
        {
            Debug.LogWarning("[MicVolumeUI] 마이크 없음");
            return;
        }

        // 이미 같은 마이크로 실행 중이면 중복 시작 방지
        if (isMicActive && currentMicDevice == selectedMic) return;

        currentMicDevice = selectedMic;
        micClip = Microphone.Start(currentMicDevice, true, 1, AudioSettings.outputSampleRate);
        isMicActive = true;

        if (statusText != null)
            statusText.text = "송신 중";

        Debug.Log($"[MicVolumeUI] 마이크 시작: {currentMicDevice}");
    }

    // 마우스 클릭 해제 시 마이크 정지
    public void StopMic()
    {
        if (!isMicActive) return;

        Microphone.End(currentMicDevice);
        isMicActive = false;
        currentVolume = 0f;
        smoothVolume = 0f;

        if (statusText != null)
            statusText.text = "대기 중";

        ResetBars();
        Debug.Log("[MicVolumeUI] 마이크 정지");
    }

    // 볼륨 측정 (RMS 방식)
    void UpdateVolume()
    {
        if (micClip == null) return;

        int micPosition = Microphone.GetPosition(currentMicDevice) - SAMPLE_SIZE;
        if (micPosition < 0) return;

        float[] samples = new float[SAMPLE_SIZE];
        micClip.GetData(samples, micPosition);

        // RMS 계산
        float sum = 0f;
        foreach (float s in samples)
            sum += s * s;

        float rms = Mathf.Sqrt(sum / SAMPLE_SIZE) * micSensitivity;

        // SettingManager의 MicVolume 설정값 반영
        float micVolumeMultiplier = SettingManager.Instance != null
            ? SettingManager.Instance.MicVolume / 10f  // 기본값 10f 기준 정규화
            : 1f;

        currentVolume = rms < noiseThreshold ? 0f : Mathf.Clamp01(rms * micVolumeMultiplier);

        // 부드럽게 변화
        smoothVolume = Mathf.Lerp(smoothVolume, currentVolume, Time.deltaTime * smoothSpeed);
    }

    // 볼륨 바 UI 업데이트
    void UpdateUI()
    {
        if (volumeBars == null || volumeBars.Length == 0) return;

        for (int i = 0; i < volumeBars.Length; i++)
        {
            if (volumeBars[i] == null) continue;

            float threshold = (float)(i + 1) / volumeBars.Length;
            bool active = smoothVolume >= threshold;

            volumeBars[i].color = active ? GetBarColor(threshold) : inactiveColor;
        }

        // 마이크 아이콘 색상 변경
        if (micIcon != null)
            micIcon.color = smoothVolume > noiseThreshold ? lowColor : Color.gray;
    }

    // 볼륨 구간별 색상
    Color GetBarColor(float threshold)
    {
        if (threshold < LOW_THRESHOLD) return lowColor;
        if (threshold < MID_THRESHOLD) return midColor;
        return highColor;
    }

    // 볼륨 바 초기화
    void ResetBars()
    {
        if (volumeBars == null) return;
        foreach (Image bar in volumeBars)
        {
            if (bar != null)
                bar.color = inactiveColor;
        }

        if (micIcon != null)
            micIcon.color = Color.gray;
    }
}