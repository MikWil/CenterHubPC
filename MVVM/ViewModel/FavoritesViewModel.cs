using System;
using Avalonia.Threading;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.ViewModel.Dashboard;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>
    /// The compact Favorites window: the cards of <c>UiSettings.FavoritesCards</c> (default: system, volume,
    /// metronome), edited with the same <see cref="CardListEditor"/> as the Home page. Transient — one per
    /// window; disposing it disposes its cards.
    /// </summary>
    public class FavoritesViewModel : BaseViewModel
    {
        public FavoritesViewModel(
            UiSettingsService settings,
            IServiceProvider services,
            ILogger<FavoritesViewModel>? logger = null) : base(logger)
        {
            Editor = new CardListEditor(
                settings,
                DashboardCardRegistry.Default,
                CardListSlot.Favorites,
                DashboardCardRegistry.DefaultFavorites,
                info => info.Factory(info, services),
                post: action => Dispatcher.UIThread.Post(action),
                logger: logger);
            Editor.SetActive(true); // the window is on screen as long as this view-model lives
        }

        public CardListEditor Editor { get; }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
                Editor.Dispose();
            base.Dispose(disposing);
        }
    }
}
