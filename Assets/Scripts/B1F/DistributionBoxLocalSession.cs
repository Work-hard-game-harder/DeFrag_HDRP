using EasyPeasyFirstPersonController;
using UnityEngine;

namespace DeFrag.B1F
{
    public sealed class DistributionBoxLocalSession : MonoBehaviour
    {
        public static DistributionBoxLocalSession Active { get; private set; }

        private DistributionBoxController controller;
        private PlayerInteraction playerInteraction;
        private Camera playerCamera;
        private Camera interactionCamera;
        private FirstPersonController movement;
        private CameraViewSwitcher viewSwitcher;
        private AudioListener interactionAudioListener;
        private bool originalPlayerCameraEnabled;
        private bool originalInteractionObjectActive;
        private bool originalInteractionCameraEnabled;
        private bool originalInteractionAudioListenerEnabled;
        private float interactionDistance;
        private bool waitingForInteractionKeyRelease;
        private bool active;

        public void Begin(
            DistributionBoxController box,
            PlayerInteraction player,
            Camera localPlayerCamera,
            Camera boxCamera,
            float rayDistance)
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
            movement = player.GetComponentInParent<FirstPersonController>(true);
            viewSwitcher = player.GetComponentInParent<CameraViewSwitcher>(true);
            interactionAudioListener = interactionCamera.GetComponent<AudioListener>();

            originalPlayerCameraEnabled = playerCamera.enabled;
            originalInteractionObjectActive = interactionCamera.gameObject.activeSelf;
            originalInteractionCameraEnabled = interactionCamera.enabled;
            originalInteractionAudioListenerEnabled =
                interactionAudioListener != null && interactionAudioListener.enabled;

            waitingForInteractionKeyRelease = true;
            active = true;
            Active = this;

            player.CloseAllUI();
            player.TogglePlayerControl(false);
            if (movement != null) movement.enabled = false;
            viewSwitcher?.SetInteractionLocked(true);

            interactionCamera.gameObject.SetActive(true);
            if (interactionAudioListener != null) interactionAudioListener.enabled = false;
            interactionCamera.enabled = true;
            playerCamera.enabled = false;
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

            if (Input.GetKeyDown(KeyCode.E))
                InteractWithFocusedControl();
        }

        public void EndSession()
        {
            if (!active) return;
            active = false;
            controller?.RequestReleaseFromLocalPlayer();

            if (playerCamera != null) playerCamera.enabled = originalPlayerCameraEnabled;
            if (interactionCamera != null)
            {
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

        private void InteractWithFocusedControl()
        {
            Ray ray = interactionCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            if (!Physics.Raycast(ray, out RaycastHit hit, interactionDistance,
                    ~0, QueryTriggerInteraction.Ignore))
                return;

            DistributionSwitch distributionSwitch =
                hit.collider.GetComponentInParent<DistributionSwitch>();
            if (distributionSwitch != null)
            {
                controller.RequestToggleFromLocalPlayer(distributionSwitch.Index);
                return;
            }

            if (hit.collider.GetComponentInParent<DistributionMainKnobTarget>() != null)
                controller.RequestSubmitFromLocalPlayer();
        }

        private void OnDestroy()
        {
            if (active) EndSession();
            if (Active == this) Active = null;
            GameplayInputGate.Release(this);
        }
    }
}
