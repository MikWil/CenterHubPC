using Avalonia.Controls;
using Avalonia.Input;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew.MVVM.View
{
    public partial class HotkeySettingsView : UserControl
    {
        public HotkeySettingsView()
        {
            InitializeComponent();
        }

        private void UserControl_KeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not HotkeySettingsViewModel vm) return;
            if (vm.RecordingBinding == null) return;

            vm.HandleKeyDown(e.Key, e.KeyModifiers);
            e.Handled = true;
        }
    }
}
