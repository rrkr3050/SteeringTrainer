namespace SteeringTrainer.Infrastructure.Persistence;

public sealed record BrakeHoldHistoryEntry(
    long Id,
    DateTime RecordedAtUtc,
    double TargetBrake,
    double ElapsedSeconds,
    double Score);
