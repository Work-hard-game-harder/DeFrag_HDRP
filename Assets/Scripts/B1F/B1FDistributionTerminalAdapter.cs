using System;
using UnityEngine;

namespace DeFrag.B1F
{
    [DisallowMultipleComponent]
    public sealed class B1FDistributionTerminalAdapter : MonoBehaviour
    {
        [SerializeField] private ConnectionDevice terminal;
        [SerializeField] private DistributionBoxController distributionBoxA;
        public static event Action<DistributionPuzzlePhase> LocalBankAdvanced;

        public void BeginDistributionHintSession()
        {
            distributionBoxA?.RequestHintSessionFromLocalPlayer();
        }

        public static void NotifyLocalBankAdvanced(DistributionPuzzlePhase nextPhase)
        {
            B1FDistributionTerminalAdapter adapter =
                FindAnyObjectByType<B1FDistributionTerminalAdapter>();
            adapter?.terminal?.ResetCommandCompletion(TerminalCommands.DownloadData);
            LocalBankAdvanced?.Invoke(nextPhase);
        }

        public static void ResetLocalTerminal()
        {
            B1FDistributionTerminalAdapter adapter =
                FindAnyObjectByType<B1FDistributionTerminalAdapter>();
            if (adapter == null) return;

            adapter.terminal?.ResetCommandCompletion(TerminalCommands.DownloadData);
            Camera.main?.GetComponent<HackingSessionController>()?.End();
        }

        private void Reset()
        {
            if (terminal == null) terminal = GetComponent<ConnectionDevice>();
        }
    }
}
