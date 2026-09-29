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
    private const string SessionKey = "DeFrag.SetupLobbyFScreenVideos.v6";

    private const string MaterialFolder = "Assets/Prefabs/Lobby Floor/FBX/Materials/";
    // Fewer simultaneous decoders is lighter and more reliable, so the five wall screens (3..7) live side by side
    // in one 3600x992 atlas (5 x 720x992, the screens' own ~0.72 aspect) that a single player decodes.
    // Both clips are baked by NexusScreenPromoBaker (DEFRAG > LobbyF > Bake Nexus Screen Videos).
    // All clips are H.264 Baseline (no B-frames):
    // Unity's Windows decoder warns about and stalls on reordered timestamps.
    private const string VerticalAtlasPath = NexusScreenPromoBaker.PortraitVideoPath;
    private const string VerticalAtlasOwner = "Vertical Screen 3";
    private const int VerticalAtlasTiles = 5;

    private readonly struct ScreenSetup
    {
        public ScreenSetup(string name, string materialPath, string rendererNamePrefix, bool rotate180, int width, int height,
            int screenTextureSize, int atlasTile, params string[] clipPaths)
        {
            Name = name;
            MaterialPath = materialPath;
            RendererNamePrefix = rendererNamePrefix;
            Rotate180 = rotate180;
            Width = width;
            Height = height;
            ScreenTextureSize = screenTextureSize;
            AtlasTile = atlasTile;
            ClipPaths = clipPaths;
        }

        public string Name { get; }
        public string MaterialPath { get; }
        public string RendererNamePrefix { get; }
        public bool Rotate180 { get; }
        public int Width { get; }
        public int Height { get; }
        public int ScreenTextureSize { get; }
        /// <summary>Tile of the vertical atlas this screen shows, or -1 when it plays its own clips.</summary>
        public int AtlasTile { get; }
        public string[] ClipPaths { get; }
        public bool DecodesAtlas => AtlasTile >= 0 && Name == VerticalAtlasOwner;
        public bool FollowsAtlas => AtlasTile >= 0 && Name != VerticalAtlasOwner;
    }

    private static readonly ScreenSetup[] Setups =
    {
        // One looping clip (swapping clips on a VideoPlayer froze it on frame 0); 2464x800 matches the ~3.08:1 screen.
        new("Large Screen 1-2 Loop", MaterialFolder + "Monitor_glass대형.mat", "대형스크린", false, 2464, 800, 2048, -1,
            NexusScreenPromoBaker.WideVideoPath),
        new(VerticalAtlasOwner, MaterialFolder + "Monitor_glass가로1.mat", "Screen_A", true, 3600, 992, 1280, 0, VerticalAtlasPath),
        new("Vertical Screen 4", MaterialFolder + "Monitor_glass가로2.mat", "Screen_A", true, 3600, 992, 1280, 1),
        new("Vertical Screen 5", MaterialFolder + "Monitor_glass가로3.mat", "Screen_A", true, 3600, 992, 1280, 2),
        new("Vertical Screen 6", MaterialFolder + "Monitor_glass가로4.mat", "Screen_A", true, 3600, 992, 1280, 3),
        new("Vertical Screen 7", MaterialFolder + "Monitor_glass가로5.mat", "Screen_A", true, 3600, 992, 1280, 4),
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
        Debug.Log("LobbyF screen videos configured: large 1→2 loop, vertical 3→7 atlas on materials 1→5 (UV-mapped, aspect kept).");
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
            SerializedProperty playlist = serialized.FindProperty("playlist");
            if (serialized.FindProperty("targetRenderers").arraySize == 0 ||
                serialized.FindProperty("rotateVideo180").boolValue != setup.Rotate180 ||
                serialized.FindProperty("fitMode").enumValueIndex != (int)ScreenFitMode.Fill ||
                serialized.FindProperty("rotationShader").objectReferenceValue == null ||
                serialized.FindProperty("screenFitShader").objectReferenceValue == null ||
                serialized.FindProperty("screenMappings").arraySize != serialized.FindProperty("targetRenderers").arraySize ||
                (serialized.FindProperty("decodeSource").objectReferenceValue != null) != setup.FollowsAtlas)
                return false;
            if (!setup.FollowsAtlas &&
                (playlist.arraySize == 0 ||
                 AssetDatabase.GetAssetPath(playlist.GetArrayElementAtIndex(0).objectReferenceValue) != setup.ClipPaths[0]))
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

        if (material == null || Array.Exists(clips, clip => clip == null) || (setup.FollowsAtlas && root.Find(VerticalAtlasOwner) == null))
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
        // The clips are authored at each screen's aspect, so Fill only trims the last percent instead of adding blurred bars.
        serialized.FindProperty("fitMode").enumValueIndex = (int)ScreenFitMode.Fill;
        serialized.FindProperty("rotationShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/HiddenVideoRotate180.shader");
        // 1080p H.264 copies of the 4K HEVC sources: lighter, and every Windows PC can decode them.
        serialized.FindProperty("screenFitShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/HiddenVideoScreenFit.shader");
        serialized.FindProperty("screenTextureSize").intValue = setup.ScreenTextureSize;
        Transform atlasOwner = setup.FollowsAtlas ? root.Find(VerticalAtlasOwner) : null;
        serialized.FindProperty("decodeSource").objectReferenceValue =
            atlasOwner != null ? atlasOwner.GetComponent<MaterialVideoPlaylistPlayer>() : null;
        serialized.FindProperty("sourceRect").rectValue = setup.AtlasTile >= 0
            ? new Rect((float)setup.AtlasTile / VerticalAtlasTiles, 0f, 1f / VerticalAtlasTiles, 1f)
            : new Rect(0f, 0f, 1f, 1f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        // Screen meshes map their UVs sideways/flipped and only use part of the texture: measure them.
        controller.BakeScreenMappings();

        EditorUtility.SetDirty(videoPlayer);
        EditorUtility.SetDirty(controller);
    }
}
