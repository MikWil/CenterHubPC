using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Manages system-wide global hotkeys. Supports dynamic registration, persistence,
    /// and dispatching to registered callbacks.
    /// Intended to be a singleton for the lifetime of the application.
    /// </summary>
    public class GlobalHotkeyService : IDisposable
    {
        // ── Win32 interop ──────────────────────────────────────────────────
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Win32 modifier flags
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        // ── State ──────────────────────────────────────────────────────────
        private IntPtr _windowHandle;
        private HwndSource? _source;
        private bool _initialized;
        private bool _disposed;

        private readonly ILogger<GlobalHotkeyService>? _logger;
        private readonly string _settingsPath;

        /// <summary>All bindable actions and their current key assignment.</summary>
        private readonly List<HotkeyBinding> _bindings = new();

        /// <summary>Maps registered Win32 hotkey id → action.</summary>
        private readonly Dictionary<int, HotkeyAction> _idToAction = new();

        /// <summary>Maps action → callback that should be invoked.</summary>
        private readonly Dictionary<HotkeyAction, Action> _callbacks = new();

        /// <summary>Next free Win32 hotkey id.</summary>
        private int _nextId = 1;

        /// <summary>Master toggle. When false all hotkeys are unregistered from the OS.</summary>
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

        // ── Constructor ────────────────────────────────────────────────────
        public GlobalHotkeyService(ILogger<GlobalHotkeyService>? logger = null)
        {
            _logger = logger;
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hotkeys.json");
            InitializeDefaultBindings();
            Load();
        }

        // ── Initialization (must be called once the main window handle exists) ──
        public void Initialize(IntPtr windowHandle)
        {
            if (_initialized) return;

            _windowHandle = windowHandle;
            _source = HwndSource.FromHwnd(windowHandle);
            _source?.AddHook(HwndHook);
            _initialized = true;

            if (_hotkeysEnabled)
            {
                RegisterAllBindings();
            }

            _logger?.LogInformation("GlobalHotkeyService initialized with {Count} bindings", _bindings.Count);
        }

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>
        /// Register a callback for a given action. Can be called before Initialize().
        /// </summary>
        public void SetCallback(HotkeyAction action, Action callback)
        {
            _callbacks[action] = callback;
        }

        /// <summary>
        /// Remove the callback for a given action.
        /// </summary>
        public void RemoveCallback(HotkeyAction action)
        {
            _callbacks.Remove(action);
        }

        /// <summary>
        /// Assign a new key combination to an action. Unregisters the old combo and
        /// registers the new one with the OS.
        /// </summary>
        /// <returns>True if the OS accepted the registration (or key is None = clear).</returns>
        public bool UpdateBinding(HotkeyAction action, Key key, ModifierKeys modifiers)
        {
            var binding = _bindings.FirstOrDefault(b => b.Action == action);
            if (binding == null) return false;

            // Unregister old combo
            if (binding.IsRegistered)
            {
                UnregisterBinding(binding);
            }

            binding.Key = key;
            binding.Modifiers = modifiers;
            binding.HasFailed = false;

            if (key != Key.None && _initialized && _hotkeysEnabled)
            {
                var success = RegisterBinding(binding);
                if (!success)
                {
                    binding.HasFailed = true;
                    _logger?.LogWarning("Failed to register hotkey {Action}: {Combo}", action, binding.DisplayString);
                    return false;
                }
            }

            Save();
            return true;
        }

        /// <summary>
        /// Clear a binding (remove the key assignment).
        /// </summary>
        public void ClearBinding(HotkeyAction action)
        {
            UpdateBinding(action, Key.None, ModifierKeys.None);
        }

        /// <summary>
        /// Check if a key combo is already in use by another action.
        /// Returns the conflicting action, or null if no conflict.
        /// </summary>
        public HotkeyBinding? FindConflict(HotkeyAction excludeAction, Key key, ModifierKeys modifiers)
        {
            if (key == Key.None) return null;
            return _bindings.FirstOrDefault(b =>
                b.Action != excludeAction &&
                b.Key == key &&
                b.Modifiers == modifiers);
        }

        /// <summary>
        /// Reset all bindings to defaults.
        /// </summary>
        public void ResetToDefaults()
        {
            UnregisterAllBindings();
            foreach (var binding in _bindings)
            {
                binding.Key = Key.None;
                binding.Modifiers = ModifierKeys.None;
                binding.IsRegistered = false;
                binding.HasFailed = false;
            }

            // Apply defaults
            ApplyDefaults();

            if (_initialized && _hotkeysEnabled)
            {
                RegisterAllBindings();
            }

            Save();
        }

        // ── Private helpers ────────────────────────────────────────────────

        private void InitializeDefaultBindings()
        {
            // App
            Add(HotkeyAction.AppShowHide, "Show / Hide Window", "APP");

            // Auto Clicker
            Add(HotkeyAction.AutoClickerStartStop, "Start / Stop", "AUTO CLICKER");
            Add(HotkeyAction.AutoClickerCapturePosition, "Capture Position", "AUTO CLICKER");

            // Audio
            Add(HotkeyAction.AudioToggleMic, "Mute / Unmute Mic", "AUDIO");
            Add(HotkeyAction.AudioNextProfile, "Next Sound Profile", "AUDIO");
            Add(HotkeyAction.AudioPrevProfile, "Previous Sound Profile", "AUDIO");

            // Clipboard
            Add(HotkeyAction.ClipboardToggleMonitoring, "Toggle Monitoring", "CLIPBOARD");

            // Soundboard
            Add(HotkeyAction.SoundboardPlay1, "Play Sound 1", "SOUNDBOARD");
            Add(HotkeyAction.SoundboardPlay2, "Play Sound 2", "SOUNDBOARD");
            Add(HotkeyAction.SoundboardPlay3, "Play Sound 3", "SOUNDBOARD");
            Add(HotkeyAction.SoundboardStopPlayback, "Stop Playback", "SOUNDBOARD");

            // Standing Timer
            Add(HotkeyAction.StandingTimerStartStop, "Start / Stop", "STANDING TIMER");

            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            // Only auto-clicker has defaults
            SetDefault(HotkeyAction.AutoClickerStartStop, Key.K, ModifierKeys.Control);
            SetDefault(HotkeyAction.AutoClickerCapturePosition, Key.P, ModifierKeys.Control);
        }

        private void SetDefault(HotkeyAction action, Key key, ModifierKeys modifiers)
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
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(binding.Key);
            int id = _nextId++;

            bool success = RegisterHotKey(_windowHandle, id, modifiers, vk);
            if (success)
            {
                _idToAction[id] = binding.Action;
                binding.IsRegistered = true;
                binding.HasFailed = false;
                _logger?.LogDebug("Registered hotkey id={Id} for {Action}: {Combo}", id, binding.Action, binding.DisplayString);
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
            {
                RegisterBinding(binding);
            }
        }

        private void UnregisterAllBindings()
        {
            if (!_initialized || _windowHandle == IntPtr.Zero) return;

            foreach (var id in _idToAction.Keys.ToList())
            {
                UnregisterHotKey(_windowHandle, id);
            }
            _idToAction.Clear();

            foreach (var binding in _bindings)
            {
                binding.IsRegistered = false;
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();

                if (_idToAction.TryGetValue(id, out var action))
                {
                    if (_callbacks.TryGetValue(action, out var callback))
                    {
                        Application.Current?.Dispatcher.Invoke(callback);
                        handled = true;
                        _logger?.LogDebug("Hotkey fired: {Action}", action);
                    }
                }
            }

            return IntPtr.Zero;
        }

        private static uint ToWin32Modifiers(ModifierKeys modifiers)
        {
            uint result = 0;
            if (modifiers.HasFlag(ModifierKeys.Alt)) result |= MOD_ALT;
            if (modifiers.HasFlag(ModifierKeys.Control)) result |= MOD_CONTROL;
            if (modifiers.HasFlag(ModifierKeys.Shift)) result |= MOD_SHIFT;
            if (modifiers.HasFlag(ModifierKeys.Windows)) result |= MOD_WIN;
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
                        Modifiers = b.Modifiers == ModifierKeys.None ? null : b.Modifiers.ToString()
                    }).ToList()
                };

                var json = JsonConvert.SerializeObject(dto, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
                _logger?.LogDebug("Hotkey settings saved to {Path}", _settingsPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save hotkey settings");
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    _logger?.LogInformation("No hotkeys.json found, using defaults");
                    return;
                }

                var json = File.ReadAllText(_settingsPath);
                var dto = JsonConvert.DeserializeObject<HotkeySettingsDto>(json);
                if (dto == null) return;

                _hotkeysEnabled = dto.HotkeysEnabled;

                foreach (var bdto in dto.Bindings)
                {
                    if (!Enum.TryParse<HotkeyAction>(bdto.Action, out var action)) continue;

                    var binding = _bindings.FirstOrDefault(b => b.Action == action);
                    if (binding == null) continue;

                    if (bdto.Key != null && Enum.TryParse<Key>(bdto.Key, out var key))
                    {
                        binding.Key = key;
                    }
                    else
                    {
                        binding.Key = Key.None;
                    }

                    if (bdto.Modifiers != null && Enum.TryParse<ModifierKeys>(bdto.Modifiers, out var mods))
                    {
                        binding.Modifiers = mods;
                    }
                    else
                    {
                        binding.Modifiers = ModifierKeys.None;
                    }
                }

                _logger?.LogInformation("Hotkey settings loaded from {Path}", _settingsPath);
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
            _source?.RemoveHook(HwndHook);
            _callbacks.Clear();
            _logger?.LogInformation("GlobalHotkeyService disposed");
        }
    }
}
