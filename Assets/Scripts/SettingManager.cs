using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// 씬 전환 시에도 유지되는 Setting Panel 매니저 (싱글톤 + DontDestroyOnLoad)
/// BrightnessScript / MicSelectScript / MicVolumeController / ResolutionManager 통합
/// 모든 설정값은 PlayerPrefs에 string 키로 저장됩니다.
/// </summary>
public class SettingManager : MonoBehaviour
{
    // ──────────────────────────────────────────────
    // 싱글톤
    // ──────────────────────────────────────────────
    public static SettingManager Instance { get; private set; }

    // ──────────────────────────────────────────────
    // PlayerPrefs 키 상수
    // ──────────────────────────────────────────────
    private const string KEY_BGM = "Setting_BGM";
    private const string KEY_SFX = "Setting_SFX";
    private const string KEY_MOUSE_SENSITIVITY = "Setting_MouseSensitivity";
    private const string KEY_BRIGHTNESS = "brightness.exposure";  // BrightnessScript와 동일 키 유지
    private const string KEY_BRIGHTNESS_FIRST = "brightness.firstRun";
    private const string KEY_MIC_VOLUME = "Setting_MicVolume";
    private const string KEY_MIC_INDEX = "Setting_MicIndex";
    private const string KEY_RESOLUTION_INDEX = "Setting_ResolutionIndex";
    private const string KEY_INVERT_Y = "Setting_InvertY";
    private const string KEY_DISPLAY_MODE = "Setting_DisplayMode"; // 0 = FullScreenWindow, 1 = Window
    private const int DEFAULT_DISPLAY_MODE = 0; // FullScreenWindow

    // ──────────────────────────────────────────────
    // 기본값 상수
    // ──────────────────────────────────────────────
    private const float DEFAULT_BGM = 1.0f;
    private const float DEFAULT_SFX = 1.0f;
    private const float DEFAULT_MOUSE_SENSITIVITY = 0.5f;
    private const float DEFAULT_BRIGHTNESS = 0f;
    private const float DEFAULT_MIC_VOLUME = 10f;  // MicVolumeController 기본값과 동일
    private const int DEFAULT_MIC_INDEX = 0;
    private const bool DEFAULT_INVERT_Y = false;

    // BrightnessScript 상수
    private const float MIN_EXPOSURE = -2f;
    private const float MAX_EXPOSURE = 2f;
    private const float SMOOTH_TIME = 0.15f;

    // MicVolumeController 상수
    private const float MIC_VOL_MIN = 0f;
    private const float MIC_VOL_MAX = 20f;

    // ──────────────────────────────────────────────
    // Inspector 연결
    // ──────────────────────────────────────────────
    [Header("Setting Panel")]
    [SerializeField] private GameObject settingPanel;
    [SerializeField] private Canvas settingCanvas; // ← Canvas를 직접 연결

    [Header("Sliders")]
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private Slider brightnessSlider;
    [SerializeField] private Slider micVolumeSlider;         // MicVolumeController의 volumeSlider

    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown micDropdown;       // MicSelectScript의 micDropdown
    [SerializeField] private TMP_Dropdown resolutionDropdown;// ResolutionManager의 resolutionDropdown
    [SerializeField] private TMP_Dropdown displayModeDropdown;

    [Header("Toggle")]
    [SerializeField] private Toggle invertYToggle;

    [Header("Post Processing")]
    [SerializeField] private Volume globalVolume;

    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer audioMixer;          // MicVolumeController의 audioMixer

    // ──────────────────────────────────────────────
    // 현재 설정값 프로퍼티 (외부 읽기 전용)
    // ──────────────────────────────────────────────
    public float BGM { get; private set; }
    public float SFX { get; private set; }
    public float MouseSensitivity { get; private set; }
    public float Brightness { get; private set; }
    public float MicVolume { get; private set; }
    public int MicIndex { get; private set; }
    public int ResolutionIndex { get; private set; }
    public bool InvertY { get; private set; }
    public int DisplayModeIndex { get; private set; } // 0 = FullScreenWindow, 1 = Window

    // MicSelectScript.selectedMic 대체 — 외부에서 참조 가능
    public string SelectedMic { get; private set; }

    // ──────────────────────────────────────────────
    // 내부 캐시
    // ──────────────────────────────────────────────
    private ColorAdjustments colorAdjustments;
    private Coroutine smoothBrightnessCoroutine;

    // ResolutionManager의 해상도 목록
    private readonly List<Resolution> resolutions = new List<Resolution>
    {
        new Resolution { width = 1280,  height = 720  },
        new Resolution { width = 1280,  height = 800  },
        new Resolution { width = 1440,  height = 900  },
        new Resolution { width = 1600,  height = 900  },
        new Resolution { width = 1680,  height = 1050 },
        new Resolution { width = 1920,  height = 1080 },
        new Resolution { width = 1920,  height = 1200 },
        new Resolution { width = 2048,  height = 1280 },
        new Resolution { width = 2560,  height = 1440 },
        new Resolution { width = 2560,  height = 1600 },
        new Resolution { width = 2880,  height = 1800 },
        new Resolution { width = 3480,  height = 2160 },
    };

    // ──────────────────────────────────────────────
    // Unity 생명주기
    // ──────────────────────────────────────────────
    private void Awake()
    {
        ClearSettingsPrefs();
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        CacheColorAdjustments();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        InitResolutionDropdown(); // 해상도 목록 먼저 구성 (optimal 인덱스 계산)
        InitMicDropdown();        // 마이크 목록 구성
        InitBrightnessSlider();   // 밝기 슬라이더 범위 설정
        InitMicVolumeSlider();    // 마이크 볼륨 슬라이더 범위 설정

        LoadSettings();           // PlayerPrefs에서 값 불러오기
        ApplyAllSettings();       // 불러온 값 UI + 실제 적용
        RegisterUICallbacks();    // UI 이벤트 연결
        InitDisplayModeDropdown(); // DisplayMode 초기화
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // 씬 전환 시 Camera 재연결 (Screen Space - Camera 대응)
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ClosePanel();
        AudioManager.Instance?.PlayBGMForScene(scene.name);
        StartCoroutine(ReconnectCamera());
    }

    private IEnumerator ReconnectCamera()
    {
        yield return null;
        while (Camera.main == null)
        {
            yield return null;
        }

        if (settingCanvas != null)
        {
            settingCanvas.worldCamera = Camera.main;
            Debug.Log($"[SettingManager] Camera 재연결 완료: {Camera.main.name}");
        }
    }

    // ──────────────────────────────────────────────
    // 초기화
    // ──────────────────────────────────────────────
    private void CacheColorAdjustments()
    {
        if (globalVolume == null || globalVolume.profile == null) return;
        if (!globalVolume.profile.TryGet(out colorAdjustments))
            colorAdjustments = globalVolume.profile.Add<ColorAdjustments>(true);
    }

    private void InitBrightnessSlider()
    {
        if (brightnessSlider == null) return;
        brightnessSlider.minValue = MIN_EXPOSURE;
        brightnessSlider.maxValue = MAX_EXPOSURE;
    }

    private void InitMicVolumeSlider()
    {
        if (micVolumeSlider == null) return;
        micVolumeSlider.minValue = MIC_VOL_MIN;
        micVolumeSlider.maxValue = MIC_VOL_MAX;
    }

    // ResolutionManager.Start() 로직 통합
    private void InitResolutionDropdown()
    {
        if (resolutionDropdown == null) return;

        resolutionDropdown.ClearOptions();
        List<string> options = new List<string>();
        int optimal = 0;

        for (int i = 0; i < resolutions.Count; i++)
        {
            string option = resolutions[i].width + " x " + resolutions[i].height;
            if (resolutions[i].width == Screen.currentResolution.width &&
                resolutions[i].height == Screen.currentResolution.height)
            {
                optimal = i;
                option += " *";
            }
            options.Add(option);
        }

        resolutionDropdown.AddOptions(options);

        // 저장된 값이 없으면 현재 모니터 최적 해상도를 기본값으로 사용
        ResolutionIndex = PlayerPrefs.GetInt(KEY_RESOLUTION_INDEX, optimal);
    }

    // MicSelectScript.Start() 로직 통합
    private void InitMicDropdown()
    {
        if (micDropdown == null) return;

        string[] devices = Microphone.devices;
        micDropdown.ClearOptions();
        micDropdown.AddOptions(new List<string>(devices));

        if (devices.Length > 0)
        {
            int savedIndex = PlayerPrefs.GetInt(KEY_MIC_INDEX, DEFAULT_MIC_INDEX);
            MicIndex = Mathf.Clamp(savedIndex, 0, devices.Length - 1);
            SelectedMic = devices[MicIndex];
        }
        else
        {
            Debug.LogWarning("[SettingManager] 연결된 마이크 장치가 없습니다.");
        }
    }

    private void InitDisplayModeDropdown()
    {
        if (displayModeDropdown == null) return;
        List<string> options = new List<string> { "Full Screen", "Window Screen" };
        displayModeDropdown.ClearOptions();
        displayModeDropdown.AddOptions(options);
        DisplayModeIndex = PlayerPrefs.GetInt(KEY_DISPLAY_MODE, DEFAULT_DISPLAY_MODE);
        SetDropdownWithoutNotify(displayModeDropdown, DisplayModeIndex);
        ApplyDisplayMode(DisplayModeIndex);
    }

    // ──────────────────────────────────────────────
    // 설정값 저장
    // ──────────────────────────────────────────────
    public void SaveSettings()
    {
        PlayerPrefs.SetFloat(KEY_BGM, BGM);
        PlayerPrefs.SetFloat(KEY_SFX, SFX);
        PlayerPrefs.SetFloat(KEY_MOUSE_SENSITIVITY, MouseSensitivity);
        PlayerPrefs.SetFloat(KEY_BRIGHTNESS, Brightness);
        PlayerPrefs.SetFloat(KEY_MIC_VOLUME, MicVolume);
        PlayerPrefs.SetInt(KEY_MIC_INDEX, MicIndex);
        PlayerPrefs.SetInt(KEY_RESOLUTION_INDEX, ResolutionIndex);
        PlayerPrefs.SetInt(KEY_INVERT_Y, InvertY ? 1 : 0);
        PlayerPrefs.SetInt(KEY_DISPLAY_MODE, DisplayModeIndex);
        PlayerPrefs.Save();
    }

    // ──────────────────────────────────────────────
    // 설정값 불러오기
    // ──────────────────────────────────────────────
    public void LoadSettings()
    {
        BGM = PlayerPrefs.GetFloat(KEY_BGM, DEFAULT_BGM);
        SFX = PlayerPrefs.GetFloat(KEY_SFX, DEFAULT_SFX);
        MouseSensitivity = PlayerPrefs.GetFloat(KEY_MOUSE_SENSITIVITY, DEFAULT_MOUSE_SENSITIVITY);
        MicVolume = PlayerPrefs.GetFloat(KEY_MIC_VOLUME, DEFAULT_MIC_VOLUME);
        MicIndex = PlayerPrefs.GetInt(KEY_MIC_INDEX, DEFAULT_MIC_INDEX);
        ResolutionIndex = PlayerPrefs.GetInt(KEY_RESOLUTION_INDEX, ResolutionIndex); // InitResolutionDropdown에서 optimal 세팅됨
        InvertY = PlayerPrefs.GetInt(KEY_INVERT_Y, DEFAULT_INVERT_Y ? 1 : 0) == 1;
        DisplayModeIndex = PlayerPrefs.GetInt(KEY_DISPLAY_MODE, DEFAULT_DISPLAY_MODE);

        // BrightnessScript 방식 그대로 유지
        if (!PlayerPrefs.HasKey(KEY_BRIGHTNESS_FIRST))
        {
            // 최초 실행 시 기본값 0 적용
            Brightness = DEFAULT_BRIGHTNESS;
            PlayerPrefs.SetInt(KEY_BRIGHTNESS_FIRST, 0);
            PlayerPrefs.SetFloat(KEY_BRIGHTNESS, Brightness);
            PlayerPrefs.Save();
        }
        else
        {
            float saved = PlayerPrefs.GetFloat(KEY_BRIGHTNESS,
                colorAdjustments != null ? colorAdjustments.postExposure.value : DEFAULT_BRIGHTNESS);
            Brightness = Mathf.Clamp(saved, MIN_EXPOSURE, MAX_EXPOSURE);
        }
    }

    // ──────────────────────────────────────────────
    // 전체 적용 (UI 반영 + 실제 게임 적용)
    // ──────────────────────────────────────────────
    private void ApplyAllSettings()
    {
        // UI 반영 (콜백 미발생)
        SetSliderWithoutNotify(bgmSlider, BGM);
        SetSliderWithoutNotify(sfxSlider, SFX);
        SetSliderWithoutNotify(mouseSensitivitySlider, MouseSensitivity);
        SetSliderWithoutNotify(brightnessSlider, Brightness);
        SetSliderWithoutNotify(micVolumeSlider, MicVolume);
        SetDropdownWithoutNotify(micDropdown, MicIndex);
        SetDropdownWithoutNotify(resolutionDropdown, ResolutionIndex);
        SetToggleWithoutNotify(invertYToggle, InvertY);
        SetDropdownWithoutNotify(displayModeDropdown, DisplayModeIndex);

        // 실제 게임 적용
        ApplyBGM(BGM);
        ApplySFX(SFX);
        ApplyBrightnessImmediate(Brightness); // 리셋/로드 시엔 즉시 적용
        ApplyMicVolume(MicVolume);
        ApplyResolution(ResolutionIndex);
        ApplyDisplayMode(DisplayModeIndex);
    }

    // ──────────────────────────────────────────────
    // UI 콜백 등록
    // ──────────────────────────────────────────────
    private void RegisterUICallbacks()
    {
        if (bgmSlider != null)
            bgmSlider.onValueChanged.AddListener(v =>
            { BGM = v; ApplyBGM(v); SaveSettings(); });

        if (sfxSlider != null)
            sfxSlider.onValueChanged.AddListener(v =>
            { SFX = v; ApplySFX(v); SaveSettings(); });

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.onValueChanged.AddListener(v =>
            { MouseSensitivity = v; SaveSettings(); });

        // BrightnessScript.OnExposureChanged 로직 통합 — 슬라이더 조작 시 부드러운 전환
        if (brightnessSlider != null)
            brightnessSlider.onValueChanged.AddListener(v =>
            { Brightness = v; ApplyBrightnessSmooth(v); SaveSettings(); });

        // MicVolumeController.OnVolumeChanged 로직 통합
        if (micVolumeSlider != null)
            micVolumeSlider.onValueChanged.AddListener(v =>
            { MicVolume = v; ApplyMicVolume(v); SaveSettings(); });

        // MicSelectScript.OnMicSelected 로직 통합
        if (micDropdown != null)
            micDropdown.onValueChanged.AddListener(index =>
            {
                MicIndex = index;
                string[] devices = Microphone.devices;
                if (devices.Length > index)
                    SelectedMic = devices[index];
                SaveSettings();
            });

        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(index =>
            { ResolutionIndex = index; ApplyResolution(index); SaveSettings(); });

        if (invertYToggle != null)
            invertYToggle.onValueChanged.AddListener(isOn =>
            { InvertY = isOn; SaveSettings(); });

        if (displayModeDropdown != null)
            displayModeDropdown.onValueChanged.AddListener(index =>
            {
                DisplayModeIndex = index;
                ApplyDisplayMode(index);
                SaveSettings();
            });
    }

    // ──────────────────────────────────────────────
    // 실제 게임 적용 메서드
    // ──────────────────────────────────────────────
    private void ApplyBGM(float value)
    {
        AudioManager.Instance?.SetBGMVolume(value);

        if (audioMixer != null)
        {
            float dB = value > 0 ? Mathf.Log10(value) * 20f : -80f;
            audioMixer.SetFloat("BGMVolume", dB);
        }
    }

    private void ApplySFX(float value)
    {
        AudioManager.Instance?.SetSFXVolume(value);

        if (audioMixer != null)
        {
            float dB = value > 0 ? Mathf.Log10(value) * 20f : -80f;
            audioMixer.SetFloat("SFXVolume", dB);
        }
    }
    private void ApplyBrightnessImmediate(float value)
    {
        if (colorAdjustments == null) CacheColorAdjustments();
        if (colorAdjustments == null) return;

        if (smoothBrightnessCoroutine != null)
        {
            StopCoroutine(smoothBrightnessCoroutine);
            smoothBrightnessCoroutine = null;
        }
        colorAdjustments.postExposure.value = value;
    }
    private void ApplyBrightnessSmooth(float target)
    {
        if (colorAdjustments == null) CacheColorAdjustments();
        if (colorAdjustments == null) return;

        if (smoothBrightnessCoroutine != null) StopCoroutine(smoothBrightnessCoroutine);
        smoothBrightnessCoroutine = StartCoroutine(SmoothSetExposure(target));
    }

    private IEnumerator SmoothSetExposure(float target)
    {
        float start = colorAdjustments.postExposure.value;
        float t = 0f;
        float span = Mathf.Max(0.0001f, SMOOTH_TIME);

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / span;
            colorAdjustments.postExposure.value = Mathf.Lerp(start, target, t);
            yield return null;
        }

        colorAdjustments.postExposure.value = target;
        smoothBrightnessCoroutine = null;
    }

    // MicVolumeController.OnVolumeChanged 로직 통합
    private void ApplyMicVolume(float value)
    {
        if (audioMixer != null)
            audioMixer.SetFloat("MicVolume", value);
    }

    // ResolutionManager.SetResolution 로직 통합
    private void ApplyResolution(int index)
    {
        if (index < 0 || index >= resolutions.Count) return;
        Resolution res = resolutions[index];
        Screen.SetResolution(res.width, res.height, Screen.fullScreen);
    }
    private void ApplyDisplayMode(int index)
    {
        switch (index)
        {
            case 0:
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                break;
            case 1:
                Screen.fullScreenMode = FullScreenMode.Windowed;
                break;
            default:
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                break;
        }
    }
    // ──────────────────────────────────────────────
    // 리셋 — 모든 값을 기본값으로 되돌리기
    // ──────────────────────────────────────────────

    /// 1. 프로퍼티를 기본값으로 덮어쓰기
    /// 2. UI 반영
    /// 3. 실제 게임(볼륨·밝기·해상도 등)에 즉시 적용
    /// 4. PlayerPrefs에 기본값으로 저장

    private void ClearSettingsPrefs()
    {
        PlayerPrefs.DeleteKey(KEY_BGM);
        PlayerPrefs.DeleteKey(KEY_SFX);
        PlayerPrefs.DeleteKey(KEY_MOUSE_SENSITIVITY);
        PlayerPrefs.DeleteKey(KEY_BRIGHTNESS);
        PlayerPrefs.DeleteKey(KEY_BRIGHTNESS_FIRST);
        PlayerPrefs.DeleteKey(KEY_MIC_VOLUME);
        PlayerPrefs.DeleteKey(KEY_MIC_INDEX);
        PlayerPrefs.DeleteKey(KEY_RESOLUTION_INDEX);
        PlayerPrefs.DeleteKey(KEY_INVERT_Y);
    }
    public void ResetUI()
    {
        BGM = DEFAULT_BGM;
        SFX = DEFAULT_SFX;
        MouseSensitivity = DEFAULT_MOUSE_SENSITIVITY;
        Brightness = DEFAULT_BRIGHTNESS;
        MicVolume = DEFAULT_MIC_VOLUME;
        MicIndex = DEFAULT_MIC_INDEX;
        InvertY = DEFAULT_INVERT_Y;
        // 해상도는 현재 모니터 최적값 유지 (원하면 아래 주석 해제)
        // ResolutionIndex = DEFAULT_RESOLUTION_INDEX;

        ApplyAllSettings(); // UI + 실제 게임 적용
        SaveSettings();     // PlayerPrefs 저장

        Debug.Log("[SettingManager] 설정이 초기화되었습니다.");
    }

    // ──────────────────────────────────────────────
    // 패널 열기 / 닫기
    // ──────────────────────────────────────────────
    public void OpenPanel()
    {
        if (settingPanel != null)
        {
            settingPanel.SetActive(true);
        }

    }

    public void ClosePanel()
    {
        if (settingPanel != null)
        {
            settingPanel.SetActive(false);
        }
    }

    // 닫기 버튼 전용 메서드 추가
    public void ClosePanelWithSFX()
    {
        AudioManager.Instance?.PlaySFX("Button1");
        ClosePanel();
    }

    // ──────────────────────────────────────────────
    // 헬퍼 — 콜백 없이 UI 값 변경
    // ──────────────────────────────────────────────
    private void SetSliderWithoutNotify(Slider s, float v)
    {
        if (s != null) s.SetValueWithoutNotify(v);
    }

    private void SetDropdownWithoutNotify(TMP_Dropdown d, int i)
    {
        if (d != null) d.SetValueWithoutNotify(i);
    }

    private void SetToggleWithoutNotify(Toggle t, bool isOn)
    {
        if (t != null) t.SetIsOnWithoutNotify(isOn);
    }
}