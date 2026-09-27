using System;
using System.Collections.Generic;
using UnityEngine;

public enum UiCue
{
    TerminalBoot, TerminalClose, MenuMove, MenuConfirm, MenuBack, AccessDenied, KeyType,
    TaskSuccess, TaskFail, RoundClear,
    TutorialOpen, TutorialClose,
    RhythmPerfect, RhythmGood, RhythmMiss, RhythmCountIn,
    CardMove, CardCorrect, CardWrong, MemoryShuffle,
    PiecePick, PieceRotate, PiecePlace, PieceInvalid, CircuitComplete,
    IrOn, IrOff, LockTick, LockAcquired, CaptureAccepted, CaptureRejected,
    MonitorMatch, MonitorMismatch, BankComplete, KnobZone,
    Alert
}

// Local 2D interface sounds, synthesized per cue so every interaction has its own voice.
public static class UiSfx
{
    private const int SampleRate = 44100;
    private const int VoiceCount = 6;
    private static readonly Dictionary<UiCue, AudioClip> Clips = new();
    private static AudioSource[] voices;
    private static int nextVoice;

    public static void Play(UiCue cue, float volume = 1f, float pitch = 1f)
    {
        AudioSource player = NextVoice();
        player.pitch = pitch;
        player.PlayOneShot(Get(cue), volume);
    }

    public static AudioClip Get(UiCue cue)
    {
        if (Clips.TryGetValue(cue, out AudioClip clip) && clip != null)
            return clip;
        (float length, Func<float, float, float> sample) = Recipe(cue);
        clip = Build(cue.ToString(), length, sample);
        Clips[cue] = clip;
        return clip;
    }

    // Round-robin voices so a pitched one-shot never retunes a sound that is still ringing.
    private static AudioSource NextVoice()
    {
        if (voices == null || voices[0] == null)
        {
            GameObject host = new("UiSfx Player") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying)
                UnityEngine.Object.DontDestroyOnLoad(host);
            voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                voices[i] = host.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0f;
                voices[i].ignoreListenerPause = true;
            }
        }
        AudioSource voice = voices[nextVoice];
        nextVoice = (nextVoice + 1) % VoiceCount;
        return voice;
    }

    private static AudioClip Build(string name, float seconds, Func<float, float, float> sample)
    {
        int count = Mathf.CeilToInt(seconds * SampleRate);
        float[] data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float fade = Mathf.Clamp01((seconds - t) * 60f) * Mathf.Clamp01(t * 2000f);
            data[i] = Mathf.Clamp(sample(t, seconds) * fade, -1f, 1f);
        }
        AudioClip clip = AudioClip.Create($"UiSfx_{name}", count, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static readonly System.Random NoiseSource = new(1337);
    private static float Noise() => (float)(NoiseSource.NextDouble() * 2.0 - 1.0);
    private static float Sin(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);
    private static float Square(float f, float t) => Mathf.Sign(Sin(f, t)) * 0.6f;
    private static float Tri(float f, float t) => Mathf.PingPong(t * f * 4f, 2f) - 1f;
    private static float Decay(float t, float rate) => Mathf.Exp(-t * rate);
    private static float Bell(float f, float t, float rate) => Mathf.Sin(2f * Mathf.PI * f * t + 1.8f * Sin(f * 2.76f, t) * Decay(t, rate * 1.5f)) * Decay(t, rate);

    private static float Note(float t, float start, float f, float rate, Func<float, float, float> wave)
    {
        if (t < start) return 0f;
        float local = t - start;
        return wave(f, local) * Decay(local, rate);
    }

    private static (float, Func<float, float, float>) Recipe(UiCue cue) => cue switch
    {
        UiCue.TerminalBoot => (0.55f, (t, _) =>
            (Note(t, 0f, 523f, 14f, Sin) + Note(t, 0.07f, 659f, 14f, Sin) + Note(t, 0.14f, 784f, 10f, Sin)) * 0.35f +
            Noise() * Decay(t, 9f) * 0.05f),
        UiCue.TerminalClose => (0.3f, (t, _) =>
            (Note(t, 0f, 660f, 20f, Sin) + Note(t, 0.08f, 440f, 16f, Sin)) * 0.32f),
        UiCue.MenuMove => (0.04f, (t, _) => Sin(3100f, t) * Decay(t, 120f) * 0.25f),
        UiCue.MenuConfirm => (0.16f, (t, _) =>
            (Note(t, 0f, 880f, 35f, Square) + Note(t, 0.05f, 1320f, 30f, Square)) * 0.18f),
        UiCue.MenuBack => (0.16f, (t, _) =>
            (Note(t, 0f, 1100f, 35f, Square) + Note(t, 0.05f, 740f, 30f, Square)) * 0.16f),
        UiCue.AccessDenied => (0.38f, (t, _) =>
            Square(110f, t) * (0.6f + 0.4f * Sin(18f, t)) * Decay(t, 5f) * 0.35f),
        UiCue.KeyType => (0.025f, (t, _) => (Noise() * 0.5f + Sin(4200f, t) * 0.3f) * Decay(t, 260f) * 0.4f),
        UiCue.TaskSuccess => (0.9f, (t, _) =>
            (Bell(1047f, t, 6f) + Note(t, 0.09f, 1319f, 6f, (f, x) => Bell(f, x, 6f)) +
             Note(t, 0.18f, 1568f, 5f, (f, x) => Bell(f, x, 5f)) + Note(t, 0.27f, 2093f, 4f, (f, x) => Bell(f, x, 4f))) * 0.2f),
        UiCue.TaskFail => (0.55f, (t, _) =>
            (Note(t, 0f, 392f, 8f, Square) + Note(t, 0.12f, 330f, 8f, Square) + Note(t, 0.24f, 262f, 6f, Square)) * 0.14f),
        UiCue.RoundClear => (0.4f, (t, _) =>
            (Note(t, 0f, 988f, 10f, Sin) + Note(t, 0.08f, 1480f, 8f, Sin)) * 0.3f),
        UiCue.TutorialOpen => (0.7f, (t, _) =>
            (Sin(523f, t) + Sin(659f, t) * 0.8f + Sin(988f, t) * 0.5f) * Mathf.Clamp01(t * 12f) * Decay(t, 4f) * 0.16f),
        UiCue.TutorialClose => (0.12f, (t, _) => Sin(Mathf.Lerp(900f, 1600f, t / 0.12f), t) * Decay(t, 25f) * 0.25f),
        UiCue.RhythmPerfect => (0.45f, (t, _) => Bell(1046f, t, 7f) * 0.4f),
        UiCue.RhythmGood => (0.3f, (t, _) => Tri(1046f, t) * Decay(t, 12f) * 0.28f),
        UiCue.RhythmMiss => (0.28f, (t, _) => (Sin(90f, t) * 0.7f + Noise() * 0.4f) * Decay(t, 14f) * 0.45f),
        UiCue.RhythmCountIn => (0.08f, (t, _) => Sin(1760f, t) * Decay(t, 60f) * 0.25f),
        UiCue.CardMove => (0.05f, (t, _) => Sin(1250f, t) * Decay(t, 90f) * 0.3f),
        UiCue.CardCorrect => (0.22f, (t, _) =>
            (Note(t, 0f, 660f, 25f, Sin) + Note(t, 0.06f, 990f, 20f, Sin)) * 0.3f),
        UiCue.CardWrong => (0.25f, (t, _) => Square(160f + 30f * Sin(30f, t), t) * Decay(t, 12f) * 0.25f),
        UiCue.MemoryShuffle => (0.5f, (t, l) => Noise() * Mathf.Sin(Mathf.PI * t / l) * 0.18f * (0.5f + 0.5f * Sin(60f, t))),
        UiCue.PiecePick => (0.06f, (t, _) => Sin(Mathf.Lerp(700f, 1400f, t / 0.06f), t) * Decay(t, 50f) * 0.3f),
        UiCue.PieceRotate => (0.07f, (t, _) =>
            (Note(t, 0f, 2400f, 140f, Sin) + Note(t, 0.03f, 2000f, 140f, Sin)) * 0.3f),
        UiCue.PiecePlace => (0.18f, (t, _) => (Sin(140f, t) * 0.8f + Noise() * 0.3f) * Decay(t, 28f) * 0.5f),
        UiCue.PieceInvalid => (0.2f, (t, _) =>
            (Note(t, 0f, 220f, 30f, Square) + Note(t, 0.09f, 220f, 30f, Square)) * 0.2f),
        UiCue.CircuitComplete => (0.8f, (t, _) =>
            Noise() * Decay(t, 20f) * 0.25f * (Sin(55f, t) > 0f ? 1f : 0.2f) +
            (Sin(523f, t) + Sin(784f, t) + Sin(1047f, t)) * Mathf.Clamp01((t - 0.08f) * 20f) * Decay(Mathf.Max(0f, t - 0.08f), 4f) * 0.14f),
        UiCue.IrOn => (0.4f, (t, l) => Sin(Mathf.Lerp(700f, 2600f, t / l), t) * 0.12f * Mathf.Clamp01(t * 20f) + Sin(120f, t) * 0.08f * Decay(t, 6f)),
        UiCue.IrOff => (0.3f, (t, l) => Sin(Mathf.Lerp(2400f, 500f, t / l), t) * 0.12f * Decay(t, 7f)),
        UiCue.LockTick => (0.05f, (t, _) => Sin(1850f, t) * Decay(t, 70f) * 0.22f),
        UiCue.LockAcquired => (0.22f, (t, _) =>
            (Note(t, 0f, 2200f, 40f, Sin) + Note(t, 0.09f, 2200f, 40f, Sin)) * 0.3f),
        UiCue.CaptureAccepted => (0.5f, (t, _) =>
            (Note(t, 0f, 784f, 12f, Square) + Note(t, 0.08f, 1175f, 10f, Square) + Note(t, 0.16f, 1568f, 8f, Square)) * 0.13f),
        UiCue.CaptureRejected => (0.4f, (t, _) =>
            (Square(196f, t) + Square(207f, t)) * Decay(t, 7f) * 0.17f),
        UiCue.MonitorMatch => (0.3f, (t, _) => Bell(1320f, t, 12f) * 0.3f),
        UiCue.MonitorMismatch => (0.08f, (t, _) => Sin(300f, t) * Decay(t, 50f) * 0.25f),
        UiCue.BankComplete => (0.9f, (t, l) =>
            (Tri(Mathf.Lerp(90f, 360f, Mathf.Clamp01(t / 0.5f)), t) * 0.35f + Sin(720f, t) * 0.15f * Mathf.Clamp01((t - 0.45f) * 10f)) * Decay(t, 2.8f)),
        UiCue.KnobZone => (0.25f, (t, _) => Bell(1760f, t, 14f) * 0.3f),
        UiCue.Alert => (0.5f, (t, _) =>
            (Note(t, 0f, 880f, 6f, Square) + Note(t, 0.2f, 660f, 6f, Square)) * 0.15f),
        _ => (0.05f, (t, _) => 0f)
    };
}
