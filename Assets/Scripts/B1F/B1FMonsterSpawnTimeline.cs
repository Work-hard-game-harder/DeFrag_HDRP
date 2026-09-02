using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace DeFrag.B1F
{
    /// <summary>
    /// Plays the authored spawn shot locally. Never moves or animates the network monster.
    /// The server sends the start time and duration, then spawns the monster afterwards.
    /// Only the owning player's view and controls on this client are temporarily changed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class B1FMonsterSpawnTimeline : MonoBehaviour
    {
        [Header("Authored Timeline")]
        [SerializeField] private PlayableDirector director;
        [SerializeField] private Camera presentationCamera;
        [SerializeField] private CinemachineBrain presentationBrain;
        [Tooltip("Timeline에서 사용할 Cinemachine 카메라들을 포함한 전용 루트입니다.")]
        [SerializeField] private GameObject shotCameraRoot;
        [Tooltip("Timeline의 Activation/Animation Track에 연결한 페이드 화면입니다.")]
        [SerializeField] private GameObject fadeOverlay;
        [SerializeField, Min(0.1f)] private float readinessTimeout = 10f;

        [Header("Cutscene UI")]
        [SerializeField] private bool hideGameplayUI = true;
        [Tooltip("페이드와 컷씬 전용 UI만 넣는 독립 Canvas입니다. 일반 HUD의 부모 Canvas를 지정하지 마세요.")]
        [SerializeField] private Canvas cutsceneCanvas;

        private Coroutine playback;
        private bool requested;
        private bool presenting;
        private bool playbackStopped;
        private NetworkObject localPlayer;
        private StarterAssets.PersonController movement;
        private PlayerInteraction interaction;
        private CameraViewSwitcher viewSwitcher;
        private Camera[] playerCameras;
        private bool[] cameraEnabledStates;
        private bool movementWasEnabled;
        private bool interactionWasEnabled;
        private readonly Dictionary<Canvas, bool> hiddenCanvases = new();
        private readonly Dictionary<GraphicRaycaster, bool> blockedRaycasters = new();

        private void Awake()
        {
            if (director != null)
            {
                director.playOnAwake = false;
                director.extrapolationMode = DirectorWrapMode.None;
                director.stopped += OnDirectorStopped;
            }
            SetPresentationVisible(false);
        }

        public bool TryGetDuration(out double duration)
        {
            duration = director != null && director.playableAsset != null
                ? director.playableAsset.duration
                : 0d;
            return !double.IsNaN(duration) && !double.IsInfinity(duration) && duration > 0d;
        }

        public void PlayOnce(double startServerTime, double duration)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (requested || !isActiveAndEnabled || manager == null || !manager.IsClient)
                return;

            if (director == null || director.playableAsset == null ||
                presentationCamera == null || presentationBrain == null)
            {
                Debug.LogWarning("[B1F Spawn Timeline] Timeline/Camera/Brain 연결을 확인하세요.", this);
                return;
            }

            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0d)
            {
                Debug.LogWarning("[B1F Spawn Timeline] 유효한 길이의 Timeline이 필요합니다.", this);
                return;
            }

            requested = true;
            playback = StartCoroutine(PlayWhenReady(startServerTime, duration));
        }

        private IEnumerator PlayWhenReady(double startServerTime, double duration)
        {
            // Always yield once so cleanup cannot race the coroutine handle assignment.
            yield return null;
            float waitStartedAt = Time.realtimeSinceStartup;
            try
            {
                while (true)
                {
                    NetworkManager manager = NetworkManager.Singleton;
                    if (manager == null || !manager.IsListening || !manager.IsClient)
                        yield break;

                    double elapsed = manager.ServerTime.Time - startServerTime;
                    if (elapsed >= duration || Time.realtimeSinceStartup - waitStartedAt >= readinessTimeout)
                    {
                        Debug.LogWarning("[B1F Spawn Timeline] 로컬 플레이어 또는 다른 UI의 종료를 기다리다 연출 시간이 만료되었습니다.", this);
                        yield break;
                    }

                    localPlayer = manager.LocalClient?.PlayerObject;
                    if (elapsed >= 0d && IsLocalPlayerAvailable() &&
                        !SettingManager.IsGamePaused && !GameState.isCutscene &&
                        !GameplayInputGate.IsBlocked && HasEnabledPlayerCamera() &&
                        GameplayInputGate.TryAcquire(this))
                        break;

                    yield return null;
                }

                BeginLocalPresentation();
                director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                director.time = System.Math.Max(0d, NetworkManager.Singleton.ServerTime.Time - startServerTime);
                playbackStopped = false;
                director.Play();

                while (!playbackStopped && IsLocalPlayerAvailable())
                {
                    NetworkManager manager = NetworkManager.Singleton;
                    if (manager == null || !manager.IsListening ||
                        manager.ServerTime.Time - startServerTime >= duration)
                        break;
                    yield return null;
                }
            }
            finally
            {
                RestoreLocalPresentation();
                playback = null;
            }
        }

        private bool IsLocalPlayerAvailable()
        {
            if (localPlayer == null || !localPlayer.IsSpawned || !localPlayer.IsOwner)
                return false;
            PlayerStats stats = localPlayer.GetComponent<PlayerStats>();
            return stats == null || !stats.IsDead;
        }

        private bool HasEnabledPlayerCamera()
        {
            foreach (Camera candidate in localPlayer.GetComponentsInChildren<Camera>(true))
                if (candidate.isActiveAndEnabled && candidate.targetTexture == null)
                    return true;
            return false;
        }

        private void BeginLocalPresentation()
        {
            presenting = true;
            movement = localPlayer.GetComponent<StarterAssets.PersonController>();
            interaction = localPlayer.GetComponentInChildren<PlayerInteraction>(true);
            viewSwitcher = localPlayer.GetComponentInChildren<CameraViewSwitcher>(true);
            movementWasEnabled = movement != null && movement.enabled;
            interactionWasEnabled = interaction != null && interaction.enabled;

            // Use the normal player view when returning, even if an item camera was equipped.
            viewSwitcher?.SetInteractionLocked(true);
            playerCameras = localPlayer.GetComponentsInChildren<Camera>(true);
            cameraEnabledStates = new bool[playerCameras.Length];
            for (int i = 0; i < playerCameras.Length; i++)
            {
                Camera camera = playerCameras[i];
                cameraEnabledStates[i] = camera.enabled;
                if (camera.isActiveAndEnabled && camera.targetTexture == null)
                    CopyRenderingSettings(camera);
                camera.enabled = false;
            }

            if (movement != null) movement.enabled = false;
            if (interaction != null) interaction.enabled = false;
            HideGameplayUI();
            // Keep the owning player's AudioListener. Never enable a second listener.
            SetPresentationVisible(true);
        }

        private void CopyRenderingSettings(Camera source)
        {
            presentationCamera.allowHDR = source.allowHDR;
            presentationCamera.allowMSAA = source.allowMSAA;
            presentationCamera.targetDisplay = source.targetDisplay;
            HDAdditionalCameraData sourceData = source.GetComponent<HDAdditionalCameraData>();
            HDAdditionalCameraData targetData = presentationCamera.GetComponent<HDAdditionalCameraData>();
            if (sourceData == null || targetData == null) return;
            targetData.volumeLayerMask = sourceData.volumeLayerMask;
            targetData.antialiasing = sourceData.antialiasing;
            targetData.SMAAQuality = sourceData.SMAAQuality;
            targetData.dithering = sourceData.dithering;
            targetData.stopNaNs = sourceData.stopNaNs;
        }

        private void SetPresentationVisible(bool visible)
        {
            if (shotCameraRoot != null) shotCameraRoot.SetActive(visible);
            if (presentationBrain != null) presentationBrain.enabled = visible;
            if (presentationCamera != null)
            {
                presentationCamera.enabled = visible;
                AudioListener listener = presentationCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
            // Activation Track controls the overlay while playing; always remove it on exit.
            if (!visible && fadeOverlay != null) fadeOverlay.SetActive(false);
        }

        private void OnDirectorStopped(PlayableDirector stoppedDirector) => playbackStopped = true;

        private void HideGameplayUI()
        {
            if (!hideGameplayUI) return;
            if (cutsceneCanvas == null)
            {
                Debug.LogWarning("[B1F Spawn Timeline] UI 숨김에는 독립된 Cutscene Canvas 연결이 필요합니다.", this);
                return;
            }

            // Runtime inventory/mic HUD and the persistent Settings UI live under different
            // roots. Discover canvases once per cutscene, including currently inactive UI.
            // Do not deactivate their GameObjects: inventory, recording and quest logic must run.
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                if (canvas.transform.IsChildOf(cutsceneCanvas.transform) ||
                    canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
                    continue;

                NetworkObject owner = canvas.GetComponentInParent<NetworkObject>(true);
                if (owner != null && owner.IsSpawned && !owner.IsOwner)
                    continue;

                hiddenCanvases.TryAdd(canvas, canvas.enabled);
                GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null) blockedRaycasters.TryAdd(raycaster, raycaster.enabled);
            }

            // Snapshot everything before disabling parent canvases, so rootCanvas queries
            // above cannot be affected by our own changes.
            Canvas.preWillRenderCanvases += KeepGameplayUIHidden;
            KeepGameplayUIHidden();
        }

        private void KeepGameplayUIHidden()
        {
            if (!presenting) return;
            foreach (Canvas canvas in hiddenCanvases.Keys)
                if (canvas != null && canvas.enabled) canvas.enabled = false;
            foreach (GraphicRaycaster raycaster in blockedRaycasters.Keys)
                if (raycaster != null && raycaster.enabled) raycaster.enabled = false;
        }

        private void RestoreGameplayUI()
        {
            Canvas.preWillRenderCanvases -= KeepGameplayUIHidden;
            foreach (KeyValuePair<Canvas, bool> state in hiddenCanvases)
                if (state.Key != null) state.Key.enabled = state.Value;
            foreach (KeyValuePair<GraphicRaycaster, bool> state in blockedRaycasters)
                if (state.Key != null) state.Key.enabled = state.Value;
            hiddenCanvases.Clear();
            blockedRaycasters.Clear();
        }

        private void RestoreLocalPresentation()
        {
            if (director != null && director.state == PlayState.Playing) director.Stop();
            SetPresentationVisible(false);
            RestoreGameplayUI();
            if (presenting && localPlayer != null && localPlayer.IsSpawned && localPlayer.IsOwner)
            {
                viewSwitcher?.SetInteractionLocked(false);
                if (playerCameras != null)
                    for (int i = 0; i < playerCameras.Length; i++)
                        if (playerCameras[i] != null) playerCameras[i].enabled = cameraEnabledStates[i];
                if (IsLocalPlayerAvailable())
                {
                    if (movement != null) movement.enabled = movementWasEnabled;
                    if (interaction != null) interaction.enabled = interactionWasEnabled;
                }
            }
            presenting = false;
            GameplayInputGate.Release(this);
            playerCameras = null;
            cameraEnabledStates = null;
        }

        public void StopPlayback()
        {
            if (playback != null) StopCoroutine(playback);
            playback = null;
            RestoreLocalPresentation();
        }

        private void OnDisable() => StopPlayback();

        private void OnDestroy()
        {
            if (director != null) director.stopped -= OnDirectorStopped;
        }
    }
}
