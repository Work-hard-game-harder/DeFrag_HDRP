using System;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Clean display surface and channel OSD for the OFFICE3 broadcast TV.
/// The TV glass is a glossy HDRP Lit material, so the video drawn into it picks up ceiling and floor
/// reflections (the top third looked cut off, the bottom washed out). While the broadcast plays this
/// lays an emissive screen over the glass that shows the decoded video plus a "CH 0n" counter which
/// steps at every signal break. It only reads the shared VideoPlayer, so each peer shows the same
/// channel as the server-synchronised broadcast without any network state of its own.
/// </summary>
[DisallowMultipleComponent]
public sealed class BroadcastChannelScreen : MonoBehaviour
{
    [Serializable]
    private struct Channel
    {
        [Min(0f)] public float startTime;
        public string label;
    }

    [Header("Source")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Header("Screen (baked from the TV glass)")]
    [Tooltip("Renderer local space. Use the context menu 'Bake Screen From Material Slot' to fill these.")]
    [SerializeField] private Renderer screenRenderer;
    [SerializeField, Min(0)] private int screenMaterialIndex = 1;
    [SerializeField] private Vector3 screenCenter;
    [SerializeField] private Vector3 screenNormal = Vector3.right;
    [SerializeField] private Vector3 screenUp = Vector3.up;
    [SerializeField] private Vector2 screenSize = new(2.97f, 1.655f);
    [SerializeField, Min(0f)] private float surfaceOffset = 0.006f;

    [Header("Look")]
    [SerializeField] private Vector2Int resolution = new(1280, 720);
    [SerializeField, Min(1f)] private float framesPerSecond = 24f;
    [SerializeField, Min(0f)] private float glow = 1.1f;
    [SerializeField, Range(0f, 1f)] private float smoothness;
    [SerializeField] private Color osdColor = new(0.55f, 1f, 0.62f, 1f);

    [Header("Channels")]
    [Tooltip("각 구간이 시작하는 영상 시간(초). 신호 끊김(노이즈)도 한 채널로 센다.")]
    [SerializeField] private Channel[] channels =
    {
        new() { startTime = 0f, label = "CH 01" },
        new() { startTime = 5f, label = "CH 02" },
        new() { startTime = 6.2f, label = "CH 03" },
        new() { startTime = 10.88f, label = "CH 04" },
        new() { startTime = 12.08f, label = "CH 05" },
        new() { startTime = 16.58f, label = "CH 06" },
        new() { startTime = 21.12f, label = "CH 07" }
    };
    [SerializeField, Min(0f)] private float channelBannerSeconds = 1.1f;

    private OffscreenUiSurface surface;
    private RawImage video;
    private TMP_Text cornerLabel;
    private TMP_Text bannerLabel;
    private Image bannerBack;
    private MeshRenderer quad;
    private Material quadMaterial;
    private int currentChannel = -1;
    private float bannerUntil;

    private void Start()
    {
        // Scene reload is disabled in this project; reset the per-run state explicitly.
        currentChannel = -1;
        bannerUntil = 0f;
        if (videoPlayer == null) videoPlayer = GetComponent<VideoPlayer>();
        if (screenRenderer == null) screenRenderer = GetComponent<Renderer>();
        if (videoPlayer == null || screenRenderer == null)
        {
            Debug.LogWarning("[BroadcastChannelScreen] VideoPlayer or screen renderer is missing.", this);
            enabled = false;
            return;
        }
        Build();
        quad.enabled = false;
    }

    private void Build()
    {
        surface = new OffscreenUiSurface("Broadcast TV", resolution, framesPerSecond);
        RectTransform root = surface.Root;
        TMP_FontAsset font = DefragUiTheme.Current != null ? DefragUiTheme.Current.font : null;

        video = new GameObject("Video", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        video.transform.SetParent(root, false);
        video.raycastTarget = false;
        RuntimeUi.Stretch(video.rectTransform);

        // Corner OSD sits over the video's own small channel caption in the top-right.
        Image cornerBack = RuntimeUi.Panel("OSD Back", root, new Color(0f, 0f, 0f, 0.62f));
        RuntimeUi.Place(cornerBack.rectTransform, new Vector2(0.775f, 0.84f), new Vector2(0.985f, 0.975f));
        cornerLabel = RuntimeUi.Text("OSD", cornerBack.transform, 64f, TextAlignmentOptions.Center, font, osdColor);
        RuntimeUi.Stretch(cornerLabel.rectTransform);
        cornerLabel.textWrappingMode = TextWrappingModes.NoWrap;

        // Big centred number shown briefly whenever the channel steps, like an old TV changing channel.
        bannerBack = RuntimeUi.Panel("Banner Back", root, new Color(0f, 0f, 0f, 0.5f));
        RuntimeUi.Place(bannerBack.rectTransform, new Vector2(0.3f, 0.36f), new Vector2(0.7f, 0.64f));
        bannerLabel = RuntimeUi.Text("Banner", bannerBack.transform, 150f, TextAlignmentOptions.Center, font, osdColor);
        RuntimeUi.Stretch(bannerLabel.rectTransform);
        bannerLabel.textWrappingMode = TextWrappingModes.NoWrap;
        RuntimeUi.Scanlines(root, 0.05f);

        Transform parent = screenRenderer.transform;
        quad = DeviceScreenQuad.Create(parent, "Broadcast Screen", screenCenter + screenNormal.normalized * surfaceOffset,
            screenNormal, screenUp, screenSize, surface.Texture);
        quadMaterial = quad.sharedMaterial;
        // Black metal has no diffuse and zero F0, so room lights and their shadows cannot tint the picture;
        // the screen shows only its emission.
        if (quadMaterial.HasProperty("_BaseColor")) quadMaterial.SetColor("_BaseColor", Color.black);
        if (quadMaterial.HasProperty("_Metallic")) quadMaterial.SetFloat("_Metallic", 1f);
        if (quadMaterial.HasProperty("_Smoothness")) quadMaterial.SetFloat("_Smoothness", smoothness);
        DeviceScreenQuad.SetGlow(quadMaterial, Color.white, glow);
    }

    private void LateUpdate()
    {
        if (surface == null) return;

        bool playing = videoPlayer.isPlaying && videoPlayer.frame >= 0 && videoPlayer.targetTexture != null;
        if (quad.enabled != playing)
        {
            quad.enabled = playing;
            if (!playing) currentChannel = -1;
        }
        if (!playing) return;

        video.texture = videoPlayer.targetTexture;
        int channel = ChannelAt((float)videoPlayer.time);
        if (channel != currentChannel)
        {
            currentChannel = channel;
            string label = channel >= 0 ? channels[channel].label : string.Empty;
            cornerLabel.text = label;
            bannerLabel.text = label;
            bannerUntil = Time.unscaledTime + channelBannerSeconds;
        }

        float banner = Mathf.Clamp01((bannerUntil - Time.unscaledTime) / 0.25f);
        bannerBack.color = new Color(0f, 0f, 0f, 0.5f * banner);
        bannerLabel.alpha = banner;
        // A short flicker on the corner number right after a change draws the eye to it.
        bool flicker = banner > 0f && Mathf.Repeat(Time.unscaledTime, 0.16f) < 0.08f;
        cornerLabel.alpha = flicker ? 0.35f : 1f;
        surface.Tick();
    }

    private int ChannelAt(float time)
    {
        int found = -1;
        for (int i = 0; i < channels.Length; i++)
            if (time + 0.001f >= channels[i].startTime) found = i;
        return found;
    }

    private void OnDestroy()
    {
        surface?.Dispose();
        surface = null;
        if (quadMaterial != null) Destroy(quadMaterial);
    }

#if UNITY_EDITOR
    [ContextMenu("Bake Screen From Material Slot")]
    private void BakeScreen()
    {
        if (screenRenderer == null) screenRenderer = GetComponent<Renderer>();
        Mesh mesh = screenRenderer != null ? screenRenderer.GetComponent<MeshFilter>()?.sharedMesh : null;
        if (mesh == null || screenMaterialIndex >= mesh.subMeshCount)
        {
            Debug.LogError("[BroadcastChannelScreen] Screen mesh or material slot not found.", this);
            return;
        }

        Mesh.MeshDataArray data;
        try
        {
            data = Mesh.AcquireReadOnlyMeshData(mesh);
        }
        catch (InvalidOperationException exception)
        {
            // Non-readable imports only expose CPU data while the editor still holds it (e.g. in Play Mode).
            Debug.LogError($"[BroadcastChannelScreen] Cannot read '{mesh.name}': {exception.Message} Bake in Play Mode or enable Read/Write.", this);
            return;
        }
        using Mesh.MeshDataArray disposeData = data;
        Mesh.MeshData meshData = data[0];
        using var positions = new NativeArray<Vector3>(meshData.vertexCount, Allocator.Temp);
        using var normals = new NativeArray<Vector3>(meshData.vertexCount, Allocator.Temp);
        meshData.GetVertices(positions);
        meshData.GetNormals(normals);
        using var indices = new NativeArray<int>(meshData.GetSubMesh(screenMaterialIndex).indexCount, Allocator.Temp);
        meshData.GetIndices(indices, screenMaterialIndex, true);

        // Use the face geometry, not the (smoothed, slightly tilted) vertex normals: a tilted quad
        // would sink behind half of the glass. Vertex normals only decide which side is the front.
        Vector3 smoothed = Vector3.zero;
        foreach (int index in indices) smoothed += normals[index];
        Vector3 normal = Vector3.zero;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            Vector3 a = positions[indices[i]], b = positions[indices[i + 1]], c = positions[indices[i + 2]];
            Vector3 face = Vector3.Cross(b - a, c - a);
            normal += Vector3.Dot(face, smoothed) < 0f ? -face : face;
        }
        normal = normal.normalized;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
        Vector3 right = Vector3.Cross(up, normal).normalized;

        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        float depth = 0f;
        foreach (int index in indices)
        {
            Vector3 p = positions[index];
            var planar = new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up));
            min = Vector2.Min(min, planar);
            max = Vector2.Max(max, planar);
            depth += Vector3.Dot(p, normal);
        }
        depth /= Mathf.Max(1, indices.Length);
        Vector2 centre = (min + max) * 0.5f;

        UnityEditor.Undo.RecordObject(this, "Bake Broadcast Screen");
        screenNormal = normal;
        screenUp = up;
        screenSize = max - min;
        screenCenter = right * centre.x + up * centre.y + normal * depth;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[BroadcastChannelScreen] Baked centre={screenCenter}, normal={screenNormal}, size={screenSize}.", this);
    }
#endif
}
