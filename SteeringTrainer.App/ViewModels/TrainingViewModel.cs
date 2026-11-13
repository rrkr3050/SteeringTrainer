using SteeringTrainer.Core.Collections;
using SteeringTrainer.Core.Models;
using SteeringTrainer.Core.Signal;
using SteeringTrainer.Core.Signal.Filters;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SteeringTrainer.App.ViewModels
{
    /// <summary>
    /// メイン練習画面のViewModel
    /// </summary>
    public class TrainingViewModel : ViewModelBase
    {
        private SignalProcessor? _processor;
        private readonly RingBuffer<SteeringSample> _sessionBuffer;
        private readonly DispatcherTimer _uiTimer;
        private volatile bool _isRunning;
        private volatile RealTimeVerdict _pendingVerdict = RealTimeVerdict.None;
        private readonly EmaFilter _accelerationDisplayFilter = new(0.72);
        private readonly Queue<DataPoint> _accelerationPoints = new();
        private const double AccelerationGraphWindowSeconds = 8.0;

        // --- コマンド ---
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public PlotModel AccelerationPlotModel { get; }
        private readonly LineSeries _filteredAccelerationSeries;

        // --- 練習設定 ---
        private double _lockAngle = 540.0;
        public double LockAngle
        {
            get => _lockAngle;
            set { if (Set(ref _lockAngle, value)) OnPropertyChanged(nameof(TargetVelocityText)); }
        }

        private double _timeSideSeconds = 2.5;
        public double TimeSideSeconds
        {
            get => _timeSideSeconds;
            set { if (Set(ref _timeSideSeconds, value)) OnPropertyChanged(nameof(TargetVelocityText)); }
        }

        public double TargetVelocity => LockAngle / TimeSideSeconds;
        public string TargetVelocityText => $"{TargetVelocity:F1}\u00b0/s";

        // --- リアルタイム数値 ---
        private double _currentAngle;
        public double CurrentAngle { get => _currentAngle; private set => Set(ref _currentAngle, value); }

        private double _currentVelocity;
        public double CurrentVelocity { get => _currentVelocity; private set => Set(ref _currentVelocity, value); }

        private double _currentAcceleration;
        public double CurrentAcceleration { get => _currentAcceleration; private set => Set(ref _currentAcceleration, value); }

        public string CurrentAngleText => $"{CurrentAngle:+0.0;-0.0; 0.0}\u00b0";
        public string CurrentVelocityText => $"{CurrentVelocity:+0.0;-0.0; 0.0}\u00b0/s";
        public string CurrentAccelerationText => $"{CurrentAcceleration:+0;-0; 0}\u00b0/s\u00b2";

        // --- GOOD/WARNING/MISS ---
        private RealTimeVerdict _verdict = RealTimeVerdict.None;
        public RealTimeVerdict Verdict
        {
            get => _verdict;
            private set
            {
                if (Set(ref _verdict, value))
                {
                    OnPropertyChanged(nameof(VerdictText));
                    OnPropertyChanged(nameof(VerdictBrush));
                }
            }
        }

        public string VerdictText => Verdict switch
        {
            RealTimeVerdict.Good => "GOOD",
            RealTimeVerdict.Warning => "WARNING",
            RealTimeVerdict.Miss => "MISS",
            _ => "---"
        };

        public Brush VerdictBrush => Verdict switch
        {
            RealTimeVerdict.Good => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            RealTimeVerdict.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
            RealTimeVerdict.Miss => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
            _ => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E))
        };

        // --- 素有ゲージ ---
        private double _velocityGaugeValue;
        public double VelocityGaugeValue { get => _velocityGaugeValue; private set => Set(ref _velocityGaugeValue, value); }

        private bool _isRunningProp;
        public bool IsRunning
        {
            get => _isRunningProp;
            private set
            {
                if (Set(ref _isRunningProp, value))
                {
                    OnPropertyChanged(nameof(StartStopText));
                }
            }
        }
        public string StartStopText => IsRunning ? "[STOP]" : "[START]";

        // --- プリセット ---
        public string[] Presets { get; } = { "Beginner (540\u00b0/3.0s)", "Normal (540\u00b0/2.5s)", "Fast (540\u00b0/2.0s)", "Advanced (720\u00b0/2.5s)" };
        private int _selectedPresetIndex = 1;
        public int SelectedPresetIndex
        {
            get => _selectedPresetIndex;
            set
            {
                if (Set(ref _selectedPresetIndex, value)) ApplyPreset(value);
            }
        }

        public TrainingViewModel()
        {
            _sessionBuffer = new RingBuffer<SteeringSample>(500 * 60 * 5); // 5分上限

            AccelerationPlotModel = new PlotModel
            {
                Title = "Steering Acceleration (Low-pass filtered)",
                Background = OxyColor.FromRgb(0x16, 0x21, 0x3E),
                PlotAreaBackground = OxyColor.FromRgb(0x1A, 0x1A, 0x2E),
                TextColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
                TitleColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                TitleFontSize = 12
            };
            AccelerationPlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Time (s)",
                TextColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                TitleColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(0x0F, 0x34, 0x60)
            });
            AccelerationPlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Acceleration (°/s²)",
                TextColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                TitleColor = OxyColor.FromRgb(0x9E, 0x9E, 0x9E),
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(0x0F, 0x34, 0x60)
            });
            _filteredAccelerationSeries = new LineSeries
            {
                Title = "Filtered acceleration",
                Color = OxyColor.FromRgb(0xFF, 0x98, 0x00),
                StrokeThickness = 2
            };
            AccelerationPlotModel.Series.Add(_filteredAccelerationSeries);

            StartCommand = new RelayCommand(StartSession, () => !IsRunning && _processor != null);
            StopCommand = new RelayCommand(StopSession, () => IsRunning);
            ExportCsvCommand = new RelayCommand(ExportCsv);

            // DispatcherPriority.Render を指定し、ウィンドウ非アクティブ時でも更新を維持
            _uiTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.CurrentDispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
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

            _accelerationDisplayFilter.Reset();
            _accelerationPoints.Clear();
            _filteredAccelerationSeries.Points.Clear();
            AccelerationPlotModel.InvalidatePlot(true);
        }

        private void OnSampleProduced(SteeringSample sample)
        {
            if (_isRunning)
            {
                _sessionBuffer.Push(sample);
                EvaluateRealTimeInternal(sample);
            }
        }

        private void ApplyPreset(int index)
        {
            (LockAngle, TimeSideSeconds) = index switch
            {
                0 => (540.0, 3.0),
                1 => (540.0, 2.5),
                2 => (540.0, 2.0),
                3 => (720.0, 2.5),
                _ => (540.0, 2.5)
            };
        }

        private void StartSession()
        {
            _sessionBuffer.Clear();
            _isRunning = true;
            IsRunning = true;
        }

        public void StopSession()
        {
            _isRunning = false;
            IsRunning = false;
            _pendingVerdict = RealTimeVerdict.None;
            Verdict = RealTimeVerdict.None;
            VelocityGaugeValue = 0;
        }

        public void ToggleStartStop()
        {
            if (_isRunning) StopSession();
            else StartSession();
        }

        private void OnUiTick(object? sender, EventArgs e)
        {
            if (_processor == null) return;

            var sample = _processor.LatestSample;
            if (sample == null) return;

            CurrentAngle = sample.AngleDegrees;
            CurrentVelocity = sample.Velocity;
            CurrentAcceleration = sample.Acceleration;

            OnPropertyChanged(nameof(CurrentAngleText));
            OnPropertyChanged(nameof(CurrentVelocityText));
            OnPropertyChanged(nameof(CurrentAccelerationText));

            UpdateAccelerationGraph(sample);

            if (_isRunning)
            {
                Verdict = _pendingVerdict;
                UpdateGauge();
            }
        }

        private void UpdateAccelerationGraph(SteeringSample sample)
        {
            var filtered = _accelerationDisplayFilter.Filter(sample.Acceleration, 1.0 / 60.0);
            var time = sample.TimestampSeconds;
            _accelerationPoints.Enqueue(new DataPoint(time, filtered));
            var minimumTime = time - AccelerationGraphWindowSeconds;
            while (_accelerationPoints.Count > 0 && _accelerationPoints.Peek().X < minimumTime)
                _accelerationPoints.Dequeue();

            _filteredAccelerationSeries.Points.Clear();
            foreach (var point in _accelerationPoints)
                _filteredAccelerationSeries.Points.Add(point);

            var xAxis = AccelerationPlotModel.Axes.First(a => a.Position == AxisPosition.Bottom);
            xAxis.Minimum = minimumTime;
            xAxis.Maximum = time;
            AccelerationPlotModel.InvalidatePlot(false);
        }

        private void EvaluateRealTimeInternal(SteeringSample sample)
        {
            if (TargetVelocity <= 0) return;

            var absVel = Math.Abs(sample.Velocity);
            var err = Math.Abs(absVel - TargetVelocity) / TargetVelocity * 100.0;

            _pendingVerdict = err switch
            {
                < 5.0 => RealTimeVerdict.Good,
                < 15.0 => RealTimeVerdict.Warning,
                _ => RealTimeVerdict.Miss
            };
        }

        private void UpdateGauge()
        {
            var ratio = TargetVelocity > 0
                ? Math.Clamp(Math.Abs(CurrentVelocity) / TargetVelocity, 0, 1.5)
                : 0;
            VelocityGaugeValue = ratio;
        }

        private void ExportCsv()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"SteeringTrainer_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("timestamp_s,raw_angle,filtered_angle,velocity,acceleration,raw_normalized");

            var samples = _sessionBuffer.ToList();
            foreach (var s in samples)
            {
                sb.AppendLine($"{s.TimestampSeconds:F4},{s.RawAngleDegrees:F2},{s.AngleDegrees:F2},{s.Velocity:F2},{s.Acceleration:F2},{s.RawNormalized:F4}");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"保存完了\n{path}", "CSVエクスポート", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
