namespace SteeringTrainer.App.ViewModels;

public sealed record BrakeTargetAverage(double TargetBrake, int Attempts, double AverageScore, double AverageBrakeStability)
{
    public string TargetText => $"{TargetBrake:F0}%";
    public string AverageScoreText => $"{AverageScore:F1}";
    public string AverageBrakeStabilityText => $"{AverageBrakeStability:F1}/50";
}
