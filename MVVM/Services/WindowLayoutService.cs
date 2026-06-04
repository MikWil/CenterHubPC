using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Capture the placement (position, size, monitor, show-state) of every
    /// visible top-level window on the desktop, persist named "layouts" to
    /// disk, and restore them on demand.
    ///
    /// Matching strategy on Apply:
    ///   - Group both saved-snapshots and live-windows by process name.
    ///   - Pair the i-th saved snapshot with the i-th live window that
    ///     contains the saved title fragment (case-insensitive). Fall back
    ///     to positional order if no title overlap.
    ///   - Windows with no live counterpart are silently skipped.
    ///   - Live windows with no saved counterpart are left untouched.
    /// </summary>
    public sealed class WindowLayoutService
    {
        private readonly ILogger<WindowLayoutService>? _logger;
        private readonly string _storePath;
        private readonly List<WindowLayout> _layouts = new();

        public IReadOnlyList<WindowLayout> Layouts => _layouts;

        public WindowLayoutService(ILogger<WindowLayoutService>? logger = null)
        {
            _logger = logger;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CenterHub");
            Directory.CreateDirectory(dir);
            _storePath = Path.Combine(dir, "layouts.json");
            Load();
        }

        // ─────────────────── Persistence ───────────────────

        public void Load()
        {
            _layouts.Clear();
            try
            {
                if (!File.Exists(_storePath)) return;
                var json = File.ReadAllText(_storePath);
                var dto = JsonSerializer.Deserialize<WindowLayoutsDto>(json);
                if (dto?.Layouts != null) _layouts.AddRange(dto.Layouts);
                _logger?.LogInformation("Loaded {Count} window layouts", _layouts.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load window layouts from {Path}", _storePath);
            }
        }

        public void Save()
        {
            try
            {
                var dto = new WindowLayoutsDto { Layouts = _layouts };
                var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storePath, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save window layouts to {Path}", _storePath);
            }
        }

        // ─────────────────── Public ops ───────────────────

        public WindowLayout AddLayout(string name)
        {
            var layout = new WindowLayout
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim(),
                SavedAt = DateTime.Now,
                Windows = CaptureCurrentWindows(),
            };
            _layouts.Add(layout);
            Save();
            _logger?.LogInformation("Saved layout '{Name}' with {Count} windows", layout.Name, layout.Windows.Count);
            return layout;
        }

        public void DeleteLayout(WindowLayout layout)
        {
            if (_layouts.Remove(layout))
            {
                Save();
                _logger?.LogInformation("Deleted layout '{Name}'", layout.Name);
            }
        }

        public void RenameLayout(WindowLayout layout, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            layout.Name = newName.Trim();
            Save();
        }

        /// <summary>Re-capture the current windows into an existing layout (overwrites).</summary>
        public void RecaptureLayout(WindowLayout layout)
        {
            layout.Windows = CaptureCurrentWindows();
            layout.SavedAt = DateTime.Now;
            Save();
            _logger?.LogInformation("Re-captured layout '{Name}' — now {Count} windows",
                layout.Name, layout.Windows.Count);
        }

        public int ApplyLayout(WindowLayout layout)
        {
            if (layout?.Windows == null || layout.Windows.Count == 0) return 0;
            var applied = 0;

            // Build a live snapshot of all candidate windows once
            var live = EnumerateLiveWindows();

            // Group both sides by process for ordered matching
            foreach (var procGroup in layout.Windows
                         .GroupBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                var saved = procGroup.ToList();
                var candidates = live
                    .Where(l => string.Equals(l.ProcessName, procGroup.Key, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var consumed = new HashSet<IntPtr>();

                foreach (var snap in saved)
                {
                    // Prefer the candidate whose title CONTAINS the saved fragment (case-insensitive)
                    var match = candidates.FirstOrDefault(c =>
                        !consumed.Contains(c.Hwnd) &&
                        !string.IsNullOrEmpty(snap.TitlePattern) &&
                        c.Title.Contains(snap.TitlePattern, StringComparison.OrdinalIgnoreCase));

                    // Fall back: next free candidate in enum order
                    if (match.Hwnd == IntPtr.Zero)
                        match = candidates.FirstOrDefault(c => !consumed.Contains(c.Hwnd));

                    if (match.Hwnd == IntPtr.Zero) continue;

                    consumed.Add(match.Hwnd);
                    if (ApplyToWindow(match.Hwnd, snap)) applied++;
                }
            }

            _logger?.LogInformation("Applied layout '{Name}': {Applied}/{Total} windows placed",
                layout.Name, applied, layout.Windows.Count);
            return applied;
        }

        // ─────────────────── Capture ───────────────────

        private List<WindowSnapshot> CaptureCurrentWindows()
        {
            var snaps = new List<WindowSnapshot>();
            foreach (var w in EnumerateLiveWindows())
            {
                var hwnd = w.Hwnd;

                // Decide state — use the dedicated APIs, not WINDOWPLACEMENT.showCmd
                int showState;
                if (IsIconic(hwnd))      showState = SW_SHOWMINIMIZED;
                else if (IsZoomed(hwnd)) showState = SW_SHOWMAXIMIZED;
                else                     showState = SW_SHOWNORMAL;

                RECT visible;
                if (showState == SW_SHOWNORMAL)
                {
                    // Capture the user-VISIBLE rect (excludes the invisible DWM shadow on Win10+)
                    visible = GetVisibleWindowRect(hwnd);
                }
                else
                {
                    // Maximized/minimized — record the "restored" rect from WINDOWPLACEMENT,
                    // which is in workspace coords. We translate those to screen coords using
                    // the primary monitor's work-area origin (which is what workspace 0,0 maps to).
                    if (!GetWindowPlacement(hwnd, out var wp)) continue;
                    visible = WorkspaceToScreen(wp.rcNormalPosition);
                }

                snaps.Add(new WindowSnapshot
                {
                    ProcessName    = w.ProcessName,
                    TitlePattern   = ExtractTitleFragment(w.Title, w.ProcessName),
                    ExecutablePath = w.ExecutablePath,
                    MonitorIndex   = GetMonitorIndex(hwnd),
                    X              = visible.Left,
                    Y              = visible.Top,
                    Width          = visible.Right - visible.Left,
                    Height         = visible.Bottom - visible.Top,
                    ShowState      = showState,
                });
            }
            return snaps;
        }

        /// <summary>
        /// Returns the user-VISIBLE rect of the window (DWM extended frame bounds on
        /// Win10+, falling back to GetWindowRect). This is the rect a user would
        /// describe if asked "where is the window?" — does NOT include the invisible
        /// drop-shadow border that Win10/11 adds.
        /// </summary>
        private static RECT GetVisibleWindowRect(IntPtr hwnd)
        {
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS,
                    out RECT ext, Marshal.SizeOf<RECT>()) == 0)
            {
                return ext;
            }
            GetWindowRect(hwnd, out var fallback);
            return fallback;
        }

        /// <summary>
        /// Returns (left, top, right, bottom) offsets between the GetWindowRect rect
        /// (what the OS treats as the window) and the DWM-extended-frame-bounds rect
        /// (what the user sees). Used to translate user-visible coordinates back into
        /// values SetWindowPos expects.
        /// </summary>
        private static (int left, int top, int right, int bottom) GetDwmFrameOffsets(IntPtr hwnd)
        {
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS,
                    out RECT ext, Marshal.SizeOf<RECT>()) == 0 &&
                GetWindowRect(hwnd, out var win))
            {
                return (
                    ext.Left   - win.Left,
                    ext.Top    - win.Top,
                    win.Right  - ext.Right,
                    win.Bottom - ext.Bottom);
            }
            return (0, 0, 0, 0);
        }

        /// <summary>
        /// Translate WINDOWPLACEMENT workspace coords to screen coords using the
        /// primary monitor's work-area origin. Accurate for windows on the primary
        /// monitor; approximate for cross-monitor maximized windows.
        /// </summary>
        private static RECT WorkspaceToScreen(RECT ws)
        {
            if (GetPrimaryWorkArea(out var pw))
            {
                return new RECT
                {
                    Left   = ws.Left   + pw.Left,
                    Top    = ws.Top    + pw.Top,
                    Right  = ws.Right  + pw.Left,
                    Bottom = ws.Bottom + pw.Top,
                };
            }
            return ws;
        }

        private List<LiveWindow> EnumerateLiveWindows()
        {
            var list = new List<LiveWindow>();
            var selfPid = Environment.ProcessId;

            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true; // skip owned popups
                var ex = (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
                if ((ex & WS_EX_TOOLWINDOW) != 0) return true; // skip toolbars/tray-y stuff

                var len = GetWindowTextLength(hwnd);
                if (len <= 0) return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == (uint)selfPid) return true; // skip our own window

                string procName = "", exePath = "";
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    procName = proc.ProcessName + ".exe";
                    try { exePath = proc.MainModule?.FileName ?? ""; } catch { /* access denied; ignore */ }
                }
                catch { return true; }

                list.Add(new LiveWindow
                {
                    Hwnd = hwnd,
                    Title = title,
                    ProcessName = procName,
                    ExecutablePath = exePath,
                });
                return true;
            }, IntPtr.Zero);

            return list;
        }

        private static string ExtractTitleFragment(string title, string processName)
        {
            // Many apps suffix the title with " - AppName"; the leading part is
            // the most identifying (the doc/tab/page name). Use the first
            // 32 chars of the title, stripped of process-name spam.
            var t = title.Trim();
            var procBase = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName[..^4]
                : processName;
            // Strip a trailing " - <ProcessName>" if present
            var idx = t.LastIndexOf(" - " + procBase, StringComparison.OrdinalIgnoreCase);
            if (idx > 0) t = t[..idx];
            return t.Length > 32 ? t[..32] : t;
        }

        // ─────────────────── Apply ───────────────────

        private bool ApplyToWindow(IntPtr hwnd, WindowSnapshot snap)
        {
            try
            {
                // 1. If currently min/maxed, restore first so SetWindowPos can move/resize freely.
                if (IsIconic(hwnd) || IsZoomed(hwnd))
                {
                    ShowWindow(hwnd, SW_RESTORE);
                }

                // 2. Build the target VISIBLE rect (user-perceived position/size).
                var target = new RECT
                {
                    Left   = snap.X,
                    Top    = snap.Y,
                    Right  = snap.X + snap.Width,
                    Bottom = snap.Y + snap.Height,
                };

                // 3. If the captured rect is off all current monitors (e.g. external
                //    display was unplugged), clamp into the primary monitor's work area.
                if (!IsRectOnAnyMonitor(target) && GetPrimaryWorkArea(out var work))
                {
                    var w = Math.Min(snap.Width,  work.Right - work.Left);
                    var h = Math.Min(snap.Height, work.Bottom - work.Top);
                    var x = Math.Clamp(snap.X, work.Left, work.Right - w);
                    var y = Math.Clamp(snap.Y, work.Top,  work.Bottom - h);
                    target = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
                }

                // 4. The user-visible rect (target) is smaller than the OS window rect
                //    that SetWindowPos expects, because of the DWM invisible border.
                //    We have to pre-position so SetWindowPos picks up the per-window
                //    border offsets, then re-position with compensation.
                //
                //    Approach: do an initial SetWindowPos to land on the right monitor,
                //    measure the DWM offsets, then do a second SetWindowPos with the
                //    visible rect inflated by those offsets.
                if (!SetWindowPos(hwnd, IntPtr.Zero,
                        target.Left, target.Top,
                        target.Right - target.Left, target.Bottom - target.Top,
                        SWP_NOZORDER | SWP_NOACTIVATE))
                {
                    _logger?.LogWarning("Initial SetWindowPos failed for {Hwnd}", hwnd);
                    return false;
                }

                var (offL, offT, offR, offB) = GetDwmFrameOffsets(hwnd);
                if (offL != 0 || offT != 0 || offR != 0 || offB != 0)
                {
                    var fixedX = target.Left   - offL;
                    var fixedY = target.Top    - offT;
                    var fixedW = (target.Right - target.Left) + offL + offR;
                    var fixedH = (target.Bottom - target.Top) + offT + offB;
                    SetWindowPos(hwnd, IntPtr.Zero, fixedX, fixedY, fixedW, fixedH,
                        SWP_NOZORDER | SWP_NOACTIVATE);
                }

                // 5. Re-apply the saved state. Maximize honors whichever monitor the
                //    window is currently on (set in steps 2–4), so this max'es onto
                //    the saved monitor as intended.
                if (snap.ShowState == SW_SHOWMAXIMIZED)
                    ShowWindow(hwnd, SW_SHOWMAXIMIZED);
                else if (snap.ShowState == SW_SHOWMINIMIZED)
                    ShowWindow(hwnd, SW_SHOWMINIMIZED);

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to place window {Hwnd}", hwnd);
                return false;
            }
        }

        private static bool IsRectOnAnyMonitor(RECT r)
        {
            var visible = false;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr mon, IntPtr _, ref RECT _, IntPtr _) =>
            {
                var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(mon, ref mi))
                {
                    if (IntersectsLoose(r, mi.rcWork)) visible = true;
                }
                return true;
            }, IntPtr.Zero);
            return visible;
        }

        private static bool IntersectsLoose(RECT a, RECT b)
        {
            // Any overlap counts (loose check)
            return a.Right > b.Left && a.Left < b.Right && a.Bottom > b.Top && a.Top < b.Bottom;
        }

        private static bool GetPrimaryWorkArea(out RECT work)
        {
            work = default;
            RECT captured = default;
            var found = false;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr mon, IntPtr _, ref RECT _, IntPtr _) =>
            {
                var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(mon, ref mi) && (mi.dwFlags & MONITORINFOF_PRIMARY) != 0)
                {
                    captured = mi.rcWork;
                    found = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            work = captured;
            return found;
        }

        private static int GetMonitorIndex(IntPtr hwnd)
        {
            var target = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var idx = 0;
            var matched = 0;
            var counter = 0;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr mon, IntPtr _, ref RECT _, IntPtr _) =>
            {
                if (mon == target) { matched = counter; return false; }
                counter++;
                return true;
            }, IntPtr.Zero);
            idx = matched;
            return idx;
        }

        // ─────────────────── P/Invoke ───────────────────

        private struct LiveWindow
        {
            public IntPtr Hwnd;
            public string Title;
            public string ProcessName;
            public string ExecutablePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public uint length;
            public uint flags;
            public int  showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const int SW_SHOWNORMAL     = 1;
        private const int SW_SHOWMINIMIZED  = 2;
        private const int SW_SHOWMAXIMIZED  = 3;
        private const int SW_RESTORE        = 9;

        private const int GWL_EXSTYLE   = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const uint GW_OWNER = 4;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const uint MONITORINFOF_PRIMARY = 0x00000001;

        // SetWindowPos flags
        private const uint SWP_NOZORDER   = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        // DwmGetWindowAttribute attribute id for "the rect the user sees"
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr hWnd, out WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute,
            out RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    }
}
