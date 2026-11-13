using SteeringTrainer.Core.Devices;
using SteeringTrainer.Core.Models;
using SteeringTrainer.Core.Signal.Filters;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace SteeringTrainer.Core.Signal
{
    /// <summary>
    /// デバイスからステアリングおよびペダル入力を高頻度サンプリングし、
    /// フィルタ・速度・加速度を計算する。UIスレッドとは独立したスレッドで動作する。
    /// </summary>
    public class SignalProcessor : IDisposable
    {
        private readonly ISteeringDevice _device;
        private readonly EmaFilter _angleFilter;
        private readonly EmaFilter _velocityFilter;
        private readonly EmaFilter _throttleFilter;
        private readonly EmaFilter _brakeFilter;
        private Thread? _thread;
        private volatile bool _running;
        private long _prevTicks;
        private double _prevFilteredAngle;
        private double _prevVelocity;
        private bool _firstSample = true;

        private readonly ConcurrentQueue<SteeringSample> _outputQueue = new();

        public ISteeringDevice Device => _device;

        public double WheelRotation { get; set; } = 900.0;
        public double FilterStrength
        {
            get => _angleFilter.Alpha;
            set
            {
                _angleFilter.Alpha = value;
                _velocityFilter.Alpha = value * 0.5;
            }
        }
        public double CenterOffset { get; set; } = 0.0;
        public bool InvertInput { get; set; } = false;

        public int TargetHz { get; set; } = 500;

        /// <summary>最新サンプル（スレッド外から参照用）</summary>
        public SteeringSample? LatestSample { get; private set; }

        /// <summary>実測サンプリングレート (Hz)</summary>
        public double ActualHz { get; private set; }

        public event Action<SteeringSample>? SampleProduced;

        public SignalProcessor(ISteeringDevice device)
        {
            _device = device;
            _angleFilter = new EmaFilter(0.1);
            _velocityFilter = new EmaFilter(0.05);
            _throttleFilter = new EmaFilter(0.02);
            _brakeFilter = new EmaFilter(0.02);
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _firstSample = true;
            _angleFilter.Reset();
            _velocityFilter.Reset();
            _throttleFilter.Reset();
            _brakeFilter.Reset();
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "SignalProcessor",
                Priority = ThreadPriority.AboveNormal
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(1000);
            _thread = null;
        }

        private void Loop()
        {
            var intervalTicks = (long)(Stopwatch.Frequency / (double)TargetHz);
            var fpsTimer = Stopwatch.StartNew();
            int frameCount = 0;

            while (_running)
            {
                var startTicks = Stopwatch.GetTimestamp();

                ProcessSample(startTicks);
                frameCount++;

                // 実測Hz計算
                if (fpsTimer.Elapsed.TotalSeconds >= 1.0)
                {
                    ActualHz = frameCount / fpsTimer.Elapsed.TotalSeconds;
                    frameCount = 0;
                    fpsTimer.Restart();
                }

                // 次サンプルまでスピンウェイト
                var nextTicks = startTicks + intervalTicks;
                while (Stopwatch.GetTimestamp() < nextTicks)
                {
                    Thread.SpinWait(100);
                }
            }
        }

        private void ProcessSample(long ticks)
        {
            if (!_device.IsConnected) return;

            var rawNorm = _device.SteeringNormalized;
            if (InvertInput) rawNorm = -rawNorm;

            // 角度変換
            var rawAngle = rawNorm * WheelRotation / 2.0 - CenterOffset;

            double dt = 0;
            if (!_firstSample && _prevTicks > 0)
            {
                dt = (double)(ticks - _prevTicks) / Stopwatch.Frequency;
            }

            // フィルタ適用
            var filteredAngle = _angleFilter.Filter(rawAngle, dt);
            var filteredNorm = filteredAngle / (WheelRotation / 2.0);

            // 角速度
            double velocity = 0;
            if (!_firstSample && dt > 1e-9)
            {
                velocity = (filteredAngle - _prevFilteredAngle) / dt;
            }
            var filteredVelocity = _velocityFilter.Filter(velocity, dt);

            // 角加速度
            double acceleration = 0;
            if (!_firstSample && dt > 1e-9)
            {
                acceleration = (filteredVelocity - _prevVelocity) / dt;
            }
            var absAcceleration = Math.Abs(acceleration);

            // ペダル値取得 & 低遅延フィルタ
            var rawThrottle = _device.ThrottleNormalized;
            var rawBrake = _device.BrakeNormalized;
            var rawClutch = _device.ClutchNormalized;

            var filteredThrottle = _throttleFilter.Filter(rawThrottle, dt);
            var filteredBrake = _brakeFilter.Filter(rawBrake, dt);
            var filteredClutch = rawClutch;

            var sample = new SteeringSample(
                ticks,
                rawNorm,
                filteredNorm,
                filteredAngle,
                rawAngle,
                filteredVelocity,
                acceleration,
                acceleration,
                absAcceleration,
                filteredThrottle,
                filteredBrake,
                filteredClutch,
                rawThrottle,
                rawBrake,
                rawClutch,
                _device.RawX,
                _device.RawY,
                _device.RawZ,
                _device.RawR,
                _device.RawU,
                _device.RawV
            );

            _prevTicks = ticks;
            _prevFilteredAngle = filteredAngle;
            _prevVelocity = filteredVelocity;
            _firstSample = false;

            LatestSample = sample;
            _outputQueue.Enqueue(sample);
            SampleProduced?.Invoke(sample);
        }

        /// <summary>キューからサンプルを消費する</summary>
        public bool TryDequeue(out SteeringSample sample)
        {
            return _outputQueue.TryDequeue(out sample!);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
