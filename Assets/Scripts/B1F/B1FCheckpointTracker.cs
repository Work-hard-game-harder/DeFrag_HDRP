using Unity.Netcode;
using UnityEngine;

namespace DeFrag.B1F
{
    public enum B1FCheckpoint : byte
    {
        Initial,
        DistributionBoxACompleted,
        ConnectServerBreachCompleted,
        GeneratorCompleted
    }

    /// <summary>
    /// Observes authoritative B1F story state and exposes the latest safe party respawn point.
    /// World state itself stays owned by its existing network components.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class B1FCheckpointTracker : MonoBehaviour
    {
        [SerializeField] private DistributionBoxController distributionBoxA;
        [SerializeField] private B1FPowerController powerController;
        [SerializeField] private B1FEscapeSequence escapeSequence;

        public static B1FCheckpointTracker Instance { get; private set; }
        public B1FCheckpoint Current { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !manager.IsServer)
                return;

            B1FCheckpoint next = EvaluateCheckpoint();
            if (next <= Current)
                return;

            Current = next;
            Debug.Log($"[B1F Checkpoint] Reached {Current}.", this);
        }

        private B1FCheckpoint EvaluateCheckpoint()
        {
            // FullPower is only a checkpoint after the generator restoration portion of
            // the escape sequence has actually been reached.
            if (powerController != null && escapeSequence != null &&
                powerController.CurrentState == B1FPowerState.FullPower &&
                escapeSequence.Stage >= B1FEscapeStage.ResumingDownload)
                return B1FCheckpoint.GeneratorCompleted;

            // The broken door becomes persistent only after every viewer has finished
            // the indirect breach presentation and the server has entered Interrupted.
            if (escapeSequence != null && escapeSequence.Stage >= B1FEscapeStage.Interrupted)
                return B1FCheckpoint.ConnectServerBreachCompleted;

            if (distributionBoxA != null && distributionBoxA.IsCompleted &&
                powerController != null &&
                powerController.CurrentState == B1FPowerState.EmergencyPower)
                return B1FCheckpoint.DistributionBoxACompleted;

            return B1FCheckpoint.Initial;
        }
    }
}
