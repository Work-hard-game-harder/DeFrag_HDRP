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
        private static readonly TutorialCard BoxCard = new()
        {
            Id = "distA.box",
            Role = "카메라맨 • 배전함 조작",
            Title = "스위치 맞추기",
            Goal = "해커가 알려주는 대로 스위치를 ON/OFF 해서 비상전력을 복구합니다.",
            Steps = new[]
            {
                ("마우스", "화면 가운데 조준점으로 스위치를 겨누세요. 왼쪽에 번호(1~5)가 떠요."),
                ("E", "스위치를 OFF / ON 으로 밀어요."),
                ("무전", "정답은 해커 화면에만 보여요. \"3번 어떻게 해?\"라고 물어보세요!"),
                ("", "뱅크 A → B → C를 맞추면 메인 노브 단계로 넘어가요.")
            }
        };

        private static readonly TutorialCard KnobCard = new()
        {
            Id = "distA.knob.box",
            Role = "카메라맨 • 메인 노브",
            Title = "해커의 신호에 맞춰 당겨라",
            Goal = "바늘이 해커가 부르는 숫자 구간에 왔을 때 노브를 당깁니다.",
            Steps = new[]
            {
                ("", "아래 눈금(0~10) 위를 바늘이 왔다 갔다 해요."),
                ("무전", "해커에게 \"구간 몇이야?\" 물어보세요. (예: 6에서 8)"),
                ("E", "바늘이 그 구간에 들어왔을 때 E! 3번 성공하면 전력 복구!")
            }
        };

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
        private DistributionOperatorHud hud;
        private bool tutorialOpen;
        private bool knobBriefed;

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

            hud = DistributionOperatorHud.Create(box, interactionCamera);
            knobBriefed = false;
            ShowTutorial(BoxCard);
        }

        private void ShowTutorial(TutorialCard card)
        {
            if (MinigameTutorial.HasSeen(card.Id))
                return;
            tutorialOpen = true;
            MinigameTutorial.ShowBlockingOverlay(card, () => tutorialOpen = false);
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

            if (tutorialOpen)
                return;

            if (waitingForInteractionKeyRelease)
            {
                if (!Input.GetKey(KeyCode.E)) waitingForInteractionKeyRelease = false;
                return;
            }

            if (!cameraTransitionComplete)
                return;

            if (!knobBriefed && controller.Phase == DistributionPuzzlePhase.MainKnob)
            {
                knobBriefed = true;
                ShowTutorial(KnobCard);
                if (tutorialOpen) return;
            }

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
            if (hud != null) Destroy(hud.gameObject);
            hud = null;
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
            float roundTripDuration,
            int round,
            int totalRounds,
            bool zoneHidden)
        {
            if (!active) return;
            DistributionTimingGaugePresenter.GetOrCreate().ShowAttempt(
                startServerTime,
                targetCenter,
                successWidth,
                roundTripDuration,
                round,
                totalRounds,
                zoneHidden);
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
            float duration,
            int round,
            int totalRounds,
            bool zoneHidden)
        {
            if (feedbackRoutine != null)
            {
                StopCoroutine(feedbackRoutine);
                feedbackRoutine = null;
            }

            DefragUiTheme theme = RuntimeUi.Theme;
            gameObject.SetActive(true);
            canvasGroup.alpha = 1f;
            panel.anchoredPosition = Vector2.zero;
            trackImage.color = new Color(0f, 0f, 0f, 0.7f);
            successImage.color = new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.6f);
            successImage.enabled = !zoneHidden;
            barImage.color = theme.highlight;
            instruction.color = theme.highlight;
            instruction.text = zoneHidden
                ? $"메인 노브 {round}/{totalRounds}  //  해커가 부르는 숫자 구간에 바늘이 오면 [E]"
                : $"메인 노브 {round}/{totalRounds}  //  초록 구간에 바늘이 오면 [E]";
            UiSfx.Play(UiCue.KnobZone);

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
            instruction.text = "빗나갔어요! 다시 한 번 — 해커의 신호를 기다리세요";
            instruction.color = RuntimeUi.Theme.danger;
            barImage.color = RuntimeUi.Theme.danger;
            UiSfx.Play(UiCue.RhythmMiss);
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
            instruction.text = "동기화 성공!";
            instruction.color = RuntimeUi.Theme.accent;
            barImage.color = RuntimeUi.Theme.accent;
            UiSfx.Play(UiCue.BankComplete);
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
            Image panelImage = RuntimeUi.FramedPanel("Timing Panel", transform, RuntimeUi.Theme.panel, 16f);
            panel = panelImage.rectTransform;
            panel.anchorMin = new Vector2(0.2f, 0.09f);
            panel.anchorMax = new Vector2(0.8f, 0.27f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;

            GameObject track = new("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(panel, false);
            RectTransform trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(0.06f, 0.32f);
            trackRect.anchorMax = new Vector2(0.94f, 0.6f);
            trackRect.offsetMin = trackRect.offsetMax = Vector2.zero;
            trackImage = track.GetComponent<Image>();
            for (int tick = 0; tick <= 10; tick++)
            {
                Image mark = RuntimeUi.Panel($"Tick {tick}", trackRect, new Color(1f, 1f, 1f, 0.35f));
                mark.rectTransform.anchorMin = new Vector2(tick / 10f, 0f);
                mark.rectTransform.anchorMax = new Vector2(tick / 10f, 1f);
                mark.rectTransform.sizeDelta = new Vector2(2f, 0f);
                TMP_Text number = RuntimeUi.Text($"Tick Label {tick}", trackRect, 26f, TextAlignmentOptions.Center, null, RuntimeUi.Theme.info);
                number.rectTransform.anchorMin = number.rectTransform.anchorMax = new Vector2(tick / 10f, 0f);
                number.rectTransform.pivot = new Vector2(0.5f, 1f);
                number.rectTransform.sizeDelta = new Vector2(50f, 34f);
                number.rectTransform.anchoredPosition = new Vector2(0f, -4f);
                number.text = tick.ToString();
            }

            GameObject zone = new("Success Zone", typeof(RectTransform), typeof(Image));
            zone.transform.SetParent(trackRect, false);
            successZone = (RectTransform)zone.transform;
            successImage = zone.GetComponent<Image>();

            GameObject bar = new("Moving Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(trackRect, false);
            movingBar = (RectTransform)bar.transform;
            movingBar.sizeDelta = new Vector2(12f, 0f);
            barImage = bar.GetComponent<Image>();

            instruction = RuntimeUi.Text("Instruction", panel, 24f, TextAlignmentOptions.Center);
            RuntimeUi.Place(instruction.rectTransform, new Vector2(0.04f, 0.66f), new Vector2(0.96f, 0.95f));
            instruction.enableAutoSizing = true;
            instruction.fontSizeMin = 14f;
            instruction.fontSizeMax = 24f;
        }
    }
}
