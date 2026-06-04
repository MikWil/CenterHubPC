using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterHubNew.MVVM.Models
{
    /// <summary>
    /// Identifies a bindable action in CenterHub.
    /// </summary>
    public enum HotkeyAction
    {
        // App
        AppShowHide,

        // Auto Clicker
        AutoClickerStartStop,
        AutoClickerCapturePosition,

        // Audio
        AudioToggleMic,
        AudioNextProfile,
        AudioPrevProfile,

        // Clipboard
        ClipboardToggleMonitoring,

        // Soundboard
        SoundboardPlay1,
        SoundboardPlay2,
        SoundboardPlay3,
        SoundboardStopPlayback,

        // Standing Timer
        StandingTimerStartStop,

        // Window Layouts (apply slot N)
        ApplyLayout1,
        ApplyLayout2,
        ApplyLayout3,
    }

    /// <summary>
    /// Represents a single hotkey binding: an action paired with a key combination.
    /// </summary>
    public partial class HotkeyBinding : ObservableObject
    {
        /// <summary>The action this binding triggers.</summary>
        public HotkeyAction Action { get; init; }

        /// <summary>Human-readable name shown in the UI.</summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>Category for grouping in the settings page.</summary>
        public string Category { get; init; } = string.Empty;

        [ObservableProperty]
        private Key _key = Key.None;

        [ObservableProperty]
        private KeyModifiers _modifiers = KeyModifiers.None;

        /// <summary>True when the OS registration succeeded.</summary>
        [ObservableProperty]
        private bool _isRegistered;

        /// <summary>True while the UI is waiting for the user to press a key combo.</summary>
        [ObservableProperty]
        private bool _isRecording;

        /// <summary>Whether registration failed (e.g. combo taken by another app).</summary>
        [ObservableProperty]
        private bool _hasFailed;

        /// <summary>Returns true when the binding has a key assigned.</summary>
        public bool IsBound => Key != Key.None;

        /// <summary>
        /// Friendly display string for the key combo, e.g. "Ctrl + Shift + M".
        /// </summary>
        public string DisplayString
        {
            get
            {
                if (Key == Key.None) return "Not Set";

                var parts = new System.Collections.Generic.List<string>();
                if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
                if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
                if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
                if (Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
                parts.Add(KeyToString(Key));
                return string.Join(" + ", parts);
            }
        }

        partial void OnKeyChanged(Key value)
        {
            OnPropertyChanged(nameof(IsBound));
            OnPropertyChanged(nameof(DisplayString));
        }

        partial void OnModifiersChanged(KeyModifiers value)
        {
            OnPropertyChanged(nameof(DisplayString));
        }

        private static string KeyToString(Key key)
        {
            return key switch
            {
                >= Key.D0 and <= Key.D9 => key.ToString()[1..], // D0-D9 → 0-9
                >= Key.NumPad0 and <= Key.NumPad9 => "Num" + key.ToString().Replace("NumPad", ""),
                Key.OemPlus => "+",
                Key.OemMinus => "-",
                Key.OemPeriod => ".",
                Key.OemComma => ",",
                Key.OemTilde => "~",
                Key.OemQuestion => "?",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemPipe => "\\",
                Key.OemSemicolon => ";",
                Key.OemQuotes => "'",
                _ => key.ToString()
            };
        }
    }

    /// <summary>
    /// Serializable DTO for persisting hotkey settings to JSON.
    /// </summary>
    public class HotkeyBindingDto
    {
        public string Action { get; set; } = string.Empty;
        public string? Key { get; set; }
        public string? Modifiers { get; set; }
    }

    /// <summary>
    /// Root object for hotkeys.json serialization.
    /// </summary>
    public class HotkeySettingsDto
    {
        public bool HotkeysEnabled { get; set; } = true;
        public System.Collections.Generic.List<HotkeyBindingDto> Bindings { get; set; } = new();
    }
}
