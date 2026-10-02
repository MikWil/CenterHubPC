using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Threading;
using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Hosts the <see cref="DrumMachineEngine"/>: owns the audio output (opened on demand,
    /// closed once the engine has been silent for a while) and pumps the engine's position
    /// events to the UI thread at the moment they become audible.
    /// All public members are meant to be called on the UI thread.
    /// </summary>
    public sealed class MetronomeService : IDisposable
    {
        private static readonly TimeSpan PumpInterval = TimeSpan.FromMilliseconds(15);
        private static readonly TimeSpan IdleShutdownAfter = TimeSpan.FromSeconds(1.5);

        private readonly ILogger<MetronomeService>? _logger;
        private readonly Dictionary<DrumKitKind, DrumKit> _kits = new();

        private IWavePlayer? _output;
        private long _framesAtOpen;
        private DispatcherTimer? _pump;
        private long _idleSinceTimestamp; // 0 = not currently idle
        private bool _disposed;

        /// <summary>Creates the engine with the Rock kit. Opens no audio device and touches no dispatcher.</summary>
        public MetronomeService(ILogger<MetronomeService>? logger = null)
        {
            _logger = logger;
            var kit = new DrumKit(DrumKitKind.Rock);
            _kits[DrumKitKind.Rock] = kit;
            KitKind = DrumKitKind.Rock;
            Engine = new DrumMachineEngine(kit);
        }

        /// <summary>The sequencer. The view-model configures tempo/style/click options directly on it.</summary>
        public DrumMachineEngine Engine { get; }

        /// <summary>The drum kit currently loaded in the engine.</summary>
        public DrumKitKind KitKind { get; private set; }

        /// <summary>True while the engine is sequencing.</summary>
        public bool IsPlaying => Engine.IsPlaying;

        /// <summary>Raised on the UI thread for each engine position event, when it becomes audible.</summary>
        public event Action<DrumEngineEvent>? PositionChanged;

        /// <summary>Switches the drum kit. Kits are cached per kind because building one synthesizes audio.</summary>
        public void SetKit(DrumKitKind kind)
        {
            if (kind == KitKind) return;
            if (!_kits.TryGetValue(kind, out var kit))
            {
                kit = new DrumKit(kind);
                _kits[kind] = kit;
            }
            Engine.SetKit(kit);
            KitKind = kind;
        }

        /// <summary>Opens the output if needed and starts the sequencer.</summary>
        public void Start()
        {
            // Without an output nothing would ever pull samples, so the engine would never advance.
            if (!EnsureOutput()) return;
            Engine.Start();
        }

        /// <summary>
        /// Stops the sequencer. The output stays open until the idle shutdown closes it after
        /// the tails ring out; the final "Stopped" event is delivered by the pump.
        /// </summary>
        public void Stop() => Engine.Stop();

        /// <summary>Plays the accent hit (e.g. tap/crash) through the output.</summary>
        public void AccentHit()
        {
            if (!EnsureOutput()) return;
            Engine.TriggerAccentHit();
        }

        /// <summary>Plays one click so the user can audition a sound.</summary>
        public void PreviewClick(MetronomeSound sound, bool accent = false)
        {
            if (!EnsureOutput()) return;
            Engine.PreviewClick(sound, accent);
        }

        /// <summary>Plays one drum piece so the user can audition it.</summary>
        public void PreviewVoice(DrumVoice voice)
        {
            if (!EnsureOutput()) return;
            Engine.PreviewVoice(voice);
        }

        /// <summary>Stops playback, the pump and the output. Safe to call more than once; never throws.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _pump?.Stop(); } catch { /* best effort */ }
            _pump = null;
            try { Engine.Stop(); } catch { /* best effort */ }
            CloseOutput();
        }

        // Returns true when an output is open and running.
        private bool EnsureOutput()
        {
            if (_disposed) return false;

            if (_output == null)
            {
                // Stale events belong to the previous output's clock; drop them unraised.
                while (Engine.TryDequeueEvent(out _)) { }
                long framesAtOpen = Engine.FramesRendered;

                var output = OpenOutput();
                if (output == null) return false;

                // Publish only after Play() succeeded. The device position restarts at 0 per output.
                output.PlaybackStopped += OnPlaybackStopped;
                _framesAtOpen = framesAtOpen;
                _output = output;
            }

            _idleSinceTimestamp = 0;
            if (_pump == null)
            {
                _pump = new DispatcherTimer { Interval = PumpInterval };
                _pump.Tick += OnPumpTick;
            }
            if (!_pump.IsEnabled) _pump.Start();
            return true;
        }

        /// <summary>
        /// Opens and starts an output on the default device, or returns null when none works.
        /// WASAPI (event-driven: the device pulls exactly what it needs) comes first. WaveOut is the
        /// fallback, with three buffers — measured on a Voicemeeter virtual device, the usual
        /// two-buffer / 80 ms WaveOut setup under-ran and audibly dragged the tempo.
        /// </summary>
        private IWavePlayer? OpenOutput()
        {
            try
            {
                return StartOutput(new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 50),
                                   convertTo16Bit: false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Metronome: WASAPI output unavailable, falling back to WaveOut");
            }

            try
            {
                // 16-bit for maximum device compatibility.
                return StartOutput(new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 3 },
                                   convertTo16Bit: true);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Metronome: failed to start audio output");
                return null;
            }
        }

        private IWavePlayer StartOutput(IWavePlayer output, bool convertTo16Bit)
        {
            try
            {
                output.Init(Engine, convertTo16Bit);
                output.Play();
                return output;
            }
            catch
            {
                try { output.Dispose(); } catch { /* half-built output */ }
                throw;
            }
        }

        /// <summary>The device went away under us (unplugged, Voicemeeter restarted…): stop cleanly.</summary>
        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                try { Dispatcher.UIThread.Post(() => OnPlaybackStopped(sender, e)); }
                catch (InvalidOperationException) { /* dispatcher already shut down */ }
                return;
            }

            if (_disposed || !ReferenceEquals(sender, _output)) return;

            _logger?.LogWarning(e.Exception, "Metronome: audio output stopped unexpectedly");
            Engine.Stop();
            while (Engine.TryDequeueEvent(out var rest)) Raise(rest);
            _pump?.Stop();
            CloseOutput();
            _idleSinceTimestamp = 0;
        }

        private void OnPumpTick(object? sender, EventArgs e)
        {
            var output = _output;
            if (output == null)
            {
                _pump?.Stop();
                return;
            }

            // 1. How far the device has actually played.
            long rendered = Engine.FramesRendered;
            long playedFrames;
            try
            {
                // The position is in bytes of the DEVICE format, which may not be the engine's.
                var position = (IWavePosition)output;
                double seconds = (double)position.GetPosition() / Math.Max(1, position.OutputWaveFormat.AverageBytesPerSecond);
                playedFrames = _framesAtOpen + (long)(seconds * Engine.WaveFormat.SampleRate);
                if (playedFrames > rendered) playedFrames = rendered;
            }
            catch
            {
                playedFrames = rendered;
            }

            // 2. Deliver every event whose frame has become audible. The output never buffers
            //    anywhere near half a second, so an event older than that is delivered regardless —
            //    a misbehaving device position counter must not freeze the beat display.
            long overdue = rendered - Engine.WaveFormat.SampleRate / 2;
            //    Strictly "<": the very first beat is frame 0 of a fresh output, and must wait until
            //    the device clock actually starts moving rather than light up a latency early.
            while (Engine.TryPeekEvent(out var ev) && (ev.Frame < playedFrames || ev.Frame < overdue))
            {
                if (!Engine.TryDequeueEvent(out ev)) break;
                Raise(ev);
            }

            // 3. Close the output once the engine has been silent for a while.
            if (Engine.IsIdle)
            {
                long now = Stopwatch.GetTimestamp();
                if (_idleSinceTimestamp == 0)
                    _idleSinceTimestamp = now;
                else if (Stopwatch.GetElapsedTime(_idleSinceTimestamp, now) > IdleShutdownAfter)
                {
                    while (Engine.TryDequeueEvent(out var rest)) Raise(rest);
                    _pump?.Stop();
                    CloseOutput();
                    _idleSinceTimestamp = 0;
                }
            }
            else
            {
                _idleSinceTimestamp = 0;
            }
        }

        // A throwing subscriber must not kill the pump or starve the other subscribers.
        private void Raise(DrumEngineEvent ev)
        {
            var handlers = PositionChanged;
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { ((Action<DrumEngineEvent>)handler)(ev); }
                catch (Exception ex) { _logger?.LogError(ex, "Metronome: PositionChanged handler threw"); }
            }
        }

        private void CloseOutput()
        {
            var output = _output;
            _output = null;
            if (output == null) return;
            output.PlaybackStopped -= OnPlaybackStopped;
            try { output.Stop(); } catch { /* best effort */ }
            try { output.Dispose(); } catch { /* best effort */ }
        }
    }
}
