using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DeFrag.Player
{
    [Serializable]
    [VolumeComponentMenu("Post-processing/Custom/Sprint Edge Blur")]
    public sealed class SprintEdgeBlur : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);
        public ClampedFloatParameter edgeStart = new(0.42f, 0.05f, 0.95f);
        public ClampedFloatParameter blurRadius = new(0.012f, 0f, 0.04f);

        private const string ShaderName = "Hidden/DeFrag/SprintEdgeBlur";
        private Material material;

        public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.AfterPostProcess;
        public bool IsActive() => material != null && intensity.value > 0.001f;

        public override void Setup()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader != null)
                material = CoreUtils.CreateEngineMaterial(shader);
            else
                Debug.LogError($"Sprint edge blur shader was not found: {ShaderName}");
        }

        public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
        {
            if (material == null)
                return;

            material.SetTexture("_InputTexture", source);
            material.SetFloat("_Intensity", intensity.value);
            material.SetFloat("_EdgeStart", edgeStart.value);
            material.SetFloat("_BlurRadius", blurRadius.value);
            HDUtils.DrawFullScreen(cmd, material, destination, shaderPassId: 0);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(material);
        }
    }
}

