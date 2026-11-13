using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using SteeringTrainer.Core.Collections;
using SteeringTrainer.Core.Models;
using SteeringTrainer.Core.Signal;
using SteeringTrainer.Core.Training.ThrottleTracking;
using SteeringTrainer.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SteeringTrainer.App.ViewModels
{
    public class ThrottleTrackingViewModel : ViewModelBase
    {
        private SignalProcessor? _processor;
        private readonly ThrottleTrackingEngine _engine;
        private readonly ThrottleTrackingAudioService _audioService = new();
        private readonly ThrottleTrackingHistoryRepository _historyRepository = new();
        private readonly RingBuffer<ThrottleTrackingSample> _recentSamples = new(3000); // 最大約6秒分
        private readonly DispatcherTimer _uiTimer;
        private SteeringSample? _latestSample;

        // --- OxyPlot モデル ---
        public PlotModel ThrottlePlotModel { get; }
        private readonly LineSeries _throttleTargetSeries;
        private readonly LineSeries _throttleUpperBandSeries;
        private readonly LineSeries _throttleLowerBandSeries;
        private readonly LineSeries _throttleActualSeries;
        private readonly LineSeries _throttleNowCursorSeries;

        public PlotModel BrakePlotModel { get; }
        private readonly LineSeries _brakeTargetSeries;
        private readonly LineSeries _brakeUpperBandSeries;
        private readonly LineSeries _brakeLowerBandSeries;
        private readonly LineSeries _brakeActualSeries;
        private readonly LineSeries _brakeNowCursorSeries;

        // --- 設定プロパティ ---
        public ThrottleTrackingConfig Config => _engine.Config;

        public double TargetBrake
        {
            get => Config.TargetBrake;
            set
            {
                var v = Math.Clamp(Math.Round(value / 5.0) * 5.0, 5, 95);
                if (Math.Abs(Config.TargetBrake - v) > 0.01)
                {
                    Config.TargetBrake = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TargetBrakeText));
                }
            }
        }
        public string TargetBrakeText => $"{TargetBrake:F0}%";

        public double BrakeTolerance
        {
            get => Config.BrakeTolerance;
            set
            {
                var v = Math.Clamp(Math.Round(value), 1, 20);
                if (Math.Abs(Config.BrakeTolerance - v) > 0.01)
                {
                    Config.BrakeTolerance = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BrakeToleranceText));
                }
            }
        }
        public string BrakeToleranceText => $"±{BrakeTolerance:F0}%";

        public double ThrottleMin
        {
            get => Config.ThrottleMin;
            set
            {
                var v = Math.Clamp(Math.Round(value / 5.0) * 5.0, 0, 90);
                if (Math.Abs(Config.ThrottleMin - v) > 0.01)
                {
                    Config.ThrottleMin = v;
                    if (Config.ThrottleMax < v + 5.0)
                    {
                        Config.ThrottleMax = Math.Min(100.0, v + 5.0);
                        OnPropertyChanged(nameof(ThrottleMax));
                        OnPropertyChanged(nameof(ThrottleMaxText));
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThrottleMinText));
                }
            }
        }
        public string ThrottleMinText => $"{ThrottleMin:F0}%";

        public double ThrottleMax
        {
            get => Config.ThrottleMax;
            set
            {
                var v = Math.Clamp(Math.Round(value / 5.0) * 5.0, 10, 100);
                if (Math.Abs(Config.ThrottleMax - v) > 0.01)
                {
                    Config.ThrottleMax = v;
                    if (Config.ThrottleMin > v - 5.0)
                    {
                        Config.ThrottleMin = Math.Max(0.0, v - 5.0);
                        OnPropertyChanged(nameof(ThrottleMin));
                        OnPropertyChanged(nameof(ThrottleMinText));
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThrottleMaxText));
                }
            }
        }
        public string ThrottleMaxText => $"{ThrottleMax:F0}%";

        public double ThrottleTolerance
        {
            get => Config.ThrottleTolerance;
            set
            {
                var v = Math.Clamp(Math.Round(value), 1, 20);
                if (Math.Abs(Config.ThrottleTolerance - v) > 0.01)
                {
                    Config.ThrottleTolerance = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThrottleToleranceText));
                }
            }
        }
        public string ThrottleToleranceText => $"±{ThrottleTolerance:F0}%";

        public double Bpm
        {
            get => Config.Bpm;
            set
            {
                var v = Math.Clamp(Math.Round(value), 4.0, 200.0);
                if (Math.Abs(Config.Bpm - v) > 0.01)
                {
                    Config.Bpm = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BpmText));
                    OnPropertyChanged(nameof(CycleDurationSeconds));
                    OnPropertyChanged(nameof(CycleDurationText));
                }
            }
        }
        public string BpmText => $"{Bpm:F0} BPM";
        public string CycleDurationText => $"{Bpm:F0} BPM (1周期: {Config.CycleDurationSeconds:F2}秒)";

        public double CycleDurationSeconds
        {
            get => Config.CycleDurationSeconds;
            set => Bpm = value > 0 ? 60.0 / value : 15.0;
        }

        public double SessionDurationSeconds
        {
            get => Config.SessionDurationSeconds;
            set
            {
                var v = Math.Clamp(Math.Round(value / 10.0) * 10.0, 10, 180);
                if (Math.Abs(Config.SessionDurationSeconds - v) > 0.01)
                {
                    Config.SessionDurationSeconds = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SessionDurationText));
                }
            }
        }
        public string SessionDurationText => $"{SessionDurationSeconds:F0} 秒";

        // --- メトロノーム設定 ---
        public bool EnableMetronome
        {
            get => _audioService.IsEnabled;
            set
            {
                if (_audioService.IsEnabled != value)
                {
                    _audioService.IsEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public double MetronomeVolumePercent
        {
            get => _audioService.Volume * 100.0;
            set
            {
                var v = Math.Clamp(Math.Round(value), 0, 100);
                if (Math.Abs(_audioService.Volume * 100.0 - v) > 0.01)
                {
                    _audioService.Volume = v / 100.0;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MetronomeVolumeText));
                }
            }
        }
        public string MetronomeVolumeText => $"{MetronomeVolumePercent:F0}%";

        // --- リアルタイム値 ---
        private double _currentBrakePercent;
        public double CurrentBrakePercent
        {
            get => _currentBrakePercent;
            private set => Set(ref _currentBrakePercent, value);
        }
        public string CurrentBrakeText => $"{CurrentBrakePercent:F1}%";

        private double _currentThrottlePercent;
        public double CurrentThrottlePercent
        {
            get => _currentThrottlePercent;
            private set => Set(ref _currentThrottlePercent, value);
        }
        public string CurrentThrottleText => $"{CurrentThrottlePercent:F1}%";

        private double _targetThrottlePercent;
        public double TargetThrottlePercent
        {
            get => _targetThrottlePercent;
            private set => Set(ref _targetThrottlePercent, value);
        }
        public string CurrentTargetThrottleText => $"{TargetThrottlePercent:F1}%";

        // --- 状態 ---
        public bool IsRunning => _engine.CurrentState == ThrottleTrackingState.WaitingForBrake ||
                                 _engine.CurrentState == ThrottleTrackingState.Tracking;

        public bool CanEditSettings => !IsRunning;

        private string _statusTitle = "待機中";
        public string StatusTitle
        {
            get => _statusTitle;
            private set => Set(ref _statusTitle, value);
        }

        private string _statusSubText = "Spaceキーまたは「開始」ボタンでスタート";
        public string StatusSubText
        {
            get => _statusSubText;
            private set => Set(ref _statusSubText, value);
        }

        private Brush _statusBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        public Brush StatusBrush
        {
            get => _statusBrush;
            private set => Set(ref _statusBrush, value);
        }

        private string _timeRemainingText = "-- s";
        public string TimeRemainingText
        {
            get => _timeRemainingText;
            private set => Set(ref _timeRemainingText, value);
        }

        // --- 評価結果プロパティ ---
        private bool _hasResult;
        public bool HasResult
        {
            get => _hasResult;
            private set => Set(ref _hasResult, value);
        }

        private string _scoreText = "-- / 100";
        public string ScoreText
        {
            get => _scoreText;
            private set => Set(ref _scoreText, value);
        }

        private string _gradeText = "---";
        public string GradeText
        {
            get => _gradeText;
            private set => Set(ref _gradeText, value);
        }

        private Brush _gradeBrush = Brushes.White;
        public Brush GradeBrush
        {
            get => _gradeBrush;
            private set => Set(ref _gradeBrush, value);
        }

        private string _waveformScoreText = "-- / 40";
        public string WaveformScoreText
        {
            get => _waveformScoreText;
            private set => Set(ref _waveformScoreText, value);
        }

        private string _phaseScoreText = "-- / 30";
        public string PhaseScoreText
        {
            get => _phaseScoreText;
            private set => Set(ref _phaseScoreText, value);
        }

        private string _phaseLagText = "遅延: -- ms";
        public string PhaseLagText
        {
            get => _phaseLagText;
            private set => Set(ref _phaseLagText, value);
        }

        private string _brakeStabilityScoreText = "-- / 30";
        public string BrakeStabilityScoreText
        {
            get => _brakeStabilityScoreText;
            private set => Set(ref _brakeStabilityScoreText, value);
        }

        private string _throttleRatioText = "-- %";
        public string ThrottleRatioText
        {
            get => _throttleRatioText;
            private set => Set(ref _throttleRatioText, value);
        }

        private string _brakeRatioText = "-- %";
        public string BrakeRatioText
        {
            get => _brakeRatioText;
            private set => Set(ref _brakeRatioText, value);
        }

        private string _bothRatioText = "-- %";
        public string BothRatioText
        {
            get => _bothRatioText;
            private set => Set(ref _bothRatioText, value);
        }

        private string _risingVsFallingErrorText = "踏み込み: -- %  |  戻し: -- %";
        public string RisingVsFallingErrorText
        {
            get => _risingVsFallingErrorText;
            private set => Set(ref _risingVsFallingErrorText, value);
        }

        private string _risingVsFallingBrakeText = "踏み込み時: -- %  |  戻し時: -- %";
        public string RisingVsFallingBrakeText
        {
            get => _risingVsFallingBrakeText;
            private set => Set(ref _risingVsFallingBrakeText, value);
        }

        // --- コマンド ---
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }

        public ThrottleTrackingViewModel()
        {
            _engine = new ThrottleTrackingEngine();
            _engine.StateChanged += OnEngineStateChanged;
            _engine.SampleRecorded += OnSampleRecorded;
            _engine.SessionFinished += OnSessionFinished;
            _engine.MetronomeTriggered += isTop => _audioService.PlayBeat(isTop);

            StartCommand = new RelayCommand(Start, () => !IsRunning && _processor != null);
            StopCommand = new RelayCommand(() => Stop(), () => IsRunning);

            // プロット初期化
            ThrottlePlotModel = CreateBasePlot("アクセル波形追従 (目標Sin波・許容帯・現在値・2秒先読み)");
            _throttleUpperBandSeries = new LineSeries { Title = "許容上限 (+tol)", Color = OxyColor.FromArgb(90, 255, 193, 7), LineStyle = LineStyle.Dash, StrokeThickness = 1.2 };
            _throttleLowerBandSeries = new LineSeries { Title = "許容下限 (-tol)", Color = OxyColor.FromArgb(90, 255, 193, 7), LineStyle = LineStyle.Dash, StrokeThickness = 1.2 };
            _throttleTargetSeries = new LineSeries { Title = "目標アクセル (Sin波)", Color = OxyColor.FromRgb(0xFF, 0xC1, 0x07), StrokeThickness = 2.0 };
            _throttleActualSeries = new LineSeries { Title = "実アクセル操作", Color = OxyColor.FromRgb(0x4C, 0xAF, 0x50), StrokeThickness = 2.8 };
            _throttleNowCursorSeries = new LineSeries { Title = "現在 (NOW)", Color = OxyColor.FromRgb(0x00, 0xBC, 0xD4), StrokeThickness = 1.5, LineStyle = LineStyle.Dot };

            ThrottlePlotModel.Series.Add(_throttleUpperBandSeries);
            ThrottlePlotModel.Series.Add(_throttleLowerBandSeries);
            ThrottlePlotModel.Series.Add(_throttleTargetSeries);
            ThrottlePlotModel.Series.Add(_throttleActualSeries);
            ThrottlePlotModel.Series.Add(_throttleNowCursorSeries);

            BrakePlotModel = CreateBasePlot("ブレーキ一定維持 (目標値・許容帯・実踏力)");
            _brakeUpperBandSeries = new LineSeries { Title = "許容上限 (+tol)", Color = OxyColor.FromArgb(90, 244, 67, 54), LineStyle = LineStyle.Dash, StrokeThickness = 1.2 };
            _brakeLowerBandSeries = new LineSeries { Title = "許容下限 (-tol)", Color = OxyColor.FromArgb(90, 244, 67, 54), LineStyle = LineStyle.Dash, StrokeThickness = 1.2 };
            _brakeTargetSeries = new LineSeries { Title = "目標ブレーキ", Color = OxyColor.FromRgb(0xFF, 0x98, 0x00), LineStyle = LineStyle.Dash, StrokeThickness = 1.8 };
            _brakeActualSeries = new LineSeries { Title = "実ブレーキ踏力", Color = OxyColor.FromRgb(0xF4, 0x43, 0x36), StrokeThickness = 2.8 };
            _brakeNowCursorSeries = new LineSeries { Title = "現在 (NOW)", Color = OxyColor.FromRgb(0x00, 0xBC, 0xD4), StrokeThickness = 1.5, LineStyle = LineStyle.Dot };

            BrakePlotModel.Series.Add(_brakeUpperBandSeries);
            BrakePlotModel.Series.Add(_brakeLowerBandSeries);
            BrakePlotModel.Series.Add(_brakeTargetSeries);
            BrakePlotModel.Series.Add(_brakeActualSeries);
            BrakePlotModel.Series.Add(_brakeNowCursorSeries);

            _uiTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _uiTimer.Tick += OnUiTick;
            _uiTimer.Start();
        }

        public void SetSignalProcessor(SignalProcessor processor)
        {
            if (_processor != null)
            {
                _processor.SampleProduced -= OnSampleProduced;
            }

            _processor = processor;
            _processor.SampleProduced += OnSampleProduced;
            CommandManager.InvalidateRequerySuggested();
        }

        public void ApplySettings(AppSettings settings)
        {
            TargetBrake = settings.TrackingTargetBrake;
            BrakeTolerance = settings.TrackingBrakeTolerance;
            ThrottleMin = settings.TrackingThrottleMin;
            ThrottleMax = settings.TrackingThrottleMax;
            ThrottleTolerance = settings.TrackingThrottleTolerance;
            if (settings.TrackingBpm >= 4.0 && settings.TrackingBpm <= 200.0)
            {
                Bpm = settings.TrackingBpm;
            }
            else if (settings.TrackingCycleDuration > 0)
            {
                CycleDurationSeconds = settings.TrackingCycleDuration;
            }
            SessionDurationSeconds = settings.TrackingSessionDuration;
            EnableMetronome = settings.TrackingEnableMetronome;
            if (settings.TrackingMetronomeVolumePercent > 0)
            {
                MetronomeVolumePercent = settings.TrackingMetronomeVolumePercent;
            }
        }

        public void WriteToSettings(AppSettings settings)
        {
            settings.TrackingTargetBrake = TargetBrake;
            settings.TrackingBrakeTolerance = BrakeTolerance;
            settings.TrackingThrottleMin = ThrottleMin;
            settings.TrackingThrottleMax = ThrottleMax;
            settings.TrackingThrottleTolerance = ThrottleTolerance;
            settings.TrackingBpm = Bpm;
            settings.TrackingCycleDuration = Config.CycleDurationSeconds;
            settings.TrackingSessionDuration = SessionDurationSeconds;
            settings.TrackingEnableMetronome = EnableMetronome;
            settings.TrackingMetronomeVolumePercent = (int)Math.Round(MetronomeVolumePercent);
        }

        public void ToggleStartStop()
        {
            if (IsRunning)
            {
                Stop();
            }
            else
            {
                Start();
            }
        }

        public void Start()
        {
            if (_processor == null || IsRunning) return;
            _recentSamples.Clear();
            HasResult = false;
            _engine.StartSession();
            UpdateStateProperties();
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _engine.StopSession(completed: false);
            UpdateStateProperties();
        }

        private void OnSampleProduced(SteeringSample sample)
        {
            _latestSample = sample;
            _engine.ProcessSample(sample);
        }

        private void OnSampleRecorded(ThrottleTrackingSample sample)
        {
            _recentSamples.Push(sample);
        }

        private void OnEngineStateChanged(ThrottleTrackingState state, string message)
        {
            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                UpdateStateProperties();
            });
        }

        private void OnSessionFinished(ThrottleTrackingResult result)
        {
            // SQLiteへ非同期保存
            try
            {
                _historyRepository.Save(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving throttle tracking history: {ex.Message}");
            }

            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                HasResult = true;
                ScoreText = $"{result.TotalScore:F1} / 100";
                GradeText = result.Grade;
                GradeBrush = result.Grade switch
                {
                    "PERFECT" => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                    "GREAT" => new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),
                    "GOOD" => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
                    _ => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36))
                };

                WaveformScoreText = $"{result.WaveformScore:F1} / 40 点";
                PhaseScoreText = $"{result.PhaseScore:F1} / 30 点";
                string lagSign = result.PhaseLagSeconds >= 0 ? "+" : "";
                PhaseLagText = $"位相差: {lagSign}{result.PhaseLagSeconds:F2} 秒 ({(result.PhaseLagSeconds >= 0 ? "遅れ" : "進み")})";
                BrakeStabilityScoreText = $"{result.BrakeStabilityScore:F1} / 30 点";

                ThrottleRatioText = $"{result.ThrottleInToleranceRatio:F1} %";
                BrakeRatioText = $"{result.BrakeInToleranceRatio:F1} %";
                BothRatioText = $"{result.BothInToleranceRatio:F1} %";

                RisingVsFallingErrorText = $"踏み込み: {result.RisingMeanError:F1}%  |  戻し: {result.FallingMeanError:F1}%";
                RisingVsFallingBrakeText = $"踏み込み時: {result.RisingBrakeRatio:F1}%  |  戻し時: {result.FallingBrakeRatio:F1}%";

                UpdateStateProperties();
            });
        }

        private void UpdateStateProperties()
        {
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(CanEditSettings));

            switch (_engine.CurrentState)
            {
                case ThrottleTrackingState.Idle:
                    StatusTitle = "待機中";
                    StatusSubText = "Spaceキーまたは「開始」ボタンでスタート";
                    StatusBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
                    TimeRemainingText = $"{SessionDurationSeconds:F0} s";
                    break;
                case ThrottleTrackingState.WaitingForBrake:
                    StatusTitle = "ブレーキ準備中";
                    StatusSubText = $"ブレーキを目標 {Config.TargetBrake:F0}% (±{Config.BrakeTolerance:F0}%) で約0.4秒安定させてください";
                    StatusBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
                    TimeRemainingText = $"{SessionDurationSeconds:F0} s";
                    break;
                case ThrottleTrackingState.Tracking:
                    StatusTitle = "波形追従トレーニング中";
                    StatusSubText = "目標Sin波に合わせてアクセルを滑らかに操作してください";
                    StatusBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                    TimeRemainingText = $"{_engine.RemainingSeconds:F1} s";
                    break;
                case ThrottleTrackingState.Finished:
                    StatusTitle = "セッション完了";
                    StatusSubText = "結果サマリーを確認してください。再挑戦はSpaceまたは開始ボタン。";
                    StatusBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4));
                    TimeRemainingText = "0.0 s";
                    break;
            }
        }

        private void OnUiTick(object? sender, EventArgs e)
        {
            if (_latestSample != null)
            {
                CurrentBrakePercent = _latestSample.Brake * 100.0;
                CurrentThrottlePercent = _latestSample.Throttle * 100.0;
                OnPropertyChanged(nameof(CurrentBrakeText));
                OnPropertyChanged(nameof(CurrentThrottleText));
            }

            double nowTime = _engine.CurrentSessionSeconds;
            if (_engine.CurrentState == ThrottleTrackingState.Tracking)
            {
                TargetThrottlePercent = Config.CalculateTargetThrottle(nowTime);
                OnPropertyChanged(nameof(CurrentTargetThrottleText));
                TimeRemainingText = $"{_engine.RemainingSeconds:F1} s";
            }
            else
            {
                TargetThrottlePercent = Config.ThrottleMin;
                OnPropertyChanged(nameof(CurrentTargetThrottleText));
            }

            UpdatePlots(nowTime);
        }

        private void UpdatePlots(double nowTime)
        {
            // 時間ウィンドウ: [nowTime - 3.0, nowTime + 2.0] (過去3秒、未来2秒先読み)
            double tMin = nowTime - 3.0;
            double tMax = nowTime + 2.0;

            // X軸更新
            foreach (var axis in ThrottlePlotModel.Axes)
            {
                if (axis.Position == AxisPosition.Bottom)
                {
                    axis.Minimum = tMin;
                    axis.Maximum = tMax;
                }
            }
            foreach (var axis in BrakePlotModel.Axes)
            {
                if (axis.Position == AxisPosition.Bottom)
                {
                    axis.Minimum = tMin;
                    axis.Maximum = tMax;
                }
            }

            // --- アクセル波形更新 ---
            _throttleTargetSeries.Points.Clear();
            _throttleUpperBandSeries.Points.Clear();
            _throttleLowerBandSeries.Points.Clear();

            int previewSteps = 250;
            double dt = (tMax - tMin) / previewSteps;
            for (int i = 0; i <= previewSteps; i++)
            {
                double t = tMin + i * dt;
                double tgt = Config.CalculateTargetThrottle(t);
                _throttleTargetSeries.Points.Add(new DataPoint(t, tgt));
                _throttleUpperBandSeries.Points.Add(new DataPoint(t, Math.Min(100.0, tgt + Config.ThrottleTolerance)));
                _throttleLowerBandSeries.Points.Add(new DataPoint(t, Math.Max(0.0, tgt - Config.ThrottleTolerance)));
            }

            // 実アクセル操作
            _throttleActualSeries.Points.Clear();
            var samples = _recentSamples.ToList();
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                if (s.SessionTimeSeconds >= tMin - 0.5 && s.SessionTimeSeconds <= nowTime)
                {
                    _throttleActualSeries.Points.Add(new DataPoint(s.SessionTimeSeconds, s.ActualThrottle));
                }
            }

            // 現在位置カーソル
            _throttleNowCursorSeries.Points.Clear();
            _throttleNowCursorSeries.Points.Add(new DataPoint(nowTime, 0));
            _throttleNowCursorSeries.Points.Add(new DataPoint(nowTime, 100));

            // --- ブレーキ波形更新 ---
            _brakeTargetSeries.Points.Clear();
            _brakeUpperBandSeries.Points.Clear();
            _brakeLowerBandSeries.Points.Clear();

            double bTgt = Config.TargetBrake;
            double bUpper = Math.Min(100.0, bTgt + Config.BrakeTolerance);
            double bLower = Math.Max(0.0, bTgt - Config.BrakeTolerance);

            _brakeTargetSeries.Points.Add(new DataPoint(tMin, bTgt));
            _brakeTargetSeries.Points.Add(new DataPoint(tMax, bTgt));

            _brakeUpperBandSeries.Points.Add(new DataPoint(tMin, bUpper));
            _brakeUpperBandSeries.Points.Add(new DataPoint(tMax, bUpper));

            _brakeLowerBandSeries.Points.Add(new DataPoint(tMin, bLower));
            _brakeLowerBandSeries.Points.Add(new DataPoint(tMax, bLower));

            _brakeActualSeries.Points.Clear();
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                if (s.SessionTimeSeconds >= tMin - 0.5 && s.SessionTimeSeconds <= nowTime)
                {
                    _brakeActualSeries.Points.Add(new DataPoint(s.SessionTimeSeconds, s.ActualBrake));
                }
            }

            _brakeNowCursorSeries.Points.Clear();
            _brakeNowCursorSeries.Points.Add(new DataPoint(nowTime, 0));
            _brakeNowCursorSeries.Points.Add(new DataPoint(nowTime, 100));

            ThrottlePlotModel.InvalidatePlot(false);
            BrakePlotModel.InvalidatePlot(false);
        }

        private static PlotModel CreateBasePlot(string title)
        {
            var model = new PlotModel
            {
                Background = OxyColor.FromRgb(0x16, 0x21, 0x3E),
                PlotAreaBackground = OxyColor.FromRgb(0x1A, 0x1A, 0x2E),
                TextColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                Title = title,
                TitleFontSize = 13,
                TitleFontWeight = OxyPlot.FontWeights.Bold,
                TitleColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0)
            };

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Time (s)",
                AxislineColor = OxyColor.FromRgb(0x0F, 0x34, 0x60),
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(0x0F, 0x34, 0x60),
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColor.FromRgb(0x0D, 0x24, 0x44),
                TextColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                TitleColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                IsZoomEnabled = false,
                IsPanEnabled = false
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Pedal (%)",
                Minimum = 0,
                Maximum = 100,
                MajorStep = 20,
                MinorStep = 10,
                AxislineColor = OxyColor.FromRgb(0x0F, 0x34, 0x60),
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(0x0F, 0x34, 0x60),
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColor.FromRgb(0x0D, 0x24, 0x44),
                TextColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                TitleColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                IsZoomEnabled = false,
                IsPanEnabled = false
            });

            return model;
        }
    }
}
