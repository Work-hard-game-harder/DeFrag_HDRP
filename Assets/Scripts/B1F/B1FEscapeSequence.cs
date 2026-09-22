using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;

namespace DeFrag.B1F
{
    public enum B1FEscapeStage : byte
    {
        Waiting, WarningVideo, Downloading, ApproachVideo, ImpactVideo,
        BreachVideo, Interrupted, RestoringGenerator, ResumingDownload,
        ExitVideo, EscapeReady
    }

    /// <summary>Scene network object. Only the server advances the story.</summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class B1FEscapeSequence : NetworkBehaviour
    {
        [SerializeField] private ConnectServerCoordinator connection;
        [SerializeField] private B1FPowerController power;
        [SerializeField] private B1FEscapePresentation presentation;
        [Header("Videos: warning / approach / impact / breach / exit")]
        [SerializeField] private VideoClip warningVideo;
        [SerializeField] private VideoClip approachVideo;
        [SerializeField] private VideoClip impactVideo;
        [SerializeField] private VideoClip breachVideo;
        [SerializeField] private VideoClip exitVideo;
        [SerializeField, Min(1)] private float placeholderSeconds = 4;
        [SerializeField, Min(5)] private float videoTimeoutMargin = 20;
        [Header("Download")]
        [SerializeField, Min(1)] private float downloadSeconds = 45;
        [SerializeField, Range(0.05f, 0.9f)] private float interruptAt = 0.35f;
        [SerializeField, Min(0)] private float interruptionNoticeSeconds = 2;
        [Header("Quest signals (configure matching steps in QuestManager)")]
        [SerializeField] private string interruptionSignal = "B1F_DOWNLOAD_INTERRUPTED";
        [SerializeField] private string downloadCompleteSignal = "B1F_DOWNLOAD_COMPLETED";
        [Header("Persistent world state (no NetworkObjects under visual roots)")]
        [SerializeField] private GameObject intactDoor;
        [SerializeField] private GameObject brokenDoor;
        [SerializeField] private ParticleSystem breachDust;
        [SerializeField] private AudioSource breachAudio;
        [SerializeField] private AudioClip breachSound;
        [Header("Server-only hooks: wire authoritative gameplay methods")]
        [SerializeField] private UnityEvent onApproachServer;
        [SerializeField] private UnityEvent onBreachServer;
        [SerializeField] private UnityEvent onEscapeReadyServer;
        private readonly NetworkVariable<B1FEscapeStage> stage = new(B1FEscapeStage.Waiting);
        private readonly NetworkVariable<float> progress = new(0);
        private readonly HashSet<ulong> finishedViewers = new();
        private double stageStarted;
        private double deadline;
        public B1FEscapeStage Stage => stage.Value;
        public float Progress => progress.Value;
        public bool EscapeReady => Stage == B1FEscapeStage.EscapeReady;
        public VideoClip CurrentClip => Stage switch
        {
            B1FEscapeStage.WarningVideo => warningVideo,
            B1FEscapeStage.ApproachVideo => approachVideo,
            B1FEscapeStage.ImpactVideo => impactVideo,
            B1FEscapeStage.BreachVideo => breachVideo,
            B1FEscapeStage.ExitVideo => exitVideo,
            _ => null
        };
        public bool IsVideo => Stage == B1FEscapeStage.WarningVideo ||
            Stage == B1FEscapeStage.ApproachVideo || Stage == B1FEscapeStage.ImpactVideo ||
            Stage == B1FEscapeStage.BreachVideo || Stage == B1FEscapeStage.ExitVideo;
        public float PlaceholderSeconds => placeholderSeconds;
        public override void OnNetworkSpawn()
        {
            stage.OnValueChanged += Changed;
            ApplyWorldState(false);
            if (IsClient && presentation != null) presentation.Bind(this);
        }
        public override void OnNetworkDespawn()
        {
            stage.OnValueChanged -= Changed;
            if (presentation != null) presentation.Unbind();
        }
        private void Changed(B1FEscapeStage before, B1FEscapeStage after) =>
            ApplyWorldState(before == B1FEscapeStage.BreachVideo);
        private void ApplyWorldState(bool effects)
        {
            bool breached = Stage >= B1FEscapeStage.Interrupted;
            if (intactDoor != null) intactDoor.SetActive(!breached);
            if (brokenDoor != null) brokenDoor.SetActive(breached);
            if (effects && IsClient)
            {
                breachDust?.Play();
                if (breachAudio != null && breachSound != null) breachAudio.PlayOneShot(breachSound);
            }
        }
        private void Enter(B1FEscapeStage next)
        {
            finishedViewers.Clear();
            stageStarted = NetworkManager.ServerTime.Time;
            stage.Value = next;
            deadline = stageStarted + (CurrentClip != null ? CurrentClip.length : placeholderSeconds) + videoTimeoutMargin;
        }
        private void Update()
        {
            if (!IsSpawned || !IsServer || connection == null || power == null) return;
            double now = NetworkManager.ServerTime.Time;
            if (IsVideo)
            {
                bool all = true;
                foreach (var client in NetworkManager.ConnectedClientsList)
                    if (!finishedViewers.Contains(client.ClientId)) all = false;
                if (!all && now < deadline) return;
                switch (Stage)
                {
                    case B1FEscapeStage.WarningVideo: Enter(B1FEscapeStage.Downloading); break;
                    case B1FEscapeStage.ApproachVideo: Enter(B1FEscapeStage.ImpactVideo); break;
                    case B1FEscapeStage.ImpactVideo: Enter(B1FEscapeStage.BreachVideo); break;
                    case B1FEscapeStage.BreachVideo:
                        Enter(B1FEscapeStage.Interrupted); onBreachServer?.Invoke(); break;
                    case B1FEscapeStage.ExitVideo:
                        Enter(B1FEscapeStage.EscapeReady);
                        QuestManager.Instance?.ReportProgress(downloadCompleteSignal, "B1F_ESCAPE_SEQUENCE");
                        onEscapeReadyServer?.Invoke(); break;
                }
                return;
            }
            switch (Stage)
            {
                case B1FEscapeStage.Waiting:
                    if (connection.Phase == ConnectServerUplinkPhase.Completed) Enter(B1FEscapeStage.WarningVideo);
                    break;
                case B1FEscapeStage.Downloading:
                case B1FEscapeStage.ResumingDownload:
                    float limit = Stage == B1FEscapeStage.Downloading ? interruptAt : 1;
                    progress.Value = Mathf.Min(limit, progress.Value + Time.unscaledDeltaTime / Mathf.Max(1, downloadSeconds));
                    if (progress.Value < limit) break;
                    if (Stage == B1FEscapeStage.Downloading)
                    { Enter(B1FEscapeStage.ApproachVideo); onApproachServer?.Invoke(); }
                    else Enter(B1FEscapeStage.ExitVideo);
                    break;
                case B1FEscapeStage.Interrupted:
                    if (now - stageStarted < interruptionNoticeSeconds) break;
                    power.SetStoryBlackoutServer();
                    QuestManager.Instance?.ReportProgress(interruptionSignal, "B1F_ESCAPE_SEQUENCE");
                    Enter(B1FEscapeStage.RestoringGenerator);
                    break;
                case B1FEscapeStage.RestoringGenerator:
                    if (power.CurrentState == B1FPowerState.FullPower) Enter(B1FEscapeStage.ResumingDownload);
                    break;
            }
        }
        public void FinishLocalVideo() { if (IsSpawned && IsClient) VideoFinishedServerRpc(Stage); }
        [ServerRpc(RequireOwnership = false)]
        private void VideoFinishedServerRpc(B1FEscapeStage expected, ServerRpcParams rpc = default)
        {
            if (IsVideo && expected == Stage) finishedViewers.Add(rpc.Receive.SenderClientId);
        }
    }
}
