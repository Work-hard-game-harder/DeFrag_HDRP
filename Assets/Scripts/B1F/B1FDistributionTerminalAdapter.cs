using System.Collections;
using UnityEngine;

namespace DeFrag.B1F
{
    [DisallowMultipleComponent]
    public sealed class B1FDistributionTerminalAdapter : MonoBehaviour
    {
        [SerializeField] private ConnectionDevice terminal;
        [SerializeField] private DistributionBoxController distributionBoxA;
        [SerializeField] private bool closeTerminalAfterDownload = true;

        public void BeginDistributionHintSession()
        {
            distributionBoxA?.RequestHintSessionFromLocalPlayer();
            if (closeTerminalAfterDownload)
                StartCoroutine(CloseTerminalNextFrame());
        }

        public static void ResetLocalTerminal()
        {
            B1FDistributionTerminalAdapter adapter =
                FindAnyObjectByType<B1FDistributionTerminalAdapter>();
            if (adapter == null) return;

            adapter.terminal?.ResetCommandCompletion(TerminalCommands.DownloadData);
            Camera.main?.GetComponent<HackingSessionController>()?.End();
        }

        private IEnumerator CloseTerminalNextFrame()
        {
            yield return null;
            Camera.main?.GetComponent<HackingSessionController>()?.End();
        }

        private void Reset()
        {
            if (terminal == null) terminal = GetComponent<ConnectionDevice>();
        }
    }
}
