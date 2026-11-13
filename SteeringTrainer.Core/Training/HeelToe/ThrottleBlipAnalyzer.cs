using System;
using System.Collections.Generic;

namespace SteeringTrainer.Core.Training.HeelToe
{
    /// <summary>
    /// アクセルブリップ評価アナライザー
    /// ブリップ量 (20点)、ブリップタイミング (20点)、ブリップ時間/滑らかさ (10点) を算出する。
    /// </summary>
    public static class ThrottleBlipAnalyzer
    {
        public static (
            double amountScore,
            double timingScore,
            double smoothnessScore,
            List<HeelToeFailureReason> failures,
            List<string> descriptions) Analyze(
            double targetThrottle,
            double throttleTolerance,
            double actualPeakThrottle,
            double reactionTimeMs,
            double blipDurationMs,
            double targetDurationMs,
            double idealReactionMinMs = 120.0,
            double idealReactionMaxMs = 400.0,
            double earlyReactionThresholdMs = 60.0,
            double lateReactionThresholdMs = 700.0)
        {
            var failures = new List<HeelToeFailureReason>();
            var descriptions = new List<string>();

            // 1. ブリップ量評価 (最大20点)
            double amountScore = 20.0;
            if (actualPeakThrottle < 5.0)
            {
                failures.Add(HeelToeFailureReason.NoBlip);
                descriptions.Add("アクセルが検出されませんでした");
                amountScore = 0.0;
            }
            else
            {
                double amountDiff = actualPeakThrottle - targetThrottle;
                if (amountDiff < -throttleTolerance)
                {
                    failures.Add(HeelToeFailureReason.UnderBlip);
                    descriptions.Add($"アクセル量が不足しています (ピーク: {actualPeakThrottle:F1}%, 目標: {targetThrottle:F0}%)");
                    double excess = Math.Abs(amountDiff) - throttleTolerance;
                    amountScore = Math.Max(0.0, 20.0 - excess * 2.0);
                }
                else if (amountDiff > throttleTolerance)
                {
                    failures.Add(HeelToeFailureReason.OverBlip);
                    descriptions.Add($"アクセルを踏みすぎています (ピーク: {actualPeakThrottle:F1}%, 目標: {targetThrottle:F0}%)");
                    double excess = amountDiff - throttleTolerance;
                    amountScore = Math.Max(0.0, 20.0 - excess * 1.5);
                }
                else
                {
                    // 許容範囲内: 誤差に応じた微減点
                    double errRatio = Math.Abs(amountDiff) / Math.Max(1.0, throttleTolerance);
                    amountScore = 20.0 - errRatio * 2.0;
                }
            }

            // 2. ブリップタイミング評価 (最大20点)
            double timingScore = 20.0;
            if (reactionTimeMs < earlyReactionThresholdMs)
            {
                failures.Add(HeelToeFailureReason.EarlyBlip);
                descriptions.Add("ブレーキが安定する前にアクセルを踏んでいます");
                timingScore = Math.Max(0.0, 20.0 - (earlyReactionThresholdMs - reactionTimeMs) * 0.3);
            }
            else if (reactionTimeMs > lateReactionThresholdMs)
            {
                failures.Add(HeelToeFailureReason.LateBlip);
                descriptions.Add($"ブリップが遅れています (反応時間: {reactionTimeMs:F0}ms)");
                double excess = reactionTimeMs - lateReactionThresholdMs;
                timingScore = Math.Max(0.0, 20.0 - excess * 0.04);
            }
            else
            {
                if (reactionTimeMs >= idealReactionMinMs && reactionTimeMs <= idealReactionMaxMs)
                {
                    timingScore = 20.0;
                }
                else
                {
                    timingScore = 18.0;
                }
            }

            // 3. ブリップ時間・キレの評価 (最大10点)
            double smoothnessScore = 10.0;
            if (blipDurationMs > 350.0)
            {
                failures.Add(HeelToeFailureReason.LongBlip);
                descriptions.Add($"アクセルを長く踏みすぎています (時間: {blipDurationMs:F0}ms, 目標: {targetDurationMs:F0}ms)");
                double excess = blipDurationMs - 350.0;
                smoothnessScore = Math.Max(0.0, 10.0 - excess * 0.05);
            }
            else if (blipDurationMs < 80.0)
            {
                // 短すぎる
                smoothnessScore = 7.0;
            }
            else
            {
                double durationDiff = Math.Abs(blipDurationMs - targetDurationMs);
                if (durationDiff <= 50.0)
                {
                    smoothnessScore = 10.0;
                }
                else
                {
                    smoothnessScore = Math.Max(5.0, 10.0 - (durationDiff - 50.0) * 0.05);
                }
            }

            amountScore = Math.Clamp(amountScore, 0.0, 20.0);
            timingScore = Math.Clamp(timingScore, 0.0, 20.0);
            smoothnessScore = Math.Clamp(smoothnessScore, 0.0, 10.0);

            return (amountScore, timingScore, smoothnessScore, failures, descriptions);
        }
    }
}
