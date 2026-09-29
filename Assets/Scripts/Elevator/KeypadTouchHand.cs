using System;
using UnityEngine;

/// <summary>
/// The local player's pointing hand for the keypad: it slides into view, hovers in front of the
/// touch pad, strikes the spot for each typed character and eases back. The hand mesh's pivot is
/// the index fingertip and its +Z is the pointing direction. Purely local presentation.
/// </summary>
public sealed class KeypadTouchHand : IDisposable
{
    private enum Mode { Hidden, Hovering, Striking, Leaving }

    private readonly Transform hand;
    private Transform panel;
    private Mode mode = Mode.Hidden;
    private Vector3 tip;
    private Vector3 tipVelocity;
    private Vector3 hoverTip;
    private Vector3 restTip;
    private Vector3 strikeFrom;
    private Vector3 strikeTo;
    private float strikeClock;
    private float lastStrikeAt = -10f;
    private float leaveClock;
    private Action contact;

    private const float StrikeSeconds = 0.075f;
    private const float HoldSeconds = 0.05f;
    private const float SettleSeconds = 0.09f;
    private const float ReturnToRestAfter = 0.9f;

    public KeypadTouchHand(GameObject prefab, Transform parent, float scale)
    {
        GameObject instance = UnityEngine.Object.Instantiate(prefab, parent);
        instance.name = "Keypad Hand (Local)";
        hand = instance.transform;
        hand.localScale = Vector3.one * scale;
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(collider);
        // The hand moves fast and close to the camera; per-object motion blur would smear it into a ghost.
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        instance.SetActive(false);
    }

    // Panel frame: out of the wall, up, and the viewer's right (local -X).
    private Vector3 Normal => panel.forward;
    private Vector3 Up => panel.up;
    private Vector3 ViewerRight => -panel.right;

    /// <summary>Slides the hand in from the lower right to rest beside the given pad point.</summary>
    public void Show(Transform panelTransform, Vector3 restPoint)
    {
        panel = panelTransform;
        restTip = restPoint + Normal * 0.08f + ViewerRight * 0.1f - Up * 0.1f;
        hoverTip = restTip;
        tip = restTip + ViewerRight * 0.22f - Up * 0.25f + Normal * 0.05f;
        tipVelocity = Vector3.zero;
        mode = Mode.Hovering;
        lastStrikeAt = -10f;
        hand.gameObject.SetActive(true);
        Apply(0f);
    }

    public void Hide()
    {
        if (mode == Mode.Hidden) return;
        mode = Mode.Leaving;
        leaveClock = 0f;
        contact = null;
    }

    /// <summary>Strikes the given surface point; onContact runs the moment the fingertip lands.</summary>
    public void Tap(Vector3 surfacePoint, Action onContact)
    {
        if (panel == null || mode == Mode.Hidden || mode == Mode.Leaving)
        {
            onContact?.Invoke();
            return;
        }
        // A tap arriving mid-strike lands the previous one at once, so fast typing never drops a ripple.
        if (mode == Mode.Striking) FireContact();
        strikeFrom = tip;
        strikeTo = surfacePoint - Normal * 0.003f;
        strikeClock = 0f;
        contact = onContact;
        mode = Mode.Striking;
        hoverTip = surfacePoint + Normal * 0.05f + ViewerRight * 0.035f - Up * 0.03f;
    }

    public void Update(float deltaTime)
    {
        if (mode == Mode.Hidden || panel == null) return;
        switch (mode)
        {
            case Mode.Striking:
                strikeClock += deltaTime;
                if (strikeClock < StrikeSeconds)
                {
                    float t = strikeClock / StrikeSeconds;
                    tip = Vector3.LerpUnclamped(strikeFrom, strikeTo, t * t);   // accelerate into the glass
                }
                else
                {
                    tip = strikeTo;
                    if (contact != null) FireContact();
                    if (strikeClock > StrikeSeconds + HoldSeconds)
                    {
                        mode = Mode.Hovering;
                        tipVelocity = Normal * 0.6f;
                        lastStrikeAt = Time.unscaledTime;
                    }
                }
                break;
            case Mode.Hovering:
            {
                Vector3 goal = Time.unscaledTime - lastStrikeAt > ReturnToRestAfter ? restTip : hoverTip;
                goal += Up * Mathf.Sin(Time.unscaledTime * 1.3f) * 0.004f + ViewerRight * Mathf.Sin(Time.unscaledTime * 0.9f) * 0.003f;
                tip = Vector3.SmoothDamp(tip, goal, ref tipVelocity, SettleSeconds * 1.6f, 4f, deltaTime);
                break;
            }
            case Mode.Leaving:
            {
                leaveClock += deltaTime;
                Vector3 away = restTip + ViewerRight * 0.25f - Up * 0.3f + Normal * 0.05f;
                tip = Vector3.SmoothDamp(tip, away, ref tipVelocity, 0.12f, 6f, deltaTime);
                if (leaveClock > 0.35f)
                {
                    mode = Mode.Hidden;
                    hand.gameObject.SetActive(false);
                    return;
                }
                break;
            }
        }
        Apply(deltaTime);
    }

    private void FireContact()
    {
        Action callback = contact;
        contact = null;
        callback?.Invoke();
    }

    private void Apply(float deltaTime)
    {
        // Point into the glass, coming from the lower right; tilt a touch more while striking.
        // The forearm runs off-screen to the lower right, so its cut end is never in view.
        float lean = mode == Mode.Striking ? 0.1f : 0f;
        Vector3 pointing = (-Normal * (0.55f + lean) + Up * 0.85f - ViewerRight * 0.5f).normalized;
        hand.SetPositionAndRotation(tip, Quaternion.LookRotation(pointing, Up + Normal * 0.3f));
    }

    public void Dispose()
    {
        if (hand != null) UnityEngine.Object.Destroy(hand.gameObject);
    }
}
