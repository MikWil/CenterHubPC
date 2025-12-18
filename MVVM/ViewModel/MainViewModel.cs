using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Windows;
using CenterHubNew.MVVM.View;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class MainViewModel : BaseViewModel
    {
        private MonitoringViewModel? _monitoringVM;
        private UtilitiesViewModel? _utilitiesVM;
        private QuickNotesViewModel? _notesVM;

        [ObservableProperty]
        private object? _currentView;

        [ObservableProperty]
        private bool isMonitoringSelected = true;

        [ObservableProperty]
        private bool isUtilitiesSelected = false;

        [ObservableProperty]
        private bool isNotesSelected = false;

        [ObservableProperty]
        private bool isSidebarExpanded = true;

        [ObservableProperty]
        private GridLength sidebarWidth = new GridLength(220);

        public string SidebarToggleIcon => IsSidebarExpanded ? "◀" : "▶";
        public string SidebarToggleText => IsSidebarExpanded ? "Collapse" : "";

        public MainViewModel(ILogger<MainViewModel>? logger = null) : base(logger)
        {
            // Set initial view to Monitoring
            MonitoringView();
            
            Logger?.LogInformation("MainViewModel initialized");
        }

        [RelayCommand]
        private void ToggleSidebar()
        {
            IsSidebarExpanded = !IsSidebarExpanded;
            SidebarWidth = IsSidebarExpanded ? new GridLength(220) : new GridLength(52);
            OnPropertyChanged(nameof(SidebarToggleIcon));
            OnPropertyChanged(nameof(SidebarToggleText));
        }

        [RelayCommand]
        private void MonitoringView()
        {
            ThrowIfDisposed();
            _monitoringVM ??= App.Services.GetService(typeof(MonitoringViewModel)) as MonitoringViewModel;
            if (_monitoringVM != null)
            {
                CurrentView = _monitoringVM;
                IsMonitoringSelected = true;
                IsUtilitiesSelected = false;
                IsNotesSelected = false;
                Logger?.LogDebug("Switched to Monitoring view");
            }
        }

        [RelayCommand]
        private void UtilitiesView()
        {
            ThrowIfDisposed();
            _utilitiesVM ??= App.Services.GetService(typeof(UtilitiesViewModel)) as UtilitiesViewModel;
            if (_utilitiesVM != null)
            {
                CurrentView = _utilitiesVM;
                IsMonitoringSelected = false;
                IsUtilitiesSelected = true;
                IsNotesSelected = false;
                Logger?.LogDebug("Switched to Utilities view");
            }
        }

        [RelayCommand]
        private void NotesView()
        {
            ThrowIfDisposed();
            _notesVM ??= App.Services.GetService(typeof(QuickNotesViewModel)) as QuickNotesViewModel;
            if (_notesVM != null)
            {
                CurrentView = _notesVM;
                IsMonitoringSelected = false;
                IsUtilitiesSelected = false;
                IsNotesSelected = true;
                Logger?.LogDebug("Switched to Notes view");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                if (CurrentView is IDisposable currentDisposable)
                {
                    currentDisposable.Dispose();
                }

                _monitoringVM?.Dispose();
                _utilitiesVM?.Dispose();
                _notesVM?.Dispose();
            }
            base.Dispose(disposing);
        }

        public string AppVersion => $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
    }
}
