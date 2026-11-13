using System;

namespace SteeringTrainer.Core.Models
{
    /// <summary>
    /// ジョイスティック軸の種類
    /// </summary>
    public enum JoystickAxis
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 3,
        R = 4,
        U = 5,
        V = 6
    }

    /// <summary>
    /// 単一ペダルのキャリブレーション・設定情報
    /// </summary>
    public class PedalConfig
    {
        /// <summary>割り当てるジョイスティック軸</summary>
        public JoystickAxis AssignedAxis { get; set; } = JoystickAxis.None;

        private uint _raw0 = 65535;
        private uint _raw100 = 0;
        private uint _minRaw = 0;
        private uint _maxRaw = 65535;
        private bool _invert = true;
        private bool _rawExplicitlySet = false;

        /// <summary>未踏み (0%) 時のRaw値 (0..65535)</summary>
        public uint Raw0Percent
        {
            get => _raw0;
            set
            {
                _raw0 = value;
                _rawExplicitlySet = true;
            }
        }

        /// <summary>全開 (100%) 時のRaw値 (0..65535)</summary>
        public uint Raw100Percent
        {
            get => _raw100;
            set
            {
                _raw100 = value;
                _rawExplicitlySet = true;
            }
        }

        /// <summary>未踏み状態のRaw最小値 (下位互換プロパティ)</summary>
        public uint MinRaw
        {
            get => _rawExplicitlySet ? Math.Min(_raw0, _raw100) : _minRaw;
            set
            {
                _minRaw = value;
                if (!_rawExplicitlySet)
                {
                    SyncFromLegacy();
                }
            }
        }

        /// <summary>全開状態のRaw最大値 (下位互換プロパティ)</summary>
        public uint MaxRaw
        {
            get => _rawExplicitlySet ? Math.Max(_raw0, _raw100) : _maxRaw;
            set
            {
                _maxRaw = value;
                if (!_rawExplicitlySet)
                {
                    SyncFromLegacy();
                }
            }
        }

        /// <summary>踏み始めデッドゾーン率 (0.0 .. 30.0%)</summary>
        public double DeadzonePercent { get; set; } = 2.0;

        /// <summary>踏み切りデッドゾーン率 (0.0 .. 30.0%)</summary>
        public double MaxDeadzonePercent { get; set; } = 2.0;

        /// <summary>
        /// 入力反転フラグ (Raw0Percent > Raw100Percent のとき true)
        /// </summary>
        public bool Invert
        {
            get => _rawExplicitlySet ? (_raw0 > _raw100) : _invert;
            set
            {
                _invert = value;
                if (_rawExplicitlySet)
                {
                    if (value != (_raw0 > _raw100))
                    {
                        var tmp = _raw0;
                        _raw0 = _raw100;
                        _raw100 = tmp;
                    }
                }
                else
                {
                    SyncFromLegacy();
                }
            }
        }

        /// <summary>ガンマカーブ指数 (0.5 〜 3.0, 1.0 = リニア)</summary>
        public double Gamma { get; set; } = 1.0;

        private void SyncFromLegacy()
        {
            if (_invert)
            {
                _raw0 = _maxRaw;
                _raw100 = _minRaw;
            }
            else
            {
                _raw0 = _minRaw;
                _raw100 = _maxRaw;
            }
        }

        /// <summary>
        /// Raw値を 0.0 (未踏み) ～ 1.0 (全開) の正規化値へ変換する
        /// </summary>
        public double Normalize(uint rawValue)
        {
            if (AssignedAxis == JoystickAxis.None) return 0.0;

            double p0 = Raw0Percent;
            double p100 = Raw100Percent;
            double diff = p100 - p0;
            if (Math.Abs(diff) < 1.0) return 0.0;

            // 0.0 (0%点) 〜 1.0 (100%点) の生比率
            double ratio = ((double)rawValue - p0) / diff;
            ratio = Math.Clamp(ratio, 0.0, 1.0);

            // デッドゾーン処理 (下端 & 上端)
            double dzMin = Math.Clamp(DeadzonePercent / 100.0, 0.0, 0.35);
            double dzMax = Math.Clamp(MaxDeadzonePercent / 100.0, 0.0, 0.35);
            double activeRange = 1.0 - dzMin - dzMax;
            if (activeRange <= 0.001) return ratio >= 0.5 ? 1.0 : 0.0;

            double normalized;
            if (ratio <= dzMin)
            {
                normalized = 0.0;
            }
            else if (ratio >= 1.0 - dzMax)
            {
                normalized = 1.0;
            }
            else
            {
                normalized = (ratio - dzMin) / activeRange;
            }

            normalized = Math.Clamp(normalized, 0.0, 1.0);

            // ガンマ補正 (y = x^Gamma)
            if (Math.Abs(Gamma - 1.0) > 0.01 && Gamma > 0.1)
            {
                normalized = Math.Pow(normalized, Gamma);
            }

            return Math.Clamp(normalized, 0.0, 1.0);
        }
    }

    /// <summary>
    /// 全ペダルの統合設定 (Throttle, Brake, Clutch)
    /// </summary>
    public class DevicePedalSettings
    {
        /// <summary>アクセル (GTFP標準: Axis Z, Invert=true)</summary>
        public PedalConfig Throttle { get; set; } = new()
        {
            AssignedAxis = JoystickAxis.Z,
            Raw0Percent = 65535,
            Raw100Percent = 0,
            Invert = true,
            DeadzonePercent = 2.0,
            MaxDeadzonePercent = 2.0,
            Gamma = 1.0
        };

        /// <summary>ブレーキ (GTFP標準: Axis R, Invert=true)</summary>
        public PedalConfig Brake { get; set; } = new()
        {
            AssignedAxis = JoystickAxis.R,
            Raw0Percent = 65535,
            Raw100Percent = 0,
            Invert = true,
            DeadzonePercent = 2.0,
            MaxDeadzonePercent = 2.0,
            Gamma = 1.0
        };

        /// <summary>クラッチ (3ペダル機材用: オプション)</summary>
        public PedalConfig Clutch { get; set; } = new()
        {
            AssignedAxis = JoystickAxis.None,
            Raw0Percent = 65535,
            Raw100Percent = 0,
            Invert = true,
            DeadzonePercent = 2.0,
            MaxDeadzonePercent = 2.0,
            Gamma = 1.0
        };
    }
}
