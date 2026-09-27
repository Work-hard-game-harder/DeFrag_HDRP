using System;
using UnityEngine;

namespace DeFrag.B1F
{
    public enum GeneratorBIgnitionResult : byte
    {
        NotReady,
        Hit,
        Backfire
    }

    [Serializable]
    public struct GeneratorBCrankSettings
    {
        [Tooltip("번갈아 누른 한 번의 크랭크가 올리는 회전수(0~1).")]
        public float strokeImpulse;
        [Tooltip("같은 키를 연속으로 누를 때 적용되는 배율. 번갈아 누르도록 유도합니다.")]
        [Range(0f, 1f)] public float repeatStrokeFactor;
        [Tooltip("초당 회전수 감소 비율. 안정 회전수 ≈ strokeImpulse × 초당 연타수 ÷ 이 값.")]
        public float decayPerSecond;
        [Tooltip("회전수 1.0일 때 점화 바늘의 초당 회전 각도.")]
        public float degreesPerSecondAtFullRpm;
        [Tooltip("실린더별 점화 가능 회전수 구간(x=최소, y=최대). 배열 길이가 필요한 점화 횟수입니다.")]
        public Vector2[] cylinderBands;
        public float baseSectorDegrees;
        [Tooltip("역화할 때마다 점화창이 넓어지는 각도. 처음 하는 플레이어를 위한 보정입니다.")]
        public float sectorGrowthPerBackfire;
        public float maxSectorDegrees;
        public float minimumStrokeInterval;
        public float hitRpmBonus;
        [Range(0f, 1f)] public float backfireRpmRetained;
        public float hitCooldown;
        public float backfireCooldown;

        public static GeneratorBCrankSettings Default => new()
        {
            strokeImpulse = 0.06f,
            repeatStrokeFactor = 0.3f,
            decayPerSecond = 0.66f,
            degreesPerSecondAtFullRpm = 300f,
            cylinderBands = new[]
            {
                new Vector2(0.28f, 0.50f),
                new Vector2(0.42f, 0.64f),
                new Vector2(0.56f, 0.80f)
            },
            baseSectorDegrees = 90f,
            sectorGrowthPerBackfire = 15f,
            maxSectorDegrees = 150f,
            minimumStrokeInterval = 0.05f,
            hitRpmBonus = 0.08f,
            backfireRpmRetained = 0.35f,
            hitCooldown = 0.8f,
            backfireCooldown = 1.5f
        };
    }

    // Server-side flywheel model: crank strokes raise RPM, RPM spins the ignition needle.
    public sealed class GeneratorBCrankSimulation
    {
        private const int HistoryLength = 48;

        private readonly double[] historyTime = new double[HistoryLength];
        private readonly float[] historyAngle = new float[HistoryLength];
        private readonly float[] historyRpm = new float[HistoryLength];
        private int historyHead;
        private int historyCount;
        private int lastStrokeSide = -1;
        private double lastStrokeTime = double.NegativeInfinity;

        public GeneratorBCrankSettings Settings { get; set; }
        public float Rpm { get; private set; }
        public float Angle { get; private set; }
        public int CylinderCount => Settings.cylinderBands?.Length ?? 0;

        public GeneratorBCrankSimulation(GeneratorBCrankSettings settings) => Settings = settings;

        public void Reset()
        {
            Rpm = 0f;
            Angle = 0f;
            lastStrokeSide = -1;
            historyCount = 0;
        }

        public bool ApplyStroke(int side, double now)
        {
            if (now - lastStrokeTime < Settings.minimumStrokeInterval)
                return false;

            float impulse = side == lastStrokeSide
                ? Settings.strokeImpulse * Settings.repeatStrokeFactor
                : Settings.strokeImpulse;
            Rpm = Mathf.Clamp01(Rpm + impulse);
            lastStrokeSide = side;
            lastStrokeTime = now;
            return true;
        }

        public void Tick(float deltaTime, double now)
        {
            // Proportional drag makes steady RPM linear in stroke cadence, so "faster/slower" calls are intuitive.
            Rpm = Mathf.Max(0f, Rpm - Settings.decayPerSecond * Rpm * deltaTime);
            Angle = Mathf.Repeat(Angle + Rpm * Settings.degreesPerSecondAtFullRpm * deltaTime, 360f);

            historyTime[historyHead] = now;
            historyAngle[historyHead] = Angle;
            historyRpm[historyHead] = Rpm;
            historyHead = (historyHead + 1) % HistoryLength;
            historyCount = Mathf.Min(historyCount + 1, HistoryLength);
        }

        public Vector2 GetBand(int cylinder)
        {
            Vector2[] bands = Settings.cylinderBands;
            return bands == null || bands.Length == 0
                ? new Vector2(0f, 1f)
                : bands[Mathf.Clamp(cylinder, 0, bands.Length - 1)];
        }

        public float GetSectorDegrees(int backfires) => Mathf.Min(
            Settings.maxSectorDegrees,
            Settings.baseSectorDegrees + Settings.sectorGrowthPerBackfire * backfires);

        public static bool IsInSector(float angle, float sectorDegrees) =>
            Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) <= sectorDegrees * 0.5f;

        public static bool IsInBand(float rpm, Vector2 band) => rpm >= band.x && rpm <= band.y;

        // Judges an ignition press against the flywheel state the pressing player actually saw.
        public GeneratorBIgnitionResult Evaluate(int cylinder, int backfires, double observedTime)
        {
            SampleAt(observedTime, out float angle, out float rpm);
            if (!IsInBand(rpm, GetBand(cylinder)))
                return GeneratorBIgnitionResult.NotReady;
            return IsInSector(angle, GetSectorDegrees(backfires))
                ? GeneratorBIgnitionResult.Hit
                : GeneratorBIgnitionResult.Backfire;
        }

        public void ApplyHit() => Rpm = Mathf.Clamp01(Rpm + Settings.hitRpmBonus);

        public void ApplyBackfire() => Rpm *= Settings.backfireRpmRetained;

        private void SampleAt(double time, out float angle, out float rpm)
        {
            angle = Angle;
            rpm = Rpm;
            for (int i = 0; i < historyCount; i++)
            {
                int index = (historyHead - 1 - i + HistoryLength) % HistoryLength;
                if (historyTime[index] > time)
                    continue;
                angle = historyAngle[index];
                rpm = historyRpm[index];
                return;
            }
        }
    }
}
