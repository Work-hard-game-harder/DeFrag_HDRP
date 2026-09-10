using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DeFrag.UI
{
    /// <summary>
    /// Applies exposure-like tinting only to UI which is rendered after camera post-processing.
    /// Graphic.color is never changed, so button transitions, TMP colors, and prefab values remain intact.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIBrightnessController : MonoBehaviour
    {
        [Tooltip("How often newly-created UI, such as dropdown popups, is added to the cache.")]
        [Min(0.1f)]
        [SerializeField] private float targetRefreshInterval = 0.5f;

        private readonly List<Graphic> targets = new();
        private float currentExposure;
        private float colorMultiplier = 1f;
        private float nextTargetRefreshTime;
        private bool isRefreshing;

        public float CurrentExposure => currentExposure;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            Canvas.willRenderCanvases += ApplyToCachedTargets;
            RefreshTargets();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Canvas.willRenderCanvases -= ApplyToCachedTargets;
            RestoreCachedTargets();
        }

        public void SetExposure(float exposure)
        {
            currentExposure = exposure;
            colorMultiplier = Mathf.Pow(2f, exposure);
            ApplyToCachedTargets();
        }

        public void RefreshTargets()
        {
            if (isRefreshing)
                return;

            isRefreshing = true;
            targets.Clear();

            Graphic[] graphics = FindObjectsByType<Graphic>(FindObjectsInactive.Include);
            foreach (Graphic graphic in graphics)
            {
                if (IsOverlayGraphic(graphic))
                    targets.Add(graphic);
            }

            nextTargetRefreshTime = Time.unscaledTime + targetRefreshInterval;
            isRefreshing = false;
            ApplyToCachedTargets();
        }

        private void ApplyToCachedTargets()
        {
            if (!isRefreshing && Time.unscaledTime >= nextTargetRefreshTime)
            {
                RefreshTargets();
                return;
            }

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                Graphic graphic = targets[i];
                if (graphic == null)
                {
                    targets.RemoveAt(i);
                    continue;
                }

                // Selectable animations and TMP may change Graphic.color at runtime.
                // Reading it every render preserves those changes and only adds brightness.
                Color renderedColor = graphic.color;
                renderedColor.r *= colorMultiplier;
                renderedColor.g *= colorMultiplier;
                renderedColor.b *= colorMultiplier;
                graphic.canvasRenderer.SetColor(renderedColor);
            }
        }

        private void RestoreCachedTargets()
        {
            foreach (Graphic graphic in targets)
            {
                if (graphic != null)
                    graphic.canvasRenderer.SetColor(graphic.color);
            }

            targets.Clear();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshTargets();
        }

        private static bool IsOverlayGraphic(Graphic graphic)
        {
            if (graphic == null)
                return false;

            Canvas canvas = graphic.canvas;
            Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
            return rootCanvas != null && rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay;
        }

        private void OnValidate()
        {
            targetRefreshInterval = Mathf.Max(0.1f, targetRefreshInterval);
        }
    }
}
