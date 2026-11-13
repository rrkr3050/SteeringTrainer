using System;

namespace SteeringTrainer.Core.Training.HeelToe
{
    /// <summary>
    /// ヒール＆トゥ練習設定
    /// </summary>
    public class HeelToeConfig
    {
        public HeelToeDifficulty Difficulty { get; set; } = HeelToeDifficulty.Normal;

        /// <summary>目標ブレーキ量 (%)。例: 30, 50, 70, 80</summary>
        public double TargetBrake { get; set; } = 50.0;

        /// <summary>目標ブレーキ量のランダム出現最小値 (%)。5%刻み</summary>
        public double RandomBrakeMin { get; set; } = 5.0;

        /// <summary>目標ブレーキ量のランダム出現最大値 (%)。5%刻み</summary>
        public double RandomBrakeMax { get; set; } = 95.0;

        /// <summary>ブレーキ許容誤差 (±%)。例: 3.0</summary>
        public double BrakeTolerance { get; set; } = 3.0;

        /// <summary>目標アクセルブリップ量 (%)。例: 20, 30, 40, 50, 60</summary>
        public double TargetThrottle { get; set; } = 30.0;

        /// <summary>目標アクセル量のランダム出現最小値 (%)。5%刻み</summary>
        public double RandomThrottleMin { get; set; } = 5.0;

        /// <summary>目標アクセル量のランダム出現最大値 (%)。5%刻み</summary>
        public double RandomThrottleMax { get; set; } = 100.0;

        /// <summary>アクセル許容誤差 (±%)。例: 7.0</summary>
        public double ThrottleTolerance { get; set; } = 7.0;

        /// <summary>目標ブリップ時間 (ms)。例: 200</summary>
        public double TargetBlipDurationMs { get; set; } = 200.0;

        /// <summary>ブレーキ開始検出閾値 (%)。例: 10.0</summary>
        public double BrakeEntryThreshold { get; set; } = 10.0;

        /// <summary>ブレーキ安定時間 (ms)。例: 250</summary>
        public double BrakeStabilizeTimeMs { get; set; } = 250.0;

        /// <summary>安定後ブリップまでのタイムアウト (ms)</summary>
        public double BlipTimeoutMs { get; set; } = 1800.0;

        /// <summary>ブリップ終了後のブレーキ維持判定時間 (ms)</summary>
        public double BrakeRecoveryTimeMs { get; set; } = 200.0;

        /// <summary>合図後の理想的なブリップ開始時間範囲</summary>
        public double IdealReactionMinMs { get; set; } = 120.0;
        public double IdealReactionMaxMs { get; set; } = 400.0;
        public double LateReactionThresholdMs { get; set; } = 700.0;
        public double EarlyReactionThresholdMs { get; set; } = 60.0;

        /// <summary>チャレンジモードの総ラウンド数</summary>
        public int ChallengeRounds { get; set; } = 10;

        /// <summary>ブラインドモード (画面最小限、音響重視)</summary>
        public bool IsBlindMode { get; set; } = false;

        /// <summary>手本モード (理想波形オーバーレイ)</summary>
        public bool ShowIdealWaveform { get; set; } = true;

        public HeelToeConfig()
        {
            ApplyDifficulty(HeelToeDifficulty.Normal);
        }

        public void ApplyDifficulty(HeelToeDifficulty diff)
        {
            Difficulty = diff;
            switch (diff)
            {
                case HeelToeDifficulty.Beginner:
                    TargetBrake = 50.0;
                    BrakeTolerance = 5.0;
                    TargetThrottle = 30.0;
                    ThrottleTolerance = 10.0;
                    TargetBlipDurationMs = 250.0;
                    BrakeStabilizeTimeMs = 200.0;
                    IdealReactionMinMs = 100.0;
                    IdealReactionMaxMs = 500.0;
                    LateReactionThresholdMs = 1000.0;
                    EarlyReactionThresholdMs = 40.0;
                    break;
                case HeelToeDifficulty.Normal:
                    TargetBrake = 50.0;
                    BrakeTolerance = 3.0;
                    TargetThrottle = 30.0;
                    ThrottleTolerance = 7.0;
                    TargetBlipDurationMs = 200.0;
                    BrakeStabilizeTimeMs = 250.0;
                    IdealReactionMinMs = 120.0;
                    IdealReactionMaxMs = 400.0;
                    LateReactionThresholdMs = 700.0;
                    EarlyReactionThresholdMs = 60.0;
                    break;
                case HeelToeDifficulty.Advanced:
                    TargetBrake = 50.0;
                    BrakeTolerance = 2.0;
                    TargetThrottle = 30.0;
                    ThrottleTolerance = 5.0;
                    TargetBlipDurationMs = 180.0;
                    BrakeStabilizeTimeMs = 300.0;
                    IdealReactionMinMs = 150.0;
                    IdealReactionMaxMs = 300.0;
                    LateReactionThresholdMs = 500.0;
                    EarlyReactionThresholdMs = 80.0;
                    break;
                case HeelToeDifficulty.Custom:
                    break;
            }
        }
    }
}
