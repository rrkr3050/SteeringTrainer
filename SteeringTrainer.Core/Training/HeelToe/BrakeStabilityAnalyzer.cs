using System;
using System.Collections.Generic;

namespace SteeringTrainer.Core.Training.HeelToe
{
    /// <summary>
    /// ブレーキ安定性評価アナライザー (最大50点)
    /// 「アクセルを踏んだ瞬間にブレーキが抜ける・踏み増す挙動」を厳格に減点評価する。
    /// </summary>
    public static class BrakeStabilityAnalyzer
    {
        public static (double score, List<HeelToeFailureReason> failures, List<string> descriptions) Analyze(
            double targetBrake,
            double tolerance,
            double brakeBeforeBlip,
            double minBrakeDuringBlip,
            double maxBrakeDuringBlip,
            double brakeAfterBlip)
        {
            var failures = new List<HeelToeFailureReason>();
            var descriptions = new List<string>();

            double score = 50.0;

            // 1. ブレーキ抜け (Brake Drop) の判定
            double dropAmount = targetBrake - minBrakeDuringBlip;
            if (dropAmount > tolerance)
            {
                failures.Add(HeelToeFailureReason.BrakeDrop);
                descriptions.Add($"アクセルを踏んだ瞬間にブレーキが抜けています (最小: {minBrakeDuringBlip:F1}%, 許容下限: {targetBrake - tolerance:F1}%)");
                // 抜け量に応じた大幅減点 (1%超過ごとに5点減点)
                double excess = dropAmount - tolerance;
                score -= Math.Min(40.0, 15.0 + excess * 5.0);
            }

            // 2. ブレーキ踏み増し (Brake Increase) の判定
            double increaseAmount = maxBrakeDuringBlip - targetBrake;
            if (increaseAmount > tolerance)
            {
                failures.Add(HeelToeFailureReason.BrakeIncrease);
                descriptions.Add($"アクセル操作中にブレーキが強くなっています (最大: {maxBrakeDuringBlip:F1}%, 許容上限: {targetBrake + tolerance:F1}%)");
                double excess = increaseAmount - tolerance;
                score -= Math.Min(30.0, 10.0 + excess * 4.0);
            }

            // 3. 全体的なブレ幅 (変動幅) による減点
            double variation = maxBrakeDuringBlip - minBrakeDuringBlip;
            if (variation > tolerance * 1.5 && !failures.Contains(HeelToeFailureReason.BrakeDrop))
            {
                score -= Math.Min(15.0, (variation - tolerance) * 3.0);
            }

            // 4. ブリップ後の復帰値
            double recoveryDiff = Math.Abs(brakeAfterBlip - targetBrake);
            if (recoveryDiff > tolerance)
            {
                score -= Math.Min(10.0, (recoveryDiff - tolerance) * 2.0);
            }

            score = Math.Clamp(score, 0.0, 50.0);
            return (score, failures, descriptions);
        }
    }
}
