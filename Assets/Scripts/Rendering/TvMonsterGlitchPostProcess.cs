using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DeFrag.Rendering
{
    [Serializable]
    [VolumeComponentMenu("Post-processing/Custom/Tv Monster Glitch")]
    public sealed class TvMonsterGlitchPostProcess : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        private const string ShaderName = "Hidden/DeFrag/TvMonsterGlitch";

        public ClampedFloatParameter intensity = new(0f, 0f, 1f);
        public ClampedFloatParameter tearAmount = new(0.035f, 0f, 0.12f);
        public ClampedFloatParameter noiseAmount = new(0.3f, 0f, 1f);

        private Material material;

        public override CustomPostProcessInjectionPoint injectionPoint =>
            CustomPostProcessInjectionPoint.AfterPostProcess;

        public bool IsActive() => material != null && intensity.value > 0.0001f;

        public override void Setup()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader != null)
                material = CoreUtils.CreateEngineMaterial(shader);
            else
                Debug.LogError($"Tv Monster glitch shader not found: {ShaderName}");
        }

        public override void Render(CommandBuffer commandBuffer, HDCamera camera, RTHandle source, RTHandle destination)
        {
            if (material == null)
                return;

            material.SetTexture(ShaderIDs.InputTexture, source);
            material.SetFloat(ShaderIDs.Intensity, intensity.value);
            material.SetFloat(ShaderIDs.TearAmount, tearAmount.value);
            material.SetFloat(ShaderIDs.NoiseAmount, noiseAmount.value);
            HDUtils.DrawFullScreen(commandBuffer, material, destination);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        private static class ShaderIDs
        {
            public static readonly int InputTexture = Shader.PropertyToID("_InputTexture");
            public static readonly int Intensity = Shader.PropertyToID("_Intensity");
            public static readonly int TearAmount = Shader.PropertyToID("_TearAmount");
            public static readonly int NoiseAmount = Shader.PropertyToID("_NoiseAmount");
        }
    }
}
