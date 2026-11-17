//
// DPT.DOOM 2D — prototype: the WAD maker.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Prototypes
{
    /// <summary>
    /// DOOM WAD MAKER — builds a WAD from a list of files, ported from
    /// oldprog/TC/PROGS/DOOMWAD/MAKEWAD.C (Aleksey Volynskov, 1995).
    ///
    /// The original read a .LST file:
    ///
    ///     output.wad
    ///     IWAD
    ///     PLAYPAL
    ///     COLORMAP
    ///     MAP01
    ///
    /// and wrote each named file into the archive under its own name, which is
    /// exactly how Doom2D's WADs are laid out. That makes this tool the one
    /// genuinely useful prototype in the collection: it is how a modder assembles
    /// the WAD that goes into a PK3.
    ///
    /// A second form takes the names from the command line, for one-off builds.
    /// </summary>
    internal static class WadBuilder
    {
        /// <summary>--wadbuild &lt;list.lst&gt;: the original's interface.</summary>
        public static int BuildFromList(string listPath)
        {
            if (!File.Exists(listPath))
            {
                Console.WriteLine("wadbuild: " + listPath + " not found");
                return 1;
            }

            string[] lines;

            try
            {
                lines = File.ReadAllLines(listPath);
            }
            catch (Exception e)
            {
                Console.WriteLine("wadbuild: cannot read the list: " + Log.Describe(e));
                return 1;
            }

            if (lines.Length < 2)
            {
                Console.WriteLine("wadbuild: the list needs an output name and IWAD/PWAD");
                return 1;
            }

            string output = lines[0].Trim();
            string kind = lines[1].Trim().ToUpperInvariant();

            if (kind.Length == 0) kind = "PWAD";
            if (kind[0] == 'I') kind = "IWAD";
            else if (kind[0] == 'P') kind = "PWAD";
            else
            {
                Console.WriteLine("wadbuild: the second line must be IWAD or PWAD");
                return 1;
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(listPath));
            var entries = new List<KeyValuePair<string, string>>();

            for (int i = 2; i < lines.Length; ++i)
            {
                string name = lines[i].Trim();
                if (name.Length == 0) break;

                if (name.Length > 8)
                {
                    Console.WriteLine("wadbuild: lump name '" + name + "' is longer than 8 characters");
                    return 1;
                }

                string file = Path.Combine(directory, name);
                entries.Add(new KeyValuePair<string, string>(name, file));
            }

            if (!Path.IsPathRooted(output)) output = Path.Combine(directory, output);

            return Write(output, kind, entries);
        }

        /// <summary>--wadbuild &lt;out.wad&gt; &lt;file&gt;...: names taken from the files.</summary>
        public static int BuildFromFiles(string output, string[] files)
        {
            var entries = new List<KeyValuePair<string, string>>();

            for (int i = 0; i < files.Length; ++i)
            {
                string name = Path.GetFileNameWithoutExtension(files[i]).ToUpperInvariant();
                if (name.Length > 8) name = name.Substring(0, 8);

                entries.Add(new KeyValuePair<string, string>(name, files[i]));
            }

            return Write(output, "PWAD", entries);
        }

        /// <summary>
        /// Writes a PWAD from lumps already in memory.
        ///
        /// The editor needs this: it builds a map in memory and saves it as a WAD
        /// without going through files on disk. Same directory layout as the file
        /// form, so a WAD made here is indistinguishable from one made by
        /// --wadbuild.
        /// </summary>
        public static bool WriteLumps(string output, List<KeyValuePair<string, byte[]>> lumps)
        {
            return WriteLumps(output, lumps, "PWAD");
        }

        /// <summary>
        /// The same, with a chosen signature: a complete data set is an IWAD, a mod or
        /// a single map is a PWAD. The game accepts both, but a replacement set that
        /// calls itself IWAD is what a player expects to find.
        /// </summary>
        public static bool WriteLumps(string output, List<KeyValuePair<string, byte[]>> lumps, string signature)
        {
            var offsets = new List<int>();
            var names = new List<string>();

            try
            {
                using (var stream = File.Create(output))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Encoding.ASCII.GetBytes(signature.PadRight(4)));
                    writer.Write(0);              // entry count, patched below
                    writer.Write(0);              // directory offset, patched below

                    for (int i = 0; i < lumps.Count; ++i)
                    {
                        string name = LumpName(lumps[i].Key);
                        if (name.Length > 8) name = name.Substring(0, 8);

                        byte[] data = lumps[i].Value ?? new byte[0];

                        offsets.Add((int)stream.Position);
                        names.Add(name);

                        writer.Write(data);
                    }

                    int directoryOffset = (int)stream.Position;

                    for (int i = 0; i < names.Count; ++i)
                    {
                        writer.Write(offsets[i]);
                        writer.Write(lumps[i].Value == null ? 0 : lumps[i].Value.Length);

                        byte[] lump = new byte[8];
                        byte[] text = Latin1.GetBytes(names[i]);
                        Buffer.BlockCopy(text, 0, lump, 0, Math.Min(8, text.Length));
                        writer.Write(lump);
                    }

                    stream.Position = 4;
                    writer.Write(names.Count);
                    writer.Write(directoryOffset);
                    writer.Flush();
                }

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("wadwrite: " + Log.Describe(e));
                return false;
            }
        }

        /// <summary>
        /// Lump names are bytes, not text.
        ///
        /// Doom2D's own data set names some of its walls in CP866, and a map refers to
        /// them by those bytes. Encoding a name as ASCII turns every byte above 127
        /// into '?', which silently merges those walls into one unusable lump; Latin-1
        /// is the identity for bytes, and it is what the game's loader uses.
        /// </summary>
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        /// <summary>Uppercases a lump name without touching the bytes above 127.</summary>
        private static string LumpName(string name)
        {
            if (name == null) return "";

            char[] chars = name.ToCharArray();

            for (int i = 0; i < chars.Length; ++i)
                if (chars[i] >= 'a' && chars[i] <= 'z') chars[i] = (char)(chars[i] - 32);

            string result = new string(chars);

            return result.Length > 8 ? result.Substring(0, 8) : result;
        }

        private static int Write(string output, string kind, List<KeyValuePair<string, string>> entries)
        {            var offsets = new List<int>();
            var lengths = new List<int>();
            var names = new List<string>();

            try
            {
                using (var stream = File.Create(output))
                using (var writer = new BinaryWriter(stream))
                {
                    byte[] signature = Encoding.ASCII.GetBytes(kind.PadRight(4));
                    writer.Write(signature);
                    writer.Write(0);              // entry count, patched below
                    writer.Write(0);              // directory offset, patched below

                    for (int i = 0; i < entries.Count; ++i)
                    {
                        string path = entries[i].Value;

                        if (!File.Exists(path))
                        {
                            // The original skipped missing files and left a
                            // zero-length lump in their place; do the same, but say so.
                            Console.WriteLine("wadbuild: missing " + path + " (empty lump)");
                            offsets.Add((int)stream.Position);
                            lengths.Add(0);
                            names.Add(entries[i].Key);
                            continue;
                        }

                        byte[] data = File.ReadAllBytes(path);

                        offsets.Add((int)stream.Position);
                        lengths.Add(data.Length);
                        names.Add(entries[i].Key);

                        writer.Write(data);
                    }

                    int directoryOffset = (int)stream.Position;

                    for (int i = 0; i < names.Count; ++i)
                    {
                        writer.Write(offsets[i]);
                        writer.Write(lengths[i]);

                        byte[] lump = new byte[8];
                        byte[] text = Latin1.GetBytes(names[i]);
                        Buffer.BlockCopy(text, 0, lump, 0, Math.Min(8, text.Length));
                        writer.Write(lump);
                    }

                    stream.Position = 4;
                    writer.Write(names.Count);
                    writer.Write(directoryOffset);
                    writer.Flush();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("wadbuild: " + Log.Describe(e));
                return 1;
            }

            Console.WriteLine(string.Format("wadbuild: {0} ({1}, {2} lumps, {3} KB)",
                Path.GetFileName(output), kind, names.Count,
                Math.Max(1, new FileInfo(output).Length / 1024)));

            for (int i = 0; i < names.Count; ++i)
                Console.WriteLine(string.Format("  {0,-8} {1,8} B", names[i], lengths[i]));

            return 0;
        }

        /// <summary>--wadlist &lt;file.wad&gt;: the directory, without loading the game.</summary>
        public static int List(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("wadlist: " + path + " not found");
                return 1;
            }

            byte[] data = File.ReadAllBytes(path);

            if (data.Length < 12)
            {
                Console.WriteLine("wadlist: " + path + " is too short to be a WAD");
                return 1;
            }

            string signature = Encoding.ASCII.GetString(data, 0, 4);

            if (signature != "IWAD" && signature != "PWAD")
            {
                Console.WriteLine("wadlist: " + path + " has no IWAD/PWAD signature");
                return 1;
            }

            int count = BitConverter.ToInt32(data, 4);
            int directory = BitConverter.ToInt32(data, 8);

            Console.WriteLine(path + " (" + signature + ", " + count + " lumps)");

            for (int i = 0; i < count; ++i)
            {
                int at = directory + i * 16;
                if (at + 16 > data.Length) break;

                int offset = BitConverter.ToInt32(data, at);
                int length = BitConverter.ToInt32(data, at + 4);
                string name = Encoding.ASCII.GetString(data, at + 8, 8).TrimEnd('\0');

                Console.WriteLine(string.Format("  {0,5} {1,-8} {2,8} B", i, name, length));
            }

            return 0;
        }
    }
}
