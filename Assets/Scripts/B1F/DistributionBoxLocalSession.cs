using System.Collections;
using EasyPeasyFirstPersonController;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DeFrag.B1F
{
    public sealed class DistributionBoxLocalSession : MonoBehaviour
    {
        public static DistributionBoxLocalSession Active { get; private set; }

        private DistributionBoxController controller;
        private PlayerInteraction playerInteraction;
        private Camera playerCamera;
        private Camera interactionCamera;
        private StarterAssets.PersonController movement;
        private CameraViewSwitcher viewSwitcher;
        private AudioListener interactionAudioListener;
        private bool originalPlayerCameraEnabled;
        private bool originalInteractionObjectActive;
        private bool originalInteractionCameraEnabled;
        private float originalInteractionCameraFieldOfView;
        private bool originalInteractionAudioListenerEnabled;
        private float interactionDistance;
        private bool waitingForInteractionKeyRelease;
        private bool cameraTransitionComplete;
        private bool active;
        private Coroutine cameraBlendRoutine;
        private Vector3 interactionCameraRestPosition;
        private Quaternion interactionCameraRestRotation;
        private float cameraLookSensitivity;
        private float cameraYawLimit;
        private float cameraUpPitchLimit;
        private float cameraDownPitchLimit;
        private float lookYaw;
        private float lookPitch;

        public bool IsFor(DistributionBoxController box) => active && controller == box;

        public void Begin(
            DistributionBoxController box,
            PlayerInteraction player,
            Camera localPlayerCamera,
            Camera boxCamera,
            Camera initialPreset,
            float initialFieldOfView,
            float rayDistance,
            float blendDuration,
            AnimationCurve blendCurve,
            float lookSensitivity,
            float yawLimit,
            float upPitchLimit,
            float downPitchLimit)
        {
            if (active || box == null || player == null ||
                localPlayerCamera == null || boxCamera == null)
                return;
            if (!GameplayInputGate.TryAcquire(this))
            {
                Debug.LogWarning("[DistributionBox] Another local modal interaction is blocking entry.", this);
                return;
            }

            controller = box;
            playerInteraction = player;
            playerCamera = localPlayerCamera;
            interactionCamera = boxCamera;
            interactionDistance = rayDistance;
            cameraLookSensitivity = lookSensitivity;
            cameraYawLimit = yawLimit;
            cameraUpPitchLimit = upPitchLimit;
            cameraDownPitchLimit = downPitchLimit;
            movement = player.GetComponentInParent<StarterAssets.PersonController>(true);
            viewSwitcher = player.GetComponentInParent<CameraViewSwitcher>(true);
            interactionAudioListener = interactionCamera.GetComponent<AudioListener>();

            originalPlayerCameraEnabled = playerCamera.enabled;
            originalInteractionObjectActive = interactionCamera.gameObject.activeSelf;
            originalInteractionCameraEnabled = interactionCamera.enabled;
            originalInteractionCameraFieldOfView = interactionCamera.fieldOfView;
            originalInteractionAudioListenerEnabled =
                interactionAudioListener != null && interactionAudioListener.enabled;
            Camera entryPreset = initialPreset != null ? initialPreset : interactionCamera;
            interactionCameraRestPosition = entryPreset.transform.position;
            interactionCameraRestRotation = entryPreset.transform.rotation;
            CopyLocalCameraRenderingSettings(playerCamera, interactionCamera);
            interactionCamera.fieldOfView = playerCamera.fieldOfView;
            lookYaw = 0f;
            lookPitch = 0f;
            cameraTransitionComplete = false;

            waitingForInteractionKeyRelease = true;
            active = true;
            Active = this;

            player.CloseAllUI();
            player.TogglePlayerControl(false);
            if (movement != null) movement.enabled = false;
            viewSwitcher?.SetInteractionLocked(true);

            interactionCamera.gameObject.SetActive(true);
            if (interactionAudioListener != null) interactionAudioListener.enabled = false;
            interactionCamera.transform.SetPositionAndRotation(
                playerCamera.transform.position,
                playerCamera.transform.rotation);
            interactionCamera.enabled = true;
            playerCamera.enabled = false;
            cameraBlendRoutine = StartCoroutine(BlendCameraToBox(
                initialFieldOfView,
                blendDuration,
                blendCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f)));
        }

        private static void CopyLocalCameraRenderingSettings(Camera source, Camera target)
        {
            if (source == null || target == null) return;

            target.allowHDR = source.allowHDR;
            target.allowMSAA = source.allowMSAA;

            HDAdditionalCameraData sourceData = source.GetComponent<HDAdditionalCameraData>();
            HDAdditionalCameraData targetData = target.GetComponent<HDAdditionalCameraData>();
            if (sourceData == null || targetData == null) return;

            // The local player's runtime glitch is a Volume effect. The box camera
            // must sample the same layers, but must not create a second glitch owner.
            targetData.volumeLayerMask = sourceData.volumeLayerMask;
            targetData.antialiasing = sourceData.antialiasing;
            targetData.SMAAQuality = sourceData.SMAAQuality;
            targetData.dithering = sourceData.dithering;
            targetData.stopNaNs = sourceData.stopNaNs;
        }

        private void Update()
        {
            if (!active) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                GameplayInputGate.ConsumeEscape(this);
                EndSession();
                return;
            }

            if (waitingForInteractionKeyRelease)
            {
                if (!Input.GetKey(KeyCode.E)) waitingForInteractionKeyRelease = false;
                return;
            }

            if (!cameraTransitionComplete)
                return;

            UpdateCameraLook();

            if (Input.GetKeyDown(KeyCode.E))
                InteractWithFocusedControl();
        }

        public void EndSession()
        {
            if (!active) return;
            active = false;
            controller?.RequestReleaseFromLocalPlayer();

            if (cameraBlendRoutine != null)
            {
                StopCoroutine(cameraBlendRoutine);
                cameraBlendRoutine = null;
            }

            if (playerCamera != null) playerCamera.enabled = originalPlayerCameraEnabled;
            if (interactionCamera != null)
            {
                interactionCamera.transform.SetPositionAndRotation(
                    interactionCameraRestPosition,
                    interactionCameraRestRotation);
                interactionCamera.fieldOfView = originalInteractionCameraFieldOfView;
                interactionCamera.enabled = originalInteractionCameraEnabled;
                if (interactionAudioListener != null)
                    interactionAudioListener.enabled = originalInteractionAudioListenerEnabled;
                interactionCamera.gameObject.SetActive(originalInteractionObjectActive);
            }

            if (movement != null) movement.enabled = true;
            playerInteraction?.TogglePlayerControl(true);
            viewSwitcher?.SetInteractionLocked(false);
            GameplayInputGate.Release(this);

            controller = null;
            playerInteraction = null;
            playerCamera = null;
            interactionCamera = null;
            interactionAudioListener = null;
            if (Active == this) Active = null;
        }

        public void BlendToPreset(
            Camera preset,
            float fieldOfView,
            float duration,
            AnimationCurve curve)
        {
            if (!active || interactionCamera == null || preset == null) return;

            interactionCameraRestPosition = preset.transform.position;
            interactionCameraRestRotation = preset.transform.rotation;
            lookYaw = 0f;
            lookPitch = 0f;
            cameraTransitionComplete = false;
            if (cameraBlendRoutine != null) StopCoroutine(cameraBlendRoutine);
            cameraBlendRoutine = StartCoroutine(BlendCameraToBox(
                fieldOfView,
                duration,
                curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f)));
        }

        private IEnumerator BlendCameraToBox(
            float targetFieldOfView,
            float duration,
            AnimationCurve curve)
        {
            Vector3 startPosition = interactionCamera.transform.position;
            Quaternion startRotation = interactionCamera.transform.rotation;
            float startFieldOfView = interactionCamera.fieldOfView;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = curve.Evaluate(Mathf.Clamp01(elapsed / duration));
                interactionCamera.transform.position =
                    Vector3.LerpUnclamped(startPosition, interactionCameraRestPosition, t);
                interactionCamera.transform.rotation =
                    Quaternion.SlerpUnclamped(startRotation, interactionCameraRestRotation, t);
                interactionCamera.fieldOfView =
                    Mathf.LerpUnclamped(startFieldOfView, targetFieldOfView, t);
                yield return null;
            }

            interactionCamera.transform.SetPositionAndRotation(
                interactionCameraRestPosition,
                interactionCameraRestRotation);
            interactionCamera.fieldOfView = targetFieldOfView;
            cameraTransitionComplete = true;
            cameraBlendRoutine = null;
        }

        private void UpdateCameraLook()
        {
            lookYaw = Mathf.Clamp(
                lookYaw + Input.GetAxisRaw("Mouse X") * cameraLookSensitivity,
                -cameraYawLimit,
                cameraYawLimit);
            lookPitch = Mathf.Clamp(
                lookPitch - Input.GetAxisRaw("Mouse Y") * cameraLookSensitivity,
                -cameraUpPitchLimit,
                cameraDownPitchLimit);

            interactionCamera.transform.rotation = interactionCameraRestRotation *
                                                   Quaternion.Euler(lookPitch, lookYaw, 0f);
        }

        private void InteractWithFocusedControl()
        {
            // Main Knob is larger than its imported pivot and can overlap the
            // cabinet/switch colliders in the ray. Give its visible bounds an
            // explicit hit test before choosing a raycast control.
            if (controller.IsMainKnobUnderCrosshair(interactionCamera, interactionDistance))
            {
                controller.RequestSubmitFromLocalPlayer();
                return;
            }

            Ray ray = interactionCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                interactionDistance,
                ~0,
                QueryTriggerInteraction.Ignore);

            DistributionSwitch closestSwitch = null;
            DistributionMainKnobTarget closestMainKnob = null;
            float closestControlDistance = float.PositiveInfinity;

            // The box door/frame can be in front of the controls. Do not stop at
            // the first collider; choose the nearest actual minigame control.
            foreach (RaycastHit hit in hits)
            {
                DistributionSwitch distributionSwitch =
                    hit.collider.GetComponentInParent<DistributionSwitch>();
                if (distributionSwitch != null && hit.distance < closestControlDistance)
                {
                    closestSwitch = distributionSwitch;
                    closestMainKnob = null;
                    closestControlDistance = hit.distance;
                    continue;
                }

                DistributionMainKnobTarget mainKnob =
                    hit.collider.GetComponentInParent<DistributionMainKnobTarget>();
                if (mainKnob != null && hit.distance < closestControlDistance)
                {
                    closestSwitch = null;
                    closestMainKnob = mainKnob;
                    closestControlDistance = hit.distance;
                }
            }

            if (closestSwitch != null)
            {
                controller.RequestToggleFromLocalPlayer(closestSwitch.Index);
                return;
            }

            if (closestMainKnob != null)
            {
                controller.RequestSubmitFromLocalPlayer();
                return;
            }

            Debug.LogWarning(
                $"[DistributionBox] No switch or MainKnob under the crosshair. " +
                $"Ray hit {hits.Length} collider(s).",
                controller);
        }

        private void OnDestroy()
        {
            if (active) EndSession();
            if (Active == this) Active = null;
            GameplayInputGate.Release(this);
        }
    }
}
