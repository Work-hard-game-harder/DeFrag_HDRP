using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Only created on the occupant's owning client. Restores the existing player camera.</summary>
[DefaultExecutionOrder(1000)]
public sealed class LockerLocalSession : MonoBehaviour
{
    private LockerHiding locker;
    private StarterAssets.PersonController movement;
    private PlayerInteraction interaction;
    private CameraViewSwitcher switcher;
    private Camera cameraView;
    private CharacterController body;
    private bool movementEnabled, interactionEnabled, bodyEnabled, active, released;
    private Vector3 entryPosition, cameraLocalPosition, cameraStartPosition;
    private Vector3 eyeOffset;
    private Quaternion entryRotation, cameraLocalRotation, cameraStartRotation;
    private Quaternion hiddenBodyRotation, hiddenViewRotation;
    private bool cursorVisible;
    private CursorLockMode cursorLock;

    public bool Begin(LockerHiding target, NetworkObject player)
    {
        if (!player.IsOwner || !GameplayInputGate.TryAcquire(this)) return false;
        locker = target;
        movement = GetComponent<StarterAssets.PersonController>();
        interaction = GetComponentInChildren<PlayerInteraction>(true);
        switcher = GetComponentInChildren<CameraViewSwitcher>(true);
        switcher?.SetInteractionLocked(true);
        cameraView = switcher != null ? switcher.ActiveCamera : interaction != null ? interaction.GetComponent<Camera>() : null;
        if (cameraView == null) { switcher?.SetInteractionLocked(false); GameplayInputGate.Release(this); return false; }
        entryPosition = transform.position; entryRotation = transform.rotation;
        // The player approaches while looking into the locker. Once hidden, both
        // the replicated body and the owning camera face back toward the door.
        hiddenBodyRotation = entryRotation * Quaternion.Euler(0f, 180f, 0f);
        hiddenViewRotation = hiddenBodyRotation * cameraView.transform.localRotation;
        cameraLocalPosition = cameraView.transform.localPosition; cameraLocalRotation = cameraView.transform.localRotation;
        cameraStartPosition = cameraView.transform.position; cameraStartRotation = cameraView.transform.rotation;
        eyeOffset = transform.InverseTransformPoint(cameraStartPosition);
        body = GetComponent<CharacterController>();
        movementEnabled = movement != null && movement.enabled;
        interactionEnabled = interaction != null && interaction.enabled;
        bodyEnabled = body != null && body.enabled;
        if (movement != null) movement.enabled = false;
        if (interaction != null) { interaction.CloseAllUI(); interaction.enabled = false; }
        if (body != null) body.enabled = false;
        cursorVisible = Cursor.visible; cursorLock = Cursor.lockState;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        active = true;
        return true;
    }
    private void LateUpdate()
    {
        if (!active) return;
        if (locker == null || !locker.IsSpawned) { Finish(); return; }
        var keyboard = Keyboard.current;
        if (keyboard != null && !keyboard.eKey.isPressed) released = true;
        if (released && locker.Phase == LockerPhase.Hidden && !SettingManager.IsMenuOpen && keyboard != null && keyboard.eKey.wasPressedThisFrame)
            locker.RequestExit();
        bool leaving = locker.Phase == LockerPhase.Exiting;
        float t = locker.Phase == LockerPhase.Hidden ? 1f : Mathf.SmoothStep(0f, 1f, locker.Progress);
        float movementT = Mathf.InverseLerp(locker.MovementDoorLead, 1f, t);
        movementT = Mathf.SmoothStep(0f, 1f, movementT);
        transform.SetPositionAndRotation(
            Vector3.Lerp(leaving ? locker.Inside.position : entryPosition, leaving ? entryPosition : locker.Inside.position, movementT),
            Quaternion.Slerp(leaving ? hiddenBodyRotation : entryRotation, leaving ? entryRotation : hiddenBodyRotation, movementT));
        Vector3 outsideEye = entryPosition + entryRotation * eyeOffset;
        cameraView.transform.SetPositionAndRotation(
            Vector3.Lerp(leaving ? locker.View.position : cameraStartPosition, leaving ? outsideEye : locker.View.position, movementT) +
                Vector3.up * (Mathf.Sin(movementT * Mathf.PI * 2f) * locker.Bob),
            Quaternion.Slerp(leaving ? hiddenViewRotation : cameraStartRotation, leaving ? entryRotation * cameraLocalRotation : hiddenViewRotation, movementT) *
                Quaternion.Euler(0f, 0f, Mathf.Sin(movementT * Mathf.PI * 2f) * locker.Roll));
    }
    public void Finish()
    {
        if (!active) return;
        active = false;
        // Return to the pose from which this local player entered. Imported locker
        // hierarchies may carry axis conversion and large scale values, so using a
        // child Exit Anchor directly can launch the character far above the map.
        transform.SetPositionAndRotation(entryPosition, entryRotation);
        if (cameraView != null) cameraView.transform.SetLocalPositionAndRotation(cameraLocalPosition, cameraLocalRotation);
        if (body != null) body.enabled = bodyEnabled;
        bool alive = !TryGetComponent<PlayerStats>(out var stats) || !stats.IsDead;
        if (movement != null && alive) movement.enabled = movementEnabled;
        if (interaction != null && alive) interaction.enabled = interactionEnabled;
        switcher?.SetInteractionLocked(false);
        Cursor.lockState = cursorLock; Cursor.visible = cursorVisible;
        GameplayInputGate.Release(this);
        Destroy(this);
    }
    private void OnDisable() { if (active) { locker?.CancelLocalEntry(); Finish(); } }
}
