//
// DPT.DOOM 2D — mod kit.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// A tiny, dependency-free mod kit: build a PK3, list what is inside one, and
    /// unpack it again.
    ///
    /// A Doom2D mod is a ZIP with WADs and loose lumps in it, so building one is
    /// "collect the files, write a ZIP". That does not need a mod IDE — it needs a
    /// command that does not require the author to learn the archive layout, and a
    /// listing command so a broken mod can be inspected without 7-Zip.
    ///
    /// The writer emits **stored** entries on purpose. A Doom2D mod is small (a map
    /// is a few KB, a texture a few hundred bytes), so the size cost is trivial,
    /// and a stored archive is readable by every tool including the Windows XP
    /// shell — no version of Deflate to get wrong.
    /// </summary>
    internal static class ModKit
    {
        private const uint LocalHeaderSignature = 0x04034b50;
        private const uint CentralHeaderSignature = 0x02014b50;
        private const uint EndOfCentralSignature = 0x06054b50;

        /// <summary>One file inside a mod, with the path it gets in the archive.</summary>
        internal sealed class FileEntry
        {
            public string ArchivePath;
            public byte[] Data;
        }

        /// <summary>
        /// --modbuild &lt;out.pk3&gt; &lt;dir-or-file&gt;...
        ///
        /// A directory is walked recursively; the archive path is the path
        /// relative to that directory, so "sprites/PLAYA0.lmp" stays where the mod
        /// author put it. A bare file lands at the archive root.
        /// </summary>
        public static int Build(string output, string[] inputs)
        {
            if (inputs == null || inputs.Length == 0)
            {
                Console.WriteLine("usage: --modbuild <out.pk3> <dir-or-file>...");
                return 1;
            }

            var files = new List<FileEntry>();

            for (int i = 0; i < inputs.Length; ++i)
            {
                string input = inputs[i];

                if (Directory.Exists(input))
                {
                    Collect(files, input, input);
                }
                else if (File.Exists(input))
                {
                    files.Add(Read(input, Path.GetFileName(input)));
                }
                else
                {
                    Console.WriteLine("modbuild: " + input + " not found");
                    return 1;
                }
            }

            if (files.Count == 0)
            {
                Console.WriteLine("modbuild: nothing to pack");
                return 1;
            }

            try
            {
                Write(output, files);
            }
            catch (Exception e)
            {
                Console.WriteLine("modbuild: " + Log.Describe(e));
                return 1;
            }

            long size = new FileInfo(output).Length;
            Console.WriteLine(string.Format("modbuild: {0} ({1} files, {2} KB)",
                Path.GetFileName(output), files.Count, Math.Max(1, size / 1024)));

            for (int i = 0; i < files.Count; ++i)
                Console.WriteLine("  " + files[i].ArchivePath + "  " + files[i].Data.Length + " B");

            return 0;
        }

        private static void Collect(List<FileEntry> files, string root, string directory)
        {
            string[] entries;

            try
            {
                entries = Directory.GetFiles(directory);
            }
            catch (Exception e)
            {
                Log.Warn("modbuild: cannot list " + directory + ": " + Log.Describe(e));
                return;
            }

            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < entries.Length; ++i)
            {
                string relative = entries[i].Substring(root.Length).TrimStart('\\', '/');
                files.Add(Read(entries[i], relative.Replace('\\', '/')));
            }

            string[] directories;

            try
            {
                directories = Directory.GetDirectories(directory);
            }
            catch
            {
                return;
            }

            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < directories.Length; ++i)
                Collect(files, root, directories[i]);
        }

        private static FileEntry Read(string path, string archivePath)
        {
            var entry = new FileEntry();
            entry.ArchivePath = archivePath;
            entry.Data = File.ReadAllBytes(path);
            return entry;
        }

        /// <summary>
        /// --modlist &lt;file.pk3&gt;: what a mod actually contains.
        ///
        /// Doom2D only understands a few file types, so the listing says which
        /// entries the game will load and which it will ignore — the usual reason
        /// a mod "does nothing" is that its files are named or placed wrong.
        /// </summary>
        public static int List(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("modlist: " + path + " not found");
                return 1;
            }

            byte[] data = File.ReadAllBytes(path);

            if (!Pk3.LooksLikeZip(data))
            {
                Console.WriteLine("modlist: " + path + " is not a ZIP archive");
                return 1;
            }

            List<Pk3.Entry> entries = Pk3.ReadDirectory(data);

            if (entries == null)
            {
                Console.WriteLine("modlist: " + path + " has no readable directory");
                return 1;
            }

            int usable = 0, ignored = 0;

            Console.WriteLine(path + " (" + entries.Count + " entries)");

            for (int i = 0; i < entries.Count; ++i)
            {
                Pk3.Entry entry = entries[i];
                if (entry.Name.EndsWith("/", StringComparison.Ordinal)) continue;

                string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                bool known = ext == ".wad" || ext == ".lmp" || ext == ".vgaimg"
                    || ext == ".dmi" || ext == ".dmm";

                if (known) ++usable; else ++ignored;

                Console.WriteLine(string.Format("  {0,-40} {1,8} B  {2}",
                    entry.Name, entry.UncompressedSize, known ? "load" : "ignore"));
            }

            Console.WriteLine(usable + " file(s) the game will load, " + ignored + " ignored");

            return usable > 0 ? 0 : 1;
        }

        /// <summary>--modunpack &lt;file.pk3&gt; &lt;dir&gt;: explode a mod back into files.</summary>
        public static int Unpack(string path, string output)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("modunpack: " + path + " not found");
                return 1;
            }

            byte[] data = File.ReadAllBytes(path);
            List<Pk3.Entry> entries = Pk3.ReadDirectory(data);

            if (entries == null)
            {
                Console.WriteLine("modunpack: " + path + " has no readable directory");
                return 1;
            }

            int written = 0;

            for (int i = 0; i < entries.Count; ++i)
            {
                Pk3.Entry entry = entries[i];
                if (entry.Name.EndsWith("/", StringComparison.Ordinal)) continue;

                byte[] payload = Pk3.Read(data, entry);
                if (payload == null) continue;

                string target = Path.Combine(output, entry.Name.Replace('/', Path.DirectorySeparatorChar));

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, payload);
                    ++written;
                }
                catch (Exception e)
                {
                    Console.WriteLine("modunpack: " + entry.Name + ": " + Log.Describe(e));
                }
            }

            Console.WriteLine("modunpack: " + written + " file(s) -> " + output);
            return written > 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- writer

        /// <summary>
        /// Writes a stored (uncompressed) ZIP. Every header is built by hand
        /// because System.IO.Compression.ZipArchive does not exist in .NET 4.0.
        /// </summary>
        private static void Write(string path, List<FileEntry> files)
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                var offsets = new int[files.Count];
                var crc = new uint[files.Count];

                for (int i = 0; i < files.Count; ++i)
                {
                    FileEntry file = files[i];
                    offsets[i] = (int)stream.Position;
                    crc[i] = Crc32(file.Data, file.Data.Length);

                    byte[] name = System.Text.Encoding.UTF8.GetBytes(file.ArchivePath);

                    writer.Write(LocalHeaderSignature);
                    writer.Write((ushort)20);            // version needed
                    writer.Write((ushort)0x0800);        // UTF-8 names
                    writer.Write((ushort)0);             // stored
                    writer.Write((ushort)0);             // time
                    writer.Write((ushort)0);             // date
                    writer.Write(crc[i]);
                    writer.Write(file.Data.Length);      // compressed
                    writer.Write(file.Data.Length);      // uncompressed
                    writer.Write((ushort)name.Length);
                    writer.Write((ushort)0);             // extra
                    writer.Write(name);
                    writer.Write(file.Data);
                }

                int directoryOffset = (int)stream.Position;

                for (int i = 0; i < files.Count; ++i)
                {
                    FileEntry file = files[i];
                    byte[] name = System.Text.Encoding.UTF8.GetBytes(file.ArchivePath);

                    writer.Write(CentralHeaderSignature);
                    writer.Write((ushort)20);            // version made by
                    writer.Write((ushort)20);            // version needed
                    writer.Write((ushort)0x0800);        // UTF-8 names
                    writer.Write((ushort)0);             // stored
                    writer.Write((ushort)0);             // time
                    writer.Write((ushort)0);             // date
                    writer.Write(crc[i]);
                    writer.Write(file.Data.Length);
                    writer.Write(file.Data.Length);
                    writer.Write((ushort)name.Length);
                    writer.Write((ushort)0);             // extra
                    writer.Write((ushort)0);             // comment
                    writer.Write((ushort)0);             // disk
                    writer.Write((ushort)0);             // internal attrs
                    writer.Write(0);                     // external attrs
                    writer.Write(offsets[i]);
                    writer.Write(name);
                }

                int directorySize = (int)stream.Position - directoryOffset;

                writer.Write(EndOfCentralSignature);
                writer.Write((ushort)0);                 // this disk
                writer.Write((ushort)0);                 // disk with directory
                writer.Write((ushort)files.Count);
                writer.Write((ushort)files.Count);
                writer.Write(directorySize);
                writer.Write(directoryOffset);
                writer.Write((ushort)0);                 // comment length

                writer.Flush();
            }
        }

        /// <summary>CRC-32 as used by ZIP. The table is built once, on first use.</summary>
        private static uint[] crcTable;

        private static uint Crc32(byte[] data, int length)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];

                for (uint i = 0; i < 256; ++i)
                {
                    uint value = i;

                    for (int bit = 0; bit < 8; ++bit)
                        value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;

                    crcTable[i] = value;
                }
            }

            uint crc = 0xFFFFFFFFu;

            for (int i = 0; i < length; ++i)
                crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
