using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class UtilitiesViewModel : BaseViewModel
    {
        private readonly SoundViewModel _soundViewModel;
        private StandingViewModel? _standingViewModel;
        private MoveFilesViewModel? _moveFilesViewModel;
        private ComputerViewModel? _computerViewModel;
        private ClipboardViewModel? _clipboardViewModel;
        private QuickNotesViewModel? _quickNotesViewModel;
        private AutoClickerViewModel? _autoClickerViewModel;

        public SoundViewModel SoundViewModel => _soundViewModel;

        public StandingViewModel StandingViewModel
        {
            get
            {
                _standingViewModel ??= App.Services.GetService(typeof(StandingViewModel)) as StandingViewModel;
                return _standingViewModel!;
            }
        }

        public MoveFilesViewModel MoveFilesViewModel
        {
            get
            {
                _moveFilesViewModel ??= App.Services.GetService(typeof(MoveFilesViewModel)) as MoveFilesViewModel;
                return _moveFilesViewModel!;
            }
        }

        public ComputerViewModel ComputerViewModel
        {
            get
            {
                _computerViewModel ??= App.Services.GetService(typeof(ComputerViewModel)) as ComputerViewModel;
                return _computerViewModel!;
            }
        }

        public ClipboardViewModel ClipboardViewModel
        {
            get
            {
                _clipboardViewModel ??= App.Services.GetService(typeof(ClipboardViewModel)) as ClipboardViewModel;
                return _clipboardViewModel!;
            }
        }

        public QuickNotesViewModel QuickNotesViewModel
        {
            get
            {
                _quickNotesViewModel ??= App.Services.GetService(typeof(QuickNotesViewModel)) as QuickNotesViewModel;
                return _quickNotesViewModel!;
            }
        }

        public AutoClickerViewModel AutoClickerViewModel
        {
            get
            {
                _autoClickerViewModel ??= App.Services.GetService(typeof(AutoClickerViewModel)) as AutoClickerViewModel;
                return _autoClickerViewModel!;
            }
        }

        public UtilitiesViewModel(
            SoundViewModel soundViewModel,
            ILogger<UtilitiesViewModel>? logger = null) : base(logger)
        {
            _soundViewModel = soundViewModel ?? throw new ArgumentNullException(nameof(soundViewModel));
            Logger?.LogInformation("UtilitiesViewModel initialized");
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                Logger?.LogInformation("UtilitiesViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}

