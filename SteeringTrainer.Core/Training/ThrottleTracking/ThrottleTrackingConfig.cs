using System;

namespace SteeringTrainer.Core.Training.ThrottleTracking
{
    /// <summary>
    /// アクセル波形追従トレーニングの設定
    /// </summary>
    public class ThrottleTrackingConfig
    {
        /// <summary>目標ブレーキ量 (%)。例: 50, 80</summary>
        public double TargetBrake { get; set; } = 50.0;

        /// <summary>ブレーキ許容ブレ幅 (±%)。例: 5.0</summary>
        public double BrakeTolerance { get; set; } = 5.0;

        /// <summary>アクセル操作最小値 (%)。例: 0</summary>
        public double ThrottleMin { get; set; } = 0.0;

        /// <summary>アクセル操作最大値 (%)。例: 40, 80</summary>
        public double ThrottleMax { get; set; } = 40.0;

        /// <summary>アクセル許容ブレ幅 (±%)。例: 5.0</summary>
        public double ThrottleTolerance { get; set; } = 5.0;

        private double _bpm = 15.0; // 初期値: 15 BPM (1周期: 4.0秒)

        /// <summary>
        /// 波形速度 (BPM: Beats Per Minute)。
        /// 上のピーク（ThrottleMax）をワンビートとして計算（1分あたりのピーク到達回数）。
        /// 1周期（秒） = 60.0 / BPM
        /// 設定範囲: 4.0 〜 200.0 BPM (15.0秒 〜 0.30秒)
        /// </summary>
        public double Bpm
        {
            get => _bpm;
            set
            {
                _bpm = Math.Clamp(value, 4.0, 200.0);
            }
        }

        /// <summary>
        /// 1周期にかかる秒数 (波形速度)。
        /// Bpm と完全連動 (CycleDurationSeconds = 60.0 / Bpm)。
        /// </summary>
        public double CycleDurationSeconds
        {
            get => _bpm > 0 ? 60.0 / _bpm : 4.0;
            set
            {
                if (value > 0)
                {
                    _bpm = Math.Clamp(60.0 / value, 4.0, 200.0);
                }
            }
        }

        /// <summary>練習時間 (秒)。例: 30, 60, 120。0なら無制限</summary>
        public double SessionDurationSeconds { get; set; } = 30.0;

        /// <summary>
        /// 指定時刻 t (秒) における目標アクセル値 (%) を計算する
        /// t=0 で ThrottleMin からスタートし、滑らかに ThrottleMax へ上昇後、ThrottleMin へ戻る正弦波
        /// y(t) = min + (max - min) * (1 - cos(2*pi*t / T)) / 2
        /// </summary>
        public double CalculateTargetThrottle(double timeSeconds)
        {
            if (CycleDurationSeconds <= 0) return ThrottleMin;
            double min = Math.Min(ThrottleMin, ThrottleMax);
            double max = Math.Max(ThrottleMin, ThrottleMax);
            double range = max - min;
            if (range <= 0) return min;

            double phase = (timeSeconds % CycleDurationSeconds) / CycleDurationSeconds; // 0.0 .. 1.0
            double normalizedSin = (1.0 - Math.Cos(phase * 2.0 * Math.PI)) * 0.5; // 0.0 .. 1.0
            return min + normalizedSin * range;
        }

        /// <summary>
        /// 指定時刻 t (秒) が踏み込み（増加）フェーズかどうかを判定
        /// </summary>
        public bool IsRisingPhase(double timeSeconds)
        {
            if (CycleDurationSeconds <= 0) return true;
            double phase = (timeSeconds % CycleDurationSeconds) / CycleDurationSeconds;
            return phase < 0.5;
        }
    }
}
