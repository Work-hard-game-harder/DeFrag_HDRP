using System;
using UnityEngine;

// Synthesized clips shared by runtime-built minigame UI; no asset wiring or generation credits needed.
public static class ProceduralSfx
{
    private const int SampleRate = 44100;

    private static AudioClip radarPing;
    private static AudioClip decoyBurst;
    private static AudioClip backfire;
    private static AudioClip ignitionCatch;
    private static AudioClip crankClick;
    private static AudioClip engineTurnLoop;
    private static AudioClip uiTick;
    private static AudioClip warningBeep;

    public static AudioClip RadarPing => radarPing ??= Build("RadarPing", 0.45f, RadarPingSample);
    public static AudioClip DecoyBurst => decoyBurst ??= Build("DecoyBurst", 0.9f, DecoyBurstSample);
    public static AudioClip Backfire => backfire ??= Build("Backfire", 1.1f, BackfireSample);
    public static AudioClip IgnitionCatch => ignitionCatch ??= Build("IgnitionCatch", 0.9f, IgnitionCatchSample);
    public static AudioClip CrankClick => crankClick ??= Build("CrankClick", 0.07f, CrankClickSample);
    public static AudioClip EngineTurnLoop => engineTurnLoop ??= Build("EngineTurnLoop", 1f, EngineTurnSample);
    public static AudioClip UiTick => uiTick ??= Build("UiTick", 0.06f, UiTickSample);
    public static AudioClip WarningBeep => warningBeep ??= Build("WarningBeep", 0.32f, WarningBeepSample);

    private static AudioClip Build(string name, float seconds, Func<float, float, System.Random, float> sample)
    {
        int count = Mathf.CeilToInt(seconds * SampleRate);
        float[] data = new float[count];
        var random = new System.Random(name.GetHashCode());
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            data[i] = Mathf.Clamp(sample(t, seconds, random), -1f, 1f);
        }

        AudioClip clip = AudioClip.Create($"Procedural_{name}", count, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Noise(System.Random random) => (float)(random.NextDouble() * 2.0 - 1.0);

    private static float RadarPingSample(float t, float length, System.Random _)
    {
        float frequency = Mathf.Lerp(1480f, 1180f, t / length);
        float envelope = Mathf.Exp(-t * 9f) * Mathf.Clamp01(t * 400f);
        float echo = Mathf.Exp(-(t - 0.18f) * 14f) * (t > 0.18f ? 0.35f : 0f);
        return Mathf.Sin(2f * Mathf.PI * frequency * t) * (envelope + echo) * 0.55f;
    }

    private static float DecoyBurstSample(float t, float length, System.Random random)
    {
        float gate = Mathf.Sin(2f * Mathf.PI * 13f * t) > 0.1f ? 1f : 0.15f;
        float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(220f, 90f, t / length) * t));
        float envelope = Mathf.Clamp01(t * 60f) * (1f - t / length);
        return (square * 0.35f + Noise(random) * 0.45f) * gate * envelope * 0.7f;
    }

    private static float BackfireSample(float t, float length, System.Random random)
    {
        float boomFrequency = Mathf.Lerp(90f, 32f, Mathf.Clamp01(t * 3f));
        float boom = Mathf.Sin(2f * Mathf.PI * boomFrequency * t) * Mathf.Exp(-t * 3.2f);
        float crack = Noise(random) * Mathf.Exp(-t * 26f);
        float rattle = Noise(random) * Mathf.Exp(-t * 5f) * 0.25f * (Mathf.Sin(2f * Mathf.PI * 31f * t) > 0f ? 1f : 0.3f);
        return (boom * 0.95f + crack * 0.8f + rattle) * Mathf.Clamp01(t * 800f);
    }

    private static float IgnitionCatchSample(float t, float length, System.Random random)
    {
        float spark = Noise(random) * Mathf.Exp(-t * 40f) * 0.6f;
        float chuffs = 0f;
        for (int i = 0; i < 3; i++)
        {
            float start = 0.12f + i * 0.2f;
            if (t < start) continue;
            float local = t - start;
            chuffs += (Mathf.Sin(2f * Mathf.PI * 58f * local) * 0.7f + Noise(random) * 0.3f) *
                      Mathf.Exp(-local * 11f) * (1f - i * 0.2f);
        }
        return spark + chuffs * 0.8f;
    }

    private static float CrankClickSample(float t, float length, System.Random random)
    {
        float body = Mathf.Sin(2f * Mathf.PI * 2100f * t) * Mathf.Exp(-t * 90f);
        return (body * 0.5f + Noise(random) * Mathf.Exp(-t * 140f) * 0.5f) * 0.7f;
    }

    private static float EngineTurnSample(float t, float length, System.Random random)
    {
        const int strokesPerLoop = 4;
        float phase = t * strokesPerLoop % 1f;
        float compression = Mathf.Exp(-phase * 7f);
        float thump = Mathf.Sin(2f * Mathf.PI * 46f * t) * compression;
        float hiss = Noise(random) * compression * 0.22f;
        float whine = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.04f;
        return (thump * 0.7f + hiss + whine) * 0.8f;
    }

    private static float UiTickSample(float t, float length, System.Random _) =>
        Mathf.Sin(2f * Mathf.PI * 2600f * t) * Mathf.Exp(-t * 70f) * 0.3f;

    private static float WarningBeepSample(float t, float length, System.Random _)
    {
        float gate = t < 0.12f || (t > 0.18f && t < 0.3f) ? 1f : 0f;
        return Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 880f * t)) * gate * 0.16f;
    }
}
