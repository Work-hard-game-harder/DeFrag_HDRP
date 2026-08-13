using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DeFrag.Player
{
    /// <summary>
    /// Owns the local-only post-processing presentation used when a Ghost touches this player.
    /// The server selects the affected player, while only that player's owning client renders it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class VisionBlurEffect : NetworkBehaviour
    {
        [Header("Timing")]
        [Tooltip("The complete effect window. Fade In + Fade Out is kept within this value.")]
        [Min(0.01f)]
        [SerializeField] private float totalDuration = 5f;
        [Min(0f)]
        [SerializeField] private float fadeInDuration = 2f;
        [Min(0f)]
        [SerializeField] private float fadeOutDuration = 3f;
        [SerializeField] private AnimationCurve fadeCurve = new(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));
        [Range(0f, 1f)]
        [SerializeField] private float maxWeight = 1f;

        [Header("Blur Appearance")]
        [Range(0f, 1f)]
        [SerializeField] private float blurIntensity = 1f;
        [Range(0f, 0.04f)]
        [SerializeField] private float blurRadius = 0.035f;
        [Range(0f, 1f)]
        [SerializeField] private float vignetteIntensity = 0.65f;
        [Range(0f, 1f)]
        [SerializeField] private float vignetteSmoothness = 0.9f;
        [SerializeField] private Color fogColor = new(0.64f, 0.67f, 0.7f, 1f);
        [Range(-100f, 100f)]
        [SerializeField] private float saturation = -55f;
        [SerializeField] private float postExposure = -0.35f;
        [SerializeField] private float volumePriority = 1200f;

        private Volume runtimeVolume;
        private VolumeProfile runtimeProfile;
        private Coroutine effectRoutine;

        /// <summary>
        /// Called by the server-side Ghost contact logic. In a network session the request is
        /// delivered only to this player object's owning client.
        /// </summary>
        public void TriggerVisionBlock()
        {
            if (!IsSpawned)
            {
                StartLocalEffect();
                return;
            }

            if (!IsServer)
            {
                if (IsOwner)
                    StartLocalEffect();
                return;
            }

            TriggerVisionBlockClientRpc(CreateOwnerTarget());
        }

        [ClientRpc]
        private void TriggerVisionBlockClientRpc(ClientRpcParams rpcParams = default)
        {
            if (IsOwner)
                StartLocalEffect();
        }

        private void StartLocalEffect()
        {
            if (IsSpawned && !IsOwner)
                return;

            EnsureRuntimeVolume();
            if (runtimeVolume == null)
                return;

            if (effectRoutine != null)
                StopCoroutine(effectRoutine);

            runtimeVolume.weight = 0f;
            effectRoutine = StartCoroutine(PlayEffect());
        }

        private IEnumerator PlayEffect()
        {
            GetValidatedDurations(out float fadeIn, out float hold, out float fadeOut);

            yield return FadeWeight(0f, maxWeight, fadeIn);

            if (hold > 0f)
                yield return new WaitForSeconds(hold);

            yield return FadeWeight(maxWeight, 0f, fadeOut);
            runtimeVolume.weight = 0f;
            effectRoutine = null;
        }

        private IEnumerator FadeWeight(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                runtimeVolume.weight = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / duration);
                float curvedTime = Mathf.Clamp01(fadeCurve.Evaluate(normalizedTime));
                runtimeVolume.weight = Mathf.LerpUnclamped(from, to, curvedTime);
                yield return null;
            }

            runtimeVolume.weight = to;
        }

        private void EnsureRuntimeVolume()
        {
            if (runtimeVolume != null)
                return;

            GameObject volumeObject = new("Local Ghost Vision Block", typeof(Volume));
            volumeObject.transform.SetParent(transform, false);

            runtimeVolume = volumeObject.GetComponent<Volume>();
            runtimeVolume.isGlobal = true;
            runtimeVolume.priority = volumePriority;
            runtimeVolume.weight = 0f;

            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "Runtime Ghost Vision Block";
            runtimeVolume.sharedProfile = runtimeProfile;

            SprintEdgeBlur blur = runtimeProfile.Add<SprintEdgeBlur>(true);
            blur.intensity.overrideState = true;
            blur.edgeStart.overrideState = true;
            blur.blurRadius.overrideState = true;
            blur.fullScreenBlend.overrideState = true;
            blur.intensity.value = blurIntensity;
            blur.edgeStart.value = 0.05f;
            blur.blurRadius.value = blurRadius;
            blur.fullScreenBlend.value = 1f;

            Vignette vignette = runtimeProfile.Add<Vignette>(true);
            vignette.color.overrideState = true;
            vignette.intensity.overrideState = true;
            vignette.smoothness.overrideState = true;
            vignette.color.value = fogColor;
            vignette.intensity.value = vignetteIntensity;
            vignette.smoothness.value = vignetteSmoothness;

            ColorAdjustments colorAdjustments = runtimeProfile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.postExposure.value = postExposure;
            colorAdjustments.saturation.value = saturation;
        }

        private void GetValidatedDurations(out float fadeIn, out float hold, out float fadeOut)
        {
            float duration = Mathf.Max(0.01f, totalDuration);
            fadeIn = Mathf.Max(0f, fadeInDuration);
            fadeOut = Mathf.Max(0f, fadeOutDuration);
            float fadeTotal = fadeIn + fadeOut;

            if (fadeTotal > duration)
            {
                float scale = duration / fadeTotal;
                fadeIn *= scale;
                fadeOut *= scale;
                hold = 0f;
                return;
            }

            hold = duration - fadeTotal;
        }

        private ClientRpcParams CreateOwnerTarget()
        {
            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId }
                }
            };
        }

        private void OnDisable()
        {
            if (effectRoutine != null)
            {
                StopCoroutine(effectRoutine);
                effectRoutine = null;
            }

            if (runtimeVolume != null)
                runtimeVolume.weight = 0f;
        }

        public override void OnDestroy()
        {
            if (runtimeVolume != null)
                Destroy(runtimeVolume.gameObject);
            if (runtimeProfile != null)
                Destroy(runtimeProfile);

            base.OnDestroy();
        }

        private void OnValidate()
        {
            totalDuration = Mathf.Max(0.01f, totalDuration);
            fadeInDuration = Mathf.Max(0f, fadeInDuration);
            fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
            maxWeight = Mathf.Clamp01(maxWeight);

            float fadeTotal = fadeInDuration + fadeOutDuration;
            if (fadeTotal > totalDuration && fadeTotal > 0f)
            {
                float scale = totalDuration / fadeTotal;
                fadeInDuration *= scale;
                fadeOutDuration *= scale;
            }
        }
    }
}
