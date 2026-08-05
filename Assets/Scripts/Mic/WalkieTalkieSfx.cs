using EasyPeasyFirstPersonController;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Local presentation for walkie-talkie transmission sounds.
/// Recording and network state remain owned by WalkieTalkieController.
/// </summary>
[DisallowMultipleComponent]
public sealed class WalkieTalkieSfx : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WalkieTalkieController walkieTalkieController;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioMixerGroup outputMixerGroup;
    [SerializeField] private string audioSourceObjectName = "Walkie-Talkie Sound";

    [Header("Transmission SFX")]
    [Tooltip("Played once when left-click transmission and recording begin.")]
    [SerializeField] private AudioClip transmissionStartClip;
    [Tooltip("Optionally played once when left-click transmission and recording end.")]
    [SerializeField] private AudioClip transmissionStopClip;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    private bool isBound;

    private void Awake()
    {
        ResolveAudioSource();
        ConfigureAudioSource();
    }

    private void OnEnable()
    {
        TryBind();
    }

    private void Start()
    {
        // FirstPersonController may add WalkieTalkieController during Awake,
        // so bind after every component has completed Awake.
        TryBind();
    }

    private void LateUpdate()
    {
        if (!isBound)
            TryBind();
    }

    private void TryBind()
    {
        if (isBound)
            return;

        if (walkieTalkieController == null)
            walkieTalkieController = GetComponent<WalkieTalkieController>();
        if (walkieTalkieController == null)
            return;

        walkieTalkieController.TransmissionStarted += OnTransmissionStarted;
        walkieTalkieController.TransmissionStopped += OnTransmissionStopped;
        isBound = true;
    }

    private void OnTransmissionStarted()
    {
        if (walkieTalkieController == null || !walkieTalkieController.IsLocalOwner)
            return;

        PlayOneShot(transmissionStartClip);
    }

    private void OnTransmissionStopped()
    {
        if (walkieTalkieController == null || !walkieTalkieController.IsLocalOwner)
            return;

        PlayOneShot(transmissionStopClip);
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (clip == null)
            return;

        ResolveAudioSource();
        if (audioSource == null)
            return;

        ConfigureAudioSource();
        audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private void ConfigureAudioSource()
    {
        if (audioSource == null)
            return;

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        if (outputMixerGroup != null)
            audioSource.outputAudioMixerGroup = outputMixerGroup;
    }

    private void ResolveAudioSource()
    {
        if (audioSource != null)
            return;

        Transform sourceTransform = FindDescendantByName(transform, audioSourceObjectName);
        if (sourceTransform == null)
        {
            GameObject sourceObject = new(audioSourceObjectName);
            sourceTransform = sourceObject.transform;
            sourceTransform.SetParent(transform, false);
        }

        audioSource = sourceTransform.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = sourceTransform.gameObject.AddComponent<AudioSource>();
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
            return null;

        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform descendant in descendants)
        {
            if (descendant != root && descendant.name == targetName)
                return descendant;
        }

        return null;
    }

    private void OnDisable()
    {
        if (!isBound || walkieTalkieController == null)
            return;

        walkieTalkieController.TransmissionStarted -= OnTransmissionStarted;
        walkieTalkieController.TransmissionStopped -= OnTransmissionStopped;
        isBound = false;
    }
}
