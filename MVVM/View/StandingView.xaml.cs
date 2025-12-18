using CenterHubNew.MVVM.ViewModel;
using System.Windows.Controls;

namespace CenterHubNew.MVVM.View
{
    /// <summary>
    /// Interaction logic for StandingView.xaml
    /// </summary>
    public partial class StandingView : UserControl
    {

        public StandingView()
        {
            InitializeComponent();

            this.DataContext = new StandingViewModel();
        }
    }
}
