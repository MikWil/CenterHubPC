using Avalonia.Input;
using CenterHubNew.MVVM.Models;
using System.Collections.ObjectModel;
using System.Linq;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class HotkeySettingsViewModel : BaseViewModel
    {
        private readonly GlobalHotkeyService _hotkeyService;

        [ObservableProperty]
        private bool _hotkeysEnabled;

        [ObservableProperty]
        private ObservableCollection<HotkeyBinding> _bindings = new();

        /// <summary>The binding currently in "recording" mode, or null.</summary>
        [ObservableProperty]
        private HotkeyBinding? _recordingBinding;

        public HotkeySettingsViewModel(
            GlobalHotkeyService hotkeyService,
            ILogger<HotkeySettingsViewModel>? logger = null) : base(logger)
        {
            _hotkeyService = hotkeyService;
            HotkeysEnabled = _hotkeyService.HotkeysEnabled;

            // Copy the service's bindings into the observable collection
            foreach (var binding in _hotkeyService.Bindings)
            {
                Bindings.Add(binding);
            }

            Logger?.LogInformation("HotkeySettingsViewModel initialized with {Count} bindings", Bindings.Count);
        }

        partial void OnHotkeysEnabledChanged(bool value)
        {
            _hotkeyService.HotkeysEnabled = value;
            var status = value ? "enabled" : "disabled";
            ToastService.Instance.Success($"Global hotkeys {status}");
            Logger?.LogInformation("Global hotkeys {Status}", status);
        }

        /// <summary>
        /// Start recording a new key combo for a binding.
        /// </summary>
        [RelayCommand]
        private void StartRecording(HotkeyBinding? binding)
        {
            if (binding == null) return;

            // Cancel any existing recording
            CancelRecording();

            binding.IsRecording = true;
            RecordingBinding = binding;
            Logger?.LogDebug("Started recording for {Action}", binding.Action);
        }

        /// <summary>
        /// Cancel the current recording.
        /// </summary>
        [RelayCommand]
        private void CancelRecording()
        {
            if (RecordingBinding != null)
            {
                RecordingBinding.IsRecording = false;
                RecordingBinding = null;
            }
        }

        /// <summary>
        /// Called from the View's KeyDown handler when recording a new key combo.
        /// </summary>
        public void HandleKeyDown(Key key, KeyModifiers modifiers)
        {
            if (RecordingBinding == null) return;

            // Escape cancels recording
            if (key == Key.Escape)
            {
                CancelRecording();
                return;
            }

            // Ignore presses of modifier-only keys
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin ||
                key == Key.System)
            {
                return;
            }

            // Normalize system key (Alt combos come as Key.System)
            if (key == Key.System)
            {
                // Already filtered above; this is a safety net
                return;
            }

            var binding = RecordingBinding;
            RecordingBinding = null;
            binding.IsRecording = false;

            // Check for conflicts
            var conflict = _hotkeyService.FindConflict(binding.Action, key, modifiers);
            if (conflict != null)
            {
                var result = System.Windows.Forms.MessageBox.Show(
                    $"'{conflict.DisplayName}' already uses {conflict.DisplayString}.\n\nReassign to '{binding.DisplayName}'?",
                    "Hotkey Conflict",
                    System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Question);

                if (result != System.Windows.Forms.DialogResult.Yes) return;

                // Clear the conflicting binding
                _hotkeyService.ClearBinding(conflict.Action);
                ToastService.Instance.Info($"Cleared hotkey for {conflict.DisplayName}");
            }

            // Apply the new binding
            bool success = _hotkeyService.UpdateBinding(binding.Action, key, modifiers);

            if (success)
            {
                ToastService.Instance.Success($"{binding.DisplayName} set to {binding.DisplayString}");
                Logger?.LogInformation("Hotkey set: {Action} = {Combo}", binding.Action, binding.DisplayString);
            }
            else
            {
                ToastService.Instance.Error($"Could not register {binding.DisplayString} — it may be in use by another application");
                Logger?.LogWarning("Failed to register {Action} = {Key}+{Modifiers}", binding.Action, key, modifiers);
            }
        }

        /// <summary>
        /// Clear the binding for a specific action.
        /// </summary>
        [RelayCommand]
        private void ClearBinding(HotkeyBinding? binding)
        {
            if (binding == null) return;

            _hotkeyService.ClearBinding(binding.Action);
            ToastService.Instance.Info($"Cleared hotkey for {binding.DisplayName}");
            Logger?.LogInformation("Cleared hotkey for {Action}", binding.Action);
        }

        /// <summary>
        /// Reset all bindings to their defaults.
        /// </summary>
        [RelayCommand]
        private void ResetToDefaults()
        {
            var result = System.Windows.Forms.MessageBox.Show(
                "Reset all hotkeys to defaults?\n\nOnly Auto Clicker will have hotkeys assigned (Ctrl+K and Ctrl+P). All other bindings will be cleared.",
                "Reset Hotkeys",
                System.Windows.Forms.MessageBoxButtons.YesNo,
                System.Windows.Forms.MessageBoxIcon.Question);

            if (result != System.Windows.Forms.DialogResult.Yes) return;

            _hotkeyService.ResetToDefaults();
            ToastService.Instance.Success("Hotkeys reset to defaults");
            Logger?.LogInformation("Hotkeys reset to defaults");
        }

        /// <summary>
        /// Get distinct categories for grouping in the UI.
        /// </summary>
        public string[] GetCategories()
        {
            return Bindings.Select(b => b.Category).Distinct().ToArray();
        }

        /// <summary>
        /// Get bindings for a specific category.
        /// </summary>
        public HotkeyBinding[] GetBindingsForCategory(string category)
        {
            return Bindings.Where(b => b.Category == category).ToArray();
        }
    }
}
