using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DeFrag.Rendering
{
    [Serializable]
    [VolumeComponentMenu("Post-processing/Custom/TV Monster Kino Glitch")]
    public sealed class TvMonsterGlitchPostProcess : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        private const string ShaderName = "Hidden/DeFrag/KinoTvMonsterGlitch";

        public ClampedFloatParameter intensity = new(0f, 0f, 1f);
        public ClampedFloatParameter scanLineJitter = new(0.65f, 0f, 1f);
        public ClampedFloatParameter verticalJump = new(0.12f, 0f, 1f);
        public ClampedFloatParameter horizontalShake = new(0.08f, 0f, 1f);
        public ClampedFloatParameter colorDrift = new(0.35f, 0f, 1f);
        public ClampedFloatParameter horizontalRipple = new(0.45f, 0f, 1f);
        public ClampedFloatParameter digitalIntensity = new(0.5f, 0f, 1f);

        private Material material;
        private RTHandle history;
        private bool historyInitialized;
        private float jumpTime;

        public override CustomPostProcessInjectionPoint injectionPoint =>
            CustomPostProcessInjectionPoint.AfterPostProcess;

        public bool IsActive() => material != null && intensity.value > 0.0001f;

        public override void Setup()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"TV Monster KinoGlitch shader not found: {ShaderName}");
                return;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
            history = RTHandles.Alloc(
                Vector2.one,
                TextureXR.slices,
                DepthBits.None,
                GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                TextureXR.dimension,
                useDynamicScale: true,
                name: "_TvMonsterGlitchHistory");
        }

        public override void Render(
            CommandBuffer commandBuffer,
            HDCamera camera,
            RTHandle source,
            RTHandle destination)
        {
            if (material == null)
                return;

            if (!historyInitialized)
            {
                HDUtils.BlitCameraTexture(commandBuffer, source, history);
                historyInitialized = true;
            }

            float strength = intensity.value;
            jumpTime = (jumpTime + Time.deltaTime * verticalJump.value * strength * 11.3f) % 600f;

            material.SetTexture(ShaderIDs.InputTexture, source);
            material.SetTexture(ShaderIDs.HistoryTexture, history);
            material.SetFloat(ShaderIDs.Intensity, strength);
            material.SetFloat(ShaderIDs.ScanLineJitter, scanLineJitter.value * strength * 0.05f);
            material.SetVector(ShaderIDs.VerticalJump, new Vector2(verticalJump.value * strength, jumpTime));
            material.SetFloat(
                ShaderIDs.HorizontalShake,
                (UnityEngine.Random.value * 2f - 1f) * horizontalShake.value * strength * 0.1f);
            material.SetFloat(ShaderIDs.ColorDrift, colorDrift.value * strength);
            material.SetFloat(ShaderIDs.HorizontalRipple, horizontalRipple.value * strength);
            material.SetFloat(ShaderIDs.DigitalIntensity, digitalIntensity.value * strength);

            HDUtils.DrawFullScreen(commandBuffer, material, destination);

            // KinoGlitch keeps sparse previous frames to create the digital block replacement.
            if (Time.frameCount % 13 == 0)
                HDUtils.BlitCameraTexture(commandBuffer, source, history);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
            history?.Release();
            history = null;
            historyInitialized = false;
        }

        private static class ShaderIDs
        {
            public static readonly int InputTexture = Shader.PropertyToID("_InputTexture");
            public static readonly int HistoryTexture = Shader.PropertyToID("_HistoryTexture");
            public static readonly int Intensity = Shader.PropertyToID("_Intensity");
            public static readonly int ScanLineJitter = Shader.PropertyToID("_ScanLineJitter");
            public static readonly int VerticalJump = Shader.PropertyToID("_VerticalJump");
            public static readonly int HorizontalShake = Shader.PropertyToID("_HorizontalShake");
            public static readonly int ColorDrift = Shader.PropertyToID("_ColorDrift");
            public static readonly int HorizontalRipple = Shader.PropertyToID("_HorizontalRipple");
            public static readonly int DigitalIntensity = Shader.PropertyToID("_DigitalIntensity");
        }
    }
}
