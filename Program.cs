using Avalonia;
using System;
using System.Threading;

namespace CenterHubNew
{
    internal sealed class Program
    {
        private const string MutexName = "CenterHubNew_SingleInstance_Mutex";
        private const string ActivateEventName = "CenterHubNew_Activate_Event";

        private static Mutex? _mutex;
        private static bool _ownsMutex;

        /// <summary>
        /// Set by a second instance to ask the already-running instance to surface
        /// its window. The running instance waits on this (see App startup).
        /// </summary>
        public static EventWaitHandle? ActivateRequested { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            // ── Single-instance guard (must happen BEFORE Avalonia starts so a
            //    second process never creates a host or a window) ──
            _mutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);

            if (!_ownsMutex)
            {
                // Another instance already holds the mutex — ask it to come to the
                // foreground, then exit this process immediately.
                try
                {
                    if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
                    {
                        existing.Set();
                        existing.Dispose();
                    }
                }
                catch { /* best effort — exiting regardless */ }

                _mutex.Dispose();
                return;
            }

            // We are the primary instance. Publish the activation event others signal.
            try { ActivateRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName); }
            catch { ActivateRequested = null; }

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("Dispatcher shut down"))
            {
                // Expected race during app shutdown — not a crash
            }
            finally
            {
                try { if (_ownsMutex) _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
                ActivateRequested?.Dispose();
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
