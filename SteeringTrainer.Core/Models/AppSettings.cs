namespace SteeringTrainer.Core.Models
{
    /// <summary>
    /// アプリケーション全体の設定保持モデル（JSONシリアライズ対応）
    /// </summary>
    public class AppSettings
    {
        /// <summary>選択中の入力デバイスID (例: "winmm_1")</summary>
        public string? SelectedDeviceId { get; set; }

        /// <summary>選択中の入力デバイス名 (確認・表示用)</summary>
        public string? SelectedDeviceName { get; set; }

        /// <summary>総舵角 (度、初期値900)</summary>
        public double WheelRotation { get; set; } = 900.0;

        /// <summary>センターオフセット (度)</summary>
        public double CenterOffset { get; set; } = 0.0;

        /// <summary>ステアリング入力反転フラグ</summary>
        public bool InvertInput { get; set; } = false;

        /// <summary>EMAフィルタ強度 (0-50%)</summary>
        public int FilterStrengthPercent { get; set; } = 10;

        /// <summary>常に最前面表示</summary>
        public bool AlwaysOnTop { get; set; } = false;

        /// <summary>選択タブインデックス</summary>
        public int SelectedTabIndex { get; set; } = 0;

        /// <summary>ハンコンのボタンで開始/停止をトグルするかどうか</summary>
        public bool EnableWheelButtonStartStop { get; set; } = true;

        /// <summary>ペダルキャリブレーション設定</summary>
        public DevicePedalSettings PedalSettings { get; set; } = new();

        // --- ヒール＆トゥ練習設定の保存 ---
        public double TargetBrake { get; set; } = 50.0;
        public double TargetThrottle { get; set; } = 60.0;
        public double BrakeTolerance { get; set; } = 5.0;
        public double ThrottleTolerance { get; set; } = 15.0;
        public int SelectedDifficultyIndex { get; set; } = 1;
        public int AudioVolumePercent { get; set; } = 50;
        public bool ShowIdealWaveform { get; set; } = true;
        public bool IsBlindMode { get; set; } = false;
        public bool RandomizeFreeBrake { get; set; } = false;
        public bool RandomizeFreeThrottle { get; set; } = false;
        public double RandomBrakeMin { get; set; } = 5.0;
        public double RandomBrakeMax { get; set; } = 95.0;
        public double RandomThrottleMin { get; set; } = 5.0;
        public double RandomThrottleMax { get; set; } = 100.0;

        // --- アクセル波形追従練習設定の保存 ---
        public double TrackingTargetBrake { get; set; } = 50.0;
        public double TrackingBrakeTolerance { get; set; } = 5.0;
        public double TrackingThrottleMin { get; set; } = 0.0;
        public double TrackingThrottleMax { get; set; } = 40.0;
        public double TrackingThrottleTolerance { get; set; } = 5.0;
        public double TrackingBpm { get; set; } = 15.0;
        public double TrackingCycleDuration { get; set; } = 4.0;
        public double TrackingSessionDuration { get; set; } = 30.0;
        public bool TrackingEnableMetronome { get; set; } = false;
        public int TrackingMetronomeVolumePercent { get; set; } = 70;
    }
}
