using System;

namespace SteeringTrainer.Infrastructure.Persistence
{
    /// <summary>
    /// アクセル波形追従トレーニングの履歴レコード
    /// </summary>
    public sealed record ThrottleTrackingHistoryEntry(
        long Id,
        DateTime RecordedAtUtc,
        double TotalScore,
        double WaveformScore,
        double PhaseScore,
        double BrakeStabilityScore,
        double PhaseLagSeconds,
        double BrakeInToleranceRatio,
        double ThrottleInToleranceRatio,
        double BothInToleranceRatio,
        double RisingMeanError,
        double FallingMeanError,
        double RisingBrakeRatio,
        double FallingBrakeRatio,
        double ElapsedSeconds,
        int TotalSamples,
        double TargetBrake,
        double BrakeTolerance,
        double ThrottleMin,
        double ThrottleMax,
        double ThrottleTolerance,
        double CycleDuration,
        string Grade)
    {
        /// <summary>上のピークをワンビートとするBPM (60.0 / CycleDuration)</summary>
        public double Bpm => CycleDuration > 0 ? Math.Round(60.0 / CycleDuration, 1) : 0.0;
    }
}
