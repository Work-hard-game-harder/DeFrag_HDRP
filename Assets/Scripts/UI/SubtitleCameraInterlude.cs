using System;
using System.Collections;
using StarterAssets;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 자막의 지정 Element 뒤에서 소유 플레이어의 카메라만 대상 쪽으로 회전/확대하고
/// 원래 시야로 복귀시킨 뒤 자막 재생을 계속합니다. 네트워크 게임 상태는 변경하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class SubtitleCameraInterlude : MonoBehaviour
{
    [Header("Subtitle Break")]
    [Min(0)] [SerializeField] private int pauseAfterSubtitleIndex = 2;

    [Header("Focus Target")]
    [SerializeField] private Transform focusTarget;
    [Tooltip("Focus Target의 로컬 좌표 기준으로 바라볼 지점입니다.")]
    [SerializeField] private Vector3 focusLocalOffset = new(0f, 1.1f, 0f);

    [Header("Camera Motion")]
    [Min(0f)] [SerializeField] private float focusDuration = 0.8f;
    [Min(0f)] [SerializeField] private float focusHoldDuration = 1f;
    [Min(0f)] [SerializeField] private float returnDuration = 0.8f;
    [Range(1f, 179f)] [SerializeField] private float focusFieldOfView = 44f;
    [SerializeField] private AnimationCurve blendCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Coroutine routine;
    private Action resumeSubtitle;
    private PersonController movement;
    private PlayerInteraction interaction;
    private CameraViewSwitcher viewSwitcher;
    private PlayerInput playerInput;
    private StarterAssetsInputs inputs;
    private Camera playerCamera;
    private CinemachineBrain cinemachineBrain;
    private Quaternion originalCameraRotation;
    private float originalFieldOfView;
    private bool originalMovementEnabled;
    private bool originalPlayerInputEnabled;
    private bool originalBrainEnabled;
    private bool controlsLocked;

    public int PauseAfterSubtitleIndex => pauseAfterSubtitleIndex;

    public void Play(Action resume)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            RestoreAndResume();
        }

        resumeSubtitle = resume;
        routine = StartCoroutine(PresentationRoutine());
    }

    private IEnumerator PresentationRoutine()
    {
        if (focusTarget == null)
        {
            Debug.LogError("[Subtitle Camera Interlude] Focus Target이 연결되지 않았습니다.", this);
            RestoreAndResume();
            yield break;
        }

        while (!GameplayInputGate.TryAcquire(this))
            yield return null;

        if (!TryResolveLocalPlayer())
        {
            Debug.LogError("[Subtitle Camera Interlude] 로컬 플레이어 카메라를 찾지 못했습니다.", this);
            GameplayInputGate.Release(this);
            RestoreAndResume();
            yield break;
        }

        LockLocalControls();
        originalCameraRotation = playerCamera.transform.rotation;
        originalFieldOfView = playerCamera.fieldOfView;

        Vector3 focusPosition = focusTarget.TransformPoint(focusLocalOffset);
        Vector3 direction = focusPosition - playerCamera.transform.position;
        Quaternion focusRotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : originalCameraRotation;

        yield return BlendCamera(
            originalCameraRotation,
            focusRotation,
            originalFieldOfView,
            focusFieldOfView,
            focusDuration);

        if (focusHoldDuration > 0f)
            yield return new WaitForSecondsRealtime(focusHoldDuration);

        yield return BlendCamera(
            playerCamera.transform.rotation,
            originalCameraRotation,
            playerCamera.fieldOfView,
            originalFieldOfView,
            returnDuration);

        RestoreAndResume();
    }

    private bool TryResolveLocalPlayer()
    {
        GameObject playerRoot = null;
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
            playerRoot = manager.LocalClient?.PlayerObject?.gameObject;
        else
            playerRoot = FindAnyObjectByType<PersonController>()?.gameObject;

        if (playerRoot == null)
            return false;

        movement = playerRoot.GetComponent<PersonController>();
        interaction = playerRoot.GetComponentInChildren<PlayerInteraction>(true);
        viewSwitcher = playerRoot.GetComponentInChildren<CameraViewSwitcher>(true);
        playerInput = playerRoot.GetComponent<PlayerInput>();
        inputs = playerRoot.GetComponent<StarterAssetsInputs>();

        viewSwitcher?.SetInteractionLocked(true);
        playerCamera = viewSwitcher != null
            ? viewSwitcher.ActiveCamera
            : playerRoot.GetComponentInChildren<Camera>(true);

        if (playerCamera != null)
            return true;

        viewSwitcher?.SetInteractionLocked(false);
        return false;
    }

    private void LockLocalControls()
    {
        originalMovementEnabled = movement != null && movement.enabled;
        originalPlayerInputEnabled = playerInput != null && playerInput.enabled;

        if (movement != null)
            movement.enabled = false;
        if (playerInput != null)
            playerInput.enabled = false;

        if (inputs != null)
        {
            inputs.MoveInput(Vector2.zero);
            inputs.LookInput(Vector2.zero);
            inputs.JumpInput(false);
            inputs.SprintInput(false);
        }

        interaction?.CloseAllUI();
        interaction?.TogglePlayerControl(false);

        cinemachineBrain = playerCamera.GetComponent<CinemachineBrain>();
        originalBrainEnabled = cinemachineBrain != null && cinemachineBrain.enabled;
        if (cinemachineBrain != null)
            cinemachineBrain.enabled = false;

        controlsLocked = true;
    }

    private IEnumerator BlendCamera(
        Quaternion fromRotation,
        Quaternion toRotation,
        float fromFieldOfView,
        float toFieldOfView,
        float duration)
    {
        if (duration <= 0f)
        {
            playerCamera.transform.rotation = toRotation;
            playerCamera.fieldOfView = toFieldOfView;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration && playerCamera != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float weight = blendCurve != null
                ? blendCurve.Evaluate(normalized)
                : normalized;

            playerCamera.transform.rotation = Quaternion.SlerpUnclamped(
                fromRotation,
                toRotation,
                weight);
            playerCamera.fieldOfView = Mathf.LerpUnclamped(
                fromFieldOfView,
                toFieldOfView,
                weight);
            yield return null;
        }

        if (playerCamera != null)
        {
            playerCamera.transform.rotation = toRotation;
            playerCamera.fieldOfView = toFieldOfView;
        }
    }

    private void RestoreAndResume()
    {
        routine = null;

        if (controlsLocked)
        {
            if (playerCamera != null)
            {
                playerCamera.transform.rotation = originalCameraRotation;
                playerCamera.fieldOfView = originalFieldOfView;
            }

            if (cinemachineBrain != null)
                cinemachineBrain.enabled = originalBrainEnabled;
            if (playerInput != null)
                playerInput.enabled = originalPlayerInputEnabled;
            if (movement != null)
                movement.enabled = originalMovementEnabled;

            viewSwitcher?.SetInteractionLocked(false);
            interaction?.TogglePlayerControl(true);
            GameplayInputGate.Release(this);
            controlsLocked = false;
        }

        Action resume = resumeSubtitle;
        resumeSubtitle = null;
        resume?.Invoke();
    }

    private void OnDisable()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        RestoreAndResume();
    }

    private void OnValidate()
    {
        pauseAfterSubtitleIndex = Mathf.Max(0, pauseAfterSubtitleIndex);
        focusFieldOfView = Mathf.Clamp(focusFieldOfView, 1f, 179f);
    }
}
