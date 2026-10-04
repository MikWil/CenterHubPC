using System;
using Avalonia.Threading;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>Practice time today / this week and the streak, from the practice log.</summary>
    public sealed partial class PracticeCardViewModel : DashboardCardViewModel
    {
        private readonly PracticeLogService _log;

        [ObservableProperty] private string _todayText = "0 min";
        [ObservableProperty] private string _weekText = "0 min";
        [ObservableProperty] private string _streakText = "0 days";

        public PracticeCardViewModel(DashboardCardInfo info, PracticeLogService log, ShellService? shell, ILogger? logger = null)
            : base(info, shell, logger)
        {
            _log = log;
            _log.Changed += OnLogChanged;
            Refresh();
        }

        public static string StreakLabel(int days) => days == 1 ? "1 day" : $"{days} days";

        protected override void OnActiveChanged(bool active)
        {
            if (active) Refresh(); // a new day may have started while the page was away
        }

        private void OnLogChanged()
        {
            // Raised on the recording thread.
            try { Dispatcher.UIThread.Post(Refresh); }
            catch (InvalidOperationException) { /* dispatcher shut down */ }
        }

        private void Refresh()
        {
            if (IsDisposed) return;
            TodayText = PracticeText.Duration(_log.TodayTotal);
            WeekText = PracticeText.Duration(_log.ThisWeekTotal);
            StreakText = StreakLabel(_log.Streak);
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
                _log.Changed -= OnLogChanged;
            base.Dispose(disposing);
        }
    }
}
