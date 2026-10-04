using System;
using System.Collections.Generic;
using System.Linq;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Navigation;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>Turns the page registry + the user's order / hidden / pinned settings into the sidebar groups.</summary>
    public static class NavModelBuilder
    {
        public const string PinnedHeader = "Pinned";

        /// <summary>A string that changes whenever the sidebar layout would change.</summary>
        public static string Signature(UiSettings s) =>
            string.Join("|", s.PageOrder) + "#" + string.Join("|", s.HiddenPages) + "#" + string.Join("|", s.PinnedPages);

        public static List<NavGroupViewModel> Build(UiSettings s) => Build(s, PageRegistry.All, PageRegistry.Groups);

        public static List<NavGroupViewModel> Build(UiSettings s, IReadOnlyList<PageDescriptor> pages, IReadOnlyList<string> groups)
        {
            var hidden = new HashSet<string>(s.HiddenPages, StringComparer.OrdinalIgnoreCase);
            var visible = pages.Where(p => !hidden.Contains(p.Key)).ToList();

            var result = new List<NavGroupViewModel>();

            // Pinned pages first, in the order they were pinned.
            var pinnedKeys = new List<string>();
            foreach (var key in s.PinnedPages)
            {
                var page = visible.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
                if (page != null && !pinnedKeys.Contains(page.Key)) pinnedKeys.Add(page.Key);
            }
            if (pinnedKeys.Count > 0)
            {
                var pinned = new NavGroupViewModel(PinnedHeader);
                foreach (var key in pinnedKeys)
                    pinned.Items.Add(new NavItemViewModel(visible.First(p => p.Key == key)));
                result.Add(pinned);
            }

            // Pages named in PageOrder come first (in that order), the rest keep registry order.
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < s.PageOrder.Count; i++)
                rank.TryAdd(s.PageOrder[i], i);
            int RankOf(PageDescriptor p) => rank.TryGetValue(p.Key, out var r) ? r : int.MaxValue;

            foreach (var groupName in groups)
            {
                var items = visible
                    .Where(p => string.Equals(p.Group, groupName, StringComparison.OrdinalIgnoreCase)
                                && !pinnedKeys.Contains(p.Key))
                    .OrderBy(RankOf) // stable: ties keep registry order
                    .ToList();
                if (items.Count == 0) continue;

                var group = new NavGroupViewModel(groupName);
                foreach (var p in items) group.Items.Add(new NavItemViewModel(p));
                result.Add(group);
            }

            return result;
        }
    }
}
