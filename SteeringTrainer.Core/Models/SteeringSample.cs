using System;

namespace SteeringTrainer.Core.Models
{
    /// <summary>
    /// 1サンプル分のステアリングおよびペダル統合入力データ
    /// </summary>
    public record SteeringSample(
        long TimestampTicks,       // Stopwatch.GetTimestamp() による高精度タイムスタンプ
        double RawNormalized,      // ハンコンから直接取得した正規化値 (-1.0 ～ +1.0)
        double FilteredNormalized, // フィルタ後の正規化値
        double AngleDegrees,       // フィルタ後の角度 (°)
        double RawAngleDegrees,    // Raw角度 (°)
        double Velocity,           // 角速度 (°/s)
        double Acceleration,       // 角加速度 (°/s²)
        double SignedAcceleration,
        double AbsoluteAcceleration,
        double Throttle = 0.0,     // 0.0 ～ 1.0 (0% ～ 100%)
        double Brake = 0.0,        // 0.0 ～ 1.0 (0% ～ 100%)
        double Clutch = 0.0,       // 0.0 ～ 1.0 (0% ～ 100%)
        double RawThrottle = 0.0,
        double RawBrake = 0.0,
        double RawClutch = 0.0,
        uint RawX = 0,
        uint RawY = 0,
        uint RawZ = 0,
        uint RawR = 0,
        uint RawU = 0,
        uint RawV = 0
    )
    {
        /// <summary>サンプリング時刻（秒）を取得（Stopwatch周波数基準）</summary>
        public double TimestampSeconds => (double)TimestampTicks / System.Diagnostics.Stopwatch.Frequency;

        public static SteeringSample Zero => new SteeringSample(
            System.Diagnostics.Stopwatch.GetTimestamp(),
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0);
    }
}
