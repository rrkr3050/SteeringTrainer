using SteeringTrainer.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SteeringTrainer.Core.Devices
{
    /// <summary>
    /// デバイスの列挙・選択・管理を担当する
    /// </summary>
    public class DeviceManager : IDisposable
    {
        private ISteeringDevice? _currentDevice;
        private bool _disposed;

        public ISteeringDevice? CurrentDevice => _currentDevice;
        public bool IsDeviceSelected => _currentDevice != null;

        public event EventHandler<ISteeringDevice>? DeviceSelected;
        public event EventHandler? DeviceRemoved;

        public IReadOnlyList<DeviceInfo> EnumerateAllDevices()
        {
            var devices = new List<DeviceInfo>();

            // まずDirectInput/HIDデバイスを試みる
            try
            {
                var directInput = new DirectInputSteeringDevice();
                devices.AddRange(directInput.EnumerateDevices());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DirectInput enum failed: {ex.Message}");
            }

            // 常にSyntheticも追加
            var synthetic = new SyntheticSteeringDevice();
            var syntheticDevices = synthetic.EnumerateDevices();
            foreach (var d in syntheticDevices)
            {
                if (!devices.Any(x => x.Id == d.Id))
                    devices.Add(d);
            }

            return devices;
        }

        public bool SelectDevice(DeviceInfo info)
        {
            if (_currentDevice != null)
            {
                _currentDevice.Stop();
                _currentDevice.Dispose();
                _currentDevice = null;
                DeviceRemoved?.Invoke(this, EventArgs.Empty);
            }

            ISteeringDevice device;
            if (info.ApiType == "Synthetic")
            {
                device = new SyntheticSteeringDevice();
            }
            else
            {
                device = new DirectInputSteeringDevice();
            }

            if (device.SelectDevice(info.Id))
            {
                _currentDevice = device;
                DeviceSelected?.Invoke(this, device);
                return true;
            }

            device.Dispose();
            return false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _currentDevice?.Stop();
                _currentDevice?.Dispose();
                _disposed = true;
            }
        }
    }
}
