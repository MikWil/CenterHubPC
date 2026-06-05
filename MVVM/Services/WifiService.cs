using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.Services
{
    public enum NetworkConnectionType
    {
        Disconnected,
        Wifi,
        Ethernet,
    }

    public sealed class NetworkConnectionInfo
    {
        public NetworkConnectionType Type    { get; init; }
        public string AdapterName            { get; init; } = "";
        public string Ssid                   { get; init; } = "";          // Wi-Fi only
        public string Bssid                  { get; init; } = "";          // Wi-Fi only — access-point MAC
        public int    SignalQuality          { get; init; }                // 0-100 (Wi-Fi) or 100 (Ethernet)
        public int    EstimatedSignalDbm     { get; init; }                // approx; -100 + (q/2)
        public int    RxRateMbps             { get; init; }                // current receive rate
        public int    TxRateMbps             { get; init; }                // current transmit rate
        public string IPv4Address            { get; init; } = "";
        public string SubnetMask             { get; init; } = "";
        public string Gateway                { get; init; } = "";
        public string IPv6Address            { get; init; } = "";
        public string DnsServers             { get; init; } = "";          // comma-joined
        public string DhcpServer             { get; init; } = "";
        public DateTime? DhcpLeaseEnd        { get; init; }
        public string Hostname               { get; init; } = "";          // computer name
        public string MacAddress             { get; init; } = "";
        public string PhyType                { get; init; } = "";          // e.g. 802.11ac
    }

    public sealed class NetworkActionResult
    {
        public bool   Success { get; init; }
        public string Message { get; init; } = "";
    }

    /// <summary>
    /// Surfaces the connected network's signal/SSID/link info via the Windows
    /// WLAN API (no shelling out, locale-independent) and wraps the common
    /// "fix my connection" commands — release, renew, flush — into one
    /// elevated batch so the UAC prompt only fires once per action.
    /// </summary>
    public sealed class WifiService
    {
        private readonly ILogger<WifiService>? _logger;
        private readonly HttpClient _http;

        public WifiService(ILogger<WifiService>? logger = null)
        {
            _logger = logger;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CenterHub/1.0");
        }

        /// <summary>
        /// Resolve the machine's public-facing IPv4 via ipify (plain-text endpoint).
        /// Returns an empty string on any failure (offline, DNS down, timeout).
        /// Separate from GetSnapshot() because it requires a network round-trip.
        /// </summary>
        public async Task<string> GetPublicIpv4Async(CancellationToken ct = default)
        {
            try
            {
                var ip = await _http.GetStringAsync("https://api.ipify.org", ct).ConfigureAwait(false);
                return ip.Trim();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Public IP lookup failed");
                return "";
            }
        }

        // =====================================================
        //  Snapshot
        // =====================================================

        public NetworkConnectionInfo GetSnapshot()
        {
            // Try Wi-Fi first via WLAN API
            try
            {
                var wifi = QueryConnectedWifi();
                if (wifi != null) return wifi;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "WLAN API query failed; falling back to NetworkInterface enumeration");
            }

            // Fall back to first up-and-running NetworkInterface (likely ethernet)
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n =>
                        n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel);
                if (nic != null)
                {
                    var d = GatherInterfaceDetails(nic);
                    return new NetworkConnectionInfo
                    {
                        Type             = NetworkConnectionType.Ethernet,
                        AdapterName      = nic.Name,
                        SignalQuality    = 100,
                        RxRateMbps       = (int)(nic.Speed / 1_000_000),
                        TxRateMbps       = (int)(nic.Speed / 1_000_000),
                        IPv4Address      = d.Ipv4,
                        SubnetMask       = d.Mask,
                        Gateway          = d.Gateway,
                        IPv6Address      = d.Ipv6,
                        DnsServers       = d.Dns,
                        DhcpServer       = d.DhcpServer,
                        DhcpLeaseEnd     = d.LeaseEnd,
                        Hostname         = Environment.MachineName,
                        MacAddress       = d.Mac,
                        PhyType          = nic.NetworkInterfaceType.ToString(),
                    };
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "NetworkInterface fallback failed");
            }

            return new NetworkConnectionInfo
            {
                Type     = NetworkConnectionType.Disconnected,
                Hostname = Environment.MachineName,
            };
        }

        // =====================================================
        //  Fix commands
        // =====================================================

        public Task<NetworkActionResult> FlushDnsAsync()
            => RunIpconfigAsync("/flushdns", elevate: true);

        public Task<NetworkActionResult> RenewDhcpAsync()
            => RunCommandAsync(
                "ipconfig /release && ipconfig /renew",
                elevate: true,
                description: "Renew DHCP lease");

        public Task<NetworkActionResult> FullResetAsync()
            => RunCommandAsync(
                "ipconfig /flushdns && ipconfig /release && ipconfig /renew && ipconfig /registerdns",
                elevate: true,
                description: "Full network reset");

        // =====================================================
        //  WLAN P/Invoke
        // =====================================================

        private NetworkConnectionInfo? QueryConnectedWifi()
        {
            IntPtr clientHandle = IntPtr.Zero;
            IntPtr ifListPtr = IntPtr.Zero;

            try
            {
                if (WlanOpenHandle(2, IntPtr.Zero, out uint _, out clientHandle) != 0)
                    return null;

                if (WlanEnumInterfaces(clientHandle, IntPtr.Zero, out ifListPtr) != 0)
                    return null;

                var ifList = Marshal.PtrToStructure<WLAN_INTERFACE_INFO_LIST>(ifListPtr);
                if (ifList.dwNumberOfItems == 0) return null;

                // Walk the interfaces; we only care about the first that's connected.
                var ifInfoOffset = Marshal.OffsetOf<WLAN_INTERFACE_INFO_LIST>(nameof(WLAN_INTERFACE_INFO_LIST.InterfaceInfo)).ToInt64();
                var ifInfoSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();

                for (int i = 0; i < ifList.dwNumberOfItems; i++)
                {
                    var ifPtr = new IntPtr(ifListPtr.ToInt64() + ifInfoOffset + (i * ifInfoSize));
                    var ifInfo = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(ifPtr);

                    if (ifInfo.isState != WLAN_INTERFACE_STATE.Connected) continue;

                    if (WlanQueryInterface(clientHandle, ref ifInfo.InterfaceGuid,
                            WLAN_INTF_OPCODE.CurrentConnection,
                            IntPtr.Zero, out _, out IntPtr connPtr, IntPtr.Zero) != 0)
                        continue;

                    try
                    {
                        var conn = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(connPtr);
                        var ssid = DecodeSsid(conn.wlanAssociationAttributes.dot11Ssid);
                        var bssid = FormatMac(conn.wlanAssociationAttributes.dot11Bssid);
                        var quality = (int)conn.wlanAssociationAttributes.wlanSignalQuality;

                        // Match to NetworkInterface to get the full IP/DNS/DHCP detail set
                        var nicName = ifInfo.strInterfaceDescription;
                        var nic = NetworkInterface.GetAllNetworkInterfaces()
                            .FirstOrDefault(n => n.Description == nicName);
                        var details = nic is null
                            ? default
                            : GatherInterfaceDetails(nic);

                        return new NetworkConnectionInfo
                        {
                            Type             = NetworkConnectionType.Wifi,
                            AdapterName      = nicName,
                            Ssid             = ssid,
                            Bssid            = bssid,
                            SignalQuality    = quality,
                            EstimatedSignalDbm = QualityToDbm(quality),
                            RxRateMbps       = (int)(conn.wlanAssociationAttributes.ulRxRate / 1000),
                            TxRateMbps       = (int)(conn.wlanAssociationAttributes.ulTxRate / 1000),
                            IPv4Address      = details.Ipv4,
                            SubnetMask       = details.Mask,
                            Gateway          = details.Gateway,
                            IPv6Address      = details.Ipv6,
                            DnsServers       = details.Dns,
                            DhcpServer       = details.DhcpServer,
                            DhcpLeaseEnd     = details.LeaseEnd,
                            Hostname         = Environment.MachineName,
                            MacAddress       = details.Mac,
                            PhyType          = PhyTypeName(conn.wlanAssociationAttributes.dot11PhyType),
                        };
                    }
                    finally { WlanFreeMemory(connPtr); }
                }

                return null;
            }
            finally
            {
                if (ifListPtr != IntPtr.Zero) WlanFreeMemory(ifListPtr);
                if (clientHandle != IntPtr.Zero) WlanCloseHandle(clientHandle, IntPtr.Zero);
            }
        }

        private record struct InterfaceDetails(
            string Ipv4, string Mask, string Gateway, string Ipv6,
            string Dns, string DhcpServer, DateTime? LeaseEnd, string Mac);

        private InterfaceDetails GatherInterfaceDetails(NetworkInterface nic)
        {
            try
            {
                var props = nic.GetIPProperties();

                // IPv4 unicast — typically first non-link-local, IPv4 family
                var v4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                var ipv4   = v4?.Address.ToString() ?? "";
                var mask   = v4?.IPv4Mask?.ToString() ?? "";

                // Lease time — AddressValidLifetime is seconds remaining on valid leases.
                // Static IPs/non-DHCP report 0 or uint.MaxValue ("never expires").
                DateTime? lease = null;
                if (v4 is not null && v4.AddressValidLifetime > 0 && v4.AddressValidLifetime < 60 * 60 * 24 * 365)
                {
                    lease = DateTime.Now.AddSeconds(v4.AddressValidLifetime);
                }

                // IPv6 — prefer the global unicast (not link-local fe80::)
                var v6 = props.UnicastAddresses
                    .FirstOrDefault(a =>
                        a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
                        !a.Address.IsIPv6LinkLocal &&
                        !a.Address.IsIPv6SiteLocal);
                var ipv6 = v6?.Address.ToString() ?? "";

                var gw = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?.Address.ToString() ?? "";

                var dns = string.Join(", ", props.DnsAddresses
                    .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.ToString()));

                var dhcp = props.DhcpServerAddresses.FirstOrDefault()?.ToString() ?? "";

                var mac = FormatMac(nic.GetPhysicalAddress().GetAddressBytes());

                return new InterfaceDetails(ipv4, mask, gw, ipv6, dns, dhcp, lease, mac);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "GatherInterfaceDetails failed");
                return default;
            }
        }

        private static string DecodeSsid(DOT11_SSID ssid)
        {
            if (ssid.uSSIDLength == 0 || ssid.ucSSID == null) return "";
            var len = Math.Min(ssid.uSSIDLength, (uint)ssid.ucSSID.Length);
            return Encoding.UTF8.GetString(ssid.ucSSID, 0, (int)len);
        }

        private static string FormatMac(byte[]? mac)
        {
            if (mac == null || mac.Length == 0) return "";
            return string.Join(":", mac.Select(b => b.ToString("X2")));
        }

        private static int QualityToDbm(int quality)
        {
            // Windows reports quality 0-100. The conversion to dBm is
            // approximate (Win quality is a linear scale, dBm is logarithmic).
            // The Microsoft-documented mapping: dBm = -100 + (quality / 2)
            // gives -100 at 0% and -50 at 100%, which matches the typical
            // useful range.
            return -100 + (quality / 2);
        }

        private static string PhyTypeName(DOT11_PHY_TYPE phy) => phy switch
        {
            DOT11_PHY_TYPE.FHSS    => "802.11 FHSS",
            DOT11_PHY_TYPE.DSSS    => "802.11 DSSS",
            DOT11_PHY_TYPE.IRBaseband => "802.11 IR",
            DOT11_PHY_TYPE.OFDM    => "802.11a",
            DOT11_PHY_TYPE.HRDSSS  => "802.11b",
            DOT11_PHY_TYPE.ERP     => "802.11g",
            DOT11_PHY_TYPE.HT      => "802.11n",
            DOT11_PHY_TYPE.VHT     => "802.11ac",
            DOT11_PHY_TYPE.DMG     => "802.11ad",
            DOT11_PHY_TYPE.HE      => "802.11ax (Wi-Fi 6)",
            _                      => phy.ToString(),
        };

        // =====================================================
        //  Shell-out helpers (elevated)
        // =====================================================

        private Task<NetworkActionResult> RunIpconfigAsync(string args, bool elevate)
            => RunCommandAsync($"ipconfig {args}", elevate, $"ipconfig {args}");

        private Task<NetworkActionResult> RunCommandAsync(string command, bool elevate, string description)
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName    = "cmd.exe",
                        Arguments   = $"/c {command}",
                        UseShellExecute = elevate, // required for verb=runas
                        Verb        = elevate ? "runas" : "",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    };

                    using var p = Process.Start(psi);
                    if (p is null)
                        return new NetworkActionResult { Success = false, Message = "Failed to start cmd.exe" };

                    p.WaitForExit(30000);
                    var ok = p.HasExited && p.ExitCode == 0;
                    _logger?.LogInformation("Ran {Desc}: exit={Exit}", description, p.ExitCode);
                    return new NetworkActionResult
                    {
                        Success = ok,
                        Message = ok
                            ? $"{description} completed."
                            : $"{description} returned exit {p.ExitCode}.",
                    };
                }
                catch (System.ComponentModel.Win32Exception wex)
                    when (unchecked((uint)wex.NativeErrorCode) == 0x800704C7 /*ERROR_CANCELLED*/)
                {
                    return new NetworkActionResult
                    {
                        Success = false,
                        Message = "Cancelled — Windows admin prompt was dismissed.",
                    };
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Network command failed: {Desc}", description);
                    return new NetworkActionResult { Success = false, Message = $"Failed: {ex.Message}" };
                }
            });
        }

        // =====================================================
        //  WLAN P/Invoke definitions
        // =====================================================

        [DllImport("wlanapi.dll")]
        private static extern int WlanOpenHandle(uint dwClientVersion, IntPtr reserved,
            out uint negotiatedVersion, out IntPtr clientHandle);

        [DllImport("wlanapi.dll")]
        private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        private static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved,
            out IntPtr interfaceList);

        [DllImport("wlanapi.dll")]
        private static extern int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid,
            WLAN_INTF_OPCODE opCode, IntPtr reserved, out int dataSize, out IntPtr ppData,
            IntPtr wlanOpCodeValueType);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr p);

        private enum WLAN_INTERFACE_STATE
        {
            NotReady = 0, Connected, AdHocNetworkFormed, Disconnecting,
            Disconnected, Associating, Discovering, Authenticating,
        }

        private enum WLAN_CONNECTION_MODE
        {
            Profile, TemporaryProfile, DiscoverySecure, DiscoveryUnsecure,
            Auto, Invalid,
        }

        private enum WLAN_INTF_OPCODE
        {
            AutoconfEnabled = 0x000000001,
            CurrentConnection = 0x00000007,
        }

        private enum DOT11_BSS_TYPE { Infrastructure = 1, Independent = 2, Any = 3 }

        private enum DOT11_PHY_TYPE : uint
        {
            Unknown = 0, FHSS = 1, DSSS = 2, IRBaseband = 3,
            OFDM = 4, HRDSSS = 5, ERP = 6, HT = 7, VHT = 8,
            DMG = 9, HE = 10,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_INTERFACE_INFO_LIST
        {
            public uint dwNumberOfItems;
            public uint dwIndex;
            public WLAN_INTERFACE_INFO InterfaceInfo;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_INTERFACE_INFO
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public WLAN_INTERFACE_STATE isState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_ASSOCIATION_ATTRIBUTES
        {
            public DOT11_SSID dot11Ssid;
            public DOT11_BSS_TYPE dot11BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] dot11Bssid;
            public DOT11_PHY_TYPE dot11PhyType;
            public uint uDot11PhyIndex;
            public uint wlanSignalQuality;
            public uint ulRxRate;
            public uint ulTxRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_SECURITY_ATTRIBUTES
        {
            [MarshalAs(UnmanagedType.Bool)] public bool bSecurityEnabled;
            [MarshalAs(UnmanagedType.Bool)] public bool bOneXEnabled;
            public uint dot11AuthAlgorithm;
            public uint dot11CipherAlgorithm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_CONNECTION_ATTRIBUTES
        {
            public WLAN_INTERFACE_STATE isState;
            public WLAN_CONNECTION_MODE wlanConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
            public WLAN_SECURITY_ATTRIBUTES wlanSecurityAttributes;
        }
    }
}
