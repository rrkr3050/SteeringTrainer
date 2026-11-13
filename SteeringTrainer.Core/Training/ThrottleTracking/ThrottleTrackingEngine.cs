using SteeringTrainer.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SteeringTrainer.Core.Training.ThrottleTracking
{
    public enum ThrottleTrackingState
    {
        Idle,
        WaitingForBrake,
        Tracking,
        Finished
    }

    /// <summary>
    /// アクセル波形追従トレーニングのステートマシンおよび評価エンジン
    /// </summary>
    public class ThrottleTrackingEngine
    {
        private readonly object _lock = new();
        private readonly List<ThrottleTrackingSample> _sessionSamples = new();

        private long _startTicks;
        private double _brakeStableStartSeconds;
        private bool _isBrakeStable;
        private int _lastMetronomeBeatIndex = -1;

        public ThrottleTrackingConfig Config { get; } = new();
        public ThrottleTrackingState CurrentState { get; private set; } = ThrottleTrackingState.Idle;

        public double CurrentSessionSeconds { get; private set; }
        public double RemainingSeconds { get; private set; }

        public event Action<ThrottleTrackingState, string>? StateChanged;
        public event Action<ThrottleTrackingSample>? SampleRecorded;
        public event Action<ThrottleTrackingResult>? SessionFinished;
        public event Action<bool>? MetronomeTriggered;

        public void StartSession()
        {
            lock (_lock)
            {
                _sessionSamples.Clear();
                CurrentSessionSeconds = 0;
                RemainingSeconds = Config.SessionDurationSeconds;
                _isBrakeStable = false;
                _brakeStableStartSeconds = 0;
                _lastMetronomeBeatIndex = -1;

                TransitionTo(ThrottleTrackingState.WaitingForBrake,
                    $"ブレーキを目標 {Config.TargetBrake:F0}% (±{Config.BrakeTolerance:F0}%) まで踏んで安定させてください");
            }
        }

        public void StopSession(bool completed = false)
        {
            ThrottleTrackingResult? result = null;
            lock (_lock)
            {
                if (CurrentState == ThrottleTrackingState.Idle) return;

                if (completed || _sessionSamples.Count >= 50)
                {
                    result = CalculateResult();
                }

                TransitionTo(ThrottleTrackingState.Finished, completed ? "セッション完了！" : "セッション停止");
            }

            if (result != null)
            {
                SessionFinished?.Invoke(result);
            }
        }

        public void ProcessSample(SteeringSample sample)
        {
            lock (_lock)
            {
                if (CurrentState == ThrottleTrackingState.Idle || CurrentState == ThrottleTrackingState.Finished)
                    return;

                double currentBrake = sample.Brake * 100.0;
                double currentThrottle = sample.Throttle * 100.0;

                if (CurrentState == ThrottleTrackingState.WaitingForBrake)
                {
                    double brakeDiff = Math.Abs(currentBrake - Config.TargetBrake);
                    double nowSec = sample.TimestampSeconds;

                    if (brakeDiff <= Config.BrakeTolerance)
                    {
                        if (!_isBrakeStable)
                        {
                            _isBrakeStable = true;
                            _brakeStableStartSeconds = nowSec;
                        }
                        else if (nowSec - _brakeStableStartSeconds >= 0.4) // 0.4秒安定でスタート
                        {
                            _startTicks = sample.TimestampTicks;
                            TransitionTo(ThrottleTrackingState.Tracking, "アクセル波形追従スタート！");
                        }
                    }
                    else
                    {
                        _isBrakeStable = false;
                    }
                    return;
                }

                if (CurrentState == ThrottleTrackingState.Tracking)
                {
                    double elapsed = (sample.TimestampTicks - _startTicks) / (double)Stopwatch.Frequency;
                    if (elapsed < 0) elapsed = 0;
                    CurrentSessionSeconds = elapsed;

                    if (Config.SessionDurationSeconds > 0)
                    {
                        RemainingSeconds = Math.Max(0, Config.SessionDurationSeconds - elapsed);
                    }
                    else
                    {
                        RemainingSeconds = 0;
                    }

                    double targetThrottle = Config.CalculateTargetThrottle(elapsed);
                    double targetBrake = Config.TargetBrake;
                    bool isRising = Config.IsRisingPhase(elapsed);

                    bool isBrakeOk = Math.Abs(currentBrake - targetBrake) <= Config.BrakeTolerance;
                    bool isThrottleOk = Math.Abs(currentThrottle - targetThrottle) <= Config.ThrottleTolerance;

                    var trackingSample = new ThrottleTrackingSample(
                        elapsed,
                        currentBrake,
                        targetBrake,
                        isBrakeOk,
                        currentThrottle,
                        targetThrottle,
                        isThrottleOk,
                        isRising);

                    _sessionSamples.Add(trackingSample);
                    SampleRecorded?.Invoke(trackingSample);

                    // メトロノーム ピーク判定 (下ピーク: t=0, 1T, 2T..., 上ピーク: t=0.5T, 1.5T, 2.5T...)
                    double halfPeriod = Config.CycleDurationSeconds * 0.5;
                    if (halfPeriod > 0.001)
                    {
                        int currentBeatIndex = (int)Math.Floor((elapsed / halfPeriod) + 1e-5);
                        if (currentBeatIndex > _lastMetronomeBeatIndex)
                        {
                            if (currentBeatIndex - _lastMetronomeBeatIndex > 2)
                            {
                                _lastMetronomeBeatIndex = currentBeatIndex - 1;
                            }
                            while (_lastMetronomeBeatIndex < currentBeatIndex)
                            {
                                _lastMetronomeBeatIndex++;
                                bool isTop = (_lastMetronomeBeatIndex % 2) != 0;
                                MetronomeTriggered?.Invoke(isTop);
                            }
                        }
                    }

                    if (Config.SessionDurationSeconds > 0 && elapsed >= Config.SessionDurationSeconds)
                    {
                        StopSession(completed: true);
                    }
                }
            }
        }

        private void TransitionTo(ThrottleTrackingState state, string message)
        {
            CurrentState = state;
            StateChanged?.Invoke(state, message);
        }

        private ThrottleTrackingResult CalculateResult()
        {
            var result = new ThrottleTrackingResult
            {
                ConfigTargetBrake = Config.TargetBrake,
                ConfigBrakeTolerance = Config.BrakeTolerance,
                ConfigThrottleMin = Config.ThrottleMin,
                ConfigThrottleMax = Config.ThrottleMax,
                ConfigThrottleTolerance = Config.ThrottleTolerance,
                ConfigCycleDuration = Config.CycleDurationSeconds,
                ElapsedSeconds = CurrentSessionSeconds,
                TotalSamples = _sessionSamples.Count
            };

            if (_sessionSamples.Count == 0) return result;

            int brakeOkCount = 0;
            int throttleOkCount = 0;
            int bothOkCount = 0;

            int risingCount = 0;
            int fallingCount = 0;
            int risingBrakeOkCount = 0;
            int fallingBrakeOkCount = 0;

            double risingThrottleErrorSum = 0;
            double fallingThrottleErrorSum = 0;

            foreach (var s in _sessionSamples)
            {
                if (s.IsBrakeInTolerance) brakeOkCount++;
                if (s.IsThrottleInTolerance) throttleOkCount++;
                if (s.IsBothInTolerance) bothOkCount++;

                if (s.IsRisingPhase)
                {
                    risingCount++;
                    risingThrottleErrorSum += s.ThrottleAbsoluteError;
                    if (s.IsBrakeInTolerance) risingBrakeOkCount++;
                }
                else
                {
                    fallingCount++;
                    fallingThrottleErrorSum += s.ThrottleAbsoluteError;
                    if (s.IsBrakeInTolerance) fallingBrakeOkCount++;
                }
            }

            int n = _sessionSamples.Count;
            result.BrakeInToleranceRatio = (double)brakeOkCount / n * 100.0;
            result.ThrottleInToleranceRatio = (double)throttleOkCount / n * 100.0;
            result.BothInToleranceRatio = (double)bothOkCount / n * 100.0;

            result.RisingMeanError = risingCount > 0 ? risingThrottleErrorSum / risingCount : 0.0;
            result.FallingMeanError = fallingCount > 0 ? fallingThrottleErrorSum / fallingCount : 0.0;

            result.RisingBrakeRatio = risingCount > 0 ? (double)risingBrakeOkCount / risingCount * 100.0 : 0.0;
            result.FallingBrakeRatio = fallingCount > 0 ? (double)fallingBrakeOkCount / fallingCount * 100.0 : 0.0;

            // 位相遅れ推定 (グリッド探索: 1周期の±45%または最大±0.8秒を探索)
            double bestTau = 0.0;
            double minMse = double.MaxValue;
            double maxLag = Math.Min(0.8, Config.CycleDurationSeconds * 0.45);
            double stepTau = Math.Max(0.002, Math.Min(0.01, Config.CycleDurationSeconds / 100.0));

            for (double tau = -maxLag; tau <= maxLag; tau += stepTau)
            {
                double sumSqr = 0;
                int count = 0;

                // 計算軽量化のため最大500点を等間隔サンプリング
                int step = Math.Max(1, _sessionSamples.Count / 500);
                for (int i = 0; i < _sessionSamples.Count; i += step)
                {
                    var s = _sessionSamples[i];
                    double targetAtShift = Config.CalculateTargetThrottle(s.SessionTimeSeconds - tau);
                    double diff = s.ActualThrottle - targetAtShift;
                    sumSqr += diff * diff;
                    count++;
                }

                if (count > 0)
                {
                    double mse = sumSqr / count;
                    if (mse < minMse)
                    {
                        minMse = mse;
                        bestTau = tau;
                    }
                }
            }

            result.PhaseLagSeconds = Math.Round(bestTau, 2);

            // --- スコア算出 (100点満点) ---
            // 1. 波形精度 (40点満点): アクセル追従率 (%) を主軸に評価
            result.WaveformScore = Math.Clamp(result.ThrottleInToleranceRatio * 0.40, 0.0, 40.0);

            // 2. 位相精度 (30点満点): 周期に合わせた許容位相差で評価
            double absLag = Math.Abs(result.PhaseLagSeconds);
            double allowedLag = Math.Min(0.05, Config.CycleDurationSeconds * 0.08);
            double zeroLag = Math.Min(0.50, Config.CycleDurationSeconds * 0.40);
            if (zeroLag <= allowedLag) zeroLag = allowedLag + 0.1;

            if (absLag <= allowedLag)
            {
                result.PhaseScore = 30.0;
            }
            else
            {
                double lagRatio = Math.Clamp((absLag - allowedLag) / (zeroLag - allowedLag), 0.0, 1.0);
                result.PhaseScore = Math.Clamp(30.0 * (1.0 - lagRatio), 0.0, 30.0);
            }

            // 3. ブレーキ安定性 (30点満点): ブレーキ維持率 (%) を主軸に評価
            result.BrakeStabilityScore = Math.Clamp(result.BrakeInToleranceRatio * 0.30, 0.0, 30.0);

            result.TotalScore = Math.Round(result.WaveformScore + result.PhaseScore + result.BrakeStabilityScore, 1);
            return result;
        }
    }
}
