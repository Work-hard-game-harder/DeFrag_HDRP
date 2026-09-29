using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace DeFrag.Video
{
    public enum ScreenFitMode
    {
        /// <summary>Whole video visible; the bars are filled with a blurred copy of the frame.</summary>
        Fit,
        /// <summary>Video covers the screen; the overflow is cropped.</summary>
        Fill,
        /// <summary>Video is stretched to the screen (old behaviour).</summary>
        Stretch
    }

    /// <summary>
    /// How one screen mesh samples its texture: an affine map from mesh UV to screen space
    /// (0..1, x = right, y = up as seen from the front) and the screen's physical aspect ratio.
    /// Baked in the editor from the mesh (see <see cref="MaterialVideoPlaylistPlayer.BakeScreenMappings"/>).
    /// </summary>
    [Serializable]
    public struct ScreenUvMapping
    {
        public Renderer renderer;
        public Vector3 uvToScreenX;
        public Vector3 uvToScreenY;
        [Min(0.01f)] public float aspect;
    }

    /// <summary>
    /// Plays a local playlist on every renderer slot that uses the assigned material.
    /// The video is decoded once and shared by all matching screens through property blocks.
    /// Screens with a baked UV mapping get the video upright and at its own aspect ratio,
    /// whatever part of the texture their mesh UVs cover.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class MaterialVideoPlaylistPlayer : MonoBehaviour
    {
        [SerializeField] private Material targetMaterial;
        [SerializeField] private Renderer[] targetRenderers = Array.Empty<Renderer>();
        [SerializeField] private VideoClip[] playlist = Array.Empty<VideoClip>();
        [Tooltip("영상 디코딩 해상도.")]
        [SerializeField, Min(16)] private int outputWidth = 1920;
        [SerializeField, Min(16)] private int outputHeight = 1080;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool muteAudio = true;
        [Tooltip("UV 매핑이 없는 화면에만 적용된다.")]
        [SerializeField] private bool rotateVideo180;
        [SerializeField] private Shader rotationShader;
        [SerializeField] private string baseMapProperty = "_BaseColorMap";
        [SerializeField] private string emissionMapProperty = "_EmissiveColorMap";

        [Header("Screen Fit")]
        [Tooltip("화면 메시별 UV→화면 좌표 매핑. 컴포넌트 우클릭 > Bake Screen UV Mappings로 만든다.")]
        [SerializeField] private ScreenUvMapping[] screenMappings = Array.Empty<ScreenUvMapping>();
        [SerializeField] private ScreenFitMode fitMode = ScreenFitMode.Fit;
        [SerializeField] private Shader screenFitShader;
        [Tooltip("화면 텍스처 한 변의 해상도 (메시 UV 공간 전체 기준).")]
        [SerializeField, Min(64)] private int screenTextureSize = 1536;
        [Tooltip("여백을 채우는 흐린 배경의 밉 레벨 (클수록 흐림).")]
        [SerializeField, Range(0f, 8f)] private float fillBlur = 5f;
        [SerializeField, Range(0f, 1f)] private float fillBrightness = 0.3f;
        [Tooltip("영상과 여백 경계의 부드러움 (화면 폭 대비).")]
        [SerializeField, Range(0f, 0.1f)] private float fillEdgeSoftness = 0.015f;

        [Header("Shared Decode (Atlas)")]
        [Tooltip("지정하면 직접 디코딩하지 않고 이 플레이어가 디코딩한 영상의 Source Rect 영역을 쓴다. " +
                 "동시에 재생할 수 있는 영상 디코더 수가 제한적이라, 여러 영상을 한 파일(아틀라스)에 모을 때 쓴다.")]
        [SerializeField] private MaterialVideoPlaylistPlayer decodeSource;
        [Tooltip("디코딩된 영상에서 이 화면이 쓰는 영역 (0..1, 아래가 y=0). 아틀라스를 직접 디코딩하는 플레이어도 자기 칸을 지정한다.")]
        [SerializeField] private Rect sourceRect = new(0f, 0f, 1f, 1f);

        private static readonly int MapXId = Shader.PropertyToID("_MapX");
        private static readonly int MapYId = Shader.PropertyToID("_MapY");
        private static readonly int VideoRectId = Shader.PropertyToID("_VideoRect");
        private static readonly int FillRectId = Shader.PropertyToID("_FillRect");
        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int SourceRectId = Shader.PropertyToID("_SourceRect");
        private const float MappingTolerance = 0.002f;
        // A decoder that fell behind during a load hitch can sit on one frame until the clip loops.
        private const float StallRestartSeconds = 2.5f;

        private readonly List<RendererSlot> rendererSlots = new();
        private readonly List<ScreenGroup> screenGroups = new();
        private VideoPlayer player;
        private RenderTexture output;
        private RenderTexture decodeOutput;
        private Material rotationMaterial;
        private Material fitMaterial;
        private int playlistIndex;
        private bool preparing;
        private long lastDecodedFrame = long.MinValue;
        private int decodeVersion;
        private int drawnVersion;
        private float lastFrameChangeTime;

        private readonly struct RendererSlot
        {
            public RendererSlot(Renderer renderer, int materialIndex)
            {
                Renderer = renderer;
                MaterialIndex = materialIndex;
            }

            public Renderer Renderer { get; }
            public int MaterialIndex { get; }
        }

        /// <summary>Screens that sample the texture the same way share one output texture.</summary>
        private sealed class ScreenGroup
        {
            public Vector3 MapX;
            public Vector3 MapY;
            public float Aspect;
            public ScreenFitMode Mode;
            public RenderTexture Texture;
            public readonly List<RendererSlot> Slots = new();
        }

        private bool UsesScreenFit => fitMaterial != null;
        private bool IsFollower => decodeSource != null && decodeSource != this;

        /// <summary>Aspect ratio of the decoded frame (the whole atlas when several videos share it).</summary>
        private float DecodedAspect => player != null && player.clip != null && player.clip.height > 0
            ? (float)player.clip.width / player.clip.height
            : (float)outputWidth / outputHeight;

        private void OnEnable()
        {
            if (targetMaterial == null || (!IsFollower && (playlist == null || playlist.Length == 0)))
            {
                enabled = false;
                return;
            }

            FindRendererSlots();
            CreateOutputTexture();
            if (IsFollower && !UsesScreenFit)
            {
                Debug.LogError($"[{nameof(MaterialVideoPlaylistPlayer)}] {name}: shared decode needs the screen fit shader.", this);
                enabled = false;
                return;
            }

            if (!IsFollower)
                ConfigurePlayer();
            ApplyOutputTexture();

            if (playOnEnable && !IsFollower)
                PlayClip(0);
        }

        private void ConfigurePlayer()
        {
            player = GetComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.VideoClip;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = decodeOutput != null ? decodeOutput : output;
            player.aspectRatio = VideoAspectRatio.Stretch;
            player.isLooping = playlist.Length == 1;
            // Ambient screens need no clock sync; skipping to catch up after a hitch froze the picture.
            player.skipOnDrop = false;
            player.waitForFirstFrame = true;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.audioOutputMode = muteAudio ? VideoAudioOutputMode.None : VideoAudioOutputMode.Direct;
            player.prepareCompleted -= HandlePrepared;
            player.prepareCompleted += HandlePrepared;
            player.loopPointReached -= HandleLoopPointReached;
            player.loopPointReached += HandleLoopPointReached;
            player.errorReceived -= HandleError;
            player.errorReceived += HandleError;
        }

        private void FindRendererSlots()
        {
            rendererSlots.Clear();
            Renderer[] renderers = targetRenderers != null && targetRenderers.Length > 0
                ? targetRenderers
                : FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            foreach (Renderer candidate in renderers)
            {
                if (candidate == null)
                    continue;
                Material[] materials = candidate.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    if (materials[index] == targetMaterial)
                        rendererSlots.Add(new RendererSlot(candidate, index));
                }
            }
        }

        private void CreateOutputTexture()
        {
            if (screenFitShader == null)
                screenFitShader = Shader.Find("Hidden/DeFrag/VideoScreenFit");
            if (screenFitShader != null && screenFitShader.isSupported)
                fitMaterial = new Material(screenFitShader) { name = $"{name}_VideoScreenFit" };

            if (UsesScreenFit && IsFollower)
            {
                BuildScreenGroups();
                return;
            }

            if (UsesScreenFit)
            {
                // Decode once (with mips for the blurred fill), then draw one texture per UV layout.
                decodeOutput = CreateTexture($"{name}_VideoDecode", outputWidth, outputHeight, mips: true);
                decodeOutput.autoGenerateMips = false;
                decodeOutput.Create();
                BuildScreenGroups();
                return;
            }

            output = CreateTexture($"{name}_Video", outputWidth, outputHeight, mips: false);
            output.Create();

            if (!rotateVideo180)
                return;

            decodeOutput = CreateTexture($"{name}_VideoDecode", outputWidth, outputHeight, mips: false);
            decodeOutput.Create();

            if (rotationShader == null)
                rotationShader = Shader.Find("Hidden/DeFrag/VideoRotate180");
            if (rotationShader != null)
                rotationMaterial = new Material(rotationShader) { name = $"{name}_VideoRotation" };
            else
                Debug.LogError($"[{nameof(MaterialVideoPlaylistPlayer)}] 180-degree rotation shader is missing.", this);
        }

        private static RenderTexture CreateTexture(string textureName, int width, int height, bool mips)
        {
            return new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = textureName,
                useMipMap = mips,
                autoGenerateMips = mips,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear
            };
        }

        private void BuildScreenGroups()
        {
            screenGroups.Clear();
            foreach (RendererSlot slot in rendererSlots)
            {
                GetMapping(slot.Renderer, out Vector3 mapX, out Vector3 mapY, out float aspect, out ScreenFitMode mode);
                ScreenGroup group = screenGroups.Find(candidate =>
                    candidate.Mode == mode &&
                    (candidate.MapX - mapX).sqrMagnitude < MappingTolerance &&
                    (candidate.MapY - mapY).sqrMagnitude < MappingTolerance &&
                    Mathf.Abs(candidate.Aspect - aspect) < 0.01f);
                if (group == null)
                {
                    group = new ScreenGroup { MapX = mapX, MapY = mapY, Aspect = aspect, Mode = mode };
                    group.Texture = CreateTexture($"{name}_Screen{screenGroups.Count}", screenTextureSize, screenTextureSize, mips: true);
                    group.Texture.Create();
                    Graphics.Blit(Texture2D.blackTexture, group.Texture);
                    screenGroups.Add(group);
                }
                group.Slots.Add(slot);
            }
        }

        private void GetMapping(Renderer target, out Vector3 mapX, out Vector3 mapY, out float aspect, out ScreenFitMode mode)
        {
            if (screenMappings != null)
            {
                foreach (ScreenUvMapping mapping in screenMappings)
                {
                    if (mapping.renderer != target)
                        continue;
                    mapX = mapping.uvToScreenX;
                    mapY = mapping.uvToScreenY;
                    aspect = Mathf.Max(0.01f, mapping.aspect);
                    mode = fitMode;
                    return;
                }
            }

            // No baked mapping: the screen shows the whole texture (optionally turned 180 degrees).
            mapX = rotateVideo180 ? new Vector3(-1f, 0f, 1f) : new Vector3(1f, 0f, 0f);
            mapY = rotateVideo180 ? new Vector3(0f, -1f, 1f) : new Vector3(0f, 1f, 0f);
            aspect = (float)outputWidth / outputHeight;
            mode = ScreenFitMode.Stretch;
        }

        private void ApplyOutputTexture()
        {
            if (UsesScreenFit)
            {
                foreach (ScreenGroup group in screenGroups)
                {
                    foreach (RendererSlot slot in group.Slots)
                        ApplyTexture(slot, group.Texture);
                }
                return;
            }

            foreach (RendererSlot slot in rendererSlots)
                ApplyTexture(slot, output);
        }

        private void ApplyTexture(RendererSlot slot, Texture texture)
        {
            var properties = new MaterialPropertyBlock();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            properties.SetTexture(Shader.PropertyToID(baseMapProperty), texture);
            properties.SetColor(Shader.PropertyToID("_BaseColor"), Color.white);
            if (!string.IsNullOrWhiteSpace(emissionMapProperty))
                properties.SetTexture(Shader.PropertyToID(emissionMapProperty), texture);
            if (UsesScreenFit)
            {
                // The baked mapping already handles orientation, so ignore hand-tuned tiling on the material.
                properties.SetVector(Shader.PropertyToID(baseMapProperty + "_ST"), new Vector4(1f, 1f, 0f, 0f));
                if (!string.IsNullOrWhiteSpace(emissionMapProperty))
                    properties.SetVector(Shader.PropertyToID(emissionMapProperty + "_ST"), new Vector4(1f, 1f, 0f, 0f));
            }
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        private void LateUpdate()
        {
            if (UsesScreenFit)
            {
                UpdateDecodedFrame();
                DrawScreens();
                return;
            }

            if (rotateVideo180 && decodeOutput != null && output != null && rotationMaterial != null)
                Graphics.Blit(decodeOutput, output, rotationMaterial);
        }

        /// <summary>Decoding player: refresh the mips once per new video frame and bump the version.</summary>
        private void UpdateDecodedFrame()
        {
            if (IsFollower || player == null || decodeOutput == null)
                return;
            if (!player.isPlaying || preparing)
            {
                lastFrameChangeTime = Time.unscaledTime;
                return;
            }

            long frame = player.frame;
            if (frame < 0 || frame == lastDecodedFrame)
            {
                if (Time.unscaledTime - lastFrameChangeTime > StallRestartSeconds)
                {
                    lastFrameChangeTime = Time.unscaledTime;
                    PlayClip(playlistIndex);
                }
                return;
            }
            lastDecodedFrame = frame;
            lastFrameChangeTime = Time.unscaledTime;
            decodeOutput.GenerateMips();
            decodeVersion++;
        }

        private void DrawScreens()
        {
            MaterialVideoPlaylistPlayer decoder = IsFollower ? decodeSource : this;
            if (decoder.decodeOutput == null || decoder.decodeVersion == drawnVersion)
                return;
            drawnVersion = decoder.decodeVersion;

            Rect source = sourceRect;
            float videoAspect = decoder.DecodedAspect * source.width / Mathf.Max(0.0001f, source.height);
            fitMaterial.SetVector(FillId, new Vector4(fillBlur, fillBrightness, fillEdgeSoftness, 0f));
            fitMaterial.SetVector(SourceRectId, new Vector4(source.x, source.y, source.width, source.height));

            foreach (ScreenGroup group in screenGroups)
            {
                fitMaterial.SetVector(MapXId, group.MapX);
                fitMaterial.SetVector(MapYId, group.MapY);
                fitMaterial.SetVector(VideoRectId, FitRect(group.Mode, group.Aspect, videoAspect));
                fitMaterial.SetVector(FillRectId, FitRect(ScreenFitMode.Fill, group.Aspect, videoAspect));
                Graphics.Blit(decoder.decodeOutput, group.Texture, fitMaterial);
            }
        }

        /// <summary>Scale/offset turning screen coordinates (0..1) into video UV.</summary>
        private static Vector4 FitRect(ScreenFitMode mode, float screenAspect, float videoAspect)
        {
            Vector2 scale = Vector2.one;
            if (mode != ScreenFitMode.Stretch)
            {
                bool videoNarrower = videoAspect < screenAspect;
                // Fit leaves bars beside a narrower video (above a wider one); Fill crops the other way.
                if (videoNarrower == (mode == ScreenFitMode.Fit))
                    scale.x = screenAspect / videoAspect;
                else
                    scale.y = videoAspect / screenAspect;
            }
            return new Vector4(scale.x, scale.y, 0.5f - 0.5f * scale.x, 0.5f - 0.5f * scale.y);
        }

        private void PlayClip(int index)
        {
            VideoClip clip = playlist[index];
            if (clip == null)
            {
                AdvancePlaylist();
                return;
            }

            playlistIndex = index;
            preparing = true;
            lastDecodedFrame = long.MinValue;
            player.Stop();
            player.clip = clip;
            player.Prepare();
        }

        private void HandlePrepared(VideoPlayer source)
        {
            preparing = false;
            source.Play();
        }

        private void HandleLoopPointReached(VideoPlayer source)
        {
            if (playlist.Length > 1)
                AdvancePlaylist();
        }

        private void AdvancePlaylist()
        {
            if (preparing || playlist.Length == 0)
                return;

            PlayClip((playlistIndex + 1) % playlist.Length);
        }

        private void HandleError(VideoPlayer source, string message)
        {
            preparing = false;
            Debug.LogError($"[{nameof(MaterialVideoPlaylistPlayer)}] {name}: {message}", this);
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.prepareCompleted -= HandlePrepared;
                player.loopPointReached -= HandleLoopPointReached;
                player.errorReceived -= HandleError;
                player.Stop();
                player.targetTexture = null;
            }

            foreach (RendererSlot slot in rendererSlots)
            {
                if (slot.Renderer != null)
                    slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
            }
            rendererSlots.Clear();

            foreach (ScreenGroup group in screenGroups)
                ReleaseTexture(ref group.Texture);
            screenGroups.Clear();
            drawnVersion = 0;
            ReleaseTexture(ref output);
            ReleaseTexture(ref decodeOutput);

            if (rotationMaterial != null)
            {
                Destroy(rotationMaterial);
                rotationMaterial = null;
            }

            if (fitMaterial != null)
            {
                Destroy(fitMaterial);
                fitMaterial = null;
            }
        }

        private static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            Destroy(texture);
            texture = null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Measures every target screen mesh (the submesh that uses <see cref="targetMaterial"/>) and stores
        /// how its UVs map onto the physical screen, so the video is drawn upright and uncropped.
        /// </summary>
        [ContextMenu("Bake Screen UV Mappings")]
        public void BakeScreenMappings()
        {
            FindRendererSlots();
            var mappings = new List<ScreenUvMapping>();
            foreach (RendererSlot slot in rendererSlots)
            {
                if (TryMeasureScreen(slot.Renderer, slot.MaterialIndex, out ScreenUvMapping mapping))
                    mappings.Add(mapping);
                else
                    Debug.LogWarning($"[{nameof(MaterialVideoPlaylistPlayer)}] Could not measure {slot.Renderer.name}.", slot.Renderer);
            }
            rendererSlots.Clear();

            UnityEditor.Undo.RecordObject(this, "Bake Screen UV Mappings");
            screenMappings = mappings.ToArray();
            if (screenFitShader == null)
                screenFitShader = Shader.Find("Hidden/DeFrag/VideoScreenFit");
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private static bool TryMeasureScreen(Renderer target, int submesh, out ScreenUvMapping mapping)
        {
            mapping = default;
            MeshFilter filter = target.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || submesh >= mesh.subMeshCount)
                return false;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            mesh.GetVertices(vertices);
            mesh.GetNormals(normals);
            mesh.GetUVs(0, uvs);
            int[] indices = mesh.GetIndices(submesh);
            if (indices.Length < 3 || uvs.Count != vertices.Count)
                return false;

            Transform transformRef = target.transform;
            Vector3 normal = Vector3.zero;
            foreach (int index in indices)
                normal += normals.Count == vertices.Count ? transformRef.TransformDirection(normals[index]) : Vector3.zero;
            if (normal.sqrMagnitude < 1e-6f)
                return false;
            normal.Normalize();

            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 0.01f)
                up = Vector3.ProjectOnPlane(transformRef.up, normal);
            up.Normalize();
            // Viewer looks along -normal; Unity is left-handed, so right = up x forward.
            Vector3 right = Vector3.Cross(up, -normal);

            var points = new List<Vector2>();
            var coords = new List<Vector2>();
            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);
            foreach (int index in indices)
            {
                Vector3 world = transformRef.TransformPoint(vertices[index]);
                var point = new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, up));
                points.Add(point);
                coords.Add(uvs[index]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            Vector2 size = max - min;
            if (size.x < 1e-4f || size.y < 1e-4f)
                return false;

            // Least squares: screen = A * (u, v, 1).
            Matrix4x4 normalMatrix = Matrix4x4.zero;
            Vector3 sumX = Vector3.zero, sumY = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                var q = new Vector3(coords[i].x, coords[i].y, 1f);
                Vector2 screen = (points[i] - min) / size;
                for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    normalMatrix[r, c] += q[r] * q[c];
                sumX += q * screen.x;
                sumY += q * screen.y;
            }
            normalMatrix[3, 3] = 1f;
            if (Mathf.Abs(normalMatrix.determinant) < 1e-9f)
                return false;
            Matrix4x4 inverse = normalMatrix.inverse;

            mapping = new ScreenUvMapping
            {
                renderer = target,
                uvToScreenX = inverse.MultiplyVector(sumX),
                uvToScreenY = inverse.MultiplyVector(sumY),
                aspect = size.x / size.y
            };
            return true;
        }
#endif
    }
}
