using System.Collections.Generic;
using UnityEngine;

namespace DeFrag.B1F
{
    // Generator B markers: fuel shows as a fuzzy zone until a player is close enough to pinpoint it.
    public sealed class GeneratorBRadarContent : IFacilityRadarContent
    {
        private static readonly Color GeneratorColor = new(1f, 0.85f, 0.25f, 1f);
        private static readonly Color FuelColor = new(1f, 0.6f, 0.1f, 1f);
        private const float GoldenAngle = 137.508f;

        private readonly GeneratorBController controller;

        public GeneratorBRadarContent(GeneratorBController controller) => this.controller = controller;

        public void CollectMarkers(List<RadarMarker> markers, IReadOnlyList<Vector3> playerPositions)
        {
            markers.Add(new RadarMarker
            {
                Position = controller.GeneratorPosition,
                Color = GeneratorColor,
                Label = controller.IsPrimed ? "발전기 (시동 대기)" : $"발전기  연료 {controller.ConsumedFuelCans}/{controller.RequiredFuelCans}"
            });

            if (controller.IsPrimed || controller.IsComplete)
                return;

            foreach (GeneratorFuelCan can in controller.WorldFuelCans())
            {
                Vector3 actual = can.SignalAnchor.position;
                if (IsAnyPlayerWithin(playerPositions, actual, controller.FuelRevealDistance))
                {
                    markers.Add(new RadarMarker { Position = actual, Color = FuelColor, Label = "연료통", Pulse = true });
                    continue;
                }

                // Stable per-can offset keeps the zone from revealing the exact spot at its center.
                float angle = can.WorldItem.NetworkObjectId * GoldenAngle * Mathf.Deg2Rad;
                float radius = controller.FuelZoneRadius;
                Vector3 center = actual + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * 0.55f;
                markers.Add(new RadarMarker { Position = center, RadiusMeters = radius, Color = FuelColor, Label = "연료 신호" });
            }
        }

        private static bool IsAnyPlayerWithin(IReadOnlyList<Vector3> players, Vector3 point, float distance)
        {
            foreach (Vector3 player in players)
                if ((player - point).sqrMagnitude <= distance * distance)
                    return true;
            return false;
        }
    }
}
