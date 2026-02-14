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
        private SoundViewModel? _soundVM;
        private SoundboardViewModel? _soundboardVM;
        private UtilitiesViewModel? _utilitiesVM;
        private AutoClickerViewModel? _autoClickerVM;
        private ClipboardViewModel? _clipboardVM;
        private StandingViewModel? _standingVM;
        private QuickNotesViewModel? _notesVM;
        private HotkeySettingsViewModel? _hotkeySettingsVM;

        [ObservableProperty]
        private object? _currentView;

        [ObservableProperty]
        private bool isMonitoringSelected = true;

        [ObservableProperty]
        private bool isSoundSelected = false;

        [ObservableProperty]
        private bool isSoundboardSelected = false;

        [ObservableProperty]
        private bool isUtilitiesSelected = false;

        [ObservableProperty]
        private bool isAutoClickerSelected = false;

        [ObservableProperty]
        private bool isClipboardSelected = false;

        [ObservableProperty]
        private bool isStandingSelected = false;

        [ObservableProperty]
        private bool isNotesSelected = false;

        [ObservableProperty]
        private bool isHotkeySettingsSelected = false;

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

        private void DeselectAll()
        {
            IsMonitoringSelected = false;
            IsSoundSelected = false;
            IsSoundboardSelected = false;
            IsUtilitiesSelected = false;
            IsAutoClickerSelected = false;
            IsClipboardSelected = false;
            IsStandingSelected = false;
            IsNotesSelected = false;
            IsHotkeySettingsSelected = false;
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
                DeselectAll();
                IsMonitoringSelected = true;
                Logger?.LogDebug("Switched to Monitoring view");
            }
        }

        [RelayCommand]
        private void SoundView()
        {
            ThrowIfDisposed();
            _soundVM ??= App.Services.GetService(typeof(SoundViewModel)) as SoundViewModel;
            if (_soundVM != null)
            {
                CurrentView = _soundVM;
                DeselectAll();
                IsSoundSelected = true;
                Logger?.LogDebug("Switched to Sound view");
            }
        }

        [RelayCommand]
        private void SoundboardView()
        {
            ThrowIfDisposed();
            _soundboardVM ??= App.Services.GetService(typeof(SoundboardViewModel)) as SoundboardViewModel;
            if (_soundboardVM != null)
            {
                CurrentView = _soundboardVM;
                DeselectAll();
                IsSoundboardSelected = true;
                Logger?.LogDebug("Switched to Soundboard view");
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
                DeselectAll();
                IsUtilitiesSelected = true;
                Logger?.LogDebug("Switched to Utilities view");
            }
        }

        [RelayCommand]
        private void AutoClickerView()
        {
            ThrowIfDisposed();
            _autoClickerVM ??= App.Services.GetService(typeof(AutoClickerViewModel)) as AutoClickerViewModel;
            if (_autoClickerVM != null)
            {
                CurrentView = _autoClickerVM;
                DeselectAll();
                IsAutoClickerSelected = true;
                Logger?.LogDebug("Switched to AutoClicker view");
            }
        }

        [RelayCommand]
        private void ClipboardView()
        {
            ThrowIfDisposed();
            _clipboardVM ??= App.Services.GetService(typeof(ClipboardViewModel)) as ClipboardViewModel;
            if (_clipboardVM != null)
            {
                CurrentView = _clipboardVM;
                DeselectAll();
                IsClipboardSelected = true;
                Logger?.LogDebug("Switched to Clipboard view");
            }
        }

        [RelayCommand]
        private void StandingView()
        {
            ThrowIfDisposed();
            _standingVM ??= App.Services.GetService(typeof(StandingViewModel)) as StandingViewModel;
            if (_standingVM != null)
            {
                CurrentView = _standingVM;
                DeselectAll();
                IsStandingSelected = true;
                Logger?.LogDebug("Switched to Standing view");
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
                DeselectAll();
                IsNotesSelected = true;
                Logger?.LogDebug("Switched to Notes view");
            }
        }

        [RelayCommand]
        private void HotkeySettingsView()
        {
            ThrowIfDisposed();
            _hotkeySettingsVM ??= App.Services.GetService(typeof(HotkeySettingsViewModel)) as HotkeySettingsViewModel;
            if (_hotkeySettingsVM != null)
            {
                CurrentView = _hotkeySettingsVM;
                DeselectAll();
                IsHotkeySettingsSelected = true;
                Logger?.LogDebug("Switched to Hotkey Settings view");
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
                _soundVM?.Dispose();
                _soundboardVM?.Dispose();
                _utilitiesVM?.Dispose();
                _autoClickerVM?.Dispose();
                _clipboardVM?.Dispose();
                _standingVM?.Dispose();
                _notesVM?.Dispose();
                _hotkeySettingsVM?.Dispose();
            }
            base.Dispose(disposing);
        }

        public string AppVersion => $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
    }
}
