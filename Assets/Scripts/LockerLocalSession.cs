using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Only created on the occupant's owning client. Restores the existing player camera.</summary>
[DefaultExecutionOrder(1000)]
public sealed class LockerLocalSession : MonoBehaviour
{
    // Hidden look-around: the player peers through the door vents without leaving the locker.
    private const float MaxLookYaw = 38f;
    private const float MaxLookPitchUp = 20f;
    private const float MaxLookPitchDown = 26f;
    private const float MouseLookScale = 0.08f;
    // Breath budget in seconds of held breath, recovery speed and the lock-out after a gasp.
    private const float BreathCapacity = 6.5f;
    private const float BreathRecoveryPerSecond = 1.3f;
    private const float GaspLockoutFraction = 0.35f;
    private const float ForcedOpenShake = 0.07f;

    private LockerHiding locker;
    private StarterAssets.PersonController movement;
    private PlayerInteraction interaction;
    private CameraViewSwitcher switcher;
    private Camera cameraView;
    private CharacterController body;
    private LockerHidingPresentation presentation;
    private bool movementEnabled, interactionEnabled, bodyEnabled, active, released;
    private Vector3 entryPosition, cameraLocalPosition, cameraStartPosition;
    private Vector3 eyeOffset;
    private Quaternion entryRotation, cameraLocalRotation, cameraStartRotation;
    private Quaternion hiddenBodyRotation, hiddenViewRotation;
    private bool cursorVisible;
    private CursorLockMode cursorLock;
    private float lookYaw, lookPitch;
    private float breath = BreathCapacity;
    private bool holdingBreath, breathLockedOut, reportedHolding;

    public float BreathNormalized => breath / BreathCapacity;
    public bool IsHoldingBreath => holdingBreath;
    public bool IsBreathLockedOut => breathLockedOut;
    public LockerHiding Locker => locker;

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
        presentation = gameObject.AddComponent<LockerHidingPresentation>();
        presentation.Begin(this, cameraView);
        active = true;
        return true;
    }
    private void LateUpdate()
    {
        if (!active) return;
        if (locker == null || !locker.IsSpawned) { Finish(); return; }
        var keyboard = Keyboard.current;
        bool menuOpen = SettingManager.IsMenuOpen;
        if (keyboard != null && !keyboard.eKey.isPressed) released = true;
        bool hidden = locker.Phase == LockerPhase.Hidden;
        if (released && hidden && !menuOpen && keyboard != null && keyboard.eKey.wasPressedThisFrame)
            locker.RequestExit();

        UpdateBreath(hidden && !menuOpen && keyboard != null && keyboard.spaceKey.isPressed);
        if (hidden && !menuOpen) UpdateLook();
        else { lookYaw = Mathf.MoveTowards(lookYaw, 0f, 180f * Time.deltaTime); lookPitch = Mathf.MoveTowards(lookPitch, 0f, 180f * Time.deltaTime); }

        bool forced = locker.Phase == LockerPhase.ForcedOpen;
        bool leaving = locker.Phase == LockerPhase.Exiting || forced;
        float t = hidden ? 1f : Mathf.SmoothStep(0f, 1f, locker.Progress);
        float movementT = forced ? t : Mathf.InverseLerp(locker.MovementDoorLead, 1f, t);
        movementT = Mathf.SmoothStep(0f, 1f, movementT);
        transform.SetPositionAndRotation(
            Vector3.Lerp(leaving ? locker.Inside.position : entryPosition, leaving ? entryPosition : locker.Inside.position, movementT),
            Quaternion.Slerp(leaving ? hiddenBodyRotation : entryRotation, leaving ? entryRotation : hiddenBodyRotation, movementT));
        Vector3 outsideEye = entryPosition + entryRotation * eyeOffset;
        Quaternion look = Quaternion.Euler(lookPitch, lookYaw, 0f);
        Vector3 shake = forced ? Random.insideUnitSphere * ForcedOpenShake * (1f - t) : Vector3.zero;
        cameraView.transform.SetPositionAndRotation(
            Vector3.Lerp(leaving ? locker.View.position : cameraStartPosition, leaving ? outsideEye : locker.View.position, movementT) +
                Vector3.up * (Mathf.Sin(movementT * Mathf.PI * 2f) * locker.Bob) + shake,
            Quaternion.Slerp(leaving ? hiddenViewRotation * look : cameraStartRotation, leaving ? entryRotation * cameraLocalRotation : hiddenViewRotation * look, movementT) *
                Quaternion.Euler(0f, 0f, Mathf.Sin(movementT * Mathf.PI * 2f) * locker.Roll));
    }

    private void UpdateLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        SettingManager settings = SettingManager.Instance;
        float sensitivity = (settings != null ? settings.LookSensitivityMultiplier : 1f) * MouseLookScale;
        float invert = settings != null && settings.InvertY ? -1f : 1f;
        Vector2 delta = mouse.delta.ReadValue();
        lookYaw = Mathf.Clamp(lookYaw + delta.x * sensitivity, -MaxLookYaw, MaxLookYaw);
        lookPitch = Mathf.Clamp(lookPitch - delta.y * sensitivity * invert, -MaxLookPitchUp, MaxLookPitchDown);
    }

    // Holding Space keeps the player silent; running out forces a loud gasp the monster can hear.
    private void UpdateBreath(bool wantsHold)
    {
        if (breathLockedOut && breath >= BreathCapacity * GaspLockoutFraction) breathLockedOut = false;
        holdingBreath = wantsHold && !breathLockedOut;
        if (holdingBreath)
        {
            breath -= Time.deltaTime;
            if (breath <= 0f)
            {
                breath = 0f;
                holdingBreath = false;
                breathLockedOut = true;
                locker.ReportGasp();
                presentation?.OnGasp();
            }
        }
        else
        {
            breath = Mathf.Min(BreathCapacity, breath + BreathRecoveryPerSecond * Time.deltaTime);
        }

        if (holdingBreath != reportedHolding)
        {
            reportedHolding = holdingBreath;
            locker.ReportHoldingBreath(holdingBreath);
        }
    }

    public void Finish()
    {
        if (!active) return;
        active = false;
        if (presentation != null) { presentation.End(); Destroy(presentation); presentation = null; }
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
