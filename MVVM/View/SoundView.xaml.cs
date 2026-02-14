using System.Windows.Controls;

namespace CenterHubNew.MVVM.View
{
    public partial class SoundView : UserControl
    {
        public SoundView()
        {
            InitializeComponent();
        }

        private void OpenSoundControlsWindow_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SoundControlsWindow.ShowSingleton();
        }
    }
}
