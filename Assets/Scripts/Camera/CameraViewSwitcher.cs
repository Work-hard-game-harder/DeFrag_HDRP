using System;
using System.Collections;
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
    [SerializeField, Range(1f, 179f)] private float itemCameraFieldOfView = 67f;

    [Header("Viewfinder")]
    [Tooltip("기존 'focus image' / 'Battery UI' 오버레이를 숨기고 새 뷰파인더 HUD를 사용합니다.")]
    [SerializeField] private bool useViewfinderHud = true;

    [Header("Raise Transition (C)")]
    [SerializeField, Min(0.05f)] private float raiseDuration = 0.36f;
    [SerializeField, Min(0.05f)] private float pushDuration = 0.24f;
    [SerializeField, Min(0.05f)] private float pullDuration = 0.18f;
    [SerializeField, Min(0.05f)] private float lowerDuration = 0.3f;
    [Tooltip("카메라를 들어 LCD를 볼 때 LCD 중심이 오는 위치 (플레이어 카메라 로컬).")]
    [SerializeField] private Vector3 presentLcdPoint = new(0f, -0.025f, 0.24f);
    [Tooltip("LCD 중심 / 크기 (Camera(Item) 메시 로컬 단위, LCD는 -X를 향함).")]
    [SerializeField] private Vector3 lcdCenter = new(-0.0772f, -0.0548f, -0.011f);
    [SerializeField] private Vector2 lcdSize = new(0.215f, 0.154f);
    [SerializeField, Min(0f)] private float lcdGlow = 1.3f;

    private enum RaiseState { Lowered, Raising, Raised, Lowering }

    private bool localPresentationEnabled = true;
    private bool interactionLocked;
    private RaiseState raiseState = RaiseState.Lowered;
    private bool wantView;
    private Coroutine transition;
    private EquipmentController equipment;
    private GameObject lcdHost;
    private MeshRenderer lcd;
    private RenderTexture lcdTexture;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private CameraViewfinderHud hud;
    private AudioSource sfx;
    private AudioClip raiseClip, powerOnClip, lowerClip;

    public bool IsCameraEquipped { get; private set; }
    public bool IsCameraViewActive { get; private set; }
    public bool IsTransitioning => raiseState is RaiseState.Raising or RaiseState.Lowering;
    public event Action<bool> CameraViewActiveChanged;
    public Camera ActiveCamera =>
        IsCameraViewActive && itemCamera != null ? itemCamera : playerCamera;

    private void Awake()
    {
        equipment = GetComponent<EquipmentController>();
        SetCameraViewActive(false);
    }

    private void Start()
    {
        EnsureViewfinder();
        raiseClip = Resources.Load<AudioClip>("DeviceSfx/Camera_Raise");
        powerOnClip = Resources.Load<AudioClip>("DeviceSfx/Camera_PowerOn");
        lowerClip = Resources.Load<AudioClip>("DeviceSfx/Camera_Lower");
    }

    private void OnEnable()
    {
        if (cameraItem != null)
            cameraItem.ViewActiveChanged += OnItemViewRequested;
    }

    private void OnDisable()
    {
        if (cameraItem != null)
            cameraItem.ViewActiveChanged -= OnItemViewRequested;

        CancelToLowered();
    }

    private void OnDestroy()
    {
        if (lcdTexture != null) { lcdTexture.Release(); Destroy(lcdTexture); }
    }

    private void LateUpdate()
    {
        if (IsCameraViewActive || IsTransitioning)
            SynchronizeItemCameraPose();
    }

    public void SetCameraEquipped(bool equipped)
    {
        IsCameraEquipped = equipped;

        if (cameraItem != null)
            cameraItem.SetEquipped(equipped);

        if (!equipped)
            CancelToLowered();
    }

    public void SetInteractionLocked(bool locked)
    {
        interactionLocked = locked;

        if (cameraItem != null)
            cameraItem.enabled = localPresentationEnabled && !locked;

        if (locked)
            CancelToLowered();
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
            cameraItem.enabled = enabled && !interactionLocked;

        CancelToLowered();
    }

    // ───────────────────────────── Raise / lower ─────────────────────────────

    private void OnItemViewRequested(bool active)
    {
        wantView = active && IsCameraEquipped && !interactionLocked && localPresentationEnabled;
        if (IsTransitioning) return; // the running transition picks up the new wish when it ends

        GameObject held = equipment != null ? equipment.HeldVisual : null;
        if (held == null)
        {
            // No first-person model to animate: switch immediately.
            raiseState = wantView ? RaiseState.Raised : RaiseState.Lowered;
            SetCameraViewActive(wantView);
            if (wantView) hud?.Show(true); else hud?.Hide();
            return;
        }

        if (wantView && raiseState == RaiseState.Lowered)
            transition = StartCoroutine(Raise(held));
        else if (!wantView && raiseState == RaiseState.Raised)
            transition = StartCoroutine(Lower(held));
    }

    private IEnumerator Raise(GameObject held)
    {
        raiseState = RaiseState.Raising;
        Transform heldTransform = held.transform;
        restPosition = heldTransform.localPosition;
        restRotation = heldTransform.localRotation;
        PresentPose(heldTransform, presentLcdPoint, out Vector3 presentPosition, out Quaternion presentRotation);
        PresentPose(heldTransform, FillPoint(heldTransform), out Vector3 fillPosition, out _);
        ShowLcd(held, true);
        Play(raiseClip, 0.9f);

        for (float t = 0f; t < 1f; t += Time.deltaTime / raiseDuration)
        {
            float e = EaseInOut(t);
            heldTransform.localPosition = Vector3.Lerp(restPosition, presentPosition, e) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * 0.04f);
            heldTransform.localRotation = Quaternion.Slerp(restRotation, presentRotation, e);
            yield return null;
        }

        Play(powerOnClip, 0.8f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / pushDuration)
        {
            float e = t * t * t; // accelerate into the screen
            heldTransform.localPosition = Vector3.Lerp(presentPosition, fillPosition, e);
            heldTransform.localRotation = presentRotation;
            yield return null;
        }

        // The LCD now fills the view: cut to the real item camera and boot the viewfinder.
        ShowLcd(held, false);
        heldTransform.localPosition = restPosition;
        heldTransform.localRotation = restRotation;
        held.SetActive(false);
        raiseState = RaiseState.Raised;
        transition = null;
        SetCameraViewActive(true);
        hud?.Show(true);

        if (!wantView) OnItemViewRequested(false);
    }

    private IEnumerator Lower(GameObject held)
    {
        raiseState = RaiseState.Lowering;
        hud?.Hide();
        SetCameraViewActive(false);
        Transform heldTransform = held.transform;
        held.SetActive(true);
        restPosition = heldTransform.localPosition;
        restRotation = heldTransform.localRotation;
        PresentPose(heldTransform, presentLcdPoint, out Vector3 presentPosition, out Quaternion presentRotation);
        PresentPose(heldTransform, FillPoint(heldTransform), out Vector3 fillPosition, out _);
        ShowLcd(held, true);
        Play(lowerClip, 0.8f);

        for (float t = 0f; t < 1f; t += Time.deltaTime / pullDuration)
        {
            float e = 1f - (1f - t) * (1f - t);
            heldTransform.localPosition = Vector3.Lerp(fillPosition, presentPosition, e);
            heldTransform.localRotation = presentRotation;
            yield return null;
        }
        for (float t = 0f; t < 1f; t += Time.deltaTime / lowerDuration)
        {
            float e = EaseInOut(t);
            heldTransform.localPosition = Vector3.Lerp(presentPosition, restPosition, e);
            heldTransform.localRotation = Quaternion.Slerp(presentRotation, restRotation, e);
            yield return null;
        }

        heldTransform.localPosition = restPosition;
        heldTransform.localRotation = restRotation;
        ShowLcd(held, false);
        raiseState = RaiseState.Lowered;
        transition = null;

        if (wantView) OnItemViewRequested(true);
    }

    /// <summary>Immediately returns to the lowered state (locks, unequip, cutscenes, disable).</summary>
    private void CancelToLowered()
    {
        wantView = false;
        if (transition != null)
        {
            StopCoroutine(transition);
            transition = null;
        }

        GameObject held = equipment != null ? equipment.HeldVisual : null;
        if (IsTransitioning && held != null)
        {
            held.transform.localPosition = restPosition;
            held.transform.localRotation = restRotation;
        }
        if (held != null) ShowLcd(held, false);
        raiseState = RaiseState.Lowered;
        hud?.Hide();
        SetCameraViewActive(false);
    }

    /// <summary>Held-visual local pose that puts the LCD at <paramref name="cameraLocalPoint"/>, facing the eye.</summary>
    private void PresentPose(Transform held, Vector3 cameraLocalPoint, out Vector3 localPosition, out Quaternion localRotation)
    {
        Transform parent = held.parent;
        Transform eye = playerCamera != null ? playerCamera.transform : transform;
        // Mesh axes → camera axes: lens (+X) forward, LCD (-X) toward the eye, mesh +Z up.
        Quaternion worldRotation = eye.rotation * Quaternion.LookRotation(Vector3.up, Vector3.right);
        Vector3 scale = held.lossyScale;
        Vector3 lcdWorldOffset = worldRotation * Vector3.Scale(lcdCenter, scale);
        Vector3 worldPosition = eye.TransformPoint(cameraLocalPoint) - lcdWorldOffset;
        localPosition = parent != null ? parent.InverseTransformPoint(worldPosition) : worldPosition;
        localRotation = parent != null ? Quaternion.Inverse(parent.rotation) * worldRotation : worldRotation;
    }

    /// <summary>Distance at which the LCD covers the whole viewport.</summary>
    private Vector3 FillPoint(Transform held)
    {
        float scale = Mathf.Abs(held.lossyScale.x);
        float halfWidth = lcdSize.x * scale * 0.5f, halfHeight = lcdSize.y * scale * 0.5f;
        float vertical = (playerCamera != null ? playerCamera.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad;
        float aspect = playerCamera != null ? playerCamera.aspect : 16f / 9f;
        float horizontal = Mathf.Atan(Mathf.Tan(vertical) * aspect);
        float distance = Mathf.Min(halfHeight / Mathf.Tan(vertical), halfWidth / Mathf.Tan(horizontal)) * 0.78f;
        float near = playerCamera != null ? playerCamera.nearClipPlane : 0.01f;
        return new Vector3(0f, 0f, Mathf.Max(distance, near * 2f));
    }

    private void ShowLcd(GameObject held, bool on)
    {
        if (on)
        {
            if (lcdTexture == null)
            {
                lcdTexture = new RenderTexture(640, 458, 24, RenderTextureFormat.ARGB32) { name = "NVCam LCD" };
                lcdTexture.Create();
            }
            if (lcd == null || lcdHost != held)
            {
                if (lcd != null) Destroy(lcd.gameObject);
                lcdHost = held;
                lcd = DeviceScreenQuad.Create(held.transform, "NVCam LCD", lcdCenter + Vector3.left * 0.0006f,
                    Vector3.left, Vector3.forward, lcdSize, lcdTexture);
                DeviceScreenQuad.SetGlow(lcd.sharedMaterial, Color.white, lcdGlow);
            }
            lcd.enabled = true;
            if (itemCamera != null)
            {
                SynchronizeItemCameraPose();
                itemCamera.targetTexture = lcdTexture;
                itemCamera.enabled = localPresentationEnabled;
            }
        }
        else
        {
            if (lcd != null) lcd.enabled = false;
            if (itemCamera != null)
            {
                itemCamera.targetTexture = null;
                itemCamera.enabled = localPresentationEnabled && IsCameraViewActive;
            }
        }
    }

    private void EnsureViewfinder()
    {
        if (!useViewfinderHud || cameraOverlayCanvas == null || hud != null) return;
        foreach (Transform child in cameraOverlayCanvas.transform)
            if (child.name == "focus image" || child.name == "Battery UI")
                child.gameObject.SetActive(false);
        hud = gameObject.AddComponent<CameraViewfinderHud>();
        hud.Initialize(cameraItem, cameraOverlayCanvas.sortingOrder - 1);
        if (IsCameraViewActive) hud.Show(false);
    }

    private void Play(AudioClip clip, float volume)
    {
        if (clip == null) return;
        if (sfx == null)
        {
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
        }
        sfx.PlayOneShot(clip, volume);
    }

    private static float EaseInOut(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // ───────────────────────────── View switch ─────────────────────────────

    private void SetCameraViewActive(bool active)
    {
        // 카메라 아이템을 장착하지 않았다면 ItemCam을 켤 수 없다.
        active &= IsCameraEquipped && !interactionLocked;

        bool changed = IsCameraViewActive != active;
        IsCameraViewActive = active;

        if (active)
            SynchronizeItemCameraPose();

        if (playerCamera != null)
            playerCamera.enabled = localPresentationEnabled && !active;

        if (playerAudioListener != null)
            playerAudioListener.enabled = localPresentationEnabled && !active;

        if (itemCamera != null)
        {
            if (active) itemCamera.targetTexture = null;
            itemCamera.enabled = localPresentationEnabled && active;
        }

        if (itemAudioListener != null)
            itemAudioListener.enabled = localPresentationEnabled && active;

        if (cameraOverlayCanvas != null)
            cameraOverlayCanvas.enabled = localPresentationEnabled && active;

        if (changed)
            CameraViewActiveChanged?.Invoke(active);
    }

    private void SynchronizeItemCameraPose()
    {
        if (playerCamera == null || itemCamera == null)
            return;

        Transform source = playerCamera.transform;
        Transform target = itemCamera.transform;

        target.SetPositionAndRotation(source.position, source.rotation);
        int mask = playerCamera.cullingMask;
        // While the LCD shows the feed, the held camera model sits right in front of the lens.
        if (IsTransitioning && lcdHost != null)
            mask &= ~(1 << lcdHost.layer);
        itemCamera.cullingMask = mask;
        itemCamera.nearClipPlane = playerCamera.nearClipPlane;
        itemCamera.farClipPlane = playerCamera.farClipPlane;
        itemCamera.fieldOfView = itemCameraFieldOfView;
    }
}
