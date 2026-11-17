//
// DPT.DOOM 2D — the data manifest: what both machines must agree on.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DPTDOOM2D.Game;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// A fingerprint of the game data a session runs on.
    ///
    /// Lockstep only works if both machines simulate the same world, and the world
    /// comes from the WAD, the mods and the actor definitions. A guest with a
    /// different doom2d.wad, a mod the host does not have, or the same mods in a
    /// different order will desync within a few ticks — or, worse, both sides play
    /// different games and it looks like a bug in the port. A changed WAD is also
    /// the simplest way to cheat: more health, more ammo, a faster weapon.
    ///
    /// So the handshake carries this manifest and the host refuses the session
    /// before the first tick, naming what differs.
    ///
    /// The hash is the first 64 bits of SHA-256. That is plenty for catching a
    /// mismatch — the thing this is for — and it keeps the manifest inside one
    /// protocol frame. It is not a security boundary: a player who wants to cheat
    /// badly enough can rebuild their WAD and patch the check out of the binary.
    /// </summary>
    public static class DataManifest
    {
        /// <summary>One file or dataset, as it takes part in the simulation.</summary>
        public sealed class Entry
        {
            public string Name;     // short label: "doom2d.wad", "mod.pk3", "actors"
            public long Size;
            public ulong Hash;

            public string Describe()
            {
                if (Size <= 0) return Name + " (" + Hash.ToString("x16") + ")";
                return Name + " (" + Size + " bytes, " + Hash.ToString("x16") + ")";
            }
        }

        /// <summary>Files in the order they were loaded: the order changes which resource wins.</summary>
        public static readonly List<string> Files = new List<string>();

        /// <summary>Remembers a file that took part in the load, once.</summary>
        public static void AddFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            string full;
            try { full = Path.GetFullPath(path); }
            catch { return; }

            for (int i = 0; i < Files.Count; ++i)
                if (string.Equals(Files[i], full, StringComparison.OrdinalIgnoreCase)) return;

            Files.Add(full);
        }

        /// <summary>The manifest of this installation, in load order.</summary>
        public static List<Entry> Build()
        {
            var entries = new List<Entry>();

            for (int i = 0; i < Files.Count; ++i)
            {
                Entry entry = HashFile(Files[i]);
                if (entry != null) entries.Add(entry);
            }

            // The compiled actor tables, not the text: this is what the simulation
            // actually reads, and it covers built-in data, actors.txt on disk and an
            // ACTORDEF lump inside a mod with one number.
            Entry actors = new Entry();
            actors.Name = "actors";
            actors.Hash = Actors.Digest();
            entries.Add(actors);

            return entries;
        }

        /// <summary>SHA-256 of a file's contents, folded into 64 bits. Null when unreadable.</summary>
        public static Entry HashFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return null;

                using (var sha = SHA256.Create())
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] digest = sha.ComputeHash(stream);

                    var entry = new Entry();
                    entry.Name = Path.GetFileName(path);
                    entry.Size = info.Length;
                    entry.Hash = Fold(digest);
                    return entry;
                }
            }
            catch (Exception e)
            {
                Log.Warn("manifest: cannot hash " + path + " (" + e.Message + ")");
                return null;
            }
        }

        /// <summary>First eight bytes of a digest, big-endian, so it prints as hex.</summary>
        static ulong Fold(byte[] digest)
        {
            ulong value = 0;
            for (int i = 0; i < 8; ++i) value = (value << 8) | digest[i];
            return value;
        }

        /// <summary>
        /// Compares two manifests. Returns an empty string when they agree, or one
        /// sentence per difference.
        ///
        /// The wording names no side — "on one machine only", "A vs B" — because both
        /// ends call this: the host compares the guest's manifest and the guest
        /// compares the host's. Each log also prints its own data line, so the pair of
        /// logs says which side is short a file without the message having to guess.
        /// </summary>
        public static string Compare(List<Entry> mine, List<Entry> theirs)
        {
            if (mine == null || theirs == null) return "";

            var lines = new List<string>();

            // Set difference first: a missing mod is the common case and the one
            // whose message matters most.
            for (int i = 0; i < mine.Count; ++i)
                if (Find(theirs, mine[i].Name) == null)
                    lines.Add(mine[i].Name + " (" + mine[i].Size + " bytes) is on one machine only");

            for (int i = 0; i < theirs.Count; ++i)
                if (Find(mine, theirs[i].Name) == null)
                    lines.Add(theirs[i].Name + " (" + theirs[i].Size + " bytes) is on one machine only");

            // Same files, different order: -file order decides which resource wins,
            // so a swapped pair is a different installation and would desync.
            //
            // Only the shared files are compared, in their own sequence: a missing
            // mod shifts every position, and reporting that shift as "order differs"
            // would bury the real problem.
            var orderHere = new List<string>();
            var orderThere = new List<string>();

            for (int i = 0; i < mine.Count; ++i)
                if (Find(theirs, mine[i].Name) != null) orderHere.Add(mine[i].Name);

            for (int i = 0; i < theirs.Count; ++i)
                if (Find(mine, theirs[i].Name) != null) orderThere.Add(theirs[i].Name);

            for (int i = 0; i < orderHere.Count && i < orderThere.Count; ++i)
            {
                if (string.Equals(orderHere[i], orderThere[i], StringComparison.OrdinalIgnoreCase))
                    continue;

                lines.Add("the load order of " + orderHere[i] + " and " + orderThere[i] + " differs");
                break;
            }

            // Same name, different contents: the file was edited or replaced.
            for (int i = 0; i < mine.Count; ++i)
            {
                Entry other = Find(theirs, mine[i].Name);
                if (other == null || other.Hash == mine[i].Hash) continue;

                if (other.Size != mine[i].Size)
                    lines.Add(mine[i].Name + " differs: " + mine[i].Size
                        + " bytes vs " + other.Size + " bytes");
                else
                    lines.Add(mine[i].Name + " differs: same size, different contents");
            }

            return string.Join("; ", lines.ToArray());
        }

        static Entry Find(List<Entry> entries, string name)
        {
            for (int i = 0; i < entries.Count; ++i)
                if (string.Equals(entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return entries[i];
            return null;
        }

        // ---- wire format -----------------------------------------------------

        /// <summary>
        /// Encodes the manifest: count, then name (12 bytes), size (u32), hash (u64)
        /// per entry. Names are truncated, which is fine — the hash identifies the
        /// file, the name is there so the error message is readable.
        /// </summary>
        public static byte[] Encode(List<Entry> entries)
        {
            if (entries == null) entries = new List<Entry>();

            const int nameLength = 12;
            byte[] buffer = new byte[1 + entries.Count * (nameLength + 4 + 8)];
            buffer[0] = (byte)Math.Min(entries.Count, 255);

            int at = 1;
            for (int i = 0; i < entries.Count && i < 255; ++i)
            {
                Entry entry = entries[i];

                for (int c = 0; c < nameLength; ++c)
                    buffer[at + c] = c < entry.Name.Length ? (byte)entry.Name[c] : (byte)0;

                Net.NetProtocol.PutU32(buffer, at + nameLength, (uint)entry.Size);
                PutU64(buffer, at + nameLength + 4, entry.Hash);
                at += nameLength + 4 + 8;
            }

            return buffer;
        }

        public static List<Entry> Decode(byte[] buffer, int offset)
        {
            var entries = new List<Entry>();
            if (buffer == null || offset >= buffer.Length) return entries;

            const int nameLength = 12;
            const int stride = nameLength + 4 + 8;

            int count = buffer[offset];
            int at = offset + 1;

            for (int i = 0; i < count && at + stride <= buffer.Length; ++i)
            {
                var entry = new Entry();

                int end = at;
                while (end < at + nameLength && buffer[end] != 0) ++end;
                entry.Name = Encoding.ASCII.GetString(buffer, at, end - at);

                entry.Size = Net.NetProtocol.GetU32(buffer, at + nameLength);
                entry.Hash = GetU64(buffer, at + nameLength + 4);
                entries.Add(entry);

                at += stride;
            }

            return entries;
        }

        static void PutU64(byte[] buffer, int at, ulong value)
        {
            for (int i = 0; i < 8; ++i)
                buffer[at + i] = (byte)((value >> (i * 8)) & 0xFF);
        }

        static ulong GetU64(byte[] buffer, int at)
        {
            ulong value = 0;
            for (int i = 0; i < 8; ++i) value |= (ulong)buffer[at + i] << (i * 8);
            return value;
        }

        /// <summary>One line per file, for the log: what this machine is running on.</summary>
        public static string Describe(List<Entry> entries)
        {
            if (entries == null || entries.Count == 0) return "(no data files)";

            var text = new StringBuilder();
            for (int i = 0; i < entries.Count; ++i)
            {
                if (i > 0) text.Append(", ");
                text.Append(entries[i].Describe());
            }
            return text.ToString();
        }

        /// <summary>A single number for the tick checksum, so mid-game tampering shows up.</summary>
        public static uint Digest(List<Entry> entries)
        {
            uint hash = 2166136261u;

            for (int i = 0; i < entries.Count; ++i)
            {
                Entry entry = entries[i];
                hash = Net.NetProtocol.Mix(hash, (uint)(entry.Hash & 0xFFFFFFFF));
                hash = Net.NetProtocol.Mix(hash, (uint)(entry.Hash >> 32));
                hash = Net.NetProtocol.Mix(hash, (uint)entry.Size);
            }

            return hash;
        }
    }
}
