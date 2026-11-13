namespace SteeringTrainer.Core.Training.HeelToe
{
    public enum HeelToeDifficulty
    {
        Beginner,
        Normal,
        Advanced,
        Custom
    }

    public enum HeelToeState
    {
        Idle,
        BrakeEntry,
        BrakeStabilizing,
        BrakeStabilized,   // BLIP! プロンプト発動
        ThrottleBlipping,  // アクセル踏み込み中
        BrakeRecovery,     // アクセル戻り後のブレーキ維持確認
        AttemptFinished,   // 1試行終了・結果判定
        ChallengeCompleted // 10回チャレンジ完了
    }

    public enum HeelToeFailureReason
    {
        None,
        BrakeDrop,     // アクセルを踏んだ瞬間にブレーキが抜けている
        BrakeIncrease, // アクセル操作中にブレーキが強くなっている
        EarlyBlip,     // ブレーキが安定する前にアクセルを踏んでいる
        LateBlip,      // ブリップが遅れている
        UnderBlip,     // アクセル量が不足している
        OverBlip,      // アクセルを踏みすぎている
        LongBlip,      // アクセルを長く踏みすぎている
        NoBlip         // アクセルが検出されなかった
    }

    public enum HeelToeBeepType
    {
        None,
        BrakeStable,   // ブレーキ安定合図
        BlipPrompt,    // BLIP! トリガー合図
        Perfect,       // パーフェクト合図（高音）
        Good,          // 良好合図
        Miss           // 失敗合図（低音）
    }

    public record HeelToeSample(
        double TimeSeconds,
        double BrakePercent,
        double ThrottlePercent,
        HeelToeState State
    );
}
