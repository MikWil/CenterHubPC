using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew.MVVM.View
{
    public partial class HotkeySettingsView : UserControl
    {
        public HotkeySettingsView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Routes physical key presses to the ViewModel while recording a hotkey.
        /// </summary>
        private void UserControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not HotkeySettingsViewModel vm) return;
            if (vm.RecordingBinding == null) return;

            // For Alt-based combos WPF sends Key.System instead of the real key
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;

            vm.HandleKeyDown(key, modifiers);
            e.Handled = true;
        }
    }
}
