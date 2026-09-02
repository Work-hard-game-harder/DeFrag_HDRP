using UnityEngine;

[DisallowMultipleComponent]
public sealed class OpticalRelayNode : MonoBehaviour
{
    [Header("Relay Identity")]
    [SerializeField] private string relayId = "TERMINAL_01";
    [SerializeField] private string sector = "SECTOR A";
    [SerializeField] private bool selectable = true;

    [Header("Optical Scan")]
    [Tooltip("IR camera aims at this point. Defaults to this transform.")]
    [SerializeField] private Transform scanAnchor;
    [Tooltip("Put this collider on the visible front face of the relay.")]
    [SerializeField] private Collider scanCollider;
    [SerializeField, Min(0.1f)] private float identificationDistance = 8f;
    [SerializeField, Min(0.1f)] private float captureDistance = 4f;
    [SerializeField, Range(-1f, 1f)] private float minimumFrontDot = 0.25f;

    public string RelayId => Normalize(relayId);
    public string Sector => string.IsNullOrWhiteSpace(sector)
        ? "UNKNOWN"
        : sector.Trim().ToUpperInvariant();
    public bool Selectable => selectable && isActiveAndEnabled;
    public Transform ScanAnchor => scanAnchor != null ? scanAnchor : transform;
    public Collider ScanCollider => scanCollider;
    public float IdentificationDistance => identificationDistance;
    public float CaptureDistance => captureDistance;

    public bool IsInFrontAndRange(Vector3 observerPosition, float maximumDistance)
    {
        Vector3 anchorPosition = ScanAnchor.position;
        Vector3 toObserver = observerPosition - anchorPosition;
        if (toObserver.sqrMagnitude > maximumDistance * maximumDistance)
            return false;

        if (toObserver.sqrMagnitude <= 0.0001f)
            return true;

        return Vector3.Dot(ScanAnchor.forward, toObserver.normalized) >= minimumFrontDot;
    }

    public bool OwnsCollider(Collider candidate)
    {
        if (candidate == null)
            return false;
        if (scanCollider != null && candidate == scanCollider)
            return true;
        return candidate.GetComponentInParent<OpticalRelayNode>() == this;
    }

    public void PlayLocalAlarm(AudioClip alarmClip, float volume)
    {
        if (alarmClip == null)
            return;

        AudioSource.PlayClipAtPoint(
            alarmClip,
            ScanAnchor.position,
            Mathf.Clamp01(volume));
    }

    private void Reset()
    {
        scanAnchor = transform;
        scanCollider = GetComponentInChildren<Collider>(true);
    }

    private void OnValidate()
    {
        relayId = Normalize(relayId);
        identificationDistance = Mathf.Max(identificationDistance, captureDistance);
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "UNASSIGNED"
            : value.Trim().Replace(' ', '_').ToUpperInvariant();
    }

    private void OnDrawGizmosSelected()
    {
        Transform anchor = ScanAnchor;
        Gizmos.color = new Color(0.1f, 1f, 0.4f, 0.75f);
        Gizmos.DrawWireSphere(anchor.position, captureDistance);
        Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(anchor.position, identificationDistance);
        Gizmos.DrawRay(anchor.position, anchor.forward * 0.75f);
    }
}
