using DeFrag.Monsters.Common;
using UnityEngine;

namespace DeFrag.Monsters.B2F
{
    /// <summary>
    /// Receives gameplay noise events and keeps only a pending position that this B2F monster
    /// can physically hear. The Behavior Tree consumes the position and owns all movement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class B2FWorldNoisePerception : MonoBehaviour
    {
        [Header("World Noise Detection")]
        [SerializeField] private bool ignoreHeightDifference;
        [SerializeField] private bool reactToUrgentNoise = true;

        private bool hasPendingNoise;
        private Vector3 pendingNoisePosition;

        public Vector3 LastHeardPosition { get; private set; }
        public float LastHeardRadius { get; private set; }

        private void OnEnable()
        {
            WorldNoiseSystem.NoiseEmitted += OnNoiseEmitted;
            if (reactToUrgentNoise)
                WorldNoiseSystem.UrgentNoiseEmitted += OnNoiseEmitted;
        }

        private void OnDisable()
        {
            WorldNoiseSystem.NoiseEmitted -= OnNoiseEmitted;
            WorldNoiseSystem.UrgentNoiseEmitted -= OnNoiseEmitted;
            hasPendingNoise = false;
        }

        public bool TryConsumeNoise(out Vector3 position)
        {
            position = pendingNoisePosition;
            if (!hasPendingNoise)
                return false;

            hasPendingNoise = false;
            return true;
        }

        private void OnNoiseEmitted(Vector3 position, float radius)
        {
            if (!Application.isPlaying || !MonsterSimulationAuthority.HasServerAuthority())
                return;

            Vector3 offset = position - transform.position;
            if (ignoreHeightDifference)
                offset.y = 0f;

            float safeRadius = Mathf.Max(0f, radius);
            if (offset.sqrMagnitude > safeRadius * safeRadius)
                return;

            // A newer valid impact replaces the previous pending stimulus. This lets a monster
            // already investigating redirect to the most recently heard collision.
            pendingNoisePosition = position;
            LastHeardPosition = position;
            LastHeardRadius = safeRadius;
            hasPendingNoise = true;
        }
    }
}
