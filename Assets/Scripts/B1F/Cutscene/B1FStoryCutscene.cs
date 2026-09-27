using System;
using UnityEngine;

namespace DeFrag.B1F
{
    /// <summary>
    /// A realtime, local-only cutscene that can replace a story video. It may change the local
    /// view, lights and props, but never shared gameplay state; the server still advances the story.
    /// </summary>
    public abstract class B1FStoryCutscene : MonoBehaviour
    {
        public abstract float Duration { get; }
        public abstract bool IsPlaying { get; }

        /// <summary>Starts playback; onFinished runs once when the cutscene reaches its end.</summary>
        public abstract void Play(Action onFinished);

        /// <summary>Stops immediately and restores everything the cutscene touched.</summary>
        public abstract void Stop();
    }
}
