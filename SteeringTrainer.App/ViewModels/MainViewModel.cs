using SteeringTrainer.Core.Devices;
using SteeringTrainer.Core.Models;
using SteeringTrainer.Core.Signal;
using SteeringTrainer.Infrastructure.Persistence;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SteeringTrainer.App.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly DeviceManager _deviceManager;
        private SignalProcessor? _signalProcessor;
        private readonly DispatcherTimer _uiTimer;
        private bool _disposed;
        private ISteeringDevice? _subscribedDevice;

        // --- 子VM ---
        public TrainingViewModel TrainingViewModel { get; }
        public HeelToeViewModel HeelToeViewModel { get; }
        public ThrottleTrackingViewModel ThrottleTrackingViewModel { get; }
        public HistoryViewModel HistoryViewModel { get; }
        public SettingsViewModel SettingsViewModel { get; }

        // --- UIバインディング ---
        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (Set(ref _selectedTabIndex, value))
                {
                    if (value == 3)
                    {
                        HistoryViewModel.LoadHistory();
                    }
                    SaveSettings();
                }
            }
        }

        private string _deviceName = "(未選択)";
        public string DeviceName
        {
            get => _deviceName;
            private set => Set(ref _deviceName, value);
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (Set(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(ConnectionStatusText));
                    OnPropertyChanged(nameof(ConnectionStatusBrush));
                }
            }
        }

        public string ConnectionStatusText => IsConnected ? "● CONNECTED" : "○ DISCONNECTED";
        public Brush ConnectionStatusBrush => IsConnected
            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
            : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));

        private string _samplingRateText = "-- Hz";
        public string SamplingRateText
        {
            get => _samplingRateText;
            private set => Set(ref _samplingRateText, value);
        }

        private string _statusMessage = "デバイスを選択してください";
        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        private bool _alwaysOnTop;
        public bool AlwaysOnTop
        {
            get => _alwaysOnTop;
            set
            {
                if (Set(ref _alwaysOnTop, value))
                {
                    SaveSettings();
                }
            }
        }

        public MainViewModel()
        {
            _deviceManager = new DeviceManager();

            // 設定読み込み
            var savedSettings = SettingsService.Load();
            _alwaysOnTop = savedSettings.AlwaysOnTop;
            _selectedTabIndex = Math.Clamp(savedSettings.SelectedTabIndex, 0, 4);

            // 子VM初期化
            TrainingViewModel = new TrainingViewModel();
            HeelToeViewModel = new HeelToeViewModel();
            HeelToeViewModel.ApplySettings(savedSettings);

            ThrottleTrackingViewModel = new ThrottleTrackingViewModel();
            ThrottleTrackingViewModel.ApplySettings(savedSettings);

            HistoryViewModel = new HistoryViewModel();
            SettingsViewModel = new SettingsViewModel(_deviceManager, OnDeviceSelected, OnDeviceApplied);
            SettingsViewModel.LoadSettings(savedSettings);

            // UI更新タイマー (DispatcherPriority.Render 指定により非アクティブでも確実に動作)
            _uiTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _uiTimer.Tick += OnUiTick;
            _uiTimer.Start();

            // デバイス列挙 & 保存済みデバイスの復元
            SettingsViewModel.RefreshDevices();

            DeviceInfo? targetDevice = null;
            if (!string.IsNullOrEmpty(savedSettings.SelectedDeviceId))
            {
                targetDevice = SettingsViewModel.AvailableDevices.FirstOrDefault(d => d.Id == savedSettings.SelectedDeviceId);
            }

            if (targetDevice == null)
            {
                targetDevice = SettingsViewModel.AvailableDevices.FirstOrDefault(d => d.IsSteeringDevice || d.ApiType != "Synthetic")
                            ?? SettingsViewModel.AvailableDevices.FirstOrDefault();
            }

            if (targetDevice != null)
            {
                SettingsViewModel.SelectedDevice = targetDevice;
                _deviceManager.SelectDevice(targetDevice);
                if (_deviceManager.CurrentDevice != null)
                {
                    _deviceManager.CurrentDevice.PedalSettings = savedSettings.PedalSettings ?? new DevicePedalSettings();
                    StartSignalProcessing(_deviceManager.CurrentDevice);
                }
            }
        }

        private void OnDeviceSelected(DeviceInfo info)
        {
            SaveSettings();
        }

        private void OnDeviceApplied()
        {
            var device = _deviceManager.CurrentDevice;
            if (device == null) return;
            StartSignalProcessing(device);
            SaveSettings();
        }

        private void StartSignalProcessing(ISteeringDevice device)
        {
            _signalProcessor?.Stop();
            _signalProcessor?.Dispose();

            if (_subscribedDevice != null)
            {
                _subscribedDevice.ButtonPressed -= OnDeviceButtonPressed;
            }

            _signalProcessor = new SignalProcessor(device);

            // 設定値を反映
            var cfg = SettingsViewModel.Config;
            _signalProcessor.WheelRotation = cfg.WheelRotation;
            _signalProcessor.FilterStrength = cfg.FilterStrength;
            _signalProcessor.CenterOffset = cfg.CenterOffset;
            _signalProcessor.InvertInput = cfg.InvertInput;

            TrainingViewModel.SetSignalProcessor(_signalProcessor);
            HeelToeViewModel.SetSignalProcessor(_signalProcessor);
            ThrottleTrackingViewModel.SetSignalProcessor(_signalProcessor);

            // ハンコンボタンによる開始/停止イベント (非アクティブ時でも有効)
            _subscribedDevice = device;
            _subscribedDevice.ButtonPressed += OnDeviceButtonPressed;

            device.Start();
            _signalProcessor.Start();

            DeviceName = device.DeviceName;
            IsConnected = device.IsConnected;
            StatusMessage = $"接続完了: {device.DeviceName}";
        }

        private void OnDeviceButtonPressed(object? sender, int buttonIndex)
        {
            // ハンコンのボタンが押された際、非アクティブでも開始/停止をトグル
            App.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                ToggleStartStop();
            }));
        }

        private void OnUiTick(object? sender, EventArgs e)
        {
            if (_signalProcessor != null)
            {
                SamplingRateText = $"{_signalProcessor.ActualHz:F0} Hz";
                IsConnected = _deviceManager.CurrentDevice?.IsConnected ?? false;
            }
        }

        public void ToggleStartStop()
        {
            if (SelectedTabIndex == 0)
            {
                TrainingViewModel.ToggleStartStop();
            }
            else if (SelectedTabIndex == 1)
            {
                HeelToeViewModel.ToggleStartStop();
            }
            else if (SelectedTabIndex == 2)
            {
                ThrottleTrackingViewModel.ToggleStartStop();
            }
        }

        public void StopSession()
        {
            if (SelectedTabIndex == 0)
            {
                TrainingViewModel.StopSession();
            }
            else if (SelectedTabIndex == 1)
            {
                HeelToeViewModel.Stop();
            }
            else if (SelectedTabIndex == 2)
            {
                ThrottleTrackingViewModel.Stop();
            }
        }

        public void SaveSettings()
        {
            try
            {
                var settings = SettingsService.Load();
                settings.AlwaysOnTop = AlwaysOnTop;
                settings.SelectedTabIndex = SelectedTabIndex;
                SettingsViewModel.WriteToSettings(settings);
                HeelToeViewModel.WriteToSettings(settings);
                ThrottleTrackingViewModel.WriteToSettings(settings);
                SettingsService.Save(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveSettings failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                SaveSettings();
                _uiTimer.Stop();

                if (_subscribedDevice != null)
                {
                    _subscribedDevice.ButtonPressed -= OnDeviceButtonPressed;
                    _subscribedDevice = null;
                }

                _signalProcessor?.Stop();
                _signalProcessor?.Dispose();
                _deviceManager.Dispose();
                _disposed = true;
            }
        }
    }
}
