namespace SteeringTrainer.Core.Models
{
    /// <summary>
    /// 練習セッションの設定
    /// </summary>
    public class TrainingConfig
    {
        /// <summary>ホイール総回転角度 (°)。例: 900</summary>
        public double WheelRotation { get; set; } = 900.0;

        /// <summary>片側ロック角度 (°)。例: 540</summary>
        public double LockAngle { get; set; } = 540.0;

        /// <summary>片側移動時間 (秒)。例: 2.5</summary>
        public double TimeSideSeconds { get; set; } = 2.5;

        /// <summary>目標角速度 (°/s) = LockAngle / TimeSideSeconds</summary>
        public double TargetVelocity => LockAngle / TimeSideSeconds;

        /// <summary>最大許容角加速度 (°/s²)</summary>
        public double MaxAcceleration { get; set; } = 1000.0;

        /// <summary>フィルタ強度 (0.0 ～ 1.0)</summary>
        public double FilterStrength { get; set; } = 0.1;

        /// <summary>センターオフセット (°)</summary>
        public double CenterOffset { get; set; } = 0.0;

        /// <summary>入力反転フラグ</summary>
        public bool InvertInput { get; set; } = false;

        /// <summary>HANDOVERウィンドウ時間 (秒)</summary>
        public double HandoverWindowSeconds { get; set; } = 0.05;

        /// <summary>HANDOVERウィンドウ角度 (°)</summary>
        public double HandoverWindowDegrees { get; set; } = 30.0;

        // --- スコア閾値 ---
        public double VelocityWarningThresholdPercent { get; set; } = 5.0;
        public double VelocityMissThresholdPercent { get; set; } = 15.0;
    }
}
