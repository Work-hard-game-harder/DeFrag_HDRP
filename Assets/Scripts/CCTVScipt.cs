using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CCTVScript : MonoBehaviour
{
    [Header("Detection Setting")]
    [SerializeField] private Light spotLight;
    [SerializeField] private float detectionRange = 10f;
    [SerializeField] private float detectionAngle = 30f;

    [Header("Detection Performance")]
    [SerializeField] private LayerMask playerLayers;
    [SerializeField] private LayerMask lineOfSightLayers;
    [SerializeField, Min(0.02f)] private float detectionInterval = 0.15f;
    [SerializeField, Min(1)] private int candidateBufferSize = 32;
    [SerializeField, Min(1)] private int lineOfSightHitBufferSize = 32;
    [Tooltip("네트워크 플레이 중에는 서버/호스트만 순찰 로봇에게 감지 위치를 보고합니다.")]
    [SerializeField] private bool serverAuthoritativeRobotReporting = true;

    [Header("Rotation Setting")]
    [SerializeField] private Transform pivot;
    [SerializeField] private float rotationSpeed = 45f;
    [SerializeField] private float maxAngle = 90f;
    [SerializeField] private float pauseDuration = 2f;

    [Header("Sound Setting")]
    [SerializeField] private AudioSource audioSource;

    [Header("Alarm Visual")]
    [SerializeField] private Color normalLightColor = new Color(1f, 0.35f, 0f, 1f);
    [SerializeField] private Color alarmLightColor = Color.red;
    [Tooltip("발각 후 빨간불과 경보음을 유지하는 시간입니다.")]
    [SerializeField, Min(0.1f)] private float alarmDuration = 5f;

    private float startYAngle;
    private Quaternion startRotation;
    private float currentAngle = 0f;
    private bool isPaused = false;
    private float nextDetectionTime;
    private float alarmEndTime;

    private Collider[] candidateBuffer;
    private RaycastHit[] lineOfSightHitBuffer;
    private readonly HashSet<Transform> checkedPlayers = new HashSet<Transform>();
    private readonly List<Collider> playerColliderBuffer = new List<Collider>(16);

    private enum CCTVState { Rotating, Detected }
    private CCTVState currentState = CCTVState.Rotating;

    void Start()
    {
        EnsurePhysicsBuffers();

        Transform rotateTarget = pivot != null ? pivot : transform;
        startRotation = rotateTarget.localRotation;

        // 여러 CCTV의 물리 검사가 같은 프레임에 몰리지 않도록 첫 검사 시점을 분산합니다.
        float staggerPhase = Mathf.Repeat(
            transform.position.x * 0.1031f + transform.position.z * 0.11369f,
            1f);
        nextDetectionTime = Time.time + detectionInterval * staggerPhase;

        if (audioSource != null)
        {
            audioSource.loop = true;
            audioSource.Stop();
        }

        SetLightColor(normalLightColor);
    }

    void Update()
    {
        if (currentState == CCTVState.Rotating)
            RotateCCTV();

        if (Time.time < nextDetectionTime)
            return;

        nextDetectionTime = Time.time + detectionInterval;

        switch (currentState)
        {
            case CCTVState.Rotating:
                DetectPlayer();
                break;
            case CCTVState.Detected:
                UpdateAlarmDuration();
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
        EnsurePhysicsBuffers();

        int candidateCount = Physics.OverlapSphereNonAlloc(
            origin,
            GetBroadphaseRadius(range, halfAngle),
            candidateBuffer,
            playerLayers,
            QueryTriggerInteraction.Ignore);
        checkedPlayers.Clear();

        for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
        {
            Collider candidate = candidateBuffer[candidateIndex];
            if (candidate == null) continue;

            Transform playerRoot = FindPlayerRoot(candidate.transform);
            if (playerRoot == null || !checkedPlayers.Add(playerRoot)) continue;

            playerColliderBuffer.Clear();
            playerRoot.GetComponentsInChildren(false, playerColliderBuffer);
            foreach (Collider playerCollider in playerColliderBuffer)
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

        EnsurePhysicsBuffers();
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction / distance,
            lineOfSightHitBuffer,
            distance + 0.05f,
            lineOfSightLayers,
            QueryTriggerInteraction.Ignore);

        Collider nearestCollider = null;
        float nearestDistance = float.PositiveInfinity;
        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            RaycastHit hit = lineOfSightHitBuffer[hitIndex];
            if (hit.collider == null) continue;
            if (hit.collider.GetComponentInParent<CCTVScript>() == this) continue;
            if (hit.distance >= nearestDistance) continue;

            nearestDistance = hit.distance;
            nearestCollider = hit.collider;
        }

        return nearestCollider == null || HasPlayerTag(nearestCollider.transform);
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
        alarmEndTime = Time.time + alarmDuration;
        SetLightColor(alarmLightColor);
        if (audioSource != null && !audioSource.isPlaying) audioSource.Play();
        if (HasRobotReportingAuthority())
            ReportToNearestRobot(detectedPosition);
    }

    PatrolRobotAI ReportToNearestRobot(Vector3 detectedPosition)
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

        return nearestRobot;
    }

    private void UpdateAlarmDuration()
    {
        if (Time.time >= alarmEndTime)
            FinishAlarm();
    }

    private void FinishAlarm()
    {
        currentState = CCTVState.Rotating;
        SetLightColor(normalLightColor);
        if (audioSource != null) audioSource.Stop();
    }

    private void SetLightColor(Color color)
    {
        if (spotLight != null)
            spotLight.color = color;
    }

    void OnPlayerLost()
    {
        if (currentState == CCTVState.Rotating) return;
        FinishAlarm();
    }

    private static bool HasPlayerTag(Transform t)
    {
        while (t != null)
        {
            if (t.CompareTag("Player")) return true;
            t = t.parent;
        }
        return false;
    }

    private bool HasRobotReportingAuthority()
    {
        if (!serverAuthoritativeRobotReporting)
            return true;

        NetworkManager manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening || manager.IsServer;
    }

    private void EnsurePhysicsBuffers()
    {
        int safeCandidateSize = Mathf.Max(1, candidateBufferSize);
        if (candidateBuffer == null || candidateBuffer.Length != safeCandidateSize)
            candidateBuffer = new Collider[safeCandidateSize];

        int safeHitSize = Mathf.Max(1, lineOfSightHitBufferSize);
        if (lineOfSightHitBuffer == null || lineOfSightHitBuffer.Length != safeHitSize)
            lineOfSightHitBuffer = new RaycastHit[safeHitSize];
    }

    private void OnValidate()
    {
        detectionRange = Mathf.Max(0f, detectionRange);
        detectionAngle = Mathf.Clamp(detectionAngle, 0f, 89.9f);
        detectionInterval = Mathf.Max(0.02f, detectionInterval);
        candidateBufferSize = Mathf.Max(1, candidateBufferSize);
        lineOfSightHitBufferSize = Mathf.Max(1, lineOfSightHitBufferSize);
        alarmDuration = Mathf.Max(0.1f, alarmDuration);
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
