using UnityEngine;

/// <summary>
/// Moves the equipped hacking pad between its normal hand pose and a closer
/// reading pose while the right mouse button is held.
/// </summary>
public sealed class HackingPadHeldController : MonoBehaviour
{
    [SerializeField] private Vector3 inspectLocalPosition = new Vector3(0f, -0.08f, 0.22f);
    [SerializeField] private Vector3 inspectLocalEulerAngles = new Vector3(-72f, 0f, 0f);
    [SerializeField] private Vector3 inspectLocalScale = new Vector3(1.15f, 1.15f, 1.15f);
    [SerializeField, Min(0.01f)] private float transitionSpeed = 9f;

    private Vector3 normalLocalPosition;
    private Quaternion normalLocalRotation;
    private Vector3 normalLocalScale;
    private bool focusLocked;

    public bool IsInspecting { get; private set; }

    private void Awake()
    {
        CaptureNormalPose();
    }

    public void CaptureNormalPose()
    {
        normalLocalPosition = transform.localPosition;
        normalLocalRotation = transform.localRotation;
        normalLocalScale = transform.localScale;
    }

    private void Update()
    {
        if (!focusLocked)
            IsInspecting = Input.GetMouseButton(1);

        Vector3 targetPosition = IsInspecting ? inspectLocalPosition : normalLocalPosition;
        Quaternion targetRotation = IsInspecting
            ? Quaternion.Euler(inspectLocalEulerAngles)
            : normalLocalRotation;
        Vector3 targetScale = IsInspecting ? inspectLocalScale : normalLocalScale;
        float t = 1f - Mathf.Exp(-transitionSpeed * Time.unscaledDeltaTime);

        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, t);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, t);
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, t);
    }

    public void SetFocusLocked(bool locked)
    {
        focusLocked = locked;
        IsInspecting = locked || Input.GetMouseButton(1);
        enabled = !locked;
    }
}
