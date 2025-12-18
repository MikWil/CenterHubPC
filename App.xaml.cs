using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Windows;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static IHost? _host;
        public static IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Services not initialized");

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                App._host = CreateHostBuilder(e.Args).Build();
                await App._host.StartAsync();

                var mainWindow = App._host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start application: {ex.Message}", "Startup Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (App._host != null)
            {
                await App._host.StopAsync();
                App._host.Dispose();
            }

            base.OnExit(e);
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Register services
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    services.AddSingleton<ICacheService, CacheService>();
                    services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
                    services.AddSingleton<ClipboardService>();
                    services.AddSingleton<QuickNotesService>();
                    services.AddSingleton<AutoClickerService>();

                    // Register ViewModels
                    services.AddTransient<MainViewModel>();
                    services.AddTransient<HomeViewModel>();
                    services.AddTransient<SoundViewModel>();
                    services.AddTransient<SoundControlsViewModel>();
                    services.AddTransient<StandingViewModel>();
                    services.AddTransient<MoveFilesViewModel>();
                    services.AddTransient<ComputerViewModel>();
                    services.AddTransient<NameInputViewModel>();
                    services.AddTransient<MonitoringViewModel>();
                    services.AddTransient<UtilitiesViewModel>();
                    services.AddTransient<ClipboardViewModel>();
                    services.AddTransient<QuickNotesViewModel>();
                    services.AddTransient<AutoClickerViewModel>();

                    // Register Views
                    services.AddTransient<MainWindow>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Information);
                });
    }
}
