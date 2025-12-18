using System.Windows.Controls;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew.MVVM.View
{
    public partial class ComputerView : UserControl
    {
        public ComputerView()
        {
            InitializeComponent();
            if (DataContext == null)
                DataContext = new ComputerViewModel();
        }
    }
} 