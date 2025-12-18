using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CenterHubNew.MVVM.Services
{
    public class GlobalHotkeyService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_K = 0x4B; // K key
        private const uint VK_P = 0x50; // P key

        private const int HOTKEY_START_STOP = 1;
        private const int HOTKEY_SET_POSITION = 2;

        private IntPtr _windowHandle;
        private HwndSource? _source;
        private bool _isRegistered;

        public event Action? OnStartStopPressed;
        public event Action? OnSetPositionPressed;

        public void Register(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            _source = HwndSource.FromHwnd(windowHandle);
            _source?.AddHook(HwndHook);

            // Register Ctrl+K for start/stop
            RegisterHotKey(_windowHandle, HOTKEY_START_STOP, MOD_CONTROL, VK_K);
            
            // Register Ctrl+P for set position
            RegisterHotKey(_windowHandle, HOTKEY_SET_POSITION, MOD_CONTROL, VK_P);

            _isRegistered = true;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                
                if (id == HOTKEY_START_STOP)
                {
                    OnStartStopPressed?.Invoke();
                    handled = true;
                }
                else if (id == HOTKEY_SET_POSITION)
                {
                    OnSetPositionPressed?.Invoke();
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_isRegistered && _windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, HOTKEY_START_STOP);
                UnregisterHotKey(_windowHandle, HOTKEY_SET_POSITION);
                _source?.RemoveHook(HwndHook);
                _isRegistered = false;
            }
        }
    }
}

