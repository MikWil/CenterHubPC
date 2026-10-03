# Drum samples

`acoustic.chdk` — the Metronome's **Real drums** kit — is built from **The Open Source Drumkit**
by Real Music Media.

- Source: https://github.com/crabacus/the-open-source-drumkit (mirror of the original release)
- Licence: public domain. The author's statement, from the release announcement
  (https://www.kvraudio.com/forum/viewtopic.php?p=4426883): "The samples and the mappings are
  completely in the public domain." No attribution is required; we credit it anyway.

## What is in the pack

107 hits picked evenly across each drum's loudness range from the original 96 kHz / 24-bit
recordings, resampled to 44.1 kHz / 16-bit stereo, long cymbal tails shortened with a fade, and
each drum normalised so its loudest hit matches the synthesized kits' level:

| Drum | Source recordings | Hits |
|---|---|---|
| Kick | `kick/kick*` | 12 |
| Snare | `snare/snare-top*` | 16 |
| Side stick | `sidestick/sidestick*` | 8 |
| Closed / pedal / open hi-hat | `hihat/closed-hihat`, `foot-hihat`, `half-open-hihat` | 14 / 6 / 10 |
| Ride / ride bell | `ride/ride-mid-in*`, `ride/ride-bell*` | 6 / 5 |
| Crash | `crash/crash*` | 6 |
| High / mid / floor tom | `toms/small-tom*`, `medium-tom*`, `large-tom*` | 8 / 8 / 8 |

Clap, cowbell, tambourine, shaker and count-in sticks are not in the recording and stay synthesized.

## Rebuilding

Download the source folders listed above, then:

```
dotnet run --project tools/page-render -- <any dir> build-drumkit <source folder> Assets/Drums/acoustic.chdk
```

The format is described in `MVVM/Services/DrumSamplePack.cs`.
