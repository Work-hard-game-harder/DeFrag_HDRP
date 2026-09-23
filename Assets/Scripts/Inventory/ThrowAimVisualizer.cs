using System.Collections.Generic;
using UnityEngine;

/// <summary>Local-only predicted ballistic path and landing marker.</summary>
public sealed class ThrowAimVisualizer : MonoBehaviour
{
    [SerializeField, Min(8)] private int pointCount = 28;
    [SerializeField, Min(0.01f)] private float simulationStep = 0.075f;
    [SerializeField, Min(0.005f)] private float pointSize = 0.035f;
    [SerializeField, Min(0f)] private float pointMotionSpeed = 5f;
    [SerializeField, Min(0.02f)] private float landingMarkerRadius = 0.22f;
    [SerializeField] private Color trajectoryColor = new(0.25f, 0.9f, 1f, 0.9f);
    [SerializeField] private Color blockedColor = new(1f, 0.3f, 0.2f, 0.95f);

    private readonly List<Transform> points = new();
    private LineRenderer landingMarker;
    private Material material;
    private Collider[] playerColliders;
    private LayerMask blockingLayers;

    public void Configure(Collider[] ownerColliders, LayerMask layers)
    {
        playerColliders = ownerColliders;
        blockingLayers = layers;
        EnsureVisuals();
        Hide();
    }

    public void Show(Vector3 origin, Vector3 velocity)
    {
        EnsureVisuals();
        if (!enabled || landingMarker == null || points.Count < pointCount) return;
        float phase = Mathf.Repeat(Time.time * pointMotionSpeed, 1f);
        Vector3 previous = origin;
        bool hitFound = false;
        RaycastHit landingHit = default;
        int visibleCount = 0;

        for (int i = 0; i < pointCount; i++)
        {
            float time = (i + phase) * simulationStep;
            Vector3 position = origin + velocity * time + 0.5f * Physics.gravity * time * time;
            Vector3 segment = position - previous;

            if (TryRaycast(previous, segment, out landingHit))
            {
                position = landingHit.point;
                hitFound = true;
            }

            Transform point = points[i];
            point.gameObject.SetActive(true);
            point.position = position;
            visibleCount = i + 1;
            previous = position;
            if (hitFound) break;
        }

        for (int i = visibleCount; i < points.Count; i++)
            points[i].gameObject.SetActive(false);

        landingMarker.gameObject.SetActive(hitFound);
        if (hitFound) DrawLandingMarker(landingHit);
    }

    public void Hide()
    {
        foreach (Transform point in points)
            if (point != null) point.gameObject.SetActive(false);
        if (landingMarker != null) landingMarker.gameObject.SetActive(false);
    }

    private bool TryRaycast(Vector3 origin, Vector3 segment, out RaycastHit closestHit)
    {
        closestHit = default;
        float distance = segment.magnitude;
        if (distance <= Mathf.Epsilon) return false;

        RaycastHit[] hits = Physics.RaycastAll(origin, segment / distance, distance,
            blockingLayers, QueryTriggerInteraction.Ignore);
        float closestDistance = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (IsOwnerCollider(hit.collider) || hit.distance >= closestDistance) continue;
            closestDistance = hit.distance;
            closestHit = hit;
        }
        return closestDistance < float.MaxValue;
    }

    private bool IsOwnerCollider(Collider candidate)
    {
        if (candidate == null || playerColliders == null) return false;
        foreach (Collider playerCollider in playerColliders)
            if (candidate == playerCollider) return true;
        return false;
    }

    private void DrawLandingMarker(RaycastHit hit)
    {
        const int segments = 32;
        landingMarker.positionCount = segments + 1;
        Vector3 normal = hit.normal.normalized;
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.Cross(normal, Vector3.right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
        Vector3 center = hit.point + normal * 0.015f;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            landingMarker.SetPosition(i, center +
                (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * landingMarkerRadius);
        }

        Color color = hit.normal.y >= 0.45f ? trajectoryColor : blockedColor;
        landingMarker.startColor = color;
        landingMarker.endColor = color;
    }

    private void EnsureVisuals()
    {
        if (material == null)
        {
            Shader shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[Throw Aim] 사용할 수 있는 Unlit 셰이더가 없습니다.", this);
                enabled = false;
                return;
            }
            material = new Material(shader) { name = "Runtime Throw Aim Material" };
            material.color = trajectoryColor;
        }

        while (points.Count < pointCount)
        {
            GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = $"Throw Trajectory Point {points.Count + 1}";
            point.transform.SetParent(transform, true);
            point.transform.localScale = Vector3.one * pointSize;
            Destroy(point.GetComponent<Collider>());
            point.GetComponent<Renderer>().sharedMaterial = material;
            points.Add(point.transform);
        }

        if (landingMarker == null)
        {
            GameObject marker = new("Throw Landing Marker");
            marker.transform.SetParent(transform, true);
            landingMarker = marker.AddComponent<LineRenderer>();
            landingMarker.useWorldSpace = true;
            landingMarker.loop = false;
            landingMarker.widthMultiplier = pointSize;
            landingMarker.sharedMaterial = material;
            landingMarker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            landingMarker.receiveShadows = false;
        }
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
