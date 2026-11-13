namespace SteeringTrainer.Core.Training.HeelToe;

/// <summary>Vehicle ratios belong to the vehicle, independently of the selected engine.</summary>
public sealed record RevMatchVehicle(double FinalDrive, double TireCircumferenceMeters, double[] GearRatios)
{
    public double Rpm(double speedKph, int gear) => speedKph / 3.6 / TireCircumferenceMeters * 60 * FinalDrive * GearRatios[gear - 1];
    public static RevMatchVehicle MarchNismoS => new(4.066, 1.884, new[] { 3.727, 2.047, 1.392, 1.029, 0.820 });
}

public sealed record RevMatchConfig(RevMatchVehicle Vehicle, double SpeedKph, int CurrentGear, int TargetGear,
    double TargetBrake, double BrakeTolerance, double RpmTolerance, double ConnectDelayMs,
    double StableMs = 250, double BlipTimeoutMs = 2500, bool AutoBlipTrigger = false)
{
    public double InitialRpm => Vehicle.Rpm(SpeedKph, CurrentGear);
    public double TargetRpm => Vehicle.Rpm(SpeedKph, TargetGear);
    public void Validate(double engineLimit)
    {
        if (!double.IsFinite(SpeedKph) || SpeedKph <= 0 || !double.IsFinite(Vehicle.FinalDrive) || Vehicle.FinalDrive <= 0
            || !double.IsFinite(Vehicle.TireCircumferenceMeters) || Vehicle.TireCircumferenceMeters <= 0
            || Vehicle.GearRatios.Length == 0 || Vehicle.GearRatios.Any(r => !double.IsFinite(r) || r <= 0))
            throw new ArgumentException("車速・ファイナル・タイヤ周長・ギヤ比は正の有限値を指定してください");
        if (CurrentGear < 1 || CurrentGear > Vehicle.GearRatios.Length || TargetGear < 1 || TargetGear > Vehicle.GearRatios.Length
            || (TargetGear >= CurrentGear && !(AutoBlipTrigger && CurrentGear == 1 && TargetGear == 1)))
            throw new ArgumentException("現在ギヤより低いシフト先（ダウンシフト）を指定してください");
        if (InitialRpm < 800 || TargetRpm + RpmTolerance >= engineLimit)
            throw new ArgumentException($"開始RPMは800以上、目標RPM＋許容幅は上限{engineLimit:F0}未満にしてください");
        if (!double.IsFinite(TargetBrake) || TargetBrake < 10 || TargetBrake > 95 || !double.IsFinite(BrakeTolerance) || BrakeTolerance < 1 || BrakeTolerance > 15
            || !double.IsFinite(RpmTolerance) || RpmTolerance < 25 || RpmTolerance > 1000
            || !double.IsFinite(ConnectDelayMs) || ConnectDelayMs < 0 || ConnectDelayMs > 1000)
            throw new ArgumentException("ブレーキ目標10〜95%、許容1〜15%、RPM許容25〜1000、接続待ち0〜1000msで指定してください");
    }
}

public enum RevMatchPhase { BrakeStabilizing, AwaitBlip, Blipping, AwaitConnection, Finished }
public sealed record RevMatchResult(double SimulationSeconds, long SimulationTick, long PedalTimestamp,
    double AssessmentRpm, double TargetRpm, double RpmError, double BrakeScore, double RpmScore, double TotalScore,
    double PeakRpm, double MinBrake, double MaxBrake, string Reason);

/// <summary>Runs only on the fixed simulation worker. Latches completion before firing any event.</summary>
public sealed class RevMatchTrainingEngine
{
    private readonly RevMatchConfig _config;
    private double? _stableSince;
    private double _promptTime, _connectTime, _minimumBrake = 100, _maximumBrake, _peakRpm, _lastBrake;
    private long _lastTick;
    public RevMatchPhase Phase { get; private set; } = RevMatchPhase.BrakeStabilizing;
    public event Action? BlipPrompt;
    public event Action<RevMatchResult>? Completed;
    public RevMatchTrainingEngine(RevMatchConfig config) => _config = config;

    public void Process(long tick, double seconds, long pedalTimestamp, double throttle, double brake, double rpm)
    {
        if (tick <= _lastTick || Phase == RevMatchPhase.Finished) return;
        _lastTick = tick;
        var percent = brake * 100;
        _lastBrake = percent;
        _peakRpm = Math.Max(_peakRpm, rpm);
        if (Phase == RevMatchPhase.BrakeStabilizing)
        {
            if (_config.AutoBlipTrigger)
            {
                if (_config.CurrentGear == 1 || throttle < .05) return;
                _minimumBrake = _maximumBrake = percent;
                _promptTime = seconds;
                Phase = RevMatchPhase.Blipping;
                BlipPrompt?.Invoke();
                return;
            }
            if (throttle >= .05) { _minimumBrake = _maximumBrake = percent; Finish(tick, seconds, pedalTimestamp, rpm, "合図前のアクセル", true); return; }
            if (Math.Abs(percent - _config.TargetBrake) > _config.BrakeTolerance) { _stableSince = null; return; }
            _stableSince ??= seconds;
            if (seconds - _stableSince.Value + 1e-9 < _config.StableMs / 1000) return;
            _minimumBrake = _maximumBrake = percent;
            _promptTime = seconds;
            Phase = RevMatchPhase.AwaitBlip;
            BlipPrompt?.Invoke();
            return;
        }
        _minimumBrake = Math.Min(_minimumBrake, percent);
        _maximumBrake = Math.Max(_maximumBrake, percent);
        if (Phase == RevMatchPhase.AwaitBlip)
        {
            if (seconds - _promptTime > _config.BlipTimeoutMs / 1000) { Finish(tick, seconds, pedalTimestamp, rpm, "BLIPタイムアウト", true); return; }
            if (throttle >= .05) Phase = RevMatchPhase.Blipping;
        }
        else if (Phase == RevMatchPhase.Blipping)
        {
            if (seconds - _promptTime > 5) { Finish(tick, seconds, pedalTimestamp, rpm, "アクセルを戻してください（5秒超過）", true); return; }
            if (throttle <= .03)
            {
                _connectTime = seconds + _config.ConnectDelayMs / 1000;
                Phase = RevMatchPhase.AwaitConnection;
            }
        }
        if (Phase == RevMatchPhase.AwaitConnection && seconds + 1e-9 >= _connectTime)
            Finish(tick, seconds, pedalTimestamp, rpm, "仮想クラッチ接続", false);
    }

    private void Finish(long tick, double seconds, long timestamp, double rpm, string reason, bool failed)
    {
        Phase = RevMatchPhase.Finished; // latch before callback / native reconnection
        var error = rpm - _config.TargetRpm;
        var rpmScore = failed ? 0 : Math.Clamp(50 - Math.Max(0, Math.Abs(error) - _config.RpmTolerance) * 50 / (_config.RpmTolerance * 3), 0, 50);
        var (brakeScore, _, _) = BrakeStabilityAnalyzer.Analyze(_config.TargetBrake, _config.BrakeTolerance,
            _config.TargetBrake, _minimumBrake, _maximumBrake, _lastBrake);
        if (failed) brakeScore = 0;
        Completed?.Invoke(new(seconds, tick, timestamp, rpm, _config.TargetRpm, error, brakeScore, rpmScore,
            brakeScore + rpmScore, _peakRpm, _minimumBrake, _maximumBrake, reason));
    }
}
