//
// DPT.DOOM 2D — the map file: reading and writing.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.Text;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Game
{
    /// <summary>
    /// The map format's block ids. They live next to the reader and writer rather
    /// than in World.cs, because the format is not the world: the editor needs the
    /// same numbers without pulling in the renderer.
    /// </summary>
    public static class MapBlockType
    {
        public const int Comment = -1;
        public const int End = 0;
        public const int WallNames = 1;
        public const int Back = 2;
        public const int WType = 3;
        public const int Front = 4;
        public const int Thing = 5;
        public const int Switch = 6;
        public const int Music = 7;
        public const int Sky = 8;
        public const int Switch2 = 9;
    }

    /// <summary>
    /// The map format's thing numbers. They live next to the reader and writer for
    /// the same reason as <see cref="MapBlockType"/>: a tool that only edits maps
    /// should not have to compile the item simulation to know what a number means.
    /// </summary>
    public static class ThingType
    {
        public const int None = 0;
        public const int Plr1 = 1, Plr2 = 2, DmStart = 3;

        public const int Clip = 100;
        // Clip..Gun2 are consecutive, matching ItemType + 99.
        public const int LastItem = 135;

        public const int Demon = 200;
        public const int LastMonster = 220;

        public const int Dir = 1;
        public const int Dm = 16;
    }

    /// <summary>One thing on the map: an item, a monster or a spawn point.</summary>
    public sealed class MapThing
    {
        public short X, Y;
        public short Type;
        public ushort Flags;

        public MapThing Clone()
        {
            var copy = new MapThing();
            copy.X = X; copy.Y = Y; copy.Type = Type; copy.Flags = Flags;
            return copy;
        }
    }

    /// <summary>One trigger: a door, a lift, a teleport.</summary>
    public sealed class MapSwitch
    {
        public byte X, Y;
        public byte Type;
        public byte Timer;
        public byte A, B;
        public byte Param;
        public byte Flags;

        public MapSwitch Clone()
        {
            var copy = new MapSwitch();
            copy.X = X; copy.Y = Y; copy.Type = Type; copy.Timer = Timer;
            copy.A = A; copy.B = B; copy.Param = Param; copy.Flags = Flags;
            return copy;
        }
    }

    /// <summary>
    /// A map as a file, independent of the running game.
    ///
    /// The game has only ever read this format (Game.LoadMap fills World, Items and
    /// Switches in place). An editor has to write it, and a round trip has to be
    /// verifiable, so the format gets its own model: read a map into it, edit, write
    /// it back. The block layout is the one in docs/FORMATS.md, and the writer emits
    /// the same block order the original DOS editor did (FILES.C: F_savemap).
    /// </summary>
    public sealed class MapFile
    {
        public const int Width = 100;
        public const int Height = 100;
        public const int Version = 2;

        /// <summary>Wall names, 1-based like the game's WalP: index 0 is "no wall".</summary>
        public readonly string[] WallName = new string[256];

        /// <summary>wall_t.t: 1 = a switch that can be pressed.</summary>
        public readonly byte[] WallType = new byte[256];

        public readonly byte[] Back = new byte[Width * Height];
        public readonly byte[] Type = new byte[Width * Height];
        public readonly byte[] Front = new byte[Width * Height];

        public readonly List<MapThing> Things = new List<MapThing>();
        public readonly List<MapSwitch> Switches = new List<MapSwitch>();

        /// <summary>Music lump name, at most 8 characters. Empty means "keep playing".</summary>
        public string Music = "";

        /// <summary>1 = normal sky, 2 = thunderstorm, 3 = hell.</summary>
        public int Sky = 1;

        /// <summary>Where this map came from, for messages: "MAP01", "mypack.wad:MAP03".</summary>
        public string Source = "";

        public MapFile()
        {
            for (int i = 0; i < WallName.Length; ++i) WallName[i] = "";
        }

        // ------------------------------------------------------------------ read

        /// <summary>
        /// Parses a map lump. Throws <see cref="Fatal"/> on a broken file, because a
        /// map the editor cannot understand must not be half-loaded and then saved
        /// over the original.
        /// </summary>
        public static MapFile Read(byte[] data, string source)
        {
            if (data == null || data.Length < 10)
                throw new Fatal(source + ": файл карты короче заголовка");

            string id = Encoding.GetEncoding(28591).GetString(data, 0, 6);
            if (id != "Doom2D" || data[6] != 0x1A)
                throw new Fatal(source + ": это не карта Doom2D");

            int version = BitConverter.ToInt16(data, 8);
            if (version > Version)
                throw new Fatal(source + ": версия карты " + version + ", поддерживается до " + Version);

            var map = new MapFile();
            map.Source = source;

            int pos = 10;
            while (pos + 8 <= data.Length)
            {
                int type = BitConverter.ToInt16(data, pos);
                int sub = BitConverter.ToInt16(data, pos + 2);
                int size = BitConverter.ToInt32(data, pos + 4);
                pos += 8;

                if (type == MapBlockType.End) break;

                if (size < 0 || pos + size > data.Length)
                    throw new Fatal(source + ": блок " + type + " выходит за конец файла");

                if (type == MapBlockType.Comment) { pos += size; continue; }

                map.LoadBlock(type, sub, data, pos, size, source);
                pos += size;
            }

            return map;
        }

        private void LoadBlock(int type, int sub, byte[] data, int pos, int size, string source)
        {
            switch (type)
            {
                case MapBlockType.WallNames:
                    {
                        int end = pos + size;
                        for (int i = 1; i < 256 && pos + 9 <= end; ++i, pos += 9)
                        {
                            WallName[i] = ReadName(data, pos);
                            WallType[i] = data[pos + 8];
                        }

                        return;
                    }

                case MapBlockType.Back:
                    Unpack(data, pos, size, sub, Back, source, "задний слой");
                    return;

                case MapBlockType.WType:
                    Unpack(data, pos, size, sub, Type, source, "типы клеток");
                    return;

                case MapBlockType.Front:
                    Unpack(data, pos, size, sub, Front, source, "передний слой");
                    return;

                case MapBlockType.Thing:
                    {
                        for (int at = 0; at + 8 <= size; at += 8)
                        {
                            var thing = new MapThing();
                            thing.X = BitConverter.ToInt16(data, pos + at);
                            thing.Y = BitConverter.ToInt16(data, pos + at + 2);
                            thing.Type = BitConverter.ToInt16(data, pos + at + 4);
                            thing.Flags = BitConverter.ToUInt16(data, pos + at + 6);

                            // A zero type is the terminator in the original tables.
                            if (thing.Type == 0) break;
                            Things.Add(thing);
                        }

                        return;
                    }

                case MapBlockType.Switch2:
                    {
                        for (int at = 0; at + 9 <= size; at += 9)
                        {
                            var sw = new MapSwitch();
                            sw.X = data[pos + at];
                            sw.Y = data[pos + at + 1];
                            sw.Type = data[pos + at + 2];
                            sw.Timer = data[pos + at + 3];
                            sw.A = data[pos + at + 4];
                            sw.B = data[pos + at + 5];
                            sw.Param = data[pos + at + 6];
                            sw.Flags = data[pos + at + 8];

                            if (sw.Type == 0) break;
                            Switches.Add(sw);
                        }

                        return;
                    }

                case MapBlockType.Music:
                    Music = ReadName(data, pos);
                    return;

                case MapBlockType.Sky:
                    if (size >= 2) Sky = BitConverter.ToInt16(data, pos);
                    return;

                // Unknown blocks are skipped by the caller: a newer map may carry
                // data this build does not know, and refusing to open it would be
                // worse than opening it without that data.
            }
        }

        private static string ReadName(byte[] data, int pos)
        {
            int n = 0;
            while (n < 8 && pos + n < data.Length && data[pos + n] != 0) ++n;
            return Encoding.GetEncoding(28591).GetString(data, pos, n);
        }

        private static void Unpack(byte[] data, int pos, int size, int sub, byte[] dst,
            string source, string what)
        {
            if (sub == 0)
            {
                if (size < dst.Length)
                    throw new Fatal(source + ": " + what + " короче " + dst.Length + " байт");

                Buffer.BlockCopy(data, pos, dst, 0, dst.Length);
                return;
            }

            if (sub != 1)
                throw new Fatal(source + ": " + what + " сжаты способом " + sub + ", такого нет");

            int at = pos;
            int end = pos + size;
            int outAt = 0;

            while (at < end && outAt < dst.Length)
            {
                byte b = data[at++];

                if (b == 255)
                {
                    if (at + 3 > end) throw new Fatal(source + ": " + what + ": обрыв RLE");
                    int count = BitConverter.ToUInt16(data, at);
                    at += 2;
                    byte value = data[at++];

                    for (int i = 0; i < count && outAt < dst.Length; ++i) dst[outAt++] = value;
                }
                else dst[outAt++] = b;
            }
        }

        // ----------------------------------------------------------------- write

        /// <summary>
        /// Serialises the map in the original block order: music, sky, walls, three
        /// layers, things, switches, end.
        /// </summary>
        public byte[] Write()
        {
            var output = new List<byte>(Width * Height * 3 / 2 + 4096);

            byte[] magic = Encoding.GetEncoding(28591).GetBytes("Doom2D");
            output.AddRange(magic);
            output.Add(0x1A);
            output.Add(0x00);                       // header padding: 8 bytes total
            AddInt16(output, Version);

            WriteMusic(output);
            WriteSky(output);
            WriteWalls(output);
            WriteLayer(output, MapBlockType.Back, Back);
            WriteLayer(output, MapBlockType.WType, Type);
            WriteLayer(output, MapBlockType.Front, Front);
            WriteThings(output);
            WriteSwitches(output);

            AddInt16(output, MapBlockType.End);
            AddInt16(output, 0);
            AddInt32(output, 0);

            return output.ToArray();
        }

        private void WriteMusic(List<byte> output)
        {
            Block(output, MapBlockType.Music, 0, delegate (List<byte> b) { AddName(b, Music); });
        }

        private void WriteSky(List<byte> output)
        {
            Block(output, MapBlockType.Sky, 0, delegate (List<byte> b) { AddInt16(b, Sky); });
        }

        private void WriteWalls(List<byte> output)
        {
            // Index 0 is "empty", so the file carries 255 entries starting at 1.
            // Trailing blanks are dropped: the loader stops at the block end, and a
            // map with four walls should not ship 251 empty names.
            int last = 255;
            while (last > 1 && (WallName[last] == null || WallName[last].Length == 0)) --last;

            Block(output, MapBlockType.WallNames, 0, delegate (List<byte> b)
            {
                for (int i = 1; i <= last; ++i)
                {
                    AddName(b, WallName[i]);
                    b.Add(WallType[i]);
                }
            });
        }

        private void WriteLayer(List<byte> output, int type, byte[] layer)
        {
            Block(output, type, 1, delegate (List<byte> b) { Pack(b, layer); });
        }

        private void WriteThings(List<byte> output)
        {
            if (Things.Count == 0) return;

            Block(output, MapBlockType.Thing, 0, delegate (List<byte> b)
            {
                for (int i = 0; i < Things.Count; ++i)
                {
                    MapThing thing = Things[i];
                    AddInt16(b, thing.X);
                    AddInt16(b, thing.Y);
                    AddInt16(b, thing.Type);
                    AddUInt16(b, thing.Flags);
                }
            });
        }

        private void WriteSwitches(List<byte> output)
        {
            if (Switches.Count == 0) return;

            Block(output, MapBlockType.Switch2, 0, delegate (List<byte> b)
            {
                for (int i = 0; i < Switches.Count; ++i)
                {
                    MapSwitch sw = Switches[i];
                    b.Add(sw.X);
                    b.Add(sw.Y);
                    b.Add(sw.Type);
                    b.Add(sw.Timer);
                    b.Add(sw.A);
                    b.Add(sw.B);
                    b.Add(sw.Param);
                    b.Add(0);              // d: unused, written as the original did
                    b.Add(sw.Flags);
                }
            });
        }

        // ------------------------------------------------------------------ rle

        /// <summary>
        /// The original's pack(): a run of five or more identical bytes becomes
        /// 0xFF, a 16-bit count and the value; shorter runs stay literal. The
        /// threshold matters — a run of four is five bytes packed, so compressing it
        /// would grow the file.
        /// </summary>
        public static void Pack(List<byte> output, byte[] data)
        {
            int at = 0;

            while (at < data.Length)
            {
                byte value = data[at];
                int end = at + 1;
                while (end < data.Length && data[end] == value) ++end;

                int run = end - at;

                if (run > 4)
                {
                    output.Add(255);
                    output.Add((byte)(run & 0xFF));
                    output.Add((byte)((run >> 8) & 0xFF));
                    output.Add(value);
                }
                else
                {
                    for (int i = 0; i < run; ++i) output.Add(value);
                }

                at = end;
            }
        }

        // --------------------------------------------------------------- blocks

        private static void BeginBlock(List<byte> output, int type, int sub)
        {
            AddInt16(output, type);
            AddInt16(output, sub);
            AddInt32(output, 0);            // size, patched by EndBlock
        }

        /// <summary>
        /// Patches a block's size. The caller passes the index of the size field —
        /// the last four bytes of the header — because searching backwards for a zero
        /// size would match a payload byte that happens to be zero.
        /// </summary>
        private static void EndBlock(List<byte> output, int sizeAt)
        {
            int size = output.Count - (sizeAt + 4);

            output[sizeAt] = (byte)(size & 0xFF);
            output[sizeAt + 1] = (byte)((size >> 8) & 0xFF);
            output[sizeAt + 2] = (byte)((size >> 16) & 0xFF);
            output[sizeAt + 3] = (byte)((size >> 24) & 0xFF);
        }

        /// <summary>
        /// Writes one block with a payload built by <paramref name="body"/>.
        ///
        /// The size field is the last four bytes of the eight-byte header, so it
        /// starts at Count-4 right after <see cref="BeginBlock"/>.
        /// </summary>
        private static void Block(List<byte> output, int type, int sub, Action<List<byte>> body)
        {
            BeginBlock(output, type, sub);
            int sizeAt = output.Count - 4;
            body(output);
            EndBlock(output, sizeAt);
        }

        private static void AddInt16(List<byte> output, int value)
        {
            output.Add((byte)(value & 0xFF));
            output.Add((byte)((value >> 8) & 0xFF));
        }

        private static void AddUInt16(List<byte> output, int value)
        {
            AddInt16(output, value);
        }

        private static void AddInt32(List<byte> output, int value)
        {
            output.Add((byte)(value & 0xFF));
            output.Add((byte)((value >> 8) & 0xFF));
            output.Add((byte)((value >> 16) & 0xFF));
            output.Add((byte)((value >> 24) & 0xFF));
        }

        private static void AddName(List<byte> output, string name)
        {
            for (int i = 0; i < 8; ++i)
                output.Add(i < name.Length ? (byte)name[i] : (byte)0);
        }

        // ------------------------------------------------------------ utilities

        /// <summary>An empty map with a floor and a border, the way a new level starts.</summary>
        public static MapFile CreateEmpty()
        {
            var map = new MapFile();
            map.WallName[1] = "WALL01_1";
            map.WallType[1] = 0;

            // A one-cell stone border so the player cannot walk off the map.
            for (int x = 0; x < Width; ++x)
            {
                map.Type[x] = 1;
                map.Type[(Height - 1) * Width + x] = 1;
            }

            for (int y = 0; y < Height; ++y)
            {
                map.Type[y * Width] = 1;
                map.Type[y * Width + Width - 1] = 1;
            }

            var start = new MapThing();
            start.X = (short)(Width / 2 * 16);
            start.Y = (short)(Height / 2 * 16);
            start.Type = ThingType.Plr1;
            map.Things.Add(start);

            return map;
        }

        /// <summary>Counts of what is on the map, for the editor's status line.</summary>
        public string Summary()
        {
            return Things.Count + " предметов, " + Switches.Count + " триггеров, "
                + SkyName() + ", музыка " + (Music.Length == 0 ? "—" : Music);
        }

        public string SkyName()
        {
            switch (Sky)
            {
                case 2: return "гроза";
                case 3: return "ад";
                default: return "обычное небо";
            }
        }
    }
}
