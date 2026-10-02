using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Thin managed wrapper over VoicemeeterRemote64.dll.
    ///
    /// The DLL is NOT bundled — it is loaded at runtime from the installed
    /// Voicemeeter directory (located via the registry, with Program Files
    /// fallbacks). All function pointers are resolved dynamically so a missing
    /// install degrades to <see cref="IsAvailable"/> == false rather than a
    /// hard DllNotFoundException at JIT time.
    ///
    /// This class only exposes the raw native calls; strongly-typed routing lives
    /// in <see cref="VoicemeeterService"/>.
    /// </summary>
    internal sealed class VoicemeeterRemote : IDisposable
    {
        // Voicemeeter "kind" as reported by GetVoicemeeterType / passed to RunVoicemeeter.
        public const int TYPE_VOICEMEETER = 1;
        public const int TYPE_BANANA = 2;
        public const int TYPE_POTATO = 3;

        // Login() return codes.
        public const int LOGIN_OK = 0;
        public const int LOGIN_OK_NOT_LAUNCHED = 1; // client registered, app not running yet

        private readonly ILogger? _logger;
        private IntPtr _handle;

        // ── native delegates (VoicemeeterRemote uses stdcall; on x64 the calling
        //    convention is unified, so Cdecl marshalling is equivalent) ──
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LoginDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LogoutDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RunVoicemeeterDelegate(int mode);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetTypeDelegate(out int type);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetVersionDelegate(out int version);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IsDirtyDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetFloatDelegate([MarshalAs(UnmanagedType.LPStr)] string param, out float value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetFloatDelegate([MarshalAs(UnmanagedType.LPStr)] string param, float value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetStringDelegate([MarshalAs(UnmanagedType.LPStr)] string param, byte[] buffer);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetStringDelegate([MarshalAs(UnmanagedType.LPStr)] string param, [MarshalAs(UnmanagedType.LPStr)] string value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetParametersDelegate([MarshalAs(UnmanagedType.LPStr)] string script);
        // Wide (UTF-16) variants: device names are often non-ASCII ("Högtalare (…)" on a Swedish Windows).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetStringWDelegate([MarshalAs(UnmanagedType.LPStr)] string param, byte[] buffer);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetStringWDelegate([MarshalAs(UnmanagedType.LPStr)] string param, [MarshalAs(UnmanagedType.LPWStr)] string value);

        private LoginDelegate? _login;
        private LogoutDelegate? _logout;
        private RunVoicemeeterDelegate? _run;
        private GetTypeDelegate? _getType;
        private GetVersionDelegate? _getVersion;
        private IsDirtyDelegate? _isDirty;
        private GetFloatDelegate? _getFloat;
        private SetFloatDelegate? _setFloat;
        private GetStringDelegate? _getString;
        private SetStringDelegate? _setString;
        private SetParametersDelegate? _setParameters;
        private GetStringWDelegate? _getStringW;
        private SetStringWDelegate? _setStringW;

        public VoicemeeterRemote(ILogger? logger = null) => _logger = logger;

        public bool IsAvailable => _handle != IntPtr.Zero && _login != null;

        /// <summary>The resolved path to VoicemeeterRemote64.dll, or null if not found.</summary>
        public string? DllPath { get; private set; }

        /// <summary>
        /// Locate and load VoicemeeterRemote64.dll. Idempotent; returns true once loaded.
        /// </summary>
        public bool Load()
        {
            if (IsAvailable) return true;

            if (!Environment.Is64BitProcess)
            {
                _logger?.LogError("Voicemeeter Remote requires a 64-bit process (VoicemeeterRemote64.dll).");
                return false;
            }

            var path = LocateDll();
            if (path is null || !File.Exists(path))
            {
                _logger?.LogInformation("VoicemeeterRemote64.dll not found — Voicemeeter not installed?");
                return false;
            }

            try
            {
                _handle = NativeLibrary.Load(path);
                DllPath = path;

                _login = Get<LoginDelegate>("VBVMR_Login");
                _logout = Get<LogoutDelegate>("VBVMR_Logout");
                _run = Get<RunVoicemeeterDelegate>("VBVMR_RunVoicemeeter");
                _getType = Get<GetTypeDelegate>("VBVMR_GetVoicemeeterType");
                _getVersion = Get<GetVersionDelegate>("VBVMR_GetVoicemeeterVersion");
                _isDirty = Get<IsDirtyDelegate>("VBVMR_IsParametersDirty");
                _getFloat = Get<GetFloatDelegate>("VBVMR_GetParameterFloat");
                _setFloat = Get<SetFloatDelegate>("VBVMR_SetParameterFloat");
                _getString = Get<GetStringDelegate>("VBVMR_GetParameterStringA");
                _setString = Get<SetStringDelegate>("VBVMR_SetParameterStringA");
                _setParameters = Get<SetParametersDelegate>("VBVMR_SetParameters");
                // Optional: fall back to the ANSI calls on a Remote DLL too old to have these.
                _getStringW = TryGet<GetStringWDelegate>("VBVMR_GetParameterStringW");
                _setStringW = TryGet<SetStringWDelegate>("VBVMR_SetParameterStringW");

                _logger?.LogInformation("Loaded VoicemeeterRemote64.dll from {Path}", path);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load VoicemeeterRemote64.dll from {Path}", path);
                Unload();
                return false;
            }
        }

        private T Get<T>(string name) where T : Delegate
        {
            var proc = NativeLibrary.GetExport(_handle, name);
            return Marshal.GetDelegateForFunctionPointer<T>(proc);
        }

        private T? TryGet<T>(string name) where T : Delegate =>
            NativeLibrary.TryGetExport(_handle, name, out var proc)
                ? Marshal.GetDelegateForFunctionPointer<T>(proc)
                : null;

        // ── raw calls ──
        public int Login() => _login!();
        public int Logout() => _logout!();
        public int RunVoicemeeter(int mode) => _run!(mode);

        public bool TryGetType(out int type)
        {
            type = 0;
            return _getType != null && _getType(out type) == 0;
        }

        public bool TryGetVersion(out int version)
        {
            version = 0;
            return _getVersion != null && _getVersion(out version) == 0;
        }

        /// <summary>Returns true when the engine has published a parameter change since last poll.</summary>
        public bool IsParametersDirty() => _isDirty != null && _isDirty() == 1;

        public bool TryGetFloat(string param, out float value)
        {
            value = 0f;
            return _getFloat != null && _getFloat(param, out value) == 0;
        }

        public bool SetFloat(string param, float value) => _setFloat != null && _setFloat(param, value) == 0;

        public bool SetString(string param, string value)
        {
            if (_setStringW != null) return _setStringW(param, value) == 0;
            return _setString != null && _setString(param, value) == 0;
        }

        public string? GetString(string param)
        {
            if (_getStringW != null)
            {
                // The API writes up to 512 UTF-16 characters, zero-terminated.
                var wide = new byte[1028];
                if (_getStringW(param, wide) != 0) return null;
                int chars = 0;
                while (chars < 512 && (wide[chars * 2] != 0 || wide[chars * 2 + 1] != 0)) chars++;
                return Encoding.Unicode.GetString(wide, 0, chars * 2);
            }

            if (_getString is null) return null;
            var buffer = new byte[512];
            if (_getString(param, buffer) != 0) return null;
            int len = Array.IndexOf(buffer, (byte)0);
            if (len < 0) len = buffer.Length;
            return Encoding.Latin1.GetString(buffer, 0, len);
        }

        /// <summary>Apply a multi-line parameter script in one call (atomic-ish).</summary>
        public bool SetParameters(string script) => _setParameters != null && _setParameters(script) == 0;

        // ─────────────────── DLL location ───────────────────

        private static readonly string[] RegistryKeys =
        {
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}",
        };

        private string? LocateDll()
        {
            // 1) Registry UninstallString → its directory holds the DLL.
            foreach (var keyPath in RegistryKeys)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                    var uninstall = key?.GetValue("UninstallString") as string;
                    if (string.IsNullOrWhiteSpace(uninstall)) continue;

                    var dir = Path.GetDirectoryName(uninstall.Trim('"'));
                    var candidate = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "VoicemeeterRemote64.dll");
                    if (candidate != null && File.Exists(candidate)) return candidate;
                }
                catch (Exception ex) { _logger?.LogDebug(ex, "Registry probe failed for {Key}", keyPath); }
            }

            // 2) Known install locations.
            foreach (var pf in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                var candidate = Path.Combine(pf, "VB", "Voicemeeter", "VoicemeeterRemote64.dll");
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        public void Unload()
        {
            _login = null; _logout = null; _run = null; _getType = null; _getVersion = null;
            _isDirty = null; _getFloat = null; _setFloat = null; _getString = null;
            _setString = null; _setParameters = null; _getStringW = null; _setStringW = null;

            if (_handle != IntPtr.Zero)
            {
                try { NativeLibrary.Free(_handle); } catch { /* ignore */ }
                _handle = IntPtr.Zero;
            }
        }

        public void Dispose() => Unload();
    }
}
