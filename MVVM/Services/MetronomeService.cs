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
            _tickPcm   = GenerateClick(frequencyHz: 1000, durationSec: 0.040, amplitude: 0.35);
            _accentPcm = GenerateClick(frequencyHz: 1500, durationSec: 0.045, amplitude: 0.55);
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

        /// <summary>Queue one click. accent=true plays the brighter beat-1 sound.</summary>
        public void Tick(bool accent)
        {
            Prime();
            var pcm = accent ? _accentPcm : _tickPcm;
            _buffer?.AddSamples(pcm, 0, pcm.Length);
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

        private byte[] GenerateClick(double frequencyHz, double durationSec, double amplitude)
        {
            int samples = (int)(_format.SampleRate * durationSec);
            int rampSamples = (int)(_format.SampleRate * 0.005); // 5 ms attack + release
            var shorts = new short[samples];

            double phase = 0;
            double phaseInc = 2 * Math.PI * frequencyHz / _format.SampleRate;

            for (int i = 0; i < samples; i++)
            {
                // Linear envelope to avoid the audible "snap" at start/end
                double env = 1.0;
                if (i < rampSamples)
                    env = (double)i / rampSamples;
                else if (i > samples - rampSamples)
                    env = (double)(samples - i) / rampSamples;

                double v = Math.Sin(phase) * amplitude * env;
                shorts[i] = (short)(v * short.MaxValue);
                phase += phaseInc;
            }

            var bytes = new byte[samples * sizeof(short)];
            Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        public void Dispose() => Stop();
    }
}
