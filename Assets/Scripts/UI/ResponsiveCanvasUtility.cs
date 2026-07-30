using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.UI
{
    /// <summary>
    /// 공통 UI 스케일 정책입니다. 화면 해상도는 각 로컬 플레이어의 기기에서만
    /// 적용되며 네트워크 게임 상태에는 포함되지 않습니다.
    /// </summary>
    public static class ResponsiveCanvasUtility
    {
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        public const float BalancedMatch = 0.5f;

        public static void Configure(CanvasScaler scaler)
        {
            if (scaler == null)
                return;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = BalancedMatch;
        }

        public static string GetAspectLabel(int width, int height)
        {
            if (width <= 0 || height <= 0)
                return "Unknown";

            float ratio = (float)width / height;

            if (Mathf.Abs(ratio - 4f / 3f) < 0.03f) return "4:3";
            if (Mathf.Abs(ratio - 16f / 10f) < 0.03f) return "16:10";
            if (Mathf.Abs(ratio - 16f / 9f) < 0.03f) return "16:9";
            if (Mathf.Abs(ratio - 21f / 9f) < 0.08f) return "21:9";
            if (Mathf.Abs(ratio - 32f / 9f) < 0.08f) return "32:9";

            int divisor = GreatestCommonDivisor(width, height);
            return $"{width / divisor}:{height / divisor}";
        }

        private static int GreatestCommonDivisor(int left, int right)
        {
            left = Mathf.Abs(left);
            right = Mathf.Abs(right);

            while (right != 0)
            {
                int remainder = left % right;
                left = right;
                right = remainder;
            }

            return Mathf.Max(1, left);
        }
    }
}
