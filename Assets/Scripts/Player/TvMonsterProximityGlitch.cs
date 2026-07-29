using System.Collections;
using System.Collections.Generic;
using DeFrag.Rendering;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace DeFrag.Player
{
    [DisallowMultipleComponent]
    public sealed class TvMonsterProximityGlitch : MonoBehaviour
    {
        private const string UiShaderName = "DeFrag/UI/TvMonsterGlitchOverlay";

        [Header("Distance Response")]
        [Tooltip("The effect reaches full strength at this distance or closer.")]
        [Min(0f)] [SerializeField] private float maximumIntensityDistance = 10f;
        [Tooltip("Input is normalized proximity: 0 at Sound Detection Range, 1 at Maximum Intensity Distance.")]
        [SerializeField] private AnimationCurve intensityCurve = new(
            new Keyframe(0f, 0f, 0f, 0.25f),
            new Keyframe(0.55f, 0.18f, 0.35f, 1.1f),
            new Keyframe(1f, 1f, 2.2f, 0f));

        [Header("Glitch Appearance")]
        [Range(0f, 0.12f)] [SerializeField] private float maximumTearAmount = 0.045f;
        [Range(0f, 1f)] [SerializeField] private float maximumNoiseAmount = 0.42f;
        [Range(0f, 1f)] [SerializeField] private float uiNoiseAmount = 0.48f;

        [Header("Source Discovery")]
        [Min(0.1f)] [SerializeField] private float refreshInterval = 1f;

        private readonly List<MonsterAI> monsters = new();
        private NetworkObject networkObject;
        private Volume runtimeVolume;
        private VolumeProfile runtimeProfile;
        private TvMonsterGlitchPostProcess postProcess;
        private Material uiMaterial;
        private Canvas uiCanvas;
        private float refreshTimer;

        private bool IsLocalOwner => networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;

        private void Awake()
        {
            networkObject = GetComponent<NetworkObject>();
        }

        private IEnumerator Start()
        {
            // Wait one frame so NetworkObject ownership is valid before creating local-only presentation.
            yield return null;
            if (!IsLocalOwner)
            {
                enabled = false;
                yield break;
            }

            CreateRuntimeVolume();
            CreateUiOverlay();
            RefreshMonsterSources();
        }

        private void Update()
        {
            if (postProcess == null || !IsLocalOwner)
                return;

            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
                RefreshMonsterSources();

            float intensity = CalculateStrongestIntensity();
            postProcess.intensity.value = intensity;
            if (uiMaterial != null)
                uiMaterial.SetFloat("_Intensity", intensity);

            // TODO(Accessibility): Connect an eventual "reduced glitch" option here and scale
            // both post-process and UI intensity without changing gameplay or monster detection.
        }

        private float CalculateStrongestIntensity()
        {
            float strongest = 0f;
            for (int i = monsters.Count - 1; i >= 0; i--)
            {
                MonsterAI monster = monsters[i];
                if (monster == null || !monster.isActiveAndEnabled)
                {
                    monsters.RemoveAt(i);
                    continue;
                }

                float startDistance = Mathf.Max(0f, monster.soundDetectionRange);
                float distance = Vector3.Distance(transform.position, monster.transform.position);
                if (distance > startDistance)
                    continue;

                float fullStrengthDistance = Mathf.Min(maximumIntensityDistance, startDistance);
                float denominator = startDistance - fullStrengthDistance;
                float normalizedProximity = denominator > 0.001f
                    ? Mathf.InverseLerp(startDistance, fullStrengthDistance, distance)
                    : Mathf.InverseLerp(startDistance, 0f, distance);

                strongest = Mathf.Max(strongest, Mathf.Clamp01(intensityCurve.Evaluate(normalizedProximity)));
            }

            return strongest;
        }

        private void RefreshMonsterSources()
        {
            refreshTimer = refreshInterval;
            monsters.Clear();
            MonsterAI[] found = FindObjectsByType<MonsterAI>(FindObjectsInactive.Exclude);
            monsters.AddRange(found);
        }

        private void CreateRuntimeVolume()
        {
            GameObject volumeObject = new("Local Tv Monster Glitch", typeof(Volume));
            volumeObject.transform.SetParent(transform, false);

            runtimeVolume = volumeObject.GetComponent<Volume>();
            runtimeVolume.isGlobal = true;
            runtimeVolume.priority = 1100f;
            runtimeVolume.weight = 1f;

            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "Runtime Tv Monster Glitch";
            runtimeVolume.sharedProfile = runtimeProfile;

            postProcess = runtimeProfile.Add<TvMonsterGlitchPostProcess>(true);
            postProcess.intensity.overrideState = true;
            postProcess.tearAmount.overrideState = true;
            postProcess.noiseAmount.overrideState = true;
            postProcess.intensity.value = 0f;
            postProcess.tearAmount.value = maximumTearAmount;
            postProcess.noiseAmount.value = maximumNoiseAmount;
        }

        private void CreateUiOverlay()
        {
            Shader shader = Shader.Find(UiShaderName);
            if (shader == null)
            {
                Debug.LogError($"Tv Monster UI glitch shader not found: {UiShaderName}", this);
                return;
            }

            uiMaterial = new Material(shader) { name = "Runtime Tv Monster UI Glitch" };
            uiMaterial.SetFloat("_Intensity", 0f);
            uiMaterial.SetFloat("_NoiseAmount", uiNoiseAmount);

            GameObject canvasObject = new("Local Tv Monster UI Glitch", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            uiCanvas = canvasObject.GetComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.sortingOrder = short.MaxValue - 1;

            GameObject overlayObject = new("Visual Glitch Overlay", typeof(RectTransform), typeof(Image));
            overlayObject.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = overlayObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = overlayObject.GetComponent<Image>();
            image.color = Color.white;
            image.material = uiMaterial;
            image.raycastTarget = false;
        }

        private void OnDisable()
        {
            if (postProcess != null)
                postProcess.intensity.value = 0f;
            if (uiMaterial != null)
                uiMaterial.SetFloat("_Intensity", 0f);
        }

        private void OnDestroy()
        {
            if (runtimeVolume != null)
                Destroy(runtimeVolume.gameObject);
            if (runtimeProfile != null)
                Destroy(runtimeProfile);
            if (uiMaterial != null)
                Destroy(uiMaterial);
            if (uiCanvas != null)
                Destroy(uiCanvas.gameObject);
        }
    }

    internal static class TvMonsterProximityGlitchInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            GameObject installer = new("Tv Monster Glitch Installer");
            installer.hideFlags = HideFlags.HideAndDontSave;
            installer.AddComponent<InstallerBehaviour>();
        }

        private sealed class InstallerBehaviour : MonoBehaviour
        {
            private float nextScanTime;

            private void Update()
            {
                if (Time.unscaledTime < nextScanTime)
                    return;

                nextScanTime = Time.unscaledTime + 1f;
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    NetworkObject cameraOwner = mainCamera.GetComponentInParent<NetworkObject>();
                    bool isUsableLocalCamera = cameraOwner == null ||
                        (cameraOwner.IsSpawned && cameraOwner.IsOwner);
                    if (isUsableLocalCamera &&
                        mainCamera.GetComponent<TvMonsterProximityGlitch>() == null &&
                        mainCamera.GetComponentInParent<TvMonsterProximityGlitch>() == null)
                    {
                        mainCamera.gameObject.AddComponent<TvMonsterProximityGlitch>();
                        return;
                    }
                }

                // Fallback for a local player whose camera is enabled or tagged a little later.
                PlayerSprintVisuals[] players = FindObjectsByType<PlayerSprintVisuals>(
                    FindObjectsInactive.Exclude);
                foreach (PlayerSprintVisuals player in players)
                {
                    NetworkObject playerNetworkObject = player.GetComponent<NetworkObject>();
                    if (playerNetworkObject != null &&
                        (!playerNetworkObject.IsSpawned || !playerNetworkObject.IsOwner))
                        continue;

                    if (player.GetComponent<TvMonsterProximityGlitch>() == null)
                        player.gameObject.AddComponent<TvMonsterProximityGlitch>();
                }
            }
        }
    }
}
