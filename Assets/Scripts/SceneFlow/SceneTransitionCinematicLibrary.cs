using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Which cinematic covers the loading of each destination scene. Whenever the server starts a
/// networked load of a listed scene (elevator, emergency exit, stage selection...), every peer plays
/// the entry locally while the scene loads behind it. Lives at Resources/SceneFlow/SceneTransitionCinematics.
/// </summary>
[CreateAssetMenu(menuName = "DEFRAG/Scene Transition Cinematics", fileName = "SceneTransitionCinematics")]
public sealed class SceneTransitionCinematicLibrary : ScriptableObject
{
    public const string ResourcePath = "SceneFlow/SceneTransitionCinematics";

    [Serializable]
    public sealed class Entry
    {
        [Tooltip("Build scene name whose loading this cinematic covers.")]
        public string destinationScene;
        [Tooltip("Pre-rendered movie. Leave empty to show the placeholder card instead.")]
        public VideoClip video;
        [Tooltip("Separate soundtrack, used as the playback clock (survives loading hitches). " +
                 "Empty: the movie's own audio track plays directly.")]
        public AudioClip soundtrack;
        [Header("Placeholder (no movie yet)")]
        public string placeholderTitle = "";
        public string placeholderSubtitle = "";
        [Min(1f)] public float placeholderSeconds = 4f;
        [Header("Playback")]
        public bool allowSkip = true;
    }

    [SerializeField] private List<Entry> entries = new();

    private static SceneTransitionCinematicLibrary cached;

    // Enter Play Mode Options에서 도메인 리로드를 꺼도 이전 실행의 캐시가 남지 않게 합니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cached = null;

    public static SceneTransitionCinematicLibrary Load()
    {
        if (cached == null) cached = Resources.Load<SceneTransitionCinematicLibrary>(ResourcePath);
        return cached;
    }

    public Entry Find(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return null;
        return entries.Find(e => e != null &&
            string.Equals(e.destinationScene, sceneName.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
