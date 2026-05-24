using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CenterHubNew.MVVM.View
{
    public partial class SoundView : UserControl
    {
        public SoundView()
        {
            InitializeComponent();
        }

        private void OpenSoundControlsWindow_Click(object? sender, RoutedEventArgs e)
        {
            SoundControlsWindow.ShowSingleton();
        }
    }
}
