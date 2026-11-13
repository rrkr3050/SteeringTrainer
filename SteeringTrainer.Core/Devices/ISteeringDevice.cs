using SteeringTrainer.Core.Models;
using System;
using System.Collections.Generic;

namespace SteeringTrainer.Core.Devices
{
    /// <summary>
    /// ステアリングおよびペダル入力デバイス抽象インターフェース
    /// </summary>
    public interface ISteeringDevice : IDisposable
    {
        string DeviceName { get; }
        bool IsConnected { get; }

        /// <summary>現在の正規化ステアリング値 (-1.0 ～ +1.0)</summary>
        double SteeringNormalized { get; }

        /// <summary>現在の正規化アクセル値 (0.0 ～ 1.0)</summary>
        double ThrottleNormalized { get; }

        /// <summary>現在の正規化ブレーキ値 (0.0 ～ 1.0)</summary>
        double BrakeNormalized { get; }

        /// <summary>現在の正規化クラッチ値 (0.0 ～ 1.0)</summary>
        double ClutchNormalized { get; }

        /// <summary>Raw軸読み取り値</summary>
        uint RawX { get; }
        uint RawY { get; }
        uint RawZ { get; }
        uint RawR { get; }
        uint RawU { get; }
        uint RawV { get; }

        /// <summary>Rawボタンビットマスク</summary>
        uint RawButtons { get; }

        /// <summary>ボタンが押下されたときの通知イベント (引数は押されたボタンインデックス 0..31)</summary>
        event EventHandler<int>? ButtonPressed;

        /// <summary>ペダルキャリブレーション・軸割り当て設定</summary>
        DevicePedalSettings PedalSettings { get; set; }

        /// <summary>最新サンプルタイムスタンプ (Stopwatch ticks)</summary>
        long LastTimestampTicks { get; }

        event EventHandler<bool> ConnectionChanged;

        /// <summary>入力取得を開始する</summary>
        void Start();

        /// <summary>入力取得を停止する</summary>
        void Stop();

        /// <summary>利用可能なデバイス一覧を列挙する</summary>
        IReadOnlyList<DeviceInfo> EnumerateDevices();

        /// <summary>指定デバイスを選択する</summary>
        bool SelectDevice(string deviceId);
    }
}
