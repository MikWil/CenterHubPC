using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.ViewModel;
using System.Windows.Controls;

namespace CenterHubNew.MVVM.View
{
    /// <summary>
    /// Interaction logic for SoundView.xaml
    /// </summary>
    public partial class SoundView : UserControl
    {
        //readonly ClickerSettings Clickersettings = new ClickerSettings("1000");

        public SoundView()
        {
            InitializeComponent();

            this.DataContext = new SoundViewModel();
        }

        private void OpenSoundControlsWindow_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            SoundControlsWindow.ShowSingleton();
        }
    }
}
