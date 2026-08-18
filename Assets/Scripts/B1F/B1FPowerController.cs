using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DeFrag.B1F
{
    public enum B1FPowerState : byte
    {
        PowerOff,
        EmergencyPower,
        FullPower
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class B1FPowerController : NetworkBehaviour
    {
        [Header("Power Roots")]
        [SerializeField] private GameObject powerOff;
        [SerializeField] private GameObject emergencyPower;
        [SerializeField] private GameObject fullPower;

        [Header("Emergency Spawn")]
        [SerializeField] private NetworkObject tvMonsterPrefab;
        [SerializeField] private Transform tvMonsterSpawnPoint;

        [Header("Power Transition")]
        [SerializeField, Min(0f)] private float transitionDelay = 1f;
        [SerializeField, Min(0.01f)] private float flickerOnDuration = 0.12f;
        [SerializeField, Min(0.01f)] private float flickerOffDuration = 0.18f;
        [SerializeField, Min(0.01f)] private float fadeInDuration = 1.25f;
        [SerializeField] private AnimationCurve fadeInCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Power Transition Audio")]
        [SerializeField] private AudioSource powerAudioSource;
        [SerializeField] private AudioClip powerRecoveryClip;

        private readonly NetworkVariable<B1FPowerState> currentState = new(
            B1FPowerState.PowerOff,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> powerTransitioning = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private bool tvMonsterSpawned;
        private Coroutine localTransitionRoutine;
        private Coroutine serverTransitionRoutine;

        public B1FPowerState CurrentState => currentState.Value;

        private void Awake() => ApplyState(currentState.Value);

        public override void OnNetworkSpawn()
        {
            currentState.OnValueChanged += OnPowerStateChanged;
            ApplyState(currentState.Value);
            if (IsServer && currentState.Value >= B1FPowerState.EmergencyPower)
                SpawnTvMonsterOnce();
        }

        public override void OnNetworkDespawn()
        {
            currentState.OnValueChanged -= OnPowerStateChanged;
        }

        public bool CanUseBoxA => !powerTransitioning.Value &&
                                  CurrentState == B1FPowerState.PowerOff;
        public bool CanUseBoxB => !powerTransitioning.Value &&
                                  CurrentState == B1FPowerState.EmergencyPower;

        public void SetEmergencyPowerServer()
        {
            if (!IsServer || powerTransitioning.Value ||
                currentState.Value != B1FPowerState.PowerOff)
                return;

            BeginPowerTransition(B1FPowerState.EmergencyPower);
        }

        public void SetFullPowerServer()
        {
            if (!IsServer || powerTransitioning.Value ||
                currentState.Value != B1FPowerState.EmergencyPower)
                return;

            BeginPowerTransition(B1FPowerState.FullPower);
        }

        private void OnPowerStateChanged(B1FPowerState previous, B1FPowerState next) =>
            ApplyState(next);

        private void ApplyState(B1FPowerState state)
        {
            if (powerOff != null) powerOff.SetActive(state == B1FPowerState.PowerOff);
            if (emergencyPower != null) emergencyPower.SetActive(state == B1FPowerState.EmergencyPower);
            if (fullPower != null) fullPower.SetActive(state == B1FPowerState.FullPower);
        }

        private void BeginPowerTransition(B1FPowerState targetState)
        {
            powerTransitioning.Value = true;
            PlayPowerTransitionClientRpc(targetState);
            if (serverTransitionRoutine != null) StopCoroutine(serverTransitionRoutine);
            serverTransitionRoutine = StartCoroutine(CommitPowerStateAfterTransition(targetState));
        }

        private IEnumerator CommitPowerStateAfterTransition(B1FPowerState targetState)
        {
            float duration = transitionDelay +
                             2f * (flickerOnDuration + flickerOffDuration) +
                             fadeInDuration;
            yield return new WaitForSecondsRealtime(duration);

            currentState.Value = targetState;
            powerTransitioning.Value = false;
            serverTransitionRoutine = null;

            if (targetState == B1FPowerState.EmergencyPower)
                SpawnTvMonsterOnce();
        }

        [ClientRpc]
        private void PlayPowerTransitionClientRpc(B1FPowerState targetState)
        {
            if (localTransitionRoutine != null) StopCoroutine(localTransitionRoutine);
            localTransitionRoutine = StartCoroutine(PlayPowerTransition(targetState));
        }

        private IEnumerator PlayPowerTransition(B1FPowerState targetState)
        {
            GameObject targetRoot = GetRoot(targetState);
            if (targetRoot == null)
            {
                localTransitionRoutine = null;
                yield break;
            }

            yield return new WaitForSecondsRealtime(transitionDelay);

            if (powerAudioSource != null && powerRecoveryClip != null)
                powerAudioSource.PlayOneShot(powerRecoveryClip);

            for (int blink = 0; blink < 2; blink++)
            {
                targetRoot.SetActive(true);
                yield return new WaitForSecondsRealtime(flickerOnDuration);
                targetRoot.SetActive(false);
                yield return new WaitForSecondsRealtime(flickerOffDuration);
            }

            targetRoot.SetActive(true);
            Light[] lights = targetRoot.GetComponentsInChildren<Light>(true);
            float[] targetIntensities = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                targetIntensities[i] = lights[i].intensity;
                lights[i].intensity = 0f;
            }

            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / fadeInDuration);
                float intensity = fadeInCurve != null
                    ? fadeInCurve.Evaluate(normalized)
                    : normalized;
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null)
                        lights[i].intensity = targetIntensities[i] * intensity;
                yield return null;
            }

            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null)
                    lights[i].intensity = targetIntensities[i];

            localTransitionRoutine = null;
        }

        private GameObject GetRoot(B1FPowerState state) => state switch
        {
            B1FPowerState.PowerOff => powerOff,
            B1FPowerState.EmergencyPower => emergencyPower,
            B1FPowerState.FullPower => fullPower,
            _ => null
        };

        private void SpawnTvMonsterOnce()
        {
            if (tvMonsterSpawned)
                return;
            if (tvMonsterPrefab == null || tvMonsterSpawnPoint == null)
            {
                Debug.LogError(
                    "[B1FPowerController] TV Monster prefab or spawn point is not assigned.",
                    this);
                return;
            }

            NetworkObject monster = Instantiate(
                tvMonsterPrefab,
                tvMonsterSpawnPoint.position,
                tvMonsterSpawnPoint.rotation);
            monster.Spawn(true);
            tvMonsterSpawned = true;
        }
    }
}
