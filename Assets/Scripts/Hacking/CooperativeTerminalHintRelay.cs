using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class CooperativeTerminalHintRelay : NetworkBehaviour
{
    private Canvas hintCanvas;
    private TMP_Text hintText;
    private Coroutine hideRoutine;

    public void RequestTerminalWorldState(
        string terminalId,
        TerminalWorldPhase phase,
        TerminalCommands command)
    {
        if (!IsOwner || !IsSpawned || string.IsNullOrWhiteSpace(terminalId)) return;
        RequestTerminalWorldStateServerRpc(terminalId, (byte)phase, (int)command);
    }

    [ServerRpc]
    private void RequestTerminalWorldStateServerRpc(string terminalId, byte rawPhase, int rawCommand)
    {
        if (rawPhase > (byte)TerminalWorldPhase.Failure) return;
        TerminalCommands command = (TerminalCommands)rawCommand;
        if (command != TerminalCommands.None && command != TerminalCommands.UnlockDoor &&
            command != TerminalCommands.DownloadData && command != TerminalCommands.ConnectServer) return;
        ApplyTerminalWorldStateClientRpc(terminalId, rawPhase, rawCommand);
    }

    [ClientRpc]
    private void ApplyTerminalWorldStateClientRpc(string terminalId, byte rawPhase, int rawCommand)
    {
        ConnectionDevice.ApplyWorldScreenState(
            terminalId, (TerminalWorldPhase)rawPhase, (TerminalCommands)rawCommand);
    }

    public void RequestTerminalCommandCompletion(
        string terminalId,
        TerminalCommands command)
    {
        if (!IsOwner || !IsSpawned || string.IsNullOrWhiteSpace(terminalId))
            return;

        RequestTerminalCommandCompletionServerRpc(terminalId, (int)command);
    }

    public bool TryCompleteTerminalCommandForStoryDebugServer(
        string terminalId,
        TerminalCommands command)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!IsSpawned || !IsServer || string.IsNullOrWhiteSpace(terminalId) ||
            !ConnectionDevice.CanSynchronizeCompletion(terminalId, command))
            return false;

        ApplyTerminalCommandCompletionClientRpc(terminalId, (int)command);
        return true;
#else
        return false;
#endif
    }

    public void ShowForTeammate(
        string terminalLabel,
        string value,
        string valueLabel = "MISSING TOKEN")
    {
        if (!IsOwner || !IsSpawned)
            return;

        ShowForTeammateServerRpc(terminalLabel, value, valueLabel);
    }

    public void HideForTeammate()
    {
        if (!IsOwner || !IsSpawned)
            return;

        HideForTeammateServerRpc();
    }

    public void ReportTerminalFailure(Vector3 terminalPosition, float alertRadius)
    {
        if (IsOwner && IsSpawned)
        {
            ReportTerminalFailureServerRpc(terminalPosition, alertRadius);
            return;
        }

        BroadcastThreat(terminalPosition, alertRadius);
    }

    public void ReportEmergencyAlarm(Vector3 alarmPosition, float alertRadius)
    {
        if (IsOwner && IsSpawned)
        {
            ReportEmergencyAlarmServerRpc(alarmPosition, alertRadius);
            return;
        }

        BroadcastEmergencyAlarm(alarmPosition, alertRadius);
    }

    [ServerRpc]
    private void RequestTerminalCommandCompletionServerRpc(
        string terminalId,
        int rawCommand)
    {
        TerminalCommands command = (TerminalCommands)rawCommand;
        if (command != TerminalCommands.UnlockDoor &&
            command != TerminalCommands.DownloadData &&
            command != TerminalCommands.ConnectServer)
            return;
        if (!ConnectionDevice.CanSynchronizeCompletion(terminalId, command))
            return;

        ApplyTerminalCommandCompletionClientRpc(terminalId, rawCommand);
    }

    [ClientRpc]
    private void ApplyTerminalCommandCompletionClientRpc(
        string terminalId,
        int rawCommand)
    {
        ConnectionDevice.ApplySynchronizedCompletion(
            terminalId,
            (TerminalCommands)rawCommand);
    }

    [ServerRpc]
    private void ShowForTeammateServerRpc(
        string terminalLabel,
        string value,
        string valueLabel,
        ServerRpcParams rpcParams = default)
    {
        ClientRpcParams targets = TeammatesOf(rpcParams.Receive.SenderClientId);
        if (targets.Send.TargetClientIds.Count == 0)
            return;

        ShowHintClientRpc(terminalLabel, value, valueLabel, targets);
    }

    [ServerRpc]
    private void HideForTeammateServerRpc(ServerRpcParams rpcParams = default)
    {
        ClientRpcParams targets = TeammatesOf(rpcParams.Receive.SenderClientId);
        if (targets.Send.TargetClientIds.Count == 0)
            return;

        HideHintClientRpc(targets);
    }

    [ServerRpc]
    private void ReportTerminalFailureServerRpc(
        Vector3 terminalPosition,
        float alertRadius)
    {
        BroadcastThreat(terminalPosition, alertRadius);
    }

    [ServerRpc]
    private void ReportEmergencyAlarmServerRpc(
        Vector3 alarmPosition,
        float alertRadius)
    {
        BroadcastEmergencyAlarm(alarmPosition, alertRadius);
    }

    private static void BroadcastEmergencyAlarm(
        Vector3 alarmPosition,
        float alertRadius)
    {
        WorldNoiseSystem.Emit(alarmPosition, alertRadius);

        PatrolRobotAI responder = DispatchClosestEmergencyResponder(alarmPosition);
        Debug.Log(
            responder != null
                ? $"[Elevator Alarm] {responder.name} is the closest reachable responder."
                : "[Elevator Alarm] No robot has a complete NavMesh path to the response waypoint.",
            responder);
    }

    public static PatrolRobotAI DispatchClosestEmergencyResponder(Vector3 alarmPosition)
    {

        PatrolRobotAI[] robots = FindObjectsByType<PatrolRobotAI>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        PatrolRobotAI closestRobot = null;
        float shortestPath = float.PositiveInfinity;
        foreach (PatrolRobotAI robot in robots)
        {
            if (robot == null || !robot.enabled || !robot.gameObject.activeInHierarchy)
                continue;
            if (!robot.TryGetEmergencyPath(alarmPosition, out _, out float pathDistance))
                continue;
            if (pathDistance >= shortestPath)
                continue;

            shortestPath = pathDistance;
            closestRobot = robot;
        }

        closestRobot?.ReceiveEmergencyAlarm(alarmPosition);
        return closestRobot;
    }

    private static void BroadcastThreat(Vector3 terminalPosition, float alertRadius)
    {
        WorldNoiseSystem.Emit(terminalPosition, alertRadius);

        Collider[] hits = Physics.OverlapSphere(
            terminalPosition,
            alertRadius,
            ~0,
            QueryTriggerInteraction.Ignore);
        HashSet<PatrolRobotAI> notifiedRobots = new();
        foreach (Collider hit in hits)
        {
            PatrolRobotAI robot = hit.GetComponentInParent<PatrolRobotAI>();
            if (robot != null && notifiedRobots.Add(robot))
                robot.ReceiveCCTVReport(terminalPosition);
        }
    }

    [ClientRpc]
    private void ShowHintClientRpc(
        string terminalLabel,
        string value,
        string valueLabel,
        ClientRpcParams rpcParams = default)
    {
        EnsureHintInterface();
        hintText.text =
            $"<size=70%><color=#{DefragUiTheme.Hex(RuntimeUi.Theme.dim)}>해커가 요청한 단어  //  {terminalLabel}</color></size>\n\n" +
            $"<size=140%><color=#{DefragUiTheme.Hex(RuntimeUi.Theme.highlight)}>{value}</color></size>\n\n" +
            $"<size=70%>무전으로 불러주세요 (철자 하나씩!)</size>";
        hintCanvas.gameObject.SetActive(true);
        UiSfx.Play(UiCue.Alert);
        MinigameTutorial.ShowFloating(new TutorialCard
        {
            Id = "terminal.partnerHint",
            Role = "동료 • 해커 지원",
            Title = "해커가 단어를 기다려요",
            Goal = "화면 가운데 단어를 무전으로 해커에게 알려주세요.",
            Steps = new[]
            {
                ("", "해커 화면에서는 이 단어가 가려져 있어요."),
                ("", "철자를 하나씩 또박또박 불러주면 해커가 입력합니다.")
            }
        });

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

        Image panel = RuntimeUi.FramedPanel("Fragment Panel", canvasObject.transform, RuntimeUi.Theme.panelRaised, 22f);
        RuntimeUi.Place(panel.rectTransform, new Vector2(0.3f, 0.36f), new Vector2(0.7f, 0.62f));
        RuntimeUi.Scanlines(panel.transform, 0.05f);

        hintText = RuntimeUi.Text("Fragment Text", panel.transform, 28f, TextAlignmentOptions.Center, null, RuntimeUi.Theme.text);
        RuntimeUi.Stretch(hintText.rectTransform, 22f);
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
