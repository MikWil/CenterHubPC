using System;
using System.Collections.Generic;
using System.Linq;
using CenterHubNew.MVVM.Models;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// The built-in drum styles ("songs"). Each style is authored as drum tabs
    /// (see <see cref="DrumBar"/>): an intro fill, parts with a main groove, fills and a
    /// transition, and an outro fill. The genre files (DrumStyleLibrary.*.cs) hold the
    /// content; this file is the registry.
    /// </summary>
    public static partial class DrumStyleLibrary
    {
        private static readonly Lazy<IReadOnlyList<DrumStyle>> _all = new(() =>
            RockAndPopStyles().Concat(GrooveAndWorldStyles()).ToList());

        /// <summary>Every built-in style, in display order.</summary>
        public static IReadOnlyList<DrumStyle> All => _all.Value;

        /// <summary>Distinct genres, in display order.</summary>
        public static IReadOnlyList<string> Genres => All.Select(s => s.Genre).Distinct().ToList();

        /// <summary>Finds a style by its stable id (null when it no longer exists).</summary>
        public static DrumStyle? Find(string? id) =>
            string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(s => s.Id == id);

        /// <summary>
        /// Checks a style's structure; returns the problems found (empty = valid).
        /// Used by the unit tests so a typo in a tab can never ship.
        /// </summary>
        public static IReadOnlyList<string> Validate(DrumStyle style)
        {
            var problems = new List<string>();
            void Check(bool ok, string message) { if (!ok) problems.Add($"{style.Id}: {message}"); }

            Check(!string.IsNullOrWhiteSpace(style.Id), "missing id");
            Check(!string.IsNullOrWhiteSpace(style.Name), "missing name");
            Check(!string.IsNullOrWhiteSpace(style.Genre), "missing genre");
            Check(style.Beats is >= 1 and <= 12, "beats must be 1..12");
            Check(style.StepsPerBeat > 0 && 48 % style.StepsPerBeat == 0, "steps per beat must divide 48");
            Check(style.DefaultBpm is >= 30 and <= 280, "default tempo out of range");
            Check(style.Parts.Count >= 1, "needs at least one part");

            void CheckBar(DrumBar? bar, string where, bool required)
            {
                if (bar is null) { Check(!required, $"{where} is missing"); return; }
                Check(bar.Steps == style.StepsPerBar, $"{where} has {bar.Steps} steps, expected {style.StepsPerBar}");
                Check(bar.Voices.Count > 0, $"{where} is empty");
            }

            CheckBar(style.Intro, "intro", required: true);
            CheckBar(style.Outro, "outro", required: true);
            for (int p = 0; p < style.Parts.Count; p++)
            {
                var part = style.Parts[p];
                string name = $"part {p + 1} ({part.Name})";
                Check(!string.IsNullOrWhiteSpace(part.Name), $"part {p + 1} has no name");
                Check(part.Main.Count >= 1, $"{name} has no main bars");
                Check(part.Fills.Count >= 1, $"{name} has no fills");
                for (int i = 0; i < part.Main.Count; i++) CheckBar(part.Main[i], $"{name} main bar {i + 1}", true);
                for (int i = 0; i < part.Fills.Count; i++) CheckBar(part.Fills[i], $"{name} fill {i + 1}", true);
                CheckBar(part.Transition, $"{name} transition", required: true);
            }
            return problems;
        }

        // Shorthand used by the genre files.
        private static DrumBar Bar(params string[] lines) => DrumBar.Parse(lines);
    }
}
