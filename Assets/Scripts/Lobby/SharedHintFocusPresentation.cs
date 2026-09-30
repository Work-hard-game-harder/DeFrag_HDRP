using System.Collections;
using StarterAssets;
using DeFrag.Lobby;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class SharedHintFocusPresentation : MonoBehaviour
{
    [System.Serializable]
    private sealed class HintFocusTarget
    {
        public string hintId;
        public Transform target;
    }

    [Header("Shared Hint")]
    [SerializeField] private HintConfirmationTracker hintTracker;
    [SerializeField] private string hintId = "EmployeeBadge";
    [SerializeField] private LobbyPowerController powerController;

    [Header("Local Camera Presentation")]
    [SerializeField] private Transform focusTarget;
    [Tooltip("힌트 ID별 포커스 대상입니다. 일치 항목이 없으면 Focus Target을 사용합니다.")]
    [SerializeField] private HintFocusTarget[] focusTargetsByHint;
    [SerializeField] private float lookStartDelay = 0.12f;
    [SerializeField] private float overshootDegrees = 1.5f;
    [SerializeField] private float settleDuration = 0.22f;
    [SerializeField] private float microSwayAmount = 0.15f;
    [SerializeField] private float microSwaySpeed = 1.2f;
    [Min(0f)] [SerializeField] private float focusDuration = 0.8f;
    [Min(0f)] [SerializeField] private float returnDuration = 0.8f;
    [Min(0f)] [SerializeField] private float fallbackFocusHoldDuration = 2f;
    [SerializeField] private AnimationCurve blendCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Assist Subtitle")]
    [SerializeField] private SubtitleTrigger assistSubtitle;

    private Coroutine presentationRoutine;
    private PersonController movement;
    private PlayerInteraction playerInteraction;
    private CameraViewSwitcher cameraViewSwitcher;
    private PlayerInput playerInput;
    private StarterAssetsInputs inputs;
    private Camera playerCamera;
    private CinemachineBrain cinemachineBrain;
    private Quaternion originalCameraRotation;
    private bool originalPlayerInputEnabled;
    private bool originalMovementEnabled;
    private bool originalBrainEnabled;
    private bool controlsLocked;
    private bool assistStartPending;
    private bool storyAssistStarted;
    private Transform activeFocusTarget;

    private void OnEnable()
    {
        if (hintTracker == null)
            hintTracker = HintConfirmationTracker.Instance;
        if (powerController == null)
            powerController = GetComponent<LobbyPowerController>();

        if (hintTracker != null)
        {
            hintTracker.HintWarningFocusRequested -= HandleHintWarningFocusRequested;
            hintTracker.HintWarningFocusRequested += HandleHintWarningFocusRequested;
        }
        else
            Debug.LogError("[SharedHintFocusPresentation] Hint Tracker가 연결되지 않았습니다.", this);
    }

    public void RequestStart()
    {
        if (string.IsNullOrWhiteSpace(hintId))
            return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            NetworkObject localPlayer = networkManager.LocalClient?.PlayerObject;
            PersonController relay = localPlayer != null
                ? localPlayer.GetComponent<PersonController>()
                : null;
            if (relay == null)
            {
                Debug.LogError(
                    "[SharedHintFocusPresentation] 로컬 네트워크 플레이어를 찾지 못했습니다.",
                    this);
                return;
            }

            relay.RequestLobbyHintPresentation(hintId);
            return;
        }

        HintConfirmationTracker tracker = hintTracker != null
            ? hintTracker
            : HintConfirmationTracker.Instance;
        if (tracker != null &&
            tracker.TryStartHintPresentationOnServer(hintId, out bool emergency))
        {
            tracker.ApplySharedHintPresentationStart(hintId, emergency);
        }
    }

    private void HandleHintWarningFocusRequested(string warningHintId, bool showAssist)
    {
        activeFocusTarget = ResolveFocusTarget(warningHintId);
        assistStartPending |= showAssist;

        // A new warning refreshes the current focus immediately. It must not
        // wait behind a long Assist subtitle while the actual lights flicker.
        if (presentationRoutine != null)
            StopCoroutine(presentationRoutine);

        presentationRoutine = StartCoroutine(PresentationRoutine());
    }

    private IEnumerator PresentationRoutine()
    {
        Transform target = activeFocusTarget != null
            ? activeFocusTarget
            : focusTarget;
        if (target == null)
        {
            Debug.LogError(
                "[SharedHintFocusPresentation] Focus Target이 연결되지 않았습니다.",
                this);
            presentationRoutine = null;
            yield break;
        }

        if (!controlsLocked)
        {
            while (!GameplayInputGate.TryAcquire(this))
                yield return null;

            if (!TryResolveLocalPlayer())
            {
                Debug.LogError(
                    "[SharedHintFocusPresentation] 로컬 플레이어 카메라를 찾지 못했습니다.",
                    this);
                GameplayInputGate.Release(this);
                presentationRoutine = null;
                yield break;
            }

            LockLocalControls();
            originalCameraRotation = playerCamera.transform.rotation;

            // Let Cinemachine and the player controller finish their current
            // frame before this component takes ownership of the camera pose.
            if (lookStartDelay > 0f)
                yield return new WaitForSecondsRealtime(lookStartDelay);
            else
                yield return null;
        }

        Vector3 direction = target.position - playerCamera.transform.position;
        Quaternion focusRotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : originalCameraRotation;

        yield return RotateCamera(
            playerCamera.transform.rotation,
            focusRotation,
            focusDuration);

        TryStartStoryAssist();

        float holdDuration = powerController != null
            ? Mathf.Max(
                fallbackFocusHoldDuration,
                powerController.WarningDuration - focusDuration)
            : fallbackFocusHoldDuration;
        if (holdDuration > 0f)
            yield return new WaitForSecondsRealtime(holdDuration);

        yield return RotateCamera(
            playerCamera.transform.rotation,
            originalCameraRotation,
            returnDuration);

        RestoreLocalControls();
        presentationRoutine = null;
    }

    private Transform ResolveFocusTarget(string warningHintId)
    {
        if (!string.IsNullOrWhiteSpace(warningHintId) &&
            focusTargetsByHint != null)
        {
            foreach (HintFocusTarget binding in focusTargetsByHint)
            {
                if (binding != null &&
                    binding.target != null &&
                    string.Equals(
                        binding.hintId?.Trim(),
                        warningHintId.Trim(),
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return binding.target;
                }
            }
        }

        return focusTarget;
    }

    private void TryStartStoryAssist()
    {
        if (!assistStartPending || storyAssistStarted)
            return;

        assistStartPending = false;
        storyAssistStarted = true;
        if (assistSubtitle != null)
            assistSubtitle.PlaySubtitleFromInteract(RequestQuestCompletion);
        else
            RequestQuestCompletion();
    }

    private void RequestQuestCompletion()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            NetworkObject localPlayer = networkManager.LocalClient?.PlayerObject;
            PersonController relay = localPlayer != null
                ? localPlayer.GetComponent<PersonController>()
                : null;
            relay?.RequestLobbyHintPresentationCompletion(hintId);
            return;
        }

        HintConfirmationTracker tracker = hintTracker != null
            ? hintTracker
            : HintConfirmationTracker.Instance;
        tracker?.TryCompleteHintPresentationQuestOnServer(hintId);
    }

    private bool TryResolveLocalPlayer()
    {
        GameObject playerRoot = null;
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
            playerRoot = networkManager.LocalClient?.PlayerObject?.gameObject;
        else
            playerRoot = FindAnyObjectByType<PersonController>()?.gameObject;

        if (playerRoot == null)
            return false;

        movement = playerRoot.GetComponent<PersonController>();
        playerInteraction = playerRoot.GetComponentInChildren<PlayerInteraction>(true);
        cameraViewSwitcher = playerRoot.GetComponentInChildren<CameraViewSwitcher>(true);
        playerInput = playerRoot.GetComponent<PlayerInput>();
        inputs = playerRoot.GetComponent<StarterAssetsInputs>();

        cameraViewSwitcher?.SetInteractionLocked(true);
        playerCamera = cameraViewSwitcher != null
            ? cameraViewSwitcher.ActiveCamera
            : playerRoot.GetComponentInChildren<Camera>(true);

        bool resolved = movement != null && playerCamera != null;
        if (!resolved)
            cameraViewSwitcher?.SetInteractionLocked(false);
        return resolved;
    }

    private void LockLocalControls()
    {
        originalPlayerInputEnabled = playerInput != null && playerInput.enabled;
        if (playerInput != null)
            playerInput.enabled = false;

        originalMovementEnabled = movement != null && movement.enabled;
        if (movement != null)
            movement.enabled = false;

        if (inputs != null)
        {
            inputs.MoveInput(Vector2.zero);
            inputs.LookInput(Vector2.zero);
            inputs.JumpInput(false);
            inputs.SprintInput(false);
        }

        playerInteraction?.CloseAllUI();
        playerInteraction?.TogglePlayerControl(false);

        cinemachineBrain = playerCamera.GetComponent<CinemachineBrain>();
        originalBrainEnabled = cinemachineBrain != null && cinemachineBrain.enabled;
        if (cinemachineBrain != null)
            cinemachineBrain.enabled = false;

        controlsLocked = true;
    }

    private IEnumerator RotateCamera(
        Quaternion startRotation,
        Quaternion endRotation,
        float duration)
    {
        if (duration <= 0f)
        {
            playerCamera.transform.rotation = endRotation;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float weight = blendCurve != null
                ? blendCurve.Evaluate(normalized)
                : normalized;
            playerCamera.transform.rotation = Quaternion.SlerpUnclamped(
                startRotation,
                endRotation,
                weight);
            yield return null;
        }

        playerCamera.transform.rotation = endRotation;
    }

    private void RestoreLocalControls()
    {
        if (!controlsLocked)
            return;

        if (playerCamera != null)
            playerCamera.transform.rotation = originalCameraRotation;
        if (cinemachineBrain != null)
            cinemachineBrain.enabled = originalBrainEnabled;
        if (playerInput != null)
            playerInput.enabled = originalPlayerInputEnabled;
        if (movement != null)
            movement.enabled = originalMovementEnabled;

        cameraViewSwitcher?.SetInteractionLocked(false);
        playerInteraction?.TogglePlayerControl(true);
        GameplayInputGate.Release(this);
        controlsLocked = false;
    }

    private void OnDisable()
    {
        if (hintTracker != null)
            hintTracker.HintWarningFocusRequested -= HandleHintWarningFocusRequested;

        if (presentationRoutine != null)
        {
            StopCoroutine(presentationRoutine);
            presentationRoutine = null;
        }

        RestoreLocalControls();
    }

    private void OnDestroy()
    {
        if (hintTracker != null)
            hintTracker.HintWarningFocusRequested -= HandleHintWarningFocusRequested;

        RestoreLocalControls();
    }
}
