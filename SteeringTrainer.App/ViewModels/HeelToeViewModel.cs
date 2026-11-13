using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using SteeringTrainer.Core.Collections;
using SteeringTrainer.Core.Models;
using SteeringTrainer.Core.Signal;
using SteeringTrainer.Core.Training.HeelToe;
using SteeringTrainer.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SteeringTrainer.App.ViewModels
{
    public class HeelToeViewModel : ViewModelBase
    {
        private SignalProcessor? _processor;
        private readonly HeelToeTrainingEngine _engine;
        private readonly HeelToeAudioService _audioService;
        private readonly BrakeHoldTrainingEngine _brakeHoldEngine;
        private readonly HeelToeHistoryRepository _historyRepository = new();
        private readonly List<HeelToeHistoryEntry> _persistentHistory = new();
        private readonly List<BrakeHoldHistoryEntry> _brakeHoldHistory = new();
        private readonly List<HeelToeAttemptResult> _scoreHistory = new();
        private readonly List<double> _consecutiveScores = new();
        private int _brakeWasReleased = 1;
        private readonly RingBuffer<SteeringSample> _recentBuffer = new(1500); // 3秒分
        private readonly DispatcherTimer _uiTimer;
        private SteeringSample? _latestPedalSample;

        // --- リアルタイムペダル値 ---
        private double _currentBrakePercent;
        public double CurrentBrakePercent
        {
            get => _currentBrakePercent;
            private set => Set(ref _currentBrakePercent, value);
        }

        private double _currentThrottlePercent;
        public double CurrentThrottlePercent
        {
            get => _currentThrottlePercent;
            private set => Set(ref _currentThrottlePercent, value);
        }

        public string CurrentBrakeText => $"{CurrentBrakePercent:F1}%";
        public string CurrentThrottleText => $"{CurrentThrottlePercent:F1}%";

        // --- 設定・目標値 ---
        public HeelToeConfig Config => _engine.Config;

        public double TargetBrake
        {
            get => Config.TargetBrake;
            set { var v = Math.Clamp(Math.Round(value / 5.0) * 5.0, 5, 95); if (Config.TargetBrake != v) { Config.TargetBrake = v; OnPropertyChanged(); OnPropertyChanged(nameof(TargetBrakeText)); PedalPlotModel?.InvalidatePlot(false); } }
        }
        public double TargetThrottle
        {
            get => Config.TargetThrottle;
            set { var v = Math.Clamp(Math.Round(value / 5.0) * 5.0, 5, 100); if (Config.TargetThrottle != v) { Config.TargetThrottle = v; OnPropertyChanged(); OnPropertyChanged(nameof(TargetThrottleText)); } }
        }
        private bool _randomizeFreeBrake;
        public bool RandomizeFreeBrake { get => _randomizeFreeBrake; set => Set(ref _randomizeFreeBrake, value); }
        private bool _randomizeFreeThrottle;
        public bool RandomizeFreeThrottle { get => _randomizeFreeThrottle; set => Set(ref _randomizeFreeThrottle, value); }

        public double RandomBrakeMin
        {
            get => Config.RandomBrakeMin;
            set
            {
                var v = Math.Clamp(Math.Round(value / 5.0) * 5.0, 5, 95);
                if (Config.RandomBrakeMin != v)
                {
                    Config.RandomBrakeMin = v;
                    if (Config.RandomBrakeMax < v)
                    {
                        Config.RandomBrakeMax = v;
                        OnPropertyChanged(nameof(RandomBrakeMax));
                        OnPropertyChanged(nameof(RandomBrakeMaxText));
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RandomBrakeMinText));
                }
            }
        }

        public double RandomBrakeMax
        {
            get => Config.RandomBrakeMax;
            set
            {
                var v = Math.Clamp(Math.Round(value / 5.0) * 5.0, 5, 95);
                if (Config.RandomBrakeMax != v)
                {
                    Config.RandomBrakeMax = v;
                    if (Config.RandomBrakeMin > v)
                    {
                        Config.RandomBrakeMin = v;
                        OnPropertyChanged(nameof(RandomBrakeMin));
                        OnPropertyChanged(nameof(RandomBrakeMinText));
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RandomBrakeMaxText));
                }
            }
        }

        public double RandomThrottleMin
        {
            get => Config.RandomThrottleMin;
            set
            {
                var v = Math.Clamp(Math.Round(value / 5.0) * 5.0, 5, 100);
                if (Config.RandomThrottleMin != v)
                {
                    Config.RandomThrottleMin = v;
                    if (Config.RandomThrottleMax < v)
                    {
                        Config.RandomThrottleMax = v;
                        OnPropertyChanged(nameof(RandomThrottleMax));
                        OnPropertyChanged(nameof(RandomThrottleMaxText));
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RandomThrottleMinText));
                }
            }
        }

        public double RandomThrottleMax
        {
            get => Config.RandomThrottleMax;
            set
            {
                var v = Math.Clamp(Math.Round(value / 5.0) * 5.0, 5, 100);
                if (Config.RandomThrottleMax != v)
                {
                    Config.RandomThrottleMax = v;
                    if (Config.RandomThrottleMin > v)
                    {
                        Config.RandomThrottleMin = v;
                        OnPropertyChanged(nameof(RandomThrottleMin));
                        OnPropertyChanged(nameof(RandomThrottleMinText));
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RandomThrottleMaxText));
                }
            }
        }

        public string RandomBrakeMinText => $"{RandomBrakeMin:F0}%";
        public string RandomBrakeMaxText => $"{RandomBrakeMax:F0}%";
        public string RandomThrottleMinText => $"{RandomThrottleMin:F0}%";
        public string RandomThrottleMaxText => $"{RandomThrottleMax:F0}%";
        public bool CanEditTargetSettings => !IsRunning || !_engine.IsChallengeMode;
        public double BrakeTolerance
        {
            get => Config.BrakeTolerance;
            set { var v = Math.Clamp(value, 1, 15); if (Config.BrakeTolerance != v) { Config.BrakeTolerance = v; OnPropertyChanged(); OnPropertyChanged(nameof(BrakeToleranceText)); } }
        }
        public double ThrottleTolerance
        {
            get => Config.ThrottleTolerance;
            set { var v = Math.Clamp(Math.Round(value / 5.0) * 5.0, 5, 30); if (Config.ThrottleTolerance != v) { Config.ThrottleTolerance = v; OnPropertyChanged(); OnPropertyChanged(nameof(ThrottleToleranceText)); } }
        }
        public string TargetBrakeText => $"{TargetBrake:F0}%";
        public string TargetThrottleText => $"{TargetThrottle:F0}%";
        public string BrakeToleranceText => $"±{BrakeTolerance:F0}%";
        public string ThrottleToleranceText => $"±{ThrottleTolerance:F0}%";
        public ObservableCollection<BrakeTargetAverage> BrakeTargetAverages { get; } = new();
        public string HoldResultText { get; private set; } = "未計測";
        public PlotModel ResultsHeatmapModel { get; }
        public PlotModel FreeResultsHeatmapModel { get; }
        public PlotModel BrakeHoldHistoryModel { get; }
        private readonly HeatMapSeries _resultsHeatmapSeries;
        private readonly HeatMapSeries _freeResultsHeatmapSeries;
        private string _historySummaryText = "まだ記録がありません";
        public string HistorySummaryText
        {
            get => _historySummaryText;
            private set => Set(ref _historySummaryText, value);
        }
        private string _freeHistorySummaryText = "まだ記録がありません";
        public string FreeHistorySummaryText
        {
            get => _freeHistorySummaryText;
            private set => Set(ref _freeHistorySummaryText, value);
        }
        private int _holdBlockNumber = 1;
        private int _holdRepetitionsInBlock;
        public string HoldProgressText { get; private set; } = "5回ずつ目標量を変えて計測します";

        private double _audioVolumePercent = 50;
        public double AudioVolumePercent
        {
            get => _audioVolumePercent;
            set
            {
                var volume = Math.Clamp(value, 0, 100);
                if (Set(ref _audioVolumePercent, volume))
                {
                    _audioService.Volume = volume / 100.0;
                    OnPropertyChanged(nameof(AudioVolumeText));
                }
            }
        }
        public string AudioVolumeText => $"{AudioVolumePercent:F0}%";

        public string[] Difficulties { get; } = { "Beginner (±5% / ±10%)", "Normal (±3% / ±7%)", "Advanced (±2% / ±5%)" };
        private int _selectedDifficultyIndex = 1;
        public int SelectedDifficultyIndex
        {
            get => _selectedDifficultyIndex;
            set
            {
                if (Set(ref _selectedDifficultyIndex, value))
                {
                    _engine.Config.ApplyDifficulty((HeelToeDifficulty)value);
                    OnPropertyChanged(nameof(TargetBrake));
                    OnPropertyChanged(nameof(TargetThrottle));
                    OnPropertyChanged(nameof(BrakeTolerance));
                    OnPropertyChanged(nameof(ThrottleTolerance));
                    OnPropertyChanged(nameof(TargetBrakeText));
                    OnPropertyChanged(nameof(TargetThrottleText));
                    OnPropertyChanged(nameof(BrakeTolerance));
                    OnPropertyChanged(nameof(BrakeToleranceText));
                    OnPropertyChanged(nameof(ThrottleTolerance));
                    OnPropertyChanged(nameof(ThrottleToleranceText));
                }
            }
        }

        public bool IsBlindMode
        {
            get => _engine.Config.IsBlindMode;
            set
            {
                if (_engine.Config.IsBlindMode != value)
                {
                    _engine.Config.IsBlindMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ShowIdealWaveform
        {
            get => _engine.Config.ShowIdealWaveform;
            set
            {
                if (_engine.Config.ShowIdealWaveform != value)
                {
                    _engine.Config.ShowIdealWaveform = value;
                    OnPropertyChanged();
                }
            }
        }

        // --- ステート・表示文言 ---
        private string _statusHeader = "IDLE";
        public string StatusHeader
        {
            get => _statusHeader;
            private set => Set(ref _statusHeader, value);
        }

        private string _statusSubText = "目標ブレーキを踏んでください";
        public string StatusSubText
        {
            get => _statusSubText;
            private set => Set(ref _statusSubText, value);
        }

        private Brush _statusBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        public Brush StatusBrush
        {
            get => _statusBrush;
            private set => Set(ref _statusBrush, value);
        }

        private string _roundProgressText = "ROUND 1 / 10";
        public string RoundProgressText
        {
            get => _roundProgressText;
            private set => Set(ref _roundProgressText, value);
        }

        private string _targetBlockProgressText = "目標ペア 1 / 5 回目";
        public string TargetBlockProgressText
        {
            get => _targetBlockProgressText;
            private set => Set(ref _targetBlockProgressText, value);
        }

        // --- スコア・フィードバック ---
        private string _scoreText = "-- / 100";
        public string ScoreText
        {
            get => _scoreText;
            private set => Set(ref _scoreText, value);
        }

        private string _consecutiveAverageText = "ブレーキを離すまでの平均: -- 点（0回）";
        public string ConsecutiveAverageText
        {
            get => _consecutiveAverageText;
            private set => Set(ref _consecutiveAverageText, value);
        }

        private string _stabilityScoreText = "-- / 50";
        public string StabilityScoreText
        {
            get => _stabilityScoreText;
            private set => Set(ref _stabilityScoreText, value);
        }

        private string _timingScoreText = "-- / 20";
        public string TimingScoreText
        {
            get => _timingScoreText;
            private set => Set(ref _timingScoreText, value);
        }

        private string _amountScoreText = "-- / 20";
        public string AmountScoreText
        {
            get => _amountScoreText;
            private set => Set(ref _amountScoreText, value);
        }

        private string _blipMetricsText = "時間: -- ms  |  反応: -- ms";
        public string BlipMetricsText
        {
            get => _blipMetricsText;
            private set => Set(ref _blipMetricsText, value);
        }

        private string _failureDiagnostics = string.Empty;
        public string FailureDiagnostics
        {
            get => _failureDiagnostics;
            private set => Set(ref _failureDiagnostics, value);
        }

        // --- チャレンジ集計 ---
        private string _challengeSummaryText = "チャレンジ待機中";
        public string ChallengeSummaryText
        {
            get => _challengeSummaryText;
            private set => Set(ref _challengeSummaryText, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (Set(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(StartBrakeHoldButtonText));
                    OnPropertyChanged(nameof(CanEditTargetSettings));
                }
            }
        }

        // --- OxyPlot モデル ---
        public PlotModel PedalPlotModel { get; }
        private readonly LineSeries _brakeSeries;
        private readonly LineSeries _throttleSeries;
        private readonly LineSeries _targetBrakeLine;
        private readonly LineSeries _idealBrakeSeries;
        private readonly LineSeries _idealThrottleSeries;

        // --- コマンド ---
        public ICommand StartChallengeCommand { get; }
        public ICommand StartFreeCommand { get; }
        public ICommand StartBrakeHoldCommand { get; }
        public ICommand TestAudioCommand { get; }
        public ICommand StopCommand { get; }

        public HeelToeViewModel()
        {
            _engine = new HeelToeTrainingEngine();
            _audioService = new HeelToeAudioService();
            _audioService.Volume = _audioVolumePercent / 100.0;
            _brakeHoldEngine = new BrakeHoldTrainingEngine();

            ResultsHeatmapModel = CreateResultsHeatmap("Challenge", out _resultsHeatmapSeries);
            FreeResultsHeatmapModel = CreateResultsHeatmap("Free Practice", out _freeResultsHeatmapSeries);
            BrakeHoldHistoryModel = CreateBrakeHoldHistoryPlot();

            _engine.StateChanged += OnEngineStateChanged;
            _engine.BeepTriggered += b => _audioService.PlayBeep(b);
            _engine.AttemptCompleted += OnAttemptCompleted;
            _engine.ChallengeCompleted += OnChallengeCompleted;
            _brakeHoldEngine.StatusChanged += message => App.Current?.Dispatcher.InvokeAsync(() => StatusSubText = message);
            _brakeHoldEngine.AttemptCompleted += seconds => App.Current?.Dispatcher.InvokeAsync(() => OnBrakeHoldCompleted(seconds));

            StartChallengeCommand = new RelayCommand(() => Start(true));
            StartFreeCommand = new RelayCommand(() => Start(false));
            StartBrakeHoldCommand = new RelayCommand(OnBrakeHoldButton, () => _processor != null);
            StopCommand = new RelayCommand(Stop, () => IsRunning);
            TestAudioCommand = new RelayCommand(() => _audioService.PlayBeep(HeelToeBeepType.Good));

            // プロット初期化
            PedalPlotModel = CreatePedalPlot();
            _brakeSeries = new LineSeries { Title = "Brake", Color = OxyColor.FromRgb(0xF4, 0x43, 0x36), StrokeThickness = 2.5 };
            _throttleSeries = new LineSeries { Title = "Throttle", Color = OxyColor.FromRgb(0x4C, 0xAF, 0x50), StrokeThickness = 2.5 };
            _targetBrakeLine = new LineSeries { Title = "Target Brake", Color = OxyColor.FromRgb(0xFF, 0xC1, 0x07), StrokeThickness = 1.5, LineStyle = LineStyle.Dash };
            _idealBrakeSeries = new LineSeries { Title = "手本: Brake", Color = OxyColor.FromArgb(100, 244, 67, 54), StrokeThickness = 1.5, LineStyle = LineStyle.Dot };
            _idealThrottleSeries = new LineSeries { Title = "手本: Throttle", Color = OxyColor.FromArgb(100, 76, 175, 80), StrokeThickness = 1.5, LineStyle = LineStyle.Dot };

            PedalPlotModel.Series.Add(_targetBrakeLine);
            PedalPlotModel.Series.Add(_idealBrakeSeries);
            PedalPlotModel.Series.Add(_idealThrottleSeries);
            PedalPlotModel.Series.Add(_brakeSeries);
            PedalPlotModel.Series.Add(_throttleSeries);

            _uiTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.CurrentDispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
            _uiTimer.Tick += OnUiTick;
            _uiTimer.Start();
            LoadPersistentHistory();
        }

        public void ApplySettings(AppSettings settings)
        {
            TargetBrake = settings.TargetBrake;
            TargetThrottle = settings.TargetThrottle;
            BrakeTolerance = settings.BrakeTolerance;
            ThrottleTolerance = settings.ThrottleTolerance;
            SelectedDifficultyIndex = Math.Clamp(settings.SelectedDifficultyIndex, 0, Difficulties.Length - 1);
            AudioVolumePercent = settings.AudioVolumePercent;
            ShowIdealWaveform = settings.ShowIdealWaveform;
            IsBlindMode = settings.IsBlindMode;
            RandomizeFreeBrake = settings.RandomizeFreeBrake;
            RandomizeFreeThrottle = settings.RandomizeFreeThrottle;
            RandomBrakeMin = settings.RandomBrakeMin;
            RandomBrakeMax = settings.RandomBrakeMax;
            RandomThrottleMin = settings.RandomThrottleMin;
            RandomThrottleMax = settings.RandomThrottleMax;
        }

        public void WriteToSettings(AppSettings settings)
        {
            settings.TargetBrake = TargetBrake;
            settings.TargetThrottle = TargetThrottle;
            settings.BrakeTolerance = BrakeTolerance;
            settings.ThrottleTolerance = ThrottleTolerance;
            settings.SelectedDifficultyIndex = SelectedDifficultyIndex;
            settings.AudioVolumePercent = (int)Math.Round(AudioVolumePercent);
            settings.ShowIdealWaveform = ShowIdealWaveform;
            settings.IsBlindMode = IsBlindMode;
            settings.RandomizeFreeBrake = RandomizeFreeBrake;
            settings.RandomizeFreeThrottle = RandomizeFreeThrottle;
            settings.RandomBrakeMin = RandomBrakeMin;
            settings.RandomBrakeMax = RandomBrakeMax;
            settings.RandomThrottleMin = RandomThrottleMin;
            settings.RandomThrottleMax = RandomThrottleMax;
        }

        public void ToggleStartStop()
        {
            if (IsRunning) Stop();
            else Start(challenge: false);
        }

        private static PlotModel CreateResultsHeatmap(string title, out HeatMapSeries series)
        {
            var model = new PlotModel
            {
                Title = $"{title} — Average score by target brake / throttle",
                Background = OxyColor.FromRgb(0x16, 0x21, 0x3E),
                PlotAreaBackground = OxyColor.FromRgb(0x1A, 0x1A, 0x2E),
                TextColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                TitleColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E)
            };
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Target Brake (%)",
                IsZoomEnabled = false,
                IsPanEnabled = false,
                Minimum = 2.5,
                Maximum = 97.5,
                MajorStep = 10,
                MinorStep = 5,
                TextColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                TitleColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(0x35, 0x45, 0x60)
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Target Throttle (%)",
                IsZoomEnabled = false,
                IsPanEnabled = false,
                Minimum = 2.5,
                Maximum = 102.5,
                MajorStep = 10,
                MinorStep = 5,
                TextColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                TitleColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(0x35, 0x45, 0x60)
            });
            model.Axes.Add(new LinearColorAxis
            {
                Position = AxisPosition.Right,
                Key = "ScoreAxis",
                Title = "Average score",
                IsZoomEnabled = false,
                IsPanEnabled = false,
                Minimum = 0,
                Maximum = 100,
                Palette = new OxyPalette(Enumerable.Range(0, 101).Select(i =>
                {
                    var t = i / 100.0;
                    if (t <= 0.5)
                    {
                        var u = t * 2.0;
                        return OxyColor.FromRgb((byte)255, (byte)(255 * u), (byte)0);
                    }

                    var v = (t - 0.5) * 2.0;
                    return OxyColor.FromRgb((byte)(255 * (1 - v)), (byte)255, (byte)0);
                }).ToList()),
                TextColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                TitleColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0)
            });
            series = new HeatMapSeries
            {
                Data = CreateEmptyHeatmap(),
                X0 = 2.5,
                X1 = 97.5,
                Y0 = 2.5,
                Y1 = 102.5,
                Interpolate = false,
                ColorAxisKey = "ScoreAxis"
            };
            model.Series.Add(series);
            return model;
        }

        private static PlotModel CreateBrakeHoldHistoryPlot()
        {
            var model = new PlotModel
            {
                Title = "Brake hold average score by target",
                Background = OxyColor.FromRgb(0x16, 0x21, 0x3E),
                PlotAreaBackground = OxyColor.FromRgb(0x1A, 0x1A, 0x2E),
                TextColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                TitleColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E)
            };
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Target Brake (%)",
                Minimum = 0,
                Maximum = 100,
                MajorStep = 10,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                TextColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                TitleColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(0x35, 0x45, 0x60)
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Average score (100 − 10 × seconds)",
                Minimum = 0,
                Maximum = 100,
                MajorStep = 10,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                TextColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                TitleColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(0x35, 0x45, 0x60)
            });
            model.Series.Add(new LineSeries
            {
                Title = "Average score",
                Color = OxyColor.FromRgb(0x00, 0xBC, 0xD4),
                MarkerType = MarkerType.Circle,
                MarkerSize = 5,
                MarkerFill = OxyColor.FromRgb(0x00, 0xBC, 0xD4),
                StrokeThickness = 2
            });
            return model;
        }

        private static double[,] CreateEmptyHeatmap()
        {
            var data = new double[20, 19];
            for (var y = 0; y < 20; y++)
                for (var x = 0; x < 19; x++)
                    data[y, x] = double.NaN;
            return data;
        }

        private void LoadPersistentHistory()
        {
            _ = Task.Run(_historyRepository.LoadAll).ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully) return;
                App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    _persistentHistory.Clear();
                    _persistentHistory.AddRange(task.Result);
                    UpdateResultsHeatmap();
                });
            });
            _ = Task.Run(_historyRepository.LoadBrakeHoldAll).ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully) return;
                App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    _brakeHoldHistory.Clear();
                    _brakeHoldHistory.AddRange(task.Result);
                    UpdateBrakeHoldHistoryPlot();
                });
            });
        }

        private void SaveHistory(HeelToeAttemptResult result)
        {
            var isChallenge = _engine.IsChallengeMode;
            _ = Task.Run(() => _historyRepository.Save(result, isChallenge)).ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully) return;
                App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (_persistentHistory.All(entry => entry.Id != task.Result.Id))
                    {
                        _persistentHistory.Add(task.Result);
                        UpdateResultsHeatmap();
                    }
                });
            });
        }

        private void UpdateResultsHeatmap()
        {
            var challengeHistory = _persistentHistory.Where(entry => entry.IsChallenge).ToList();
            var freeHistory = _persistentHistory.Where(entry => !entry.IsChallenge).ToList();
            _resultsHeatmapSeries.Data = CreateHeatmapData(challengeHistory);
            _freeResultsHeatmapSeries.Data = CreateHeatmapData(freeHistory);
            HistorySummaryText = BuildHistorySummary(challengeHistory);
            FreeHistorySummaryText = BuildHistorySummary(freeHistory);
            ResultsHeatmapModel.InvalidatePlot(true);
            FreeResultsHeatmapModel.InvalidatePlot(true);
        }

        private static double[,] CreateHeatmapData(IReadOnlyList<HeelToeHistoryEntry> history)
        {
            var data = CreateEmptyHeatmap();
            var groups = history.GroupBy(entry =>
                (Brake: (int)Math.Round(entry.TargetBrake / 5.0), Throttle: (int)Math.Round(entry.TargetThrottle / 5.0)));
            foreach (var group in groups)
            {
                var x = group.Key.Brake - 1;
                var y = group.Key.Throttle - 1;
                if (x is >= 0 and < 19 && y is >= 0 and < 20)
                    data[y, x] = group.Average(entry => entry.Score);
            }

            return data;
        }

        private static string BuildHistorySummary(IReadOnlyList<HeelToeHistoryEntry> history)
        {
            var combinations = history
                .GroupBy(entry => (entry.TargetBrake, entry.TargetThrottle))
                .Select(group => new { group.Key.TargetBrake, group.Key.TargetThrottle, Average = group.Average(x => x.Score), Count = group.Count() })
                .ToList();
            if (combinations.Count == 0)
                return "まだ記録がありません。練習結果がここに蓄積されます。";
            var weakest = combinations.OrderBy(x => x.Average).First();
            return $"全{history.Count}回 / {combinations.Count}条件 | 苦手: Brake {weakest.TargetBrake:F0}% × Throttle {weakest.TargetThrottle:F0}%（平均 {weakest.Average:F1}点・{weakest.Count}回）";
        }

        private static PlotModel CreatePedalPlot()
        {
            var model = new PlotModel
            {
                Background = OxyColor.FromRgb(0x16, 0x21, 0x3E),
                PlotAreaBackground = OxyColor.FromRgb(0x1A, 0x1A, 0x2E),
                TextColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                Title = "Heel & Toe Pedal Waveforms (Brake & Throttle)",
                TitleFontSize = 12,
                TitleColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E)
            };

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Time (s)",
                AxislineColor = OxyColor.FromRgb(0x0F, 0x34, 0x60),
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(0x0F, 0x34, 0x60),
                TextColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                TitleColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E)
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Pedal (%)",
                Minimum = 0,
                Maximum = 100,
                AxislineColor = OxyColor.FromRgb(0x0F, 0x34, 0x60),
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(0x0F, 0x34, 0x60),
                TextColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                TitleColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E)
            });

            return model;
        }

        public void SetSignalProcessor(SignalProcessor processor)
        {
            if (_processor != null)
            {
                _processor.SampleProduced -= OnSampleProduced;
            }
            _processor = processor;
            _processor.SampleProduced += OnSampleProduced;
        }

        private void OnSampleProduced(SteeringSample sample)
        {
            _latestPedalSample = sample;
            _recentBuffer.Push(sample);
            if (sample.Brake <= 0.005)
            {
                if (Interlocked.Exchange(ref _brakeWasReleased, 1) == 0)
                {
                    App.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _consecutiveScores.Clear();
                        ConsecutiveAverageText = "ブレーキを離すまでの平均: -- 点（0回）";
                    }));
                }
            }
            else
            {
                Interlocked.Exchange(ref _brakeWasReleased, 0);
            }
            if (IsRunning)
            {
                if (_isBrakeHoldMode) _brakeHoldEngine.ProcessSample(sample);
                else _engine.ProcessSample(sample);
            }
        }

        private bool _isBrakeHoldMode;
        public string StartBrakeHoldButtonText => IsRunning && _isBrakeHoldMode
            ? "ブリッピング練習へ戻る"
            : "ブレーキ安定テスト（0.5秒保持）";

        private void Start(bool challenge)
        {
            if (_processor == null)
            {
                StatusHeader = "NO INPUT";
                StatusSubText = "入力デバイスを初期化してから開始してください。";
                return;
            }
            _isBrakeHoldMode = false;
            OnPropertyChanged(nameof(StartBrakeHoldButtonText));
            _brakeHoldEngine.Stop();
            _recentBuffer.Clear();
            _scoreHistory.Clear();
            BrakeTargetAverages.Clear();
            IsRunning = true;
            _engine.StartSession(challenge, challenge || RandomizeFreeBrake, challenge || RandomizeFreeThrottle);
            OnPropertyChanged(nameof(CanEditTargetSettings));
            RoundProgressText = challenge ? $"ROUND 1 / {_engine.Config.ChallengeRounds}" : "FREE PRACTICE";
            FailureDiagnostics = string.Empty;
        }

        private void StartBrakeHold()
        {
            _engine.StopSession();
            _isBrakeHoldMode = true;
            OnPropertyChanged(nameof(StartBrakeHoldButtonText));
            _recentBuffer.Clear();
            IsRunning = true;
            _holdBlockNumber = 1;
            _holdRepetitionsInBlock = 0;
            HoldProgressText = $"ブロック 1: 目標 {TargetBrake:F0}% / 0回目";
            OnPropertyChanged(nameof(HoldProgressText));
            HoldResultText = "ブレーキを踏むと計測開始";
            OnPropertyChanged(nameof(HoldResultText));
            StatusHeader = $"BRAKE {TargetBrake:F0}%";
            StatusBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
            ScoreText = "0.5秒保持";
            _brakeHoldEngine.Start(TargetBrake, BrakeTolerance, 0.5);
        }

        private void OnBrakeHoldButton()
        {
            if (IsRunning && _isBrakeHoldMode)
            {
                Start(challenge: false);
                return;
            }
            StartBrakeHold();
        }

        private void OnBrakeHoldCompleted(double seconds)
        {
            var completedTarget = TargetBrake;
            _ = Task.Run(() => _historyRepository.SaveBrakeHold(completedTarget, seconds)).ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully) return;
                App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (_brakeHoldHistory.All(entry => entry.Id != task.Result.Id))
                    {
                        _brakeHoldHistory.Add(task.Result);
                        UpdateBrakeHoldHistoryPlot();
                    }
                });
            });

            _holdRepetitionsInBlock++;
            HoldResultText = $"目標範囲に入るまで {seconds:F2} 秒（±{BrakeTolerance:F0}%内を0.5秒保持）";
            StatusHeader = "STABLE";
            StatusBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

            if (_holdRepetitionsInBlock >= 5)
            {
                var previousTarget = TargetBrake;
                var nextTarget = previousTarget;

                int minStep = (int)Math.Round(Math.Clamp(RandomBrakeMin, 5, 95) / 5.0);
                int maxStep = (int)Math.Round(Math.Clamp(RandomBrakeMax, 5, 95) / 5.0);
                if (minStep > maxStep) (minStep, maxStep) = (maxStep, minStep);

                if (minStep == maxStep)
                {
                    nextTarget = minStep * 5.0;
                }
                else
                {
                    while (nextTarget == previousTarget)
                        nextTarget = Random.Shared.Next(minStep, maxStep + 1) * 5.0;
                }

                _holdBlockNumber++;
                _holdRepetitionsInBlock = 0;
                Config.TargetBrake = nextTarget;
                _brakeHoldEngine.SetTargetBrake(nextTarget);
                OnPropertyChanged(nameof(TargetBrake));
                OnPropertyChanged(nameof(TargetBrakeText));
                HoldProgressText = $"次の目標へ: {nextTarget:F0}% （5回完了後に新しい量をランダム選択）";
            }
            else
            {
                HoldProgressText = $"ブロック {_holdBlockNumber}: 目標 {TargetBrake:F0}% / {_holdRepetitionsInBlock}/5回";
            }
            OnPropertyChanged(nameof(HoldResultText));
            OnPropertyChanged(nameof(HoldProgressText));
        }

        private void UpdateBrakeHoldHistoryPlot()
        {
            var series = (LineSeries)BrakeHoldHistoryModel.Series[0];
            series.Points.Clear();
            foreach (var group in _brakeHoldHistory.GroupBy(entry => entry.TargetBrake).OrderBy(group => group.Key))
                series.Points.Add(new DataPoint(group.Key, group.Average(entry => entry.Score)));
            BrakeHoldHistoryModel.InvalidatePlot(true);
        }

        public void Stop()
        {
            IsRunning = false;
            _engine.StopSession();
            _brakeHoldEngine.Stop();
            StatusHeader = "STOPPED";
            StatusSubText = "トレーニング停止中";
            StatusBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        }

        private void OnEngineStateChanged(HeelToeState state, string message)
        {
            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                StatusSubText = message;
                switch (state)
                {
                    case HeelToeState.Idle:
                        StatusHeader = $"BRAKE {TargetBrake:F0}%";
                        StatusBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
                        break;
                    case HeelToeState.BrakeEntry:
                        StatusHeader = "BRAKE ENTRY";
                        StatusBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                        break;
                    case HeelToeState.BrakeStabilizing:
                        StatusHeader = "STABILIZING";
                        StatusBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
                        break;
                    case HeelToeState.BrakeStabilized:
                        StatusHeader = "BLIP!";
                        StatusBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                        break;
                    case HeelToeState.ThrottleBlipping:
                        StatusHeader = "BLIPPING...";
                        StatusBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4));
                        break;
                    case HeelToeState.BrakeRecovery:
                        StatusHeader = "RECOVERY";
                        StatusBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0x27, 0xB0));
                        break;
                }

                if (_engine.IsChallengeMode)
                {
                    RoundProgressText = $"ROUND {_engine.CurrentRound} / {_engine.Config.ChallengeRounds}";
                }
                TargetBlockProgressText = $"目標ペア {_engine.TargetBlockAttempt} / 5 回目";
                OnPropertyChanged(nameof(TargetBrakeText));
                OnPropertyChanged(nameof(TargetThrottleText));
                OnPropertyChanged(nameof(TargetBrake));
                OnPropertyChanged(nameof(TargetThrottle));
            });
        }

        private void OnAttemptCompleted(HeelToeAttemptResult result)
        {
            _scoreHistory.Add(result);
            App.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _consecutiveScores.Add(result.TotalScore);
                ConsecutiveAverageText = $"ブレーキを離すまでの平均: {_consecutiveScores.Average():F1} 点（{_consecutiveScores.Count}回）";
            }));
            RefreshBrakeTargetAverages();
            SaveHistory(result);
            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                StatusHeader = result.Grade;
                StatusSubText = result.PrimaryFeedback;
                ScoreText = $"{result.TotalScore:F0} / 100";
                StabilityScoreText = $"{result.BrakeStabilityScore:F1} / 50";
                TimingScoreText = $"{result.BlipTimingScore:F1} / 20";
                AmountScoreText = $"{result.BlipAmountScore:F1} / 20";
                BlipMetricsText = $"時間: {result.BlipDurationMs:F0} ms  |  反応: {result.BlipReactionTimeMs:F0} ms  |  ピーク: {result.ActualPeakThrottle:F1}%";

                StatusBrush = result.Grade switch
                {
                    "PERFECT BLIP!" => new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
                    "GOOD" => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                    "OK" => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
                    _ => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36))
                };

                if (result.FailureDescriptions.Count > 0)
                {
                    FailureDiagnostics = "【改善ポイント】\n" + string.Join("\n", result.FailureDescriptions.Select(d => "・" + d));
                }
                else
                {
                    FailureDiagnostics = "【診断結果】減点項目なし。極めて精度の高い操作です。";
                }
            });
        }

        private void RefreshBrakeTargetAverages()
        {
            var summaries = _scoreHistory
                .GroupBy(x => x.TargetBrake)
                .OrderBy(x => x.Key)
                .Select(group => new BrakeTargetAverage(
                    group.Key,
                    group.Count(),
                    group.Average(x => x.TotalScore),
                    group.Average(x => x.BrakeStabilityScore)))
                .ToList();
            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                BrakeTargetAverages.Clear();
                foreach (var summary in summaries) BrakeTargetAverages.Add(summary);
            });
        }

        private void OnChallengeCompleted(HeelToeChallengeSummary summary)
        {
            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                StatusHeader = "CHALLENGE FINISHED";
                StatusBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                ChallengeSummaryText = $"【10回チャレンジ結果】\n" +
                    $"平均スコア: {summary.AverageScore:F1} 点  |  ベスト: {summary.BestScore:F1} 点\n" +
                    $"安定性平均: {summary.BrakeStabilityAverage:F1}/50  |  タイミング平均: {summary.BlipTimingAverage:F1}/20\n" +
                    $"PERFECT: {summary.PerfectCount} 回  |  MISS: {summary.MissCount} 回";
                IsRunning = false;
            });
        }

        private void OnUiTick(object? sender, EventArgs e)
        {
            if (_latestPedalSample is { } sample)
            {
                CurrentBrakePercent = sample.Brake * 100.0;
                CurrentThrottlePercent = sample.Throttle * 100.0;
                OnPropertyChanged(nameof(CurrentBrakeText));
                OnPropertyChanged(nameof(CurrentThrottleText));
            }

            UpdatePlot();
        }

        private void UpdatePlot()
        {
            var samples = _recentBuffer.ToList();
            if (samples.Count == 0) return;

            double lastTime = samples[^1].TimestampSeconds;
            double windowSeconds = 3.0;
            double minTime = lastTime - windowSeconds;

            _brakeSeries.Points.Clear();
            _throttleSeries.Points.Clear();
            _targetBrakeLine.Points.Clear();
            _idealBrakeSeries.Points.Clear();
            _idealThrottleSeries.Points.Clear();

            _targetBrakeLine.Points.Add(new DataPoint(minTime, TargetBrake));
            _targetBrakeLine.Points.Add(new DataPoint(lastTime, TargetBrake));

            foreach (var s in samples)
            {
                double t = s.TimestampSeconds;
                if (t < minTime) continue;
                _brakeSeries.Points.Add(new DataPoint(t, s.Brake * 100.0));
                _throttleSeries.Points.Add(new DataPoint(t, s.Throttle * 100.0));
            }

            // 手本モード（理想波形の重ね合わせ描画）
            if (ShowIdealWaveform)
            {
                double centerTime = lastTime - 1.0;
                double blipWidth = 0.20; // 200ms
                for (double t = minTime; t <= lastTime; t += 0.05)
                {
                    _idealBrakeSeries.Points.Add(new DataPoint(t, TargetBrake));
                    double dt = (t - centerTime) / (blipWidth * 0.5);
                    double throttleVal = TargetThrottle * Math.Exp(-dt * dt * 2.0);
                    _idealThrottleSeries.Points.Add(new DataPoint(t, throttleVal));
                }
            }

            foreach (var axis in PedalPlotModel.Axes)
            {
                if (axis.Position == AxisPosition.Bottom)
                {
                    axis.Minimum = minTime;
                    axis.Maximum = lastTime;
                }
            }

            PedalPlotModel.InvalidatePlot(true);
        }
    }
}
