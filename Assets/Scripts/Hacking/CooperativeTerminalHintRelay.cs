using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class CooperativeTerminalHintRelay : NetworkBehaviour
{
    private static readonly Color HintGreen = new(0.1f, 1f, 0.2f);

    private Canvas hintCanvas;
    private TMP_Text hintText;
    private Coroutine hideRoutine;

    public void ShowForTeammate(string terminalLabel, string missingToken)
    {
        if (!IsOwner || !IsSpawned)
            return;

        ShowForTeammateServerRpc(terminalLabel, missingToken);
    }

    public void HideForTeammate()
    {
        if (!IsOwner || !IsSpawned)
            return;

        HideForTeammateServerRpc();
    }

    [ServerRpc]
    private void ShowForTeammateServerRpc(
        string terminalLabel,
        string missingToken,
        ServerRpcParams rpcParams = default)
    {
        ClientRpcParams targets = TeammatesOf(rpcParams.Receive.SenderClientId);
        if (targets.Send.TargetClientIds.Count == 0)
            return;

        ShowHintClientRpc(terminalLabel, missingToken, targets);
    }

    [ServerRpc]
    private void HideForTeammateServerRpc(ServerRpcParams rpcParams = default)
    {
        ClientRpcParams targets = TeammatesOf(rpcParams.Receive.SenderClientId);
        if (targets.Send.TargetClientIds.Count == 0)
            return;

        HideHintClientRpc(targets);
    }

    [ClientRpc]
    private void ShowHintClientRpc(
        string terminalLabel,
        string missingToken,
        ClientRpcParams rpcParams = default)
    {
        EnsureHintInterface();
        hintText.text =
            $"REMOTE AUTHENTICATION FRAGMENT\n" +
            $"{terminalLabel}\n\n" +
            $"MISSING TOKEN:  <color=#FFFFFF>{missingToken}</color>";
        hintCanvas.gameObject.SetActive(true);

        if (hideRoutine != null)
            StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfterTimeout());
    }

    [ClientRpc]
    private void HideHintClientRpc(ClientRpcParams rpcParams = default)
    {
        HideLocalHint();
    }

    private ClientRpcParams TeammatesOf(ulong senderClientId)
    {
        List<ulong> targets = new();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            if (clientId != senderClientId)
                targets.Add(clientId);

        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = targets }
        };
    }

    private void EnsureHintInterface()
    {
        if (hintCanvas != null)
            return;

        GameObject canvasObject = new(
            "Cooperative Terminal Hint",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        hintCanvas = canvasObject.GetComponent<Canvas>();
        hintCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hintCanvas.sortingOrder = 130;
        canvasObject.GetComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObject.GetComponent<CanvasScaler>().referenceResolution =
            new Vector2(1920f, 1080f);

        GameObject panel = new("Fragment Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = new Vector2(0.28f, 0.4f);
        panelRect.anchorMax = new Vector2(0.72f, 0.6f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0.04f, 0.01f, 0.94f);

        GameObject textObject = new(
            "Fragment Text",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panel.transform, false);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 18f);
        textRect.offsetMax = new Vector2(-24f, -18f);
        hintText = textObject.GetComponent<TMP_Text>();
        hintText.alignment = TextAlignmentOptions.Center;
        hintText.color = HintGreen;
        hintText.fontSize = 27f;
        hintText.fontStyle = FontStyles.Bold;
        hintText.raycastTarget = false;
    }

    private IEnumerator HideAfterTimeout()
    {
        yield return new WaitForSecondsRealtime(45f);
        HideLocalHint();
    }

    private void HideLocalHint()
    {
        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        if (hintCanvas != null)
            hintCanvas.gameObject.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        HideLocalHint();
        if (hintCanvas != null)
            Destroy(hintCanvas.gameObject);
        base.OnNetworkDespawn();
    }
}
