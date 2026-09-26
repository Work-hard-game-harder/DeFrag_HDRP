using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace DeFrag.B1F
{
    /// <summary>Local UI/video only. No gameplay progress is advanced here.</summary>
    public sealed class B1FEscapePresentation : MonoBehaviour
    {
        private B1FEscapeSequence sequence;
        private B1FEscapeStage shown = (B1FEscapeStage)255;
        private GameObject canvasRoot;
        private GameObject videoRoot;
        private TMP_Text label;
        private RawImage display;
        private VideoPlayer video;
        private RenderTexture texture;
        private StarterAssets.PersonController movement;
        private bool previousMovement;
        private PlayerInteraction interaction;
        private bool previousInteraction;
        private bool locked;
        private bool sent;
        private float began;
        private float timeout;
        public void Bind(B1FEscapeSequence source)
        {
            sequence = source;
            canvasRoot = new GameObject("Local B1F story", typeof(Canvas), typeof(CanvasScaler));
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            videoRoot = new GameObject("Letterbox", typeof(RectTransform), typeof(Image));
            videoRoot.transform.SetParent(canvasRoot.transform, false);
            Stretch((RectTransform)videoRoot.transform);
            videoRoot.GetComponent<Image>().color = Color.black;
            var picture = new GameObject("Video", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            picture.transform.SetParent(videoRoot.transform, false);
            Stretch((RectTransform)picture.transform);
            display = picture.GetComponent<RawImage>();
            display.raycastTarget = false;
            var fit = picture.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 16f / 9f;
            var text = new GameObject("Download status", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(canvasRoot.transform, false);
            label = text.GetComponent<TMP_Text>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 34;
            label.color = new Color(0.4f, 1, 0.8f);
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(1200, 180);
            video = gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.isLooping = false;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.audioOutputMode = VideoAudioOutputMode.Direct;
            video.prepareCompleted += Prepared;
            video.loopPointReached += Finished;
            video.errorReceived += Failed;
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private void Update()
        {
            if (sequence == null) return;
            if (shown != sequence.Stage)
            {
                shown = sequence.Stage;
                StopVideo();
                videoRoot.SetActive(sequence.IsVideo);
                label.gameObject.SetActive(shown != B1FEscapeStage.Waiting && shown != B1FEscapeStage.EscapeReady);
                if (sequence.IsVideo) StartVideo();
                else Unlock();
            }
            if (sequence.IsVideo)
            {
                Lock();
                if (!sent && Time.unscaledTime - began >= timeout) Complete();
                return;
            }
            label.text = shown switch
            {
                B1FEscapeStage.Interrupted => "==================================\nDOWNLOAD INTERRUPTED\n==================================",
                B1FEscapeStage.RestoringGenerator => "DOWNLOAD PAUSED // MAIN POWER REQUIRED",
                _ => $"SERVER DOWNLOAD\n{sequence.Progress * 100:0}%"
            };
        }
        private void StartVideo()
        {
            sent = false;
            began = Time.unscaledTime;
            var clip = sequence.CurrentClip;
            timeout = clip != null ? (float)clip.length + 15 : sequence.PlaceholderSeconds;
            label.text = clip == null ? $"{shown}\n[VIDEO PLACEHOLDER]" : "PREPARING VIDEO...";
            display.gameObject.SetActive(clip != null);
            if (clip == null) return;
            float aspect = clip.height > 0 ? clip.width / (float)clip.height : 16f / 9f;
            display.GetComponent<AspectRatioFitter>().aspectRatio = aspect;
            texture = new RenderTexture(1280, Mathf.Clamp(Mathf.RoundToInt(1280 / aspect), 1, 1280), 0);
            texture.Create();
            display.texture = texture;
            video.clip = clip;
            video.targetTexture = texture;
            video.Prepare();
        }
        private void Prepared(VideoPlayer source) { label.text = ""; source.Play(); }
        private void Finished(VideoPlayer source) => Complete();
        private void Failed(VideoPlayer source, string error)
        {
            Debug.LogWarning($"[B1F story video] {error}", this);
            Complete();
        }
        private void Complete()
        {
            if (sent) return;
            sent = true;
            label.text = "WAITING FOR PARTNER...";
            sequence.FinishLocalVideo();
        }
        private void Lock()
        {
            if (locked) return;
            var player = sequence.NetworkManager.LocalClient?.PlayerObject;
            if (player == null) return;
            player.GetComponentInChildren<HackingSessionController>(true)?.End();
            if (!GameplayInputGate.TryAcquire(this)) return;
            movement = player.GetComponent<StarterAssets.PersonController>();
            if (movement != null) { previousMovement = movement.enabled; movement.enabled = false; }
            interaction = player.GetComponentInChildren<PlayerInteraction>(true);
            if (interaction != null)
            {
                previousInteraction = interaction.enabled;
                interaction.CloseAllUI();
                interaction.enabled = false;
            }
            locked = true;
        }
        private void Unlock()
        {
            if (locked && movement != null) movement.enabled = previousMovement;
            if (locked && interaction != null) interaction.enabled = previousInteraction;
            interaction = null;
            movement = null;
            locked = false;
            GameplayInputGate.Release(this);
        }
        private void StopVideo()
        {
            if (video != null) { video.Stop(); video.targetTexture = null; }
            if (display != null) display.texture = null;
            if (texture != null) { texture.Release(); Destroy(texture); texture = null; }
        }
        public void Unbind()
        {
            StopVideo(); Unlock(); sequence = null; shown = (B1FEscapeStage)255;
            if (video != null)
            {
                video.prepareCompleted -= Prepared; video.loopPointReached -= Finished; video.errorReceived -= Failed;
                Destroy(video); video = null;
            }
            if (canvasRoot != null) Destroy(canvasRoot);
        }
        private void OnDestroy() => Unbind();
    }
}
