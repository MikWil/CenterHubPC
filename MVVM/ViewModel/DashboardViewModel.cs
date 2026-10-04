using System;
using System.Globalization;
using Avalonia.Threading;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.ViewModel.Dashboard;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>
    /// The Home page: a page of cards the user picks and arranges (see <see cref="CardListEditor"/>;
    /// the list is <c>UiSettings.DashboardCards</c>). DI singleton. The cards are created the first time
    /// the page is shown and keep their state while it is away; their timers run only while it is on screen.
    /// </summary>
    public partial class DashboardViewModel : BaseViewModel
    {
        private readonly UiSettingsService _settings;
        private readonly IServiceProvider _services;
        private CardListEditor? _editor;
        private DispatcherTimer? _clock;
        private bool _active;

        [ObservableProperty] private string _subtitle;

        public DashboardViewModel(
            UiSettingsService settings,
            IServiceProvider services,
            ILogger<DashboardViewModel>? logger = null) : base(logger)
        {
            _settings = settings;
            _services = services;
            _subtitle = Describe(DateTime.Now);
        }

        /// <summary>The cards and their editing. Created on first use.</summary>
        public CardListEditor Editor => _editor ??= CreateEditor();

        private CardListEditor CreateEditor()
        {
            var editor = new CardListEditor(
                _settings,
                DashboardCardRegistry.Default,
                CardListSlot.Dashboard,
                DashboardCardRegistry.DefaultDashboard,
                info => info.Factory(info, _services),
                post: action => Dispatcher.UIThread.Post(action),
                logger: Logger);
            editor.SetActive(_active);
            return editor;
        }

        /// <summary>Called by the view when the page appears (true) or goes away (false).</summary>
        public void SetActive(bool active)
        {
            if (IsDisposed) return;
            _active = active;
            _editor?.SetActive(active);

            if (active)
            {
                Subtitle = Describe(DateTime.Now);
                _ = Editor; // make sure the cards exist (and start)
                _clock ??= CreateClock();
                _clock.Start();
            }
            else
            {
                _clock?.Stop();
            }
        }

        private DispatcherTimer CreateClock()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            timer.Tick += (_, _) =>
            {
                if (!IsDisposed) Subtitle = Describe(DateTime.Now);
            };
            return timer;
        }

        /// <summary>"Good evening".</summary>
        public static string Greeting(DateTime now) => now.Hour switch
        {
            >= 5 and < 12 => "Good morning",
            >= 12 and < 18 => "Good afternoon",
            >= 18 and < 23 => "Good evening",
            _ => "Good night",
        };

        /// <summary>"Good evening · Saturday 4 October".</summary>
        public static string Describe(DateTime now) =>
            $"{Greeting(now)} · {now.ToString("dddd d MMMM", CultureInfo.InvariantCulture)}";

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _clock?.Stop();
                _clock = null;
                _editor?.Dispose();
                _editor = null;
            }
            base.Dispose(disposing);
        }
    }
}
