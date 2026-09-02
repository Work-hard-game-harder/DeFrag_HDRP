using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace DeFrag.B1F
{
    [DisallowMultipleComponent]
    public sealed class GeneratorBLocalSession : MonoBehaviour
    {
        private static readonly Color Green = new(0.12f, 1f, 0.25f, 1f);
        private static readonly Color BrightGreen = new(0.45f, 1f, 0.55f, 1f);
        private static readonly Color Red = new(1f, 0.12f, 0.08f, 1f);

        public static GeneratorBLocalSession Active { get; private set; }

        private GeneratorBController controller;
        private PlayerInteraction player;
        private Camera playerCamera;
        private Camera interactionCamera;
        private AudioListener interactionAudioListener;
        private StarterAssets.PersonController movement;
        private CameraViewSwitcher viewSwitcher;
        private Canvas canvas;
        private TMP_Text pressureText;
        private TMP_Text statusText;
        private TMP_InputField commandInput;
        private RectTransform fuelFill;
        private RectTransform movingMarker;
        private RectTransform successZone;
        private GeneratorBSessionMode mode;
        private bool originalPlayerCameraEnabled;
        private bool originalInteractionObjectActive;
        private bool originalInteractionCameraEnabled;
        private bool originalInteractionAudioListenerEnabled;
        private int lastSubmittedAttempt = -1;
        private int observedPours;
        private bool active;
        private bool ending;
        private Coroutine delayedExit;

        public bool IsFor(GeneratorBController target) => active && controller == target;

        public void Begin(
            GeneratorBController target,
            PlayerInteraction localPlayer,
            Camera generatorCamera,
            GeneratorBSessionMode sessionMode)
        {
            if (active || target == null || localPlayer == null || generatorCamera == null)
                return;
            if (!GameplayInputGate.TryAcquire(this))
                return;

            controller = target;
            player = localPlayer;
            playerCamera = localPlayer.GetComponent<Camera>();
            interactionCamera = generatorCamera;
            interactionAudioListener = interactionCamera.GetComponent<AudioListener>();
            mode = sessionMode;
            movement = localPlayer.GetComponentInParent<StarterAssets.PersonController>(true);
            viewSwitcher = localPlayer.GetComponentInParent<CameraViewSwitcher>(true);
            observedPours = controller.SuccessfulPours;
            lastSubmittedAttempt = -1;
            ending = false;
            active = true;
            Active = this;

            player.CloseAllUI();
            player.TogglePlayerControl(false);
            if (movement != null)
                movement.enabled = false;
            viewSwitcher?.SetInteractionLocked(true);

            if (playerCamera != null)
                originalPlayerCameraEnabled = playerCamera.enabled;
            originalInteractionObjectActive = interactionCamera.gameObject.activeSelf;
            originalInteractionCameraEnabled = interactionCamera.enabled;
            originalInteractionAudioListenerEnabled =
                interactionAudioListener != null && interactionAudioListener.enabled;
            CopyCameraSettings(playerCamera, interactionCamera);
            interactionCamera.gameObject.SetActive(true);
            interactionCamera.enabled = true;
            if (interactionAudioListener != null)
                interactionAudioListener.enabled = false;
            if (playerCamera != null)
                playerCamera.enabled = false;

            BuildUi();
            SetCursor(mode == GeneratorBSessionMode.Search);
            if (commandInput != null)
            {
                commandInput.ActivateInputField();
                commandInput.Select();
            }
        }

        private void Update()
        {
            if (!active)
                return;

            if (EscapePressed())
            {
                GameplayInputGate.ConsumeEscape(this);
                EndSession();
                return;
            }

            RefreshFuelDisplay();
            if (controller.IsComplete && !ending)
            {
                ending = true;
                statusText.text = "GENERATOR B // FULL POWER RESTORED";
                statusText.color = BrightGreen;
                delayedExit = StartCoroutine(ExitAfterDelay(1.25f));
                return;
            }

            if (mode == GeneratorBSessionMode.Search)
                UpdateSearchInput();
            else
                UpdateFuelTiming();
        }

        public void ResolveSearchCommand(GeneratorBController source, bool accepted)
        {
            if (!IsFor(source) || statusText == null)
                return;

            if (!accepted)
            {
                statusText.text = "COMMAND REJECTED // TYPE SEARCH FUEL_B_CONTINUOUS";
                statusText.color = Red;
                commandInput?.ActivateInputField();
                return;
            }

            statusText.text = "CONTINUOUS FUEL SEARCH ACTIVE // LISTEN FOR 3D SIGNAL";
            statusText.color = BrightGreen;
            if (commandInput != null)
                commandInput.interactable = false;
            ending = true;
            delayedExit = StartCoroutine(ExitAfterDelay(0.9f));
        }

        private void UpdateSearchInput()
        {
            if (commandInput == null || !commandInput.interactable)
                return;

            if (TabPressed())
            {
                string current = commandInput.text.Trim().ToUpperInvariant();
                if (GeneratorBController.SearchCommand.StartsWith(current))
                {
                    commandInput.text = GeneratorBController.SearchCommand;
                    commandInput.caretPosition = commandInput.text.Length;
                    statusText.text = "AUTOCOMPLETE READY // PRESS ENTER";
                    statusText.color = Green;
                }
            }

            if (ConfirmPressed())
            {
                controller.SubmitSearchCommand(commandInput.text);
                statusText.text = "VALIDATING SEARCH COMMAND...";
                statusText.color = Green;
            }
        }

        private void UpdateFuelTiming()
        {
            if (movingMarker == null || successZone == null)
                return;

            float target = controller.TimingTarget;
            float halfWidth = controller.SuccessZoneWidth * 0.5f;
            successZone.anchorMin = new Vector2(Mathf.Clamp01(target - halfWidth), 0f);
            successZone.anchorMax = new Vector2(Mathf.Clamp01(target + halfWidth), 1f);
            successZone.offsetMin = Vector2.zero;
            successZone.offsetMax = Vector2.zero;

            float position = controller.EvaluateGauge(controller.ServerTime);
            movingMarker.anchorMin = new Vector2(position, 0f);
            movingMarker.anchorMax = new Vector2(position, 1f);
            movingMarker.anchoredPosition = Vector2.zero;

            int serial = controller.AttemptSerial;
            if (serial != lastSubmittedAttempt)
            {
                statusText.text = "PRESS SPACE INSIDE THE BRIGHT ZONE";
                statusText.color = Green;
            }

            if (SpacePressed() && controller.ServerTime >= controller.TimingStart &&
                lastSubmittedAttempt != serial)
            {
                lastSubmittedAttempt = serial;
                statusText.text = "CHECKING PRESSURE TIMING...";
                controller.SubmitFuelHit(controller.ServerTime, serial);
            }

            if (controller.SuccessfulPours != observedPours)
            {
                observedPours = controller.SuccessfulPours;
                statusText.text = $"FUEL INTAKE ACCEPTED // {controller.FuelPercent}%";
                statusText.color = BrightGreen;
            }
        }

        private void RefreshFuelDisplay()
        {
            if (pressureText != null)
                pressureText.text = controller.IsComplete
                    ? "FUEL PRESSURE 100% // NOMINAL"
                    : $"FUEL PRESSURE {controller.FuelPercent}% // " +
                      (mode == GeneratorBSessionMode.Search
                          ? "EMERGENCY FUEL REQUIRED"
                          : "MANUAL INTAKE ACTIVE");
            if (fuelFill != null)
                fuelFill.anchorMax = new Vector2(controller.FuelRatio, 1f);
        }

        private void BuildUi()
        {
            GameObject canvasObject = new(
                "Generator B Local UI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 160;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject panel = CreatePanel("Diagnostic Panel", canvasObject.transform);
            RectTransform panelRect = (RectTransform)panel.transform;
            Place(panelRect, new Vector2(0.12f, 0.13f), new Vector2(0.88f, 0.87f));
            panel.GetComponent<Image>().color = new Color(0.01f, 0.025f, 0.015f, 0.9f);

            TMP_Text title = CreateText(
                "Title", panel.transform, 38f, TextAlignmentOptions.TopLeft);
            Place(title.rectTransform, new Vector2(0.06f, 0.82f), new Vector2(0.94f, 0.96f));
            title.text = "GENERATOR B // EMERGENCY FUEL CONTROL";

            pressureText = CreateText(
                "Pressure", panel.transform, 31f, TextAlignmentOptions.Center);
            Place(pressureText.rectTransform, new Vector2(0.08f, 0.68f), new Vector2(0.92f, 0.80f));

            GameObject fuelBar = CreatePanel("Fuel Bar", panel.transform);
            RectTransform fuelBarRect = (RectTransform)fuelBar.transform;
            Place(fuelBarRect, new Vector2(0.12f, 0.61f), new Vector2(0.88f, 0.67f));
            fuelBar.GetComponent<Image>().color = new Color(0f, 0.12f, 0.025f, 1f);
            GameObject fill = CreatePanel("Fuel Fill", fuelBar.transform);
            fuelFill = (RectTransform)fill.transform;
            fuelFill.anchorMin = Vector2.zero;
            fuelFill.anchorMax = new Vector2(0f, 1f);
            fuelFill.offsetMin = Vector2.zero;
            fuelFill.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = BrightGreen;

            statusText = CreateText(
                "Status", panel.transform, 24f, TextAlignmentOptions.Center);
            Place(statusText.rectTransform, new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.25f));

            if (mode == GeneratorBSessionMode.Search)
                BuildSearchUi(panel.transform);
            else
                BuildFuelUi(panel.transform);

            TMP_Text footer = CreateText(
                "Footer", panel.transform, 20f, TextAlignmentOptions.BottomLeft);
            Place(footer.rectTransform, new Vector2(0.05f, 0.025f), new Vector2(0.95f, 0.10f));
            footer.text = mode == GeneratorBSessionMode.Search
                ? "[SE + TAB] AUTOCOMPLETE    [ENTER] EXECUTE    [ESC] EXIT"
                : "[SPACE] INJECT FUEL    [ESC] EXIT";
        }

        private void BuildSearchUi(Transform parent)
        {
            TMP_Text instruction = CreateText(
                "Instruction", parent, 25f, TextAlignmentOptions.Center);
            Place(instruction.rectTransform, new Vector2(0.08f, 0.46f), new Vector2(0.92f, 0.58f));
            instruction.text = "LOCATE EMERGENCY FUEL WITH A CONTINUOUS SEARCH COMMAND";

            GameObject inputObject = new(
                "Search Command Input",
                typeof(RectTransform),
                typeof(Image),
                typeof(TMP_InputField));
            inputObject.transform.SetParent(parent, false);
            RectTransform inputRect = (RectTransform)inputObject.transform;
            Place(inputRect, new Vector2(0.12f, 0.31f), new Vector2(0.88f, 0.43f));
            inputObject.GetComponent<Image>().color = new Color(0f, 0.15f, 0.035f, 0.95f);

            TMP_Text inputText = CreateText(
                "Text", inputObject.transform, 28f, TextAlignmentOptions.MidlineLeft);
            StretchWithPadding(inputText.rectTransform, 20f, 20f);
            TMP_Text placeholder = CreateText(
                "Placeholder", inputObject.transform, 25f, TextAlignmentOptions.MidlineLeft);
            StretchWithPadding(placeholder.rectTransform, 20f, 20f);
            placeholder.text = "> TYPE SE, THEN PRESS TAB";
            placeholder.color = new Color(Green.r, Green.g, Green.b, 0.42f);

            commandInput = inputObject.GetComponent<TMP_InputField>();
            commandInput.textComponent = (TextMeshProUGUI)inputText;
            commandInput.placeholder = (Graphic)placeholder;
            commandInput.lineType = TMP_InputField.LineType.SingleLine;
            commandInput.characterLimit = 32;
            commandInput.onValidateInput = ValidateCommandCharacter;
            statusText.text = "FUEL INDEX OFFLINE // MANUAL SEARCH REQUIRED";
        }

        private void BuildFuelUi(Transform parent)
        {
            TMP_Text instruction = CreateText(
                "Instruction", parent, 25f, TextAlignmentOptions.Center);
            Place(instruction.rectTransform, new Vector2(0.08f, 0.48f), new Vector2(0.92f, 0.58f));
            instruction.text = "ALIGN THE PRESSURE MARKER WITH THE INTAKE ZONE";

            GameObject gauge = CreatePanel("Timing Gauge", parent);
            RectTransform gaugeRect = (RectTransform)gauge.transform;
            Place(gaugeRect, new Vector2(0.12f, 0.34f), new Vector2(0.88f, 0.44f));
            gauge.GetComponent<Image>().color = new Color(0f, 0.08f, 0.02f, 1f);

            GameObject zone = CreatePanel("Success Zone", gauge.transform);
            successZone = (RectTransform)zone.transform;
            zone.GetComponent<Image>().color = new Color(0.35f, 1f, 0.45f, 0.72f);

            GameObject marker = CreatePanel("Moving Marker", gauge.transform);
            movingMarker = (RectTransform)marker.transform;
            movingMarker.sizeDelta = new Vector2(10f, 0f);
            marker.GetComponent<Image>().color = Color.white;

            statusText.text = "PRESS SPACE INSIDE THE BRIGHT ZONE";
        }

        public void EndSession()
        {
            if (!active)
                return;
            active = false;
            if (delayedExit != null)
                StopCoroutine(delayedExit);
            controller?.ReleaseLocalControl();

            if (playerCamera != null)
                playerCamera.enabled = originalPlayerCameraEnabled;
            if (interactionCamera != null)
            {
                interactionCamera.enabled = originalInteractionCameraEnabled;
                if (interactionAudioListener != null)
                    interactionAudioListener.enabled = originalInteractionAudioListenerEnabled;
                interactionCamera.gameObject.SetActive(originalInteractionObjectActive);
            }
            if (movement != null)
                movement.enabled = true;
            player?.TogglePlayerControl(true);
            viewSwitcher?.SetInteractionLocked(false);
            SetCursor(false);
            GameplayInputGate.Release(this);

            if (canvas != null)
                Destroy(canvas.gameObject);
            canvas = null;
            commandInput = null;
            controller = null;
            player = null;
            interactionCamera = null;
            interactionAudioListener = null;
            if (Active == this)
                Active = null;
        }

        private IEnumerator ExitAfterDelay(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            delayedExit = null;
            EndSession();
        }

        private void OnDestroy()
        {
            if (active)
                EndSession();
            if (Active == this)
                Active = null;
            GameplayInputGate.Release(this);
        }

        private static void CopyCameraSettings(Camera source, Camera target)
        {
            if (source == null || target == null)
                return;
            target.allowHDR = source.allowHDR;
            target.allowMSAA = source.allowMSAA;
            HDAdditionalCameraData sourceData = source.GetComponent<HDAdditionalCameraData>();
            HDAdditionalCameraData targetData = target.GetComponent<HDAdditionalCameraData>();
            if (sourceData == null || targetData == null)
                return;
            targetData.volumeLayerMask = sourceData.volumeLayerMask;
            targetData.antialiasing = sourceData.antialiasing;
            targetData.SMAAQuality = sourceData.SMAAQuality;
        }

        private static GameObject CreatePanel(string name, Transform parent)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            return panel;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            float size,
            TextAlignmentOptions alignment)
        {
            GameObject target = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            target.transform.SetParent(parent, false);
            TMP_Text text = target.GetComponent<TMP_Text>();
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = Green;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void StretchWithPadding(RectTransform rect, float left, float right)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
        }

        private static char ValidateCommandCharacter(string _, int __, char character)
        {
            char upper = char.ToUpperInvariant(character);
            return char.IsLetterOrDigit(upper) || upper == '_' || upper == ' '
                ? upper
                : '\0';
        }

        private static bool EscapePressed() => Keyboard.current != null
            ? Keyboard.current.escapeKey.wasPressedThisFrame
            : Input.GetKeyDown(KeyCode.Escape);
        private static bool TabPressed() => Keyboard.current != null
            ? Keyboard.current.tabKey.wasPressedThisFrame
            : Input.GetKeyDown(KeyCode.Tab);
        private static bool ConfirmPressed() => Keyboard.current != null
            ? Keyboard.current.enterKey.wasPressedThisFrame ||
              Keyboard.current.numpadEnterKey.wasPressedThisFrame
            : Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        private static bool SpacePressed() => Keyboard.current != null
            ? Keyboard.current.spaceKey.wasPressedThisFrame
            : Input.GetKeyDown(KeyCode.Space);
        private static void SetCursor(bool ui)
        {
            Cursor.lockState = ui ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = ui;
        }
    }
}
