//
// Copyright (C) 2026 DartPower Team
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DPTDOOM2D.Shared
{
    /// <summary>
    /// A fingerprint of the game data a session runs on, shared by the game, the
    /// master and the launcher.
    ///
    /// Lockstep needs both machines to simulate the same world, and the world comes
    /// from the WAD, the mods and the actor tables. A guest with a different
    /// doom2d.wad, a mod the host does not have, or the same mods in another order
    /// desyncs within a few ticks — or both sides play different games and it looks
    /// like a bug in the port. A changed WAD is also the cheapest cheat there is.
    ///
    /// The hash is the first 64 bits of SHA-256: plenty to catch a mismatch, small
    /// enough to keep a manifest inside one frame. It is not a security boundary —
    /// a player who wants to cheat badly enough can rebuild the binary and drop the
    /// check. The job is to stop an accidental mismatch and to say what differs.
    /// </summary>
    public static class DataManifest
    {
        /// <summary>Longest manifest the master will store or hand out.</summary>
        public const int MaxSize = 1024;

        /// <summary>One file or dataset, as it takes part in the simulation.</summary>
        public sealed class Entry
        {
            /// <summary>Short label: "doom2d.wad", "mod.pk3", "actors".</summary>
            public string Name = "";

            public long Size;
            public ulong Hash;

            public string Describe()
            {
                if (Size <= 0)
                {
                    return Name + " (" + Hash.ToString("x16") + ")";
                }

                return Name + " (" + Size + " bytes, " + Hash.ToString("x16") + ")";
            }
        }

        /// <summary>SHA-256 of a file's contents, folded into 64 bits. Null when unreadable.</summary>
        public static Entry HashFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return null;
                }

                using (var sha = SHA256.Create())
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var digest = sha.ComputeHash(stream);

                    var entry = new Entry();
                    entry.Name = Path.GetFileName(path);
                    entry.Size = info.Length;
                    entry.Hash = Fold(digest);
                    return entry;
                }
            }
            catch
            {
                // A file that cannot be read cannot be part of the session either;
                // the caller notices it is absent from the manifest.
                return null;
            }
        }

        /// <summary>An entry for something that is not a file, such as the actor tables.</summary>
        public static Entry Inline(string name, uint hash)
        {
            var entry = new Entry();
            entry.Name = name;
            entry.Size = 0;
            entry.Hash = hash;
            return entry;
        }

        /// <summary>First eight bytes of a digest, big-endian, so it prints as hex.</summary>
        private static ulong Fold(byte[] digest)
        {
            var value = 0UL;
            for (var i = 0; i < 8; i++)
            {
                value = (value << 8) | digest[i];
            }

            return value;
        }

        // ---- wire format -----------------------------------------------------

        /// <summary>
        /// Count, then name (12 bytes), size (u32), hash (u64) per entry. Names are
        /// truncated: the hash identifies the file, the name is there so the error
        /// message is readable.
        /// </summary>
        public static byte[] Encode(List<Entry> entries)
        {
            if (entries == null)
            {
                entries = new List<Entry>();
            }

            const int nameLength = 12;
            const int stride = nameLength + 4 + 8;

            var count = Math.Min(entries.Count, 255);
            var buffer = new byte[1 + count * stride];
            buffer[0] = (byte)count;

            var at = 1;
            for (var i = 0; i < count; i++)
            {
                var entry = entries[i];

                for (var c = 0; c < nameLength; c++)
                {
                    buffer[at + c] = c < entry.Name.Length ? (byte)entry.Name[c] : (byte)0;
                }

                MasterProtocol.Put32(buffer, at + nameLength, (uint)entry.Size);
                PutU64(buffer, at + nameLength + 4, entry.Hash);
                at += stride;
            }

            return buffer;
        }

        public static List<Entry> Decode(byte[] buffer, int offset)
        {
            var entries = new List<Entry>();
            if (buffer == null || offset >= buffer.Length)
            {
                return entries;
            }

            const int nameLength = 12;
            const int stride = nameLength + 4 + 8;

            int count = buffer[offset];
            var at = offset + 1;

            for (var i = 0; i < count && at + stride <= buffer.Length; i++)
            {
                var entry = new Entry();

                var end = at;
                while (end < at + nameLength && buffer[end] != 0)
                {
                    end++;
                }

                entry.Name = Encoding.ASCII.GetString(buffer, at, end - at);
                entry.Size = MasterProtocol.Get32(buffer, at + nameLength);
                entry.Hash = GetU64(buffer, at + nameLength + 4);
                entries.Add(entry);

                at += stride;
            }

            return entries;
        }

        private static void PutU64(byte[] buffer, int at, ulong value)
        {
            for (var i = 0; i < 8; i++)
            {
                buffer[at + i] = (byte)((value >> (i * 8)) & 0xFF);
            }
        }

        private static ulong GetU64(byte[] buffer, int at)
        {
            var value = 0UL;
            for (var i = 0; i < 8; i++)
            {
                value |= (ulong)buffer[at + i] << (i * 8);
            }

            return value;
        }

        // ---- comparison ------------------------------------------------------

        /// <summary>
        /// Compares two manifests. Returns an empty string when they agree, or one
        /// sentence per difference.
        ///
        /// The wording names no side — "on one machine only", "A vs B" — because both
        /// ends call this: the host compares the guest's manifest, the guest compares
        /// the host's, and the launcher compares a session's manifest with its own
        /// installation.
        /// </summary>
        public static string Compare(List<Entry> mine, List<Entry> theirs)
        {
            if (mine == null || theirs == null)
            {
                return "";
            }

            var lines = new List<string>();

            // Set difference first: a missing mod is the common case and the one
            // whose message matters most.
            for (var i = 0; i < mine.Count; i++)
            {
                if (Find(theirs, mine[i].Name) == null)
                {
                    lines.Add(mine[i].Name + " (" + mine[i].Size + " bytes) is on one machine only");
                }
            }

            for (var i = 0; i < theirs.Count; i++)
            {
                if (Find(mine, theirs[i].Name) == null)
                {
                    lines.Add(theirs[i].Name + " (" + theirs[i].Size + " bytes) is on one machine only");
                }
            }

            // Same files, different order: the load order decides which resource
            // wins, so a swapped pair is a different installation and would desync.
            //
            // Only the shared files are compared, in their own sequence: a missing
            // mod shifts every position, and reporting that shift as "order differs"
            // would bury the real problem.
            var orderHere = new List<string>();
            var orderThere = new List<string>();

            for (var i = 0; i < mine.Count; i++)
            {
                if (Find(theirs, mine[i].Name) != null)
                {
                    orderHere.Add(mine[i].Name);
                }
            }

            for (var i = 0; i < theirs.Count; i++)
            {
                if (Find(mine, theirs[i].Name) != null)
                {
                    orderThere.Add(theirs[i].Name);
                }
            }

            for (var i = 0; i < orderHere.Count && i < orderThere.Count; i++)
            {
                if (string.Equals(orderHere[i], orderThere[i], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                lines.Add("the load order of " + orderHere[i] + " and " + orderThere[i] + " differs");
                break;
            }

            // Same name, different contents: the file was edited or replaced.
            for (var i = 0; i < mine.Count; i++)
            {
                var other = Find(theirs, mine[i].Name);
                if (other == null || other.Hash == mine[i].Hash)
                {
                    continue;
                }

                if (other.Size != mine[i].Size)
                {
                    lines.Add(mine[i].Name + " differs: " + mine[i].Size + " bytes vs " + other.Size + " bytes");
                }
                else
                {
                    lines.Add(mine[i].Name + " differs: same size, different contents");
                }
            }

            return string.Join("; ", lines.ToArray());
        }

        /// <summary>True when every file the session needs is present here with the same hash.</summary>
        public static bool IsSatisfiedBy(List<Entry> session, List<Entry> local)
        {
            if (session == null)
            {
                return true;
            }

            for (var i = 0; i < session.Count; i++)
            {
                var mine = Find(local, session[i].Name);
                if (mine == null || mine.Hash != session[i].Hash)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The files a session needs that this installation does not have.</summary>
        public static List<Entry> Missing(List<Entry> session, List<Entry> local)
        {
            var missing = new List<Entry>();
            if (session == null)
            {
                return missing;
            }

            for (var i = 0; i < session.Count; i++)
            {
                var mine = Find(local, session[i].Name);
                if (mine == null || mine.Hash != session[i].Hash)
                {
                    missing.Add(session[i]);
                }
            }

            return missing;
        }

        public static Entry Find(List<Entry> entries, string name)
        {
            if (entries == null)
            {
                return null;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entries[i];
                }
            }

            return null;
        }

        // ---- derived numbers -------------------------------------------------

        /// <summary>One line, for the log: what this machine is running on.</summary>
        public static string Describe(List<Entry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return "(no data files)";
            }

            var text = new StringBuilder();
            for (var i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(entries[i].Describe());
            }

            return text.ToString();
        }

        /// <summary>A single number for the tick checksum, so mid-game tampering shows up.</summary>
        public static uint Digest(List<Entry> entries)
        {
            var hash = 2166136261u;

            if (entries == null)
            {
                return hash;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                hash = Mix(hash, (uint)(entry.Hash & 0xFFFFFFFF));
                hash = Mix(hash, (uint)(entry.Hash >> 32));
                hash = Mix(hash, (uint)entry.Size);
            }

            return hash;
        }

        /// <summary>FNV-1a, same as the game's tick checksum mixer.</summary>
        public static uint Mix(uint hash, uint value)
        {
            hash ^= value & 0xFF;
            hash *= 16777619u;
            hash ^= (value >> 8) & 0xFF;
            hash *= 16777619u;
            hash ^= (value >> 16) & 0xFF;
            hash *= 16777619u;
            hash ^= (value >> 24) & 0xFF;
            hash *= 16777619u;
            return hash;
        }
    }
}
