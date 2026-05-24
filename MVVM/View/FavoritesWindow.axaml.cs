using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CenterHubNew.MVVM.ViewModel;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.View
{
    public partial class FavoritesWindow : Window
    {
        private static FavoritesWindow? _instance;

        public static void ShowSingleton()
        {
            if (_instance == null)
            {
                try
                {
                    _instance = new FavoritesWindow();
                    _instance.Closed += (s, e) => _instance = null;
                    _instance.Show();
                    _instance.Activate();
                }
                catch
                {
                    _instance = null;
                }
            }
            else
            {
                _instance.Activate();
            }
        }

        public FavoritesWindow(FavoritesViewModel? viewModel = null, ILogger<FavoritesWindow>? logger = null)
        {
            InitializeComponent();

            try
            {
                var vm = viewModel ?? App.Services.GetService(typeof(FavoritesViewModel)) as FavoritesViewModel;
                DataContext = vm;
                if (vm != null)
                    Closed += (_, _) => vm.Dispose();
            }
            catch
            {
                // Window will have no DataContext if DI fails
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
    }
}
