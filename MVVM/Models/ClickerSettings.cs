using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterHubNew.MVVM.Models
{
    public partial class ClickerSettings : ObservableObject
    {
        [ObservableProperty]
        private int clickCount;

        [ObservableProperty]
        private int clicksPerSecond;

        [ObservableProperty]
        private bool isRunning;

        public ClickerSettings()
        {
            
        }
    }
} 