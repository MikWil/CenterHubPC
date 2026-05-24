using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CenterHubNew.MVVM.View
{
    public partial class SoundboardView : UserControl
    {
        public SoundboardView()
        {
            InitializeComponent();
        }

        private void HelpBtn_Click(object? sender, RoutedEventArgs e)
        {
            HelpPanel.IsVisible = true;
        }

        private void CloseHelpBtn_Click(object? sender, RoutedEventArgs e)
        {
            HelpPanel.IsVisible = false;
        }
    }
}
