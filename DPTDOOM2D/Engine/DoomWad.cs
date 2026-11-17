//
// DPT.DOOM 2D — a reader for Doom-format WADs, for the FreeDoom converter.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Reads the bits of a Doom WAD that a replacement data set can be built from.
    ///
    /// This is deliberately not <see cref="Wad"/>: that one is the game's own loader,
    /// it assumes Doom2D's lump layout and it is what decides whether the game runs.
    /// A Doom WAD is a different animal — sprites are column patches, walls are
    /// textures composed from patches through TEXTURE1, and music is MUS — so the
    /// converter needs its own reader that never touches the game's state.
    ///
    /// Only the parts a converter needs are here: the directory, PLAYPAL, flats,
    /// patches, textures, sounds and raw lumps. Levels, THINGS, LINEDEFS and the
    /// rest are not read, because Doom2D cannot use a Doom level: it is a different
    /// game with a different map format.
    /// </summary>
    public sealed class DoomWad
    {
        /// <summary>One lump in the directory.</summary>
        public struct Entry
        {
            public string Name;
            public int Offset;
            public int Length;
        }

        private readonly byte[] data;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<string, int> byName =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Lump names are decoded byte for byte, not as ASCII.
        ///
        /// Doom2D's data set names some of its walls in CP866 — a map refers to them by
        /// those bytes — so a reader that maps anything above 127 to '?' turns every
        /// one of them into the same unusable name. Latin-1 is the identity for bytes,
        /// which is what the game's own loader does.
        /// </summary>
        private static readonly System.Text.Encoding Latin1 = System.Text.Encoding.GetEncoding(28591);

        /// <summary>PLAYPAL as 8-bit RGB, three bytes per index.</summary>
        public byte[] Palette { get; private set; }

        public int Count { get { return entries.Count; } }

        public string Path { get; private set; }

        public DoomWad(string path)
        {
            Path = path;
            data = File.ReadAllBytes(path);

            if (data.Length < 12) throw new Fatal(path + ": файл короче заголовка WAD");

            string signature = Latin1.GetString(data, 0, 4);
            if (signature != "IWAD" && signature != "PWAD")
                throw new Fatal(path + ": нет подписи IWAD или PWAD");

            int count = BitConverter.ToInt32(data, 4);
            int directory = BitConverter.ToInt32(data, 8);

            if (count < 0 || directory < 12 || directory + count * 16 > data.Length)
                throw new Fatal(path + ": каталог WAD выходит за конец файла");

            for (int i = 0; i < count; ++i)
            {
                int at = directory + i * 16;

                var entry = new Entry();
                entry.Offset = BitConverter.ToInt32(data, at);
                entry.Length = BitConverter.ToInt32(data, at + 4);
                entry.Name = Latin1.GetString(data, at + 8, 8).TrimEnd('\0');

                if (entry.Offset < 0 || entry.Length < 0 || entry.Offset + entry.Length > data.Length)
                    throw new Fatal(path + ": лампа " + entry.Name + " выходит за конец файла");

                entries.Add(entry);

                // The last entry wins, which is how Doom itself resolves a name that
                // appears twice: a PWAD loaded over an IWAD replaces lumps that way.
                byName[entry.Name] = i;
            }

            LoadPalette();
        }

        private void LoadPalette()
        {
            int at = Find("PLAYPAL");
            if (at < 0) throw new Fatal(Path + ": нет PLAYPAL");

            Entry entry = entries[at];

            // Doom stores 14 palettes of 256 colours; the first one is the one the
            // game starts with. Doom2D stores a single 6-bit palette, so the first
            // 768 bytes are what a converter wants.
            Palette = new byte[768];
            Buffer.BlockCopy(data, entry.Offset, Palette, 0, Math.Min(768, entry.Length));
        }

        /// <summary>The lump's index, or -1. Names compare the way the game's do.</summary>
        public int Find(string name)
        {
            int at;
            return byName.TryGetValue(name ?? "", out at) ? at : -1;
        }

        public bool Has(string name)
        {
            return Find(name) >= 0;
        }

        public string NameOf(int index)
        {
            return index >= 0 && index < entries.Count ? entries[index].Name : "";
        }

        public int LengthOf(int index)
        {
            return index >= 0 && index < entries.Count ? entries[index].Length : 0;
        }

        public List<string> Names()
        {
            var names = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; ++i) names.Add(entries[i].Name);
            return names;
        }

        /// <summary>The raw bytes of one lump.</summary>
        public byte[] Read(int index)
        {
            if (index < 0 || index >= entries.Count) return null;

            Entry entry = entries[index];
            byte[] copy = new byte[entry.Length];
            Buffer.BlockCopy(data, entry.Offset, copy, 0, entry.Length);
            return copy;
        }

        public byte[] Read(string name)
        {
            return Read(Find(name));
        }

        /// <summary>A view of the lump without copying it, for large pictures.</summary>
        public ArraySegment<byte> Segment(int index)
        {
            if (index < 0 || index >= entries.Count) return new ArraySegment<byte>();

            Entry entry = entries[index];
            return new ArraySegment<byte>(data, entry.Offset, entry.Length);
        }

        // ---- flats ---------------------------------------------------------

        /// <summary>
        /// A flat: a square picture with no header, 64x64 in Doom and Doom II.
        ///
        /// Flats are what the converter turns into Doom2D's walls. They are seamless
        /// by definition — a floor tile has to tile — which is exactly the property a
        /// Doom2D wall needs, and the reason a Doom *wall texture* is the wrong
        /// source: those are designed to be seen once, not repeated.
        /// </summary>
        public byte[] ReadFlat(int index, out int size)
        {
            size = 64;

            byte[] raw = Read(index);
            if (raw == null) return null;

            if (raw.Length == 64 * 64) size = 64;
            else if (raw.Length == 128 * 128) size = 128;
            else if (raw.Length == 32 * 32) size = 32;
            else
            {
                // Not a flat: refuse rather than produce noise.
                return null;
            }

            return raw;
        }

        /// <summary>All flat names, from the F_START..F_END (or FF_*) block.</summary>
        public List<string> FlatNames()
        {
            var names = new List<string>();

            int start = Find("F_START");
            int end = Find("F_END");

            if (start < 0)
            {
                start = Find("FF_START");
                end = Find("FF_END");
            }

            if (start < 0 || end <= start) return names;

            for (int i = start + 1; i < end; ++i)
            {
                string name = entries[i].Name;

                // Doom II nests several flat blocks: F1_START, F1_END, F2_START...
                // and the sky marker. None of them is a flat.
                if (name.Length == 0) continue;
                if (name.EndsWith("_START", StringComparison.Ordinal)) continue;
                if (name.EndsWith("_END", StringComparison.Ordinal)) continue;
                if (name == "F_SKY1") continue;

                names.Add(name);
            }

            return names;
        }

        // ---- sounds --------------------------------------------------------

        /// <summary>
        /// A Doom sound: u16 format (3 = PCM), u16 rate, u32 sample count, then
        /// unsigned 8-bit samples.
        ///
        /// Doom2D's DMI is the same thing with a different header and signed samples,
        /// so the conversion is a header rewrite and a byte flip — see
        /// <see cref="DmiSample.FromPcm8"/>. The sample rate is already 11025 in both.
        /// </summary>
        public bool ReadSound(int index, out byte[] pcm, out int rate)
        {
            pcm = null;
            rate = 11025;

            byte[] raw = Read(index);
            if (raw == null || raw.Length < 8) return false;

            int format = BitConverter.ToUInt16(raw, 0);
            rate = BitConverter.ToUInt16(raw, 2);
            int length = BitConverter.ToInt32(raw, 4);

            // Format 0 is Doom's "silence" placeholder: a real lump, no sound.
            if (format != 3) return false;
            if (length <= 0 || 8 + length > raw.Length) return false;

            pcm = new byte[length];
            Buffer.BlockCopy(raw, 8, pcm, 0, length);
            return true;
        }
    }
}
