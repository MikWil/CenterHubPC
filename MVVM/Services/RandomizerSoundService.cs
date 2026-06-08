using System;
using NAudio.Wave;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Tiny in-memory synth for the Randomizer:
    ///   • a soft low "tick" played on each step while the wheel is rolling
    ///   • a celebratory ascending fanfare ("cheer") when a winner is picked
    /// One shared WaveOutEvent + BufferedWaveProvider keeps latency low.
    /// </summary>
    public sealed class RandomizerSoundService : IDisposable
    {
        private readonly WaveFormat _format = new(44100, 16, 1);
        private readonly byte[] _tickPcm;
        private readonly byte[] _winPcm;

        private WaveOutEvent? _output;
        private BufferedWaveProvider? _buffer;

        public RandomizerSoundService()
        {
            _tickPcm = GenerateRollTick();
            _winPcm  = GenerateFanfare();
        }

        public void Prime()
        {
            if (_output is not null) return;
            _buffer = new BufferedWaveProvider(_format)
            {
                BufferLength = _format.AverageBytesPerSecond * 2, // 2 s ring buffer (fanfare is ~1 s)
                DiscardOnBufferOverflow = true,
            };
            _output = new WaveOutEvent { DesiredLatency = 70 };
            _output.Init(_buffer);
            _output.Play();
        }

        /// <summary>Soft low blip for each roll step.</summary>
        public void PlayTick(float volume = 0.5f) => Queue(_tickPcm, volume);

        /// <summary>Celebratory fanfare when a winner is chosen.</summary>
        public void PlayWin(float volume = 0.85f) => Queue(_winPcm, volume);

        private void Queue(byte[] src, float volume)
        {
            Prime();
            if (Math.Abs(volume - 1.0f) < 0.005f)
            {
                _buffer?.AddSamples(src, 0, src.Length);
                return;
            }
            var scaled = new byte[src.Length];
            for (int i = 0; i < src.Length - 1; i += 2)
            {
                short s = (short)(BitConverter.ToInt16(src, i) * volume);
                scaled[i]     = (byte)(s & 0xFF);
                scaled[i + 1] = (byte)((s >> 8) & 0xFF);
            }
            _buffer?.AddSamples(scaled, 0, scaled.Length);
        }

        public void Stop()
        {
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            _output = null;
            _buffer = null;
        }

        // ─────────────────── Synthesis ───────────────────

        /// <summary>Low, soft, very short blip — the "ticking" of the wheel.</summary>
        private byte[] GenerateRollTick()
        {
            const double durationSec = 0.030;
            int samples       = (int)(_format.SampleRate * durationSec);
            int attackSamples = Math.Max(1, (int)(_format.SampleRate * 0.001));
            var shorts = new short[samples];

            double phase = 0, inc = 2 * Math.PI * 196.0 / _format.SampleRate; // G3 — low
            for (int i = 0; i < samples; i++)
            {
                double t      = (double)i / _format.SampleRate;
                double env    = Math.Exp(-120 * t);
                double attack = i < attackSamples ? (double)i / attackSamples : 1.0;
                // fundamental + soft 2nd harmonic, low amplitude so it stays subtle
                double v = (Math.Sin(phase) * 0.8 + Math.Sin(phase * 2) * 0.2) * 0.35 * env * attack;
                shorts[i] = (short)(Math.Clamp(v, -1.0, 1.0) * short.MaxValue);
                phase += inc;
            }
            return ToBytes(shorts);
        }

        /// <summary>
        /// Ascending C-major arpeggio (C5-E5-G5) resolving to a held C6 + C-major
        /// chord — a short triumphant "ta-da" cheer.
        /// </summary>
        private byte[] GenerateFanfare()
        {
            // note: (freq, startSec, durSec)
            var notes = new (double f, double start, double dur)[]
            {
                (523.25, 0.00, 0.16),  // C5
                (659.25, 0.12, 0.16),  // E5
                (783.99, 0.24, 0.18),  // G5
                // Final chord (C6 + E6 + G6) rings out
                (1046.50, 0.38, 0.55), // C6
                (1318.51, 0.38, 0.55), // E6
                (1567.98, 0.38, 0.55), // G6
            };

            double totalSec = 0;
            foreach (var n in notes) totalSec = Math.Max(totalSec, n.start + n.dur);
            int samples = (int)(_format.SampleRate * (totalSec + 0.02));
            var mix = new double[samples];

            foreach (var n in notes)
            {
                int start    = (int)(n.start * _format.SampleRate);
                int len      = (int)(n.dur  * _format.SampleRate);
                int attack   = Math.Max(1, (int)(_format.SampleRate * 0.005));
                double inc1  = 2 * Math.PI * n.f       / _format.SampleRate;
                double inc2  = 2 * Math.PI * n.f * 2   / _format.SampleRate;
                double inc3  = 2 * Math.PI * n.f * 3   / _format.SampleRate;
                double p1 = 0, p2 = 0, p3 = 0;

                for (int i = 0; i < len && start + i < samples; i++)
                {
                    // Pluck-ish envelope: quick attack, gentle exponential tail
                    double env = Math.Exp(-3.0 * i / _format.SampleRate * 1.0);
                    if (i < attack) env *= (double)i / attack;

                    // Bright timbre: fundamental + 2nd + 3rd harmonics
                    double v = (Math.Sin(p1) * 0.6 + Math.Sin(p2) * 0.25 + Math.Sin(p3) * 0.15) * env;
                    mix[start + i] += v * 0.5;
                    p1 += inc1; p2 += inc2; p3 += inc3;
                }
            }

            // Normalize to avoid clipping where notes overlap
            double peak = 0.0001;
            foreach (var v in mix) peak = Math.Max(peak, Math.Abs(v));
            double gain = peak > 1.0 ? 0.95 / peak : 1.0;

            var shorts = new short[samples];
            for (int i = 0; i < samples; i++)
                shorts[i] = (short)(Math.Clamp(mix[i] * gain, -1.0, 1.0) * short.MaxValue);

            return ToBytes(shorts);
        }

        private byte[] ToBytes(short[] shorts)
        {
            var bytes = new byte[shorts.Length * sizeof(short)];
            Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        public void Dispose() => Stop();
    }
}
