using System.Windows;
using System.Windows.Controls;

namespace CenterHubNew.MVVM.View
{
    public partial class SoundboardView : UserControl
    {
        public SoundboardView()
        {
            InitializeComponent();
        }

        private void HelpBtn_Click(object sender, RoutedEventArgs e)
        {
            HelpPanel.Visibility = Visibility.Visible;
        }

        private void CloseHelpBtn_Click(object sender, RoutedEventArgs e)
        {
            HelpPanel.Visibility = Visibility.Collapsed;
        }
    }
}

