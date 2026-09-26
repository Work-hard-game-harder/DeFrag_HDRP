using System;
using DeFrag.Video;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

[InitializeOnLoad]
internal static class SetupLobbyFScreenVideosOnce
{
    private const string ScenePath = "Assets/Scene/LobbyF.unity";
    private const string RootName = "[LobbyF] Screen Video Players";
    private const string SessionKey = "DeFrag.SetupLobbyFScreenVideos.v3";

    private readonly struct ScreenSetup
    {
        public ScreenSetup(string name, string materialPath, string rendererNamePrefix, bool rotate180, int width, int height, params string[] clipPaths)
        {
            Name = name;
            MaterialPath = materialPath;
            RendererNamePrefix = rendererNamePrefix;
            Rotate180 = rotate180;
            Width = width;
            Height = height;
            ClipPaths = clipPaths;
        }

        public string Name { get; }
        public string MaterialPath { get; }
        public string RendererNamePrefix { get; }
        public bool Rotate180 { get; }
        public int Width { get; }
        public int Height { get; }
        public string[] ClipPaths { get; }
    }

    private static readonly ScreenSetup[] Setups =
    {
        new("Large Screen 1-2 Loop", "Assets/Prefabs/Lobby Floor/FBX/Materials/Monitor_glass대형.mat", "대형스크린", false, 1920, 1080,
            "Assets/Movies/대형스크린1.mp4", "Assets/Movies/대형스크린2.mp4"),
        new("Vertical Screen 3", "Assets/Prefabs/Lobby Floor/FBX/Materials/Monitor_glass가로1.mat", "Screen_A", true, 1080, 1920,
            "Assets/Movies/세로스크린3.mp4"),
        new("Vertical Screen 4", "Assets/Prefabs/Lobby Floor/FBX/Materials/Monitor_glass가로2.mat", "Screen_A", true, 1080, 1920,
            "Assets/Movies/세로스크린4.mp4"),
        new("Vertical Screen 5", "Assets/Prefabs/Lobby Floor/FBX/Materials/Monitor_glass가로3.mat", "Screen_A", true, 1080, 1920,
            "Assets/Movies/세로스크린5.mp4"),
        new("Vertical Screen 6", "Assets/Prefabs/Lobby Floor/FBX/Materials/Monitor_glass가로4.mat", "Screen_A", true, 1080, 1920,
            "Assets/Movies/세로스크린6.mp4"),
        new("Vertical Screen 7", "Assets/Prefabs/Lobby Floor/FBX/Materials/Monitor_glass가로5.mat", "Screen_A", true, 1080, 1920,
            "Assets/Movies/세로스크린7.mp4"),
    };

    static SetupLobbyFScreenVideosOnce()
    {
        EditorApplication.delayCall += SetupCurrentLobbyScene;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += SetupCurrentLobbyScene;
        };
    }

    [MenuItem("Tools/LobbyF/Setup Screen Videos")]
    private static void SetupFromMenu() => SetupCurrentLobbyScene(force: true);

    private static void SetupCurrentLobbyScene() => SetupCurrentLobbyScene(force: false);

    private static void SetupCurrentLobbyScene(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (!force && SessionState.GetBool(SessionKey, false))
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            return;

        GameObject existingRoot = GameObject.Find(RootName);
        if (!force && IsConfigured(existingRoot))
        {
            SessionState.SetBool(SessionKey, true);
            return;
        }

        SessionState.SetBool(SessionKey, true);
        GameObject root = existingRoot;
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create LobbyF screen video players");
        }

        foreach (ScreenSetup setup in Setups)
            CreateOrUpdatePlayer(root.transform, setup);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("LobbyF screen videos configured: large 1→2 loop, vertical 3→7 on materials 1→5.");
    }

    private static bool IsConfigured(GameObject root)
    {
        if (root == null)
            return false;

        foreach (ScreenSetup setup in Setups)
        {
            Transform child = root.transform.Find(setup.Name);
            MaterialVideoPlaylistPlayer controller = child != null
                ? child.GetComponent<MaterialVideoPlaylistPlayer>()
                : null;
            if (controller == null)
                return false;

            var serialized = new SerializedObject(controller);
            if (serialized.FindProperty("targetRenderers").arraySize == 0 ||
                serialized.FindProperty("rotateVideo180").boolValue != setup.Rotate180 ||
                serialized.FindProperty("rotationShader").objectReferenceValue == null)
                return false;
        }

        return true;
    }

    private static void CreateOrUpdatePlayer(Transform root, ScreenSetup setup)
    {
        Transform existing = root.Find(setup.Name);
        GameObject target = existing != null ? existing.gameObject : new GameObject(setup.Name);
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(target, "Create screen video player");
            target.transform.SetParent(root, false);
        }

        VideoPlayer videoPlayer = target.GetComponent<VideoPlayer>();
        if (videoPlayer == null)
            videoPlayer = Undo.AddComponent<VideoPlayer>(target);

        MaterialVideoPlaylistPlayer controller = target.GetComponent<MaterialVideoPlaylistPlayer>();
        if (controller == null)
            controller = Undo.AddComponent<MaterialVideoPlaylistPlayer>(target);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(setup.MaterialPath);
        var clips = new VideoClip[setup.ClipPaths.Length];
        for (int index = 0; index < clips.Length; index++)
            clips[index] = AssetDatabase.LoadAssetAtPath<VideoClip>(setup.ClipPaths[index]);

        if (material == null || Array.Exists(clips, clip => clip == null))
        {
            Debug.LogError($"Could not configure {setup.Name}: a material or video clip is missing.", target);
            return;
        }

        var serialized = new SerializedObject(controller);
        serialized.FindProperty("targetMaterial").objectReferenceValue = material;
        Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
        Renderer[] targets = Array.FindAll(renderers, renderer =>
            renderer.gameObject.name.StartsWith(setup.RendererNamePrefix, StringComparison.Ordinal) &&
            Array.IndexOf(renderer.sharedMaterials, material) >= 0);
        SerializedProperty targetRenderers = serialized.FindProperty("targetRenderers");
        targetRenderers.arraySize = targets.Length;
        for (int index = 0; index < targets.Length; index++)
            targetRenderers.GetArrayElementAtIndex(index).objectReferenceValue = targets[index];
        SerializedProperty playlist = serialized.FindProperty("playlist");
        playlist.arraySize = clips.Length;
        for (int index = 0; index < clips.Length; index++)
            playlist.GetArrayElementAtIndex(index).objectReferenceValue = clips[index];
        serialized.FindProperty("outputWidth").intValue = setup.Width;
        serialized.FindProperty("outputHeight").intValue = setup.Height;
        serialized.FindProperty("playOnEnable").boolValue = true;
        serialized.FindProperty("muteAudio").boolValue = true;
        serialized.FindProperty("rotateVideo180").boolValue = setup.Rotate180;
        serialized.FindProperty("rotationShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/HiddenVideoRotate180.shader");
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(videoPlayer);
        EditorUtility.SetDirty(controller);
    }
}
