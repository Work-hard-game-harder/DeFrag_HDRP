using UnityEngine;

namespace DeFrag.B1F
{
    /// <summary>
    /// Moves the cutscene camera between authored poses with handheld drift, trauma-based shake
    /// and a "knocked to the floor" drop. Pure presentation; reads only its own inputs.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CutsceneCameraRig : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float maxShakeAngle = 6f;
        [SerializeField, Min(0f)] private float maxShakeOffset = 0.12f;
        [SerializeField, Min(0.1f)] private float shakeFrequency = 22f;
        [SerializeField, Min(0.1f)] private float traumaRecovery = 1.4f;

        private Camera view;
        private Pose from;
        private Pose to;
        private float fovFrom;
        private float fovTo;
        private float shotStart;
        private float shotLength = 1f;
        private float handheld;
        private float trauma;
        private float seed;

        private bool dropping;
        private Pose dropRest;
        private float dropStart;
        private float dropLength;
        private float dropRoll;
        private float clock;

        public Camera View => view != null ? view : view = GetComponent<Camera>();

        private void Awake()
        {
            view = GetComponent<Camera>();
            seed = Random.value * 100f;
        }

        public void ResetClock()
        {
            clock = 0f;
            trauma = 0f;
            dropping = false;
        }

        public void Cut(Transform start, Transform end, float fovStart, float fovEnd, float length, float handheldAmount)
        {
            from = PoseOf(start);
            to = PoseOf(end != null ? end : start);
            fovFrom = fovStart;
            fovTo = fovEnd;
            shotStart = clock;
            shotLength = Mathf.Max(0.01f, length);
            handheld = handheldAmount;
            dropping = false;
            Apply();
        }

        public void AddTrauma(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        /// <summary>The camera is knocked down: it tumbles to rest, bouncing once, and stays tilted.</summary>
        public void Drop(Transform rest, float length, float roll)
        {
            if (rest == null) return;
            // Start the fall from wherever the shot currently is.
            from = new Pose(transform.position, transform.rotation);
            dropRest = PoseOf(rest);
            dropStart = clock;
            dropLength = Mathf.Max(0.05f, length);
            dropRoll = roll;
            dropping = true;
        }

        private static Pose PoseOf(Transform anchor) => anchor != null ? new Pose(anchor.position, anchor.rotation) : new Pose(Vector3.zero, Quaternion.identity);

        private void LateUpdate()
        {
            clock += Time.deltaTime;
            trauma = Mathf.Max(0f, trauma - traumaRecovery * Time.deltaTime);
            Apply();
        }

        private void Apply()
        {
            Vector3 position;
            Quaternion rotation;
            if (dropping)
            {
                float u = Mathf.Clamp01((clock - dropStart) / dropLength);
                float fall = Bounce(u);
                position = Vector3.LerpUnclamped(from.position, dropRest.position, fall);
                // Tumble: overshoot the roll, then settle on the rest tilt.
                float tumble = Mathf.Sin(u * Mathf.PI) * 35f * (1f - u);
                rotation = Quaternion.SlerpUnclamped(from.rotation, dropRest.rotation, Mathf.SmoothStep(0f, 1f, u)) *
                           Quaternion.Euler(tumble * 0.4f, tumble * 0.2f, dropRoll * Mathf.SmoothStep(0f, 1f, u) + tumble);
            }
            else
            {
                float u = Smoother(Mathf.Clamp01((clock - shotStart) / shotLength));
                position = Vector3.Lerp(from.position, to.position, u);
                rotation = Quaternion.Slerp(from.rotation, to.rotation, u);
                View.fieldOfView = Mathf.Lerp(fovFrom, fovTo, u);
            }

            float t = clock + seed;
            // Handheld: slow breathing drift.
            if (handheld > 0f)
            {
                rotation *= Quaternion.Euler(
                    (Mathf.PerlinNoise(t * 0.35f, 1.7f) - 0.5f) * 1.6f * handheld,
                    (Mathf.PerlinNoise(t * 0.3f, 5.1f) - 0.5f) * 2.0f * handheld,
                    (Mathf.PerlinNoise(t * 0.25f, 9.3f) - 0.5f) * 1.0f * handheld);
                position += new Vector3(Mathf.PerlinNoise(t * 0.4f, 3.3f) - 0.5f, Mathf.PerlinNoise(t * 0.5f, 7.7f) - 0.5f, 0f) * 0.02f * handheld;
            }
            // Impact shake grows with the square of trauma so small hits stay subtle.
            float shake = trauma * trauma;
            if (shake > 0f)
            {
                float f = t * shakeFrequency;
                rotation *= Quaternion.Euler(
                    (Mathf.PerlinNoise(f, 11f) - 0.5f) * 2f * maxShakeAngle * shake,
                    (Mathf.PerlinNoise(f, 23f) - 0.5f) * 2f * maxShakeAngle * shake,
                    (Mathf.PerlinNoise(f, 37f) - 0.5f) * 2f * maxShakeAngle * shake);
                position += rotation * new Vector3(Mathf.PerlinNoise(f, 41f) - 0.5f, Mathf.PerlinNoise(f, 53f) - 0.5f, 0f) * 2f * maxShakeOffset * shake;
            }
            transform.SetPositionAndRotation(position, rotation);
        }

        private static float Smoother(float u) => u * u * u * (u * (u * 6f - 15f) + 10f);

        // Gravity-like fall with one small bounce on landing.
        private static float Bounce(float u)
        {
            const float land = 0.62f;
            if (u < land) { float k = u / land; return k * k; }
            float b = (u - land) / (1f - land);
            return 1f - 0.08f * Mathf.Sin(b * Mathf.PI) * (1f - b);
        }
    }
}
