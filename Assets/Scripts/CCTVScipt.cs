using System.Collections;
using UnityEngine;
using System.Collections.Generic;

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
        if (TryFindVisiblePlayer(out Vector3 detectedPosition))
            OnPlayerDetected(detectedPosition);
    }

    void CheckPlayerLeft()
    {
        if (!TryFindVisiblePlayer(out _))
            OnPlayerLost();
    }

    private bool TryFindVisiblePlayer(out Vector3 detectedPosition)
    {
        detectedPosition = Vector3.zero;
        if (spotLight == null || !spotLight.enabled || !spotLight.gameObject.activeInHierarchy)
            return false;

        Vector3 origin = spotLight.transform.position;
        Vector3 forward = spotLight.transform.forward.normalized;
        float range = GetDetectionRange();
        float halfAngle = GetDetectionHalfAngle();
        Collider[] candidates = Physics.OverlapSphere(
            origin,
            GetBroadphaseRadius(range, halfAngle),
            ~0,
            QueryTriggerInteraction.Ignore);
        HashSet<Transform> checkedPlayers = new HashSet<Transform>();

        foreach (Collider candidate in candidates)
        {
            Transform playerRoot = FindPlayerRoot(candidate.transform);
            if (playerRoot == null || !checkedPlayers.Add(playerRoot)) continue;

            Collider[] playerColliders = playerRoot.GetComponentsInChildren<Collider>(false);
            foreach (Collider playerCollider in playerColliders)
            {
                if (!playerCollider.enabled || playerCollider.isTrigger) continue;
                if (!TryGetConeIntersectionPoint(
                        playerCollider, origin, forward, range, halfAngle, out Vector3 targetPoint))
                    continue;
                if (!HasClearLineOfSight(origin, targetPoint)) continue;

                detectedPosition = playerCollider.bounds.center;
                return true;
            }
        }

        return false;
    }

    private bool TryGetConeIntersectionPoint(
        Collider target,
        Vector3 origin,
        Vector3 forward,
        float range,
        float halfAngle,
        out Vector3 intersectionPoint)
    {
        Bounds bounds = target.bounds;
        Vector3 toCenter = bounds.center - origin;
        float centerDepth = Vector3.Dot(toCenter, forward);
        float depthExtent = Mathf.Abs(forward.x) * bounds.extents.x +
                            Mathf.Abs(forward.y) * bounds.extents.y +
                            Mathf.Abs(forward.z) * bounds.extents.z;

        float nearDepth = Mathf.Clamp(centerDepth - depthExtent, 0f, range);
        float middleDepth = Mathf.Clamp(centerDepth, 0f, range);
        float farDepth = Mathf.Clamp(centerDepth + depthExtent, 0f, range);

        if (TryPointAtDepth(nearDepth, target, origin, forward, range, halfAngle, out intersectionPoint) ||
            TryPointAtDepth(middleDepth, target, origin, forward, range, halfAngle, out intersectionPoint) ||
            TryPointAtDepth(farDepth, target, origin, forward, range, halfAngle, out intersectionPoint))
            return true;

        intersectionPoint = Vector3.zero;
        return false;
    }

    private bool TryPointAtDepth(
        float depth,
        Collider target,
        Vector3 origin,
        Vector3 forward,
        float range,
        float halfAngle,
        out Vector3 point)
    {
        Vector3 axisPoint = origin + forward * depth;
        point = target.ClosestPoint(axisPoint);
        return IsPointInsideLightVolume(point, origin, forward, range, halfAngle);
    }

    private bool IsPointInsideLightVolume(
        Vector3 point,
        Vector3 origin,
        Vector3 forward,
        float range,
        float halfAngle)
    {
        Vector3 localPoint = spotLight.transform.InverseTransformPoint(point);
        if (localPoint.z < 0f) return false;

        if (spotLight.type == LightType.Box)
        {
            Vector2 size = spotLight.areaSize;
            return localPoint.z <= range &&
                   Mathf.Abs(localPoint.x) <= size.x * 0.5f &&
                   Mathf.Abs(localPoint.y) <= size.y * 0.5f;
        }

        if (spotLight.type == LightType.Pyramid)
        {
            if (localPoint.z > range) return false;

            GetPyramidHalfExtents(localPoint.z, halfAngle, out float halfWidth, out float halfHeight);
            return Mathf.Abs(localPoint.x) <= halfWidth &&
                   Mathf.Abs(localPoint.y) <= halfHeight;
        }

        Vector3 offset = point - origin;
        float axialDistance = Vector3.Dot(offset, forward);
        if (axialDistance < 0f || offset.sqrMagnitude > range * range) return false;

        float radialSqr = Mathf.Max(0f, offset.sqrMagnitude - axialDistance * axialDistance);
        float coneRadius = axialDistance * Mathf.Tan(halfAngle * Mathf.Deg2Rad);
        return radialSqr <= coneRadius * coneRadius;
    }

    private bool HasClearLineOfSight(Vector3 origin, Vector3 targetPoint)
    {
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;
        if (distance <= Mathf.Epsilon) return true;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction / distance,
            distance + 0.05f,
            ~0,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.GetComponentInParent<CCTVScript>() == this) continue;
            return HasPlayerTag(hit.transform);
        }

        return true;
    }

    private static Transform FindPlayerRoot(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            if (current.CompareTag("Player")) return current;
            current = current.parent;
        }
        return null;
    }

    private float GetDetectionRange()
    {
        return spotLight != null && IsSupportedSpotShape(spotLight.type)
            ? Mathf.Max(0f, spotLight.range)
            : Mathf.Max(0f, detectionRange);
    }

    private float GetDetectionHalfAngle()
    {
        return spotLight != null && (spotLight.type == LightType.Spot || spotLight.type == LightType.Pyramid)
            ? Mathf.Clamp(spotLight.spotAngle * 0.5f, 0f, 89.9f)
            : Mathf.Clamp(detectionAngle, 0f, 89.9f);
    }

    private float GetBroadphaseRadius(float range, float halfAngle)
    {
        if (spotLight.type == LightType.Box)
        {
            Vector2 halfSize = spotLight.areaSize * 0.5f;
            return Mathf.Sqrt(range * range + halfSize.x * halfSize.x + halfSize.y * halfSize.y);
        }

        if (spotLight.type == LightType.Pyramid)
        {
            GetPyramidHalfExtents(range, halfAngle, out float halfWidth, out float halfHeight);
            return Mathf.Sqrt(range * range + halfWidth * halfWidth + halfHeight * halfHeight);
        }

        return range;
    }

    private void GetPyramidHalfExtents(float depth, float halfAngle, out float halfWidth, out float halfHeight)
    {
        float baseHalfExtent = depth * Mathf.Tan(halfAngle * Mathf.Deg2Rad);
        float outerTangent = Mathf.Tan(spotLight.spotAngle * 0.5f * Mathf.Deg2Rad);
        float innerTangent = Mathf.Tan(spotLight.innerSpotAngle * 0.5f * Mathf.Deg2Rad);
        float aspectRatio = outerTangent > 0.0001f
            ? Mathf.Max(0.0001f, innerTangent / outerTangent)
            : 1f;

        if (aspectRatio >= 1f)
        {
            halfHeight = baseHalfExtent;
            halfWidth = baseHalfExtent * aspectRatio;
        }
        else
        {
            halfWidth = baseHalfExtent;
            halfHeight = baseHalfExtent / aspectRatio;
        }
    }

    private static bool IsSupportedSpotShape(LightType lightType)
    {
        return lightType == LightType.Spot ||
               lightType == LightType.Pyramid ||
               lightType == LightType.Box;
    }

    void OnPlayerDetected(Vector3 detectedPosition)
    {
        if (currentState == CCTVState.Detected) return;
        currentState = CCTVState.Detected;
        if (!audioSource.isPlaying) audioSource.Play();
        ReportToNearestRobot(detectedPosition);
    }

    void ReportToNearestRobot(Vector3 detectedPosition)
    {
        PatrolRobotAI[] robots = FindObjectsByType<PatrolRobotAI>(FindObjectsInactive.Exclude);

        PatrolRobotAI nearestRobot = null;
        float nearestSqrDistance = float.MaxValue;

        foreach (PatrolRobotAI robot in robots)
        {
            if (robot == null || !robot.enabled || !robot.gameObject.activeInHierarchy)
                continue;

            float sqrDistance = (robot.transform.position - detectedPosition).sqrMagnitude;
            if (sqrDistance >= nearestSqrDistance) continue;

            nearestSqrDistance = sqrDistance;
            nearestRobot = robot;
        }

        if (nearestRobot != null)
        {
            nearestRobot.ReceiveCCTVReport(detectedPosition);
            Debug.Log($"[CCTV] {nearestRobot.name} is responding to the alarm.");
        }
        else
        {
            Debug.LogWarning("[CCTV] No active patrol robot can respond to the alarm.");
        }
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
        Vector3 origin = spotLight.transform.position;
        float range = GetDetectionRange();

        Color coneColor = (currentState == CCTVState.Detected)
            ? new Color(1f, 0f, 0f, 0.2f)
            : new Color(0f, 1f, 0f, 0.2f);

        DrawDetectionCone(coneColor, origin);

        Gizmos.color = new Color(1f, 1f, 0f, 0.1f);
        Gizmos.DrawWireSphere(origin, range);

        if (pivot != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(pivot.position, 0.1f);
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(spotLight.transform.position,
            spotLight.transform.position + spotLight.transform.forward * range);
    }

    void DrawDetectionCone(Color color, Vector3 origin)
    {
        Gizmos.color = color;
        Vector3 forward = spotLight.transform.forward;
        Vector3 up = spotLight.transform.up;
        float halfAngle = GetDetectionHalfAngle();
        float range = GetDetectionRange();

        int segments = 30;
        float angleStep = (halfAngle * 2f) / segments;
        Vector3 prevPoint = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float a = -halfAngle + angleStep * i;
            Vector3 dir = Quaternion.AngleAxis(a, up) * forward;
            Vector3 point = origin + dir * range;

            Gizmos.DrawLine(origin, point);
            if (i > 0) Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }
    }
}
