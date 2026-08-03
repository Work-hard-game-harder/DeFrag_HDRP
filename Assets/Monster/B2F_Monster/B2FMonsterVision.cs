using DeFrag.Monsters.Common;
using UnityEngine;

namespace DeFrag.Monsters.B2F
{
    /// <summary>
    /// Authoritative visual-perception settings shared by the B2F behavior task and Scene gizmo.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class B2FMonsterVision : MonoBehaviour
    {
        [Header("Vision Detection")]
        [SerializeField, Min(0f)] private float viewDistance = 20f;
        [SerializeField, Range(0f, 360f)] private float fieldOfView = 120f;
        [SerializeField] private LayerMask obstacleMask;
        [SerializeField] private float eyeHeight = 1.5f;
        [SerializeField] private float targetHeightOffset = 1f;

        [Header("Debug Gizmo")]
        [SerializeField] private bool showVisionGizmo = true;
        [SerializeField] private Color visionColor = new Color(1f, 0.85f, 0.1f, 0.8f);
        [SerializeField, Range(2, 32)] private int arcSegments = 16;

        public float ViewDistance => viewDistance;
        public float FieldOfView => fieldOfView;
        public LayerMask ObstacleMask => obstacleMask;
        public float EyeHeight => eyeHeight;
        public float TargetHeightOffset => targetHeightOffset;

        public bool CanSee(Transform target)
        {
            return MonsterPerceptionUtility.CanSeeTarget(
                transform,
                target,
                viewDistance,
                fieldOfView,
                obstacleMask,
                eyeHeight,
                targetHeightOffset);
        }

        private void OnValidate()
        {
            viewDistance = Mathf.Max(0f, viewDistance);
            fieldOfView = Mathf.Clamp(fieldOfView, 0f, 360f);
            arcSegments = Mathf.Clamp(arcSegments, 2, 32);
        }

        private void OnDrawGizmosSelected()
        {
            if (!showVisionGizmo || viewDistance <= 0f || fieldOfView <= 0f)
                return;

            Vector3 origin = transform.position + Vector3.up * eyeHeight;
            float halfAngle = fieldOfView * 0.5f;
            Vector3 leftDirection = Quaternion.AngleAxis(-halfAngle, Vector3.up) * transform.forward;
            Vector3 rightDirection = Quaternion.AngleAxis(halfAngle, Vector3.up) * transform.forward;

            Gizmos.color = visionColor;
            Gizmos.DrawLine(origin, origin + leftDirection * viewDistance);
            Gizmos.DrawLine(origin, origin + rightDirection * viewDistance);

            Vector3 previousPoint = origin + leftDirection * viewDistance;
            int segments = Mathf.Max(2, arcSegments);
            for (int i = 1; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-halfAngle, halfAngle, i / (float)segments);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * transform.forward;
                Vector3 nextPoint = origin + direction * viewDistance;
                Gizmos.DrawLine(previousPoint, nextPoint);
                previousPoint = nextPoint;
            }

#if UNITY_EDITOR
            UnityEditor.Handles.color = visionColor;
            UnityEditor.Handles.Label(
                origin + transform.forward * Mathf.Min(2f, viewDistance),
                $"B2F Vision ({fieldOfView:0}° / {viewDistance:0.0}m)");
#endif
        }
    }
}
