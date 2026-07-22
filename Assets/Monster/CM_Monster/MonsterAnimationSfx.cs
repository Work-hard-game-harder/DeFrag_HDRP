using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class MonsterAnimationSfx : MonoBehaviour
{
    [Serializable]
    public sealed class AnimationSound
    {
        [Tooltip("Animator의 상태 이름입니다. 예: Idle, Walking, Running, Attack, Missing")]
        public string stateName;

        [Min(0)] public int layerIndex;

        [Tooltip("한 애니메이션 주기 중 효과음이 재생될 시점입니다.")]
        [Range(0f, 0.999f)] public float normalizedTime;

        [Tooltip("Loop 애니메이션이 반복될 때마다 효과음을 다시 재생합니다.")]
        public bool repeatEveryLoop;

        [Range(0f, 1f)] public float volume = 1f;
        public Vector2 pitchRange = new Vector2(1f, 1f);

        [Tooltip("여러 클립을 등록하면 재생할 때 하나를 무작위로 선택합니다.")]
        public List<AudioClip> clips = new List<AudioClip>();

        [NonSerialized] internal int stateHash;
        [NonSerialized] internal int fullPathHash;
        [NonSerialized] internal int lastPlayedLoop = int.MinValue;
        [NonSerialized] internal bool wasInState;
    }

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private MonsterAI monsterAI;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioSource normalLoopSource;
    [SerializeField] private AudioSource chaseLoopSource;
    [SerializeField] private AudioMixerGroup outputAudioMixerGroup;

    [Header("State Loop Sound")]
    [Tooltip("Idle, Search, Investigate 상태에서 반복 재생됩니다.")]
    [SerializeField] private AudioClip normalSound;
    [Tooltip("Chase와 Attack 상태에서 반복 재생됩니다.")]
    [SerializeField] private AudioClip chaseSound;
    [Range(0f, 1f)] [SerializeField] private float normalSoundVolume = 1f;
    [Range(0f, 1f)] [SerializeField] private float chaseSoundVolume = 1f;
    [Min(0f)] [SerializeField] private float stateSoundFadeDuration = 0.35f;

    [Header("3D Sound")]
    [Range(0f, 1f)] [SerializeField] private float spatialBlend = 1f;
    [Min(0f)] [SerializeField] private float minDistance = 2f;
    [Min(0.01f)] [SerializeField] private float maxDistance = 25f;

    [Header("Animation Sound List")]
    [SerializeField] private List<AnimationSound> animationSounds = new List<AnimationSound>();

    private void Reset()
    {
        animator = GetComponent<Animator>();
        monsterAI = GetComponent<MonsterAI>();
        EnsureAudioSources();
    }

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (monsterAI == null)
            monsterAI = GetComponent<MonsterAI>();

        EnsureAudioSources();
        CacheStateHashes();
    }

    private void OnEnable()
    {
        ResetRuntimeState();
        ResetLoopSource(normalLoopSource);
        ResetLoopSource(chaseLoopSource);
    }

    private void OnDisable()
    {
        StopLoopSource(normalLoopSource);
        StopLoopSource(chaseLoopSource);
    }

    private void OnValidate()
    {
        minDistance = Mathf.Max(0f, minDistance);
        maxDistance = Mathf.Max(minDistance + 0.01f, maxDistance);

        if (animator == null)
            animator = GetComponent<Animator>();

        if (monsterAI == null)
            monsterAI = GetComponent<MonsterAI>();

        CacheStateHashes();
        ApplyAudioSourceSettings(audioSource, false);
        ApplyAudioSourceSettings(normalLoopSource, true);
        ApplyAudioSourceSettings(chaseLoopSource, true);
    }

    private void Update()
    {
        UpdateStateLoopSounds();

        if (animator == null || !animator.isActiveAndEnabled)
            return;

        for (int i = 0; i < animationSounds.Count; i++)
            UpdateSound(animationSounds[i]);
    }

    private void UpdateStateLoopSounds()
    {
        if (monsterAI == null)
            return;

        MonsterAI.MonsterState state = monsterAI.CurrentState;
        bool usesNormalSound =
            state == MonsterAI.MonsterState.Idle ||
            state == MonsterAI.MonsterState.Search ||
            state == MonsterAI.MonsterState.Investigate;

        // Attack 상태에서는 Chase 루프를 유지하면서 아래 애니메이션 SFX도 함께 재생합니다.
        bool usesChaseSound =
            state == MonsterAI.MonsterState.Chase ||
            state == MonsterAI.MonsterState.Attack;
        float fadeStep = stateSoundFadeDuration <= 0f
            ? 1f
            : Time.deltaTime / stateSoundFadeDuration;

        UpdateLoopSource(
            normalLoopSource,
            normalSound,
            usesNormalSound ? normalSoundVolume : 0f,
            fadeStep);

        UpdateLoopSource(
            chaseLoopSource,
            chaseSound,
            usesChaseSound ? chaseSoundVolume : 0f,
            fadeStep);
    }

    private static void UpdateLoopSource(
        AudioSource source,
        AudioClip clip,
        float targetVolume,
        float fadeStep)
    {
        if (source == null)
            return;

        if (source.clip != clip)
        {
            source.Stop();
            source.clip = clip;
            source.volume = 0f;
        }

        if (clip == null)
        {
            source.Stop();
            return;
        }

        if (targetVolume > 0f && !source.isPlaying)
            source.Play();

        source.volume = Mathf.MoveTowards(source.volume, targetVolume, fadeStep);

        if (targetVolume <= 0f && source.volume <= 0f && source.isPlaying)
            source.Stop();
    }

    private void UpdateSound(AnimationSound sound)
    {
        if (sound == null || string.IsNullOrWhiteSpace(sound.stateName) ||
            sound.layerIndex < 0 || sound.layerIndex >= animator.layerCount)
        {
            return;
        }

        AnimatorStateInfo stateInfo = GetMatchingStateInfo(sound, out bool isMatching);
        if (!isMatching)
        {
            sound.wasInState = false;
            sound.lastPlayedLoop = int.MinValue;
            return;
        }

        int currentLoop = Mathf.Max(0, Mathf.FloorToInt(stateInfo.normalizedTime));
        float timeInLoop = stateInfo.normalizedTime - Mathf.Floor(stateInfo.normalizedTime);

        if (!sound.wasInState)
        {
            sound.wasInState = true;
            sound.lastPlayedLoop = int.MinValue;
        }

        bool canPlayThisLoop = sound.repeatEveryLoop
            ? sound.lastPlayedLoop != currentLoop
            : sound.lastPlayedLoop == int.MinValue;

        if (canPlayThisLoop && timeInLoop >= sound.normalizedTime)
        {
            PlaySound(sound);
            sound.lastPlayedLoop = currentLoop;
        }
    }

    private AnimatorStateInfo GetMatchingStateInfo(AnimationSound sound, out bool isMatching)
    {
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(sound.layerIndex);
        if (Matches(current, sound))
        {
            isMatching = true;
            return current;
        }

        if (animator.IsInTransition(sound.layerIndex))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(sound.layerIndex);
            if (Matches(next, sound))
            {
                isMatching = true;
                return next;
            }
        }

        isMatching = false;
        return current;
    }

    private static bool Matches(AnimatorStateInfo stateInfo, AnimationSound sound)
    {
        return stateInfo.shortNameHash == sound.stateHash ||
               stateInfo.fullPathHash == sound.fullPathHash;
    }

    private void PlaySound(AnimationSound sound)
    {
        if (audioSource == null || sound.clips == null || sound.clips.Count == 0)
            return;

        AudioClip clip = GetRandomValidClip(sound.clips);
        if (clip == null)
            return;

        float minimumPitch = Mathf.Min(sound.pitchRange.x, sound.pitchRange.y);
        float maximumPitch = Mathf.Max(sound.pitchRange.x, sound.pitchRange.y);
        audioSource.pitch = UnityEngine.Random.Range(minimumPitch, maximumPitch);
        audioSource.PlayOneShot(clip, sound.volume);
    }

    private static AudioClip GetRandomValidClip(List<AudioClip> clips)
    {
        int validCount = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            if (clips[i] != null)
                validCount++;
        }

        if (validCount == 0)
            return null;

        int selectedIndex = UnityEngine.Random.Range(0, validCount);
        for (int i = 0; i < clips.Count; i++)
        {
            if (clips[i] == null)
                continue;

            if (selectedIndex-- == 0)
                return clips[i];
        }

        return null;
    }

    private void EnsureAudioSources()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (normalLoopSource == null)
            normalLoopSource = gameObject.AddComponent<AudioSource>();

        if (chaseLoopSource == null)
            chaseLoopSource = gameObject.AddComponent<AudioSource>();

        ApplyAudioSourceSettings(audioSource, false);
        ApplyAudioSourceSettings(normalLoopSource, true);
        ApplyAudioSourceSettings(chaseLoopSource, true);
    }

    private void ApplyAudioSourceSettings(AudioSource source, bool shouldLoop)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = shouldLoop;
        source.spatialBlend = spatialBlend;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.outputAudioMixerGroup = outputAudioMixerGroup;
    }


    private static void ResetLoopSource(AudioSource source)
    {
        if (source == null)
            return;

        source.Stop();
        source.volume = 0f;
    }

    private static void StopLoopSource(AudioSource source)
    {
        if (source != null)
            source.Stop();
    }

    private void CacheStateHashes()
    {
        if (animationSounds == null)
            return;

        for (int i = 0; i < animationSounds.Count; i++)
        {
            AnimationSound sound = animationSounds[i];
            if (sound == null || string.IsNullOrWhiteSpace(sound.stateName))
                continue;

            sound.stateHash = Animator.StringToHash(sound.stateName);
            string layerName = animator != null && sound.layerIndex < animator.layerCount
                ? animator.GetLayerName(sound.layerIndex)
                : "Base Layer";
            sound.fullPathHash = Animator.StringToHash($"{layerName}.{sound.stateName}");
        }
    }

    private void ResetRuntimeState()
    {
        if (animationSounds == null)
            return;

        for (int i = 0; i < animationSounds.Count; i++)
        {
            AnimationSound sound = animationSounds[i];
            if (sound == null)
                continue;

            sound.wasInState = false;
            sound.lastPlayedLoop = int.MinValue;
        }
    }
}
