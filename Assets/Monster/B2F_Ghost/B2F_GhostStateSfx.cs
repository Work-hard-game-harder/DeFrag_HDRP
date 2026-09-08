using UnityEngine;

[DisallowMultipleComponent]
public sealed class B2F_GhostStateSfx : MonoBehaviour
{
    [Header("State Audio Sources")]
    [Tooltip("Idle and Moving states share this looping AudioSource.")]
    [SerializeField] private AudioSource generalSource;
    [Tooltip("The Smoking animation uses this AudioSource.")]
    [SerializeField] private AudioSource attackSource;

    [Header("Smoking")]
    [Tooltip("Enable only when the Smoking sound itself must repeat until the state ends.")]
    [SerializeField] private bool loopAttackSource;

    private void Awake()
    {
        ConfigureSources();
    }

    public void PlayGeneral()
    {
        Stop(attackSource);

        if (generalSource == null || generalSource.clip == null || generalSource.isPlaying)
            return;

        generalSource.Play();
    }

    public void PlaySmoking()
    {
        Stop(generalSource);

        if (attackSource == null || attackSource.clip == null || attackSource.isPlaying)
            return;

        attackSource.Play();
    }

    public void StopAll()
    {
        Stop(generalSource);
        Stop(attackSource);
    }

    private void OnDisable()
    {
        StopAll();
    }

    private void ConfigureSources()
    {
        if (generalSource != null)
        {
            generalSource.playOnAwake = false;
            generalSource.loop = true;
        }

        if (attackSource != null)
        {
            attackSource.playOnAwake = false;
            attackSource.loop = loopAttackSource;
        }
    }

    private static void Stop(AudioSource source)
    {
        if (source != null && source.isPlaying)
            source.Stop();
    }

    private void OnValidate()
    {
        if (generalSource != null && generalSource == attackSource)
        {
            Debug.LogWarning(
                "[B2F Ghost SFX] General and attack must use different AudioSources.",
                this);
            return;
        }

        ConfigureSources();
    }
}
