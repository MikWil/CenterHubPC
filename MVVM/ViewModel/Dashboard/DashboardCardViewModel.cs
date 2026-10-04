using CenterHubNew.MVVM.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>
    /// A dashboard / Favorites card. Owns whatever timers and subscriptions its content needs:
    /// event subscriptions are made in the constructor and removed in <c>Dispose</c>; timers run only
    /// while the card is <see cref="IsActive"/> (the hosting page is visible), see <see cref="OnActiveChanged"/>.
    /// The card's chrome (header, move/remove buttons) binds to the members of this base class.
    /// </summary>
    public abstract partial class DashboardCardViewModel : BaseViewModel
    {
        private readonly ShellService? _shell;
        private CardListEditor? _owner;

        protected DashboardCardViewModel(DashboardCardInfo info, ShellService? shell, ILogger? logger = null) : base(logger)
        {
            Info = info;
            _shell = shell;
        }

        public DashboardCardInfo Info { get; }
        public string Key => Info.Key;
        public string Title => Info.Title;
        public string Glyph => Info.Glyph;
        public string Description => Info.Description;

        /// <summary>The page a click on the header opens.</summary>
        public string OpenPageKey => Info.OpenPageKey;

        /// <summary>The host is in edit mode: the card shows its move / remove buttons.</summary>
        [ObservableProperty] private bool _isEditing;

        [ObservableProperty] private bool _canMoveUp;
        [ObservableProperty] private bool _canMoveDown;
        [ObservableProperty] private bool _canRemove;

        /// <summary>True while the hosting page / window is on screen. Timers run only then.</summary>
        public bool IsActive { get; private set; }

        public void SetActive(bool active)
        {
            if (IsDisposed || IsActive == active) return;
            IsActive = active;
            OnActiveChanged(active);
        }

        /// <summary>Start (true) or stop (false) timers; refresh the data when becoming active.</summary>
        protected virtual void OnActiveChanged(bool active) { }

        internal void Attach(CardListEditor owner) => _owner = owner;

        internal void SetPosition(int index, int count, bool editing)
        {
            CanMoveUp = index > 0;
            CanMoveDown = index < count - 1;
            CanRemove = count > 1;
            IsEditing = editing;
        }

        [RelayCommand]
        private void Open()
        {
            if (IsDisposed || string.IsNullOrEmpty(OpenPageKey)) return;
            _shell?.NavigateTo(OpenPageKey);
        }

        [RelayCommand]
        private void MoveUp()
        {
            if (IsDisposed) return;
            _owner?.MoveUp(Key);
        }

        [RelayCommand]
        private void MoveDown()
        {
            if (IsDisposed) return;
            _owner?.MoveDown(Key);
        }

        [RelayCommand]
        private void Remove()
        {
            if (IsDisposed) return;
            _owner?.Remove(Key);
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                IsActive = false;
                _owner = null;
            }
            base.Dispose(disposing);
        }
    }
}
