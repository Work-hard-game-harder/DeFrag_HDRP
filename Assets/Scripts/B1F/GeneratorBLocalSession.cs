using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;

namespace DeFrag.B1F
{
    // Owns the local player's generator station: camera swap, input capture, heartbeat, and HUD lifetime.
    [DisallowMultipleComponent]
    public sealed class GeneratorBLocalSession : MonoBehaviour
    {
        private const float HeartbeatInterval = 0.5f;
        private const float OwnershipGraceSeconds = 1.5f;
        private const float CrankShakeMeters = 0.012f;
        private const float BackfireShakeMeters = 0.06f;

        private static readonly TutorialCard RadarCard = new()
        {
            Id = "generatorB.radar",
            Role = "해커 • 발전기 제어 패널",
            Title = "시설 레이더로 길잡이",
            Goal = "동료가 연료통 2개를 찾아 발전기에 붓도록 안내하세요.",
            Steps = new[]
            {
                ("", "초록 화살표 = 동료, 주황 원 = 연료 신호 구역, 빨간 점 = 괴물(3초마다 탐지)"),
                ("무전", "\"왼쪽 복도로 쭉!\"처럼 길을 안내하고, 괴물이 가까우면 경고하세요."),
                ("클릭", "지도를 클릭하면 그곳에서 소음 미끼가 터져 괴물을 유인해요. (40초마다)"),
                ("", "연료 2개가 들어가면 점화 단계가 시작돼요.")
            }
        };

        private static readonly TutorialCard IgnitionCard = new()
        {
            Id = "generatorB.ignition",
            Role = "해커 • 점화 제어",
            Title = "점화 타이밍",
            Goal = "동료의 크랭크 속도를 맞춰주고, 알맞은 순간에 점화하세요.",
            Steps = new[]
            {
                ("", "오른쪽 막대의 초록 구간이 필요한 회전수예요. 화면에 '더 빨리/천천히'가 떠요."),
                ("무전", "그 문구를 그대로 외쳐서 동료의 연타 속도를 맞추세요."),
                ("SPACE", "회전수가 맞을 때 바늘이 초록 창에 오면 SPACE! 3번 성공하면 시동."),
                ("", "빗나가면 역화 폭음에 괴물이 옵니다. 대신 점화창이 조금씩 넓어져요.")
            }
        };

        private static readonly TutorialCard CrankCard = new()
        {
            Id = "generatorB.crank",
            Role = "카메라맨 • 시동 크랭크",
            Title = "크랭크를 돌려라",
            Goal = "해커의 신호에 맞춰 A와 D를 번갈아 연타합니다.",
            Steps = new[]
            {
                ("A  D", "번갈아 누를수록 빨리 돌아요. 같은 키만 누르면 거의 안 돌아요."),
                ("무전", "해커가 \"더 빨리!\" \"천천히!\"라고 외치면 속도를 맞추세요."),
                ("", "해커가 점화에 성공할 때마다 불이 하나씩 켜져요. 3개면 시동!")
            }
        };

        public static GeneratorBLocalSession Active { get; private set; }

        private GeneratorBController controller;
        private PlayerInteraction player;
        private Camera playerCamera;
        private Camera interactionCamera;
        private AudioListener interactionAudioListener;
        private StarterAssets.PersonController movement;
        private CameraViewSwitcher viewSwitcher;
        private Canvas canvas;
        private GeneratorBPanelView panelView;
        private GeneratorBCrankView crankView;
        private AudioSource uiAudio;
        private GeneratorBSessionMode mode;
        private bool originalPlayerCameraEnabled;
        private bool originalInteractionObjectActive;
        private bool originalInteractionCameraEnabled;
        private bool originalInteractionAudioListenerEnabled;
        private Vector3 cameraRestLocalPosition;
        private bool active;
        private bool ending;
        private float startedAt;
        private float nextHeartbeat;
        private float backfireShakeUntil;
        private bool tutorialOpen;
        private Coroutine delayedExit;

        public bool IsFor(GeneratorBController target) => active && controller == target;

        public void Begin(GeneratorBController target, PlayerInteraction localPlayer, Camera generatorCamera,
            GeneratorBSessionMode sessionMode)
        {
            if (active || target == null || localPlayer == null || generatorCamera == null)
                return;
            if (!GameplayInputGate.TryAcquire(this))
            {
                target.ReleaseLocalControl();
                return;
            }

            controller = target;
            player = localPlayer;
            playerCamera = localPlayer.GetComponent<Camera>();
            interactionCamera = generatorCamera;
            interactionAudioListener = interactionCamera.GetComponent<AudioListener>();
            mode = sessionMode;
            movement = localPlayer.GetComponentInParent<StarterAssets.PersonController>(true);
            viewSwitcher = localPlayer.GetComponentInParent<CameraViewSwitcher>(true);
            ending = false;
            active = true;
            startedAt = Time.unscaledTime;
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
            originalInteractionAudioListenerEnabled = interactionAudioListener != null && interactionAudioListener.enabled;
            cameraRestLocalPosition = interactionCamera.transform.localPosition;
            CopyCameraSettings(playerCamera, interactionCamera);
            interactionCamera.gameObject.SetActive(true);
            interactionCamera.enabled = true;
            if (interactionAudioListener != null)
                interactionAudioListener.enabled = false;
            if (playerCamera != null)
                playerCamera.enabled = false;

            canvas = RuntimeUi.Canvas("Generator B Session UI", transform, 160);
            uiAudio = canvas.gameObject.AddComponent<AudioSource>();
            uiAudio.playOnAwake = false;
            uiAudio.spatialBlend = 0f;
            if (mode == GeneratorBSessionMode.Panel)
                panelView = GeneratorBPanelView.Create(canvas.transform, controller);
            else
                crankView = GeneratorBCrankView.Create(canvas.transform, controller);

            controller.IgnitionResolved += OnIgnitionResolved;
            SetCursor(mode == GeneratorBSessionMode.Panel);
            controller.SendHeartbeat();
            nextHeartbeat = Time.unscaledTime + HeartbeatInterval;
            tutorialOpen = false;
            ShowCard(mode == GeneratorBSessionMode.Crank ? CrankCard : controller.IsPrimed ? IgnitionCard : RadarCard);
        }

        private void ShowCard(TutorialCard card)
        {
            if (MinigameTutorial.HasSeen(card.Id) || canvas == null)
                return;
            tutorialOpen = true;
            MinigameTutorial.ShowBlocking(card, (RectTransform)canvas.transform, () => tutorialOpen = false);
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

            if (controller == null || !controller.IsSpawned)
            {
                EndSession();
                return;
            }

            if (controller.IsComplete)
            {
                if (!ending)
                {
                    ending = true;
                    delayedExit = StartCoroutine(ExitAfterDelay(1.5f));
                }
                return;
            }

            if (Time.unscaledTime - startedAt > OwnershipGraceSeconds && !OwnsStation())
            {
                EndSession();
                return;
            }

            if (Time.unscaledTime >= nextHeartbeat)
            {
                controller.SendHeartbeat();
                nextHeartbeat = Time.unscaledTime + HeartbeatInterval;
            }

            if (!tutorialOpen && mode == GeneratorBSessionMode.Panel && controller.IsPrimed)
                ShowCard(IgnitionCard);
            if (tutorialOpen)
                return;

            if (mode == GeneratorBSessionMode.Panel)
            {
                if (SpacePressed())
                    panelView.RequestIgnition();
            }
            else
            {
                UpdateCrankInput();
            }
        }

        private void LateUpdate()
        {
            if (!active || mode != GeneratorBSessionMode.Crank || interactionCamera == null)
                return;

            float amplitude = CrankShakeMeters * controller.Rpm;
            if (Time.unscaledTime < backfireShakeUntil)
                amplitude += BackfireShakeMeters;
            interactionCamera.transform.localPosition = cameraRestLocalPosition + Random.insideUnitSphere * amplitude;
        }

        private void UpdateCrankInput()
        {
            if (!controller.IsPrimed)
                return;
            int side = LeftPressed() ? 0 : RightPressed() ? 1 : -1;
            if (side < 0)
                return;
            controller.SubmitCrankStroke(side);
            crankView.FlashKey(side);
            uiAudio.PlayOneShot(ProceduralSfx.CrankClick, 0.6f);
        }

        private bool OwnsStation()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && controller.OwnsSession(manager.LocalClientId, mode);
        }

        private void OnIgnitionResolved(GeneratorBIgnitionResult result)
        {
            if (result == GeneratorBIgnitionResult.Backfire)
                backfireShakeUntil = Time.unscaledTime + 0.35f;
        }

        public void EndSession()
        {
            if (!active)
                return;
            active = false;
            if (delayedExit != null)
                StopCoroutine(delayedExit);
            if (controller != null)
            {
                controller.IgnitionResolved -= OnIgnitionResolved;
                controller.ReleaseLocalControl();
            }

            if (playerCamera != null)
                playerCamera.enabled = originalPlayerCameraEnabled;
            if (interactionCamera != null)
            {
                interactionCamera.transform.localPosition = cameraRestLocalPosition;
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
            panelView = null;
            crankView = null;
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

        private static bool EscapePressed() => Keyboard.current != null
            ? Keyboard.current.escapeKey.wasPressedThisFrame
            : Input.GetKeyDown(KeyCode.Escape);
        private static bool SpacePressed() => Keyboard.current != null
            ? Keyboard.current.spaceKey.wasPressedThisFrame
            : Input.GetKeyDown(KeyCode.Space);
        private static bool LeftPressed() => Keyboard.current != null
            ? Keyboard.current.aKey.wasPressedThisFrame || Keyboard.current.leftArrowKey.wasPressedThisFrame
            : Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
        private static bool RightPressed() => Keyboard.current != null
            ? Keyboard.current.dKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame
            : Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);

        private static void SetCursor(bool visible)
        {
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = visible;
        }
    }
}
