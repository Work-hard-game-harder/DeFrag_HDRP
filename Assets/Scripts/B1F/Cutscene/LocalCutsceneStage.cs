using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace DeFrag.B1F
{
    /// <summary>
    /// Borrows the local player's view for a cutscene: swaps to the cutscene camera, hides the
    /// gameplay HUD and every player avatar (actors stand in for them), and restores it all on exit.
    /// Only this client's presentation is touched; network objects keep running.
    /// </summary>
    public sealed class LocalCutsceneStage
    {
        private readonly Camera cutsceneCamera;
        private readonly Transform keepVisibleRoot;
        private readonly List<(Camera camera, bool enabled)> playerCameras = new();
        private readonly List<(Renderer renderer, bool forcedOff)> avatarRenderers = new();
        private readonly List<(Light light, bool enabled)> avatarLights = new();
        private readonly Dictionary<Canvas, bool> hiddenCanvases = new();
        private readonly Dictionary<GraphicRaycaster, bool> blockedRaycasters = new();
        private CameraViewSwitcher viewSwitcher;
        private bool active;

        public LocalCutsceneStage(Camera cutsceneCamera, Transform keepVisibleRoot)
        {
            this.cutsceneCamera = cutsceneCamera;
            this.keepVisibleRoot = keepVisibleRoot;
        }

        /// <summary>Colliders of every spawned player, so local props can ignore them.</summary>
        public static List<Collider> PlayerColliders()
        {
            var colliders = new List<Collider>();
            foreach (NetworkObject player in Players())
                colliders.AddRange(player.GetComponentsInChildren<Collider>(true));
            return colliders;
        }

        public static IEnumerable<NetworkObject> Players()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null) yield break;
            foreach (NetworkObject player in manager.SpawnManager.PlayerObjects)
                if (player != null) yield return player;
        }

        public void Enter()
        {
            if (active) return;
            active = true;
            NetworkObject local = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
            if (local != null)
            {
                viewSwitcher = local.GetComponentInChildren<CameraViewSwitcher>(true);
                viewSwitcher?.SetInteractionLocked(true);
                foreach (Camera camera in local.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.isActiveAndEnabled && camera.targetTexture == null) CopyRenderingSettings(camera);
                    playerCameras.Add((camera, camera.enabled));
                    camera.enabled = false;
                }
            }

            foreach (NetworkObject player in Players())
            {
                foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>(true))
                {
                    avatarRenderers.Add((renderer, renderer.forceRenderingOff));
                    renderer.forceRenderingOff = true;
                }
                foreach (Light light in player.GetComponentsInChildren<Light>(true))
                {
                    avatarLights.Add((light, light.enabled));
                    light.enabled = false;
                }
            }

            if (cutsceneCamera != null)
            {
                cutsceneCamera.gameObject.SetActive(true);
                cutsceneCamera.enabled = true;
            }
            HideGameplayUI();
        }

        public void Exit()
        {
            if (!active) return;
            active = false;
            if (cutsceneCamera != null) cutsceneCamera.gameObject.SetActive(false);
            foreach (var (camera, enabled) in playerCameras) if (camera != null) camera.enabled = enabled;
            foreach (var (renderer, forcedOff) in avatarRenderers) if (renderer != null) renderer.forceRenderingOff = forcedOff;
            foreach (var (light, enabled) in avatarLights) if (light != null) light.enabled = enabled;
            viewSwitcher?.SetInteractionLocked(false);
            playerCameras.Clear();
            avatarRenderers.Clear();
            avatarLights.Clear();
            viewSwitcher = null;
            RestoreGameplayUI();
        }

        private void CopyRenderingSettings(Camera source)
        {
            if (cutsceneCamera == null) return;
            cutsceneCamera.allowHDR = source.allowHDR;
            cutsceneCamera.allowMSAA = source.allowMSAA;
            cutsceneCamera.targetDisplay = source.targetDisplay;
            var sourceData = source.GetComponent<HDAdditionalCameraData>();
            var targetData = cutsceneCamera.GetComponent<HDAdditionalCameraData>();
            if (sourceData == null || targetData == null) return;
            targetData.volumeLayerMask = sourceData.volumeLayerMask;
            targetData.antialiasing = sourceData.antialiasing;
            targetData.SMAAQuality = sourceData.SMAAQuality;
            targetData.dithering = sourceData.dithering;
            targetData.stopNaNs = sourceData.stopNaNs;
        }

        // HUD canvases are created at runtime under many roots, so they are discovered once per
        // cutscene. They are only disabled, never deactivated, so HUD logic keeps running.
        private void HideGameplayUI()
        {
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (canvas.rootCanvas.renderMode == RenderMode.WorldSpace) continue;
                if (keepVisibleRoot != null && canvas.transform.IsChildOf(keepVisibleRoot)) continue;
                NetworkObject owner = canvas.GetComponentInParent<NetworkObject>(true);
                if (owner != null && owner.IsSpawned && !owner.IsOwner) continue;
                hiddenCanvases.TryAdd(canvas, canvas.enabled);
                GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null) blockedRaycasters.TryAdd(raycaster, raycaster.enabled);
            }
            Canvas.preWillRenderCanvases += KeepGameplayUIHidden;
            KeepGameplayUIHidden();
        }

        private void KeepGameplayUIHidden()
        {
            if (!active) return;
            foreach (Canvas canvas in hiddenCanvases.Keys) if (canvas != null && canvas.enabled) canvas.enabled = false;
            foreach (GraphicRaycaster raycaster in blockedRaycasters.Keys) if (raycaster != null && raycaster.enabled) raycaster.enabled = false;
        }

        private void RestoreGameplayUI()
        {
            Canvas.preWillRenderCanvases -= KeepGameplayUIHidden;
            foreach (var state in hiddenCanvases) if (state.Key != null) state.Key.enabled = state.Value;
            foreach (var state in blockedRaycasters) if (state.Key != null) state.Key.enabled = state.Value;
            hiddenCanvases.Clear();
            blockedRaycasters.Clear();
        }
    }
}
