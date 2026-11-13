using System;
using System.Collections.Generic;

namespace SteeringTrainer.Core.Training.HeelToe
{
    /// <summary>
    /// 1回のヒール＆トゥ試行結果
    /// </summary>
    public class HeelToeAttemptResult
    {
        public int AttemptNumber { get; set; }
        public double TargetBrake { get; set; }
        public double TargetThrottle { get; set; }

        public double ActualBrakeBeforeBlip { get; set; }
        public double ActualMinBrakeDuringBlip { get; set; }
        public double ActualMaxBrakeDuringBlip { get; set; }
        public double ActualBrakeAfterBlip { get; set; }

        public double ActualPeakThrottle { get; set; }
        public double BlipDurationMs { get; set; }
        public double BlipReactionTimeMs { get; set; }

        // スコア内訳 (合計100点)
        public double BrakeStabilityScore { get; set; } // 最大50点
        public double BlipTimingScore { get; set; }     // 最大20点
        public double BlipAmountScore { get; set; }     // 最大20点
        public double BlipSmoothnessScore { get; set; } // 最大10点
        public double TotalScore { get; set; }          // 0..100点

        public string Grade { get; set; } = "MISS";
        public string PrimaryFeedback { get; set; } = string.Empty;

        public List<HeelToeFailureReason> FailureReasons { get; set; } = new();
        public List<string> FailureDescriptions { get; set; } = new();

        public List<HeelToeSample> RecordedSamples { get; set; } = new();

        public bool IsPerfect => TotalScore >= 95.0 && FailureReasons.Count == 0;
        public bool IsSuccess => TotalScore >= 70.0;
    }

    /// <summary>
    /// 10回連続チャレンジの集計サマリー
    /// </summary>
    public class HeelToeChallengeSummary
    {
        public int TotalAttempts { get; set; }
        public double AverageScore { get; set; }
        public double BestScore { get; set; }
        public double BrakeStabilityAverage { get; set; }
        public double BlipTimingAverage { get; set; }
        public double BlipAmountAverage { get; set; }
        public int PerfectCount { get; set; }
        public int MissCount { get; set; }
        public List<HeelToeAttemptResult> Attempts { get; set; } = new();
    }
}
