using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace SteeringTrainer.Core.Training.ThrottleTracking
{
    /// <summary>
    /// アクセル波形追従練習用メトロノーム音響サービス。
    /// 目標Sin波の上下ピーク（最大アクセル値・最小アクセル値）到達時に高音・中音のメトロノーム音を再生する。
    /// </summary>
    public class ThrottleTrackingAudioService
    {
        public bool IsEnabled { get; set; } = false;
        private double _volume = 0.7;

        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0.0, 1.0);
                RebuildWaves();
            }
        }

        private byte[] _topWave = Array.Empty<byte>();
        private byte[] _bottomWave = Array.Empty<byte>();
        private readonly object _lock = new();

        public ThrottleTrackingAudioService()
        {
            RebuildWaves();
        }

        private void RebuildWaves()
        {
            lock (_lock)
            {
                // 上ピーク (ThrottleMax到達時): 1760 Hz (A6), 25ms - 高音のクリスタルクリック
                _topWave = CreateMetronomeClick(1760, 25, _volume);
                // 下ピーク (ThrottleMin到達時): 1175 Hz (D6), 25ms - 落ち着いた中高音クリック
                _bottomWave = CreateMetronomeClick(1175, 25, _volume);
            }
        }

        /// <summary>
        /// ピーク到達時にメトロノーム音を非同期再生する
        /// </summary>
        /// <param name="isTopPeak">true: 上ピーク(ThrottleMax), false: 下ピーク(ThrottleMin)</param>
        public void PlayBeat(bool isTopPeak)
        {
            if (!IsEnabled || _volume <= 0.001) return;

            byte[] waveData;
            lock (_lock)
            {
                waveData = isTopPeak ? _topWave : _bottomWave;
            }

            if (waveData.Length == 0) return;

            Task.Run(() =>
            {
                try
                {
                    using var stream = new MemoryStream(waveData);
                    using var player = new SoundPlayer(stream);
                    player.PlaySync();
                }
                catch
                {
                    // オーディオデバイス初期化不可環境での安全な無視
                }
            });
        }

        private static byte[] CreateMetronomeClick(int frequency, int durationMs, double volume)
        {
            const int sampleRate = 22050;
            const short channels = 1;
            const short bitsPerSample = 16;
            int sampleCount = sampleRate * durationMs / 1000;
            int dataLength = sampleCount * channels * bitsPerSample / 8;
            using var stream = new MemoryStream(44 + dataLength);
            using var writer = new BinaryWriter(stream);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bitsPerSample / 8);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            for (int i = 0; i < sampleCount; i++)
            {
                // アタック（立ち上がり2ms）と急峻な減衰でメトロノームの木製/電子クリック感を再現
                double attack = Math.Min(1.0, (double)i / (sampleRate * 0.002));
                double decay = Math.Exp(-6.0 * (double)i / sampleCount);
                double envelope = attack * decay;
                double sample = Math.Sin(2.0 * Math.PI * frequency * i / sampleRate) * volume * envelope;
                writer.Write((short)(sample * short.MaxValue));
            }
            writer.Flush();
            return stream.ToArray();
        }
    }
}
