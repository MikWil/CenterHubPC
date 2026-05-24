using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CenterHubNew.MVVM.ViewModel;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.View
{
    public partial class SoundControlsWindow : Window
    {
        private static SoundControlsWindow? _instance;
        private readonly ILogger<SoundControlsWindow>? _logger;

        public static void ShowSingleton()
        {
            if (_instance == null)
            {
                try
                {
                    _instance = new SoundControlsWindow();
                    _instance.Closed += (s, e) => _instance = null;
                    _instance.Show();
                    _instance.Activate();
                }
                catch
                {
                    _instance = new SoundControlsWindow(new SoundControlsViewModel());
                    _instance.Closed += (s, e) => _instance = null;
                    _instance.Show();
                    _instance.Activate();
                }
            }
            else
            {
                _instance.Activate();
            }
        }

        public SoundControlsWindow(SoundControlsViewModel? viewModel = null, ILogger<SoundControlsWindow>? logger = null)
        {
            InitializeComponent();
            _logger = logger;

            try
            {
                DataContext = viewModel ?? App.Services.GetService(typeof(SoundControlsViewModel)) as SoundControlsViewModel;
                _logger?.LogInformation("SoundControlsWindow initialized");
            }
            catch
            {
                DataContext = new SoundControlsViewModel();
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
