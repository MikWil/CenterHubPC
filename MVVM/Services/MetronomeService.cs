using System;
using NAudio.Wave;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Generates a tick / tock click for the metronome using a small in-memory
    /// sine wave sample. One shared WaveOutEvent + BufferedWaveProvider so
    /// every click goes through the same low-latency pipeline — no per-tick
    /// device allocation.
    /// </summary>
    public sealed class MetronomeService : IDisposable
    {
        private readonly WaveFormat _format = new(44100, 16, 1); // 44.1 kHz mono 16-bit
        private readonly byte[] _tickPcm;       // normal beat
        private readonly byte[] _accentPcm;     // beat 1 of each measure

        private WaveOutEvent? _output;
        private BufferedWaveProvider? _buffer;

        public MetronomeService()
        {
            _tickPcm   = GenerateClick(frequencyHz: 1200, durationSec: 0.035, amplitude: 0.60, decayRate: 80);
            _accentPcm = GenerateClick(frequencyHz:  800, durationSec: 0.045, amplitude: 0.90, decayRate: 60);
        }

        /// <summary>Start the output stream if not already running.</summary>
        public void Prime()
        {
            if (_output is not null) return;
            _buffer = new BufferedWaveProvider(_format)
            {
                BufferLength = _format.AverageBytesPerSecond, // 1 s ring buffer
                DiscardOnBufferOverflow = true,
            };
            _output = new WaveOutEvent { DesiredLatency = 60 };
            _output.Init(_buffer);
            _output.Play();
        }

        /// <summary>Queue one click. accent=true plays the deeper beat-1 sound.</summary>
        public void Tick(bool accent, float volume = 1.0f)
        {
            Prime();
            var src = accent ? _accentPcm : _tickPcm;
            if (Math.Abs(volume - 1.0f) < 0.005f)
            {
                _buffer?.AddSamples(src, 0, src.Length);
                return;
            }
            // Scale amplitude by volume without allocating on every tick
            var scaled = new byte[src.Length];
            for (int i = 0; i < src.Length - 1; i += 2)
            {
                short s = (short)(BitConverter.ToInt16(src, i) * volume);
                scaled[i]     = (byte)(s & 0xFF);
                scaled[i + 1] = (byte)((s >> 8) & 0xFF);
            }
            _buffer?.AddSamples(scaled, 0, scaled.Length);
        }

        /// <summary>Stop and dispose the output (keeps the cached PCM).</summary>
        public void Stop()
        {
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            _output = null;
            _buffer = null;
        }

        // ─────────────────── Tone synthesis ───────────────────

        // Percussive click: very short attack (1 ms), then exponential decay.
        // Mixing the fundamental with a 2x overtone adds presence and cuts through
        // well at higher tempos.
        private byte[] GenerateClick(double frequencyHz, double durationSec, double amplitude, double decayRate = 70)
        {
            int samples    = (int)(_format.SampleRate * durationSec);
            int attackSamples = (int)(_format.SampleRate * 0.001); // 1 ms attack
            var shorts = new short[samples];

            double phase     = 0;
            double phase2    = 0;
            double phaseInc  = 2 * Math.PI * frequencyHz / _format.SampleRate;
            double phaseInc2 = 2 * Math.PI * frequencyHz * 2 / _format.SampleRate;

            for (int i = 0; i < samples; i++)
            {
                double t   = (double)i / _format.SampleRate;
                double env = Math.Exp(-decayRate * t);
                if (i < attackSamples)
                    env *= (double)i / attackSamples; // brief linear attack to avoid transient pop

                // Fundamental + 2nd harmonic (−10 dB) for a woodier timbre
                double v = (Math.Sin(phase) * 0.85 + Math.Sin(phase2) * 0.15) * amplitude * env;
                shorts[i] = (short)(Math.Clamp(v, -1.0, 1.0) * short.MaxValue);
                phase  += phaseInc;
                phase2 += phaseInc2;
            }

            var bytes = new byte[samples * sizeof(short)];
            Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        public void Dispose() => Stop();
    }
}
