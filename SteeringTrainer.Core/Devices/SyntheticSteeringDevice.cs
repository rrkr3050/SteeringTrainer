using SteeringTrainer.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SteeringTrainer.Core.Devices
{
    /// <summary>
    /// テスト・開発用の合成デバイス。実ハンコンなしで動作確認できる。
    /// ステアリング正弦波およびペダル入力のシミュレーションに対応。
    /// </summary>
    public class SyntheticSteeringDevice : ISteeringDevice
    {
        private Thread? _thread;
        private volatile bool _running;
        private double _normalized;
        private double _throttleNormalized;
        private double _brakeNormalized;
        private double _clutchNormalized;
        private long _lastTicks;
        private readonly double _frequencyHz;
        private readonly double _amplitudeNormalized;
        private readonly double _periodSeconds;

        public string DeviceName => "Synthetic Device (Test)";
        public bool IsConnected => true;
        public double SteeringNormalized => _normalized;
        public double ThrottleNormalized => _throttleNormalized;
        public double BrakeNormalized => _brakeNormalized;
        public double ClutchNormalized => _clutchNormalized;

        public uint RawX => (uint)((_normalized + 1.0) / 2.0 * 65535.0);
        public uint RawY => (uint)((1.0 - _throttleNormalized) * 65535.0);
        public uint RawZ => (uint)((1.0 - _clutchNormalized) * 65535.0);
        public uint RawR => (uint)((1.0 - _brakeNormalized) * 65535.0);
        public uint RawU => 32767;
        public uint RawV => 32767;

        public uint RawButtons => 0;
        public event EventHandler<int>? ButtonPressed;

        /// <summary>テスト用: ボタン押下イベントをシミュレート</summary>
        public void SimulateButtonPress(int button) => ButtonPressed?.Invoke(this, button);

        public DevicePedalSettings PedalSettings { get; set; } = new();

        public long LastTimestampTicks => _lastTicks;

        public event EventHandler<bool>? ConnectionChanged;

        /// <summary>
        /// 外部テストから直接ペダル値を注入可能
        /// </summary>
        public void SetPedals(double brake, double throttle, double clutch = 0.0)
        {
            _brakeNormalized = Math.Clamp(brake, 0.0, 1.0);
            _throttleNormalized = Math.Clamp(throttle, 0.0, 1.0);
            _clutchNormalized = Math.Clamp(clutch, 0.0, 1.0);
            _lastTicks = Stopwatch.GetTimestamp();
        }

        public SyntheticSteeringDevice(
            double frequencyHz = 500.0,
            double amplitudeNormalized = 1.0,
            double periodSeconds = 5.0)
        {
            _frequencyHz = frequencyHz;
            _amplitudeNormalized = amplitudeNormalized;
            _periodSeconds = periodSeconds;
        }

        public IReadOnlyList<DeviceInfo> EnumerateDevices()
        {
            return new List<DeviceInfo>
            {
                new DeviceInfo
                {
                    Id = "synthetic",
                    Name = DeviceName,
                    ApiType = "Synthetic",
                    AxisCount = 3,
                    IsSteeringDevice = true
                }
            };
        }

        public bool SelectDevice(string deviceId)
        {
            return deviceId == "synthetic";
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            ConnectionChanged?.Invoke(this, true);
            _thread = new Thread(Loop) { IsBackground = true, Name = "SyntheticDevice" };
            _thread.Start();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            ConnectionChanged?.Invoke(this, false);
            _thread?.Join(500);
            _thread = null;
        }

        private void Loop()
        {
            var sw = Stopwatch.StartNew();
            var intervalMs = (int)(1000.0 / _frequencyHz);

            while (_running)
            {
                var t = sw.Elapsed.TotalSeconds;
                _normalized = _amplitudeNormalized * Math.Sin(2 * Math.PI * t / _periodSeconds);
                _lastTicks = Stopwatch.GetTimestamp();

                Thread.Sleep(Math.Max(1, intervalMs));
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
