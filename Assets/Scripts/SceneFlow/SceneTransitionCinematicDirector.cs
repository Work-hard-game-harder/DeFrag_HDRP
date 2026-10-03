using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Local presentation only. Listens to the netcode scene events and, the moment this peer starts
/// loading a scene listed in <see cref="SceneTransitionCinematicLibrary"/>, starts its cinematic so the
/// load happens behind the movie instead of freezing the game. The server still decides every scene
/// change through the existing NetworkSceneManager calls; nothing here sends network messages.
/// </summary>
public sealed class SceneTransitionCinematicDirector : MonoBehaviour
{
    private static SceneTransitionCinematicDirector instance;
    private NetworkManager watchedManager;
    private NetworkSceneManager watchedScenes;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (instance != null) return;
        var host = new GameObject("Scene Transition Cinematic Director") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(host);
        instance = host.AddComponent<SceneTransitionCinematicDirector>();
    }

    private void Update()
    {
        // NetworkManager.SceneManager is recreated per session, so re-subscribe when it changes.
        NetworkManager manager = NetworkManager.Singleton;
        NetworkSceneManager scenes = manager != null && manager.IsListening ? manager.SceneManager : null;
        if (scenes == watchedScenes) return;
        Unsubscribe();
        watchedManager = manager;
        watchedScenes = scenes;
        if (watchedScenes != null) watchedScenes.OnSceneEvent += OnSceneEvent;
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneEventType != SceneEventType.Load) return;
        if (watchedManager == null || sceneEvent.ClientId != watchedManager.LocalClientId) return;
        SceneTransitionCinematicLibrary library = SceneTransitionCinematicLibrary.Load();
        SceneTransitionCinematicLibrary.Entry entry = library != null ? library.Find(sceneEvent.SceneName) : null;
        if (entry != null) CinematicLoadingOverlay.Begin(entry, sceneEvent.SceneName);
    }

    private void Unsubscribe()
    {
        if (watchedScenes != null) watchedScenes.OnSceneEvent -= OnSceneEvent;
        watchedScenes = null;
        watchedManager = null;
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (instance == this) instance = null;
    }
}
