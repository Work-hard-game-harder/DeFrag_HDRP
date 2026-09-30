using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class SharedHintFocusPresentation : MonoBehaviour
{
    [Header("Shared Hint")]
    [SerializeField] private HintConfirmationTracker hintTracker;
    [SerializeField] private string hintId = "EmployeeBadge";

    [Header("Local Camera Presentation")]
    [SerializeField] private Transform focusTarget;
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
    private bool originalBrainEnabled;
    private bool controlsLocked;
    private readonly Queue<bool> pendingPresentations = new();

    private void OnEnable()
    {
        if (hintTracker == null)
            hintTracker = HintConfirmationTracker.Instance;

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

    private void HandleHintWarningFocusRequested(bool showAssist)
    {
        if (presentationRoutine != null)
        {
            pendingPresentations.Enqueue(showAssist);
            return;
        }

        presentationRoutine = StartCoroutine(PresentationRoutine(showAssist));
    }

    private IEnumerator PresentationRoutine(bool showAssist)
    {
        if (focusTarget == null)
        {
            Debug.LogError(
                "[SharedHintFocusPresentation] Focus Target이 연결되지 않았습니다.",
                this);
            presentationRoutine = null;
            yield break;
        }

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
        Vector3 direction = focusTarget.position - playerCamera.transform.position;
        Quaternion focusRotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : originalCameraRotation;

        yield return RotateCamera(originalCameraRotation, focusRotation, focusDuration);

        bool subtitleFinished = false;
        if (showAssist && assistSubtitle != null)
        {
            assistSubtitle.PlaySubtitleFromInteract(() => subtitleFinished = true);
            while (!subtitleFinished)
                yield return null;
        }
        else if (fallbackFocusHoldDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(fallbackFocusHoldDuration);
        }

        yield return RotateCamera(
            playerCamera.transform.rotation,
            originalCameraRotation,
            returnDuration);

        if (showAssist)
            RequestQuestCompletion();

        RestoreLocalControls();
        presentationRoutine = null;

        if (pendingPresentations.Count > 0)
            presentationRoutine = StartCoroutine(
                PresentationRoutine(pendingPresentations.Dequeue()));
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

        pendingPresentations.Clear();
        RestoreLocalControls();
    }

    private void OnDestroy()
    {
        if (hintTracker != null)
            hintTracker.HintWarningFocusRequested -= HandleHintWarningFocusRequested;

        RestoreLocalControls();
    }
}
