namespace SteeringTrainer.Core.Training.ThrottleTracking
{
    /// <summary>
    /// アクセル波形追従セッション中のサンプリングデータ
    /// </summary>
    public readonly struct ThrottleTrackingSample
    {
        public double SessionTimeSeconds { get; }
        public double ActualBrake { get; }
        public double TargetBrake { get; }
        public bool IsBrakeInTolerance { get; }

        public double ActualThrottle { get; }
        public double TargetThrottle { get; }
        public bool IsThrottleInTolerance { get; }

        public bool IsBothInTolerance => IsBrakeInTolerance && IsThrottleInTolerance;
        public bool IsRisingPhase { get; }

        public double ThrottleAbsoluteError => System.Math.Abs(ActualThrottle - TargetThrottle);
        public double BrakeAbsoluteError => System.Math.Abs(ActualBrake - TargetBrake);

        public ThrottleTrackingSample(
            double sessionTimeSeconds,
            double actualBrake,
            double targetBrake,
            bool isBrakeInTolerance,
            double actualThrottle,
            double targetThrottle,
            bool isThrottleInTolerance,
            bool isRisingPhase)
        {
            SessionTimeSeconds = sessionTimeSeconds;
            ActualBrake = actualBrake;
            TargetBrake = targetBrake;
            IsBrakeInTolerance = isBrakeInTolerance;
            ActualThrottle = actualThrottle;
            TargetThrottle = targetThrottle;
            IsThrottleInTolerance = isThrottleInTolerance;
            IsRisingPhase = isRisingPhase;
        }
    }
}
