using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CenterHubNew.MVVM.Services
{
    public class AutoClickerService
    {
        private readonly ILogger<AutoClickerService>? _logger;
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        public bool IsRunning => _isRunning;

        public AutoClickerService(ILogger<AutoClickerService>? logger = null)
        {
            _logger = logger;
        }

        public void Start(int x, int y, double intervalSeconds)
        {
            if (_isRunning) return;

            _cts = new CancellationTokenSource();
            _isRunning = true;

            Task.Run(async () =>
            {
                _logger?.LogInformation("AutoClicker started at ({X}, {Y}) with interval {Interval}s", x, y, intervalSeconds);

                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        // Move cursor and click
                        SetCursorPos(x, y);
                        mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, UIntPtr.Zero);
                        mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, UIntPtr.Zero);

                        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), _cts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "AutoClicker error");
                    }
                }

                _isRunning = false;
                _logger?.LogInformation("AutoClicker stopped");
            }, _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _isRunning = false;
        }

        public static (int X, int Y) GetCurrentMousePosition()
        {
            GetCursorPos(out POINT point);
            return (point.X, point.Y);
        }
    }
}

