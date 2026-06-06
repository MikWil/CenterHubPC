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
            // "tick" — high, light clock tick
            _tickPcm   = GenerateClockTick(bodyHz: 1800, clickHz: 4000, durationSec: 0.028,
                                           amplitude: 0.70, bodyDecay: 280, clickDecay: 600);
            // "tock" — accent beat 1: lower, heavier
            _accentPcm = GenerateClockTick(bodyHz:  900, clickHz: 2800, durationSec: 0.040,
                                           amplitude: 0.95, bodyDecay: 180, clickDecay: 400);
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

        /// <summary>
        /// Synthesises a clock-tick sound by layering two components:
        ///   • "click" layer  — very high frequency, extremely fast decay (~1 ms)
        ///                      mimics the hard mechanical impact transient
        ///   • "body" layer   — mid frequency, slower exponential decay
        ///                      mimics the resonant woody body of a clock escapement
        /// Both layers share a 0.5 ms linear attack ramp to avoid a digital pop.
        /// </summary>
        private byte[] GenerateClockTick(
            double bodyHz, double clickHz,
            double durationSec, double amplitude,
            double bodyDecay, double clickDecay)
        {
            int samples       = (int)(_format.SampleRate * durationSec);
            int attackSamples = Math.Max(1, (int)(_format.SampleRate * 0.0005)); // 0.5 ms

            var shorts = new short[samples];

            double bodyPhase  = 0, bodyInc  = 2 * Math.PI * bodyHz  / _format.SampleRate;
            double clickPhase = 0, clickInc = 2 * Math.PI * clickHz / _format.SampleRate;

            for (int i = 0; i < samples; i++)
            {
                double t      = (double)i / _format.SampleRate;
                double attack = i < attackSamples ? (double)i / attackSamples : 1.0;

                double body  = Math.Sin(bodyPhase)  * Math.Exp(-bodyDecay  * t);
                double click = Math.Sin(clickPhase) * Math.Exp(-clickDecay * t);

                // click layer is ~40 % of body level — adds the sharp initial "snap"
                double v = (body * 0.70 + click * 0.30) * amplitude * attack;
                shorts[i] = (short)(Math.Clamp(v, -1.0, 1.0) * short.MaxValue);

                bodyPhase  += bodyInc;
                clickPhase += clickInc;
            }

            var bytes = new byte[samples * sizeof(short)];
            Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        public void Dispose() => Stop();
    }
}
