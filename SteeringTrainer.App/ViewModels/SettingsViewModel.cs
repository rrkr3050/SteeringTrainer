using SteeringTrainer.Core.Devices;
using SteeringTrainer.Core.Models;
using SteeringTrainer.Infrastructure.Persistence;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;

namespace SteeringTrainer.App.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly DeviceManager _deviceManager;
        private readonly Action<DeviceInfo> _onDeviceSelected;
        private readonly Action _onDeviceApplied;
        private readonly DispatcherTimer _monitorTimer;

        public ObservableCollection<DeviceInfo> AvailableDevices { get; } = new();

        private DeviceInfo? _selectedDevice;
        public DeviceInfo? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (Set(ref _selectedDevice, value))
                {
                    SaveCurrentSettings();
                }
            }
        }

        public TrainingConfig Config { get; } = new TrainingConfig();
        private DevicePedalSettings _pedalSettings = new();
        public DevicePedalSettings PedalSettings
        {
            get => _deviceManager.CurrentDevice?.PedalSettings ?? _pedalSettings;
            set
            {
                _pedalSettings = value;
                if (_deviceManager.CurrentDevice != null)
                {
                    _deviceManager.CurrentDevice.PedalSettings = value;
                }
                OnPropertyChanged();
                NotifyAllPedalProperties();
            }
        }

        public JoystickAxis[] PedalAxisOptions { get; } = (JoystickAxis[])Enum.GetValues(typeof(JoystickAxis));

        // ホイール回転角度選択肢 (GTFPは900°と200°対応、設定は200, 360, 540, 720, 900, 1080)
        public double[] WheelRotationOptions { get; } = { 200, 360, 540, 720, 900, 1080 };
        private double _selectedWheelRotation = 900;
        public double SelectedWheelRotation
        {
            get => _selectedWheelRotation;
            set
            {
                if (Set(ref _selectedWheelRotation, value))
                {
                    Config.WheelRotation = value;
                    SaveCurrentSettings();
                }
            }
        }

        // フィルタ強度 (0-50%)
        private int _filterStrengthPercent = 10;
        public int FilterStrengthPercent
        {
            get => _filterStrengthPercent;
            set
            {
                if (Set(ref _filterStrengthPercent, value))
                {
                    Config.FilterStrength = value / 100.0;
                    SaveCurrentSettings();
                }
            }
        }

        private bool _invertInput;
        public bool InvertInput
        {
            get => _invertInput;
            set
            {
                if (Set(ref _invertInput, value))
                {
                    Config.InvertInput = value;
                    SaveCurrentSettings();
                }
            }
        }

        private double _centerOffset;
        public double CenterOffset
        {
            get => _centerOffset;
            set
            {
                if (Set(ref _centerOffset, value))
                {
                    Config.CenterOffset = value;
                    SaveCurrentSettings();
                }
            }
        }

        // --- リアルタイムRaw軸モニター (校正用) ---
        private uint _rawX;
        public uint RawX { get => _rawX; private set => Set(ref _rawX, value); }
        private uint _rawY;
        public uint RawY { get => _rawY; private set => Set(ref _rawY, value); }
        private uint _rawZ;
        public uint RawZ { get => _rawZ; private set => Set(ref _rawZ, value); }
        private uint _rawR;
        public uint RawR { get => _rawR; private set => Set(ref _rawR, value); }
        private uint _rawU;
        public uint RawU { get => _rawU; private set => Set(ref _rawU, value); }
        private uint _rawV;
        public uint RawV { get => _rawV; private set => Set(ref _rawV, value); }

        private double _liveThrottlePercent;
        public double LiveThrottlePercent { get => _liveThrottlePercent; private set => Set(ref _liveThrottlePercent, value); }

        private double _liveBrakePercent;
        public double LiveBrakePercent { get => _liveBrakePercent; private set => Set(ref _liveBrakePercent, value); }

        private double _liveClutchPercent;
        public double LiveClutchPercent { get => _liveClutchPercent; private set => Set(ref _liveClutchPercent, value); }

        // --- BRAKE キャリブレーション プロパティ ---
        private uint _brakeCurrentRaw;
        public uint BrakeCurrentRaw { get => _brakeCurrentRaw; private set => Set(ref _brakeCurrentRaw, value); }

        public JoystickAxis BrakeAxis
        {
            get => PedalSettings.Brake.AssignedAxis;
            set
            {
                if (PedalSettings.Brake.AssignedAxis != value)
                {
                    PedalSettings.Brake.AssignedAxis = value;
                    OnPropertyChanged();
                    SaveCurrentSettings();
                }
            }
        }

        public uint BrakeRaw0
        {
            get => PedalSettings.Brake.Raw0Percent;
            set
            {
                if (PedalSettings.Brake.Raw0Percent != value)
                {
                    PedalSettings.Brake.Raw0Percent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BrakeInvert));
                    SaveCurrentSettings();
                }
            }
        }

        public uint BrakeRaw100
        {
            get => PedalSettings.Brake.Raw100Percent;
            set
            {
                if (PedalSettings.Brake.Raw100Percent != value)
                {
                    PedalSettings.Brake.Raw100Percent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BrakeInvert));
                    SaveCurrentSettings();
                }
            }
        }

        public double BrakeDeadzone
        {
            get => PedalSettings.Brake.DeadzonePercent;
            set
            {
                var v = Math.Clamp(Math.Round(value), 0, 30);
                if (Math.Abs(PedalSettings.Brake.DeadzonePercent - v) > 0.01)
                {
                    PedalSettings.Brake.DeadzonePercent = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BrakeDeadzoneText));
                    SaveCurrentSettings();
                }
            }
        }
        public string BrakeDeadzoneText => $"{BrakeDeadzone:F0}%";

        public double BrakeMaxDeadzone
        {
            get => PedalSettings.Brake.MaxDeadzonePercent;
            set
            {
                var v = Math.Clamp(Math.Round(value), 0, 30);
                if (Math.Abs(PedalSettings.Brake.MaxDeadzonePercent - v) > 0.01)
                {
                    PedalSettings.Brake.MaxDeadzonePercent = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BrakeMaxDeadzoneText));
                    SaveCurrentSettings();
                }
            }
        }
        public string BrakeMaxDeadzoneText => $"{BrakeMaxDeadzone:F0}%";

        public double BrakeGamma
        {
            get => PedalSettings.Brake.Gamma;
            set
            {
                var v = Math.Clamp(Math.Round(value, 1), 0.5, 3.0);
                if (Math.Abs(PedalSettings.Brake.Gamma - v) > 0.01)
                {
                    PedalSettings.Brake.Gamma = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BrakeGammaText));
                    SaveCurrentSettings();
                }
            }
        }
        public string BrakeGammaText => $"{BrakeGamma:F1} ({GetGammaDescription(BrakeGamma)})";

        public bool BrakeInvert
        {
            get => PedalSettings.Brake.Invert;
            set
            {
                if (PedalSettings.Brake.Invert != value)
                {
                    PedalSettings.Brake.Invert = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BrakeRaw0));
                    OnPropertyChanged(nameof(BrakeRaw100));
                    SaveCurrentSettings();
                }
            }
        }

        // --- THROTTLE キャリブレーション プロパティ ---
        private uint _throttleCurrentRaw;
        public uint ThrottleCurrentRaw { get => _throttleCurrentRaw; private set => Set(ref _throttleCurrentRaw, value); }

        public JoystickAxis ThrottleAxis
        {
            get => PedalSettings.Throttle.AssignedAxis;
            set
            {
                if (PedalSettings.Throttle.AssignedAxis != value)
                {
                    PedalSettings.Throttle.AssignedAxis = value;
                    OnPropertyChanged();
                    SaveCurrentSettings();
                }
            }
        }

        public uint ThrottleRaw0
        {
            get => PedalSettings.Throttle.Raw0Percent;
            set
            {
                if (PedalSettings.Throttle.Raw0Percent != value)
                {
                    PedalSettings.Throttle.Raw0Percent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThrottleInvert));
                    SaveCurrentSettings();
                }
            }
        }

        public uint ThrottleRaw100
        {
            get => PedalSettings.Throttle.Raw100Percent;
            set
            {
                if (PedalSettings.Throttle.Raw100Percent != value)
                {
                    PedalSettings.Throttle.Raw100Percent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThrottleInvert));
                    SaveCurrentSettings();
                }
            }
        }

        public double ThrottleDeadzone
        {
            get => PedalSettings.Throttle.DeadzonePercent;
            set
            {
                var v = Math.Clamp(Math.Round(value), 0, 30);
                if (Math.Abs(PedalSettings.Throttle.DeadzonePercent - v) > 0.01)
                {
                    PedalSettings.Throttle.DeadzonePercent = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThrottleDeadzoneText));
                    SaveCurrentSettings();
                }
            }
        }
        public string ThrottleDeadzoneText => $"{ThrottleDeadzone:F0}%";

        public double ThrottleMaxDeadzone
        {
            get => PedalSettings.Throttle.MaxDeadzonePercent;
            set
            {
                var v = Math.Clamp(Math.Round(value), 0, 30);
                if (Math.Abs(PedalSettings.Throttle.MaxDeadzonePercent - v) > 0.01)
                {
                    PedalSettings.Throttle.MaxDeadzonePercent = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThrottleMaxDeadzoneText));
                    SaveCurrentSettings();
                }
            }
        }
        public string ThrottleMaxDeadzoneText => $"{ThrottleMaxDeadzone:F0}%";

        public double ThrottleGamma
        {
            get => PedalSettings.Throttle.Gamma;
            set
            {
                var v = Math.Clamp(Math.Round(value, 1), 0.5, 3.0);
                if (Math.Abs(PedalSettings.Throttle.Gamma - v) > 0.01)
                {
                    PedalSettings.Throttle.Gamma = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThrottleGammaText));
                    SaveCurrentSettings();
                }
            }
        }
        public string ThrottleGammaText => $"{ThrottleGamma:F1} ({GetGammaDescription(ThrottleGamma)})";

        public bool ThrottleInvert
        {
            get => PedalSettings.Throttle.Invert;
            set
            {
                if (PedalSettings.Throttle.Invert != value)
                {
                    PedalSettings.Throttle.Invert = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThrottleRaw0));
                    OnPropertyChanged(nameof(ThrottleRaw100));
                    SaveCurrentSettings();
                }
            }
        }

        // --- CLUTCH キャリブレーション プロパティ ---
        private uint _clutchCurrentRaw;
        public uint ClutchCurrentRaw { get => _clutchCurrentRaw; private set => Set(ref _clutchCurrentRaw, value); }

        public JoystickAxis ClutchAxis
        {
            get => PedalSettings.Clutch.AssignedAxis;
            set
            {
                if (PedalSettings.Clutch.AssignedAxis != value)
                {
                    PedalSettings.Clutch.AssignedAxis = value;
                    OnPropertyChanged();
                    SaveCurrentSettings();
                }
            }
        }

        public uint ClutchRaw0
        {
            get => PedalSettings.Clutch.Raw0Percent;
            set
            {
                if (PedalSettings.Clutch.Raw0Percent != value)
                {
                    PedalSettings.Clutch.Raw0Percent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClutchInvert));
                    SaveCurrentSettings();
                }
            }
        }

        public uint ClutchRaw100
        {
            get => PedalSettings.Clutch.Raw100Percent;
            set
            {
                if (PedalSettings.Clutch.Raw100Percent != value)
                {
                    PedalSettings.Clutch.Raw100Percent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClutchInvert));
                    SaveCurrentSettings();
                }
            }
        }

        public double ClutchDeadzone
        {
            get => PedalSettings.Clutch.DeadzonePercent;
            set
            {
                var v = Math.Clamp(Math.Round(value), 0, 30);
                if (Math.Abs(PedalSettings.Clutch.DeadzonePercent - v) > 0.01)
                {
                    PedalSettings.Clutch.DeadzonePercent = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClutchDeadzoneText));
                    SaveCurrentSettings();
                }
            }
        }
        public string ClutchDeadzoneText => $"{ClutchDeadzone:F0}%";

        public double ClutchMaxDeadzone
        {
            get => PedalSettings.Clutch.MaxDeadzonePercent;
            set
            {
                var v = Math.Clamp(Math.Round(value), 0, 30);
                if (Math.Abs(PedalSettings.Clutch.MaxDeadzonePercent - v) > 0.01)
                {
                    PedalSettings.Clutch.MaxDeadzonePercent = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClutchMaxDeadzoneText));
                    SaveCurrentSettings();
                }
            }
        }
        public string ClutchMaxDeadzoneText => $"{ClutchMaxDeadzone:F0}%";

        public double ClutchGamma
        {
            get => PedalSettings.Clutch.Gamma;
            set
            {
                var v = Math.Clamp(Math.Round(value, 1), 0.5, 3.0);
                if (Math.Abs(PedalSettings.Clutch.Gamma - v) > 0.01)
                {
                    PedalSettings.Clutch.Gamma = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClutchGammaText));
                    SaveCurrentSettings();
                }
            }
        }
        public string ClutchGammaText => $"{ClutchGamma:F1} ({GetGammaDescription(ClutchGamma)})";

        public bool ClutchInvert
        {
            get => PedalSettings.Clutch.Invert;
            set
            {
                if (PedalSettings.Clutch.Invert != value)
                {
                    PedalSettings.Clutch.Invert = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClutchRaw0));
                    OnPropertyChanged(nameof(ClutchRaw100));
                    SaveCurrentSettings();
                }
            }
        }

        private string _saveStatusText = "設定は自動保存されます";
        public string SaveStatusText { get => _saveStatusText; set => Set(ref _saveStatusText, value); }

        private string? _savedDeviceId;

        public ICommand RefreshDevicesCommand { get; }
        public ICommand ApplyDeviceCommand { get; }
        public ICommand SaveSettingsCommand { get; }

        public ICommand SetBrake0PercentCommand { get; }
        public ICommand SetBrake100PercentCommand { get; }
        public ICommand SetThrottle0PercentCommand { get; }
        public ICommand SetThrottle100PercentCommand { get; }
        public ICommand SetClutch0PercentCommand { get; }
        public ICommand SetClutch100PercentCommand { get; }

        public SettingsViewModel(DeviceManager deviceManager, Action<DeviceInfo> onDeviceSelected, Action onDeviceApplied)
        {
            _deviceManager = deviceManager;
            _onDeviceSelected = onDeviceSelected;
            _onDeviceApplied = onDeviceApplied;

            RefreshDevicesCommand = new RelayCommand(RefreshDevices);
            ApplyDeviceCommand = new RelayCommand(ApplyDevice, () => SelectedDevice != null);
            SaveSettingsCommand = new RelayCommand(SaveCurrentSettings);

            SetBrake0PercentCommand = new RelayCommand(() => BrakeRaw0 = BrakeCurrentRaw);
            SetBrake100PercentCommand = new RelayCommand(() => BrakeRaw100 = BrakeCurrentRaw);
            SetThrottle0PercentCommand = new RelayCommand(() => ThrottleRaw0 = ThrottleCurrentRaw);
            SetThrottle100PercentCommand = new RelayCommand(() => ThrottleRaw100 = ThrottleCurrentRaw);
            SetClutch0PercentCommand = new RelayCommand(() => ClutchRaw0 = ClutchCurrentRaw);
            SetClutch100PercentCommand = new RelayCommand(() => ClutchRaw100 = ClutchCurrentRaw);

            // モニター用タイマー (非アクティブ時も動くよう Render 優先度)
            _monitorTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _monitorTimer.Tick += OnMonitorTick;
            _monitorTimer.Start();
        }

        public void LoadSettings(AppSettings settings)
        {
            _savedDeviceId = settings.SelectedDeviceId;
            _selectedWheelRotation = settings.WheelRotation;
            Config.WheelRotation = settings.WheelRotation;
            _centerOffset = settings.CenterOffset;
            Config.CenterOffset = settings.CenterOffset;
            _invertInput = settings.InvertInput;
            Config.InvertInput = settings.InvertInput;
            _filterStrengthPercent = settings.FilterStrengthPercent;
            Config.FilterStrength = settings.FilterStrengthPercent / 100.0;

            if (settings.PedalSettings != null)
            {
                _pedalSettings = settings.PedalSettings;
                if (_deviceManager.CurrentDevice != null)
                {
                    _deviceManager.CurrentDevice.PedalSettings = settings.PedalSettings;
                }
            }

            OnPropertyChanged(nameof(SelectedWheelRotation));
            OnPropertyChanged(nameof(CenterOffset));
            OnPropertyChanged(nameof(InvertInput));
            OnPropertyChanged(nameof(FilterStrengthPercent));
            OnPropertyChanged(nameof(PedalSettings));
            NotifyAllPedalProperties();
        }

        public void WriteToSettings(AppSettings settings)
        {
            settings.SelectedDeviceId = SelectedDevice?.Id;
            settings.SelectedDeviceName = SelectedDevice?.Name;
            settings.WheelRotation = SelectedWheelRotation;
            settings.CenterOffset = CenterOffset;
            settings.InvertInput = InvertInput;
            settings.FilterStrengthPercent = FilterStrengthPercent;
            settings.PedalSettings = PedalSettings;
        }

        public void SaveCurrentSettings()
        {
            var settings = SettingsService.Load();
            WriteToSettings(settings);
            SettingsService.Save(settings);
            SaveStatusText = $"設定を保存しました ({DateTime.Now:HH:mm:ss})";
        }

        public void RefreshDevices()
        {
            AvailableDevices.Clear();
            var devices = _deviceManager.EnumerateAllDevices();
            foreach (var d in devices)
            {
                AvailableDevices.Add(d);
            }

            // 保存済みIDと一致するデバイスがあればそれを最優先
            if (!string.IsNullOrEmpty(_savedDeviceId))
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.Id == _savedDeviceId);
            }

            // 見つからなければ実機優先、それもなければSynthetic
            if (SelectedDevice == null)
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IsSteeringDevice || d.ApiType != "Synthetic")
                              ?? AvailableDevices.FirstOrDefault();
            }
        }

        public void ApplyDevice()
        {
            if (SelectedDevice == null) return;
            _savedDeviceId = SelectedDevice.Id;
            _deviceManager.SelectDevice(SelectedDevice);
            if (_deviceManager.CurrentDevice != null)
            {
                _deviceManager.CurrentDevice.PedalSettings = _pedalSettings;
            }
            OnPropertyChanged(nameof(PedalSettings));
            NotifyAllPedalProperties();
            _onDeviceSelected(SelectedDevice);
            _onDeviceApplied();
            SaveCurrentSettings();
            SaveStatusText = $"デバイスを適用・保存しました: {SelectedDevice.Name} ({DateTime.Now:HH:mm:ss})";
        }

        private void OnMonitorTick(object? sender, EventArgs e)
        {
            var device = _deviceManager.CurrentDevice;
            if (device != null && device.IsConnected)
            {
                RawX = device.RawX;
                RawY = device.RawY;
                RawZ = device.RawZ;
                RawR = device.RawR;
                RawU = device.RawU;
                RawV = device.RawV;
                LiveThrottlePercent = device.ThrottleNormalized * 100.0;
                LiveBrakePercent = device.BrakeNormalized * 100.0;
                LiveClutchPercent = device.ClutchNormalized * 100.0;

                BrakeCurrentRaw = GetCurrentRawForAxis(PedalSettings.Brake.AssignedAxis);
                ThrottleCurrentRaw = GetCurrentRawForAxis(PedalSettings.Throttle.AssignedAxis);
                ClutchCurrentRaw = GetCurrentRawForAxis(PedalSettings.Clutch.AssignedAxis);
            }
        }

        public uint GetCurrentRawForAxis(JoystickAxis axis) => axis switch
        {
            JoystickAxis.X => RawX,
            JoystickAxis.Y => RawY,
            JoystickAxis.Z => RawZ,
            JoystickAxis.R => RawR,
            JoystickAxis.U => RawU,
            JoystickAxis.V => RawV,
            _ => 0
        };

        private static string GetGammaDescription(double g)
        {
            if (Math.Abs(g - 1.0) < 0.05) return "リニア (標準)";
            if (g > 1.0) return "プログレッシブ (奥で強く効く・踏み切り急増防止)";
            return "ディグレッシブ (手前が敏感)";
        }

        private void NotifyAllPedalProperties()
        {
            OnPropertyChanged(nameof(BrakeAxis));
            OnPropertyChanged(nameof(BrakeRaw0));
            OnPropertyChanged(nameof(BrakeRaw100));
            OnPropertyChanged(nameof(BrakeDeadzone));
            OnPropertyChanged(nameof(BrakeDeadzoneText));
            OnPropertyChanged(nameof(BrakeMaxDeadzone));
            OnPropertyChanged(nameof(BrakeMaxDeadzoneText));
            OnPropertyChanged(nameof(BrakeGamma));
            OnPropertyChanged(nameof(BrakeGammaText));
            OnPropertyChanged(nameof(BrakeInvert));

            OnPropertyChanged(nameof(ThrottleAxis));
            OnPropertyChanged(nameof(ThrottleRaw0));
            OnPropertyChanged(nameof(ThrottleRaw100));
            OnPropertyChanged(nameof(ThrottleDeadzone));
            OnPropertyChanged(nameof(ThrottleDeadzoneText));
            OnPropertyChanged(nameof(ThrottleMaxDeadzone));
            OnPropertyChanged(nameof(ThrottleMaxDeadzoneText));
            OnPropertyChanged(nameof(ThrottleGamma));
            OnPropertyChanged(nameof(ThrottleGammaText));
            OnPropertyChanged(nameof(ThrottleInvert));

            OnPropertyChanged(nameof(ClutchAxis));
            OnPropertyChanged(nameof(ClutchRaw0));
            OnPropertyChanged(nameof(ClutchRaw100));
            OnPropertyChanged(nameof(ClutchDeadzone));
            OnPropertyChanged(nameof(ClutchDeadzoneText));
            OnPropertyChanged(nameof(ClutchMaxDeadzone));
            OnPropertyChanged(nameof(ClutchMaxDeadzoneText));
            OnPropertyChanged(nameof(ClutchGamma));
            OnPropertyChanged(nameof(ClutchGammaText));
            OnPropertyChanged(nameof(ClutchInvert));
        }
    }
}
