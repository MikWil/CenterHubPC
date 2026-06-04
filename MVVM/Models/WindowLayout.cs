using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterHubNew.MVVM.Models
{
    /// <summary>
    /// One window's placement snapshot. Identified by process name + a fragment
    /// of the title for disambiguation when there are multiple instances.
    /// </summary>
    public sealed class WindowSnapshot
    {
        public string ProcessName  { get; set; } = "";        // e.g. "Code.exe"
        public string TitlePattern { get; set; } = "";        // best-match fragment
        public string ExecutablePath { get; set; } = "";      // for future "launch if missing"

        public int MonitorIndex { get; set; }                 // index into Screens.All at capture time
        public int X { get; set; }
        public int Y { get; set; }
        public int Width  { get; set; }
        public int Height { get; set; }

        /// <summary>0 = Hidden, 1 = Normal, 2 = Minimized, 3 = Maximized.</summary>
        public int ShowState { get; set; }
    }

    public sealed class WindowLayout
    {
        public string Name { get; set; } = "Untitled";
        public DateTime SavedAt { get; set; } = DateTime.Now;
        public List<WindowSnapshot> Windows { get; set; } = new();
    }

    /// <summary>
    /// Display-friendly observable wrapper used by the view.
    /// </summary>
    public partial class WindowLayoutItem : ObservableObject
    {
        public WindowLayout Layout { get; init; } = new();

        public string Name           => Layout.Name;
        public DateTime SavedAt      => Layout.SavedAt;
        public int WindowCount       => Layout.Windows.Count;
        public string SavedAtDisplay => Layout.SavedAt.ToString("MMM d, HH:mm");
        public string CountDisplay   => Layout.Windows.Count == 1
            ? "1 window"
            : $"{Layout.Windows.Count} windows";

        /// <summary>Comma-joined list of top processes for the card subtitle.</summary>
        public string AppsDisplay
        {
            get
            {
                if (Layout.Windows.Count == 0) return "Empty";
                var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var w in Layout.Windows)
                {
                    var n = w.ProcessName;
                    if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        n = n[..^4];
                    apps.Add(n);
                    if (apps.Count >= 4) break;
                }
                return string.Join(" · ", apps);
            }
        }
    }

    /// <summary>Root for layouts.json persistence.</summary>
    public class WindowLayoutsDto
    {
        public List<WindowLayout> Layouts { get; set; } = new();
    }
}
