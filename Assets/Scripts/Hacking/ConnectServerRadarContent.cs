using System.Collections.Generic;
using UnityEngine;

// Relay markers for the uplink radar: every relay is a dim dot, the active target pulses.
public sealed class ConnectServerRadarContent : IFacilityRadarContent
{
    private const float TargetRingMeters = 5f;

    private readonly ConnectServerCoordinator coordinator;

    public ConnectServerRadarContent(ConnectServerCoordinator coordinator) => this.coordinator = coordinator;

    public void CollectMarkers(List<RadarMarker> markers, IReadOnlyList<Vector3> playerPositions)
    {
        if (coordinator == null)
            return;

        DefragUiTheme theme = RuntimeUi.Theme;
        bool scanning = coordinator.Phase == ConnectServerUplinkPhase.AwaitingOpticalScan ||
                        coordinator.Phase == ConnectServerUplinkPhase.AwaitingVerification;
        string targetId = coordinator.TargetRelayId;
        foreach (OpticalRelayNode relay in coordinator.RelayNodes)
        {
            if (relay == null || !relay.Selectable)
                continue;
            bool target = scanning && relay.RelayId == targetId;
            markers.Add(new RadarMarker
            {
                Position = relay.ScanAnchor.position,
                RadiusMeters = target ? TargetRingMeters : 0f,
                Color = target ? theme.info : new Color(theme.dim.r, theme.dim.g, theme.dim.b, 0.8f),
                Label = target ? $"목표 {ShortId(relay.RelayId)}" : ShortId(relay.RelayId),
                Pulse = target
            });
        }
    }

    public static string ShortId(string relayId) =>
        relayId.StartsWith("TERMINAL_") ? $"T-{relayId.Substring(9)}" : relayId;
}
