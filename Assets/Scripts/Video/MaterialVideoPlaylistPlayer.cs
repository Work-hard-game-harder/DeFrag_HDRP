using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace DeFrag.Video
{
    /// <summary>
    /// Plays a local playlist on every renderer slot that uses the assigned material.
    /// The video is decoded once and shared by all matching screens through property blocks.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class MaterialVideoPlaylistPlayer : MonoBehaviour
    {
        [SerializeField] private Material targetMaterial;
        [SerializeField] private Renderer[] targetRenderers = Array.Empty<Renderer>();
        [SerializeField] private VideoClip[] playlist = Array.Empty<VideoClip>();
        [SerializeField, Min(16)] private int outputWidth = 1920;
        [SerializeField, Min(16)] private int outputHeight = 1080;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool muteAudio = true;
        [SerializeField] private bool rotateVideo180;
        [SerializeField] private Shader rotationShader;
        [SerializeField] private string baseMapProperty = "_BaseColorMap";
        [SerializeField] private string emissionMapProperty = "_EmissiveColorMap";

        private readonly List<RendererSlot> rendererSlots = new();
        private VideoPlayer player;
        private RenderTexture output;
        private RenderTexture decodeOutput;
        private Material rotationMaterial;
        private int playlistIndex;
        private bool preparing;

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

        private void OnEnable()
        {
            if (targetMaterial == null || playlist == null || playlist.Length == 0)
            {
                enabled = false;
                return;
            }

            FindRendererSlots();
            CreateOutputTexture();
            ConfigurePlayer();
            ApplyOutputTexture();

            if (playOnEnable)
                PlayClip(0);
        }

        private void ConfigurePlayer()
        {
            player = GetComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.VideoClip;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = rotateVideo180 ? decodeOutput : output;
            player.isLooping = playlist.Length == 1;
            player.skipOnDrop = true;
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
            output = new RenderTexture(outputWidth, outputHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = $"{name}_Video",
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            output.Create();

            if (!rotateVideo180)
                return;

            decodeOutput = new RenderTexture(outputWidth, outputHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = $"{name}_VideoDecode",
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            decodeOutput.Create();

            if (rotationShader == null)
                rotationShader = Shader.Find("Hidden/DeFrag/VideoRotate180");
            if (rotationShader != null)
                rotationMaterial = new Material(rotationShader) { name = $"{name}_VideoRotation" };
            else
                Debug.LogError($"[{nameof(MaterialVideoPlaylistPlayer)}] 180-degree rotation shader is missing.", this);
        }

        private void ApplyOutputTexture()
        {
            int baseMapId = Shader.PropertyToID(baseMapProperty);
            int emissionMapId = Shader.PropertyToID(emissionMapProperty);
            int baseColorId = Shader.PropertyToID("_BaseColor");

            foreach (RendererSlot slot in rendererSlots)
            {
                var properties = new MaterialPropertyBlock();
                slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
                properties.SetTexture(baseMapId, output);
                properties.SetColor(baseColorId, Color.white);

                if (!string.IsNullOrWhiteSpace(emissionMapProperty))
                {
                    properties.SetTexture(emissionMapId, output);
                }

                slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
            }
        }

        private void LateUpdate()
        {
            if (rotateVideo180 && decodeOutput != null && output != null && rotationMaterial != null)
                Graphics.Blit(decodeOutput, output, rotationMaterial);
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

            if (output != null)
            {
                output.Release();
                Destroy(output);
                output = null;
            }

            if (decodeOutput != null)
            {
                decodeOutput.Release();
                Destroy(decodeOutput);
                decodeOutput = null;
            }

            if (rotationMaterial != null)
            {
                Destroy(rotationMaterial);
                rotationMaterial = null;
            }
        }
    }
}
