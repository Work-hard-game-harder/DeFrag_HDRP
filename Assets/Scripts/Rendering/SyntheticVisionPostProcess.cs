using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DeFrag.Rendering
{
    [Serializable]
    [VolumeComponentMenu("Post-processing/Custom/Synthetic Vision")]
    public sealed class SyntheticVisionPostProcess : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        private const string ShaderName = "Hidden/DeFrag/SyntheticVision";

        [Tooltip("전체 효과의 혼합 강도입니다. 0이면 원본 화면을 그대로 출력합니다.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("AI 영상 보정처럼 경계를 미세하게 강조합니다.")]
        public ClampedFloatParameter sharpening = new ClampedFloatParameter(0.1f, 0f, 0.5f);

        [Tooltip("색상 단계의 수입니다. 값이 작을수록 디지털 밴딩이 강해집니다.")]
        public ClampedIntParameter colorSteps = new ClampedIntParameter(48, 8, 128);

        [Tooltip("색상 양자화가 원본 색에 섞이는 비율입니다.")]
        public ClampedFloatParameter quantization = new ClampedFloatParameter(0.12f, 0f, 1f);

        [Tooltip("주변부에서 영상 처리 품질이 감소하는 강도입니다.")]
        public ClampedFloatParameter peripheralFalloff = new ClampedFloatParameter(0.12f, 0f, 1f);

        [Tooltip("주변부 품질 저하가 시작되는 화면 반경입니다.")]
        public ClampedFloatParameter peripheralStart = new ClampedFloatParameter(0.55f, 0.1f, 0.95f);

        private Material _material;

        public override CustomPostProcessInjectionPoint injectionPoint =>
            CustomPostProcessInjectionPoint.AfterPostProcess;

        public bool IsActive() => _material != null && intensity.value > 0f;

        public override void Setup()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader != null)
            {
                _material = CoreUtils.CreateEngineMaterial(shader);
                return;
            }

            Debug.LogError($"Synthetic Vision shader를 찾을 수 없습니다: {ShaderName}");
        }

        public override void Render(CommandBuffer commandBuffer, HDCamera camera, RTHandle source, RTHandle destination)
        {
            if (_material == null)
                return;

            _material.SetFloat(ShaderIDs.Intensity, intensity.value);
            _material.SetFloat(ShaderIDs.Sharpening, sharpening.value);
            _material.SetFloat(ShaderIDs.ColorSteps, colorSteps.value);
            _material.SetFloat(ShaderIDs.Quantization, quantization.value);
            _material.SetFloat(ShaderIDs.PeripheralFalloff, peripheralFalloff.value);
            _material.SetFloat(ShaderIDs.PeripheralStart, peripheralStart.value);
            _material.SetTexture(ShaderIDs.InputTexture, source);

            HDUtils.DrawFullScreen(commandBuffer, _material, destination);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        private static class ShaderIDs
        {
            public static readonly int InputTexture = Shader.PropertyToID("_InputTexture");
            public static readonly int Intensity = Shader.PropertyToID("_Intensity");
            public static readonly int Sharpening = Shader.PropertyToID("_Sharpening");
            public static readonly int ColorSteps = Shader.PropertyToID("_ColorSteps");
            public static readonly int Quantization = Shader.PropertyToID("_Quantization");
            public static readonly int PeripheralFalloff = Shader.PropertyToID("_PeripheralFalloff");
            public static readonly int PeripheralStart = Shader.PropertyToID("_PeripheralStart");
        }
    }
}
