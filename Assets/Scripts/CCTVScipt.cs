using System.Collections;
using UnityEngine;

public class CCTVScript : MonoBehaviour
{
    [Header("Detection Setting")]
    [SerializeField] private Light spotLight;
    [SerializeField] private float detectionRange = 10f;
    [SerializeField] private float detectionAngle = 30f;

    [Header("Rotation Setting")]
    [SerializeField] private Transform pivot;
    [SerializeField] private float rotationSpeed = 45f;
    [SerializeField] private float maxAngle = 90f;
    [SerializeField] private float pauseDuration = 2f;

    [Header("Sound Setting")]
    [SerializeField] private AudioSource audioSource;

    private float startYAngle;
    private Quaternion startRotation;
    private float currentAngle = 0f;
    private bool isPaused = false;

    private enum CCTVState { Rotating, Detected }
    private CCTVState currentState = CCTVState.Rotating;

    void Start()
    {
        Transform rotateTarget = pivot != null ? pivot : transform;
        startRotation = rotateTarget.localRotation;

        audioSource.loop = true;
        audioSource.Stop();
    }

    void Update()
    {
        switch (currentState)
        {
            case CCTVState.Rotating:
                RotateCCTV();
                DetectPlayer();
                break;
            case CCTVState.Detected:
                CheckPlayerLeft();
                break;
        }
    }

    void RotateCCTV()
    {
        if (isPaused) return;

        Transform rotateTarget = pivot != null ? pivot : transform;

        currentAngle += rotationSpeed * Time.deltaTime;

        if (Mathf.Abs(currentAngle) >= maxAngle)
        {
            currentAngle = maxAngle * Mathf.Sign(currentAngle);
            StartCoroutine(PauseCCTV());
        }

        rotateTarget.localRotation = startRotation * Quaternion.Euler(0, currentAngle, 0);
    }

    IEnumerator PauseCCTV()
    {
        isPaused = true;
        yield return new WaitForSeconds(pauseDuration);
        rotationSpeed = -rotationSpeed;
        isPaused = false;
    }

    void DetectPlayer()
    {
        Vector3 origin = pivot != null ? pivot.position : transform.position;

        Collider[] hits = Physics.OverlapSphere(origin, detectionRange);
        foreach (var hit in hits)
        {
            if (!HasPlayerTag(hit.transform)) continue;

            Vector3 dirToPlayer = (hit.transform.position - origin).normalized;
            float angle = Vector3.Angle(spotLight.transform.forward, dirToPlayer);

            if (angle < detectionAngle)
            {
                if (Physics.Raycast(spotLight.transform.position, dirToPlayer,
                    out RaycastHit rayHit, detectionRange))
                {
                    if (HasPlayerTag(rayHit.transform))
                    {
                        OnPlayerDetected();
                        return;
                    }
                }
            }
        }
    }

    void CheckPlayerLeft()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { OnPlayerLost(); return; }

        Collider playerCollider = player.GetComponentInChildren<Collider>();
        Vector3 targetPos = playerCollider != null
            ? playerCollider.bounds.center
            : player.transform.position;

        Vector3 dirToPlayer = (targetPos - spotLight.transform.position).normalized;
        float angle = Vector3.Angle(spotLight.transform.forward, dirToPlayer);
        float distance = Vector3.Distance(spotLight.transform.position, targetPos);

        bool outOfRange = distance > detectionRange;
        bool outOfAngle = angle >= detectionAngle;
        bool blocked = false;

        if (!outOfRange && !outOfAngle)
        {
            if (Physics.Raycast(spotLight.transform.position, dirToPlayer,
                out RaycastHit rayHit, detectionRange))
                blocked = !HasPlayerTag(rayHit.transform);
        }

        if (outOfRange || outOfAngle || blocked) OnPlayerLost();
    }

    void OnPlayerDetected()
    {
        if (currentState == CCTVState.Detected) return;
        currentState = CCTVState.Detected;
        if (!audioSource.isPlaying) audioSource.Play();
    }

    void OnPlayerLost()
    {
        if (currentState == CCTVState.Rotating) return;
        currentState = CCTVState.Rotating;
        audioSource.Stop();
    }

    bool HasPlayerTag(Transform t)
    {
        while (t != null)
        {
            if (t.CompareTag("Player")) return true;
            t = t.parent;
        }
        return false;
    }

    void OnDrawGizmos()
    {
        if (spotLight == null) return;
        Vector3 origin = pivot != null ? pivot.position : transform.position;

        Color coneColor = (currentState == CCTVState.Detected)
            ? new Color(1f, 0f, 0f, 0.2f)
            : new Color(0f, 1f, 0f, 0.2f);

        DrawDetectionCone(coneColor, origin);

        Gizmos.color = new Color(1f, 1f, 0f, 0.1f);
        Gizmos.DrawWireSphere(origin, detectionRange);

        if (pivot != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(pivot.position, 0.1f);
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(spotLight.transform.position,
            spotLight.transform.position + spotLight.transform.forward * detectionRange);
    }

    void DrawDetectionCone(Color color, Vector3 origin)
    {
        Gizmos.color = color;
        Vector3 forward = spotLight.transform.forward;
        Vector3 up = spotLight.transform.up;

        int segments = 30;
        float angleStep = (detectionAngle * 2f) / segments;
        Vector3 prevPoint = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float a = -detectionAngle + angleStep * i;
            Vector3 dir = Quaternion.AngleAxis(a, up) * forward;
            Vector3 point = origin + dir * detectionRange;

            Gizmos.DrawLine(origin, point);
            if (i > 0) Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }
    }
}