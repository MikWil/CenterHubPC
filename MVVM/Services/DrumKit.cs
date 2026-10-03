using System;
using System.Collections.Generic;
using CenterHubNew.MVVM.Models;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Pre-rendered mono float samples for every drum voice and click sound.
    /// Everything is synthesised once in the constructor and is fully deterministic
    /// (all noise comes from a fixed per-voice seed), so two kits of the same kind
    /// hold identical data. The <see cref="DrumKitKind.Acoustic"/> kit instead plays recorded
    /// multi-velocity hits for the drums the sample pack covers (see <see cref="GetLayers"/>)
    /// and synthesizes the rest like the Rock kit.
    /// </summary>
    public sealed class DrumKit
    {
        // The pack is big (decoded PCM); decode it once per process, not once per kit instance.
        private static readonly Lazy<DrumSamplePack?> RecordedPack = new(LoadRecordedPack);

        private readonly Dictionary<DrumVoice, float[]> _samples = new();
        private readonly Dictionary<DrumVoice, IReadOnlyList<DrumSampleLayer>> _layers = new();
        private readonly Dictionary<MetronomeSound, (float[] tick, float[] accent)> _clicks = new();
        private readonly Params _p;

        /// <summary>Renders the whole kit at <paramref name="sampleRate"/>.</summary>
        public DrumKit(DrumKitKind kind = DrumKitKind.Rock, int sampleRate = 44100)
        {
            if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            Kind = kind;
            SampleRate = sampleRate;
            _p = ParamsFor(kind);

            // Missing pack, load failure or a different sample rate: every voice falls back to the Rock synthesis.
            var pack = kind == DrumKitKind.Acoustic ? RecordedPack.Value : null;
            if (pack != null && pack.SampleRate != sampleRate) pack = null;

            foreach (DrumVoice voice in Enum.GetValues<DrumVoice>())
            {
                if (pack != null && pack.Voices.TryGetValue(voice, out var layers) && layers.Count > 0)
                {
                    _layers[voice] = layers;
                    _samples[voice] = MonoMix(layers[layers.Count - 1]);
                }
                else
                {
                    _samples[voice] = Render(voice);
                }
            }

            BuildClicks();
        }

        /// <summary>Which kit this is.</summary>
        public DrumKitKind Kind { get; }

        /// <summary>Sample rate (Hz) of every buffer this kit returns.</summary>
        public int SampleRate { get; }

        /// <summary>
        /// Mono samples for a drum piece, values within [-1, 1]. For a recorded voice this is a mono
        /// mix of its loudest hit (for previews and tests; the engine plays <see cref="GetLayers"/>).
        /// </summary>
        public float[] GetSample(DrumVoice voice) =>
            _samples.TryGetValue(voice, out var s) ? s : throw new ArgumentOutOfRangeException(nameof(voice));

        /// <summary>True when this voice plays recorded hits.</summary>
        public bool IsSampled(DrumVoice voice) => _layers.ContainsKey(voice);

        /// <summary>The recorded hits of a voice, soft → loud; empty for a synthesized voice.</summary>
        public IReadOnlyList<DrumSampleLayer> GetLayers(DrumVoice voice) =>
            _layers.TryGetValue(voice, out var l) ? l : Array.Empty<DrumSampleLayer>();

        private static DrumSamplePack? LoadRecordedPack()
        {
            try { return DrumSamplePack.LoadEmbedded("acoustic"); }
            catch { return null; }
        }

        /// <summary>(L+R)/2 of a hit; a mono hit is copied as is.</summary>
        private static float[] MonoMix(DrumSampleLayer layer)
        {
            var mix = new float[layer.Frames];
            var right = layer.Right;
            for (int i = 0; i < mix.Length; i++)
                mix[i] = right == null ? layer.Left[i] : (layer.Left[i] + right[i]) * 0.5f;
            return mix;
        }

        /// <summary>Default stereo position of a drum piece: -1 (left) .. +1 (right).</summary>
        public float GetPan(DrumVoice voice) => voice switch
        {
            DrumVoice.ClosedHat => 0.25f,
            DrumVoice.PedalHat => 0.25f,
            DrumVoice.OpenHat => 0.25f,
            DrumVoice.Ride => -0.30f,
            DrumVoice.RideBell => -0.30f,
            DrumVoice.Crash => 0.35f,
            DrumVoice.HighTom => 0.30f,
            DrumVoice.FloorTom => -0.40f,
            DrumVoice.Cowbell => -0.20f,
            DrumVoice.Tambourine => 0.40f,
            DrumVoice.Shaker => -0.40f,
            _ => 0f,
        };

        /// <summary>Mono metronome click: the normal tick, or the brighter/heavier beat-1 accent.</summary>
        public float[] GetClick(MetronomeSound sound, bool accent)
        {
            var pair = _clicks.TryGetValue(sound, out var p) ? p : _clicks[MetronomeSound.Clock];
            return accent ? pair.accent : pair.tick;
        }

        // ─────────────────── Kit parameters ───────────────────

        /// <summary>Everything that differs between the kits. Defaults are the Rock kit.</summary>
        private sealed record Params
        {
            // Kick
            public double KickBase { get; init; } = 48;
            public double KickSweep { get; init; } = 110;
            public double KickSweepTau { get; init; } = 0.028;
            public double KickTau { get; init; } = 0.16;
            public double KickLen { get; init; } = 0.45;
            public double KickNoise { get; init; } = 0.35;
            public double KickDrive { get; init; } = 1.6;
            public float KickPeak { get; init; } = 0.95f;

            // Snare
            public double SnareBodyTau { get; init; } = 0.055;
            public double SnareBodyMix { get; init; } = 0.55;
            public double SnareWireMix { get; init; } = 0.75;
            public double SnareWireHz { get; init; } = 1800;
            public bool SnareWireBand { get; init; } = true;   // extra 5 kHz band-pass blended in
            public double SnareWireTau { get; init; } = 0.085;
            public double SnareWireAttack { get; init; } = 0;
            public double SnareDrive { get; init; } = 1.3;      // 0 = no soft clip
            public float SnarePeak { get; init; } = 0.90f;

            // Hats
            public bool HatNoise { get; init; } = true;
            public double ClosedHatHz { get; init; } = 7000;
            public double PedalHatHz { get; init; } = 5000;
            public double OpenHatHz { get; init; } = 6500;
            public double ClosedHatTau { get; init; } = 0.018;
            public double OpenHatTau { get; init; } = 0.14;
            public float ClosedHatPeak { get; init; } = 0.55f;
            public float PedalHatPeak { get; init; } = 0.40f;
            public float OpenHatPeak { get; init; } = 0.55f;

            // Cymbals
            public double RideTail { get; init; } = 0.35;
            public double RideLen { get; init; } = 0.90;
            public float RidePeak { get; init; } = 0.50f;
            public float RideBellPeak { get; init; } = 0.55f;
            public double CrashTau { get; init; } = 0.45;
            public float CrashPeak { get; init; } = 0.70f;

            // Toms
            public double TomTauMul { get; init; } = 1.0;
            public double TomLenMul { get; init; } = 1.0;
            public double TomSweep { get; init; } = 0.6;
            public double TomNoise { get; init; } = 0.2;
            public float TomPeak { get; init; } = 0.85f;
        }

        private static Params ParamsFor(DrumKitKind kind) => kind switch
        {
            // 808-style: long boomy kick, synthetic snare, pure-metal hats, softer-pitched toms.
            DrumKitKind.Electro => new Params
            {
                KickBase = 45, KickSweep = 80, KickSweepTau = 0.020, KickTau = 0.32, KickLen = 0.80,
                KickNoise = 0.08, KickDrive = 1.2,
                SnareBodyTau = 0.040, SnareBodyMix = 0.6, SnareWireMix = 0.6, SnareWireHz = 3000,
                SnareWireBand = false, SnareWireTau = 0.11,
                HatNoise = false, ClosedHatHz = 8000, PedalHatHz = 8000, OpenHatHz = 8000,
                ClosedHatTau = 0.014, OpenHatTau = 0.18,
                CrashTau = 0.55,
                TomTauMul = 1.5, TomLenMul = 1.4, TomSweep = 0.3, TomNoise = 0,
            },
            // Soft and brushy: short quiet kick, wispy snare, gentle hats, long ride wash.
            DrumKitKind.Jazz => new Params
            {
                KickBase = 60, KickSweep = 60, KickSweepTau = 0.030, KickTau = 0.11, KickLen = 0.35,
                KickDrive = 1.0, KickPeak = 0.70f,
                SnareBodyMix = 0.35, SnareWireMix = 0.8, SnareWireHz = 2500, SnareWireBand = false,
                SnareWireTau = 0.12, SnareWireAttack = 0.003, SnareDrive = 0, SnarePeak = 0.70f,
                ClosedHatPeak = 0.40f, PedalHatPeak = 0.35f, OpenHatPeak = 0.45f,
                RideTail = 0.50, RideLen = 1.2, RidePeak = 0.60f,
                CrashTau = 0.55, CrashPeak = 0.60f,
                TomPeak = 0.70f,
            },
            // Rock, and Acoustic's synthesized voices (clap, cowbell, tambourine, shaker, sticks) / fallback.
            _ => new Params(),
        };

        // ─────────────────── Drum voices ───────────────────

        private float[] Render(DrumVoice voice)
        {
            var rng = new Random(1000 + (int)voice);
            var p = _p;
            switch (voice)
            {
                case DrumVoice.Kick: return Kick(rng);
                case DrumVoice.Snare: return Snare(rng);
                case DrumVoice.SideStick: return SideStick(rng);
                case DrumVoice.Clap: return Clap(rng);
                case DrumVoice.ClosedHat: return Hat(rng, 0.07, p.ClosedHatHz, p.ClosedHatTau, p.ClosedHatPeak);
                case DrumVoice.PedalHat: return Hat(rng, 0.06, p.PedalHatHz, 0.012, p.PedalHatPeak);
                case DrumVoice.OpenHat: return Hat(rng, 0.45, p.OpenHatHz, p.OpenHatTau, p.OpenHatPeak);
                case DrumVoice.Ride: return Ride(rng);
                case DrumVoice.RideBell: return RideBell();
                case DrumVoice.Crash: return Crash(rng);
                case DrumVoice.HighTom: return Tom(rng, 190, 0.14, 0.35);
                case DrumVoice.MidTom: return Tom(rng, 140, 0.17, 0.40);
                case DrumVoice.FloorTom: return Tom(rng, 95, 0.22, 0.50);
                case DrumVoice.Cowbell: return Cowbell();
                case DrumVoice.Tambourine: return Tambourine(rng);
                case DrumVoice.Shaker: return Shaker(rng);
                case DrumVoice.Sticks: return Sticks(rng);
                default: throw new ArgumentOutOfRangeException(nameof(voice));
            }
        }

        /// <summary>Sine with a fast downward pitch sweep plus a short filtered-noise beater click.</summary>
        private float[] Kick(Random rng)
        {
            var p = _p;
            var buf = Alloc(p.KickLen);
            var click = Biquad.LowPass(3500, 0.7, SampleRate);
            double phase = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double f = p.KickBase + p.KickSweep * Math.Exp(-t / p.KickSweepTau);
                phase += 2 * Math.PI * f / SampleRate;
                double body = Math.Sin(phase) * Math.Exp(-t / p.KickTau);
                double beater = click.Process(Noise(rng)) * Math.Exp(-t / 0.004) * p.KickNoise;
                buf[i] = (float)SoftClip(body + beater, p.KickDrive);
            }
            return Finish(buf, p.KickPeak, dcBlock: false);
        }

        /// <summary>Two pitch-dropping sine partials (shell) plus filtered noise (snare wires).</summary>
        private float[] Snare(Random rng)
        {
            var p = _p;
            var buf = Alloc(0.30);
            var hp = Biquad.HighPass(p.SnareWireHz, 0.7, SampleRate);
            var bp = Biquad.BandPass(5000, 0.6, SampleRate);
            double ph1 = 0, ph2 = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double bend = 1 + 0.25 * Math.Exp(-t / 0.010);
                ph1 += 2 * Math.PI * 185 * bend / SampleRate;
                ph2 += 2 * Math.PI * 330 * bend / SampleRate;
                double body = (Math.Sin(ph1) * 1.0 + Math.Sin(ph2) * 0.5) * Math.Exp(-t / p.SnareBodyTau);

                double high = hp.Process(Noise(rng));
                double wires = p.SnareWireBand ? 0.5 * high + 0.5 * bp.Process(high) : high;
                double wireEnv = Math.Exp(-t / p.SnareWireTau);
                if (p.SnareWireAttack > 0) wireEnv *= Math.Min(1.0, t / p.SnareWireAttack);

                double x = body * p.SnareBodyMix + wires * wireEnv * p.SnareWireMix;
                buf[i] = (float)(p.SnareDrive > 0 ? SoftClip(x, p.SnareDrive) : x);
            }
            return Finish(buf, p.SnarePeak, dcBlock: true);
        }

        /// <summary>Short, bright wooden "tock": two high sines plus a tiny noise transient.</summary>
        private float[] SideStick(Random rng)
        {
            var buf = Alloc(0.08);
            var bp = Biquad.BandPass(3000, 1.5, SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double tone = (Math.Sin(2 * Math.PI * 1750 * t) + 0.6 * Math.Sin(2 * Math.PI * 2450 * t))
                              * Math.Exp(-t / 0.012);
                double snap = bp.Process(Noise(rng)) * Math.Exp(-t / 0.004) * 0.3;
                buf[i] = (float)(tone + snap);
            }
            return Finish(buf, 0.70f, dcBlock: true);
        }

        /// <summary>Band-passed noise with three quick re-triggers and a reverby tail.</summary>
        private float[] Clap(Random rng)
        {
            var buf = Alloc(0.25);
            var bp = Biquad.BandPass(1200, 1.2, SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double env;
                if (t >= 0.03)
                {
                    env = Math.Exp(-(t - 0.03) / 0.07);
                }
                else
                {
                    env = Math.Exp(-t / 0.004);
                    if (t >= 0.010) env += Math.Exp(-(t - 0.010) / 0.004);
                    if (t >= 0.020) env += Math.Exp(-(t - 0.020) / 0.004);
                    env = Math.Min(env, 1.0);
                }
                buf[i] = (float)(bp.Process(Noise(rng)) * env);
            }
            return Finish(buf, 0.80f, dcBlock: true);
        }

        /// <summary>Hi-hat: 808 metallic source (plus noise on the acoustic kits) through a high-pass.</summary>
        private float[] Hat(Random rng, double length, double highPassHz, double tau, float peak)
        {
            var buf = Alloc(length);
            var hp = Biquad.HighPass(highPassHz, 0.7, SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double src = _p.HatNoise ? Metallic(t, 1.0) * 0.5 + Noise(rng) * 0.5 : Metallic(t, 1.0);
                buf[i] = (float)(hp.Process(src) * Math.Exp(-t / tau));
            }
            return Finish(buf, peak, dcBlock: true);
        }

        /// <summary>Ride: filtered metal + noise with a sharp stick attack and a long wash, plus a high "ping".</summary>
        private float[] Ride(Random rng)
        {
            var p = _p;
            var buf = Alloc(p.RideLen);
            var bp = Biquad.BandPass(5500, 0.7, SampleRate);
            var hp = Biquad.HighPass(3000, 0.7, SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double src = Metallic(t, 1.35) * 0.6 + Noise(rng) * 0.25;
                double env = 0.6 * Math.Exp(-t / 0.02) + 0.4 * Math.Exp(-t / p.RideTail);
                double ping = Math.Sin(2 * Math.PI * 3100 * t) * 0.15 * Math.Exp(-t / 0.05);
                buf[i] = (float)(hp.Process(bp.Process(src)) * env + ping);
            }
            return Finish(buf, p.RidePeak, dcBlock: true);
        }

        /// <summary>Ride bell: inharmonic bell partials with a shimmer of metal on top.</summary>
        private float[] RideBell()
        {
            var buf = Alloc(0.70);
            double[] freqs = { 1050, 1580, 2410, 3320 };
            double[] gains = { 1.0, 0.7, 0.5, 0.3 };
            var hp = Biquad.HighPass(4000, 0.7, SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double env = Math.Exp(-t / 0.22);
                double tone = 0;
                for (int k = 0; k < freqs.Length; k++)
                    tone += Math.Sin(2 * Math.PI * freqs[k] * t) * gains[k];
                double shimmer = hp.Process(Metallic(t, 1.35) * 0.2);
                buf[i] = (float)((tone + shimmer) * env);
            }
            return Finish(buf, _p.RideBellPeak, dcBlock: true);
        }

        /// <summary>Crash: bright noise + metal with an instant attack and a long decay.</summary>
        private float[] Crash(Random rng)
        {
            var p = _p;
            var buf = Alloc(1.60);
            var hp = Biquad.HighPass(2500, 0.7, SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double src = Noise(rng) * 0.7 + Metallic(t, 1.2) * 0.4;
                double env = Math.Min(1.0, t / 0.002) * Math.Exp(-t / p.CrashTau);
                buf[i] = (float)(hp.Process(src) * env);
            }
            return Finish(buf, p.CrashPeak, dcBlock: true);
        }

        /// <summary>Tom: a pitch-dropping sine with a short stick-noise knock. One path for all three toms.</summary>
        private float[] Tom(Random rng, double f0, double tau, double length)
        {
            var p = _p;
            tau *= p.TomTauMul;
            var buf = Alloc(length * p.TomLenMul);
            var lp = Biquad.LowPass(2000, 0.7, SampleRate);
            double phase = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double f = f0 * (1 + p.TomSweep * Math.Exp(-t / 0.03));
                phase += 2 * Math.PI * f / SampleRate;
                double v = Math.Sin(phase) * Math.Exp(-t / tau);
                if (p.TomNoise > 0)
                    v += lp.Process(Noise(rng)) * Math.Exp(-t / 0.006) * p.TomNoise;
                buf[i] = (float)v;
            }
            return Finish(buf, p.TomPeak, dcBlock: false);
        }

        /// <summary>808 cowbell: two square waves through a resonant band-pass.</summary>
        private float[] Cowbell()
        {
            var buf = Alloc(0.25);
            var bp = Biquad.BandPass(800, 1.5, SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double sq = (Math.Sign(Math.Sin(2 * Math.PI * 540 * t)) + Math.Sign(Math.Sin(2 * Math.PI * 800 * t))) / 2.0;
                buf[i] = (float)(bp.Process(sq) * Math.Exp(-t / 0.08));
            }
            return Finish(buf, 0.60f, dcBlock: true);
        }

        /// <summary>Tambourine: bright noise + metal jingle, high-passed.</summary>
        private float[] Tambourine(Random rng)
        {
            var buf = Alloc(0.18);
            var hp = Biquad.HighPass(6000, 0.7, SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double src = Noise(rng) * 0.7 + Metallic(t, 2.0) * 0.3;
                buf[i] = (float)(hp.Process(src) * Math.Exp(-t / 0.05));
            }
            return Finish(buf, 0.50f, dcBlock: true);
        }

        /// <summary>Shaker: band-passed noise with a soft 8 ms swell.</summary>
        private float[] Shaker(Random rng)
        {
            var buf = Alloc(0.09);
            var bp = Biquad.BandPass(6500, 1.0, SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double env = t < 0.008 ? t / 0.008 : Math.Exp(-(t - 0.008) / 0.03);
                buf[i] = (float)(bp.Process(Noise(rng)) * env);
            }
            return Finish(buf, 0.40f, dcBlock: true);
        }

        /// <summary>Stick click: two bright sines plus a very short noise tick.</summary>
        private float[] Sticks(Random rng)
        {
            var buf = Alloc(0.06);
            var hp = Biquad.HighPass(4000, 0.7, SampleRate);
            for (int i = 0; i < buf.Length; i++)
            {
                double t = (double)i / SampleRate;
                double tone = (Math.Sin(2 * Math.PI * 1900 * t) + Math.Sin(2 * Math.PI * 2900 * t)) * Math.Exp(-t / 0.009);
                double tick = hp.Process(Noise(rng)) * Math.Exp(-t / 0.002) * 0.4;
                buf[i] = (float)(tone + tick);
            }
            return Finish(buf, 0.80f, dcBlock: true);
        }

        // ─────────────────── Click sounds (ported from MetronomeService) ───────────────────

        private void BuildClicks()
        {
            // Clock — high, light mechanical tick; deeper tock on beat 1
            _clicks[MetronomeSound.Clock] = (
                ClockTick(bodyHz: 1800, clickHz: 4000, durationSec: 0.028, amplitude: 0.70, bodyDecay: 280, clickDecay: 600),
                ClockTick(bodyHz: 900, clickHz: 2800, durationSec: 0.040, amplitude: 0.95, bodyDecay: 180, clickDecay: 400));

            // Wood block — warm, woody knock with a couple of harmonics
            _clicks[MetronomeSound.WoodBlock] = (
                DecayTone(new[] { 1200.0, 2400.0, 3600.0 }, new[] { 1.0, 0.45, 0.20 }, 0.045, 0.78, 95),
                DecayTone(new[] { 820.0, 1640.0, 2460.0 }, new[] { 1.0, 0.45, 0.20 }, 0.055, 0.98, 75));

            // Beep — clean digital metronome tone (flat envelope)
            _clicks[MetronomeSound.Beep] = (
                SustainTone(1320, 0.045, 0.55),
                SustainTone(880, 0.060, 0.78));

            // Click — short, snappy sine click
            _clicks[MetronomeSound.Click] = (
                DecayTone(new[] { 2000.0 }, new[] { 1.0 }, 0.022, 0.60, 360),
                DecayTone(new[] { 1500.0 }, new[] { 1.0 }, 0.030, 0.88, 260));

            // Cowbell — metallic 808-style pair of detuned square tones
            _clicks[MetronomeSound.Cowbell] = (
                SquarePair(540, 800, 0.110, 0.50, 30),
                SquarePair(480, 720, 0.140, 0.65, 26));

            // Rim — tight, bright rimshot-like blip
            _clicks[MetronomeSound.Rim] = (
                DecayTone(new[] { 2400.0, 3200.0 }, new[] { 1.0, 0.7 }, 0.018, 0.65, 520),
                DecayTone(new[] { 1700.0, 2550.0 }, new[] { 1.0, 0.7 }, 0.024, 0.90, 380));
        }

        /// <summary>Clock tick: a fast-decaying high click layered over a resonant body.</summary>
        private float[] ClockTick(double bodyHz, double clickHz, double durationSec, double amplitude,
                                  double bodyDecay, double clickDecay)
        {
            int samples = (int)(SampleRate * durationSec);
            int attackSamples = Math.Max(1, (int)(SampleRate * 0.0005));
            var buf = new float[samples];

            double bodyPhase = 0, bodyInc = 2 * Math.PI * bodyHz / SampleRate;
            double clickPhase = 0, clickInc = 2 * Math.PI * clickHz / SampleRate;

            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / SampleRate;
                double attack = i < attackSamples ? (double)i / attackSamples : 1.0;

                double body = Math.Sin(bodyPhase) * Math.Exp(-bodyDecay * t);
                double click = Math.Sin(clickPhase) * Math.Exp(-clickDecay * t);

                double v = (body * 0.70 + click * 0.30) * amplitude * attack;
                buf[i] = (float)Math.Clamp(v, -1.0, 1.0);

                bodyPhase += bodyInc;
                clickPhase += clickInc;
            }
            return buf;
        }

        /// <summary>Percussive additive tone: sine partials under a shared exponential decay with a short attack.</summary>
        private float[] DecayTone(double[] freqs, double[] amps, double durationSec, double amplitude,
                                  double decay, double attackSec = 0.0008)
        {
            int samples = (int)(SampleRate * durationSec);
            int attackSamples = Math.Max(1, (int)(SampleRate * attackSec));
            var buf = new float[samples];
            var phases = new double[freqs.Length];

            double norm = 0;
            foreach (var a in amps) norm += a;
            if (norm <= 0) norm = 1;

            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / SampleRate;
                double env = Math.Exp(-decay * t);
                double attack = i < attackSamples ? (double)i / attackSamples : 1.0;

                double v = 0;
                for (int k = 0; k < freqs.Length; k++)
                {
                    v += Math.Sin(phases[k]) * amps[k];
                    phases[k] += 2 * Math.PI * freqs[k] / SampleRate;
                }
                v = v / norm * amplitude * env * attack;
                buf[i] = (float)Math.Clamp(v, -1.0, 1.0);
            }
            return buf;
        }

        /// <summary>Flat-envelope sine "beep" with short attack/release ramps.</summary>
        private float[] SustainTone(double freq, double durationSec, double amplitude)
        {
            int samples = (int)(SampleRate * durationSec);
            int ramp = Math.Max(1, (int)(SampleRate * 0.005));
            var buf = new float[samples];

            double phase = 0, inc = 2 * Math.PI * freq / SampleRate;
            for (int i = 0; i < samples; i++)
            {
                double env = 1.0;
                if (i < ramp) env = (double)i / ramp;
                else if (i > samples - ramp) env = (double)(samples - i) / ramp;

                double v = Math.Sin(phase) * amplitude * env;
                buf[i] = (float)Math.Clamp(v, -1.0, 1.0);
                phase += inc;
            }
            return buf;
        }

        /// <summary>Two detuned square oscillators under an exponential decay (classic cowbell timbre).</summary>
        private float[] SquarePair(double f1, double f2, double durationSec, double amplitude, double decay)
        {
            int samples = (int)(SampleRate * durationSec);
            int ramp = Math.Max(1, (int)(SampleRate * 0.001));
            var buf = new float[samples];

            double p1 = 0, p2 = 0;
            double i1 = 2 * Math.PI * f1 / SampleRate;
            double i2 = 2 * Math.PI * f2 / SampleRate;

            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / SampleRate;
                double env = Math.Exp(-decay * t);
                double attack = i < ramp ? (double)i / ramp : 1.0;

                // Soft squares (0.6 weight) keep the metallic edge without harsh clipping
                double s1 = Math.Sign(Math.Sin(p1));
                double s2 = Math.Sign(Math.Sin(p2));
                double v = (s1 + s2) / 2 * 0.6 * amplitude * env * attack;
                buf[i] = (float)Math.Clamp(v, -1.0, 1.0);

                p1 += i1;
                p2 += i2;
            }
            return buf;
        }

        // ─────────────────── Rendering helpers ───────────────────

        private float[] Alloc(double seconds) => new float[Math.Max(1, (int)Math.Round(SampleRate * seconds))];

        /// <summary>
        /// Common finishing: optional 20 Hz DC-blocking high-pass, a 0.3 ms attack ramp (so the
        /// first sample is exactly 0), a 3 ms linear fade-out to exactly 0, then peak normalisation.
        /// </summary>
        private float[] Finish(float[] buf, float peak, bool dcBlock)
        {
            if (dcBlock)
            {
                // One-pole high-pass: y[n] = a * (y[n-1] + x[n] - x[n-1])
                double a = 1.0 / (1.0 + 2 * Math.PI * 20.0 / SampleRate);
                double prevIn = 0, prevOut = 0;
                for (int i = 0; i < buf.Length; i++)
                {
                    double x = buf[i];
                    double y = a * (prevOut + x - prevIn);
                    prevIn = x;
                    prevOut = y;
                    buf[i] = (float)y;
                }
            }

            int attack = Math.Min(buf.Length, Math.Max(1, (int)Math.Ceiling(SampleRate * 0.0004)));
            for (int i = 0; i < attack; i++)
                buf[i] *= (float)i / attack;

            FadeOut(buf, Math.Max(1, (int)(SampleRate * 0.003)));
            Normalize(buf, peak);
            return buf;
        }

        // ─────────────────── DSP helpers ───────────────────

        /// <summary>Uniform white noise in [-1, 1].</summary>
        private static double Noise(Random rng) => rng.NextDouble() * 2.0 - 1.0;

        /// <summary>The classic 808 cymbal source: six square waves mixed down and scaled.</summary>
        private static double Metallic(double t, double scale)
        {
            double sum = 0;
            foreach (double f in MetalFreqs)
                sum += Math.Sign(Math.Sin(2 * Math.PI * f * t)) * scale;
            return sum / 6.0;
        }

        private static readonly double[] MetalFreqs = { 205.3, 304.4, 369.6, 522.7, 540.0, 800.0 };

        /// <summary>Soft saturation: tanh(drive * x).</summary>
        private static double SoftClip(double x, double drive) => Math.Tanh(drive * x);

        /// <summary>Scales the buffer so its largest absolute value equals <paramref name="peak"/>.</summary>
        private static void Normalize(float[] buf, float peak)
        {
            float max = 0f;
            foreach (float v in buf)
            {
                float a = Math.Abs(v);
                if (a > max) max = a;
            }
            if (max <= 1e-9f) return;
            float gain = peak / max;
            for (int i = 0; i < buf.Length; i++)
                buf[i] = Math.Clamp(buf[i] * gain, -1f, 1f);
        }

        /// <summary>Linear fade of the last <paramref name="samples"/> samples down to exactly 0.</summary>
        private static void FadeOut(float[] buf, int samples)
        {
            samples = Math.Min(samples, buf.Length);
            int start = buf.Length - samples;
            for (int i = start; i < buf.Length; i++)
                buf[i] *= (float)(buf.Length - 1 - i) / samples;
        }

        /// <summary>RBJ cookbook biquad (Direct Form I) with double-precision state.</summary>
        private sealed class Biquad
        {
            private readonly double _b0, _b1, _b2, _a1, _a2;
            private double _x1, _x2, _y1, _y2;

            private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
            {
                _b0 = b0 / a0;
                _b1 = b1 / a0;
                _b2 = b2 / a0;
                _a1 = a1 / a0;
                _a2 = a2 / a0;
            }

            public static Biquad LowPass(double fc, double q, int sampleRate)
            {
                Setup(fc, q, sampleRate, out double cos, out double alpha);
                return new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
            }

            public static Biquad HighPass(double fc, double q, int sampleRate)
            {
                Setup(fc, q, sampleRate, out double cos, out double alpha);
                return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
            }

            /// <summary>Band-pass with constant 0 dB peak gain.</summary>
            public static Biquad BandPass(double fc, double q, int sampleRate)
            {
                Setup(fc, q, sampleRate, out double cos, out double alpha);
                return new Biquad(alpha, 0, -alpha, 1 + alpha, -2 * cos, 1 - alpha);
            }

            private static void Setup(double fc, double q, int sampleRate, out double cos, out double alpha)
            {
                fc = Math.Min(fc, sampleRate * 0.45); // stay clear of Nyquist at low sample rates
                double w0 = 2 * Math.PI * fc / sampleRate;
                cos = Math.Cos(w0);
                alpha = Math.Sin(w0) / (2 * q);
            }

            public double Process(double x)
            {
                double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1;
                _x1 = x;
                _y2 = _y1;
                _y1 = y;
                return y;
            }
        }
    }
}
