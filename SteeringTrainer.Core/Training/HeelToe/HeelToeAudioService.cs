using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace SteeringTrainer.Core.Training.HeelToe
{
    /// <summary>
    /// ヒール＆トゥ練習用の非同期音響フィードバックサービス。
    /// ブラインドモードおよび通常モードでのタイミング・成否通知を担当する。
    /// </summary>
    public class HeelToeAudioService
    {
        public bool IsAudioEnabled { get; set; } = true;
        public double Volume { get; set; } = 0.5;

        public void PlayBeep(HeelToeBeepType type)
        {
            if (!IsAudioEnabled || type == HeelToeBeepType.None) return;

            var volume = Math.Clamp(Volume, 0.0, 1.0);
            Task.Run(() =>
            {
                try
                {
                    switch (type)
                    {
                        case HeelToeBeepType.BrakeStable:
                            PlayTone(880, 80, volume);
                            break;

                        case HeelToeBeepType.BlipPrompt:
                            // BLIP指示: 高めのクリア音
                            PlayTone(1200, 100, volume);
                            break;

                        case HeelToeBeepType.Perfect:
                            // パーフェクト: 2連高音ファンファーレ
                            PlayTone(1400, 100, volume);
                            PlayTone(1760, 160, volume);
                            break;

                        case HeelToeBeepType.Good:
                            // 良好: 軽快な中高音
                            PlayTone(1046, 120, volume);
                            break;

                        case HeelToeBeepType.Miss:
                            // 失敗: 低音ブザー
                            PlayTone(330, 220, volume);
                            break;
                    }
                }
                catch
                {
                    // サウンドデバイス非対応環境での例外を安全に無視
                }
            });
        }

        private static void PlayTone(int frequency, int durationMs, double volume)
        {
            if (volume <= 0) return;
            using var stream = new MemoryStream(CreateWave(frequency, durationMs, volume));
            using var player = new SoundPlayer(stream);
            player.PlaySync();
        }

        private static byte[] CreateWave(int frequency, int durationMs, double volume)
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
                double edgeFade = Math.Min(1.0, Math.Min(i / 220.0, (sampleCount - i) / 220.0));
                double sample = Math.Sin(2 * Math.PI * frequency * i / sampleRate) * volume * edgeFade;
                writer.Write((short)(sample * short.MaxValue));
            }
            writer.Flush();
            return stream.ToArray();
        }
    }
}
