using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Flies the local player's camera up to a device, holds it there with small reactive
/// nudges and shakes, then flies it back and restores everything it touched.
/// Local presentation only: it locks the owning player's movement while focused.
/// </summary>
public sealed class DeviceFocusCamera
{
    private enum Mode { Idle, FlyingIn, Focused, FlyingOut }

    private readonly Transform cameraTransform;
    private readonly Camera camera;
    private readonly Vector3 homeLocalPosition;
    private readonly Quaternion homeLocalRotation;
    private readonly float homeFieldOfView;
    private readonly List<(Behaviour behaviour, bool enabled)> lockedBehaviours = new();
    private readonly List<(Renderer renderer, bool enabled)> hiddenRenderers = new();

    private Mode mode;
    private float clock;
    private float duration;
    private Pose from;
    private Pose target;
    private float fromFov;
    private float targetFov;

    private Vector3 nudgeDirection;
    private float nudgeStrength;
    private float nudgeAge = 10f;
    private float shake;

    public bool IsFocused => mode == Mode.Focused;
    public bool IsActive => mode != Mode.Idle;
    public Camera Camera => camera;

    /// <summary>Raised once the camera is back in the player's head and control is restored.</summary>
    public event Action Returned;

    private DeviceFocusCamera(Camera camera)
    {
        this.camera = camera;
        cameraTransform = camera.transform;
        homeLocalPosition = cameraTransform.localPosition;
        homeLocalRotation = cameraTransform.localRotation;
        homeFieldOfView = camera.fieldOfView;
    }

    /// <summary>Starts flying toward a pose that looks at a device face.</summary>
    public static DeviceFocusCamera Begin(Camera camera, Pose focusPose, float fieldOfView, float seconds)
    {
        if (camera == null) return null;
        var focus = new DeviceFocusCamera(camera);
        focus.LockPlayer();
        focus.from = new Pose(camera.transform.position, camera.transform.rotation);
        focus.fromFov = camera.fieldOfView;
        focus.target = focusPose;
        focus.targetFov = fieldOfView;
        focus.duration = Mathf.Max(0.01f, seconds);
        focus.clock = 0f;
        focus.mode = Mode.FlyingIn;
        return focus;
    }

    public float HomeFieldOfView => homeFieldOfView;

    /// <summary>Distance at which a face of the given size fills the requested share of the screen.</summary>
    public static float FitDistance(Camera camera, float fieldOfView, Vector2 faceSize, float screenFill)
    {
        float vertical = fieldOfView * 0.5f * Mathf.Deg2Rad;
        float horizontal = Mathf.Atan(Mathf.Tan(vertical) * camera.aspect);
        float fill = Mathf.Clamp(screenFill, 0.1f, 1f);
        float byHeight = faceSize.y * 0.5f / fill / Mathf.Tan(vertical);
        float byWidth = faceSize.x * 0.5f / fill / Mathf.Tan(horizontal);
        return Mathf.Max(byHeight, byWidth, camera.nearClipPlane * 3f);
    }

    public void Release(float seconds)
    {
        if (mode == Mode.Idle || mode == Mode.FlyingOut) return;
        from = new Pose(cameraTransform.position, cameraTransform.rotation);
        fromFov = camera.fieldOfView;
        duration = Mathf.Max(0.01f, seconds);
        clock = 0f;
        mode = Mode.FlyingOut;
    }

    /// <summary>A small reactive lean toward a point (e.g. the key being pressed).</summary>
    public void Nudge(Vector3 worldPoint, float strength)
    {
        nudgeDirection = (worldPoint - cameraTransform.position).normalized;
        nudgeStrength = strength;
        nudgeAge = 0f;
    }

    public void Shake(float amount) => shake = Mathf.Max(shake, amount);

    /// <summary>Call from LateUpdate so the pose wins over anything that moved the camera this frame.</summary>
    public void Update(float deltaTime)
    {
        if (mode == Mode.Idle || cameraTransform == null) return;
        clock += deltaTime;
        float t = Mathf.Clamp01(clock / duration);

        Vector3 position;
        Quaternion rotation;
        float fov;
        switch (mode)
        {
            case Mode.FlyingIn:
            {
                // Turn toward the device first, then glide in and settle.
                float look = 1f - Mathf.Pow(1f - Mathf.Clamp01(t * 1.5f), 3f);
                float pull = t < 1f ? 1f - Mathf.Pow(1f - t, 3f) : 1f;
                position = Vector3.Lerp(from.position, target.position, pull);
                rotation = Quaternion.Slerp(from.rotation, target.rotation, look) *
                           Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI) * 1.5f);
                fov = Mathf.Lerp(fromFov, targetFov, pull);
                if (t >= 1f) mode = Mode.Focused;
                break;
            }
            case Mode.FlyingOut:
            {
                Transform parent = cameraTransform.parent;
                Vector3 homePosition = parent != null ? parent.TransformPoint(homeLocalPosition) : homeLocalPosition;
                Quaternion homeRotation = parent != null ? parent.rotation * homeLocalRotation : homeLocalRotation;
                float e = 1f - Mathf.Pow(1f - t, 3f);
                position = Vector3.Lerp(from.position, homePosition, e);
                rotation = Quaternion.Slerp(from.rotation, homeRotation, e);
                fov = Mathf.Lerp(fromFov, homeFieldOfView, e);
                if (t >= 1f)
                {
                    RestoreImmediately();
                    return;
                }
                break;
            }
            default:
                position = target.position;
                rotation = target.rotation;
                fov = targetFov;
                break;
        }

        // Lean toward the pressed key and spring back.
        nudgeAge += deltaTime;
        float lean = nudgeStrength * Mathf.Exp(-nudgeAge * 9f) * Mathf.Sin(Mathf.Min(1f, nudgeAge * 18f) * Mathf.PI * 0.5f);
        if (lean > 0.0001f)
        {
            Vector3 localDirection = Quaternion.Inverse(rotation) * nudgeDirection;
            rotation *= Quaternion.Euler(-localDirection.y * 1.4f * lean, localDirection.x * 1.4f * lean, -localDirection.x * 0.8f * lean);
            position += nudgeDirection * 0.006f * lean;
        }

        shake = Mathf.MoveTowards(shake, 0f, deltaTime * 2.2f);
        if (shake > 0f)
        {
            float s = shake * shake;
            float n = Time.unscaledTime * 38f;
            rotation *= Quaternion.Euler((Mathf.PerlinNoise(n, 1.3f) - 0.5f) * 3f * s, (Mathf.PerlinNoise(n, 7.1f) - 0.5f) * 3f * s, 0f);
        }

        cameraTransform.SetPositionAndRotation(position, rotation);
        camera.fieldOfView = fov;
    }

    /// <summary>Puts the camera home at once and gives control back (also used on teardown).</summary>
    public void RestoreImmediately()
    {
        if (mode == Mode.Idle) return;
        mode = Mode.Idle;
        if (cameraTransform != null)
        {
            cameraTransform.localPosition = homeLocalPosition;
            cameraTransform.localRotation = homeLocalRotation;
        }
        if (camera != null) camera.fieldOfView = homeFieldOfView;
        foreach (var (behaviour, enabled) in lockedBehaviours) if (behaviour != null) behaviour.enabled = enabled;
        foreach (var (renderer, enabled) in hiddenRenderers) if (renderer != null) renderer.enabled = enabled;
        lockedBehaviours.Clear();
        hiddenRenderers.Clear();
        Returned?.Invoke();
    }

    private void LockPlayer()
    {
        // Movement and look must stop: typed letters (W, A, S, D, C) would otherwise walk or crouch.
        Transform root = cameraTransform.root;
        var movement = root.GetComponentInChildren<StarterAssets.PersonController>(true);
        if (movement != null)
        {
            lockedBehaviours.Add((movement, movement.enabled));
            movement.enabled = false;
        }
        // Held items hang off the camera and would sit in front of the device.
        foreach (Renderer renderer in cameraTransform.GetComponentsInChildren<Renderer>(true))
        {
            hiddenRenderers.Add((renderer, renderer.enabled));
            renderer.enabled = false;
        }
    }
}
