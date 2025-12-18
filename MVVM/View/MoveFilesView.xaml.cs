using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.ViewModel;
using System.Windows;
using System.Windows.Controls;

namespace CenterHubNew.MVVM.View
{
    /// <summary>
    /// Interaction logic for ClickerView.xaml
    /// </summary>
    public partial class MoveFilesView : UserControl
    {        
        public MoveFilesView()
        {
            InitializeComponent();

            this.DataContext = new MoveFilesViewModel();
        }    
    }
}
