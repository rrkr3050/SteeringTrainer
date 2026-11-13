using SteeringTrainer.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SteeringTrainer.Core.Training.HeelToe
{
    /// <summary>
    /// ヒール＆トゥ練習モードのステートマシンおよびセッション進行エンジン
    /// </summary>
    public class HeelToeTrainingEngine
    {
        private readonly List<HeelToeSample> _currentAttemptSamples = new();
        private readonly List<HeelToeAttemptResult> _challengeAttempts = new();
        private readonly object _sampleLock = new();

        private Stopwatch _timer = new();
        private double _stabilizingStartTime;
        private double _blipPromptTime;
        private double _blipStartTime;
        private double _blipEndTime;
        private double _recoveryStartTime;

        private double _brakeBeforeBlip;
        private double _minBrakeDuringBlip;
        private double _maxBrakeDuringBlip;
        private double _peakThrottle;

        private int _currentRound = 1;
        private int _attemptsInTargetBlock;
        private bool _isChallengeMode = true;
        private bool _randomizeBrakeEveryFive = true;
        private bool _randomizeThrottleEveryFive = true;
        private bool _attemptCompletionInProgress;
        private long _lastSampleTimestampTicks;

        public HeelToeConfig Config { get; } = new();
        public HeelToeState CurrentState { get; private set; } = HeelToeState.Idle;

        public int CurrentRound => _currentRound;
        public int TargetBlockAttempt => Math.Clamp(_attemptsInTargetBlock + 1, 1, 5);
        public bool IsChallengeMode => _isChallengeMode;

        public event Action<HeelToeState, string>? StateChanged;
        public event Action<HeelToeBeepType>? BeepTriggered;
        public event Action<HeelToeAttemptResult>? AttemptCompleted;
        public event Action<HeelToeChallengeSummary>? ChallengeCompleted;

        public HeelToeTrainingEngine()
        {
        }

        public void StartSession(bool challengeMode = true, bool randomizeBrakeEveryFive = true, bool randomizeThrottleEveryFive = true)
        {
            lock (_sampleLock)
            {
            _isChallengeMode = challengeMode;
            _randomizeBrakeEveryFive = randomizeBrakeEveryFive;
            _randomizeThrottleEveryFive = randomizeThrottleEveryFive;
            _currentRound = 1;
            _attemptsInTargetBlock = 0;
            _attemptCompletionInProgress = false;
            _lastSampleTimestampTicks = 0;
            _challengeAttempts.Clear();
            _timer.Restart();

            if (_randomizeBrakeEveryFive)
            {
                double minB = Math.Min(Config.RandomBrakeMin, Config.RandomBrakeMax);
                double maxB = Math.Max(Config.RandomBrakeMin, Config.RandomBrakeMax);
                Config.TargetBrake = Math.Clamp(Config.TargetBrake, minB, maxB);
            }
            if (_randomizeThrottleEveryFive)
            {
                double minT = Math.Min(Config.RandomThrottleMin, Config.RandomThrottleMax);
                double maxT = Math.Max(Config.RandomThrottleMin, Config.RandomThrottleMax);
                Config.TargetThrottle = Math.Clamp(Config.TargetThrottle, minT, maxT);
            }

            TransitionTo(HeelToeState.Idle, $"目標ブレーキ: {Config.TargetBrake:F0}% を踏んでください");
            }
        }

        public void StopSession()
        {
            lock (_sampleLock)
            {
            _timer.Stop();
            TransitionTo(HeelToeState.Idle, "トレーニング停止中");
            }
        }

        /// <summary>
        /// サンプリングスレッドまたはタイマーから呼び出される更新処理
        /// </summary>
        public void ProcessSample(SteeringSample sample)
        {
            lock (_sampleLock)
            {
                if (sample.TimestampTicks <= _lastSampleTimestampTicks) return;
                _lastSampleTimestampTicks = sample.TimestampTicks;
                ProcessSampleLocked(sample);
            }
        }

        private void ProcessSampleLocked(SteeringSample sample)
        {
            if (!_timer.IsRunning) return;

            double now = _timer.Elapsed.TotalSeconds;
            double brakePercent = sample.Brake * 100.0;
            double throttlePercent = sample.Throttle * 100.0;

            var htSample = new HeelToeSample(now, brakePercent, throttlePercent, CurrentState);
            if (CurrentState != HeelToeState.Idle && CurrentState != HeelToeState.ChallengeCompleted)
            {
                _currentAttemptSamples.Add(htSample);
            }

            switch (CurrentState)
            {
                case HeelToeState.AttemptFinished:
                    // A trial is acknowledged when the throttle has returned to
                    // its zero/noise-floor range; brake may remain pressed.
                    if (throttlePercent <= 3.0)
                    {
                        _attemptCompletionInProgress = false;
                        TransitionTo(HeelToeState.Idle, $"目標ブレーキ: {Config.TargetBrake:F0}% を踏んでください");
                    }
                    break;

                case HeelToeState.Idle:
                    if (brakePercent >= Config.BrakeEntryThreshold)
                    {
                        _currentAttemptSamples.Clear();
                        _currentAttemptSamples.Add(htSample);
                        TransitionTo(HeelToeState.BrakeEntry, $"ブレーキ入力検知 ({brakePercent:F0}%) -> 目標 {Config.TargetBrake:F0}% へ");
                    }
                    break;

                case HeelToeState.BrakeEntry:
                    if (throttlePercent >= 5.0)
                    {
                        FinishAttemptNoBlip(HeelToeFailureReason.EarlyBlip,
                            "ブレーキが安定する前にアクセルを踏んでいます");
                        break;
                    }
                    // 目標範囲に入ったかチェック
                    if (Math.Abs(brakePercent - Config.TargetBrake) <= Config.BrakeTolerance)
                    {
                        _stabilizingStartTime = now;
                        TransitionTo(HeelToeState.BrakeStabilizing, $"安定化中... そのまま維持 ({brakePercent:F0}%)");
                    }
                    else if (brakePercent < Config.BrakeEntryThreshold * 0.5)
                    {
                        // ブレーキを戻してしまった
                        TransitionTo(HeelToeState.Idle, $"目標ブレーキ: {Config.TargetBrake:F0}% を踏んでください");
                    }
                    break;

                case HeelToeState.BrakeStabilizing:
                    if (throttlePercent >= 5.0)
                    {
                        FinishAttemptNoBlip(HeelToeFailureReason.EarlyBlip,
                            "ブレーキが安定する前にアクセルを踏んでいます");
                        break;
                    }
                    if (Math.Abs(brakePercent - Config.TargetBrake) <= Config.BrakeTolerance)
                    {
                        double elapsedMs = (now - _stabilizingStartTime) * 1000.0;
                        if (elapsedMs >= Config.BrakeStabilizeTimeMs)
                        {
                            _blipPromptTime = now;
                            _brakeBeforeBlip = brakePercent;
                            _minBrakeDuringBlip = brakePercent;
                            _maxBrakeDuringBlip = brakePercent;
                            _peakThrottle = 0.0;

                            BeepTriggered?.Invoke(HeelToeBeepType.BlipPrompt);
                            TransitionTo(HeelToeState.BrakeStabilized, "BLIP! (アクセルを踏んでください)");
                        }
                    }
                    else
                    {
                        // ブレーキがブレたため安定化タイマーリセット
                        _stabilizingStartTime = now;
                    }
                    break;

                case HeelToeState.BrakeStabilized:
                    // アクセル踏み込み検知 (5%以上)
                    if (throttlePercent >= 5.0)
                    {
                        _blipStartTime = now;
                        _peakThrottle = throttlePercent;
                        _minBrakeDuringBlip = brakePercent;
                        _maxBrakeDuringBlip = brakePercent;
                        TransitionTo(HeelToeState.ThrottleBlipping, "BLIPPING...");
                    }
                    else
                    {
                        // タイムアウト判定
                        double waitMs = (now - _blipPromptTime) * 1000.0;
                        if (waitMs > Config.BlipTimeoutMs)
                        {
                            FinishAttemptNoBlip(HeelToeFailureReason.LateBlip,
                                "ブレーキ安定後のブリップが遅すぎます（タイムアウト）");
                        }
                    }
                    break;

                case HeelToeState.ThrottleBlipping:
                    // ピーク更新およびブレーキ変動記録
                    if (throttlePercent > _peakThrottle) _peakThrottle = throttlePercent;
                    if (brakePercent < _minBrakeDuringBlip) _minBrakeDuringBlip = brakePercent;
                    if (brakePercent > _maxBrakeDuringBlip) _maxBrakeDuringBlip = brakePercent;

                    // アクセル戻り判定 (3%以下またはピークの15%以下に低下)
                    if (throttlePercent <= Math.Max(3.0, _peakThrottle * 0.15))
                    {
                        _blipEndTime = now;
                        _recoveryStartTime = now;
                        TransitionTo(HeelToeState.BrakeRecovery, "ブレーキ維持確認中...");
                    }
                    break;

                case HeelToeState.BrakeRecovery:
                    double recoveryMs = (now - _recoveryStartTime) * 1000.0;
                    if (recoveryMs >= Config.BrakeRecoveryTimeMs)
                    {
                        EvaluateAttempt(brakePercent);
                    }
                    break;
            }
        }

        private void FinishAttemptNoBlip(HeelToeFailureReason reason, string description)
        {
            var result = new HeelToeAttemptResult
            {
                AttemptNumber = _currentRound,
                TargetBrake = Config.TargetBrake,
                TargetThrottle = Config.TargetThrottle,
                ActualBrakeBeforeBlip = _brakeBeforeBlip,
                ActualPeakThrottle = 0.0,
                TotalScore = 0.0,
                Grade = "MISS",
                PrimaryFeedback = description,
                RecordedSamples = new List<HeelToeSample>(_currentAttemptSamples)
            };
            result.FailureReasons.Add(reason);
            result.FailureDescriptions.Add(description);

            HandleAttemptFinished(result);
        }

        private void EvaluateAttempt(double brakeAfterBlip)
        {
            double reactionTimeMs = (_blipStartTime - _blipPromptTime) * 1000.0;
            double blipDurationMs = (_blipEndTime - _blipStartTime) * 1000.0;

            // ブレーキ安定性解析
            var (brakeScore, brakeFailures, brakeDesc) = BrakeStabilityAnalyzer.Analyze(
                Config.TargetBrake,
                Config.BrakeTolerance,
                _brakeBeforeBlip,
                _minBrakeDuringBlip,
                _maxBrakeDuringBlip,
                brakeAfterBlip);

            // スロットルブリップ解析
            var (amountScore, timingScore, smoothScore, blipFailures, blipDesc) = ThrottleBlipAnalyzer.Analyze(
                Config.TargetThrottle,
                Config.ThrottleTolerance,
                _peakThrottle,
                reactionTimeMs,
                blipDurationMs,
                Config.TargetBlipDurationMs,
                Config.IdealReactionMinMs,
                Config.IdealReactionMaxMs,
                Config.EarlyReactionThresholdMs,
                Config.LateReactionThresholdMs);

            double totalScore = brakeScore + amountScore + timingScore + smoothScore;

            var allFailures = new List<HeelToeFailureReason>();
            allFailures.AddRange(brakeFailures);
            allFailures.AddRange(blipFailures);

            var allDesc = new List<string>();
            allDesc.AddRange(brakeDesc);
            allDesc.AddRange(blipDesc);

            string grade;
            string feedback;
            HeelToeBeepType beep;

            if (totalScore >= 92.0 && allFailures.Count == 0)
            {
                grade = "PERFECT BLIP!";
                feedback = "完璧なヒール＆トゥ！ブレーキが一切乱れていません";
                beep = HeelToeBeepType.Perfect;
            }
            else if (totalScore >= 80.0)
            {
                grade = "GOOD";
                feedback = allDesc.Count > 0 ? allDesc[0] : "良好なブリップ操作です";
                beep = HeelToeBeepType.Good;
            }
            else if (totalScore >= 60.0)
            {
                grade = "OK";
                feedback = allDesc.Count > 0 ? allDesc[0] : "ブレーキを一定に保つ意識を高めましょう";
                beep = HeelToeBeepType.Good;
            }
            else
            {
                grade = "MISS";
                feedback = allDesc.Count > 0 ? allDesc[0] : "操作を見直しましょう";
                beep = HeelToeBeepType.Miss;
            }

            var result = new HeelToeAttemptResult
            {
                AttemptNumber = _currentRound,
                TargetBrake = Config.TargetBrake,
                TargetThrottle = Config.TargetThrottle,
                ActualBrakeBeforeBlip = _brakeBeforeBlip,
                ActualMinBrakeDuringBlip = _minBrakeDuringBlip,
                ActualMaxBrakeDuringBlip = _maxBrakeDuringBlip,
                ActualBrakeAfterBlip = brakeAfterBlip,
                ActualPeakThrottle = _peakThrottle,
                BlipDurationMs = blipDurationMs,
                BlipReactionTimeMs = reactionTimeMs,
                BrakeStabilityScore = brakeScore,
                BlipAmountScore = amountScore,
                BlipTimingScore = timingScore,
                BlipSmoothnessScore = smoothScore,
                TotalScore = totalScore,
                Grade = grade,
                PrimaryFeedback = feedback,
                FailureReasons = allFailures,
                FailureDescriptions = allDesc,
                RecordedSamples = new List<HeelToeSample>(_currentAttemptSamples)
            };

            BeepTriggered?.Invoke(beep);
            HandleAttemptFinished(result);
        }

        private void HandleAttemptFinished(HeelToeAttemptResult result)
        {
            // BrakeRecovery receives samples continuously. Latch completion before
            // publishing events so overlapping/re-entrant samples cannot count one
            // physical blip more than once.
            if (_attemptCompletionInProgress) return;
            _attemptCompletionInProgress = true;
            TransitionTo(HeelToeState.AttemptFinished, "試行結果を記録中...");

            _challengeAttempts.Add(result);
            AttemptCompleted?.Invoke(result);

            if (_isChallengeMode)
            {
                if (_currentRound >= Config.ChallengeRounds)
                {
                    // チャレンジ終了
                    var summary = CreateChallengeSummary();
                    TransitionTo(HeelToeState.ChallengeCompleted, $"10回チャレンジ完了！ 平均スコア: {summary.AverageScore:F1}点");
                    ChallengeCompleted?.Invoke(summary);
                    return;
                }
            }

            _currentRound++;
            _attemptsInTargetBlock++;
            if (_attemptsInTargetBlock >= 5)
            {
                ApplyRandomTargetPair();
                _attemptsInTargetBlock = 0;
            }

            TransitionTo(HeelToeState.AttemptFinished,
                $"{result.Grade} ({result.TotalScore:F0}点) - アクセルを戻すと回数を確定します");
        }

        public void ApplyRandomTargetPair()
        {
            double brake = Config.TargetBrake;
            if (_randomizeBrakeEveryFive)
            {
                int minBrakeStep = (int)Math.Round(Math.Clamp(Config.RandomBrakeMin, 5, 95) / 5.0);
                int maxBrakeStep = (int)Math.Round(Math.Clamp(Config.RandomBrakeMax, 5, 95) / 5.0);
                if (minBrakeStep > maxBrakeStep) (minBrakeStep, maxBrakeStep) = (maxBrakeStep, minBrakeStep);

                brake = minBrakeStep == maxBrakeStep
                    ? minBrakeStep * 5.0
                    : Random.Shared.Next(minBrakeStep, maxBrakeStep + 1) * 5.0;
            }

            double throttle = Config.TargetThrottle;
            if (_randomizeThrottleEveryFive)
            {
                int minThrottleStep = (int)Math.Round(Math.Clamp(Config.RandomThrottleMin, 5, 100) / 5.0);
                int maxThrottleStep = (int)Math.Round(Math.Clamp(Config.RandomThrottleMax, 5, 100) / 5.0);
                if (minThrottleStep > maxThrottleStep) (minThrottleStep, maxThrottleStep) = (maxThrottleStep, minThrottleStep);

                throttle = minThrottleStep == maxThrottleStep
                    ? minThrottleStep * 5.0
                    : Random.Shared.Next(minThrottleStep, maxThrottleStep + 1) * 5.0;
            }

            // 直前と完全に同一ペアかつ選択肢がある場合は微調整して変化をつける
            if (_randomizeBrakeEveryFive && _randomizeThrottleEveryFive && brake == Config.TargetBrake && throttle == Config.TargetThrottle)
            {
                int minThrottleStep = (int)Math.Round(Math.Clamp(Config.RandomThrottleMin, 5, 100) / 5.0);
                int maxThrottleStep = (int)Math.Round(Math.Clamp(Config.RandomThrottleMax, 5, 100) / 5.0);
                if (maxThrottleStep > minThrottleStep)
                {
                    throttle = throttle >= maxThrottleStep * 5.0 ? throttle - 5.0 : throttle + 5.0;
                }
                else
                {
                    int minBrakeStep = (int)Math.Round(Math.Clamp(Config.RandomBrakeMin, 5, 95) / 5.0);
                    int maxBrakeStep = (int)Math.Round(Math.Clamp(Config.RandomBrakeMax, 5, 95) / 5.0);
                    if (maxBrakeStep > minBrakeStep)
                    {
                        brake = brake >= maxBrakeStep * 5.0 ? brake - 5.0 : brake + 5.0;
                    }
                }
            }

            Config.TargetBrake = brake;
            Config.TargetThrottle = throttle;
        }

        private HeelToeChallengeSummary CreateChallengeSummary()
        {
            if (_challengeAttempts.Count == 0) return new HeelToeChallengeSummary();

            double total = 0, best = 0, brakeAvg = 0, timingAvg = 0, amountAvg = 0;
            int perfects = 0, misses = 0;

            foreach (var a in _challengeAttempts)
            {
                total += a.TotalScore;
                if (a.TotalScore > best) best = a.TotalScore;
                brakeAvg += a.BrakeStabilityScore;
                timingAvg += a.BlipTimingScore;
                amountAvg += a.BlipAmountScore;
                if (a.IsPerfect) perfects++;
                if (a.Grade == "MISS") misses++;
            }

            int n = _challengeAttempts.Count;
            return new HeelToeChallengeSummary
            {
                TotalAttempts = n,
                AverageScore = total / n,
                BestScore = best,
                BrakeStabilityAverage = brakeAvg / n,
                BlipTimingAverage = timingAvg / n,
                BlipAmountAverage = amountAvg / n,
                PerfectCount = perfects,
                MissCount = misses,
                Attempts = new List<HeelToeAttemptResult>(_challengeAttempts)
            };
        }

        private void TransitionTo(HeelToeState newState, string message)
        {
            CurrentState = newState;
            StateChanged?.Invoke(newState, message);
        }
    }
}
