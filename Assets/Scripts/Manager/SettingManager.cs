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
using DeFrag.UI;

/// <summary>
/// 씬 전환 시에도 유지되는 Setting Panel 매니저 (싱글톤 + DontDestroyOnLoad)
/// BrightnessScript / MicSelectScript / MicVolumeController / ResolutionManager 통합
/// 모든 설정값은 PlayerPrefs에 string 키로 저장됩니다.
/// </summary>
[DefaultExecutionOrder(-10000)]
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
    private const string KEY_RESOLUTION_WIDTH = "Setting_ResolutionWidth";
    private const string KEY_RESOLUTION_HEIGHT = "Setting_ResolutionHeight";
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
    private const int MIC_SAMPLE_SIZE = 256;
    private const float MIC_METER_FLOOR_DB = -60f;
    private const float MIC_METER_CEILING_DB = -6f;

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

    [Header("MIC Control")]
    [SerializeField] private AudioSource micAudioSource; // 마이크 입력용 AudioSource
    [SerializeField] private AudioMixerGroup micMixerGroup; // AudioMixer 그룹 연결
    [SerializeField] private AudioMixer audioMixer;          // MicVolumeController의 audioMixer
    [SerializeField] private StableMicrophoneInput micLowLatency; // 게임 전체에서 유지되는 실제 마이크 캡처

    [Header("Pause Panel")]
    [SerializeField] private GameObject PausePanel;

    [Header("Cursor Policy")]
    [Tooltip("Scenes that keep the cursor visible while no modal menu is open.")]
    [SerializeField] private string[] cursorVisibleSceneNames =
    {
        "MainLobby",
        "LobbyScene",
        "CreateLobby"
    };

    // TMP_Dropdown creates its popup using a separate Canvas at runtime. Keep
    // enough sorting-order headroom so that popup and blocker canvases can be
    // rendered and raycast above the settings panel.
    private const int MENU_OVERLAY_SORTING_ORDER = 29000;
    private const int PAUSE_PANEL_SORTING_ORDER = 29010;
    private const int SETTING_PANEL_SORTING_ORDER = 29020;
    private Canvas pauseCanvas;
    private Canvas settingPanelCanvas;
    private Canvas menuOverlayCanvas;
    private readonly Dictionary<Canvas, CanvasRenderState> overriddenCanvases = new Dictionary<Canvas, CanvasRenderState>();
    private readonly Dictionary<GraphicRaycaster, bool> overriddenRaycasters =
        new Dictionary<GraphicRaycaster, bool>();

    private struct CanvasRenderState
    {
        public RenderMode renderMode;
        public Camera worldCamera;
        public float planeDistance;
        public bool overrideSorting;
        public int sortingLayerId;
        public int sortingOrder;
    }

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

    /// <summary>
    /// 로컬 화면의 해상도 적용이 완료된 다음 프레임에 호출됩니다.
    /// 비율별 별도 레이아웃이 필요한 UI만 선택적으로 구독하면 됩니다.
    /// </summary>
    public event System.Action<Vector2Int, float> ResolutionChanged;

    // MicSelectScript.selectedMic 대체 — 외부에서 참조 가능
    public string SelectedMic { get; private set; }

    /// <summary>0~1로 정규화된 현재 마이크 입력 레벨입니다.</summary>
    public float MicInputLevel { get; private set; }
    public bool IsMicPreviewActive { get; private set; }
    public string MicPreviewStatus { get; private set; } = "Microphone ready";

    /// <summary>슬라이더의 10을 원본 크기(1배)로 사용하는 실제 마이크 게인입니다.</summary>
    public float MicGain => Mathf.Max(0f, MicVolume / DEFAULT_MIC_VOLUME);
    public StableMicrophoneInput MicrophoneInput => micLowLatency;
    public bool IsPausePanelOpen => PausePanel != null && PausePanel.activeSelf;
    public static bool IsGamePaused => Instance != null && Instance.IsPausePanelOpen;

    public StablePlayerVoice playerVoice;

    // ──────────────────────────────────────────────
    // 내부 캐시
    // ──────────────────────────────────────────────
    private ColorAdjustments colorAdjustments;
    private Coroutine smoothBrightnessCoroutine;
    private Coroutine micPreviewCoroutine;
    private AudioClip micPreviewClip;
    private string activeMicDevice;
    private StableMicrophoneInput sharedMicSource;
    private Coroutine micRestartCoroutine;
    private Coroutine resolutionChangedCoroutine;
    private string micDeviceSignature = string.Empty;
    private float nextMicDevicePollTime;
    private readonly float[] micSamples = new float[MIC_SAMPLE_SIZE];

    // 16:9 해상도만 제공하며, 인덱스 변경에 안전하도록 실제 너비/높이도 함께 저장합니다.
    private readonly List<Resolution> resolutions = new List<Resolution>
    {
        new Resolution { width = 1280,  height = 720  },
        new Resolution { width = 1600,  height = 900  },
        new Resolution { width = 1920,  height = 1080 },
        new Resolution { width = 2560,  height = 1440 },
        new Resolution { width = 3840,  height = 2160 },
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
        ConfigureUICameraForGlobalVolume(settingCanvas != null ? settingCanvas.worldCamera : null);
        ConfigurePausePanelCanvas();
        ConfigureSettingPanelCanvas();
        SceneManager.sceneLoaded += OnSceneLoaded;
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        InitFloorUnlock();
    }
    void Update()
    {
        UpdateMicInputLevel();

        if (Time.unscaledTime >= nextMicDevicePollTime)
        {
            nextMicDevicePollTime = Time.unscaledTime + 1f;
            CheckForMicDeviceChanges();
            ConnectPlayerVoiceSources();
        }

        string currentSceneName = SceneManager.GetActiveScene().name;

        // MainLobby 씬에서는 ESC 입력 무시
        if (currentSceneName == "MainLobby")
            return;


        // 다른 씬에서는 ESC 입력 처리
        if (Input.GetKeyDown(KeyCode.Escape) && PausePanel != null)
            SetPausePanelState(!PausePanel.activeSelf);
    }

    private void LateUpdate()
    {
        if (IsPausePanelOpen || (settingPanel != null && settingPanel.activeInHierarchy))
            KeepMenuPanelsAboveAllCanvases();
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
        RestartManagedMicrophone();

        SetPausePanelState(false);
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        StopMicPreview();
        StopManagedMicrophoneSafely();
        RestoreOverriddenCanvases();
        if (IsPausePanelOpen)
            SetPlayerInputLock(false);
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    // 씬 전환 시 Camera 재연결 (Screen Space - Camera 대응)
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[SettingManager] OnSceneLoaded: {scene.name}");
        Debug.Log($"[SettingManager] Unlocked_MainLobby: {PlayerPrefs.GetInt("Unlocked_MainLobby", 0)}");

        ClosePanel();
        SetPausePanelState(false);
        AudioManager.Instance?.PlayBGMForScene(scene.name);
        StartCoroutine(ReconnectCamera());
        StartCoroutine(ReconnectPlayerVoiceSources());
    }

    private IEnumerator ReconnectCamera()
    {
        yield return null;
        Camera uiCamera = settingCanvas != null ? settingCanvas.worldCamera : null;
        if (uiCamera == null)
            uiCamera = GameObject.Find("UICamera")?.GetComponent<Camera>();

        if (uiCamera != null && settingCanvas != null)
        {
            settingCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            settingCanvas.worldCamera = uiCamera;
            ConfigureUICameraForGlobalVolume(uiCamera);
            EnsureMenuOverlayCanvas();
            Debug.Log($"[SettingManager] UI 카메라 재연결 완료: {uiCamera.name}");
        }
        else
        {
            Debug.LogWarning("[SettingManager] UI 카메라를 찾을 수 없습니다.");
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
        int optimal = FindClosestResolutionIndex(
            Screen.currentResolution.width,
            Screen.currentResolution.height);

        for (int i = 0; i < resolutions.Count; i++)
        {
            string aspectLabel = ResponsiveCanvasUtility.GetAspectLabel(
                resolutions[i].width,
                resolutions[i].height);
            string option = $"{resolutions[i].width} x {resolutions[i].height} ({aspectLabel})";
            if (resolutions[i].width == Screen.currentResolution.width &&
                resolutions[i].height == Screen.currentResolution.height)
            {
                option += " *";
            }
            options.Add(option);
        }

        resolutionDropdown.AddOptions(options);

        if (PlayerPrefs.HasKey(KEY_RESOLUTION_WIDTH) && PlayerPrefs.HasKey(KEY_RESOLUTION_HEIGHT))
        {
            ResolutionIndex = FindClosestResolutionIndex(
                PlayerPrefs.GetInt(KEY_RESOLUTION_WIDTH),
                PlayerPrefs.GetInt(KEY_RESOLUTION_HEIGHT));
        }
        else if (PlayerPrefs.HasKey(KEY_RESOLUTION_INDEX) &&
                 TryGetLegacyResolution(
                     PlayerPrefs.GetInt(KEY_RESOLUTION_INDEX),
                     out int legacyWidth,
                     out int legacyHeight))
        {
            // 이전 버전의 16:10/4:3/21:9 선택값도 가장 가까운 16:9 값으로 이관합니다.
            ResolutionIndex = FindClosestResolutionIndex(legacyWidth, legacyHeight);
        }
        else
        {
            ResolutionIndex = optimal;
        }
    }

    // MicSelectScript.Start() 로직 통합
    private void InitMicDropdown()
    {
        string[] devices = Microphone.devices;
        micDeviceSignature = string.Join("\n", devices);

        if (micDropdown != null)
        {
            micDropdown.ClearOptions();
            micDropdown.AddOptions(new List<string>(devices));
        }

        if (devices.Length > 0)
        {
            int savedIndex = PlayerPrefs.GetInt(KEY_MIC_INDEX, DEFAULT_MIC_INDEX);
            MicIndex = Mathf.Clamp(savedIndex, 0, devices.Length - 1);
            SelectedMic = devices[MicIndex];
            SetDropdownWithoutNotify(micDropdown, MicIndex);
        }
        else
        {
            SelectedMic = null;
            MicPreviewStatus = "No microphone detected";
            Debug.LogWarning("[SettingManager] 연결된 마이크 장치가 없습니다.");
        }
    }

    private void ConfigureUICameraForGlobalVolume(Camera uiCamera)
    {
        if (uiCamera == null || settingCanvas == null) return;

        settingCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        settingCanvas.worldCamera = uiCamera;
        uiCamera.allowHDR = true;

        HDAdditionalCameraData cameraData = uiCamera.GetComponent<HDAdditionalCameraData>();
        if (cameraData == null)
            cameraData = uiCamera.gameObject.AddComponent<HDAdditionalCameraData>();

        cameraData.customRenderingSettings = true;
        cameraData.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, true);
        cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;

        if (globalVolume != null)
        {
            int volumeLayer = 1 << globalVolume.gameObject.layer;
            cameraData.volumeLayerMask = cameraData.volumeLayerMask | volumeLayer;
            cameraData.volumeAnchorOverride = uiCamera.transform;
        }
    }

    private void RefreshMicDevicesPreservingSelection()
    {
        string previousDevice = SelectedMic;
        string[] devices = Microphone.devices;
        micDeviceSignature = string.Join("\n", devices);

        if (micDropdown != null)
        {
            micDropdown.ClearOptions();
            micDropdown.AddOptions(new List<string>(devices));
        }

        if (devices.Length == 0)
        {
            SelectedMic = null;
            MicIndex = 0;
            MicPreviewStatus = "No microphone detected";
            return;
        }

        int previousIndex = string.IsNullOrEmpty(previousDevice)
            ? -1
            : System.Array.IndexOf(devices, previousDevice);
        MicIndex = previousIndex >= 0 ? previousIndex : Mathf.Clamp(MicIndex, 0, devices.Length - 1);
        SelectedMic = devices[MicIndex];
        SetDropdownWithoutNotify(micDropdown, MicIndex);
    }

    private void CheckForMicDeviceChanges()
    {
        string signature = string.Join("\n", Microphone.devices);
        if (signature != micDeviceSignature)
        {
            RefreshMicDevicesPreservingSelection();
            RestartManagedMicrophone();
            return;
        }

        // 장치명은 그대로지만 출력 장치 재설정으로 캡처만 끊긴 경우도 복구합니다.
        if (micRestartCoroutine == null && micLowLatency != null &&
            !string.IsNullOrEmpty(SelectedMic) && !Microphone.IsRecording(SelectedMic))
            RestartManagedMicrophone();
    }

    private void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        // 헤드셋/스피커 전환은 샘플레이트만 바뀌는 경우도 있어 항상 캡처를 다시 엽니다.
        RestartManagedMicrophone();
    }

    private void RestartManagedMicrophone()
    {
        if (micRestartCoroutine != null)
            StopCoroutine(micRestartCoroutine);
        micRestartCoroutine = StartCoroutine(RestartManagedMicrophoneRoutine());
    }

    private IEnumerator RestartManagedMicrophoneRoutine()
    {
        MicPreviewStatus = "Connecting microphone...";

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            MicPreviewStatus = "Microphone permission denied";
            micRestartCoroutine = null;
            yield break;
        }

        // 출력 장치 변경 직후에는 Windows 장치 목록이 잠시 비는 경우가 있어 안정될 때까지 기다립니다.
        float deviceTimeout = Time.realtimeSinceStartup + 5f;
        while (Microphone.devices.Length == 0 && Time.realtimeSinceStartup < deviceTimeout)
            yield return new WaitForSecondsRealtime(0.25f);

        RefreshMicDevicesPreservingSelection();
        if (string.IsNullOrEmpty(SelectedMic))
        {
            micRestartCoroutine = null;
            yield break;
        }

        if (micLowLatency == null)
            micLowLatency = GetComponent<StableMicrophoneInput>();

        if (micLowLatency == null)
        {
            MicPreviewStatus = "Microphone capture is unavailable";
            micRestartCoroutine = null;
            yield break;
        }

        StopManagedMicrophoneSafely();
        yield return new WaitForSecondsRealtime(0.15f);
        micLowLatency.StartMic(MicIndex);
        sharedMicSource = micLowLatency;
        ConnectPlayerVoiceSources();

        float startTimeout = Time.realtimeSinceStartup + 3f;
        while ((!Microphone.IsRecording(SelectedMic) || Microphone.GetPosition(SelectedMic) <= 0) &&
               Time.realtimeSinceStartup < startTimeout)
            yield return null;

        IsMicPreviewActive = Microphone.IsRecording(SelectedMic) && Microphone.GetPosition(SelectedMic) > 0;
        MicPreviewStatus = IsMicPreviewActive ? "Listening" : "Microphone did not respond";
        micRestartCoroutine = null;
    }

    private void StopManagedMicrophoneSafely()
    {
        if (micLowLatency == null) return;

        string[] devices = micLowLatency.Devices;
        if (devices.Length > micLowLatency.CurrentDeviceIndex)
            micLowLatency.StopMic();
    }

    private void ConnectPlayerVoiceSources()
    {
        // 씬 프리팹 오버라이드나 네트워크 스폰 여부와 관계없이 녹음기를 보장합니다.
        EasyPeasyFirstPersonController.FirstPersonController[] controllers =
            FindObjectsByType<EasyPeasyFirstPersonController.FirstPersonController>(FindObjectsInactive.Include);
        foreach (EasyPeasyFirstPersonController.FirstPersonController controller in controllers)
        {
            if (controller.GetComponent<WalkieTalkieVoiceRecorder>() == null)
                controller.gameObject.AddComponent<WalkieTalkieVoiceRecorder>();
        }

        if (micLowLatency == null) return;

        StablePlayerVoice[] voices = FindObjectsByType<StablePlayerVoice>(FindObjectsInactive.Include);
        foreach (StablePlayerVoice voice in voices)
            voice.micSource = micLowLatency;
    }

    private IEnumerator ReconnectPlayerVoiceSources()
    {
        yield return null;
        ConnectPlayerVoiceSources();
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
    private void StartMicPreview()
    {
        if (string.IsNullOrEmpty(SelectedMic))
        {
            MicPreviewStatus = "No microphone detected";
            return;
        }

        // 인게임 캡처가 동작 중이면 같은 버퍼를 사용합니다.
        if (micLowLatency != null)
        {
            sharedMicSource = micLowLatency;
            IsMicPreviewActive = Microphone.IsRecording(SelectedMic);
            MicPreviewStatus = IsMicPreviewActive ? "Listening" : "Connecting microphone...";
            if (!IsMicPreviewActive)
                RestartManagedMicrophone();
            return;
        }

        StopMicPreview();

        micPreviewCoroutine = StartCoroutine(StartMicPreviewRoutine(SelectedMic));
    }

    private IEnumerator StartMicPreviewRoutine(string device)
    {
        MicPreviewStatus = "Waiting for microphone permission...";

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            MicPreviewStatus = "Microphone permission denied";
            micPreviewCoroutine = null;
            yield break;
        }

        // 플레이어가 이미 같은 장치를 캡처 중이면 녹음을 빼앗지 않고 공유 버퍼를 읽습니다.
        if (Microphone.IsRecording(device))
        {
            StableMicrophoneInput source = micLowLatency != null
                ? micLowLatency
                : FindAnyObjectByType<StableMicrophoneInput>();
            string[] sourceDevices = source != null ? source.Devices : null;
            if (sourceDevices != null && sourceDevices.Length > source.CurrentDeviceIndex &&
                sourceDevices[source.CurrentDeviceIndex] == device)
            {
                sharedMicSource = source;
                IsMicPreviewActive = true;
                MicPreviewStatus = "Listening";
                micPreviewCoroutine = null;
                yield break;
            }

            MicPreviewStatus = "Microphone is already in use";
            micPreviewCoroutine = null;
            yield break;
        }

        activeMicDevice = device;
        micPreviewClip = Microphone.Start(device, true, 1, AudioSettings.outputSampleRate);
        if (micPreviewClip == null)
        {
            activeMicDevice = null;
            MicPreviewStatus = "Unable to start microphone";
            micPreviewCoroutine = null;
            yield break;
        }

        float timeout = Time.realtimeSinceStartup + 3f;
        while (Microphone.GetPosition(device) <= 0 && Time.realtimeSinceStartup < timeout)
            yield return null;

        if (Microphone.GetPosition(device) <= 0)
        {
            if (Microphone.IsRecording(device))
                Microphone.End(device);
            micPreviewClip = null;
            activeMicDevice = null;
            IsMicPreviewActive = false;
            MicInputLevel = 0f;
            micPreviewCoroutine = null;
            MicPreviewStatus = "Microphone did not respond";
            yield break;
        }

        // 미리보기는 로컬 스피커로 재생하지 않아 하울링을 방지합니다.
        if (micAudioSource != null)
        {
            micAudioSource.Stop();
            micAudioSource.clip = micPreviewClip;
            micAudioSource.loop = true;
            micAudioSource.outputAudioMixerGroup = micMixerGroup;
        }

        IsMicPreviewActive = true;
        MicPreviewStatus = "Listening";
        micPreviewCoroutine = null;
        Debug.Log($"[SettingManager] 마이크 미리보기 시작: {device}");
    }

    private void StopMicPreview()
    {
        if (micPreviewCoroutine != null)
        {
            StopCoroutine(micPreviewCoroutine);
            micPreviewCoroutine = null;
        }

        if (!string.IsNullOrEmpty(activeMicDevice) && Microphone.IsRecording(activeMicDevice))
            Microphone.End(activeMicDevice);

        if (micAudioSource != null)
        {
            micAudioSource.Stop();
            micAudioSource.clip = null;
        }

        micPreviewClip = null;
        activeMicDevice = null;

        // 설정 패널만 닫힌 경우 인게임 캡처와 HUD는 계속 유지합니다.
        bool managedMicIsActive = micLowLatency != null && !string.IsNullOrEmpty(SelectedMic) &&
                                  Microphone.IsRecording(SelectedMic);
        sharedMicSource = managedMicIsActive ? micLowLatency : null;
        IsMicPreviewActive = managedMicIsActive;
        if (!managedMicIsActive)
            MicInputLevel = 0f;
    }

    private void UpdateMicInputLevel()
    {
        float target = 0f;

        if (sharedMicSource != null && sharedMicSource.CircularBuffer != null &&
            !string.IsNullOrEmpty(SelectedMic) && Microphone.IsRecording(SelectedMic))
        {
            float[] buffer = sharedMicSource.CircularBuffer;
            int count = Mathf.Min(MIC_SAMPLE_SIZE, buffer.Length);
            int readPosition = sharedMicSource.WritePos - count;
            if (readPosition < 0) readPosition += buffer.Length;

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                float sample = buffer[(readPosition + i) % buffer.Length];
                sum += sample * sample;
            }

            target = NormalizeMicRms(Mathf.Sqrt(sum / count) * MicGain);
        }
        else if (IsMicPreviewActive && micPreviewClip != null && !string.IsNullOrEmpty(activeMicDevice))
        {
            int position = Microphone.GetPosition(activeMicDevice);
            if (position >= MIC_SAMPLE_SIZE)
            {
                micPreviewClip.GetData(micSamples, position - MIC_SAMPLE_SIZE);
                float sum = 0f;
                for (int i = 0; i < micSamples.Length; i++)
                    sum += micSamples[i] * micSamples[i];

                target = NormalizeMicRms(Mathf.Sqrt(sum / micSamples.Length) * MicGain);
            }
        }

        float speed = target > MicInputLevel ? 18f : 7f;
        MicInputLevel = Mathf.MoveTowards(MicInputLevel, target, speed * Time.unscaledDeltaTime);
    }

    private static float NormalizeMicRms(float rms)
    {
        float decibels = 20f * Mathf.Log10(Mathf.Max(rms, 0.000001f));
        return Mathf.InverseLerp(MIC_METER_FLOOR_DB, MIC_METER_CEILING_DB, decibels);
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
        if (ResolutionIndex >= 0 && ResolutionIndex < resolutions.Count)
        {
            PlayerPrefs.SetInt(KEY_RESOLUTION_WIDTH, resolutions[ResolutionIndex].width);
            PlayerPrefs.SetInt(KEY_RESOLUTION_HEIGHT, resolutions[ResolutionIndex].height);
        }
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
        // InitResolutionDropdown에서 저장된 실제 크기를 16:9 목록의 인덱스로 변환합니다.
        ResolutionIndex = Mathf.Clamp(ResolutionIndex, 0, resolutions.Count - 1);
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
                {
                    SelectedMic = devices[index];
                    RestartManagedMicrophone();
                }
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
        {
            float decibels = value > 0f ? 20f * Mathf.Log10(value / DEFAULT_MIC_VOLUME) : -80f;
            audioMixer.SetFloat("MicVolume", decibels);
        }
    }

    // ResolutionManager.SetResolution 로직 통합
    private void ApplyResolution(int index)
    {
        if (index < 0 || index >= resolutions.Count) return;
        ResolutionIndex = index;
        Resolution res = resolutions[index];
        Screen.SetResolution(res.width, res.height, GetFullScreenMode(DisplayModeIndex));

        if (resolutionChangedCoroutine != null)
            StopCoroutine(resolutionChangedCoroutine);
        resolutionChangedCoroutine = StartCoroutine(NotifyResolutionChanged(res.width, res.height));
    }

    private IEnumerator NotifyResolutionChanged(int requestedWidth, int requestedHeight)
    {
        // Screen.width/height는 SetResolution 호출 직후가 아니라 다음 프레임에 갱신됩니다.
        yield return null;

        int appliedWidth = Screen.width > 0 ? Screen.width : requestedWidth;
        int appliedHeight = Screen.height > 0 ? Screen.height : requestedHeight;
        ResolutionChanged?.Invoke(
            new Vector2Int(appliedWidth, appliedHeight),
            (float)appliedWidth / Mathf.Max(1, appliedHeight));
        resolutionChangedCoroutine = null;
    }

    private static FullScreenMode GetFullScreenMode(int index)
    {
        return index == 1
            ? FullScreenMode.Windowed
            : FullScreenMode.FullScreenWindow;
    }

    private int FindClosestResolutionIndex(int width, int height)
    {
        int closestIndex = 0;
        long closestDifference = long.MaxValue;

        for (int i = 0; i < resolutions.Count; i++)
        {
            long widthDifference = resolutions[i].width - (long)width;
            long heightDifference = resolutions[i].height - (long)height;
            long difference = widthDifference * widthDifference + heightDifference * heightDifference;
            if (difference >= closestDifference)
                continue;

            closestDifference = difference;
            closestIndex = i;
        }

        return closestIndex;
    }

    private static bool TryGetLegacyResolution(int index, out int width, out int height)
    {
        (width, height) = index switch
        {
            0 => (1280, 720),
            1 => (1280, 800),
            2 => (1440, 900),
            3 => (1600, 900),
            4 => (1680, 1050),
            5 => (1920, 1080),
            6 => (1920, 1200),
            7 => (2048, 1280),
            8 => (2560, 1440),
            9 => (2560, 1600),
            10 => (2880, 1800),
            11 => (3840, 2160),
            12 => (1600, 1200),
            13 => (3440, 1440),
            _ => (0, 0)
        };

        return width > 0 && height > 0;
    }

    private void ApplyDisplayMode(int index)
    {
        Screen.fullScreenMode = GetFullScreenMode(index);
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
#if UNITY_EDITOR
        PlayerPrefs.DeleteKey(KEY_BGM);
        PlayerPrefs.DeleteKey(KEY_SFX);
        PlayerPrefs.DeleteKey(KEY_MOUSE_SENSITIVITY);
        PlayerPrefs.DeleteKey(KEY_BRIGHTNESS);
        PlayerPrefs.DeleteKey(KEY_BRIGHTNESS_FIRST);
        PlayerPrefs.DeleteKey(KEY_MIC_VOLUME);
        PlayerPrefs.DeleteKey(KEY_MIC_INDEX);
        PlayerPrefs.DeleteKey(KEY_RESOLUTION_INDEX);
        PlayerPrefs.DeleteKey(KEY_RESOLUTION_WIDTH);
        PlayerPrefs.DeleteKey(KEY_RESOLUTION_HEIGHT);
        PlayerPrefs.DeleteKey(KEY_INVERT_Y);
        foreach (var floor in floorOrder)
            PlayerPrefs.DeleteKey("Unlocked_" + floor);
#endif
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
            ConfigureSettingPanelCanvas();
            settingPanel.transform.SetAsLastSibling();
            settingPanel.SetActive(true);
            KeepMenuPanelsAboveAllCanvases();
            StartMicPreview();
        }

    }

    public void ClosePanel()
    {
        if (settingPanel != null)
        {
            settingPanel.SetActive(false);
        }
        StopMicPreview();

        if (!IsPausePanelOpen)
            RestoreOverriddenCanvases();
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

    // ─────────────────────────────────────
    // 층 해금 관리
    // ─────────────────────────────────────

    private readonly string[] floorOrder = new string[]
    {
    "LobbyF",
    "B1F",
    "B2F",
    "B3F",
    "B4F",
    "B5F"
    };

    private void InitFloorUnlock()
    {
        if (PlayerPrefs.GetInt("Unlocked_LobbyF", 0) == 0)
        {
            PlayerPrefs.SetInt("Unlocked_LobbyF", 1);
            PlayerPrefs.Save();
        }
    }

    /* public void UnlockNextFloor(string currentFloorName)
    {
        int currentIndex = System.Array.IndexOf(floorOrder, currentFloorName);

        if (currentIndex == -1)
        {
            Debug.LogWarning($"[SettingManager] {currentFloorName} 을 floorOrder에서 찾을 수 없음");
            return;
        }

        int nextIndex = currentIndex + 1;

        if (nextIndex >= floorOrder.Length)
        {
            Debug.Log("[SettingManager] 모든 층 해금 완료!");
            return;
        }

        string nextKey = "Unlocked_" + floorOrder[nextIndex];
        PlayerPrefs.SetInt(nextKey, 1);
        PlayerPrefs.Save();
        Debug.Log($"[SettingManager] {floorOrder[nextIndex]} 해금!");
    } 

    public bool IsUnlocked(string floorName)
    {
        return PlayerPrefs.GetInt("Unlocked_" + floorName, 0) == 1;
    }
    public void ClearCurrentFloorAndReturn()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        UnlockNextFloor(currentScene);
        SceneManager.LoadScene("LobbyScene", LoadSceneMode.Single);
        Debug.Log($"SelectedFloor로 이동");

    } */

    //Pause Panel 관련
    public void BackMainScene()
    {
        SetPausePanelState(false);
        AudioManager.Instance?.PlaySFX("Button1");
        SceneManager.LoadScene("MainLobby", LoadSceneMode.Single);
    }

    private void ConfigurePausePanelCanvas()
    {
        if (PausePanel == null) return;

        EnsureMenuCanvasVisible();
        EnsureMenuOverlayCanvas();

        pauseCanvas = PausePanel.GetComponent<Canvas>();
        if (pauseCanvas == null)
            pauseCanvas = PausePanel.AddComponent<Canvas>();

        pauseCanvas.overrideSorting = true;
        pauseCanvas.sortingLayerID = GetTopSortingLayerId();
        pauseCanvas.sortingOrder = PAUSE_PANEL_SORTING_ORDER;

        if (PausePanel.GetComponent<GraphicRaycaster>() == null)
            PausePanel.AddComponent<GraphicRaycaster>();
    }

    private void ConfigureSettingPanelCanvas()
    {
        if (settingPanel == null) return;

        EnsureMenuCanvasVisible();
        EnsureMenuOverlayCanvas();

        settingPanelCanvas = settingPanel.GetComponent<Canvas>();
        if (settingPanelCanvas == null)
            settingPanelCanvas = settingPanel.AddComponent<Canvas>();

        settingPanelCanvas.overrideSorting = true;
        settingPanelCanvas.sortingLayerID = GetTopSortingLayerId();
        settingPanelCanvas.sortingOrder = SETTING_PANEL_SORTING_ORDER;

        if (settingPanel.GetComponent<GraphicRaycaster>() == null)
            settingPanel.AddComponent<GraphicRaycaster>();
    }

    private void EnsureMenuCanvasVisible()
    {
        if (settingCanvas == null) return;

        settingCanvas.enabled = true;
        settingCanvas.transform.localScale = Vector3.one;

        Camera uiCamera = settingCanvas.worldCamera;
        if (uiCamera != null)
            uiCamera.enabled = true;
    }

    private void EnsureMenuOverlayCanvas()
    {
        if (menuOverlayCanvas == null)
        {
            Transform existing = transform.Find("MenuOverlayCanvas");
            GameObject overlayObject;

            if (existing != null)
            {
                overlayObject = existing.gameObject;
                menuOverlayCanvas = overlayObject.GetComponent<Canvas>();
            }
            else
            {
                overlayObject = new GameObject(
                        "MenuOverlayCanvas",
                        typeof(RectTransform),
                        typeof(Canvas),
                        typeof(CanvasScaler),
                        typeof(GraphicRaycaster));
                overlayObject.transform.SetParent(transform, false);
                menuOverlayCanvas = overlayObject.GetComponent<Canvas>();
            }

            if (menuOverlayCanvas == null)
                menuOverlayCanvas = overlayObject.AddComponent<Canvas>();

            CanvasScaler overlayScaler = overlayObject.GetComponent<CanvasScaler>();
            CanvasScaler sourceScaler = settingCanvas != null
                    ? settingCanvas.GetComponent<CanvasScaler>()
                    : null;
            if (overlayScaler != null && sourceScaler != null)
            {
                overlayScaler.uiScaleMode = sourceScaler.uiScaleMode;
                overlayScaler.referenceResolution = sourceScaler.referenceResolution;
                overlayScaler.screenMatchMode = sourceScaler.screenMatchMode;
                overlayScaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
                overlayScaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
            }
            else
            {
                ResponsiveCanvasUtility.Configure(overlayScaler);
            }
        }

        menuOverlayCanvas.enabled = true;

        Camera uiCamera = settingCanvas != null ? settingCanvas.worldCamera : null;
        if (uiCamera == null)
            uiCamera = GetComponentInChildren<Camera>(true);

        if (uiCamera != null)
        {
            uiCamera.enabled = true;
            uiCamera.allowHDR = true;
            uiCamera.depth = Mathf.Max(uiCamera.depth, 100f);
            uiCamera.cullingMask |= 1 << 5; // UI layer

            ConfigureUICameraForGlobalVolume(uiCamera);
            menuOverlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            menuOverlayCanvas.worldCamera = uiCamera;
            menuOverlayCanvas.planeDistance = Mathf.Max(uiCamera.nearClipPlane + 0.1f, 1f);
        }
        else
        {
            // 카메라가 없는 예외 상황에서도 메뉴 자체는 보이도록 유지합니다.
            menuOverlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            menuOverlayCanvas.worldCamera = null;
        }

        menuOverlayCanvas.overrideSorting = true;
        menuOverlayCanvas.sortingLayerID = GetTopSortingLayerId();
        menuOverlayCanvas.sortingOrder = MENU_OVERLAY_SORTING_ORDER;
        menuOverlayCanvas.transform.localScale = Vector3.one;

        MovePanelToMenuOverlay(PausePanel);
        MovePanelToMenuOverlay(settingPanel);
    }

    private void MovePanelToMenuOverlay(GameObject panel)
    {
        if (panel == null || menuOverlayCanvas == null) return;
        if (panel.transform.parent == menuOverlayCanvas.transform) return;

        panel.transform.SetParent(menuOverlayCanvas.transform, false);
        panel.transform.localScale = Vector3.one;
    }

    private static int GetTopSortingLayerId()
    {
        SortingLayer[] layers = SortingLayer.layers;
        return layers.Length > 0 ? layers[layers.Length - 1].id : 0;
    }

    private void SetPausePanelState(bool isOpen)
    {
        if (PausePanel == null) return;

        if (isOpen)
        {
            ConfigurePausePanelCanvas();
            PausePanel.transform.SetAsLastSibling();
        }

        PausePanel.SetActive(isOpen);
        SetPlayerInputLock(isOpen);

        if (isOpen)
            KeepMenuPanelsAboveAllCanvases();
        else if (settingPanel == null || !settingPanel.activeInHierarchy)
            RestoreOverriddenCanvases();
    }

    private void KeepMenuPanelsAboveAllCanvases()
    {
        Camera uiCamera = settingCanvas != null ? settingCanvas.worldCamera : null;
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        int topLayerId = GetTopSortingLayerId();

        foreach (Canvas canvas in canvases)
        {
            if (canvas == null || canvas == settingCanvas || canvas == menuOverlayCanvas ||
                    canvas == pauseCanvas || canvas == settingPanelCanvas)
                continue;

            Transform canvasTransform = canvas.transform;
            if ((menuOverlayCanvas != null && canvasTransform.IsChildOf(menuOverlayCanvas.transform)) ||
                (PausePanel != null && canvasTransform.IsChildOf(PausePanel.transform)) ||
                (settingPanel != null && canvasTransform.IsChildOf(settingPanel.transform)))
                continue;

            if (!overriddenCanvases.ContainsKey(canvas))
            {
                overriddenCanvases.Add(canvas, new CanvasRenderState
                {
                    renderMode = canvas.renderMode,
                    worldCamera = canvas.worldCamera,
                    planeDistance = canvas.planeDistance,
                    overrideSorting = canvas.overrideSorting,
                    sortingLayerId = canvas.sortingLayerID,
                    sortingOrder = canvas.sortingOrder
                });
            }

            DisableCanvasRaycasters(canvas);

            if (canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace && uiCamera != null)
            {
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;

                canvas.worldCamera = uiCamera;
                canvas.planeDistance = settingCanvas.planeDistance;
            }

            canvas.overrideSorting = true;
            canvas.sortingLayerID = topLayerId;
            canvas.sortingOrder = Mathf.Min(canvas.sortingOrder, PAUSE_PANEL_SORTING_ORDER - 1);
        }

        ConfigurePausePanelCanvas();
        ConfigureSettingPanelCanvas();
        if (PausePanel != null && PausePanel.activeSelf)
            PausePanel.transform.SetAsLastSibling();
        if (settingPanel != null && settingPanel.activeInHierarchy)
            settingPanel.transform.SetAsLastSibling();
    }

    private void RestoreOverriddenCanvases()
    {
        foreach (KeyValuePair<Canvas, CanvasRenderState> entry in overriddenCanvases)
        {
            Canvas canvas = entry.Key;
            if (canvas == null) continue;

            CanvasRenderState state = entry.Value;
            canvas.renderMode = state.renderMode;
            canvas.worldCamera = state.worldCamera;
            canvas.planeDistance = state.planeDistance;
            canvas.overrideSorting = state.overrideSorting;
            canvas.sortingLayerID = state.sortingLayerId;
            canvas.sortingOrder = state.sortingOrder;
        }

        overriddenCanvases.Clear();

        foreach (KeyValuePair<GraphicRaycaster, bool> entry in overriddenRaycasters)
        {
            if (entry.Key != null)
                entry.Key.enabled = entry.Value;
        }

        overriddenRaycasters.Clear();
    }

    private void DisableCanvasRaycasters(Canvas canvas)
    {
        if (canvas == null) return;

        GraphicRaycaster[] raycasters = canvas.GetComponents<GraphicRaycaster>();
        foreach (GraphicRaycaster raycaster in raycasters)
        {
            if (raycaster == null) continue;

            if (!overriddenRaycasters.ContainsKey(raycaster))
                overriddenRaycasters.Add(raycaster, raycaster.enabled);

            raycaster.enabled = false;
        }
    }

    private void SetPlayerInputLock(bool isLocked)
    {
        bool shouldShowCursor = isLocked || IsCursorVisibleScene(SceneManager.GetActiveScene().name);
        Cursor.lockState = shouldShowCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = shouldShowCursor;
    }

    private bool IsCursorVisibleScene(string sceneName)
    {
        if (cursorVisibleSceneNames == null)
        {
            return false;
        }

        foreach (string cursorVisibleSceneName in cursorVisibleSceneNames)
        {
            if (string.Equals(cursorVisibleSceneName, sceneName, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public void ClosePausePanel()
    {
        if (PausePanel != null)
        {
            AudioManager.Instance?.PlaySFX("Button1");
            SetPausePanelState(false);
        }
    }
}
