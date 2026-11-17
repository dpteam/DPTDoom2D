//
// DPT.DOOM 2D — PK3 (ZIP) mod containers.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Reads a PK3 — a ZIP file used as a mod container.
    ///
    /// Doom engines have used PK3 for decades: it is a plain ZIP, so a mod author
    /// can build one with Explorer or 7-Zip, and the engine walks the archive
    /// instead of a directory. The model here follows DPT.DOOM's:
    ///
    ///   * every <c>*.wad</c> inside is added as a WAD, in path order, so later
    ///     ones override earlier ones exactly like a -file chain;
    ///   * any loose lump at the root replaces the resource of that name, which
    ///     is how a single sprite or a map gets patched without a WAD at all.
    ///
    /// No third-party library: the central directory is parsed by hand and the
    /// entries are inflated with <see cref="DeflateStream"/>, both of which are
    /// in .NET 4.0. (System.IO.Compression.ZipArchive arrived in 4.5, which is
    /// exactly the version the port cannot use.)
    /// </summary>
    internal static class Pk3
    {
        private const uint LocalHeaderSignature = 0x04034b50;
        private const uint CentralHeaderSignature = 0x02014b50;
        private const uint EndOfCentralSignature = 0x06054b50;

        internal sealed class Entry
        {
            public string Name;
            public int CompressedSize;
            public int UncompressedSize;
            public int LocalHeaderOffset;
            public int Method;          // 0 = stored, 8 = deflate
        }

        public static bool LooksLikeZip(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            return data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04;
        }

        /// <summary>
        /// Lists the entries. Returns null when the archive is not readable, so a
        /// corrupt mod costs a log line instead of the game.
        /// </summary>
        public static List<Entry> ReadDirectory(byte[] data)
        {
            if (data == null || data.Length < 22) return null;

            // The end-of-central-directory record is at the tail, after a comment
            // of up to 64 KB; scan backwards for its signature.
            int eocd = -1;
            int lowest = Math.Max(0, data.Length - 22 - 0xFFFF);

            for (int i = data.Length - 22; i >= lowest; --i)
            {
                if (BitConverter.ToUInt32(data, i) == EndOfCentralSignature) { eocd = i; break; }
            }

            if (eocd < 0) return null;

            int count = BitConverter.ToUInt16(data, eocd + 10);
            int directoryOffset = BitConverter.ToInt32(data, eocd + 16);

            if (count <= 0 || count > 65535) return null;
            if (directoryOffset < 0 || directoryOffset + count * 46 > data.Length) return null;

            var entries = new List<Entry>(count);
            int at = directoryOffset;

            for (int i = 0; i < count; ++i)
            {
                if (at + 46 > data.Length) break;
                if (BitConverter.ToUInt32(data, at) != CentralHeaderSignature) break;

                var entry = new Entry();
                entry.Method = BitConverter.ToUInt16(data, at + 10);
                entry.CompressedSize = BitConverter.ToInt32(data, at + 20);
                entry.UncompressedSize = BitConverter.ToInt32(data, at + 24);
                entry.LocalHeaderOffset = BitConverter.ToInt32(data, at + 42);

                int nameLength = BitConverter.ToUInt16(data, at + 28);
                int extraLength = BitConverter.ToUInt16(data, at + 30);
                int commentLength = BitConverter.ToUInt16(data, at + 32);

                if (at + 46 + nameLength > data.Length) break;

                entry.Name = System.Text.Encoding.UTF8.GetString(data, at + 46, nameLength)
                    .Replace('\\', '/');

                entries.Add(entry);
                at += 46 + nameLength + extraLength + commentLength;
            }

            return entries;
        }

        /// <summary>Extracts one entry, or null when the data cannot be read.</summary>
        public static byte[] Read(byte[] data, Entry entry)
        {
            if (data == null || entry == null) return null;

            int at = entry.LocalHeaderOffset;
            if (at < 0 || at + 30 > data.Length) return null;
            if (BitConverter.ToUInt32(data, at) != LocalHeaderSignature) return null;

            int nameLength = BitConverter.ToUInt16(data, at + 26);
            int extraLength = BitConverter.ToUInt16(data, at + 28);
            int start = at + 30 + nameLength + extraLength;

            if (start < 0 || start + entry.CompressedSize > data.Length) return null;

            try
            {
                if (entry.Method == 0)
                {
                    byte[] stored = new byte[entry.CompressedSize];
                    Buffer.BlockCopy(data, start, stored, 0, stored.Length);
                    return stored;
                }

                if (entry.Method == 8)
                {
                    using (var input = new MemoryStream(data, start, entry.CompressedSize))
                    using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream(Math.Max(entry.UncompressedSize, 1024)))
                    {
                        var buffer = new byte[8192];
                        int read;

                        while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                            output.Write(buffer, 0, read);

                        return output.ToArray();
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("pk3: cannot read " + entry.Name + ": " + Log.Describe(e));
            }

            // Methods 1..7 are obsolete compression schemes no archiver emits now.
            return null;
        }
    }
}
