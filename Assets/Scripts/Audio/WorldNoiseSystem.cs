using System;
using UnityEngine;

/// <summary>
/// Gameplay-only world noise channel. Audio playback and monster perception remain separate:
/// callers play their presentation sound independently and publish only position/radius here.
/// </summary>
public static class WorldNoiseSystem
{
    public static event Action<Vector3, float> NoiseEmitted;
    public static event Action<Vector3, float> UrgentNoiseEmitted;

    public static void Emit(Vector3 position, float radius)
    {
        if (radius <= 0f)
            return;

        NoiseEmitted?.Invoke(position, radius);
    }

    public static void EmitUrgent(Vector3 position, float radius)
    {
        if (radius <= 0f)
            return;

        UrgentNoiseEmitted?.Invoke(position, radius);
    }
}
