using Microsoft.Extensions.Logging;
using System.Windows;
using CenterHubNew.MVVM.ViewModel;

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
                catch (System.Exception)
                {
                    // Fallback to basic window creation if DI fails
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
            catch (System.Exception ex)
            {
                _logger?.LogError(ex, "Failed to get SoundControlsViewModel from DI, creating new instance");
                DataContext = new SoundControlsViewModel();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _logger?.LogDebug("SoundControlsWindow closing");
            this.Close();
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
    }
} 