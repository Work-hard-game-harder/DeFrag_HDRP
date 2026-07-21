using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.Player
{
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class PlayerLocalFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private PlayerStats playerStats;

        [Header("Damage Flash")]
        [SerializeField] private Color damageColor = new Color(0.65f, 0f, 0f, 0.38f);
        [Min(0.01f)] [SerializeField] private float damageFlashDuration = 0.25f;

        [Header("Damage Shake")]
        [Min(0f)] [SerializeField] private float damageShakeDuration = 0.3f;
        [Min(0f)] [SerializeField] private float damagePositionAmplitude = 0.045f;
        [Min(0f)] [SerializeField] private float damageRotationAmplitude = 1.5f;
        [Min(0f)] [SerializeField] private float damageShakeFrequency = 24f;

        [Header("Exhaustion Breathing")]
        [Min(0.01f)] [SerializeField] private float breathingDuration = 3f;
        [Min(1)] [SerializeField] private int breathingCycles = 3;
        [Min(0f)] [SerializeField] private float breathingAmplitude = 0.04f;

        private NetworkObject networkObject;
        private PlayerStamina stamina;
        private CanvasGroup damageOverlay;
        private Coroutine damageRoutine;
        private Coroutine breathingRoutine;
        private Vector3 cameraBaseLocalPosition;
        private int observedHealth;
        private bool hasObservedHealth;

        private bool IsLocalOwner => networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;

        private void Awake()
        {
            networkObject = GetComponent<NetworkObject>();
            stamina = GetComponent<PlayerStamina>();
            playerStats = playerStats != null ? playerStats : GetComponent<PlayerStats>();
            playerCamera = playerCamera != null ? playerCamera : GetComponentInChildren<Camera>(true);

            if (playerCamera != null)
                cameraBaseLocalPosition = playerCamera.transform.localPosition;
        }

        private void OnEnable()
        {
            if (stamina != null)
                stamina.Exhausted += PlayExhaustionFeedback;
        }

        private IEnumerator Start()
        {
            yield return null;

            if (playerStats != null)
            {
                observedHealth = playerStats.Health;
                hasObservedHealth = true;
            }

            if (IsLocalOwner)
                CreateDamageOverlay();
        }

        private void Update()
        {
            if (!IsLocalOwner || playerStats == null)
                return;

            if (!hasObservedHealth)
            {
                observedHealth = playerStats.Health;
                hasObservedHealth = true;
                return;
            }

            if (playerStats.Health < observedHealth)
                PlayDamageFeedback();

            observedHealth = playerStats.Health;
        }

        private void OnDisable()
        {
            if (stamina != null)
                stamina.Exhausted -= PlayExhaustionFeedback;

            ResetCameraOffset();
        }

        /// <summary>
        /// Local presentation hook for the networking layer after authoritative damage is confirmed.
        /// </summary>
        public void PlayDamageFeedback()
        {
            if (!IsLocalOwner)
                return;

            CreateDamageOverlay();
            RestartCoroutine(ref damageRoutine, DamageFeedbackRoutine());
        }

        public void PlayExhaustionFeedback()
        {
            if (!IsLocalOwner || playerCamera == null)
                return;

            RestartCoroutine(ref breathingRoutine, BreathingRoutine());
        }

        private IEnumerator DamageFeedbackRoutine()
        {
            float elapsed = 0f;
            float totalDuration = Mathf.Max(damageFlashDuration, damageShakeDuration);
            while (elapsed < totalDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / damageFlashDuration);
                if (damageOverlay != null)
                    damageOverlay.alpha = Mathf.Sin(progress * Mathf.PI);

                ApplyDamageShake(elapsed);
                yield return null;
            }

            if (damageOverlay != null)
                damageOverlay.alpha = 0f;

            ResetCameraOffset();
            damageRoutine = null;
        }

        private IEnumerator BreathingRoutine()
        {
            float elapsed = 0f;
            while (elapsed < breathingDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / breathingDuration);
                float envelope = Mathf.Sin(progress * Mathf.PI);
                float wave = Mathf.Sin(progress * Mathf.PI * 2f * breathingCycles);
                SetCameraPositionOffset(Vector3.up * (wave * envelope * breathingAmplitude));
                yield return null;
            }

            ResetCameraOffset();
            breathingRoutine = null;
        }

        private void ApplyDamageShake(float elapsed)
        {
            if (playerCamera == null || elapsed > damageShakeDuration)
                return;

            float fade = 1f - Mathf.Clamp01(elapsed / damageShakeDuration);
            float noiseX = Mathf.PerlinNoise(elapsed * damageShakeFrequency, 0.17f) * 2f - 1f;
            float noiseY = Mathf.PerlinNoise(0.73f, elapsed * damageShakeFrequency) * 2f - 1f;
            SetCameraPositionOffset(new Vector3(noiseX, noiseY, 0f) * damagePositionAmplitude * fade);
            playerCamera.transform.localRotation *= Quaternion.Euler(noiseY * damageRotationAmplitude * fade, 0f, noiseX * damageRotationAmplitude * fade);
        }

        private void CreateDamageOverlay()
        {
            if (damageOverlay != null || !IsLocalOwner)
                return;

            GameObject canvasObject = new GameObject("LocalDamageOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            damageOverlay = canvasObject.GetComponent<CanvasGroup>();
            damageOverlay.alpha = 0f;
            damageOverlay.blocksRaycasts = false;
            damageOverlay.interactable = false;

            GameObject imageObject = new GameObject("DamageTint", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);
            RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            Image image = imageObject.GetComponent<Image>();
            image.color = damageColor;
            image.raycastTarget = false;
        }

        private void RestartCoroutine(ref Coroutine runningCoroutine, IEnumerator routine)
        {
            if (runningCoroutine != null)
                StopCoroutine(runningCoroutine);

            runningCoroutine = StartCoroutine(routine);
        }

        private void SetCameraPositionOffset(Vector3 offset)
        {
            if (playerCamera != null)
                playerCamera.transform.localPosition = cameraBaseLocalPosition + offset;
        }

        private void ResetCameraOffset()
        {
            SetCameraPositionOffset(Vector3.zero);
            if (damageOverlay != null)
                damageOverlay.alpha = 0f;
        }
    }
}
