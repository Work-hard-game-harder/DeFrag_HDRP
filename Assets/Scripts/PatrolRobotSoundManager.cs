using System;
using UnityEngine;

[RequireComponent(typeof(PatrolRobotAI), typeof(AudioSource))]
public sealed class PatrolRobotSoundManager : MonoBehaviour
{
    [Serializable]
    private struct StateSound
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
        [Range(0.1f, 3f)] public float pitch;
    }

    [Header("State Sounds")]
    [SerializeField] private StateSound patrol = new StateSound { volume = 1f, pitch = 1f };
    [SerializeField] private StateSound alert = new StateSound { volume = 1f, pitch = 1f };
    [SerializeField] private StateSound chase = new StateSound { volume = 1f, pitch = 1f };

    private PatrolRobotAI robotAI;
    private AudioSource audioSource;

    private void Awake()
    {
        robotAI = GetComponent<PatrolRobotAI>();
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = true;
    }

    private void OnEnable()
    {
        robotAI.StateChanged += HandleStateChanged;
        HandleStateChanged(robotAI.currentState);
    }

    private void OnDisable()
    {
        robotAI.StateChanged -= HandleStateChanged;
        audioSource.Stop();
    }

    private void HandleStateChanged(PatrolRobotAI.State state)
    {
        StateSound sound = state switch
        {
            PatrolRobotAI.State.Alert => alert,
            PatrolRobotAI.State.Chase => chase,
            _ => patrol
        };

        if (sound.clip == null)
        {
            audioSource.Stop();
            audioSource.clip = null;
            return;
        }

        audioSource.volume = sound.volume;
        audioSource.pitch = sound.pitch;

        if (audioSource.clip == sound.clip && audioSource.isPlaying) return;

        audioSource.Stop();
        audioSource.clip = sound.clip;
        audioSource.Play();
    }
}
