using SteeringTrainer.Core.Models;
using System;
using System.Diagnostics;

namespace SteeringTrainer.Core.Training.HeelToe;

/// <summary>Measures the time from brake entry until the target stays in tolerance for the requested hold.</summary>
public sealed class BrakeHoldTrainingEngine
{
    private bool _running;
    private bool _attemptStarted;
    private bool _waitingForRelease;
    private long _attemptStartTicks;
    private long? _stableStartTicks;

    public double TargetBrake { get; private set; }
    public double Tolerance { get; private set; }
    public double RequiredStableSeconds { get; private set; }
    public double EntryThreshold { get; set; } = 10.0;

    public event Action<string>? StatusChanged;
    public event Action<double>? AttemptCompleted;

    public void Start(double targetBrake, double tolerance, double requiredStableSeconds = 0.5)
    {
        TargetBrake = Math.Clamp(targetBrake, 0, 100);
        Tolerance = Math.Clamp(tolerance, 0.5, 25);
        RequiredStableSeconds = Math.Max(0.1, requiredStableSeconds);
        _running = true;
        _attemptStarted = false;
        _waitingForRelease = false;
        _stableStartTicks = null;
        StatusChanged?.Invoke($"ブレーキ {TargetBrake:F0}% を踏んでください");
    }

    public void Stop()
    {
        _running = false;
        _attemptStarted = false;
        _waitingForRelease = false;
        _stableStartTicks = null;
    }

    public void SetTargetBrake(double targetBrake) => TargetBrake = Math.Clamp(targetBrake, 0, 100);

    public void ProcessSample(SteeringSample sample)
    {
        if (!_running) return;
        var brake = sample.Brake * 100.0;

        if (_waitingForRelease)
        {
            if (brake < EntryThreshold)
            {
                _waitingForRelease = false;
                _attemptStarted = false;
                _stableStartTicks = null;
                StatusChanged?.Invoke($"次の計測: ブレーキ {TargetBrake:F0}% を踏んでください");
            }
            return;
        }

        if (!_attemptStarted)
        {
            if (brake >= EntryThreshold)
            {
                _attemptStarted = true;
                _attemptStartTicks = sample.TimestampTicks;
                StatusChanged?.Invoke("計測中: 目標範囲でブレーキを維持してください");
            }
            return;
        }

        var inRange = Math.Abs(brake - TargetBrake) <= Tolerance;
        if (!inRange)
        {
            _stableStartTicks = null;
            return;
        }

        _stableStartTicks ??= sample.TimestampTicks;
        var stableSeconds = (sample.TimestampTicks - _stableStartTicks.Value) / (double)Stopwatch.Frequency;
        if (stableSeconds < RequiredStableSeconds) return;

        var elapsed = (sample.TimestampTicks - _attemptStartTicks) / (double)Stopwatch.Frequency;
        _waitingForRelease = true;
        AttemptCompleted?.Invoke(elapsed);
        StatusChanged?.Invoke($"安定成功。目標範囲に入るまで {elapsed:F2} 秒。ブレーキを離すと再計測します。");
    }
}
