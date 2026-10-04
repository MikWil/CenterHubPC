using Avalonia.Input;
using Avalonia.Threading;
using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Manages system-wide global hotkeys. Uses Win32 RegisterHotKey and WndProc subclassing.
    /// Must be initialized after the main window handle is available.
    /// </summary>
    public class GlobalHotkeyService : IDisposable
    {
        // ── Win32 interop ──────────────────────────────────────────────────
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newValue);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtrDelegate(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const int GWLP_WNDPROC = -4;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint WM_HOTKEY = 0x0312;

        // ── State ──────────────────────────────────────────────────────────
        private IntPtr _windowHandle;
        private WndProcDelegate? _newWndProc;
        private IntPtr _oldWndProc;
        private bool _initialized;
        private bool _disposed;

        private readonly ILogger<GlobalHotkeyService>? _logger;
        private readonly string _settingsPath;

        private readonly List<HotkeyBinding> _bindings = new();
        private readonly Dictionary<int, HotkeyAction> _idToAction = new();
        private readonly Dictionary<HotkeyAction, Action> _callbacks = new();
        private int _nextId = 1;

        private bool _hotkeysEnabled = true;

        public bool HotkeysEnabled
        {
            get => _hotkeysEnabled;
            set
            {
                if (_hotkeysEnabled == value) return;
                _hotkeysEnabled = value;
                if (_initialized)
                {
                    if (value) RegisterAllBindings();
                    else UnregisterAllBindings();
                }
                Save();
            }
        }

        public IReadOnlyList<HotkeyBinding> Bindings => _bindings.AsReadOnly();

        public GlobalHotkeyService(ILogger<GlobalHotkeyService>? logger = null)
        {
            _logger = logger;
            // Per-user AppData, not the install folder: Program Files isn't writable for a
            // standard user (bindings silently never saved) and MSI upgrades wipe it.
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CenterHub");
            Directory.CreateDirectory(folder);
            _settingsPath = Path.Combine(folder, "hotkeys.json");
            MigrateLegacySettings();
            InitializeDefaultBindings();
            Load();
        }

        public void Initialize(IntPtr windowHandle)
        {
            if (_initialized) return;

            _windowHandle = windowHandle;

            // Subclass the WndProc to intercept WM_HOTKEY messages
            _newWndProc = new WndProcDelegate(HwndHook);
            _oldWndProc = SetWindowLongPtrDelegate(windowHandle, GWLP_WNDPROC, _newWndProc);

            _initialized = true;

            if (_hotkeysEnabled)
                RegisterAllBindings();

            _logger?.LogInformation("GlobalHotkeyService initialized with {Count} bindings", _bindings.Count);
        }

        public void SetCallback(HotkeyAction action, Action callback)
        {
            _callbacks[action] = callback;
        }

        public void RemoveCallback(HotkeyAction action)
        {
            _callbacks.Remove(action);
        }

        public bool UpdateBinding(HotkeyAction action, Key key, KeyModifiers modifiers)
        {
            var binding = _bindings.FirstOrDefault(b => b.Action == action);
            if (binding == null) return false;

            if (binding.IsRegistered)
                UnregisterBinding(binding);

            binding.Key = key;
            binding.Modifiers = modifiers;
            binding.HasFailed = false;

            if (key != Key.None && _initialized && _hotkeysEnabled)
            {
                var success = RegisterBinding(binding);
                if (!success)
                {
                    binding.HasFailed = true;
                    return false;
                }
            }

            Save();
            return true;
        }

        public void ClearBinding(HotkeyAction action)
        {
            UpdateBinding(action, Key.None, KeyModifiers.None);
        }

        public HotkeyBinding? FindConflict(HotkeyAction excludeAction, Key key, KeyModifiers modifiers)
        {
            if (key == Key.None) return null;
            return _bindings.FirstOrDefault(b =>
                b.Action != excludeAction && b.Key == key && b.Modifiers == modifiers);
        }

        public void ResetToDefaults()
        {
            UnregisterAllBindings();
            foreach (var binding in _bindings)
            {
                binding.Key = Key.None;
                binding.Modifiers = KeyModifiers.None;
                binding.IsRegistered = false;
                binding.HasFailed = false;
            }
            ApplyDefaults();
            if (_initialized && _hotkeysEnabled)
                RegisterAllBindings();
            Save();
        }

        // ── Private helpers ────────────────────────────────────────────────

        private void InitializeDefaultBindings()
        {
            Add(HotkeyAction.AppShowHide, "Show / Hide Window", "APP");
            Add(HotkeyAction.AppCommandPalette, "Command Palette", "APP");
            Add(HotkeyAction.AutoClickerStartStop, "Start / Stop", "AUTO CLICKER");
            Add(HotkeyAction.AutoClickerCapturePosition, "Capture Position", "AUTO CLICKER");
            Add(HotkeyAction.AudioToggleMic, "Mute / Unmute Mic", "AUDIO");
            Add(HotkeyAction.AudioNextProfile, "Next Sound Preset", "AUDIO");
            Add(HotkeyAction.AudioPrevProfile, "Previous Sound Preset", "AUDIO");
            Add(HotkeyAction.ClipboardToggleMonitoring, "Toggle Monitoring", "CLIPBOARD");
            Add(HotkeyAction.SoundboardPlay1, "Play Sound 1", "SOUNDBOARD");
            Add(HotkeyAction.SoundboardPlay2, "Play Sound 2", "SOUNDBOARD");
            Add(HotkeyAction.SoundboardPlay3, "Play Sound 3", "SOUNDBOARD");
            Add(HotkeyAction.SoundboardStopPlayback, "Stop Playback", "SOUNDBOARD");
            Add(HotkeyAction.StandingTimerStartStop, "Start / Stop", "STANDING TIMER");
            Add(HotkeyAction.ApplyLayout1, "Apply Layout 1", "WINDOW LAYOUTS");
            Add(HotkeyAction.ApplyLayout2, "Apply Layout 2", "WINDOW LAYOUTS");
            Add(HotkeyAction.ApplyLayout3, "Apply Layout 3", "WINDOW LAYOUTS");
            Add(HotkeyAction.MetronomeStartStop, "Start / Stop", "METRONOME");
            Add(HotkeyAction.MetronomeFill, "Drum Fill", "METRONOME");
            Add(HotkeyAction.MetronomeNextPart, "Next Song Part", "METRONOME");
            Add(HotkeyAction.MetronomeTapTempo, "Tap Tempo", "METRONOME");
            Add(HotkeyAction.MetronomeNextSong, "Next Setlist Song", "METRONOME");
            Add(HotkeyAction.MetronomePrevSong, "Previous Setlist Song", "METRONOME");
            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            SetDefault(HotkeyAction.AutoClickerStartStop, Key.K, KeyModifiers.Control);
            SetDefault(HotkeyAction.AutoClickerCapturePosition, Key.P, KeyModifiers.Control);
        }

        private void SetDefault(HotkeyAction action, Key key, KeyModifiers modifiers)
        {
            var binding = _bindings.FirstOrDefault(b => b.Action == action);
            if (binding != null)
            {
                binding.Key = key;
                binding.Modifiers = modifiers;
            }
        }

        private void Add(HotkeyAction action, string displayName, string category)
        {
            _bindings.Add(new HotkeyBinding
            {
                Action = action,
                DisplayName = displayName,
                Category = category
            });
        }

        private bool RegisterBinding(HotkeyBinding binding)
        {
            if (!_initialized || _windowHandle == IntPtr.Zero || binding.Key == Key.None)
                return false;

            uint modifiers = ToWin32Modifiers(binding.Modifiers) | MOD_NOREPEAT;
            // Avalonia's Key enum follows the WPF layout (Key.A = 44), NOT Win32 VK codes
            // (VK_A = 0x41). Casting directly registered the wrong key for every hotkey.
            uint vk = KeyToVirtualKey(binding.Key);
            if (vk == 0)
            {
                _logger?.LogWarning("Hotkey key {Key} has no Win32 virtual-key mapping; skipping", binding.Key);
                return false;
            }
            int id = _nextId++;

            bool success = RegisterHotKey(_windowHandle, id, modifiers, vk);
            if (success)
            {
                _idToAction[id] = binding.Action;
                binding.IsRegistered = true;
                binding.HasFailed = false;
            }
            else
            {
                _logger?.LogWarning("OS rejected hotkey for {Action}: {Combo}", binding.Action, binding.DisplayString);
            }
            return success;
        }

        private void UnregisterBinding(HotkeyBinding binding)
        {
            if (!_initialized || _windowHandle == IntPtr.Zero) return;

            var idsToRemove = _idToAction
                .Where(kv => kv.Value == binding.Action)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var id in idsToRemove)
            {
                UnregisterHotKey(_windowHandle, id);
                _idToAction.Remove(id);
            }
            binding.IsRegistered = false;
        }

        private void RegisterAllBindings()
        {
            foreach (var binding in _bindings.Where(b => b.IsBound))
                RegisterBinding(binding);
        }

        private void UnregisterAllBindings()
        {
            if (!_initialized || _windowHandle == IntPtr.Zero) return;

            foreach (var id in _idToAction.Keys.ToList())
                UnregisterHotKey(_windowHandle, id);

            _idToAction.Clear();

            foreach (var binding in _bindings)
                binding.IsRegistered = false;
        }

        private IntPtr HwndHook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_idToAction.TryGetValue(id, out var action) &&
                    _callbacks.TryGetValue(action, out var callback))
                {
                    try { Dispatcher.UIThread.Post(callback); }
                    catch (InvalidOperationException) { }
                    _logger?.LogDebug("Hotkey fired: {Action}", action);
                }
            }
            return CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam);
        }

        private static uint ToWin32Modifiers(KeyModifiers modifiers)
        {
            uint result = 0;
            if (modifiers.HasFlag(KeyModifiers.Alt)) result |= MOD_ALT;
            if (modifiers.HasFlag(KeyModifiers.Control)) result |= MOD_CONTROL;
            if (modifiers.HasFlag(KeyModifiers.Shift)) result |= MOD_SHIFT;
            if (modifiers.HasFlag(KeyModifiers.Meta)) result |= MOD_WIN;
            return result;
        }

        // ── Persistence ────────────────────────────────────────────────────

        public void Save()
        {
            try
            {
                var dto = new HotkeySettingsDto
                {
                    HotkeysEnabled = _hotkeysEnabled,
                    Bindings = _bindings.Select(b => new HotkeyBindingDto
                    {
                        Action = b.Action.ToString(),
                        Key = b.Key == Key.None ? null : b.Key.ToString(),
                        Modifiers = b.Modifiers == KeyModifiers.None ? null : b.Modifiers.ToString()
                    }).ToList()
                };
                var json = JsonConvert.SerializeObject(dto, Formatting.Indented);
                AtomicFile.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save hotkey settings");
            }
        }

        /// <summary>Copy hotkeys.json from the old install-folder location once, if present.</summary>
        private void MigrateLegacySettings()
        {
            try
            {
                var legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hotkeys.json");
                if (!File.Exists(_settingsPath) && File.Exists(legacy))
                {
                    File.Copy(legacy, _settingsPath);
                    _logger?.LogInformation("Migrated hotkey settings from {Legacy}", legacy);
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Hotkey settings migration failed"); }
        }

        /// <summary>
        /// Map an Avalonia <see cref="Key"/> to a Win32 virtual-key code for RegisterHotKey.
        /// Returns 0 for keys that can't be registered as a global hotkey.
        /// </summary>
        public static uint KeyToVirtualKey(Key key)
        {
            if (key >= Key.A && key <= Key.Z) return 0x41u + (uint)(key - Key.A);
            if (key >= Key.D0 && key <= Key.D9) return 0x30u + (uint)(key - Key.D0);
            if (key >= Key.F1 && key <= Key.F24) return 0x70u + (uint)(key - Key.F1);
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return 0x60u + (uint)(key - Key.NumPad0);

            return key switch
            {
                Key.Back => 0x08, Key.Tab => 0x09, Key.Enter => 0x0D, Key.Pause => 0x13,
                Key.CapsLock => 0x14, Key.Escape => 0x1B, Key.Space => 0x20,
                Key.PageUp => 0x21, Key.PageDown => 0x22, Key.End => 0x23, Key.Home => 0x24,
                Key.Left => 0x25, Key.Up => 0x26, Key.Right => 0x27, Key.Down => 0x28,
                Key.PrintScreen => 0x2C, Key.Insert => 0x2D, Key.Delete => 0x2E,
                Key.Multiply => 0x6A, Key.Add => 0x6B, Key.Separator => 0x6C,
                Key.Subtract => 0x6D, Key.Decimal => 0x6E, Key.Divide => 0x6F,
                Key.NumLock => 0x90, Key.Scroll => 0x91,
                Key.VolumeMute => 0xAD, Key.VolumeDown => 0xAE, Key.VolumeUp => 0xAF,
                Key.MediaNextTrack => 0xB0, Key.MediaPreviousTrack => 0xB1,
                Key.MediaStop => 0xB2, Key.MediaPlayPause => 0xB3,
                Key.OemSemicolon => 0xBA, Key.OemPlus => 0xBB, Key.OemComma => 0xBC,
                Key.OemMinus => 0xBD, Key.OemPeriod => 0xBE, Key.OemQuestion => 0xBF,
                Key.OemTilde => 0xC0, Key.OemOpenBrackets => 0xDB, Key.OemPipe => 0xDC,
                Key.OemCloseBrackets => 0xDD, Key.OemQuotes => 0xDE, Key.OemBackslash => 0xE2,
                _ => 0
            };
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;

                var json = File.ReadAllText(_settingsPath);
                var dto = JsonConvert.DeserializeObject<HotkeySettingsDto>(json);
                if (dto == null) return;

                _hotkeysEnabled = dto.HotkeysEnabled;

                foreach (var bdto in dto.Bindings)
                {
                    if (!Enum.TryParse<HotkeyAction>(bdto.Action, out var action)) continue;
                    var binding = _bindings.FirstOrDefault(b => b.Action == action);
                    if (binding == null) continue;

                    binding.Key = bdto.Key != null && Enum.TryParse<Key>(bdto.Key, out var key)
                        ? key : Key.None;
                    binding.Modifiers = bdto.Modifiers != null && Enum.TryParse<KeyModifiers>(bdto.Modifiers, out var mods)
                        ? mods : KeyModifiers.None;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load hotkey settings, using defaults");
            }
        }

        // ── IDisposable ────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            UnregisterAllBindings();

            // Restore original WndProc
            if (_initialized && _windowHandle != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, _oldWndProc);
            }

            _callbacks.Clear();
            _logger?.LogInformation("GlobalHotkeyService disposed");
        }
    }
}
