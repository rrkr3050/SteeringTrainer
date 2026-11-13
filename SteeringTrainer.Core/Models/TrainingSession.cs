using System;
using System.Collections.Generic;

namespace SteeringTrainer.Core.Models
{
    /// <summary>
    /// 1練習セッションの結果
    /// </summary>
    public class TrainingSession
    {
        public int Id { get; set; }
        public DateTime StartedAt { get; set; }
        public double DurationSeconds { get; set; }
        public double WheelRotation { get; set; }
        public double LockAngle { get; set; }
        public double TargetVelocity { get; set; }
        public double Score { get; set; }
        public double VelocityScore { get; set; }
        public double AccelerationScore { get; set; }
        public double HandoverScore { get; set; }
        public double OvershootScore { get; set; }
        public double SymmetryScore { get; set; }
        public string? Notes { get; set; }
        public List<SteeringSample> Samples { get; set; } = new();
    }
}
