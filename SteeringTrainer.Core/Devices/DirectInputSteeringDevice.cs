using SteeringTrainer.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace SteeringTrainer.Core.Devices
{
    /// <summary>
    /// Windows Multimedia (WinMM / DirectInput準拠) によるステアリングおよびペダルデバイス実装。
    /// Logicool GTFP (Driving Force Pro) をはじめ、DirectInput対応のすべてのハンコン・ペダルに対応。
    /// winmm.dllのjoyGetPosExを高頻度でポーリングし、低遅延・高精度でステアリング・ブレーキ・アクセル入力を取得する。
    /// </summary>
    public class DirectInputSteeringDevice : ISteeringDevice
    {
        #region WinMM P/Invoke

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct JOYCAPS
        {
            public ushort wMid;
            public ushort wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint wXmin;
            public uint wXmax;
            public uint wYmin;
            public uint wYmax;
            public uint wZmin;
            public uint wZmax;
            public uint wNumButtons;
            public uint wPeriodMin;
            public uint wPeriodMax;
            public uint wRmin;
            public uint wRmax;
            public uint wUmin;
            public uint wUmax;
            public uint wVmin;
            public uint wVmax;
            public uint wCaps;
            public uint wMaxAxes;
            public uint wNumAxes;
            public uint wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szOEMVxD;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOYINFOEX
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwXpos;
            public uint dwYpos;
            public uint dwZpos;
            public uint dwRpos;
            public uint dwUpos;
            public uint dwVpos;
            public uint dwButtons;
            public uint dwButtonNumber;
            public uint dwPOV;
            public uint dwReserved1;
            public uint dwReserved2;
        }

        private const uint JOY_RETURNX = 0x00000001;
        private const uint JOY_RETURNY = 0x00000002;
        private const uint JOY_RETURNZ = 0x00000004;
        private const uint JOY_RETURNR = 0x00000008;
        private const uint JOY_RETURNU = 0x00000010;
        private const uint JOY_RETURNV = 0x00000020;
        private const uint JOY_RETURNPOV = 0x00000040;
        private const uint JOY_RETURNBUTTONS = 0x00000080;
        private const uint JOY_RETURNALL = (JOY_RETURNX | JOY_RETURNY | JOY_RETURNZ | JOY_RETURNR | JOY_RETURNU | JOY_RETURNV | JOY_RETURNPOV | JOY_RETURNBUTTONS);

        private const uint JOYERR_NOERROR = 0;
        private const uint JOYERR_UNPLUGGED = 167;

        [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint joyGetDevCaps(uint uJoyID, out JOYCAPS pjc, uint cbjc);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);

        [DllImport("winmm.dll")]
        private static extern uint joyGetNumDevs();

        #endregion

        private uint _selectedJoyId = 0;
        private uint _minX = 0;
        private uint _maxX = 65535;
        private Thread? _pollingThread;
        private volatile bool _running;
        private double _steeringNormalized;
        private double _throttleNormalized;
        private double _brakeNormalized;
        private double _clutchNormalized;

        private uint _rawX;
        private uint _rawY;
        private uint _rawZ;
        private uint _rawR;
        private uint _rawU;
        private uint _rawV;
        private uint _rawButtons;
        private uint _prevButtons;

        private long _lastTicks;
        private bool _isConnected;
        private string _deviceName = "Logicool Driving Force Pro (GTFP)";
        private bool _disposed;

        public string DeviceName => _deviceName;
        public bool IsConnected => _isConnected;
        public double SteeringNormalized => _steeringNormalized;
        public double ThrottleNormalized => _throttleNormalized;
        public double BrakeNormalized => _brakeNormalized;
        public double ClutchNormalized => _clutchNormalized;

        public uint RawX => _rawX;
        public uint RawY => _rawY;
        public uint RawZ => _rawZ;
        public uint RawR => _rawR;
        public uint RawU => _rawU;
        public uint RawV => _rawV;
        public uint RawButtons => _rawButtons;

        public DevicePedalSettings PedalSettings { get; set; } = new();

        public long LastTimestampTicks => _lastTicks;

        public event EventHandler<bool>? ConnectionChanged;
        public event EventHandler<int>? ButtonPressed;

        public DirectInputSteeringDevice()
        {
        }

        public IReadOnlyList<DeviceInfo> EnumerateDevices()
        {
            var list = new List<DeviceInfo>();
            uint count = joyGetNumDevs();

            for (uint i = 0; i < count; i++)
            {
                JOYCAPS caps;
                uint res = joyGetDevCaps(i, out caps, (uint)Marshal.SizeOf(typeof(JOYCAPS)));
                if (res == JOYERR_NOERROR)
                {
                    string name = caps.szPname;
                    if (string.IsNullOrWhiteSpace(name) || name.Contains("PC"))
                    {
                        name = $"DirectInput Device #{i} (Driving Force Pro / Game Controller)";
                    }

                    list.Add(new DeviceInfo
                    {
                        Id = $"winmm_{i}",
                        Name = name,
                        ApiType = "DirectInput/WinMM",
                        AxisCount = (int)caps.wNumAxes,
                        IsSteeringDevice = caps.wNumAxes >= 1
                    });
                }
            }

            return list;
        }

        public bool SelectDevice(string deviceId)
        {
            if (deviceId.StartsWith("winmm_"))
            {
                if (uint.TryParse(deviceId.Substring("winmm_".Length), out uint id))
                {
                    _selectedJoyId = id;
                    JOYCAPS caps;
                    uint res = joyGetDevCaps(id, out caps, (uint)Marshal.SizeOf(typeof(JOYCAPS)));
                    if (res == JOYERR_NOERROR)
                    {
                        _minX = caps.wXmin;
                        _maxX = caps.wXmax > caps.wXmin ? caps.wXmax : 65535;
                        _deviceName = string.IsNullOrWhiteSpace(caps.szPname)
                            ? $"Driving Force Pro (ID #{id})"
                            : caps.szPname;
                        _isConnected = true;
                        return true;
                    }
                }
            }

            return false;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _pollingThread = new Thread(PollingLoop)
            {
                IsBackground = true,
                Name = "DirectInputPollingThread",
                Priority = ThreadPriority.AboveNormal
            };
            _pollingThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _pollingThread?.Join(500);
            _pollingThread = null;
        }

        private void PollingLoop()
        {
            var info = new JOYINFOEX();

            while (_running)
            {
                info.dwSize = (uint)Marshal.SizeOf(typeof(JOYINFOEX));
                info.dwFlags = JOY_RETURNALL;

                uint res = joyGetPosEx(_selectedJoyId, ref info);
                long ticks = Stopwatch.GetTimestamp();

                if (res == JOYERR_NOERROR)
                {
                    if (!_isConnected)
                    {
                        _isConnected = true;
                        ConnectionChanged?.Invoke(this, true);
                    }

                    _rawX = info.dwXpos;
                    _rawY = info.dwYpos;
                    _rawZ = info.dwZpos;
                    _rawR = info.dwRpos;
                    _rawU = info.dwUpos;
                    _rawV = info.dwVpos;

                    // ボタン入力の検知 (ビットマスク差分)
                    uint currentButtons = info.dwButtons;
                    _rawButtons = currentButtons;
                    uint newlyPressed = currentButtons & ~_prevButtons;
                    if (newlyPressed != 0)
                    {
                        for (int b = 0; b < 32; b++)
                        {
                            if ((newlyPressed & (1u << b)) != 0)
                            {
                                ButtonPressed?.Invoke(this, b);
                            }
                        }
                    }
                    _prevButtons = currentButtons;

                    // ステアリング X軸 (0..65535) を -1.0 .. +1.0 に正規化
                    double range = _maxX - _minX;
                    if (range > 0)
                    {
                        double val = (info.dwXpos - _minX) / range; // 0.0 .. 1.0
                        _steeringNormalized = Math.Clamp(val * 2.0 - 1.0, -1.0, 1.0);
                    }

                    // ペダル軸 (0.0 .. 1.0) の正規化
                    _throttleNormalized = PedalSettings.Throttle.Normalize(GetAxisValue(ref info, PedalSettings.Throttle.AssignedAxis));
                    _brakeNormalized = PedalSettings.Brake.Normalize(GetAxisValue(ref info, PedalSettings.Brake.AssignedAxis));
                    _clutchNormalized = PedalSettings.Clutch.Normalize(GetAxisValue(ref info, PedalSettings.Clutch.AssignedAxis));

                    _lastTicks = ticks;
                }
                else
                {
                    if (_isConnected)
                    {
                        _isConnected = false;
                        ConnectionChanged?.Invoke(this, false);
                    }
                }

                // 500Hzポーリング (1msウェイト)
                Thread.Sleep(1);
            }
        }

        private static uint GetAxisValue(ref JOYINFOEX info, JoystickAxis axis)
        {
            return axis switch
            {
                JoystickAxis.X => info.dwXpos,
                JoystickAxis.Y => info.dwYpos,
                JoystickAxis.Z => info.dwZpos,
                JoystickAxis.R => info.dwRpos,
                JoystickAxis.U => info.dwUpos,
                JoystickAxis.V => info.dwVpos,
                _ => 0
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
