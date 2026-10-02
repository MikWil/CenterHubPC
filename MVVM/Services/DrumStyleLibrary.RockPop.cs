using System.Collections.Generic;
using CenterHubNew.MVVM.Models;

namespace CenterHubNew.MVVM.Services
{
    // Straight-feel rock, pop, metal, punk, disco and country styles.
    public static partial class DrumStyleLibrary
    {
        private static IEnumerable<DrumStyle> RockAndPopStyles()
        {
            // ───────────────────────── Rock 8ths ─────────────────────────
            // The reference style: every other style follows this shape.
            // 16 steps = 4 beats x sixteenths. Fills keep the groove for the first half
            // of the bar and fill the second half, so they work wherever they are triggered.
            yield return new DrumStyle
            {
                Id = "rock-8ths",
                Name = "Rock 8ths",
                Genre = "Rock",
                Description = "Straight eighths, backbeat on 2 and 4",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 116,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "SD|x-x-|x-x-|xxxx|----|",
                    "T1|----|----|----|xx--|",
                    "T3|----|----|----|--xx|",
                    "BD|x---|x---|x---|x---|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-x-|x-x-|x-x-|x-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x---|----|x-x-|----|"),
                            Bar("HH|x-x-|x-x-|x-x-|x-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x---|----|x-x-|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-x-|x-x-|----|----|",
                                "SD|----|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T3|----|----|----|--xx|",
                                "BD|x---|----|x---|x---|"),
                            Bar("HH|x-x-|x-x-|x-x-|----|",
                                "SD|----|X---|----|xx--|",
                                "T2|----|----|----|--x-|",
                                "T3|----|----|----|---x|",
                                "BD|x---|----|x-x-|----|"),
                        },
                        Transition = Bar(
                            "HH|x-x-|x-x-|----|----|",
                            "SD|----|X---|ssxx|xxXX|",
                            "BD|x---|----|x---|x---|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|x-x-|x-x-|x-x-|x-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x-x-|----|x-x-|----|"),
                            Bar("RD|x-x-|x-x-|x-x-|x-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x-x-|----|x-x-|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|x-x-|x-x-|----|----|",
                                "SD|----|X---|x-xx|----|",
                                "T1|----|----|----|x-x-|",
                                "T3|----|----|----|-x-x|",
                                "BD|x-x-|----|x---|x---|"),
                            Bar("RD|x-x-|x-x-|x-x-|----|",
                                "SD|----|X---|----|xxxx|",
                                "BD|x-x-|----|x-x-|x---|"),
                        },
                        Transition = Bar(
                            "RD|x-x-|x-x-|----|----|",
                            "SD|----|X---|xx--|----|",
                            "T1|----|----|--xx|----|",
                            "T2|----|----|----|xx--|",
                            "T3|----|----|----|--xx|",
                            "BD|x-x-|----|x---|x---|"),
                    },
                },
                Outro = Bar(
                    "HH|x-x-|x-x-|----|----|",
                    "SD|----|X---|X-X-|XXXX|",
                    "BD|x---|----|x-x-|x---|"),
            };

            // ───────────────────────── Driving Rock ─────────────────────────
            // Hard rock: accented 8th hats and a kick that pushes; the chorus opens the hats.
            yield return new DrumStyle
            {
                Id = "rock-driving",
                Name = "Driving Rock",
                Genre = "Rock",
                Description = "Hard 8ths, pushing kick, open hats in the chorus",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 132,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "SD|x-x-|x-x-|xxxx|----|",
                    "T1|----|----|----|xx--|",
                    "T2|----|----|----|--x-|",
                    "T3|----|----|----|---x|",
                    "BD|x---|x---|x---|x---|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|X-x-|X-x-|X-x-|X-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x---|----|x---|--x-|"),
                            Bar("HH|X-x-|X-x-|X-x-|X-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x---|--x-|x---|x-x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|X-x-|X-x-|----|----|",
                                "SD|----|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|x---|----|x---|x---|"),
                            Bar("HH|X-x-|X-x-|X-x-|----|",
                                "SD|----|X---|----|xxx-|",
                                "T3|----|----|----|---X|",
                                "BD|x---|----|x---|----|"),
                            Bar("HH|X-x-|X-x-|X-x-|----|",
                                "SD|----|X---|----|--xX|",
                                "BD|x---|----|x-x-|xx--|"),
                        },
                        Transition = Bar(
                            "HH|X-x-|X-x-|----|----|",
                            "SD|----|X---|ssxx|x---|",
                            "T2|----|----|----|-xx-|",
                            "T3|----|----|----|---X|",
                            "BD|x---|----|x---|x---|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("OH|X---|x---|x---|x---|",
                                "HH|--x-|--x-|--x-|--x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x-x-|----|x-x-|--x-|"),
                            Bar("OH|X---|x---|x---|x---|",
                                "HH|--x-|--x-|--x-|--x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x-x-|--x-|x-x-|----|"),
                        },
                        Fills = new[]
                        {
                            Bar("OH|X---|x---|----|----|",
                                "HH|--x-|--x-|----|----|",
                                "SD|----|X---|x-xx|----|",
                                "T1|----|----|----|xx--|",
                                "T3|----|----|----|--xx|",
                                "BD|x-x-|----|x---|x---|"),
                            Bar("OH|X---|x---|x---|----|",
                                "HH|--x-|--x-|--x-|----|",
                                "SD|----|X---|----|xxxx|",
                                "BD|x-x-|----|x-x-|x---|"),
                            Bar("OH|X---|x---|----|----|",
                                "HH|--x-|--x-|----|----|",
                                "SD|----|X---|----|----|",
                                "T1|----|----|x-x-|----|",
                                "T2|----|----|----|x-x-|",
                                "T3|----|----|----|---X|",
                                "BD|x-x-|----|x---|x-x-|"),
                        },
                        Transition = Bar(
                            "OH|X---|x---|----|----|",
                            "HH|--x-|--x-|----|----|",
                            "SD|----|X---|ssxx|----|",
                            "T1|----|----|----|xx--|",
                            "T2|----|----|----|--x-|",
                            "T3|----|----|----|---X|",
                            "BD|x-x-|----|x---|x---|"),
                    },
                },
                Outro = Bar(
                    "OH|X---|x---|----|----|",
                    "HH|--x-|--x-|----|----|",
                    "SD|----|X---|X-X-|XXXX|",
                    "BD|x-x-|----|x-x-|x-x-|"),
            };

            // ───────────────────────── Half-Time Rock ─────────────────────────
            // Snare only on beat 3: heavy and spacious. The chorus moves to the ride with bell accents.
            yield return new DrumStyle
            {
                Id = "rock-half-time",
                Name = "Half-Time Rock",
                Genre = "Rock",
                Description = "Snare on 3 only, heavy and spacious",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 84,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "BD|x---|----|----|----|",
                    "T1|----|x---|----|----|",
                    "T2|----|----|x---|----|",
                    "T3|----|----|----|X---|",
                    "SD|----|----|----|--xx|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|X-x-|x-x-|X-x-|x-x-|",
                                "SD|----|----|X---|----|",
                                "BD|x---|--x-|----|----|"),
                            Bar("HH|X-x-|x-x-|X-x-|x-x-|",
                                "SD|----|----|X---|----|",
                                "BD|x---|--x-|----|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|X-x-|x-x-|----|----|",
                                "SD|----|----|X-xx|xxXX|",
                                "BD|x---|--x-|x---|x---|"),
                            Bar("HH|X-x-|x-x-|----|----|",
                                "SD|----|----|X---|----|",
                                "T1|----|----|--xx|----|",
                                "T2|----|----|----|xx--|",
                                "T3|----|----|----|--xX|",
                                "BD|x---|--x-|x---|x---|"),
                            Bar("HH|X-x-|x-x-|X-x-|----|",
                                "SD|----|----|X--g|--xX|",
                                "BD|x---|--x-|----|x-x-|"),
                        },
                        Transition = Bar(
                            "HH|X-x-|x-x-|----|----|",
                            "SD|----|----|ssxx|----|",
                            "T1|----|----|----|xx--|",
                            "T2|----|----|----|--xx|",
                            "BD|x---|--x-|x---|x--X|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RB|X---|----|X---|----|",
                                "RD|--x-|x-x-|--x-|x-x-|",
                                "SD|----|----|X---|----|",
                                "BD|x---|--x-|--x-|----|"),
                            Bar("RB|X---|----|X---|----|",
                                "RD|--x-|x-x-|--x-|x-x-|",
                                "SD|----|----|X---|----|",
                                "BD|x---|--x-|----|x-x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("RB|X---|----|----|----|",
                                "RD|--x-|x-x-|----|----|",
                                "SD|----|----|X-xx|xxXX|",
                                "BD|x---|--x-|x---|x---|"),
                            Bar("RB|X---|----|----|----|",
                                "RD|--x-|x-x-|----|----|",
                                "SD|----|----|X---|----|",
                                "T1|----|----|--xx|----|",
                                "T2|----|----|----|xx--|",
                                "T3|----|----|----|--xX|",
                                "BD|x---|--x-|x---|x---|"),
                            Bar("RB|X---|----|X---|----|",
                                "RD|--x-|x-x-|----|----|",
                                "SD|----|----|X--g|--xX|",
                                "BD|x---|--x-|----|x-x-|"),
                        },
                        Transition = Bar(
                            "RB|X---|----|----|----|",
                            "RD|--x-|x-x-|----|----|",
                            "SD|----|----|ssss|xx--|",
                            "T2|----|----|----|--x-|",
                            "T3|----|----|----|---X|",
                            "BD|x---|--x-|x---|x---|"),
                    },
                },
                Outro = Bar(
                    "RB|X---|----|----|----|",
                    "RD|--x-|x-x-|----|----|",
                    "SD|----|----|X---|XXXX|",
                    "BD|x---|--x-|x---|x-x-|"),
            };

            // ───────────────────────── Rock Ballad ─────────────────────────
            // Slow, soft 8ths. Verse on side stick, chorus opens up to full snare and ride.
            yield return new DrumStyle
            {
                Id = "rock-ballad",
                Name = "Rock Ballad",
                Genre = "Rock",
                Description = "Slow 8ths, side stick verse, full snare + ride chorus",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 68,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "HH|x-s-|x-s-|----|----|",
                    "SS|----|x---|----|----|",
                    "T1|----|----|x-x-|----|",
                    "T2|----|----|----|x---|",
                    "T3|----|----|----|--X-|",
                    "BD|x---|----|x---|----|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-s-|x-s-|x-s-|x-s-|",
                                "SS|----|x---|----|x---|",
                                "BD|x---|----|--x-|----|"),
                            Bar("HH|x-s-|x-s-|x-s-|x-s-|",
                                "SS|----|x---|----|x---|",
                                "BD|x---|----|x-x-|----|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-s-|x-s-|----|----|",
                                "SS|----|x---|----|----|",
                                "SD|----|----|s-s-|x-xX|",
                                "BD|x---|----|x---|x---|"),
                            Bar("HH|x-s-|x-s-|x-s-|----|",
                                "SS|----|x---|----|----|",
                                "T2|----|----|----|x-x-|",
                                "T3|----|----|----|---X|",
                                "BD|x---|----|--x-|----|"),
                        },
                        Transition = Bar(
                            "HH|x-s-|x-s-|----|----|",
                            "SS|----|x---|----|----|",
                            "SD|----|----|ssss|xxXX|",
                            "BD|x---|----|x---|x---|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|X-x-|x-x-|x-x-|x-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x---|----|x-x-|----|"),
                            Bar("RD|X-x-|x-x-|x-x-|x-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x---|--x-|x---|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X-x-|x-x-|----|----|",
                                "SD|----|X---|x---|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|x---|----|x---|x---|"),
                            Bar("RD|X-x-|x-x-|x-x-|----|",
                                "SD|----|X---|----|xxXX|",
                                "BD|x---|----|x-x-|----|"),
                        },
                        Transition = Bar(
                            "RD|X-x-|x-x-|----|----|",
                            "SD|----|X---|ssxx|x---|",
                            "T1|----|----|----|-x--|",
                            "T2|----|----|----|--x-|",
                            "T3|----|----|----|---X|",
                            "BD|x---|----|x---|x---|"),
                    },
                },
                Outro = Bar(
                    "RD|X-x-|x-x-|----|----|",
                    "SD|----|X---|s-s-|xxXX|",
                    "BD|x---|----|x---|x---|"),
            };

            // ───────────────────────── Pop 16ths ─────────────────────────
            // Busy accented sixteenth hats over a syncopated kick; the chorus opens the off-beat hats.
            yield return new DrumStyle
            {
                Id = "pop-16ths",
                Name = "Pop 16ths",
                Genre = "Pop",
                Description = "Sixteenth hats, syncopated kick",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 96,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "HH|Xsxs|Xsxs|----|----|",
                    "SD|----|X---|ssxx|xxXX|",
                    "BD|x---|--x-|x---|----|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|Xsxs|Xsxs|Xsxs|Xsxs|",
                                "SD|----|X---|----|X---|",
                                "BD|x---|--x-|x---|-x--|"),
                            Bar("HH|Xsxs|Xsxs|Xsxs|Xsxs|",
                                "SD|----|X--g|----|X---|",
                                "BD|x---|--x-|x---|x-x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|Xsxs|Xsxs|----|----|",
                                "SD|----|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|x---|--x-|x---|x---|"),
                            Bar("HH|Xsxs|Xsxs|Xsxs|----|",
                                "SD|----|X---|----|x-xX|",
                                "BD|x---|--x-|x---|----|"),
                            Bar("HH|Xsxs|Xsxs|----|----|",
                                "SD|----|X---|--x-|x-x-|",
                                "T3|----|----|----|---X|",
                                "BD|x--x|--x-|x---|x---|"),
                        },
                        Transition = Bar(
                            "HH|Xsxs|Xsxs|----|----|",
                            "SD|----|X---|ssxx|xxXX|",
                            "BD|x---|--x-|x---|x-x-|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("HH|Xs-x|Xs-x|Xs-x|Xs-x|",
                                "OH|--x-|--x-|--x-|--x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x--x|--x-|x---|-x--|"),
                            Bar("HH|Xs-x|Xs-x|Xs-x|Xs-x|",
                                "OH|--x-|--x-|--x-|--x-|",
                                "SD|---g|X---|--g-|X--g|",
                                "BD|x---|--x-|x--x|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|Xs-x|Xs-x|----|----|",
                                "OH|--x-|--x-|----|----|",
                                "SD|----|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T3|----|----|----|--xx|",
                                "BD|x--x|--x-|x---|x---|"),
                            Bar("HH|Xs-x|Xs-x|Xs-x|----|",
                                "OH|--x-|--x-|--x-|----|",
                                "SD|----|X---|----|xxxx|",
                                "BD|x--x|--x-|x---|x---|"),
                        },
                        Transition = Bar(
                            "HH|Xs-x|Xs-x|----|----|",
                            "OH|--x-|--x-|----|----|",
                            "SD|----|X---|ssxx|xxXX|",
                            "BD|x--x|--x-|x---|x-x-|"),
                    },
                },
                Outro = Bar(
                    "HH|Xsxs|Xsxs|----|----|",
                    "SD|----|X---|X-X-|xxXX|",
                    "BD|x---|--x-|x---|x-x-|"),
            };

            // ───────────────────────── Four on the Floor ─────────────────────────
            // Electro pop: kick on every beat, clap + snare on 2 and 4, open hats on the off-beats.
            yield return new DrumStyle
            {
                Id = "pop-four-floor",
                Name = "Four on the Floor",
                Genre = "Pop",
                Description = "Kick on every beat, clap backbeat, off-beat open hats",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 120,
                SuggestedKit = DrumKitKind.Electro,
                Intro = Bar(
                    "BD|x---|x---|x---|x---|",
                    "SD|----|x---|ssxx|xxXX|",
                    "CP|----|X---|----|----|",
                    "HH|x--s|x--s|----|----|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|x---|----|x---|",
                                "CP|----|X---|----|X---|",
                                "HH|x--s|x--s|x--s|x--s|",
                                "OH|--x-|--x-|--x-|--x-|"),
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|x---|--g-|x-g-|",
                                "CP|----|X---|----|X---|",
                                "HH|x--s|x--s|x--s|x--s|",
                                "OH|--x-|--x-|--x-|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|x---|xxxx|----|",
                                "CP|----|X---|----|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "HH|x--s|x--s|----|----|",
                                "OH|--x-|--x-|----|----|"),
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|x---|----|xxxx|",
                                "CP|----|X---|----|X---|",
                                "HH|x--s|x--s|x--s|----|",
                                "OH|--x-|--x-|--x-|----|"),
                            Bar("BD|x---|x---|x---|----|",
                                "SD|----|x---|----|----|",
                                "CP|----|X---|ssss|xxXX|",
                                "HH|x--s|x--s|----|----|",
                                "OH|--x-|--x-|----|----|"),
                        },
                        Transition = Bar(
                            "BD|x---|x---|x---|x---|",
                            "SD|----|x---|ssxx|xxXX|",
                            "CP|----|X---|----|--XX|",
                            "HH|x--s|x--s|----|----|",
                            "OH|--x-|--x-|----|----|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|x---|----|x---|",
                                "CP|----|X---|----|X---|",
                                "HH|xs-x|xs-x|xs-x|xs-x|",
                                "OH|--X-|--X-|--X-|--X-|",
                                "SH|x-s-|x-s-|x-s-|x-s-|"),
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|x---|--g-|x-g-|",
                                "CP|----|X---|----|X-X-|",
                                "HH|xs-x|xs-x|xs-x|xs-x|",
                                "OH|--X-|--X-|--X-|--X-|",
                                "SH|x-s-|x-s-|x-s-|x-s-|"),
                        },
                        Fills = new[]
                        {
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|x---|xxxx|----|",
                                "CP|----|X---|----|----|",
                                "T1|----|----|----|xx--|",
                                "T3|----|----|----|--xx|",
                                "HH|xs-x|xs-x|----|----|",
                                "OH|--X-|--X-|----|----|",
                                "SH|x-s-|x-s-|----|----|"),
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|x---|----|xxxx|",
                                "CP|----|X---|----|--XX|",
                                "HH|xs-x|xs-x|xs-x|----|",
                                "OH|--X-|--X-|--X-|----|",
                                "SH|x-s-|x-s-|x-s-|----|"),
                        },
                        Transition = Bar(
                            "BD|x---|x---|x---|x---|",
                            "SD|----|x---|ssxx|xxXX|",
                            "CP|----|X---|----|--XX|",
                            "HH|xs-x|xs-x|----|----|",
                            "OH|--X-|--X-|----|----|",
                            "SH|x-s-|x-s-|----|----|"),
                    },
                },
                Outro = Bar(
                    "BD|x---|x---|x---|x---|",
                    "SD|----|x---|x-x-|XXXX|",
                    "CP|----|X---|----|X---|",
                    "HH|x--s|x--s|----|----|",
                    "OH|--x-|--x-|----|----|"),
            };

            // ───────────────────────── Disco ─────────────────────────
            // Four on the floor under "pea-soup" sixteenth hats, open hat on every off-beat 8th.
            yield return new DrumStyle
            {
                Id = "disco",
                Name = "Disco",
                Genre = "Pop",
                Description = "Four on the floor, pea-soup hats, open off-beats",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 116,
                SuggestedKit = DrumKitKind.Electro,
                Intro = Bar(
                    "BD|x---|x---|x---|x---|",
                    "SD|----|X---|ssss|xxXX|",
                    "HH|xs-s|xs-s|----|----|",
                    "OH|--x-|--x-|----|----|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|X---|----|X---|",
                                "HH|xs-s|xs-s|xs-s|xs-s|",
                                "OH|--X-|--X-|--X-|--X-|"),
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|---g|X--g|--g-|X-g-|",
                                "HH|xs-s|xs-s|xs-s|xs-s|",
                                "OH|--X-|--X-|--X-|--X-|"),
                        },
                        Fills = new[]
                        {
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--x-|",
                                "T3|----|----|----|---X|",
                                "HH|xs-s|xs-s|----|----|",
                                "OH|--X-|--X-|----|----|"),
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|X---|----|xxxx|",
                                "HH|xs-s|xs-s|xs-s|----|",
                                "OH|--X-|--X-|--X-|----|"),
                            Bar("BD|x---|x---|x---|----|",
                                "SD|----|X---|----|----|",
                                "CP|----|----|s-s-|xxXX|",
                                "HH|xs-s|xs-s|----|----|",
                                "OH|--X-|--X-|----|----|"),
                        },
                        Transition = Bar(
                            "BD|x---|x---|x---|x---|",
                            "SD|----|X---|ssxx|xxXX|",
                            "HH|xs-s|xs-s|----|----|",
                            "OH|--X-|--X-|----|----|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|X---|----|X---|",
                                "CP|----|X---|----|X---|",
                                "HH|Xs-s|Xs-s|Xs-s|Xs-s|",
                                "OH|--X-|--X-|--X-|--X-|",
                                "TB|s-x-|s-x-|s-x-|s-x-|"),
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|---g|X--g|--g-|X-g-|",
                                "CP|----|X---|----|X---|",
                                "HH|Xs-s|Xs-s|Xs-s|Xs-s|",
                                "OH|--X-|--X-|--X-|--X-|",
                                "TB|s-x-|s-x-|s-x-|s-x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|X---|xxxx|----|",
                                "CP|----|X---|----|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--x-|",
                                "T3|----|----|----|---X|",
                                "HH|Xs-s|Xs-s|----|----|",
                                "OH|--X-|--X-|----|----|",
                                "TB|s-x-|s-x-|----|----|"),
                            Bar("BD|x---|x---|x---|x---|",
                                "SD|----|X---|----|xxxx|",
                                "CP|----|X---|----|----|",
                                "HH|Xs-s|Xs-s|Xs-s|----|",
                                "OH|--X-|--X-|--X-|----|",
                                "TB|s-x-|s-x-|s-x-|----|"),
                        },
                        Transition = Bar(
                            "BD|x---|x---|x---|x---|",
                            "SD|----|X---|ssxx|xxXX|",
                            "CP|----|X---|----|--XX|",
                            "HH|Xs-s|Xs-s|----|----|",
                            "OH|--X-|--X-|----|----|",
                            "TB|s-x-|s-x-|----|----|"),
                    },
                },
                Outro = Bar(
                    "BD|x---|x---|x---|x---|",
                    "SD|----|X---|X-X-|XXXX|",
                    "HH|xs-s|xs-s|----|----|",
                    "OH|--X-|--X-|----|----|"),
            };

            // ───────────────────────── Motown ─────────────────────────
            // Snare on every beat (accented 2 and 4), tambourine 8ths and a bouncing kick.
            yield return new DrumStyle
            {
                Id = "motown",
                Name = "Motown",
                Genre = "Pop",
                Description = "Snare on every beat, tambourine 8ths, bouncing kick",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 124,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "SD|x---|X---|xxxx|xxXX|",
                    "TB|x-x-|x-x-|----|----|",
                    "BD|x---|--x-|x---|x---|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("TB|X-x-|X-x-|X-x-|X-x-|",
                                "HH|s---|s---|s---|s---|",
                                "SD|x---|X---|x---|X---|",
                                "BD|x--x|--x-|x---|--x-|"),
                            Bar("TB|X-x-|X-x-|X-x-|X-x-|",
                                "HH|s---|s---|s---|s---|",
                                "SD|x---|X---|x---|X--g|",
                                "BD|x---|--x-|x--x|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("TB|X-x-|X-x-|----|----|",
                                "HH|s---|s---|----|----|",
                                "SD|x---|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|x--x|--x-|x---|x---|"),
                            Bar("TB|X-x-|X-x-|X-x-|----|",
                                "HH|s---|s---|s---|----|",
                                "SD|x---|X---|x---|xxxx|",
                                "BD|x--x|--x-|x---|x---|"),
                            Bar("TB|X-x-|X-x-|----|----|",
                                "HH|s---|s---|----|----|",
                                "SD|x---|X---|x---|----|",
                                "T1|----|----|--x-|x---|",
                                "T2|----|----|----|--x-|",
                                "T3|----|----|----|---X|",
                                "BD|x--x|--x-|x---|----|"),
                        },
                        Transition = Bar(
                            "TB|X-x-|X-x-|----|----|",
                            "HH|s---|s---|----|----|",
                            "SD|x---|X---|ssxx|xxXX|",
                            "BD|x--x|--x-|x---|x-x-|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|x-x-|x-x-|x-x-|x-x-|",
                                "TB|X-x-|X-x-|X-x-|X-x-|",
                                "SD|x---|X---|x---|X---|",
                                "BD|x--x|--x-|x--x|--x-|"),
                            Bar("RD|x-x-|x-x-|x-x-|x-x-|",
                                "TB|X-x-|X-x-|X-x-|X-x-|",
                                "SD|x--g|X---|x-g-|X---|",
                                "BD|x--x|--x-|x---|x-x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|x-x-|x-x-|----|----|",
                                "TB|X-x-|X-x-|----|----|",
                                "SD|x---|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T3|----|----|----|--xx|",
                                "BD|x--x|--x-|x---|x---|"),
                            Bar("RD|x-x-|x-x-|x-x-|----|",
                                "TB|X-x-|X-x-|X-x-|----|",
                                "SD|x---|X---|x---|xxxx|",
                                "BD|x--x|--x-|x--x|x---|"),
                        },
                        Transition = Bar(
                            "RD|x-x-|x-x-|----|----|",
                            "TB|X-x-|X-x-|----|----|",
                            "SD|x---|X---|ssxx|xxXX|",
                            "BD|x--x|--x-|x---|x-x-|"),
                    },
                },
                Outro = Bar(
                    "TB|X-x-|X-x-|----|----|",
                    "HH|s---|s---|----|----|",
                    "SD|x---|X---|X-X-|XXXX|",
                    "BD|x--x|--x-|x---|x-x-|"),
            };

            // ───────────────────────── Punk ─────────────────────────
            // Fast 8th hats; the chorus alternates kick and snare every 8th (d-beat feel) on the ride.
            yield return new DrumStyle
            {
                Id = "punk",
                Name = "Punk",
                Genre = "Rock",
                Description = "Fast 8ths, d-beat kick/snare chorus",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 176,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "ST|x---|x---|x---|x---|",
                    "SD|----|----|----|xxxx|",
                    "BD|----|----|x---|x---|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-x-|x-x-|x-x-|x-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x---|--x-|x---|--x-|"),
                            Bar("HH|x-x-|x-x-|x-x-|x-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x-x-|--x-|x---|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-x-|x-x-|----|----|",
                                "SD|----|X---|xxxx|xxxx|",
                                "BD|x---|--x-|x---|x---|"),
                            Bar("HH|x-x-|x-x-|x-x-|----|",
                                "SD|----|X---|x-x-|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|x---|--x-|x---|x---|"),
                        },
                        Transition = Bar(
                            "HH|x-x-|x-x-|----|----|",
                            "SD|----|X---|xxxx|xxXX|",
                            "BD|x---|--x-|x-x-|x-x-|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|X-x-|X-x-|X-x-|X-x-|",
                                "SD|--X-|--X-|--X-|--X-|",
                                "BD|x---|x---|x---|x---|"),
                            Bar("RD|X-x-|X-x-|X-x-|X-x-|",
                                "SD|--X-|--X-|--X-|--X-|",
                                "BD|x---|x---|x---|x--x|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X-x-|X-x-|----|----|",
                                "SD|--X-|--X-|xxxx|xxxx|",
                                "BD|x---|x---|x---|x---|"),
                            Bar("RD|X-x-|X-x-|X-x-|----|",
                                "SD|--X-|--X-|--X-|xxXX|",
                                "BD|x---|x---|x---|x---|"),
                        },
                        Transition = Bar(
                            "RD|X-x-|X-x-|----|----|",
                            "SD|--X-|--X-|xxxx|xxXX|",
                            "BD|x---|x---|x-x-|x-x-|"),
                    },
                },
                Outro = Bar(
                    "RD|X-x-|X-x-|----|----|",
                    "SD|--X-|--X-|xxxx|XXXX|",
                    "BD|x---|x---|x-x-|x-x-|"),
            };

            // ───────────────────────── Metal Double Kick ─────────────────────────
            // Sixteenth-note double kick under a half-time snare; the chorus rides the bell with snare on 2 and 4.
            yield return new DrumStyle
            {
                Id = "metal-double-kick",
                Name = "Metal Double Kick",
                Genre = "Metal",
                Description = "16th double kick, half-time snare, ride bell chorus",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 150,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "HH|x---|x---|----|----|",
                    "T1|----|----|xxxx|----|",
                    "T2|----|----|----|xxxx|",
                    "BD|x-x-|x-x-|xxxx|xxxx|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-x-|x-x-|x-x-|x-x-|",
                                "SD|----|----|X---|----|",
                                "BD|Xxxx|xxxx|Xxxx|xxxx|"),
                            Bar("HH|x-x-|x-x-|x-x-|x-x-|",
                                "SD|----|----|X---|----|",
                                "BD|Xxxx|xxxx|Xxxx|x-xx|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-x-|x-x-|----|----|",
                                "SD|----|----|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|Xxxx|xxxx|xxxx|xxxx|"),
                            Bar("HH|x-x-|x-x-|----|----|",
                                "SD|----|----|xxxx|xxXX|",
                                "BD|Xxxx|xxxx|x-x-|x-x-|"),
                            Bar("HH|x-x-|x-x-|x-x-|----|",
                                "SD|----|----|X---|----|",
                                "T1|----|----|-xx-|----|",
                                "T2|----|----|----|xx--|",
                                "T3|----|----|----|--xX|",
                                "BD|Xxxx|xxxx|x---|x---|"),
                        },
                        Transition = Bar(
                            "HH|x-x-|x-x-|----|----|",
                            "SD|----|----|ssxx|xxXX|",
                            "BD|Xxxx|xxxx|xxxx|xxxx|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RB|X-x-|X-x-|X-x-|X-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|Xxxx|xxxx|Xxxx|xxxx|"),
                            Bar("RB|X-x-|X-x-|X-x-|X-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|Xxxx|xxxx|Xxxx|x-xx|"),
                        },
                        Fills = new[]
                        {
                            Bar("RB|X-x-|X-x-|----|----|",
                                "SD|----|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|Xxxx|xxxx|xxxx|xxxx|"),
                            Bar("RB|X-x-|X-x-|----|----|",
                                "SD|----|X---|xxxx|xxXX|",
                                "BD|Xxxx|xxxx|x-x-|x-x-|"),
                            Bar("RB|X-x-|X-x-|X-x-|----|",
                                "SD|----|X---|----|----|",
                                "T1|----|----|-xx-|----|",
                                "T2|----|----|----|xx--|",
                                "T3|----|----|----|--xX|",
                                "BD|Xxxx|xxxx|x---|x---|"),
                        },
                        Transition = Bar(
                            "RB|X-x-|X-x-|----|----|",
                            "SD|----|X---|ssxx|xxXX|",
                            "BD|Xxxx|xxxx|xxxx|xxxx|"),
                    },
                },
                Outro = Bar(
                    "RB|X-x-|X-x-|----|----|",
                    "SD|----|X---|xxxx|XXXX|",
                    "BD|Xxxx|xxxx|xxxx|xxxx|"),
            };

            // ───────────────────────── Metal Gallop ─────────────────────────
            // Galloping kick (x-xx per beat) with snare on 2 and 4; the chorus rings open hat quarters like a china.
            yield return new DrumStyle
            {
                Id = "metal-gallop",
                Name = "Metal Gallop",
                Genre = "Metal",
                Description = "Galloping kick, backbeat snare, open hat quarters",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 140,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "HH|x-x-|x-x-|----|----|",
                    "SD|----|X---|ssss|xxXX|",
                    "BD|X-xx|x-xx|X-xx|x-xx|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-x-|x-x-|x-x-|x-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|X-xx|x-xx|X-xx|x-xx|"),
                            Bar("HH|x-x-|x-x-|x-x-|x-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|X-xx|x-xx|X-xx|xxxx|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-x-|x-x-|----|----|",
                                "SD|----|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|X-xx|x-xx|X-xx|x-xx|"),
                            Bar("HH|x-x-|x-x-|----|----|",
                                "SD|----|X---|x-xx|x-xx|",
                                "BD|X-xx|x-xx|X---|X---|"),
                        },
                        Transition = Bar(
                            "HH|x-x-|x-x-|----|----|",
                            "SD|----|X---|ssxx|xxXX|",
                            "BD|X-xx|x-xx|X-xx|x-xx|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("OH|X---|X---|X---|X---|",
                                "SD|----|X---|----|X---|",
                                "BD|X-xx|x-xx|X-xx|x-xx|"),
                            Bar("OH|X---|X---|X---|X---|",
                                "SD|----|X---|----|X---|",
                                "BD|X-xx|x-xx|X-xx|xxxx|"),
                        },
                        Fills = new[]
                        {
                            Bar("OH|X---|X---|----|----|",
                                "SD|----|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|X-xx|x-xx|X-xx|x-xx|"),
                            Bar("OH|X---|X---|----|----|",
                                "SD|----|X---|x-xx|x-xx|",
                                "BD|X-xx|x-xx|X---|X---|"),
                        },
                        Transition = Bar(
                            "OH|X---|X---|----|----|",
                            "SD|----|X---|ssxx|xxXX|",
                            "BD|X-xx|x-xx|X-xx|x-xx|"),
                    },
                },
                Outro = Bar(
                    "OH|X---|X---|----|----|",
                    "SD|----|X---|xxxx|XXXX|",
                    "BD|X-xx|x-xx|X-xx|x-xx|"),
            };

            // ───────────────────────── Country Train ─────────────────────────
            // Train beat: constant snare sixteenths, ghost notes with accents on the off-beats, kick on 1 and 3.
            yield return new DrumStyle
            {
                Id = "country-train",
                Name = "Country Train",
                Genre = "Country",
                Description = "Rolling snare sixteenths with off-beat accents, kick on 1 and 3",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 112,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "SD|gXgg|ggXg|xxxx|xxXX|",
                    "HH|x---|x---|----|----|",
                    "BD|x---|----|x---|x---|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("SD|gXgg|ggXg|gXgg|ggXg|",
                                "HH|x---|x---|x---|x---|",
                                "BD|x---|----|x---|----|"),
                            Bar("SD|gXgg|ggXg|gXgg|XgXg|",
                                "HH|x---|x---|x---|x---|",
                                "BD|x---|----|x---|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("SD|gXgg|ggXg|ssss|xxXX|",
                                "HH|x---|x---|----|----|",
                                "BD|x---|----|x---|x---|"),
                            Bar("SD|gXgg|ggXg|gXgg|----|",
                                "HH|x---|x---|x---|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--x-|",
                                "T3|----|----|----|---X|",
                                "BD|x---|----|x---|x---|"),
                        },
                        Transition = Bar(
                            "SD|gXgg|ggXg|ssxx|xxXX|",
                            "HH|x---|x---|----|----|",
                            "BD|x---|----|x---|x---|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("SD|gXgg|XgXg|gXgg|XgXg|",
                                "RD|x-x-|x-x-|x-x-|x-x-|",
                                "BD|x---|--x-|x---|--x-|"),
                            Bar("SD|gXgg|XgXg|gXgg|XgXg|",
                                "RD|x-x-|x-x-|x-x-|x-x-|",
                                "BD|x---|--x-|x---|x-x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("SD|gXgg|XgXg|ssss|xxXX|",
                                "RD|X-x-|X-x-|----|----|",
                                "BD|x---|--x-|x---|x---|"),
                            Bar("SD|gXgg|XgXg|gXgg|----|",
                                "RD|X-x-|X-x-|X-x-|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|x---|--x-|x---|x---|"),
                        },
                        Transition = Bar(
                            "SD|gXgg|XgXg|ssxx|xxXX|",
                            "RD|X-x-|X-x-|----|----|",
                            "BD|x---|--x-|x---|x---|"),
                    },
                },
                Outro = Bar(
                    "SD|gXgg|XgXg|xxxx|XXXX|",
                    "RD|X-x-|X-x-|----|----|",
                    "BD|x---|--x-|x---|x---|"),
            };

            // ───────────────────────── Country Two-Step ─────────────────────────
            // Boom-chick: kick on 1 and 3, side stick on 2 and 4, light straight hats; the chorus moves to full snare.
            yield return new DrumStyle
            {
                Id = "country-two-step",
                Name = "Country Two-Step",
                Genre = "Country",
                Description = "Boom-chick, side stick backbeat, light straight hats",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 104,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "HH|x-s-|x-s-|----|----|",
                    "SS|----|X---|----|----|",
                    "SD|----|----|s-s-|xxXX|",
                    "BD|x---|----|x---|----|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-s-|x-s-|x-s-|x-s-|",
                                "SS|----|X---|----|X---|",
                                "BD|x---|----|x---|----|"),
                            Bar("HH|x-s-|x-s-|x-s-|x-s-|",
                                "SS|----|X---|----|X---|",
                                "BD|x---|----|x-x-|----|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-s-|x-s-|----|----|",
                                "SS|----|X---|----|----|",
                                "SD|----|----|x-x-|xxXX|",
                                "BD|x---|----|x---|x---|"),
                            Bar("HH|x-s-|x-s-|x-s-|----|",
                                "SS|----|X---|----|----|",
                                "T1|----|----|----|x-x-|",
                                "T3|----|----|----|-x-X|",
                                "BD|x---|----|x---|----|"),
                        },
                        Transition = Bar(
                            "HH|x-s-|x-s-|----|----|",
                            "SS|----|X---|----|----|",
                            "SD|----|----|ssxx|xxXX|",
                            "BD|x---|----|x---|x---|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("HH|X-x-|X-x-|X-x-|X-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x---|----|x-x-|----|"),
                            Bar("HH|X-x-|X-x-|X-x-|X-x-|",
                                "SD|----|X---|----|X---|",
                                "BD|x---|--x-|x---|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|X-x-|X-x-|----|----|",
                                "SD|----|X---|xxxx|----|",
                                "T1|----|----|----|xx--|",
                                "T2|----|----|----|--xx|",
                                "BD|x---|----|x---|x---|"),
                            Bar("HH|X-x-|X-x-|X-x-|----|",
                                "SD|----|X---|----|xxXX|",
                                "BD|x---|----|x-x-|----|"),
                        },
                        Transition = Bar(
                            "HH|X-x-|X-x-|----|----|",
                            "SD|----|X---|ssxx|xxXX|",
                            "BD|x---|----|x---|x---|"),
                    },
                },
                Outro = Bar(
                    "HH|X-x-|X-x-|----|----|",
                    "SD|----|X---|X---|XXXX|",
                    "BD|x---|----|x---|x---|"),
            };
        }
    }
}
