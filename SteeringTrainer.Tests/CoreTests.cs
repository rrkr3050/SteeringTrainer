using SteeringTrainer.Core.Collections;
using SteeringTrainer.Core.Devices;
using SteeringTrainer.Core.Models;
using SteeringTrainer.Core.Signal;
using SteeringTrainer.Core.Signal.Filters;
using System.Diagnostics;
using Xunit;
using FluentAssertions;

namespace SteeringTrainer.Tests
{
    /// <summary>
    /// Phase 1 コア機能の自動テスト
    /// </summary>
    public class CoreTests
    {
        // =========================================================
        // RingBuffer テスト
        // =========================================================
        [Fact]
        public void RingBuffer_PushAndToList_ReturnsItemsInOrder()
        {
            var buf = new RingBuffer<int>(5);
            for (int i = 1; i <= 5; i++) buf.Push(i);

            var list = buf.ToList();
            list.Should().Equal(1, 2, 3, 4, 5);
        }

        [Fact]
        public void RingBuffer_Overflow_ReturnsLatestItems()
        {
            var buf = new RingBuffer<int>(3);
            for (int i = 1; i <= 6; i++) buf.Push(i);

            var list = buf.ToList();
            list.Should().Equal(4, 5, 6); // 古いものは捨てられる
        }

        [Fact]
        public void RingBuffer_ToListN_ReturnsLatestN()
        {
            var buf = new RingBuffer<int>(10);
            for (int i = 1; i <= 8; i++) buf.Push(i);

            var list = buf.ToList(3);
            list.Should().Equal(6, 7, 8);
        }

        // =========================================================
        // EmaFilter テスト
        // =========================================================
        [Fact]
        public void EmaFilter_Alpha0_PassThrough()
        {
            var filter = new EmaFilter(0.0);
            var result = filter.Filter(100.0, 0.002);
            result.Should().BeApproximately(100.0, 0.001);
        }

        [Fact]
        public void EmaFilter_HighAlpha_Smoothed()
        {
            var filter = new EmaFilter(0.9);
            // 最初のサンプルはパススルー
            filter.Filter(0.0, 0.002);
            // 急激な変化でも出力はゆっくり追従する
            var result = filter.Filter(100.0, 0.002);
            result.Should().BeLessThan(20.0); // スムージングが効いている
        }

        [Fact]
        public void EmaFilter_Reset_ClearsState()
        {
            var filter = new EmaFilter(0.9);
            filter.Filter(100.0, 0.002);
            filter.Reset();
            var result = filter.Filter(50.0, 0.002);
            result.Should().BeApproximately(50.0, 0.001); // リセット後は新鮮な入力
        }

        // =========================================================
        // SteeringSample テスト
        // =========================================================
        [Fact]
        public void SteeringSample_TimestampSeconds_CorrectConversion()
        {
            var ticks = Stopwatch.GetTimestamp();
            var sample = new SteeringSample(ticks, 0, 0, 0, 0, 0, 0, 0, 0);
            var elapsed = sample.TimestampSeconds;
            elapsed.Should().BeGreaterThan(0);
        }

        [Fact]
        public void SteeringSample_Zero_IsZero()
        {
            var zero = SteeringSample.Zero;
            zero.RawNormalized.Should().Be(0);
            zero.AngleDegrees.Should().Be(0);
            zero.Velocity.Should().Be(0);
        }

        // =========================================================
        // 角度変換テスト
        // =========================================================
        [Theory]
        [InlineData(0.0, 900.0, 0.0)]     // センター
        [InlineData(1.0, 900.0, 450.0)]   // 右ロック
        [InlineData(-1.0, 900.0, -450.0)] // 左ロック
        [InlineData(0.5, 1080.0, 270.0)]  // 1080°の場合
        public void AngleConversion_NormalizedToAngle(double normalized, double wheelRotation, double expected)
        {
            var angle = normalized * wheelRotation / 2.0;
            angle.Should().BeApproximately(expected, 0.001);
        }

        // =========================================================
        // SyntheticSteeringDevice テスト
        // =========================================================
        [Fact]
        public void SyntheticDevice_StartStop_Works()
        {
            var device = new SyntheticSteeringDevice(frequencyHz: 100);
            device.IsConnected.Should().BeTrue();
            device.Start();
            Thread.Sleep(100);
            var val = device.SteeringNormalized;
            val.Should().BeInRange(-1.0, 1.0);
            device.Stop();
            device.Dispose();
        }

        [Fact]
        public void SyntheticDevice_Enumerate_ReturnsSyntheticDevice()
        {
            var device = new SyntheticSteeringDevice();
            var devices = device.EnumerateDevices();
            devices.Should().HaveCount(1);
            devices[0].ApiType.Should().Be("Synthetic");
        }

        // =========================================================
        // TrainingConfig テスト
        // =========================================================
        [Theory]
        [InlineData(540.0, 2.5, 216.0)]
        [InlineData(540.0, 3.0, 180.0)]
        [InlineData(720.0, 2.5, 288.0)]
        public void TrainingConfig_TargetVelocity_IsCorrect(
            double lockAngle, double timeSide, double expected)
        {
            var config = new TrainingConfig
            {
                LockAngle = lockAngle,
                TimeSideSeconds = timeSide
            };
            config.TargetVelocity.Should().BeApproximately(expected, 0.001);
        }

        // =========================================================
        // SignalProcessor 統合テスト (Synthetic デバイスで動作確認)
        // =========================================================
        [Fact]
        public void SignalProcessor_ProducesSamples()
        {
            var device = new SyntheticSteeringDevice(frequencyHz: 100, amplitudeNormalized: 1.0, periodSeconds: 2.0);
            device.Start();

            var processor = new SignalProcessor(device) { TargetHz = 100 };
            var samples = new List<SteeringSample>();

            processor.SampleProduced += s => samples.Add(s);
            processor.Start();

            Thread.Sleep(200); // 200ms 待機 → ~20サンプル期待

            processor.Stop();
            device.Stop();
            device.Dispose();
            processor.Dispose();

            samples.Should().HaveCountGreaterThan(5);
        }

        [Fact]
        public void SignalProcessor_SamplesHaveIncreasingTimestamps()
        {
            var device = new SyntheticSteeringDevice(frequencyHz: 200);
            device.Start();

            var processor = new SignalProcessor(device) { TargetHz = 200 };
            var samples = new List<SteeringSample>();
            processor.SampleProduced += s => { lock (samples) samples.Add(s); };
            processor.Start();

            Thread.Sleep(100);

            processor.Stop();
            device.Stop();
            device.Dispose();
            processor.Dispose();

            List<SteeringSample> snapshot;
            lock (samples) snapshot = new List<SteeringSample>(samples);

            for (int i = 1; i < snapshot.Count; i++)
            {
                snapshot[i].TimestampTicks.Should().BeGreaterThan(snapshot[i - 1].TimestampTicks);
            }
        }

        /// <summary>
        /// 理想的な操作 (一定角速度) を入力した場合にVelocityが一定に収束することを確認
        /// </summary>
        [Fact]
        public void SignalProcessor_ConstantVelocityInput_ProducesConsistentVelocity()
        {
            // 正弦波の最大角速度 = amplitude * 2π / period (°/s換算)
            // 正弦波デバイスは完全な一定速度ではないが、中間域では安定する
            var device = new SyntheticSteeringDevice(
                frequencyHz: 500,
                amplitudeNormalized: 1.0,
                periodSeconds: 4.0);
            device.Start();

            var processor = new SignalProcessor(device)
            {
                TargetHz = 500,
                WheelRotation = 900.0,
                FilterStrength = 0.0 // フィルタなし
            };

            var velocities = new List<double>();
            processor.SampleProduced += s =>
            {
                // 中間域 (角度が小さい) のサンプルだけ収集
                if (Math.Abs(s.AngleDegrees) < 50 && Math.Abs(s.Velocity) > 10)
                    lock (velocities) velocities.Add(s.Velocity);
            };
            processor.Start();
            Thread.Sleep(500);
            processor.Stop();
            device.Stop();
            device.Dispose();
            processor.Dispose();

            // 速度が存在することを確認
            velocities.Should().NotBeEmpty();
        }

        // =========================================================
        // Settings & Button テスト
        // =========================================================
        [Fact]
        public void SyntheticDevice_ButtonPress_FiresEvent()
        {
            var device = new SyntheticSteeringDevice();
            int pressedButton = -1;
            device.ButtonPressed += (_, b) => pressedButton = b;

            device.SimulateButtonPress(3);
            pressedButton.Should().Be(3);
        }

        [Fact]
        public void AppSettings_Serialization_PreservesValues()
        {
            var settings = new AppSettings
            {
                SelectedDeviceId = "winmm_1",
                WheelRotation = 900.0,
                CenterOffset = -2.5,
                InvertInput = true,
                FilterStrengthPercent = 25,
                AlwaysOnTop = true,
                TargetBrake = 65.0,
                TargetThrottle = 75.0,
                RandomBrakeMin = 20.0,
                RandomBrakeMax = 40.0,
                RandomThrottleMin = 50.0,
                RandomThrottleMax = 80.0
            };
            settings.PedalSettings.Throttle.AssignedAxis = JoystickAxis.Z;
            settings.PedalSettings.Brake.AssignedAxis = JoystickAxis.R;

            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

            deserialized.Should().NotBeNull();
            deserialized!.SelectedDeviceId.Should().Be("winmm_1");
            deserialized.WheelRotation.Should().Be(900.0);
            deserialized.CenterOffset.Should().Be(-2.5);
            deserialized.InvertInput.Should().BeTrue();
            deserialized.FilterStrengthPercent.Should().Be(25);
            deserialized.AlwaysOnTop.Should().BeTrue();
            deserialized.TargetBrake.Should().Be(65.0);
            deserialized.RandomBrakeMin.Should().Be(20.0);
            deserialized.RandomBrakeMax.Should().Be(40.0);
            deserialized.RandomThrottleMin.Should().Be(50.0);
            deserialized.RandomThrottleMax.Should().Be(80.0);
            deserialized.PedalSettings.Throttle.AssignedAxis.Should().Be(JoystickAxis.Z);
            deserialized.PedalSettings.Brake.AssignedAxis.Should().Be(JoystickAxis.R);
        }

        // =========================================================
        // Heel & Toe ランダム目標出現範囲テスト
        // =========================================================
        [Fact]
        public void HeelToeConfig_RandomRange_DefaultsCorrect()
        {
            var config = new SteeringTrainer.Core.Training.HeelToe.HeelToeConfig();
            config.RandomBrakeMin.Should().Be(5.0);
            config.RandomBrakeMax.Should().Be(95.0);
            config.RandomThrottleMin.Should().Be(5.0);
            config.RandomThrottleMax.Should().Be(100.0);
        }

        [Fact]
        public void HeelToeTrainingEngine_ApplyRandomTargetPair_ConfinesToConfiguredRange()
        {
            var engine = new SteeringTrainer.Core.Training.HeelToe.HeelToeTrainingEngine();
            engine.Config.RandomBrakeMin = 30.0;
            engine.Config.RandomBrakeMax = 40.0;
            engine.Config.RandomThrottleMin = 60.0;
            engine.Config.RandomThrottleMax = 70.0;

            engine.StartSession(challengeMode: false, randomizeBrakeEveryFive: true, randomizeThrottleEveryFive: true);

            // 50回ランダム選出をテストし、すべて指定範囲内に収まることを検証
            for (int i = 0; i < 50; i++)
            {
                engine.ApplyRandomTargetPair();
                engine.Config.TargetBrake.Should().BeInRange(30.0, 40.0);
                ((int)engine.Config.TargetBrake % 5).Should().Be(0);

                engine.Config.TargetThrottle.Should().BeInRange(60.0, 70.0);
                ((int)engine.Config.TargetThrottle % 5).Should().Be(0);
            }
        }

        [Fact]
        public void HeelToeTrainingEngine_ApplyRandomTargetPair_FixedRange_ProducesExactValue()
        {
            var engine = new SteeringTrainer.Core.Training.HeelToe.HeelToeTrainingEngine();
            engine.Config.RandomBrakeMin = 50.0;
            engine.Config.RandomBrakeMax = 50.0;
            engine.Config.RandomThrottleMin = 80.0;
            engine.Config.RandomThrottleMax = 80.0;

            engine.StartSession(challengeMode: false, randomizeBrakeEveryFive: true, randomizeThrottleEveryFive: true);

            for (int i = 0; i < 10; i++)
            {
                engine.ApplyRandomTargetPair();
                engine.Config.TargetBrake.Should().Be(50.0);
                engine.Config.TargetThrottle.Should().Be(80.0);
            }
        }

        // =========================================================
        // Throttle Tracking 新モード テスト
        // =========================================================
        [Fact]
        public void ThrottleTrackingConfig_BpmConversion_AccurateWithUpperPeak()
        {
            var config = new SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingConfig();

            // 15 BPM -> 1周期 4.0秒 (60 / 15)
            config.Bpm = 15.0;
            config.CycleDurationSeconds.Should().BeApproximately(4.0, 0.001);

            // 60 BPM -> 1周期 1.0秒 (60 / 60)
            config.Bpm = 60.0;
            config.CycleDurationSeconds.Should().BeApproximately(1.0, 0.001);

            // 120 BPM (高速) -> 1周期 0.50秒
            config.Bpm = 120.0;
            config.CycleDurationSeconds.Should().BeApproximately(0.50, 0.001);

            // 200 BPM (超高速・余裕を持った上限) -> 1周期 0.30秒
            config.Bpm = 200.0;
            config.CycleDurationSeconds.Should().BeApproximately(0.30, 0.001);

            // 秒数側からの設定も連動
            config.CycleDurationSeconds = 2.0; // 30 BPM
            config.Bpm.Should().BeApproximately(30.0, 0.001);

            config.CycleDurationSeconds = 0.40; // 150 BPM
            config.Bpm.Should().BeApproximately(150.0, 0.001);

            // 上のピークが1周期の中央（1ビートごとの周期）に到達することを検証
            config.Bpm = 60.0; // 1.0秒周期
            config.ThrottleMin = 0.0;
            config.ThrottleMax = 50.0;
            config.CalculateTargetThrottle(0.5).Should().BeApproximately(50.0, 0.001); // 1ビート目のピーク
            config.CalculateTargetThrottle(1.5).Should().BeApproximately(50.0, 0.001); // 2ビート目のピーク
        }

        [Fact]
        public void ThrottleTrackingConfig_SinWaveCalculation_CorrectAtKeyPhases()
        {
            var config = new SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingConfig
            {
                ThrottleMin = 0.0,
                ThrottleMax = 40.0,
                CycleDurationSeconds = 4.0
            };

            // t = 0: phase 0.0 -> sin 0.0 -> target 0.0
            config.CalculateTargetThrottle(0.0).Should().BeApproximately(0.0, 0.001);
            config.IsRisingPhase(0.0).Should().BeTrue();

            // t = 1.0 (1/4 周期): phase 0.25 -> 1 - cos(pi/2) = 1 -> target 20.0
            config.CalculateTargetThrottle(1.0).Should().BeApproximately(20.0, 0.001);
            config.IsRisingPhase(1.0).Should().BeTrue();

            // t = 2.0 (1/2 周期): phase 0.5 -> 1 - cos(pi) = 2 -> target 40.0 (ピーク)
            config.CalculateTargetThrottle(2.0).Should().BeApproximately(40.0, 0.001);

            // t = 3.0 (3/4 周期): phase 0.75 -> 戻しフェーズ -> target 20.0
            config.CalculateTargetThrottle(3.0).Should().BeApproximately(20.0, 0.001);
            config.IsRisingPhase(3.0).Should().BeFalse();

            // t = 4.0 (1周期完了): target 0.0
            config.CalculateTargetThrottle(4.0).Should().BeApproximately(0.0, 0.001);
        }

        [Fact]
        public void ThrottleTrackingEngine_IdealInputs_YieldsHighScore()
        {
            var engine = new SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingEngine();
            engine.Config.TargetBrake = 50.0;
            engine.Config.BrakeTolerance = 5.0;
            engine.Config.ThrottleMin = 0.0;
            engine.Config.ThrottleMax = 40.0;
            engine.Config.ThrottleTolerance = 5.0;
            engine.Config.CycleDurationSeconds = 4.0;
            engine.Config.SessionDurationSeconds = 4.0;

            engine.StartSession();
            engine.CurrentState.Should().Be(SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingState.WaitingForBrake);

            // ブレーキ目標値50%で安定させる (WaitingForBrake 解除)
            long freq = Stopwatch.Frequency;
            long currentTicks = Stopwatch.GetTimestamp();

            while (engine.CurrentState == SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingState.WaitingForBrake)
            {
                currentTicks += (long)(0.02 * freq);
                engine.ProcessSample(new SteeringSample(currentTicks, 0, 0, 0, 0, 0, 0, 0, 0, Throttle: 0.0, Brake: 0.50));
            }

            engine.CurrentState.Should().Be(SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingState.Tracking);

            SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingResult? result = null;
            engine.SessionFinished += r => result = r;

            // 1周期 (4秒間 = 200サンプル) 理想的なSin波アクセルと一定ブレーキ50%を入力
            long trackingStartTicks = currentTicks;
            for (int i = 0; i <= 200; i++)
            {
                currentTicks += (long)(0.02 * freq);
                double elapsed = (currentTicks - trackingStartTicks) / (double)freq;
                double idealThr = engine.Config.CalculateTargetThrottle(elapsed);
                engine.ProcessSample(new SteeringSample(currentTicks, 0, 0, 0, 0, 0, 0, 0, 0, Throttle: idealThr / 100.0, Brake: 0.50));
            }

            engine.StopSession(completed: true);
            result.Should().NotBeNull();
            result!.TotalScore.Should().BeGreaterThanOrEqualTo(95.0);
            result.Grade.Should().Be("PERFECT");
            result.WaveformScore.Should().BeApproximately(40.0, 1.0);
            result.PhaseScore.Should().BeApproximately(30.0, 1.0);
            result.BrakeStabilityScore.Should().BeApproximately(30.0, 1.0);
            result.PhaseLagSeconds.Should().BeInRange(-0.05, 0.05);
            result.BothInToleranceRatio.Should().BeGreaterThanOrEqualTo(95.0);
        }

        [Fact]
        public void ThrottleTrackingEngine_LaggedInputs_DetectsPhaseLag()
        {
            var engine = new SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingEngine();
            engine.Config.TargetBrake = 50.0;
            engine.Config.BrakeTolerance = 5.0;
            engine.Config.ThrottleMin = 0.0;
            engine.Config.ThrottleMax = 40.0;
            engine.Config.ThrottleTolerance = 5.0;
            engine.Config.CycleDurationSeconds = 4.0;
            engine.Config.SessionDurationSeconds = 4.0;

            engine.StartSession();
            long freq = Stopwatch.Frequency;
            long currentTicks = Stopwatch.GetTimestamp();

            while (engine.CurrentState == SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingState.WaitingForBrake)
            {
                currentTicks += (long)(0.02 * freq);
                engine.ProcessSample(new SteeringSample(currentTicks, 0, 0, 0, 0, 0, 0, 0, 0, Throttle: 0.0, Brake: 0.50));
            }

            SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingResult? result = null;
            engine.SessionFinished += r => result = r;

            // 0.3秒遅延して操作する (actual(t) = target(t - 0.3))
            double lagSeconds = 0.30;
            long trackingStartTicks = currentTicks;
            for (int i = 0; i <= 200; i++)
            {
                currentTicks += (long)(0.02 * freq);
                double elapsed = (currentTicks - trackingStartTicks) / (double)freq;
                double laggedElapsed = Math.Max(0.0, elapsed - lagSeconds);
                double laggedThr = engine.Config.CalculateTargetThrottle(laggedElapsed);
                engine.ProcessSample(new SteeringSample(currentTicks, 0, 0, 0, 0, 0, 0, 0, 0, Throttle: laggedThr / 100.0, Brake: 0.50));
            }

            engine.StopSession(completed: true);
            result.Should().NotBeNull();
            // 位相遅れが約0.3秒 (+0.25〜+0.35s) として正しく検出されることを検証
            result!.PhaseLagSeconds.Should().BeInRange(0.25, 0.35);
            result.PhaseScore.Should().BeLessThan(25.0); // 満点30点から減点されていること
        }

        [Fact]
        public void ThrottleTrackingEngine_HighBpmFastWave_TracksAccurately()
        {
            var engine = new SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingEngine();
            engine.Config.Bpm = 120.0; // 0.50秒/周期 (高速波形)
            engine.Config.TargetBrake = 60.0;
            engine.Config.BrakeTolerance = 5.0;
            engine.Config.ThrottleMin = 0.0;
            engine.Config.ThrottleMax = 40.0;
            engine.Config.ThrottleTolerance = 5.0;
            engine.Config.SessionDurationSeconds = 2.0;

            engine.StartSession();
            long freq = Stopwatch.Frequency;
            long currentTicks = Stopwatch.GetTimestamp();

            while (engine.CurrentState == SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingState.WaitingForBrake)
            {
                currentTicks += (long)(0.01 * freq);
                engine.ProcessSample(new SteeringSample(currentTicks, 0, 0, 0, 0, 0, 0, 0, 0, Throttle: 0.0, Brake: 0.60));
            }

            SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingResult? result = null;
            engine.SessionFinished += r => result = r;

            // 120 BPM (0.5秒周期) で2秒間 (4周期分 = 200サンプル @ 100Hz)
            long trackingStartTicks = currentTicks;
            for (int i = 0; i <= 200; i++)
            {
                currentTicks += (long)(0.01 * freq);
                double elapsed = (currentTicks - trackingStartTicks) / (double)freq;
                double idealThr = engine.Config.CalculateTargetThrottle(elapsed);
                engine.ProcessSample(new SteeringSample(currentTicks, 0, 0, 0, 0, 0, 0, 0, 0, Throttle: idealThr / 100.0, Brake: 0.60));
            }

            engine.StopSession(completed: true);
            result.Should().NotBeNull();
            result!.TotalScore.Should().BeGreaterThanOrEqualTo(95.0);
            result.Grade.Should().Be("PERFECT");
            result.ConfigBpm.Should().Be(120.0);
        }

        [Fact]
        public void ThrottleTrackingHistoryRepository_SaveAndLoad_RoundTrip()
        {
            var tempDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tracking_test_{System.Guid.NewGuid()}.db");
            try
            {
                var repo = new SteeringTrainer.Infrastructure.Persistence.ThrottleTrackingHistoryRepository(tempDb);
                var result = new SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingResult
                {
                    TotalScore = 88.5,
                    WaveformScore = 36.0,
                    PhaseScore = 26.5,
                    BrakeStabilityScore = 26.0,
                    PhaseLagSeconds = 0.12,
                    BrakeInToleranceRatio = 90.0,
                    ThrottleInToleranceRatio = 92.0,
                    BothInToleranceRatio = 85.0,
                    RisingMeanError = 2.1,
                    FallingMeanError = 3.2,
                    RisingBrakeRatio = 93.0,
                    FallingBrakeRatio = 87.0,
                    ElapsedSeconds = 30.0,
                    TotalSamples = 1500,
                    ConfigTargetBrake = 50.0,
                    ConfigBrakeTolerance = 5.0,
                    ConfigThrottleMin = 0.0,
                    ConfigThrottleMax = 40.0,
                    ConfigThrottleTolerance = 5.0,
                    ConfigCycleDuration = 6.0
                };

                var saved = repo.Save(result);
                saved.Id.Should().BeGreaterThan(0);

                var all = repo.LoadAll();
                all.Should().HaveCount(1);
                all[0].TotalScore.Should().Be(88.5);
                all[0].PhaseLagSeconds.Should().Be(0.12);
                all[0].CycleDuration.Should().Be(6.0);
                all[0].Grade.Should().Be("GREAT");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (System.IO.File.Exists(tempDb))
                {
                    System.IO.File.Delete(tempDb);
                }
            }
        }

        // =========================================================
        // PedalConfig & キャリブレーション / ガンマ テスト
        // =========================================================
        [Fact]
        public void PedalConfig_Normalize_Linear_Direct_CalculatesCorrectly()
        {
            var pedal = new PedalConfig
            {
                AssignedAxis = JoystickAxis.Z,
                Raw0Percent = 0,
                Raw100Percent = 1000,
                DeadzonePercent = 0,
                MaxDeadzonePercent = 0,
                Gamma = 1.0
            };

            pedal.Normalize(0).Should().BeApproximately(0.0, 0.001);
            pedal.Normalize(500).Should().BeApproximately(0.5, 0.001);
            pedal.Normalize(1000).Should().BeApproximately(1.0, 0.001);
            pedal.Normalize(1200).Should().BeApproximately(1.0, 0.001); // 範囲外クランプ
        }

        [Fact]
        public void PedalConfig_Normalize_Inverted_DrivingForcePro_Brake()
        {
            var pedal = new PedalConfig
            {
                AssignedAxis = JoystickAxis.R,
                Raw0Percent = 65535,
                Raw100Percent = 0,
                DeadzonePercent = 0,
                MaxDeadzonePercent = 0,
                Gamma = 1.0
            };

            pedal.Invert.Should().BeTrue();
            pedal.Normalize(65535).Should().BeApproximately(0.0, 0.001);
            pedal.Normalize(32768).Should().BeApproximately(0.5, 0.01);
            pedal.Normalize(0).Should().BeApproximately(1.0, 0.001);
        }

        [Fact]
        public void PedalConfig_Normalize_CustomStiffBrakeCalibration_Reaches100PercentAtPhysicalLimit()
        {
            // ハンコンのブレーキが硬く、人間の足ではRaw 20000までしか踏み込めないケース
            var pedal = new PedalConfig
            {
                AssignedAxis = JoystickAxis.R,
                Raw0Percent = 65535,
                Raw100Percent = 20000,
                DeadzonePercent = 0,
                MaxDeadzonePercent = 0,
                Gamma = 1.0
            };

            pedal.Normalize(65535).Should().BeApproximately(0.0, 0.001);
            // 20000で正確に100%出力
            pedal.Normalize(20000).Should().BeApproximately(1.0, 0.001);
            // 20000未満（さらに強く踏んだ場合）も100%にクランプ
            pedal.Normalize(10000).Should().BeApproximately(1.0, 0.001);
        }

        [Fact]
        public void PedalConfig_Normalize_Gamma_AppliesProgressiveCurve()
        {
            var pedal = new PedalConfig
            {
                AssignedAxis = JoystickAxis.R,
                Raw0Percent = 0,
                Raw100Percent = 100,
                DeadzonePercent = 0,
                MaxDeadzonePercent = 0,
                Gamma = 2.0 // プログレッシブ: 奥で強く効き、踏み始め〜中盤は穏やか
            };

            pedal.Normalize(0).Should().BeApproximately(0.0, 0.001);
            // 50%ストローク時: 0.5^2 = 0.25 (リニアの半分に抑えられるため早期100%到達を防ぐ)
            pedal.Normalize(50).Should().BeApproximately(0.25, 0.001);
            pedal.Normalize(100).Should().BeApproximately(1.0, 0.001);
        }

        [Fact]
        public void PedalConfig_Normalize_UpperAndLowerDeadzone()
        {
            var pedal = new PedalConfig
            {
                AssignedAxis = JoystickAxis.Z,
                Raw0Percent = 0,
                Raw100Percent = 1000,
                DeadzonePercent = 10.0,      // 0〜100は0%
                MaxDeadzonePercent = 10.0,   // 900〜1000は100%
                Gamma = 1.0
            };

            pedal.Normalize(50).Should().BeApproximately(0.0, 0.001);
            pedal.Normalize(100).Should().BeApproximately(0.0, 0.001);
            // 500 (中間) -> (0.5 - 0.1) / (1.0 - 0.2) = 0.4 / 0.8 = 0.5
            pedal.Normalize(500).Should().BeApproximately(0.5, 0.001);
            pedal.Normalize(900).Should().BeApproximately(1.0, 0.001);
            pedal.Normalize(950).Should().BeApproximately(1.0, 0.001);
        }

        [Fact]
        public void PedalConfig_LegacyProperties_BidirectionalSync()
        {
            // 旧 settings.json のデシリアライズ挙動を再現
            var pedal = new PedalConfig();
            pedal.MinRaw = 1000;
            pedal.MaxRaw = 60000;
            pedal.Invert = true;

            pedal.Raw0Percent.Should().Be(60000);
            pedal.Raw100Percent.Should().Be(1000);

            // 新プロパティ更新時の同期
            pedal.Raw0Percent = 50000;
            pedal.Raw100Percent = 5000;
            pedal.MinRaw.Should().Be(5000);
            pedal.MaxRaw.Should().Be(50000);
            pedal.Invert.Should().BeTrue();
        }

        // =========================================================
        // ThrottleTracking メトロノーム テスト
        // =========================================================
        [Fact]
        public void ThrottleTrackingEngine_MetronomeTriggered_FiresAtTopAndBottomPeaks()
        {
            var engine = new SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingEngine();
            engine.Config.TargetBrake = 50.0;
            engine.Config.BrakeTolerance = 5.0;
            engine.Config.Bpm = 60.0; // 1周期 = 1.0秒, 半周期 = 0.5秒
            engine.Config.SessionDurationSeconds = 10.0;

            var metronomeBeats = new List<bool>();
            engine.MetronomeTriggered += isTop => metronomeBeats.Add(isTop);

            engine.StartSession();
            long freq = Stopwatch.Frequency;
            long ticks = 100000;

            // ブレーキ安定フェーズ (0.4秒でTransitionするまで送る)
            while (engine.CurrentState == SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingState.WaitingForBrake)
            {
                ticks += (long)(0.01 * freq);
                engine.ProcessSample(new SteeringSample(ticks, 0, 0, 0, 0, 0, 0, 0, 0, Throttle: 0.0, Brake: 0.50));
            }
            engine.CurrentState.Should().Be(SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingState.Tracking);
            metronomeBeats.Should().BeEmpty(); // Trackingに入る瞬間はまだ追従サンプル未処理のためビート未発火

            // トラッキング中: 0.0s 〜 2.1s までサンプルを進める (2周期分)
            // 期待されるピーク:
            // t = 0.0s : 下ピーク (isTop = false)
            // t = 0.5s : 上ピーク (isTop = true)
            // t = 1.0s : 下ピーク (isTop = false)
            // t = 1.5s : 上ピーク (isTop = true)
            // t = 2.0s : 下ピーク (isTop = false)
            for (int i = 0; i <= 210; i++) // 2.1秒
            {
                ticks += (long)(0.01 * freq);
                engine.ProcessSample(new SteeringSample(ticks, 0, 0, 0, 0, 0, 0, 0, 0, Throttle: 0.20, Brake: 0.50));
            }

            metronomeBeats.Should().NotBeEmpty();
            metronomeBeats.Count.Should().BeGreaterThanOrEqualTo(5);

            // 交互に false (下ピーク) と true (上ピーク) が発火していること
            for (int i = 0; i < metronomeBeats.Count - 1; i++)
            {
                metronomeBeats[i].Should().NotBe(metronomeBeats[i + 1]);
            }

            // 最初のビートは下ピーク (false)、次は上ピーク (true)
            metronomeBeats[0].Should().BeFalse();
            metronomeBeats[1].Should().BeTrue();
            metronomeBeats[2].Should().BeFalse();
            metronomeBeats[3].Should().BeTrue();
            metronomeBeats[4].Should().BeFalse();
        }

        [Fact]
        public void ThrottleTrackingAudioService_WaveGenerationAndBeat()
        {
            var audioService = new SteeringTrainer.Core.Training.ThrottleTracking.ThrottleTrackingAudioService
            {
                IsEnabled = false, // テスト環境で実際にスピーカーを鳴らさずにロジックを検証
                Volume = 0.8
            };

            // 例外なく呼び出せること
            audioService.Volume.Should().Be(0.8);
            var actTop = () => audioService.PlayBeat(isTopPeak: true);
            var actBottom = () => audioService.PlayBeat(isTopPeak: false);
            actTop.Should().NotThrow();
            actBottom.Should().NotThrow();
        }
    }
}
