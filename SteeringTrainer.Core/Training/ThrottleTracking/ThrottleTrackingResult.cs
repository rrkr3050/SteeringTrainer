using System;

namespace SteeringTrainer.Core.Training.ThrottleTracking
{
    /// <summary>
    /// アクセル波形追従セッションの評価結果
    /// </summary>
    public class ThrottleTrackingResult
    {
        public long Id { get; set; }
        public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>総合スコア (100点満点)</summary>
        public double TotalScore { get; set; }

        /// <summary>基本評価1: 波形精度スコア (40点満点)</summary>
        public double WaveformScore { get; set; }

        /// <summary>基本評価2: 位相精度スコア (30点満点)</summary>
        public double PhaseScore { get; set; }

        /// <summary>基本評価3: ブレーキ安定性スコア (30点満点)</summary>
        public double BrakeStabilityScore { get; set; }

        /// <summary>位相遅延時間 (秒)。正: 操作遅れ、負: 早すぎ(進み)</summary>
        public double PhaseLagSeconds { get; set; }

        /// <summary>ブレーキ維持率 (%): 許容範囲内に収まった時間の割合</summary>
        public double BrakeInToleranceRatio { get; set; }

        /// <summary>アクセル追従率 (%): Sin波形の許容範囲内に収まった時間の割合</summary>
        public double ThrottleInToleranceRatio { get; set; }

        /// <summary>同時達成率 (%): 両ペダルが同時に許容範囲内だった時間の割合</summary>
        public double BothInToleranceRatio { get; set; }

        /// <summary>踏み込み精度 (%): アクセル増加区間の平均絶対誤差</summary>
        public double RisingMeanError { get; set; }

        /// <summary>戻し精度 (%): アクセル減少区間の平均絶対誤差</summary>
        public double FallingMeanError { get; set; }

        /// <summary>踏み込み時ブレーキ維持率 (%)</summary>
        public double RisingBrakeRatio { get; set; }

        /// <summary>戻し時ブレーキ維持率 (%)</summary>
        public double FallingBrakeRatio { get; set; }

        /// <summary>セッション実時間 (秒)</summary>
        public double ElapsedSeconds { get; set; }

        /// <summary>総サンプリング点数</summary>
        public int TotalSamples { get; set; }

        // --- 設定スナップショット ---
        public double ConfigTargetBrake { get; set; }
        public double ConfigBrakeTolerance { get; set; }
        public double ConfigThrottleMin { get; set; }
        public double ConfigThrottleMax { get; set; }
        public double ConfigThrottleTolerance { get; set; }
        public double ConfigCycleDuration { get; set; }
        public double ConfigBpm => ConfigCycleDuration > 0 ? Math.Round(60.0 / ConfigCycleDuration, 1) : 0.0;

        public string Grade => TotalScore switch
        {
            >= 90 => "PERFECT",
            >= 75 => "GREAT",
            >= 60 => "GOOD",
            _ => "TRY AGAIN"
        };
    }
}
