using CenterHubNew.MVVM.Navigation;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel.Dashboard
{
    /// <summary>
    /// The sit/stand timer's state with start / stop. The timer itself is the Standing page's singleton
    /// view-model; the card binds straight to it (no timer or subscription of its own).
    /// </summary>
    public sealed class StandingCardViewModel : DashboardCardViewModel
    {
        public StandingCardViewModel(DashboardCardInfo info, StandingViewModel standing, ShellService? shell, ILogger? logger = null)
            : base(info, shell, logger)
        {
            Standing = standing;
        }

        public StandingViewModel Standing { get; }
    }
}
