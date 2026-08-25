using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TerminalSfxPlayer : MonoBehaviour
{
    [Header("Audio Output")]
    [Tooltip("Optional. If left empty, a local 2D AudioSource is created at runtime.")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Header("Terminal SFX")]
    [SerializeField] private AudioClip sessionOpenedClip;
    [SerializeField] private AudioClip characterTypedClip;
    [SerializeField] private AudioClip menuSelectedClip;
    [SerializeField] private AudioClip menuBackClip;
    [SerializeField] private AudioClip incorrectAnswerClip;

    [Header("Typing Variation")]
    [SerializeField, Min(0f)] private float minimumTypingInterval = 0.025f;
    [SerializeField] private Vector2 typingPitchRange = new(0.96f, 1.04f);

    private float nextTypingTime;
    private float basePitch = 1f;
    private bool audioSourceResolved;

    public void PlaySessionOpened() => PlayOneShot(sessionOpenedClip);
    public void PlayMenuSelected() => PlayOneShot(menuSelectedClip);
    public void PlayMenuBack() => PlayOneShot(menuBackClip);
    public void PlayIncorrectAnswer() => PlayOneShot(incorrectAnswerClip);

    public void BindTyping(TMP_InputField input)
    {
        if (input == null)
            return;

        int previousLength = input.text?.Length ?? 0;
        input.onValueChanged.AddListener(value =>
        {
            int currentLength = value?.Length ?? 0;
            if (currentLength > previousLength)
                PlayTypingCharacter();
            previousLength = currentLength;
        });
    }

    private void PlayTypingCharacter()
    {
        if (characterTypedClip == null || Time.unscaledTime < nextTypingTime)
            return;

        ResolveAudioSource();
        nextTypingTime = Time.unscaledTime + minimumTypingInterval;
        float minimumPitch = Mathf.Min(typingPitchRange.x, typingPitchRange.y);
        float maximumPitch = Mathf.Max(typingPitchRange.x, typingPitchRange.y);
        audioSource.pitch = basePitch * Random.Range(minimumPitch, maximumPitch);
        audioSource.PlayOneShot(characterTypedClip, volume);
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (clip == null)
            return;

        ResolveAudioSource();
        audioSource.pitch = basePitch;
        audioSource.PlayOneShot(clip, volume);
    }

    private void ResolveAudioSource()
    {
        if (audioSourceResolved)
            return;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        basePitch = audioSource.pitch;
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSourceResolved = true;
    }
}
