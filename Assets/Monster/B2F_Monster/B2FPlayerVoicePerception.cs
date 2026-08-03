using System.Collections.Generic;
using EasyPeasyFirstPersonController;
using UnityEngine;

namespace DeFrag.Monsters.B2F
{
    /// <summary>
    /// Converts a player's live microphone level into a world-space hearing range for B2F monsters.
    /// The microphone capture remains owned by the player-side SoundEmitter/SettingManager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class B2FPlayerVoicePerception : MonoBehaviour
    {
        [Header("Voice Detection")]
        [SerializeField, Min(0f)] private float maximumDetectionRange = 60f;
        [SerializeField, Range(0f, 1f)] private float minimumVoiceVolume = 0.05f;
        [SerializeField, Min(0f)] private float minimumDetectionRange = 5f;
        [SerializeField, Min(0.1f)] private float rangeExponent = 1.35f;
        [Tooltip("PlayerTarget이 비활성화되거나 교체됐을 때 활성 플레이어 음성 소스를 다시 찾는 간격입니다.")]
        [SerializeField, Min(0.1f)] private float voiceSourceRefreshInterval = 0.5f;

        [Header("Debug Gizmos")]
        [SerializeField] private bool showDetectionGizmos = true;
        [SerializeField] private Color maximumRangeColor = new Color(0.15f, 0.45f, 1f, 0.65f);
        [SerializeField] private Color currentRangeColor = new Color(0.1f, 1f, 0.2f, 0.9f);

        private readonly List<SoundEmitter> activeVoiceSources = new List<SoundEmitter>();
        private float nextSourceRefreshTime;

        public float CurrentDetectionRange { get; private set; }
        public float CurrentVoiceVolume { get; private set; }
        public Transform LastHeardTarget { get; private set; }

        public bool TryHear(Transform target, out Vector3 heardPosition)
        {
            return TryHear(target, out _, out heardPosition);
        }

        public bool TryHear(
            Transform preferredTarget,
            out Transform heardTarget,
            out Vector3 heardPosition)
        {
            heardTarget = null;
            heardPosition = default;
            CurrentDetectionRange = 0f;
            CurrentVoiceVolume = 0f;
            LastHeardTarget = null;

            RefreshVoiceSources();

            SoundEmitter preferredEmitter = ResolveActiveEmitter(preferredTarget);
            float bestScore = float.MinValue;
            EvaluateEmitter(preferredEmitter, ref bestScore, ref heardTarget, ref heardPosition);

            foreach (SoundEmitter emitter in activeVoiceSources)
            {
                if (emitter == preferredEmitter)
                    continue;

                EvaluateEmitter(emitter, ref bestScore, ref heardTarget, ref heardPosition);
            }

            LastHeardTarget = heardTarget;
            return heardTarget != null;
        }

        private void EvaluateEmitter(
            SoundEmitter emitter,
            ref float bestScore,
            ref Transform heardTarget,
            ref Vector3 heardPosition)
        {
            if (emitter == null || !emitter.isActiveAndEnabled || !emitter.IsMicActive)
                return;

            float volume = Mathf.Clamp01(emitter.CurrentVolume);
            if (volume < minimumVoiceVolume)
                return;

            float normalizedVolume = Mathf.InverseLerp(minimumVoiceVolume, 1f, volume);
            float volumeFactor = Mathf.Pow(normalizedVolume, Mathf.Max(0.1f, rangeExponent));
            float detectionRange = Mathf.Lerp(
                Mathf.Min(minimumDetectionRange, maximumDetectionRange),
                Mathf.Max(minimumDetectionRange, maximumDetectionRange),
                volumeFactor);

            Transform sourceTarget = emitter.transform;
            float distance = Vector3.Distance(transform.position, sourceTarget.position);
            if (distance > detectionRange)
                return;

            // 두 플레이어가 동시에 말하면 음량이 크고 가까운 쪽을 조사 대상으로 선택합니다.
            float score = volume / Mathf.Max(1f, distance);
            if (score <= bestScore)
                return;

            bestScore = score;
            CurrentVoiceVolume = volume;
            CurrentDetectionRange = detectionRange;
            heardTarget = sourceTarget;
            heardPosition = sourceTarget.position;
        }

        private void RefreshVoiceSources()
        {
            if (Time.unscaledTime < nextSourceRefreshTime && activeVoiceSources.Count > 0)
                return;

            nextSourceRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, voiceSourceRefreshInterval);
            activeVoiceSources.Clear();
            SoundEmitter[] sources = FindObjectsByType<SoundEmitter>(FindObjectsInactive.Exclude);
            foreach (SoundEmitter source in sources)
            {
                if (source != null && source.isActiveAndEnabled)
                    activeVoiceSources.Add(source);
            }
        }

        private static SoundEmitter ResolveActiveEmitter(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
                return null;

            SoundEmitter emitter = target.GetComponentInChildren<SoundEmitter>();
            if (emitter == null)
                emitter = target.GetComponentInParent<SoundEmitter>();

            return emitter != null && emitter.isActiveAndEnabled ? emitter : null;
        }

        private void OnValidate()
        {
            maximumDetectionRange = Mathf.Max(0f, maximumDetectionRange);
            minimumDetectionRange = Mathf.Clamp(minimumDetectionRange, 0f, maximumDetectionRange);
            rangeExponent = Mathf.Max(0.1f, rangeExponent);
            voiceSourceRefreshInterval = Mathf.Max(0.1f, voiceSourceRefreshInterval);

            // Enter Play Mode Options에서 Domain Reload를 끈 경우에도
            // 편집 모드에 마지막 런타임 감지 반경이 남지 않도록 정리합니다.
            if (!Application.isPlaying)
            {
                CurrentDetectionRange = 0f;
                CurrentVoiceVolume = 0f;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!showDetectionGizmos)
                return;

            Gizmos.color = maximumRangeColor;
            Gizmos.DrawWireSphere(transform.position, maximumDetectionRange);

#if UNITY_EDITOR
            UnityEditor.Handles.color = maximumRangeColor;
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.6f,
                $"Maximum Voice Detection Range ({maximumDetectionRange:0.0}m)");
#endif

            // Current Range는 실시간 마이크 입력으로 계산되는 런타임 정보입니다.
            // 편집 모드에서는 이전 Play Mode 값이 남아 있어도 표시하지 않습니다.
            if (!Application.isPlaying || CurrentDetectionRange <= 0f)
                return;

            Gizmos.color = currentRangeColor;
            Gizmos.DrawWireSphere(transform.position, CurrentDetectionRange);

#if UNITY_EDITOR
            UnityEditor.Handles.color = currentRangeColor;
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 2f,
                $"Current Voice Detection Range ({CurrentDetectionRange:0.0}m)");
#endif
        }
    }
}
