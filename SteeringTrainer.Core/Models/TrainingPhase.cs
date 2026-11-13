namespace SteeringTrainer.Core.Models
{
    /// <summary>
    /// 練習フェーズ
    /// </summary>
    public enum TrainingPhase
    {
        Idle,
        Center,
        AccelRight,
        ConstantRight,
        DecelRight,
        HandoverRight,
        AccelLeft,
        ConstantLeft,
        DecelLeft,
        HandoverLeft,
        Finished
    }

    /// <summary>
    /// リアルタイム評価結果
    /// </summary>
    public enum RealTimeVerdict
    {
        None,
        Good,
        Warning,
        Miss
    }
}
