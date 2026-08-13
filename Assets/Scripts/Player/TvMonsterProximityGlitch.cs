using System.Collections;
using System.Collections.Generic;
using DeFrag.Rendering;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeFrag.Player
{
    [DisallowMultipleComponent]
    public sealed class TvMonsterProximityGlitch : MonoBehaviour
    {
        [Header("Distance Response")]
        [Tooltip("The effect reaches full strength at this distance or closer.")]
        [Min(0f)] [SerializeField] private float maximumIntensityDistance = 10f;
        [Tooltip("Input is normalized proximity: 0 at Sound Detection Range, 1 at Maximum Intensity Distance.")]
        [SerializeField] private AnimationCurve intensityCurve = new(
            new Keyframe(0f, 0f, 0f, 0.25f),
            new Keyframe(0.55f, 0.18f, 0.35f, 1.1f),
            new Keyframe(1f, 1f, 2.2f, 0f));

        [Header("KinoGlitch Appearance")]
        [Range(0f, 1f)] [SerializeField] private float scanLineJitter = 0.65f;
        [Range(0f, 1f)] [SerializeField] private float verticalJump = 0.12f;
        [Range(0f, 1f)] [SerializeField] private float horizontalShake = 0.08f;
        [Range(0f, 1f)] [SerializeField] private float colorDrift = 0.35f;
        [Range(0f, 1f)] [SerializeField] private float horizontalRipple = 0.45f;
        [Range(0f, 1f)] [SerializeField] private float digitalIntensity = 0.5f;

        [Header("Source Discovery")]
        [Min(0.1f)] [SerializeField] private float refreshInterval = 1f;

        [Header("Debug")]
        [Tooltip("-1 uses monster distance. Set 0-1 to preview and tune the effect in Play Mode.")]
        [Range(-1f, 1f)] [SerializeField] private float intensityOverride = -1f;
        [Tooltip("Runtime value after evaluating the closest monster.")]
        [SerializeField] private float currentIntensity;
        [Tooltip("Number of active MonsterAI sources found in the current scene.")]
        [SerializeField] private int detectedSourceCount;

        private readonly List<MonsterAI> monsters = new();
        private NetworkObject networkObject;
        private Volume runtimeVolume;
        private VolumeProfile runtimeProfile;
        private TvMonsterGlitchPostProcess postProcess;
        private float refreshTimer;
        private float forcedIntensity;
        private float forcedIntensityEndTime;
        private bool initialized;

        private bool IsLocalOwner => networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;

        private void Awake()
        {
            networkObject = GetComponentInParent<NetworkObject>();
        }

        private IEnumerator Start()
        {
            // Host ownership can be unresolved for more than one frame while the
            // player prefab is entering the gameplay scene.
            while (networkObject != null && !networkObject.IsSpawned)
                yield return null;

            if (!IsLocalOwner)
            {
                enabled = false;
                yield break;
            }

            InitializeForConfirmedLocalOwner();
        }

        public void InitializeForConfirmedLocalOwner()
        {
            if (initialized || !IsLocalOwner) return;

            initialized = true;
            CreateRuntimeVolume();
            RefreshMonsterSources();
        }

        private void Update()
        {
            if (postProcess == null || !IsLocalOwner)
                return;

            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
                RefreshMonsterSources();

            ApplyAppearanceSettings();
            currentIntensity = intensityOverride >= 0f
                ? intensityOverride
                : CalculateStrongestIntensity();
            if (Time.unscaledTime < forcedIntensityEndTime)
                currentIntensity = Mathf.Max(currentIntensity, forcedIntensity);
            postProcess.intensity.value = currentIntensity;
        }

        public void PlayFailureBurst(float intensity, float duration)
        {
            forcedIntensity = Mathf.Clamp01(intensity);
            forcedIntensityEndTime = Time.unscaledTime + Mathf.Max(0f, duration);
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
            detectedSourceCount = monsters.Count;
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
            postProcess.scanLineJitter.overrideState = true;
            postProcess.verticalJump.overrideState = true;
            postProcess.horizontalShake.overrideState = true;
            postProcess.colorDrift.overrideState = true;
            postProcess.horizontalRipple.overrideState = true;
            postProcess.digitalIntensity.overrideState = true;
            postProcess.intensity.value = 0f;
            postProcess.scanLineJitter.value = scanLineJitter;
            postProcess.verticalJump.value = verticalJump;
            postProcess.horizontalShake.value = horizontalShake;
            postProcess.colorDrift.value = colorDrift;
            postProcess.horizontalRipple.value = horizontalRipple;
            postProcess.digitalIntensity.value = digitalIntensity;
        }

        private void ApplyAppearanceSettings()
        {
            postProcess.scanLineJitter.value = scanLineJitter;
            postProcess.verticalJump.value = verticalJump;
            postProcess.horizontalShake.value = horizontalShake;
            postProcess.colorDrift.value = colorDrift;
            postProcess.horizontalRipple.value = horizontalRipple;
            postProcess.digitalIntensity.value = digitalIntensity;
        }

        private void OnDisable()
        {
            if (postProcess != null)
                postProcess.intensity.value = 0f;
        }

        private void OnDestroy()
        {
            if (runtimeVolume != null)
                Destroy(runtimeVolume.gameObject);
            if (runtimeProfile != null)
                Destroy(runtimeProfile);
        }
    }

    internal static class TvMonsterProximityGlitchInstaller
    {
        private static InstallerBehaviour instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null)
                return;

            GameObject installer = new("Tv Monster Glitch Installer");
            installer.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(installer);
            instance = installer.AddComponent<InstallerBehaviour>();
        }

        private sealed class InstallerBehaviour : MonoBehaviour
        {
            private float nextScanTime;

            private void OnDestroy()
            {
                if (instance == this)
                    instance = null;
            }

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
                        !cameraOwner.IsSpawned ||
                        cameraOwner.IsOwner;
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
                        playerNetworkObject.IsSpawned &&
                        !playerNetworkObject.IsOwner)
                        continue;

                    if (player.GetComponent<TvMonsterProximityGlitch>() == null)
                        player.gameObject.AddComponent<TvMonsterProximityGlitch>();
                }
            }
        }
    }
}
