using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CenterHubNew.MVVM.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class NetworkViewModel : BaseViewModel
    {
        private readonly WifiService _wifi;
        private DispatcherTimer? _poll;

        // ── Live connection state ──
        [ObservableProperty] private bool   _isConnected;
        [ObservableProperty] private bool   _isWifi;
        [ObservableProperty] private string _connectionTypeLabel = "DISCONNECTED";
        [ObservableProperty] private string _ssid = "";
        [ObservableProperty] private string _adapterName = "";
        [ObservableProperty] private int    _signalQuality;       // 0-100
        [ObservableProperty] private int    _signalDbm;           // approx
        [ObservableProperty] private string _signalLabel = "";    // "Excellent" / "Good" / "Fair" / "Weak"
        [ObservableProperty] private string _signalBars  = "";    // ▮▮▮▯▯
        [ObservableProperty] private int    _bars;                // 0-5 — for individual element coloring
        [ObservableProperty] private string _rxRateDisplay = "";  // "150 Mbps"
        [ObservableProperty] private string _txRateDisplay = "";
        [ObservableProperty] private string _bssid       = "";
        [ObservableProperty] private string _ipv4        = "";
        [ObservableProperty] private string _subnetMask  = "";
        [ObservableProperty] private string _gateway     = "";
        [ObservableProperty] private string _ipv6        = "";
        [ObservableProperty] private string _dnsServers  = "";
        [ObservableProperty] private string _dhcpServer  = "";
        [ObservableProperty] private string _dhcpLeaseEnd = "";
        [ObservableProperty] private string _hostname    = "";
        [ObservableProperty] private string _macAddress  = "";
        [ObservableProperty] private string _phyType     = "";

        // Public IP — fetched async; nullable while loading
        [ObservableProperty] private string _publicIpv4 = "Loading…";
        [ObservableProperty] private bool   _isPublicIpLoading;

        // ── Action state ──
        [ObservableProperty] private bool   _isBusy;
        [ObservableProperty] private string _statusMessage = "Ready";

        // ── Confirm dialog ──
        [ObservableProperty] private bool   _isConfirmOpen;
        [ObservableProperty] private string _confirmTitle = "";
        [ObservableProperty] private string _confirmBody  = "";
        private Func<Task>?   _confirmAction;
        private string        _confirmRunningLabel = "";

        public NetworkViewModel(
            WifiService wifi,
            ILogger<NetworkViewModel>? logger = null) : base(logger)
        {
            _wifi = wifi;
            Refresh();
            StartPolling();
            _ = RefreshPublicIpAsync();
        }

        private void StartPolling()
        {
            _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _poll.Tick += (_, _) => { if (!IsDisposed) Refresh(); };
            _poll.Start();
        }

        // ── Polling ──

        [RelayCommand]
        private void Refresh()
        {
            try
            {
                var info = _wifi.GetSnapshot();
                ApplySnapshot(info);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to refresh network snapshot");
            }
        }

        /// <summary>
        /// Fetch the WAN IP via ipify. Async because it requires a real HTTP call.
        /// Public — bound to a Refresh button next to the field.
        /// </summary>
        [RelayCommand]
        public async Task RefreshPublicIpAsync()
        {
            if (IsPublicIpLoading) return;
            IsPublicIpLoading = true;
            PublicIpv4 = "Loading…";
            try
            {
                var ip = await _wifi.GetPublicIpv4Async();
                PublicIpv4 = string.IsNullOrEmpty(ip) ? "—" : ip;
            }
            catch (Exception ex)
            {
                Logger?.LogDebug(ex, "Public IP fetch failed");
                PublicIpv4 = "—";
            }
            finally
            {
                IsPublicIpLoading = false;
            }
        }

        private void ApplySnapshot(NetworkConnectionInfo info)
        {
            IsConnected = info.Type != NetworkConnectionType.Disconnected;
            IsWifi      = info.Type == NetworkConnectionType.Wifi;

            ConnectionTypeLabel = info.Type switch
            {
                NetworkConnectionType.Wifi          => "WI-FI",
                NetworkConnectionType.Ethernet      => "ETHERNET",
                _                                   => "DISCONNECTED",
            };

            Ssid          = info.Ssid;
            AdapterName   = info.AdapterName;
            SignalQuality = info.SignalQuality;
            SignalDbm     = info.EstimatedSignalDbm;
            RxRateDisplay = info.RxRateMbps > 0 ? $"{info.RxRateMbps} Mbps" : "—";
            TxRateDisplay = info.TxRateMbps > 0 ? $"{info.TxRateMbps} Mbps" : "—";
            Bssid         = info.Bssid;
            Ipv4          = info.IPv4Address;
            SubnetMask    = info.SubnetMask;
            Gateway       = info.Gateway;
            Ipv6          = info.IPv6Address;
            DnsServers    = info.DnsServers;
            DhcpServer    = info.DhcpServer;
            DhcpLeaseEnd  = info.DhcpLeaseEnd is { } end
                ? $"{end:MMM d, HH:mm}"
                : "—";
            Hostname      = info.Hostname;
            MacAddress    = info.MacAddress;
            PhyType       = info.PhyType;

            // Bars (0..5) — quality / 20, but only for Wi-Fi
            Bars        = info.Type == NetworkConnectionType.Wifi
                ? Math.Clamp((info.SignalQuality + 10) / 20, 0, 5)
                : 5;
            SignalBars  = MakeBars(Bars);
            SignalLabel = info.Type switch
            {
                NetworkConnectionType.Disconnected => "No connection",
                NetworkConnectionType.Ethernet     => "Wired",
                _ => info.SignalQuality switch
                {
                    >= 80 => "Excellent",
                    >= 60 => "Good",
                    >= 40 => "Fair",
                    >= 20 => "Weak",
                    _     => "Very Weak",
                },
            };
        }

        private static string MakeBars(int filled)
        {
            // Solid ▮ for filled, light ▯ for empty
            var f = Math.Clamp(filled, 0, 5);
            return new string('▮', f) + new string('▯', 5 - f);
        }

        // ── Fix commands ──

        [RelayCommand]
        private void ConfirmRenewDhcp()
        {
            ConfirmTitle = "Renew DHCP lease?";
            ConfirmBody = "This briefly disconnects you from the network while " +
                          "Windows asks the router for a fresh IP address. Usually " +
                          "takes 2–5 seconds. Useful when you get 'no internet' " +
                          "after a sleep / wake or when the router rebooted.";
            _confirmRunningLabel = "Renewing DHCP…";
            _confirmAction = () => RunNetworkAction(_wifi.RenewDhcpAsync, "DHCP lease renewed");
            IsConfirmOpen = true;
        }

        [RelayCommand]
        private void ConfirmFlushDns()
        {
            ConfirmTitle = "Flush DNS cache?";
            ConfirmBody = "Clears the local DNS resolver cache. Doesn't affect your " +
                          "connection — fixes 'site not loading' or 'wrong page' " +
                          "issues after DNS changes propagate.";
            _confirmRunningLabel = "Flushing DNS…";
            _confirmAction = () => RunNetworkAction(_wifi.FlushDnsAsync, "DNS cache flushed");
            IsConfirmOpen = true;
        }

        [RelayCommand]
        private void ConfirmFullReset()
        {
            ConfirmTitle = "Run full network reset?";
            ConfirmBody = "Runs all four steps in sequence:\n\n" +
                          "  1. Flush the DNS cache\n" +
                          "  2. Release the current DHCP lease\n" +
                          "  3. Renew the DHCP lease\n" +
                          "  4. Re-register with the DNS server\n\n" +
                          "You'll be briefly offline (2–10 s). Use this when the " +
                          "computer is 'stuck' — connected to Wi-Fi but no internet, " +
                          "or pages won't load.";
            _confirmRunningLabel = "Running full network reset…";
            _confirmAction = () => RunNetworkAction(_wifi.FullResetAsync, "Network reset complete");
            IsConfirmOpen = true;
        }

        [RelayCommand]
        private void CancelConfirm()
        {
            IsConfirmOpen = false;
            _confirmAction = null;
        }

        [RelayCommand]
        private async Task RunConfirmed()
        {
            if (_confirmAction is null) return;
            IsConfirmOpen = false;
            var action = _confirmAction;
            _confirmAction = null;

            IsBusy = true;
            StatusMessage = _confirmRunningLabel;
            try { await action(); }
            finally
            {
                IsBusy = false;
                // Force a re-check so the user sees the fresh state
                Refresh();
            }
        }

        private async Task RunNetworkAction(Func<Task<NetworkActionResult>> op, string successMessage)
        {
            var result = await op();
            if (result.Success)
            {
                StatusMessage = $"{successMessage} · {DateTime.Now:HH:mm:ss}";
                ToastService.Instance.Success(successMessage);
            }
            else
            {
                StatusMessage = result.Message;
                ToastService.Instance.Error(result.Message);
            }
        }

        // ── Cleanup ──

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                try { _poll?.Stop(); } catch { }
                _poll = null;
            }
            base.Dispose(disposing);
        }
    }
}
