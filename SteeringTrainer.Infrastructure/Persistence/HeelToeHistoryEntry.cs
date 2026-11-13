namespace SteeringTrainer.Infrastructure.Persistence;

public sealed record HeelToeHistoryEntry(
    long Id,
    DateTime RecordedAtUtc,
    double TargetBrake,
    double TargetThrottle,
    double Score,
    double BrakeStabilityScore,
    double TimingScore,
    double AmountScore,
    string Grade,
    bool IsChallenge = true);
