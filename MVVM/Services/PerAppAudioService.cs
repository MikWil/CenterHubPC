using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>A running application that is producing audio (has a session).</summary>
    public sealed class AudioAppInfo
    {
        public int ProcessId { get; init; }
        public string DisplayName { get; init; } = "";
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Sets a per-application default output device in Windows from inside the app —
    /// the same capability as the Windows "App volume and device preferences" page,
    /// via the undocumented Windows.Media.Internal.AudioPolicyConfig activation
    /// factory (the approach EarTrumpet uses).
    ///
    /// .NET 5+ removed built-in WinRT (HSTRING/IInspectable) marshaling, so this uses
    /// classic interop only: the factory is activated as a raw IUnknown pointer and its
    /// SetPersistedDefaultAudioEndpoint method is invoked through the vtable directly
    /// (same slot on Win10 and Win11 — only the activation IID differs). Strings cross
    /// as hand-made HSTRINGs; everything else as IntPtr/int/uint.
    /// </summary>
    public sealed class PerAppAudioService : IDisposable
    {
        private readonly ILogger<PerAppAudioService>? _logger;

        private IntPtr _factory;                 // IUnknown* to the activated factory
        private SetPersistedDelegate? _set;      // cached vtable thunk
        private bool _activationTried;

        private const string DevInterfaceAudioRender = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
        private const string MmDevApiToken = @"\\?\SWD#MMDEVAPI#";
        private const string ActivatableClass = "Windows.Media.Internal.AudioPolicyConfig";

        private const int E_RENDER = 0;
        private const int ROLE_CONSOLE = 0;
        private const int ROLE_MULTIMEDIA = 1;

        // Vtable layout: IUnknown(3) + IInspectable(3) + 19 unused = 25 slots before
        // SetPersistedDefaultAudioEndpoint.
        private const int SetPersistedSlot = 25;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetPersistedDelegate(IntPtr thisPtr, uint processId, int flow, int role, IntPtr deviceIdHString);

        public PerAppAudioService(ILogger<PerAppAudioService>? logger = null) => _logger = logger;

        // ─────────────────── Enumerate audio apps ───────────────────

        public IReadOnlyList<AudioAppInfo> GetAudioApps()
        {
            var byPid = new Dictionary<int, AudioAppInfo>();
            try
            {
                using var en = new MMDeviceEnumerator();
                foreach (var dev in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    try
                    {
                        var sessions = dev.AudioSessionManager.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            using var s = sessions[i];
                            int pid = (int)s.GetProcessID;
                            if (pid <= 0 || byPid.ContainsKey(pid)) continue;

                            var name = FriendlyProcessName(pid);
                            if (name is null) continue;
                            byPid[pid] = new AudioAppInfo { ProcessId = pid, DisplayName = name };
                        }
                    }
                    catch (Exception ex) { _logger?.LogDebug(ex, "Session enumeration failed for a device"); }
                    finally { dev.Dispose(); }
                }
            }
            catch (Exception ex) { _logger?.LogError(ex, "Failed to enumerate audio apps"); }

            return byPid.Values.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Re-resolve a live process id for the selected app at the moment of assignment.
        /// The dropdown's captured PID can be stale (the app restarted, or Windows recycled
        /// the PID onto a different process) — assigning that would route the WRONG app.
        /// Prefer the captured PID only if it still belongs to a same-named audio session;
        /// otherwise fall back to any current audio session with that process name.
        /// </summary>
        public int? ResolveLivePid(int preferredPid, string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return null;
            var live = GetAudioApps();
            var exact = live.FirstOrDefault(a => a.ProcessId == preferredPid &&
                string.Equals(a.DisplayName, processName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact.ProcessId;
            var byName = live.FirstOrDefault(a =>
                string.Equals(a.DisplayName, processName, StringComparison.OrdinalIgnoreCase));
            return byName?.ProcessId;
        }

        private static string? FriendlyProcessName(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                var name = p.ProcessName;
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch { return null; }
        }

        // ─────────────────── Set / clear per-app output ───────────────────

        public bool SetAppRenderDevice(int processId, string mmDeviceId)
        {
            if (string.IsNullOrWhiteSpace(mmDeviceId)) return false;
            return SetPersisted(processId, mmDeviceId);
        }

        public bool ClearApp(int processId) => SetPersisted(processId, null);

        private bool SetPersisted(int processId, string? mmDeviceId)
        {
            var set = EnsureFactory();
            if (set is null) return false;

            IntPtr hDevice = IntPtr.Zero;
            try
            {
                if (!string.IsNullOrWhiteSpace(mmDeviceId))
                {
                    var packed = $"{MmDevApiToken}{mmDeviceId}{DevInterfaceAudioRender}";
                    if (NativeMethods.WindowsCreateString(packed, packed.Length, out hDevice) != 0)
                    {
                        _logger?.LogWarning("WindowsCreateString failed for device id");
                        return false;
                    }
                }

                int r1 = set(_factory, (uint)processId, E_RENDER, ROLE_MULTIMEDIA, hDevice);
                int r2 = set(_factory, (uint)processId, E_RENDER, ROLE_CONSOLE, hDevice);
                var ok = r1 >= 0 && r2 >= 0;
                if (!ok) _logger?.LogWarning("SetPersistedDefaultAudioEndpoint HRESULTs 0x{R1:X8}/0x{R2:X8} pid {Pid}", r1, r2, processId);
                return ok;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Per-app output set failed for pid {Pid}", processId);
                return false;
            }
            finally
            {
                if (hDevice != IntPtr.Zero) NativeMethods.WindowsDeleteString(hDevice);
            }
        }

        private SetPersistedDelegate? EnsureFactory()
        {
            if (_set != null) return _set;
            if (_activationTried) return null;
            _activationTried = true;

            IntPtr hClass = IntPtr.Zero;
            try
            {
                bool win11 = Environment.OSVersion.Version.Build >= 22000;
                var iid = win11 ? IidWin11 : IidWin10;

                if (NativeMethods.WindowsCreateString(ActivatableClass, ActivatableClass.Length, out hClass) != 0)
                    return null;

                int hr = NativeMethods.RoGetActivationFactory(hClass, ref iid, out IntPtr pFactory);
                if (hr < 0 || pFactory == IntPtr.Zero)
                {
                    _logger?.LogWarning("RoGetActivationFactory failed 0x{Hr:X8}", hr);
                    return null;
                }

                _factory = pFactory; // keep the reference; released in Dispose
                IntPtr vtable = Marshal.ReadIntPtr(_factory);
                IntPtr method = Marshal.ReadIntPtr(vtable, SetPersistedSlot * IntPtr.Size);
                _set = Marshal.GetDelegateForFunctionPointer<SetPersistedDelegate>(method);
                return _set;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Could not activate AudioPolicyConfig factory");
                return null;
            }
            finally
            {
                if (hClass != IntPtr.Zero) NativeMethods.WindowsDeleteString(hClass);
            }
        }

        public void Dispose()
        {
            if (_factory != IntPtr.Zero)
            {
                try { Marshal.Release(_factory); } catch { }
                _factory = IntPtr.Zero;
            }
        }

        private static Guid IidWin11 = new("ab3d4648-e242-459f-b02f-541c70306324");
        private static Guid IidWin10 = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");
    }

    internal static class NativeMethods
    {
        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        public static extern int WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        public static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        public static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
    }
}
