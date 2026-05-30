using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CenterHubNew.MVVM.Services
{
    public enum ClickMode
    {
        /// <summary>Click at a fixed (X,Y) screen position via PostMessage — does not move the cursor.</summary>
        SilentFixed,
        /// <summary>Move the real cursor to (X,Y) and click — for apps that ignore synthetic messages.</summary>
        TeleportFixed,
        /// <summary>Click wherever the user's cursor currently is — never teleports.</summary>
        Follow,
    }

    public enum MouseClickButton
    {
        Left,
        Right,
        Middle,
    }

    public sealed class AutoClickerOptions
    {
        public int X { get; init; }
        public int Y { get; init; }
        public double IntervalSeconds { get; init; } = 1.0;
        public int MaxClicks { get; init; }            // 0 = infinite
        public ClickMode Mode { get; init; } = ClickMode.SilentFixed;
        public MouseClickButton Button { get; init; } = MouseClickButton.Left;
        public TimeSpan ArmDelay { get; init; } = TimeSpan.FromSeconds(3);
        public int JitterPixels { get; init; }          // ±N px random offset
        public bool FailsafeEnabled { get; init; } = true;
    }

    public sealed class AutoClickerService : IDisposable
    {
        private readonly ILogger<AutoClickerService>? _logger;
        private CancellationTokenSource? _cts;
        private volatile bool _isRunning;
        private int _clicksDelivered;
        private DateTime _startedAt;

        public bool IsRunning => _isRunning;
        public int ClicksDelivered => _clicksDelivered;
        public TimeSpan Elapsed => _isRunning ? DateTime.Now - _startedAt : TimeSpan.Zero;

        /// <summary>Fires after each click is dispatched, with the new total.</summary>
        public event Action<int>? ClickDelivered;
        /// <summary>Fires when the service finishes (limit reached, stop, failsafe, error).</summary>
        public event Action<string>? Stopped;
        /// <summary>Fires every second during the arm countdown with seconds remaining.</summary>
        public event Action<int>? Arming;

        // ============== Win32 ==============

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT p);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr ChildWindowFromPoint(IntPtr hWndParent, POINT pt);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
            public static int Size => Marshal.SizeOf<INPUT>();
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;

        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP   = 0x0202;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP   = 0x0205;
        private const uint WM_MBUTTONDOWN = 0x0207;
        private const uint WM_MBUTTONUP   = 0x0208;

        private const int MK_LBUTTON = 0x0001;
        private const int MK_RBUTTON = 0x0002;
        private const int MK_MBUTTON = 0x0010;

        public AutoClickerService(ILogger<AutoClickerService>? logger = null)
        {
            _logger = logger;
        }

        public void Start(AutoClickerOptions options)
        {
            if (_isRunning)
            {
                _logger?.LogWarning("AutoClicker already running — ignoring Start");
                return;
            }
            if (options.IntervalSeconds <= 0.005)
            {
                _logger?.LogWarning("Interval too small; clamping to 5ms");
            }

            _cts = new CancellationTokenSource();
            _isRunning = true;
            _clicksDelivered = 0;
            _startedAt = DateTime.Now;
            var token = _cts.Token;

            Task.Run(async () => await RunLoopAsync(options, token).ConfigureAwait(false), token);
        }

        public void Stop() => StopInternal("manual stop");

        private void StopInternal(string reason)
        {
            if (!_isRunning) return;
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            _isRunning = false;
            _logger?.LogInformation("AutoClicker stopped: {Reason}", reason);
            try { Stopped?.Invoke(reason); } catch { /* observers don't break us */ }
        }

        private async Task RunLoopAsync(AutoClickerOptions opts, CancellationToken token)
        {
            try
            {
                // === ARM countdown ===
                var armTotal = (int)Math.Ceiling(opts.ArmDelay.TotalSeconds);
                for (int i = armTotal; i > 0; i--)
                {
                    if (token.IsCancellationRequested) return;
                    try { Arming?.Invoke(i); } catch { }
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }
                try { Arming?.Invoke(0); } catch { }

                var rng = new Random();
                var intervalMs = Math.Max(5, (int)(opts.IntervalSeconds * 1000));

                while (!token.IsCancellationRequested)
                {
                    // === FAILSAFE check (cursor in top-left corner aborts) ===
                    if (opts.FailsafeEnabled && CursorIsInFailsafeCorner())
                    {
                        StopInternal("failsafe — cursor in top-left corner");
                        return;
                    }

                    // === Resolve target point ===
                    int targetX, targetY;
                    if (opts.Mode == ClickMode.Follow)
                    {
                        GetCursorPos(out var cur);
                        targetX = cur.X;
                        targetY = cur.Y;
                    }
                    else
                    {
                        targetX = opts.X;
                        targetY = opts.Y;
                    }

                    if (opts.JitterPixels > 0)
                    {
                        targetX += rng.Next(-opts.JitterPixels, opts.JitterPixels + 1);
                        targetY += rng.Next(-opts.JitterPixels, opts.JitterPixels + 1);
                    }

                    // === Dispatch the click ===
                    try
                    {
                        switch (opts.Mode)
                        {
                            case ClickMode.SilentFixed:
                                ClickSilent(targetX, targetY, opts.Button);
                                break;
                            case ClickMode.TeleportFixed:
                                ClickViaSendInput(targetX, targetY, opts.Button, moveFirst: true);
                                break;
                            case ClickMode.Follow:
                                // user's cursor is already there — just press the button at current pos
                                ClickViaSendInput(targetX, targetY, opts.Button, moveFirst: false);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error dispatching click");
                    }

                    Interlocked.Increment(ref _clicksDelivered);
                    try { ClickDelivered?.Invoke(_clicksDelivered); } catch { }

                    if (opts.MaxClicks > 0 && _clicksDelivered >= opts.MaxClicks)
                    {
                        StopInternal($"reached limit ({opts.MaxClicks} clicks)");
                        return;
                    }

                    try
                    {
                        await Task.Delay(intervalMs, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException) { return; }
                }
            }
            catch (OperationCanceledException) { /* normal shutdown */ }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fatal error in AutoClicker loop");
                StopInternal($"error: {ex.Message}");
            }
            finally
            {
                _isRunning = false;
            }
        }

        // ---- Silent click via PostMessage (does NOT move cursor) ----
        private static void ClickSilent(int screenX, int screenY, MouseClickButton button)
        {
            var pt = new POINT { X = screenX, Y = screenY };
            var hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return;

            // descend into child window under point for more accurate hit
            var child = ChildWindowFromPoint(hwnd, ToClient(hwnd, pt));
            if (child != IntPtr.Zero) hwnd = child;

            var client = pt;
            ScreenToClient(hwnd, ref client);
            var lParam = MakeLParam(client.X, client.Y);

            switch (button)
            {
                case MouseClickButton.Left:
                    PostMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
                    PostMessage(hwnd, WM_LBUTTONUP,   IntPtr.Zero,        lParam);
                    break;
                case MouseClickButton.Right:
                    PostMessage(hwnd, WM_RBUTTONDOWN, (IntPtr)MK_RBUTTON, lParam);
                    PostMessage(hwnd, WM_RBUTTONUP,   IntPtr.Zero,        lParam);
                    break;
                case MouseClickButton.Middle:
                    PostMessage(hwnd, WM_MBUTTONDOWN, (IntPtr)MK_MBUTTON, lParam);
                    PostMessage(hwnd, WM_MBUTTONUP,   IntPtr.Zero,        lParam);
                    break;
            }
        }

        // ---- Real click via SendInput (works in games / DirectInput apps) ----
        private static void ClickViaSendInput(int screenX, int screenY, MouseClickButton button, bool moveFirst)
        {
            if (moveFirst)
            {
                SetCursorPos(screenX, screenY);
                // tiny settle delay so the OS registers the move before the click
                Thread.Sleep(2);
            }

            uint down, up;
            switch (button)
            {
                case MouseClickButton.Right:  down = MOUSEEVENTF_RIGHTDOWN;  up = MOUSEEVENTF_RIGHTUP;  break;
                case MouseClickButton.Middle: down = MOUSEEVENTF_MIDDLEDOWN; up = MOUSEEVENTF_MIDDLEUP; break;
                default:                      down = MOUSEEVENTF_LEFTDOWN;   up = MOUSEEVENTF_LEFTUP;   break;
            }

            var inputs = new INPUT[]
            {
                new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = down } } },
                new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = up   } } },
            };
            SendInput((uint)inputs.Length, inputs, INPUT.Size);
        }

        private static bool CursorIsInFailsafeCorner()
        {
            GetCursorPos(out var p);
            return p.X <= 2 && p.Y <= 2;
        }

        private static POINT ToClient(IntPtr hwnd, POINT screen)
        {
            var p = screen;
            ScreenToClient(hwnd, ref p);
            return p;
        }

        private static IntPtr MakeLParam(int low, int high)
            => (IntPtr)((high << 16) | (low & 0xFFFF));

        public static (int X, int Y) GetCurrentMousePosition()
        {
            GetCursorPos(out var p);
            return (p.X, p.Y);
        }

        public void Dispose()
        {
            try { StopInternal("dispose"); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }
    }
}
