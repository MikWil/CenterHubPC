using System.Collections.Generic;
using CenterHubNew.MVVM.Models;

namespace CenterHubNew.MVVM.Services
{
    // Shuffle, swing, funk, hip-hop, Latin, reggae and odd-meter styles.
    public static partial class DrumStyleLibrary
    {
        private static IEnumerable<DrumStyle> GrooveAndWorldStyles()
        {
            // ───────────────────────── Blues Shuffle ─────────────────────────
            // 12 steps = 4 beats x triplets; "x-x" per beat is the long-short shuffle.
            // Verse on shuffled hats, chorus moves to the ride with the foot on 2 and 4.
            yield return new DrumStyle
            {
                Id = "blues-shuffle",
                Name = "Blues Shuffle",
                Genre = "Blues",
                Description = "Shuffled hats, snare on 2 and 4, kick pushing on 3",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 3,
                DefaultBpm = 112,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "SD|x-x|---|sss|xxX|",
                    "BD|x--|---|x--|x--|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-s|x-s|x-s|x-s|",
                                "SD|---|X--|---|X--|",
                                "BD|x--|---|x-x|---|"),
                            Bar("HH|x-s|x-s|x-s|x-s|",
                                "SD|---|X--|---|X--|",
                                "BD|x--|--x|x--|---|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-s|x-s|---|---|",
                                "SD|---|X--|sss|xxX|",
                                "BD|x--|---|x--|x--|"),
                            Bar("HH|x-s|x-s|x-s|---|",
                                "SD|---|X--|x-x|---|",
                                "T2|---|---|---|x-x|",
                                "T3|---|---|---|-X-|",
                                "BD|x--|---|x--|---|"),
                        },
                        Transition = Bar(
                            "HH|x-s|x-s|---|---|",
                            "SD|---|X--|sxx|---|",
                            "T1|---|---|---|xx-|",
                            "T3|---|---|---|--X|",
                            "BD|x--|---|x--|---|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|X-x|x-x|x-x|x-x|",
                                "PH|---|x--|---|x--|",
                                "SD|---|X--|---|X--|",
                                "BD|x--|--x|x--|--x|"),
                            Bar("RD|X-x|x-x|x-x|x-x|",
                                "PH|---|x--|---|x--|",
                                "SD|---|X--|---|X--|",
                                "BD|x-x|---|x--|--x|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X-x|x-x|---|---|",
                                "PH|---|x--|---|---|",
                                "SD|---|X--|x-x|xxX|",
                                "BD|x--|--x|x--|---|"),
                            Bar("RD|X-x|x-x|x-x|---|",
                                "PH|---|x--|---|---|",
                                "SD|---|X--|---|---|",
                                "T1|---|---|---|x--|",
                                "T3|---|---|---|-xX|",
                                "BD|x--|--x|x--|---|"),
                        },
                        Transition = Bar(
                            "RD|X-x|x-x|---|---|",
                            "PH|---|x--|---|---|",
                            "SD|---|X--|sss|---|",
                            "T1|---|---|---|x--|",
                            "T2|---|---|---|-x-|",
                            "T3|---|---|---|--X|",
                            "BD|x--|--x|x--|---|"),
                    },
                },
                Outro = Bar(
                    "HH|x-s|x-s|---|---|",
                    "SD|---|X--|x-X|XXX|",
                    "BD|x--|---|x--|x-x|"),
            };

            // ───────────────────────── Slow Blues 12/8 ─────────────────────────
            // 24 steps = 4 beats x sixteenth-triplets. Steps 0, 2, 4 of each beat are the
            // triplet eighths (the 12/8 pulse); odd steps are pickups. Snare on beats 2 and 4.
            yield return new DrumStyle
            {
                Id = "blues-slow-12-8",
                Name = "Slow Blues 12/8",
                Genre = "Blues",
                Description = "Slow triplet pulse, big backbeat on 2 and 4",
                Signature = "12/8",
                Beats = 4,
                StepsPerBeat = 6,
                DefaultBpm = 58,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "SD|------|------|s-s-s-|xxxxXX|",
                    "BD|x-----|------|x-----|x-----|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-s-s-|x-s-s-|x-s-s-|x-s-s-|",
                                "SD|------|X-----|------|X-----|",
                                "BD|x-----|------|x-----|------|"),
                            Bar("HH|x-s-s-|x-s-s-|x-s-s-|x-s-s-|",
                                "SD|----g-|X-----|------|X----g|",
                                "BD|x-----|------|x-x---|------|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-s-s-|x-s-s-|------|------|",
                                "SD|------|X-----|x-x-x-|xxxxXX|",
                                "BD|x-----|------|x-----|x-----|"),
                            Bar("HH|x-s-s-|x-s-s-|------|------|",
                                "SD|------|X-----|------|------|",
                                "T1|------|------|x-x-x-|------|",
                                "T2|------|------|------|x-x---|",
                                "T3|------|------|------|----X-|",
                                "BD|x-----|------|x-----|x-----|"),
                        },
                        Transition = Bar(
                            "HH|x-s-s-|x-s-s-|------|------|",
                            "SD|------|X-----|ssssxx|------|",
                            "T1|------|------|------|xx----|",
                            "T2|------|------|------|--xx--|",
                            "T3|------|------|------|----XX|",
                            "BD|x-----|------|x-----|------|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|X-x-x-|x-x-x-|X-x-x-|x-x-x-|",
                                "SD|------|X-----|------|X-----|",
                                "BD|x-----|----x-|x-----|------|"),
                            Bar("RD|X-x-x-|x-x-x-|X-x-x-|x-x-x-|",
                                "SD|----g-|X-----|------|X----g|",
                                "BD|x-----|------|x-x---|--x---|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X-x-x-|x-x-x-|------|------|",
                                "SD|------|X-----|x-x-x-|xxxxXX|",
                                "BD|x-----|----x-|x-----|x-----|"),
                            Bar("RD|X-x-x-|x-x-x-|------|------|",
                                "SD|------|X-----|x-x---|------|",
                                "T1|------|------|----x-|x-x---|",
                                "T2|------|------|------|----x-|",
                                "T3|------|------|------|-----X|",
                                "BD|x-----|----x-|x-----|------|"),
                        },
                        Transition = Bar(
                            "RD|X-x-x-|x-x-x-|------|------|",
                            "SD|------|X-----|ssssxx|xxxxXX|",
                            "BD|x-----|----x-|x-----|x-----|"),
                    },
                },
                Outro = Bar(
                    "HH|x-s-s-|x-s-s-|------|------|",
                    "SD|------|X-----|X-X-X-|XXXXXX|",
                    "BD|x-----|------|x-----|x-----|"),
            };

            // ───────────────────────── Texas Shuffle ─────────────────────────
            // Double shuffle: the snare ghosts along with the hats ("g-g") between the
            // accents on 2 and 4, kick four on the floor. Chorus moves to the ride.
            yield return new DrumStyle
            {
                Id = "texas-shuffle",
                Name = "Texas Shuffle",
                Genre = "Blues",
                Description = "Driving double shuffle, ghosted snare, four on the floor",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 3,
                DefaultBpm = 128,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "SD|x--|x--|xxx|xxX|",
                    "BD|x--|x--|x--|x--|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-x|x-x|x-x|x-x|",
                                "SD|g-g|X-g|g-g|X-g|",
                                "BD|x--|x--|x--|x--|"),
                            Bar("HH|x-x|x-x|x-x|x-x|",
                                "SD|g-g|X-g|g-g|X-g|",
                                "BD|x--|x--|x--|x-x|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-x|x-x|---|---|",
                                "SD|g-g|X-g|sss|xxX|",
                                "BD|x--|x--|x--|x--|"),
                            Bar("HH|x-x|x-x|x-x|---|",
                                "SD|g-g|X-g|g-g|---|",
                                "T1|---|---|---|x--|",
                                "T2|---|---|---|-x-|",
                                "T3|---|---|---|--X|",
                                "BD|x--|x--|x--|---|"),
                        },
                        Transition = Bar(
                            "HH|x-x|x-x|---|---|",
                            "SD|g-g|X-g|xxx|XXX|",
                            "BD|x--|x--|x--|x--|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|x-x|x-x|x-x|x-x|",
                                "SD|--g|X--|--g|X--|",
                                "BD|x--|x--|x--|x--|"),
                            Bar("RD|x-x|x-x|x-x|x-x|",
                                "SD|--g|X--|--g|X--|",
                                "BD|x--|x-x|x--|x--|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|x-x|x-x|---|---|",
                                "SD|--g|X--|sss|xxX|",
                                "BD|x--|x--|x--|x--|"),
                            Bar("RD|x-x|x-x|x-x|---|",
                                "SD|--g|X--|--x|---|",
                                "T1|---|---|---|x--|",
                                "T3|---|---|---|-xX|",
                                "BD|x--|x--|x--|---|"),
                        },
                        Transition = Bar(
                            "RD|x-x|x-x|---|---|",
                            "SD|--g|X--|xxx|XXX|",
                            "BD|x--|x--|x--|x--|"),
                    },
                },
                Outro = Bar(
                    "HH|x-x|x-x|---|---|",
                    "SD|g-g|X-g|X-X|XXX|",
                    "BD|x--|x--|x--|x-x|"),
            };

            // ───────────────────────── Funk 16ths ─────────────────────────
            // Syncopated kick, backbeat with ghost notes, 16th hats with an open-hat bark.
            // The chorus opens the hats on the "&" of 2 and 4.
            yield return new DrumStyle
            {
                Id = "funk-16ths",
                Name = "Funk 16ths",
                Genre = "Funk",
                Description = "Syncopated kick, ghosted snare, 16th hats with open-hat barks",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 98,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "SD|x--x|--x-|x-x-|xxXX|",
                    "HH|xsxs|xsxs|----|----|",
                    "BD|x---|----|x---|x---|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|xsxs|xsxs|xsxs|xs-s|",
                                "OH|----|----|----|--x-|",
                                "SD|----|X--g|-g-g|X-g-|",
                                "BD|x--x|--x-|--x-|----|"),
                            Bar("HH|xsxs|xsxs|xsxs|xs-s|",
                                "OH|----|----|----|--x-|",
                                "SD|----|X-gg|-g--|X--g|",
                                "BD|x--x|--x-|--x-|-x--|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|xsxs|xsxs|----|----|",
                                "SD|----|X--g|sxsx|xxXX|",
                                "BD|x--x|--x-|----|----|"),
                            Bar("HH|xsxs|xsxs|----|----|",
                                "SD|----|X--g|--x-|----|",
                                "T1|----|----|x--x|----|",
                                "T2|----|----|----|x---|",
                                "T3|----|----|----|--xX|",
                                "BD|x--x|--x-|----|----|"),
                        },
                        Transition = Bar(
                            "HH|xsxs|xsxs|----|----|",
                            "SD|----|X--g|ssxx|xxXX|",
                            "BD|x--x|--x-|----|----|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("HH|xsxs|xs-s|xsxs|xs-s|",
                                "OH|----|--x-|----|--x-|",
                                "SD|----|X--g|-g-g|X-gx|",
                                "BD|x--x|-x--|--x-|----|"),
                            Bar("HH|xsxs|xs-s|xsxs|xs-s|",
                                "OH|----|--x-|----|--x-|",
                                "SD|----|X-g-|-g-g|X--g|",
                                "BD|x-xx|--x-|-x--|-x--|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|xsxs|xs-s|----|----|",
                                "OH|----|--x-|----|----|",
                                "SD|----|X--g|-sxx|xxXX|",
                                "BD|x--x|-x--|x---|----|"),
                            Bar("HH|xsxs|xs-s|----|----|",
                                "OH|----|--x-|----|----|",
                                "SD|----|X--g|----|----|",
                                "T1|----|----|x-x-|----|",
                                "T3|----|----|----|x-x-|",
                                "T2|----|----|----|-x-X|",
                                "BD|x--x|-x--|----|----|"),
                        },
                        Transition = Bar(
                            "HH|xsxs|xs-s|----|----|",
                            "OH|----|--x-|----|----|",
                            "SD|----|X--g|sxsx|xxXX|",
                            "BD|x--x|-x--|----|----|"),
                    },
                },
                Outro = Bar(
                    "HH|xsxs|xsxs|----|----|",
                    "SD|----|X--g|X-X-|XXXX|",
                    "BD|x--x|--x-|x---|x---|"),
            };

            // ───────────────────────── Linear Funk ─────────────────────────
            // Linear groove: hat, snare and kick never land on the same 16th, so the
            // pattern is a single interlocking line. The chorus opens up to a fat 2 and 4.
            yield return new DrumStyle
            {
                Id = "funk-linear",
                Name = "Linear Funk",
                Genre = "Funk",
                Description = "Interlocking hat, ghost snare and kick, never together",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 92,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "ST|x---|x---|x-x-|xxxx|",
                    "BD|x---|----|x---|x---|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|-xs-|-x-s|x--x|--x-|",
                                "SD|---g|X---|-g--|Xg--|",
                                "BD|x---|--x-|--x-|---x|"),
                            Bar("HH|-xxx|---x|x-x-|-x-x|",
                                "SD|----|Xg--|---g|X---|",
                                "BD|x---|--x-|-x--|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|-xs-|-x-s|----|----|",
                                "SD|---g|X---|-gxx|xxXX|",
                                "BD|x---|--x-|x---|----|"),
                            Bar("HH|-xs-|-x-s|----|----|",
                                "SD|---g|X---|----|----|",
                                "T1|----|----|x-x-|----|",
                                "T2|----|----|----|x-x-|",
                                "T3|----|----|----|-x-X|",
                                "BD|x---|--x-|----|----|"),
                        },
                        Transition = Bar(
                            "HH|-xs-|-x-s|----|----|",
                            "SD|---g|X---|-gxx|----|",
                            "T1|----|----|----|x---|",
                            "T2|----|----|----|-x--|",
                            "T3|----|----|----|--xX|",
                            "BD|x---|--x-|----|----|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("HH|x-x-|x-x-|x-x-|x---|",
                                "OH|----|----|----|--x-|",
                                "SD|----|X--g|-g--|X--g|",
                                "BD|x--x|--x-|x-x-|----|"),
                            Bar("HH|x-x-|x-x-|x-x-|x---|",
                                "OH|----|----|----|--x-|",
                                "SD|----|X--g|-g--|X---|",
                                "BD|x--x|--x-|x--x|-x--|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-x-|x-x-|----|----|",
                                "SD|----|X--g|-gxx|xxXX|",
                                "BD|x--x|--x-|x---|----|"),
                            Bar("HH|x-x-|x-x-|x-x-|----|",
                                "SD|----|X--g|-g--|XxxX|",
                                "BD|x--x|--x-|x-x-|----|"),
                        },
                        Transition = Bar(
                            "HH|x-x-|x-x-|----|----|",
                            "SD|----|X--g|ssxx|xxXX|",
                            "BD|x--x|--x-|----|----|"),
                    },
                },
                Outro = Bar(
                    "HH|-xs-|-x-s|----|----|",
                    "SD|---g|X---|X-X-|XXXX|",
                    "BD|x---|--x-|x---|x---|"),
            };

            // ───────────────────────── Boom Bap ─────────────────────────
            // 24 steps = 4 beats x sixteenth-triplets. Eighths on steps 0 and 3, swung
            // sixteenths on 2 and 5. Lazy kick, snare and clap layered on 2 and 4.
            yield return new DrumStyle
            {
                Id = "hiphop-boom-bap",
                Name = "Boom Bap",
                Genre = "Hip-Hop",
                Description = "Swung 16ths, lazy kick, hard snare and clap on 2 and 4",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 6,
                DefaultBpm = 88,
                SuggestedKit = DrumKitKind.Electro,
                Intro = Bar(
                    "ST|x-----|x-----|x--x--|x-x-x-|",
                    "BD|x-----|------|x-----|x-----|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x--x-s|x-sx--|x--x-s|x-sx--|",
                                "SD|------|X-----|------|X-----|",
                                "CP|------|X-----|------|X-----|",
                                "BD|x-----|---x--|x-----|------|"),
                            Bar("HH|x--x-s|x-sx--|x--x-s|x-sx--|",
                                "SD|------|X----g|------|X--g--|",
                                "CP|------|X-----|------|X-----|",
                                "BD|x--x--|------|x-----|---x--|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x--x-s|x-sx--|------|------|",
                                "SD|------|X-----|x-x-x-|xxxxXX|",
                                "CP|------|X-----|------|------|",
                                "BD|x-----|---x--|x-----|------|"),
                            Bar("HH|x--x-s|x-sx--|------|------|",
                                "SD|------|X-----|------|------|",
                                "CP|------|X-----|------|------|",
                                "T1|------|------|x--x--|------|",
                                "T2|------|------|------|x--x--|",
                                "T3|------|------|------|----xX|",
                                "BD|x-----|---x--|x-----|------|"),
                        },
                        Transition = Bar(
                            "HH|x--x-s|x-sx--|------|------|",
                            "SD|------|X-----|ssssxx|xxxxXX|",
                            "CP|------|X-----|------|------|",
                            "BD|x-----|---x--|x-----|------|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("HH|x--x-s|x--x-s|x--x-s|x-----|",
                                "OH|------|------|------|---x--|",
                                "SD|------|X--g--|------|X----g|",
                                "CP|------|X-----|------|X-----|",
                                "BD|x--x--|--x---|x-----|--x---|"),
                            Bar("HH|x--x-s|x--x-s|x--x-s|x-----|",
                                "OH|------|------|------|---x--|",
                                "SD|------|X----g|-g----|X-----|",
                                "CP|------|X-----|------|X-----|",
                                "BD|x--x--|------|x--x--|--x---|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x--x-s|x--x-s|------|------|",
                                "SD|------|X--g--|x-x-x-|xxxxXX|",
                                "CP|------|X-----|------|------|",
                                "BD|x--x--|--x---|x-----|------|"),
                            Bar("HH|x--x-s|x--x-s|------|------|",
                                "SD|------|X--g--|------|------|",
                                "CP|------|X-----|------|------|",
                                "T1|------|------|x-x---|------|",
                                "T2|------|------|----x-|x-----|",
                                "T3|------|------|------|--x-xX|",
                                "BD|x--x--|--x---|x-----|------|"),
                        },
                        Transition = Bar(
                            "HH|x--x-s|x--x-s|------|------|",
                            "SD|------|X--g--|ssssxx|xxxxXX|",
                            "CP|------|X-----|------|-----X|",
                            "BD|x--x--|--x---|x-----|------|"),
                    },
                },
                Outro = Bar(
                    "HH|x--x-s|x-sx--|------|------|",
                    "SD|------|X-----|X--X--|X-X-XX|",
                    "CP|------|X-----|X-----|------|",
                    "BD|x-----|---x--|x-----|x-----|"),
            };

            // ───────────────────────── Trap ─────────────────────────
            // 24 steps = 4 beats x sixteenth-triplets. Half-time feel: clap and snare on
            // beat 3, a booming sparse kick, 8th hats (steps 0 and 3) and triplet hat rolls.
            yield return new DrumStyle
            {
                Id = "hiphop-trap",
                Name = "Trap",
                Genre = "Hip-Hop",
                Description = "Half-time clap on 3, booming kick, rolling hi-hats",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 6,
                DefaultBpm = 70,
                SuggestedKit = DrumKitKind.Electro,
                Intro = Bar(
                    "ST|x-----|x-----|x--x--|ssxxxX|",
                    "BD|X-----|------|------|------|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x--x--|x--x--|x--x--|x--sxx|",
                                "CP|------|------|X-----|------|",
                                "SD|------|------|X-----|------|",
                                "BD|X-----|------|--x---|------|"),
                            Bar("HH|x--x--|x--sxx|x--x--|x-x-x-|",
                                "CP|------|------|X-----|------|",
                                "SD|------|------|X-----|------|",
                                "BD|X-----|---x--|------|--x---|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x--x--|x--x--|------|------|",
                                "CP|------|------|X-----|------|",
                                "SD|------|------|X-----|sssxxX|",
                                "BD|X-----|------|--x---|------|"),
                            Bar("HH|x--x--|x--x--|------|------|",
                                "CP|------|------|X-----|------|",
                                "SD|------|------|X-----|------|",
                                "T1|------|------|------|x-x---|",
                                "T2|------|------|------|----x-|",
                                "T3|------|------|------|-----X|",
                                "BD|X-----|------|--x---|------|"),
                        },
                        Transition = Bar(
                            "HH|x--x--|x--x--|x--x--|ssxxxX|",
                            "CP|------|------|X-----|------|",
                            "SD|------|------|X-----|------|",
                            "BD|X-----|------|--x---|x-----|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("HH|x--x--|x--xxx|x--x--|x-----|",
                                "OH|------|------|------|---x--|",
                                "CP|------|------|X-----|------|",
                                "SD|------|------|X-----|----g-|",
                                "BD|X-----|---x--|--x---|------|"),
                            Bar("HH|x--x--|x--x--|x--xxx|ssxxxX|",
                                "CP|------|------|X-----|------|",
                                "SD|------|------|X-----|------|",
                                "BD|X--x--|------|--x---|------|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x--x--|x--xxx|------|------|",
                                "CP|------|------|X-----|------|",
                                "SD|------|------|X-----|ssxxxX|",
                                "BD|X-----|---x--|--x---|------|"),
                            Bar("HH|x--x--|x--xxx|------|------|",
                                "CP|------|------|X-----|------|",
                                "SD|------|------|X-----|------|",
                                "T1|------|------|------|x-x-x-|",
                                "T3|------|------|------|-----X|",
                                "BD|X-----|---x--|--x---|------|"),
                        },
                        Transition = Bar(
                            "HH|x--x--|x--xxx|x--x--|ssxxxX|",
                            "CP|------|------|X-----|------|",
                            "SD|------|------|X-----|------|",
                            "BD|X-----|---x--|--x---|x-----|"),
                    },
                },
                Outro = Bar(
                    "HH|x--x--|x--x--|------|------|",
                    "CP|------|------|X-----|------|",
                    "SD|------|------|X-----|ssxxXX|",
                    "BD|X-----|------|--x---|x-----|"),
            };

            // ───────────────────────── Reggae One Drop ─────────────────────────
            // 12 steps = 4 beats x triplets. Nothing on 1; kick and side stick land
            // together on 3 only. The chorus switches to "steppers" (kick on every beat).
            yield return new DrumStyle
            {
                Id = "reggae-one-drop",
                Name = "Reggae One Drop",
                Genre = "Reggae",
                Description = "Kick and rim together on beat 3, nothing on 1",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 3,
                DefaultBpm = 76,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "SS|---|---|x-x|x-x|",
                    "BD|---|---|x--|---|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "One Drop",
                        Main = new[]
                        {
                            Bar("HH|x-s|x-s|x-s|x-s|",
                                "SS|---|---|x--|---|",
                                "BD|---|---|x--|---|"),
                            Bar("HH|x-s|x-s|x-s|x-x|",
                                "SS|---|--g|x--|---|",
                                "BD|---|---|x--|---|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-s|x-s|---|---|",
                                "SS|---|---|x--|---|",
                                "T1|---|---|--x|x--|",
                                "T2|---|---|---|-x-|",
                                "T3|---|---|---|--X|",
                                "BD|---|---|x--|---|"),
                            Bar("HH|x-s|x-s|---|---|",
                                "SS|---|---|x-x|-xx|",
                                "BD|---|---|x--|---|"),
                        },
                        Transition = Bar(
                            "HH|x-s|x-s|---|---|",
                            "SS|---|---|x-x|---|",
                            "SD|---|---|---|sxX|",
                            "BD|---|---|x--|x--|"),
                    },
                    new DrumPart
                    {
                        Name = "Steppers",
                        Main = new[]
                        {
                            Bar("HH|x-s|x-s|x-s|x-s|",
                                "SS|---|---|x--|---|",
                                "BD|x--|x--|x--|x--|"),
                            Bar("HH|x-s|x-s|x-s|x--|",
                                "OH|---|---|---|--x|",
                                "SS|---|---|x--|---|",
                                "BD|x--|x--|x--|x--|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-s|x-s|---|---|",
                                "SS|---|---|x--|---|",
                                "T1|---|---|---|x--|",
                                "T2|---|---|---|-x-|",
                                "T3|---|---|---|--X|",
                                "BD|x--|x--|x--|x--|"),
                            Bar("HH|x-s|x-s|x-s|---|",
                                "SS|---|---|x--|x-x|",
                                "T3|---|---|---|-x-|",
                                "BD|x--|x--|x--|x--|"),
                        },
                        Transition = Bar(
                            "HH|x-s|x-s|---|---|",
                            "SS|---|---|x-x|---|",
                            "SD|---|---|---|sxX|",
                            "BD|x--|x--|x--|x--|"),
                    },
                },
                Outro = Bar(
                    "HH|x-s|x-s|---|---|",
                    "SS|---|---|x--|x-x|",
                    "T3|---|---|---|--X|",
                    "BD|---|---|x--|x--|"),
            };

            // ───────────────────────── Bossa Nova ─────────────────────────
            // 8 steps = 4 beats x eighths. The side stick plays the 2-bar bossa clave
            // (3-2: 1, "&" of 2, 4 | 2, 3), the kick is the surdo figure. Part B moves to the ride.
            yield return new DrumStyle
            {
                Id = "bossa-nova",
                Name = "Bossa Nova",
                Genre = "Latin",
                Description = "Side-stick clave over a steady surdo kick",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 2,
                DefaultBpm = 132,
                SuggestedKit = DrumKitKind.Jazz,
                Intro = Bar(
                    "SS|x-|-x|--|x-|",
                    "SD|--|--|gs|xX|",
                    "BD|x-|-x|x-|--|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "A",
                        Main = new[]
                        {
                            Bar("HH|xs|xs|xs|xs|",
                                "SS|x-|-x|--|x-|",
                                "BD|x-|-x|x-|-x|"),
                            Bar("HH|xs|xs|xs|xs|",
                                "SS|--|x-|-x|--|",
                                "BD|x-|-x|x-|-s|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|xs|xs|--|--|",
                                "SS|x-|-x|--|--|",
                                "SD|--|--|gs|sx|",
                                "BD|x-|-x|x-|-x|"),
                            Bar("HH|xs|xs|--|--|",
                                "SS|--|x-|--|--|",
                                "T1|--|--|x-|--|",
                                "T2|--|--|-x|x-|",
                                "T3|--|--|--|-X|",
                                "BD|x-|-x|x-|--|"),
                        },
                        Transition = Bar(
                            "HH|xs|xs|--|--|",
                            "SS|x-|-x|--|--|",
                            "SD|--|--|sx|xX|",
                            "BD|x-|-x|x-|x-|"),
                    },
                    new DrumPart
                    {
                        Name = "B",
                        Main = new[]
                        {
                            Bar("RD|Xs|xs|xs|xs|",
                                "PH|--|x-|--|x-|",
                                "SS|x-|-x|--|x-|",
                                "BD|x-|-x|x-|-x|"),
                            Bar("RD|Xs|xs|xs|xs|",
                                "PH|--|x-|--|x-|",
                                "SS|--|x-|-x|--|",
                                "BD|x-|-x|x-|-s|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|Xs|xs|--|--|",
                                "PH|--|x-|--|--|",
                                "SS|x-|-x|--|--|",
                                "SD|--|--|gs|sx|",
                                "BD|x-|-x|x-|-x|"),
                            Bar("RD|Xs|xs|--|--|",
                                "PH|--|x-|--|--|",
                                "SS|--|x-|--|--|",
                                "T1|--|--|x-|--|",
                                "T2|--|--|-x|x-|",
                                "T3|--|--|--|-X|",
                                "BD|x-|-x|x-|--|"),
                        },
                        Transition = Bar(
                            "RD|Xs|xs|--|--|",
                            "PH|--|x-|--|--|",
                            "SS|x-|-x|--|--|",
                            "SD|--|--|sx|xX|",
                            "BD|x-|-x|x-|x-|"),
                    },
                },
                Outro = Bar(
                    "HH|xs|xs|--|--|",
                    "SS|x-|-x|--|--|",
                    "SD|--|--|sx|xX|",
                    "BD|x-|-x|x-|x-|"),
            };

            // ───────────────────────── Samba ─────────────────────────
            // 16 steps = 4 beats x sixteenths. Surdo-style kick on every beat, 16th shaker
            // with accents on the "&", side-stick partido-alto (3-3-2) on top. Part B: ride.
            yield return new DrumStyle
            {
                Id = "samba",
                Name = "Samba",
                Genre = "Latin",
                Description = "Surdo kick, shaker 16ths, partido-alto rim line",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 104,
                SuggestedKit = DrumKitKind.Jazz,
                Intro = Bar(
                    "SD|x--x|--x-|x-x-|xxXX|",
                    "BD|x--s|x--s|x---|x---|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "A",
                        Main = new[]
                        {
                            Bar("SH|xsXs|xsXs|xsXs|xsXs|",
                                "SS|x--x|--x-|x--x|--x-|",
                                "BD|x--s|x--s|x--s|x--s|"),
                            Bar("SH|xsXs|xsXs|xsXs|xsXs|",
                                "SS|x--x|--x-|--x-|x--x|",
                                "SD|----|---g|----|---g|",
                                "BD|x--s|x--s|x--s|x--s|"),
                        },
                        Fills = new[]
                        {
                            Bar("SH|xsXs|xsXs|----|----|",
                                "SS|x--x|--x-|----|----|",
                                "SD|----|----|s-sx|xxXX|",
                                "BD|x--s|x--s|x---|x---|"),
                            Bar("SH|xsXs|xsXs|----|----|",
                                "SS|x--x|--x-|----|----|",
                                "T1|----|----|x-x-|----|",
                                "T2|----|----|----|x---|",
                                "T3|----|----|----|--xX|",
                                "BD|x--s|x--s|x---|----|"),
                        },
                        Transition = Bar(
                            "SH|xsXs|xsXs|----|----|",
                            "SS|x--x|--x-|----|----|",
                            "SD|----|----|ssxx|xxXX|",
                            "BD|x--s|x--s|x---|x---|"),
                    },
                    new DrumPart
                    {
                        Name = "B",
                        Main = new[]
                        {
                            Bar("RD|X-xx|x-xx|X-xx|x-xx|",
                                "SS|x--x|--x-|x--x|--x-|",
                                "BD|X--s|x--s|X--s|x--s|"),
                            Bar("RD|X-xx|x-xx|X-xx|x-xx|",
                                "SS|x--x|--x-|--x-|x--x|",
                                "SD|----|---g|----|---g|",
                                "BD|X--s|x--s|X--s|x--s|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X-xx|x-xx|----|----|",
                                "SS|x--x|--x-|----|----|",
                                "SD|----|----|s-sx|xxXX|",
                                "BD|X--s|x--s|x---|x---|"),
                            Bar("RD|X-xx|x-xx|----|----|",
                                "SS|x--x|--x-|----|----|",
                                "T1|----|----|x-x-|----|",
                                "T2|----|----|----|x---|",
                                "T3|----|----|----|--xX|",
                                "BD|X--s|x--s|x---|----|"),
                        },
                        Transition = Bar(
                            "RD|X-xx|x-xx|----|----|",
                            "SS|x--x|--x-|----|----|",
                            "SD|----|----|ssxx|xxXX|",
                            "BD|X--s|x--s|x---|x---|"),
                    },
                },
                Outro = Bar(
                    "SH|xsXs|xsXs|----|----|",
                    "SS|x--x|--x-|----|----|",
                    "SD|----|----|X-X-|XXXX|",
                    "BD|x--s|x--s|x---|x---|"),
            };

            // ───────────────────────── Latin Rock ─────────────────────────
            // Santana-style: cowbell on the quarters, syncopated kick, toms played like
            // congas (high slaps, floor-tom tones), snare on 2 and 4. Chorus: ride + 8th cowbell.
            yield return new DrumStyle
            {
                Id = "latin-songo",
                Name = "Latin Rock",
                Genre = "Latin",
                Description = "Cowbell, syncopated kick and conga-like toms",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 4,
                DefaultBpm = 110,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "CB|x---|x---|x---|x-x-|",
                    "SD|----|----|----|xxXX|",
                    "T3|----|----|x-x-|----|",
                    "BD|x---|----|x---|----|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-x-|x-x-|x-x-|x-x-|",
                                "CB|x---|x---|x---|x---|",
                                "SD|----|X---|----|X--g|",
                                "T1|--s-|----|--s-|----|",
                                "T3|----|-x--|----|-x--|",
                                "BD|x--x|--x-|x--x|--x-|"),
                            Bar("HH|x-x-|x-x-|x-x-|x-x-|",
                                "CB|x---|x---|x---|x-x-|",
                                "SD|----|X---|----|X---|",
                                "T1|--s-|----|--s-|----|",
                                "T3|----|-x--|----|-x-x|",
                                "BD|x--x|--x-|x--x|-x--|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-x-|x-x-|----|----|",
                                "CB|x---|x---|----|----|",
                                "SD|----|X---|----|xxXX|",
                                "T1|----|----|x-x-|----|",
                                "T3|----|----|-x-x|----|",
                                "BD|x--x|--x-|----|----|"),
                            Bar("HH|x-x-|x-x-|----|----|",
                                "CB|x---|x---|----|----|",
                                "SD|----|X---|----|----|",
                                "T1|----|----|x-x-|x-x-|",
                                "T3|----|----|-x-x|-x-X|",
                                "BD|x--x|--x-|----|----|"),
                        },
                        Transition = Bar(
                            "HH|x-x-|x-x-|----|----|",
                            "CB|x---|x---|----|----|",
                            "SD|----|X---|ssxx|xxXX|",
                            "BD|x--x|--x-|x---|x---|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|x-x-|x-x-|x-x-|x-x-|",
                                "CB|x-s-|x-s-|x-s-|x-s-|",
                                "SD|----|X---|----|X--g|",
                                "T1|--x-|----|--x-|----|",
                                "T3|----|-x--|----|-x--|",
                                "BD|x--x|--x-|x--x|--x-|"),
                            Bar("RD|x-x-|x-x-|x-x-|x-x-|",
                                "CB|x-s-|x-s-|x-s-|x-s-|",
                                "SD|----|X--g|----|X---|",
                                "T1|--x-|----|--x-|----|",
                                "T3|----|-x--|----|-x--|",
                                "BD|x--x|--x-|x-x-|-x-x|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|x-x-|x-x-|----|----|",
                                "CB|x-s-|x-s-|----|----|",
                                "SD|----|X---|----|xxXX|",
                                "T1|----|----|x-x-|----|",
                                "T3|----|----|-x-x|----|",
                                "BD|x--x|--x-|----|----|"),
                            Bar("RD|x-x-|x-x-|----|----|",
                                "CB|x-s-|x-s-|----|----|",
                                "SD|----|X---|----|----|",
                                "T1|----|----|x-x-|x-x-|",
                                "T3|----|----|-x-x|-x-X|",
                                "BD|x--x|--x-|----|----|"),
                        },
                        Transition = Bar(
                            "RD|x-x-|x-x-|----|----|",
                            "CB|x-s-|x-s-|----|----|",
                            "SD|----|X---|ssxx|xxXX|",
                            "BD|x--x|--x-|x---|x---|"),
                    },
                },
                Outro = Bar(
                    "HH|x-x-|x-x-|----|----|",
                    "CB|x---|x---|----|----|",
                    "SD|----|X---|X-X-|XXXX|",
                    "BD|x--x|--x-|x---|x---|"),
            };

            // ───────────────────────── Jazz Swing ─────────────────────────
            // 12 steps = 4 beats x triplets. Ride "ding, ding-a-ding", foot hat on 2 and 4,
            // feathered kick on every quarter, light snare comping. Part B comps harder.
            yield return new DrumStyle
            {
                Id = "jazz-swing",
                Name = "Jazz Swing",
                Genre = "Jazz",
                Description = "Ride ding-ding-a-ding, feathered kick, light comping",
                Signature = "4/4",
                Beats = 4,
                StepsPerBeat = 3,
                DefaultBpm = 140,
                SuggestedKit = DrumKitKind.Jazz,
                Intro = Bar(
                    "RD|x--|x-x|---|---|",
                    "SD|---|--g|sss|xxX|",
                    "BD|s--|s--|s--|x--|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "A",
                        Main = new[]
                        {
                            Bar("RD|x--|x-x|x--|x-x|",
                                "PH|---|x--|---|x--|",
                                "SD|---|--g|---|---|",
                                "BD|g--|g--|g--|g--|"),
                            Bar("RD|x--|x-x|x--|x-x|",
                                "PH|---|x--|---|x--|",
                                "SD|---|---|--s|---|",
                                "BD|g--|g--|g--|g--|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|x--|x-x|---|---|",
                                "PH|---|x--|---|---|",
                                "SD|---|--g|ggs|sxx|",
                                "BD|g--|g--|s--|---|"),
                            Bar("RD|x--|x-x|---|---|",
                                "PH|---|x--|---|---|",
                                "SD|---|--g|---|---|",
                                "T1|---|---|x-x|---|",
                                "T2|---|---|---|x--|",
                                "T3|---|---|---|--x|",
                                "BD|g--|g--|---|---|"),
                        },
                        Transition = Bar(
                            "RD|x--|x-x|---|---|",
                            "PH|---|x--|---|---|",
                            "SD|---|--g|sss|xxX|",
                            "BD|g--|g--|s--|x--|"),
                    },
                    new DrumPart
                    {
                        Name = "B",
                        Main = new[]
                        {
                            Bar("RD|X--|x-x|x--|x-x|",
                                "PH|---|x--|---|x--|",
                                "SD|--g|---|--s|---|",
                                "BD|s--|s--|s--|s--|"),
                            Bar("RD|X--|x-x|x--|x-x|",
                                "PH|---|x--|---|x--|",
                                "SD|---|--g|g--|--s|",
                                "BD|s--|s--|s--|s--|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X--|x-x|---|---|",
                                "PH|---|x--|---|---|",
                                "SD|--g|---|gss|xxx|",
                                "BD|s--|s--|s--|---|"),
                            Bar("RD|X--|x-x|---|---|",
                                "PH|---|x--|---|---|",
                                "SD|--g|---|---|---|",
                                "T1|---|---|x-x|---|",
                                "T2|---|---|---|x-x|",
                                "T3|---|---|---|-X-|",
                                "BD|s--|s--|---|---|"),
                        },
                        Transition = Bar(
                            "RD|X--|x-x|---|---|",
                            "PH|---|x--|---|---|",
                            "SD|--g|---|sss|xxX|",
                            "BD|s--|s--|s--|x--|"),
                    },
                },
                Outro = Bar(
                    "RD|x--|x-x|---|---|",
                    "PH|---|x--|---|---|",
                    "SD|---|--g|x-x|XXX|",
                    "BD|g--|g--|x--|X--|"),
            };

            // ───────────────────────── Jazz Waltz ─────────────────────────
            // 9 steps = 3 beats x triplets. Swung ride in 3, foot hat on 2 (and 3),
            // a light kick on 1 and snare comping.
            yield return new DrumStyle
            {
                Id = "jazz-waltz",
                Name = "Jazz Waltz",
                Genre = "Jazz",
                Description = "Swung ride in three, foot hat on 2 and 3",
                Signature = "3/4",
                Beats = 3,
                StepsPerBeat = 3,
                DefaultBpm = 138,
                SuggestedKit = DrumKitKind.Jazz,
                Intro = Bar(
                    "SD|s--|g-s|xxX|",
                    "BD|s--|---|---|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "A",
                        Main = new[]
                        {
                            Bar("RD|x--|x-x|x-x|",
                                "PH|---|x--|x--|",
                                "SD|---|--g|---|",
                                "BD|s--|---|---|"),
                            Bar("RD|x--|x-x|x-x|",
                                "PH|---|x--|x--|",
                                "SD|---|---|--s|",
                                "BD|s--|---|g--|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|x--|---|---|",
                                "SD|---|g-s|xxX|",
                                "BD|s--|---|---|"),
                            Bar("RD|x--|---|---|",
                                "T1|---|x-x|---|",
                                "T2|---|---|x--|",
                                "T3|---|---|--X|",
                                "BD|s--|---|---|"),
                        },
                        Transition = Bar(
                            "RD|x--|---|---|",
                            "SD|---|sss|xxX|",
                            "BD|s--|---|x--|"),
                    },
                    new DrumPart
                    {
                        Name = "B",
                        Main = new[]
                        {
                            Bar("RD|X--|x-x|x-x|",
                                "PH|---|x--|---|",
                                "SD|--g|---|--s|",
                                "BD|s--|---|---|"),
                            Bar("RD|X--|x-x|x-x|",
                                "PH|---|x--|---|",
                                "SD|---|g-s|---|",
                                "BD|s--|---|g--|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X--|x-x|---|",
                                "SD|--g|---|sxX|",
                                "BD|s--|---|---|"),
                            Bar("RD|X--|---|---|",
                                "T1|---|x-x|---|",
                                "T2|---|---|x-x|",
                                "T3|---|---|-X-|",
                                "BD|s--|---|---|"),
                        },
                        Transition = Bar(
                            "RD|X--|---|---|",
                            "SD|--g|sss|xxX|",
                            "BD|s--|---|x--|"),
                    },
                },
                Outro = Bar(
                    "RD|x--|---|---|",
                    "SD|---|x-x|XXX|",
                    "BD|s--|---|X--|"),
            };

            // ───────────────────────── Waltz ─────────────────────────
            // 12 steps = 3 beats x sixteenths. Straight boom-chick-chick: kick on 1,
            // side stick on 2 and 3 over 8th hats. The chorus moves to the ride and snare.
            yield return new DrumStyle
            {
                Id = "waltz",
                Name = "Waltz",
                Genre = "Other",
                Description = "Boom-chick-chick in three, straight eighths",
                Signature = "3/4",
                Beats = 3,
                StepsPerBeat = 4,
                DefaultBpm = 96,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "ST|x---|x---|x-x-|",
                    "BD|x---|----|----|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|X-x-|x-x-|x-x-|",
                                "SS|----|x---|x---|",
                                "BD|x---|----|----|"),
                            Bar("HH|X-x-|x-x-|x-x-|",
                                "SS|----|x---|x--g|",
                                "BD|x---|----|--x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|X-x-|----|----|",
                                "SD|----|g-s-|x-xX|",
                                "BD|x---|----|----|"),
                            Bar("HH|X-x-|----|----|",
                                "T1|----|x-x-|----|",
                                "T2|----|----|x-x-|",
                                "T3|----|----|---X|",
                                "BD|x---|----|----|"),
                        },
                        Transition = Bar(
                            "HH|X-x-|----|----|",
                            "SD|----|ssss|xxXX|",
                            "BD|x---|----|x---|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|X-x-|x-x-|x-x-|",
                                "SD|----|X---|X---|",
                                "BD|x---|----|--x-|"),
                            Bar("RD|X-x-|x-x-|x-x-|",
                                "SD|----|X---|X---|",
                                "BD|x-x-|----|----|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X-x-|----|----|",
                                "SD|----|g-s-|x-xX|",
                                "BD|x---|----|----|"),
                            Bar("RD|X-x-|----|----|",
                                "T1|----|x-x-|----|",
                                "T2|----|----|x-x-|",
                                "T3|----|----|---X|",
                                "BD|x---|----|----|"),
                        },
                        Transition = Bar(
                            "RD|X-x-|----|----|",
                            "SD|----|ssss|xxXX|",
                            "BD|x---|----|x---|"),
                    },
                },
                Outro = Bar(
                    "HH|X-x-|----|----|",
                    "SD|----|x---|X-XX|",
                    "BD|x---|----|x---|"),
            };

            // ───────────────────────── 6/8 Ballad ─────────────────────────
            // 12 steps = 2 beats (dotted quarters) x sixteenth-triplets. Six 8ths per bar are
            // steps 0, 2, 4 of each beat. Kick on 1, snare on the 4th eighth, ghost pickups.
            yield return new DrumStyle
            {
                Id = "ballad-6-8",
                Name = "6/8 Ballad",
                Genre = "Other",
                Description = "Slow six-eight, kick on 1, snare on 4",
                Signature = "6/8",
                Beats = 2,
                StepsPerBeat = 6,
                DefaultBpm = 56,
                SuggestedKit = DrumKitKind.Rock,
                Intro = Bar(
                    "ST|x-x-x-|x-x-x-|",
                    "BD|x-----|------|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("HH|x-s-s-|x-s-s-|",
                                "SD|------|X-----|",
                                "BD|x-----|------|"),
                            Bar("HH|x-s-s-|x-s-s-|",
                                "SD|------|X----g|",
                                "BD|x-----|----x-|"),
                        },
                        Fills = new[]
                        {
                            Bar("HH|x-s-s-|------|",
                                "SD|------|x-x-xx|",
                                "BD|x-----|------|"),
                            Bar("HH|x-s-s-|------|",
                                "T1|------|xx----|",
                                "T2|------|--xx--|",
                                "T3|------|----xX|",
                                "BD|x-----|------|"),
                        },
                        Transition = Bar(
                            "HH|x-s-s-|------|",
                            "SD|------|ssxxXX|",
                            "BD|x-----|------|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|X-x-x-|x-x-x-|",
                                "SD|------|X-----|",
                                "BD|x-----|--x---|"),
                            Bar("RD|X-x-x-|x-x-x-|",
                                "SD|------|X----g|",
                                "BD|x---x-|--x---|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X-x-x-|------|",
                                "SD|------|g-s-xX|",
                                "BD|x-----|------|"),
                            Bar("RD|X-x-x-|------|",
                                "T1|------|x-x---|",
                                "T2|------|----x-|",
                                "T3|------|-----X|",
                                "BD|x-----|------|"),
                        },
                        Transition = Bar(
                            "RD|X-x-x-|------|",
                            "SD|------|ssxxXX|",
                            "BD|x-----|--x---|"),
                    },
                },
                Outro = Bar(
                    "HH|x-s-s-|------|",
                    "SD|------|X-X-XX|",
                    "BD|x-----|x-----|"),
            };

            // ───────────────────────── 5/4 Groove ─────────────────────────
            // 20 steps = 5 beats x sixteenths, felt as 3+2. Ride 8ths, kick on 1 and 4,
            // side stick on 3 and 5 shapes the phrase. The chorus swaps in a fuller snare.
            yield return new DrumStyle
            {
                Id = "odd-5-4",
                Name = "5/4 Groove",
                Genre = "Other",
                Description = "Take-Five style 3+2 feel, ride eighths",
                Signature = "5/4",
                Beats = 5,
                StepsPerBeat = 4,
                DefaultBpm = 126,
                SuggestedKit = DrumKitKind.Jazz,
                Intro = Bar(
                    "ST|x---|x---|x---|x-x-|xxxx|",
                    "BD|x---|----|----|x---|----|"),
                Parts = new[]
                {
                    new DrumPart
                    {
                        Name = "Verse",
                        Main = new[]
                        {
                            Bar("RD|X-x-|x-x-|x-x-|X-x-|x-x-|",
                                "SS|----|----|x---|----|x---|",
                                "BD|x---|----|----|x---|----|"),
                            Bar("RD|X-x-|x-x-|x-x-|X-x-|x-x-|",
                                "SS|----|----|x---|----|x---|",
                                "SD|----|---g|----|----|---g|",
                                "BD|x---|----|----|x-x-|----|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X-x-|x-x-|----|----|----|",
                                "SS|----|----|x---|----|----|",
                                "SD|----|----|----|x-x-|xxXX|",
                                "BD|x---|----|----|x---|----|"),
                            Bar("RD|X-x-|x-x-|----|----|----|",
                                "T1|----|----|x-x-|----|----|",
                                "T2|----|----|----|x---|----|",
                                "T3|----|----|----|--xX|xxXX|",
                                "BD|x---|----|----|----|----|"),
                        },
                        Transition = Bar(
                            "RD|X-x-|x-x-|----|----|----|",
                            "SD|----|----|ssss|xxxx|xxXX|",
                            "BD|x---|----|----|x---|x---|"),
                    },
                    new DrumPart
                    {
                        Name = "Chorus",
                        Main = new[]
                        {
                            Bar("RD|X-x-|x-x-|x-x-|X-x-|x-x-|",
                                "SD|----|--g-|X---|--g-|X-g-|",
                                "BD|x---|----|----|x---|--x-|"),
                            Bar("RD|X-x-|x-x-|x-x-|X-x-|x-x-|",
                                "SD|----|--g-|X---|--g-|X--g|",
                                "BD|x---|----|--x-|x---|----|"),
                        },
                        Fills = new[]
                        {
                            Bar("RD|X-x-|x-x-|----|----|----|",
                                "SD|----|--g-|X---|x-x-|xxXX|",
                                "BD|x---|----|----|x---|----|"),
                            Bar("RD|X-x-|x-x-|----|----|----|",
                                "SD|----|--g-|----|----|----|",
                                "T1|----|----|x---|x---|----|",
                                "T2|----|----|----|-x--|x---|",
                                "T3|----|----|----|----|-xxX|",
                                "BD|x---|----|----|----|----|"),
                        },
                        Transition = Bar(
                            "RD|X-x-|x-x-|----|----|----|",
                            "SD|----|--g-|ssss|xxxx|xxXX|",
                            "BD|x---|----|----|x---|x---|"),
                    },
                },
                Outro = Bar(
                    "RD|X-x-|x-x-|----|----|----|",
                    "SD|----|----|X---|X-X-|XXXX|",
                    "BD|x---|----|x---|x---|x---|"),
            };
        }
    }
}
