using System.Collections;
using EasyPeasyFirstPersonController;
using DeFrag.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

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

            if (controller.Phase != DistributionPuzzlePhase.MainKnob)
                UpdateCameraLook();

            if (Input.GetKeyDown(KeyCode.E))
            {
                if (controller.Phase == DistributionPuzzlePhase.MainKnob)
                    controller.RequestSubmitFromLocalPlayer();
                else
                    InteractWithFocusedControl();
            }
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
            DistributionTimingGaugePresenter.TryHideImmediate();
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

        public void ShowTimingGauge(
            double startServerTime,
            float targetCenter,
            float successWidth,
            float roundTripDuration)
        {
            if (!active) return;
            DistributionTimingGaugePresenter.GetOrCreate().ShowAttempt(
                startServerTime,
                targetCenter,
                successWidth,
                roundTripDuration);
        }

        public void ShowTimingFailure()
        {
            if (active) DistributionTimingGaugePresenter.TryShowFailure();
        }

        public void ShowTimingSuccess()
        {
            if (active) DistributionTimingGaugePresenter.TryShowSuccess();
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

namespace DeFrag.B1F
{
    [DisallowMultipleComponent]
    public sealed class DistributionTimingGaugePresenter : MonoBehaviour
    {
        private static readonly Color TerminalGreen = new(0.1f, 1f, 0.2f, 1f);
        private static readonly Color FailureRed = new(1f, 0.12f, 0.08f, 1f);
        private static DistributionTimingGaugePresenter instance;

        private RectTransform panel;
        private RectTransform successZone;
        private RectTransform movingBar;
        private Image trackImage;
        private Image successImage;
        private Image barImage;
        private TMP_Text instruction;
        private CanvasGroup canvasGroup;
        private double startServerTime;
        private float roundTripDuration;
        private bool running;
        private Coroutine feedbackRoutine;

        public static DistributionTimingGaugePresenter GetOrCreate()
        {
            if (instance != null) return instance;

            GameObject canvasObject = new(
                "Distribution Timing Gauge Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 126;
            ResponsiveCanvasUtility.Configure(canvasObject.GetComponent<CanvasScaler>());
            instance = canvasObject.AddComponent<DistributionTimingGaugePresenter>();
            instance.Build();
            return instance;
        }

        public static void TryHideImmediate()
        {
            if (instance == null) return;
            instance.running = false;
            instance.gameObject.SetActive(false);
        }

        public static void TryShowFailure()
        {
            if (instance != null) instance.PlayFailure();
        }

        public static void TryShowSuccess()
        {
            if (instance != null) instance.PlaySuccess();
        }

        public void ShowAttempt(
            double serverStartTime,
            float targetCenter,
            float successWidth,
            float duration)
        {
            if (feedbackRoutine != null)
            {
                StopCoroutine(feedbackRoutine);
                feedbackRoutine = null;
            }

            gameObject.SetActive(true);
            canvasGroup.alpha = 1f;
            panel.anchoredPosition = Vector2.zero;
            trackImage.color = new Color(0f, 0.04f, 0.01f, 0.94f);
            successImage.color = new Color(0.1f, 0.85f, 0.2f, 0.72f);
            barImage.color = Color.white;
            instruction.color = Color.white;
            instruction.text = "MAIN KNOB SYNCHRONIZATION  //  PRESS [E] IN THE GREEN ZONE";

            float halfWidth = successWidth * 0.5f;
            successZone.anchorMin = new Vector2(targetCenter - halfWidth, 0f);
            successZone.anchorMax = new Vector2(targetCenter + halfWidth, 1f);
            successZone.offsetMin = Vector2.zero;
            successZone.offsetMax = Vector2.zero;
            startServerTime = serverStartTime;
            roundTripDuration = Mathf.Max(0.4f, duration);
            running = true;
            UpdateBar();
        }

        private void Update()
        {
            if (running) UpdateBar();
        }

        private void UpdateBar()
        {
            double serverTime = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.ServerTime.Time
                : Time.unscaledTimeAsDouble;
            double elapsed = System.Math.Max(0d, serverTime - startServerTime);
            float position = Mathf.PingPong(
                (float)(elapsed / (roundTripDuration * 0.5f)),
                1f);
            movingBar.anchorMin = new Vector2(position, 0f);
            movingBar.anchorMax = new Vector2(position, 1f);
            movingBar.anchoredPosition = Vector2.zero;
        }

        private void PlayFailure()
        {
            running = false;
            if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
            feedbackRoutine = StartCoroutine(FailureRoutine());
        }

        private IEnumerator FailureRoutine()
        {
            instruction.text = "SYNCHRONIZATION FAILED // RECALIBRATING";
            instruction.color = FailureRed;
            barImage.color = FailureRed;
            Vector2 origin = panel.anchoredPosition;
            float elapsed = 0f;
            const float duration = 0.42f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float strength = 18f * (1f - elapsed / duration);
                panel.anchoredPosition = origin + Vector2.right *
                    Mathf.Sin(elapsed * 95f) * strength;
                yield return null;
            }
            panel.anchoredPosition = origin;
            feedbackRoutine = null;
        }

        private void PlaySuccess()
        {
            running = false;
            if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
            feedbackRoutine = StartCoroutine(SuccessRoutine());
        }

        private IEnumerator SuccessRoutine()
        {
            instruction.text = "SYNCHRONIZATION COMPLETE";
            instruction.color = TerminalGreen;
            barImage.color = TerminalGreen;
            float elapsed = 0f;
            const float duration = 0.3f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / duration);
                yield return null;
            }
            gameObject.SetActive(false);
            feedbackRoutine = null;
        }

        private void Build()
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
            GameObject panelObject = new("Timing Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(transform, false);
            panel = (RectTransform)panelObject.transform;
            panel.anchorMin = new Vector2(0.23f, 0.11f);
            panel.anchorMax = new Vector2(0.77f, 0.25f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.76f);

            GameObject track = new("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(panel, false);
            RectTransform trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(0.08f, 0.2f);
            trackRect.anchorMax = new Vector2(0.92f, 0.57f);
            trackRect.offsetMin = trackRect.offsetMax = Vector2.zero;
            trackImage = track.GetComponent<Image>();

            GameObject zone = new("Success Zone", typeof(RectTransform), typeof(Image));
            zone.transform.SetParent(trackRect, false);
            successZone = (RectTransform)zone.transform;
            successImage = zone.GetComponent<Image>();

            GameObject bar = new("Moving Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(trackRect, false);
            movingBar = (RectTransform)bar.transform;
            movingBar.sizeDelta = new Vector2(12f, 0f);
            barImage = bar.GetComponent<Image>();

            GameObject textObject = new(
                "Instruction", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panel, false);
            instruction = textObject.GetComponent<TextMeshProUGUI>();
            instruction.rectTransform.anchorMin = new Vector2(0.04f, 0.62f);
            instruction.rectTransform.anchorMax = new Vector2(0.96f, 0.95f);
            instruction.rectTransform.offsetMin = instruction.rectTransform.offsetMax = Vector2.zero;
            instruction.fontSize = 22f;
            instruction.fontStyle = FontStyles.Bold;
            instruction.alignment = TextAlignmentOptions.Center;
            instruction.raycastTarget = false;
        }
    }
}
