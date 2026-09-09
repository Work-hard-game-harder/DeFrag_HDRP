using UnityEngine;

/// <summary>Receiver feedback only; never publishes gameplay noise events.</summary>
[DisallowMultipleComponent]
public sealed class LocalSignalAudio : MonoBehaviour
{
    [SerializeField] private AudioClip pulseClip;
    [SerializeField, Range(0f, 1f)] private float maximumVolume = 0.55f;
    [SerializeField, Min(0.1f)] private float farInterval = 1.8f;
    [SerializeField, Min(0.1f)] private float nearInterval = 0.25f;
    private AudioSource source;
    private AudioClip generatedClip;
    private float requestedAt = float.NegativeInfinity;
    private float strength;
    private float nextPulse;

    public void Report(float value)
    {
        if (requestedAt != Time.unscaledTime) strength = 0f;
        requestedAt = Time.unscaledTime;
        strength = Mathf.Max(strength, Mathf.Clamp01(value));
    }

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.loop = false;
        if (pulseClip != null) return;
        const int rate = 22050;
        float[] samples = new float[2205];
        for (int i = 0; i < samples.Length; i++)
        {
            float envelope = Mathf.Sin(Mathf.PI * i / (samples.Length - 1));
            samples[i] = 0.35f * envelope * envelope * Mathf.Sin(2f * Mathf.PI * 880f * i / rate);
        }
        generatedClip = AudioClip.Create("Receiver pulse", samples.Length, 1, rate, false);
        generatedClip.SetData(samples, 0);
    }

    private void LateUpdate()
    {
        if (requestedAt != Time.unscaledTime || strength <= 0.01f)
        {
            source.Stop();
            nextPulse = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime < nextPulse) return;
        source.pitch = Mathf.Lerp(0.85f, 1.2f, strength);
        source.PlayOneShot(pulseClip != null ? pulseClip : generatedClip,
            maximumVolume * Mathf.Lerp(0.08f, 1f, strength));
        nextPulse = Time.unscaledTime + Mathf.Lerp(farInterval, nearInterval, strength);
    }

    private void OnDisable() { if (source != null) source.Stop(); }
    private void OnDestroy()
    {
        if (source != null) Destroy(source);
        if (generatedClip != null) Destroy(generatedClip);
    }
}
