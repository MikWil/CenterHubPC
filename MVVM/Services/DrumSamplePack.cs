using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using CenterHubNew.MVVM.Models;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>One recorded hit: its level relative to the loudest hit of the same drum, and its audio.</summary>
    public sealed class DrumSampleLayer
    {
        public DrumSampleLayer(float loudness, float[] left, float[]? right)
        {
            Loudness = loudness;
            Left = left;
            Right = right;
        }

        /// <summary>Peak level (0..1) after the pack's per-drum normalisation; layers of a drum are sorted by it.</summary>
        public float Loudness { get; }
        public float[] Left { get; }
        /// <summary>Null for a mono hit.</summary>
        public float[]? Right { get; }
        public int Frames => Left.Length;
    }

    /// <summary>
    /// A recorded drum kit: several hits per drum, from soft to loud, so the engine can pick the
    /// one matching the velocity and alternate between neighbours (real drums never sound the same
    /// twice — repeating one sample is what makes a drum machine sound robotic).
    ///
    /// File format (".chdk"), the whole file Deflate-compressed:
    ///   "CHDK" | int32 version (1) | int32 sampleRate | int32 entryCount
    ///   entries: byte voice (DrumVoice) | byte channels (1|2) | float32 loudness | int32 frames
    ///            | frames*channels int16, interleaved, each channel delta-coded (first value raw)
    /// Entries of one voice appear in ascending loudness.
    /// </summary>
    public sealed class DrumSamplePack
    {
        private const string Magic = "CHDK";
        private const int Version = 1;

        private DrumSamplePack(int sampleRate, Dictionary<DrumVoice, IReadOnlyList<DrumSampleLayer>> voices)
        {
            SampleRate = sampleRate;
            Voices = voices;
        }

        public int SampleRate { get; }

        /// <summary>The recorded drums; a voice that is missing here has no recording (callers fall back to synthesis).</summary>
        public IReadOnlyDictionary<DrumVoice, IReadOnlyList<DrumSampleLayer>> Voices { get; }

        /// <summary>Reads a pack embedded in this assembly, e.g. "acoustic" → Assets/Drums/acoustic.chdk. Null if absent.</summary>
        public static DrumSamplePack? LoadEmbedded(string name)
        {
            var asm = typeof(DrumSamplePack).Assembly;
            var resource = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($".{name}.chdk", StringComparison.OrdinalIgnoreCase));
            if (resource is null) return null;
            using var stream = asm.GetManifestResourceStream(resource);
            return stream is null ? null : Read(stream);
        }

        public static DrumSamplePack Read(Stream compressed)
        {
            using var deflate = new DeflateStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            using var r = new BinaryReader(deflate, Encoding.ASCII, leaveOpen: true);

            if (new string(r.ReadChars(4)) != Magic) throw new InvalidDataException("Not a CenterHub drum pack.");
            int version = r.ReadInt32();
            if (version != Version) throw new InvalidDataException($"Unsupported drum pack version {version}.");
            int sampleRate = r.ReadInt32();
            int count = r.ReadInt32();

            var voices = new Dictionary<DrumVoice, List<DrumSampleLayer>>();
            for (int e = 0; e < count; e++)
            {
                var voice = (DrumVoice)r.ReadByte();
                int channels = r.ReadByte();
                float loudness = r.ReadSingle();
                int frames = r.ReadInt32();
                if (channels is < 1 or > 2 || frames < 0) throw new InvalidDataException("Corrupt drum pack entry.");

                var left = new float[frames];
                var right = channels == 2 ? new float[frames] : null;
                short prevL = 0, prevR = 0;
                for (int i = 0; i < frames; i++)
                {
                    prevL = unchecked((short)(prevL + r.ReadInt16()));
                    left[i] = prevL / 32768f;
                    if (right != null)
                    {
                        prevR = unchecked((short)(prevR + r.ReadInt16()));
                        right[i] = prevR / 32768f;
                    }
                }

                if (!voices.TryGetValue(voice, out var list)) voices[voice] = list = new List<DrumSampleLayer>();
                list.Add(new DrumSampleLayer(loudness, left, right));
            }

            return new DrumSamplePack(sampleRate, voices.ToDictionary(
                p => p.Key,
                p => (IReadOnlyList<DrumSampleLayer>)p.Value.OrderBy(l => l.Loudness).ToList()));
        }

        /// <summary>Writes a pack (used by the kit builder in tools/page-render).</summary>
        public static void Write(Stream output, int sampleRate, IEnumerable<(DrumVoice Voice, DrumSampleLayer Layer)> entries)
        {
            var list = entries.ToList();
            using var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
            using var w = new BinaryWriter(deflate, Encoding.ASCII, leaveOpen: true);

            w.Write(Magic.ToCharArray());
            w.Write(Version);
            w.Write(sampleRate);
            w.Write(list.Count);

            static short ToPcm(float v) => (short)Math.Clamp((int)MathF.Round(v * 32767f), short.MinValue, short.MaxValue);

            foreach (var (voice, layer) in list.OrderBy(e => e.Voice).ThenBy(e => e.Layer.Loudness))
            {
                w.Write((byte)voice);
                w.Write((byte)(layer.Right is null ? 1 : 2));
                w.Write(layer.Loudness);
                w.Write(layer.Frames);
                short prevL = 0, prevR = 0;
                for (int i = 0; i < layer.Frames; i++)
                {
                    short l = ToPcm(layer.Left[i]);
                    w.Write(unchecked((short)(l - prevL)));
                    prevL = l;
                    if (layer.Right != null)
                    {
                        short rr = ToPcm(layer.Right[i]);
                        w.Write(unchecked((short)(rr - prevR)));
                        prevR = rr;
                    }
                }
            }
        }
    }
}
