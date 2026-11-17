using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// files.c — WAD directory and resource access.
    ///
    /// The whole WAD is kept in memory: doom2d.wad is ~4.4 MB, and the original
    /// code seeks around it constantly (F_loadres on every M_lock), so reading it
    /// once is both simpler and faster than mirroring the FILE* juggling.
    /// Names are kept as ISO-8859-1 strings so byte-for-byte identity with the
    /// C code is preserved (several resources are named with cp1251 bytes).
    /// </summary>
    public static class Wad
    {
        public const int MaxWads = 20;
        public const int MaxEntries = 2000;

        public sealed class Entry
        {
            public int Offset;
            public int Length;
            public int File;
            /// <summary>8-byte name, cut at the first NUL (C: char n[8]).</summary>
            public string Name;
        }

        static readonly List<string> wadNames = new List<string>();
        static readonly List<byte[]> wadData = new List<byte[]>();

        public static readonly List<Entry> Entries = new List<Entry>();

        public static int ResCount { get { return Entries.Count; } }

        // F_allocres() markers.
        public static int DStart, DEnd, MStart, MEnd, SStart, SEnd;

        static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public static void Startup()
        {
            wadNames.Clear();
            wadData.Clear();
            Entries.Clear();
            preloaded.Clear();
            looseLumps.Clear();
        }

        public static void AddWad(string fn)
        {
            if (wadNames.Count >= MaxWads)
                throw new Fatal("Не могу добавить WAD " + fn);
            wadNames.Add(fn);
        }

        /// <summary>
        /// Adds a WAD whose bytes are already in memory: a WAD unpacked from a PK3.
        /// <paramref name="displayName"/> is what the log and the error messages
        /// call it.
        /// </summary>
        public static void AddWadFromBytes(string displayName, byte[] data)
        {
            if (wadNames.Count >= MaxWads)
                throw new Fatal("Не могу добавить WAD " + displayName);

            preloaded[wadNames.Count] = data;
            wadNames.Add(displayName);
        }

        /// <summary>A single loose lump: a resource from a PK3 that is not in a WAD.</summary>
        public static void AddLumpFromBytes(string name, byte[] data)
        {
            var lump = new Lump();
            lump.Name = name;
            lump.Data = data;
            looseLumps.Add(lump);
        }

        private sealed class Lump
        {
            public string Name;
            public byte[] Data;
        }

        /// <summary>WAD bytes that came from memory, keyed by their index in wadNames.</summary>
        static readonly Dictionary<int, byte[]> preloaded = new Dictionary<int, byte[]>();

        static readonly List<Lump> looseLumps = new List<Lump>();

        public static void InitWads()
        {
            if (wadNames.Count == 0)
                throw new Fatal("Не задан ни один WAD-файл");

            wadData.Clear();

            for (int i = 0; i < wadNames.Count; ++i)
            {
                byte[] inMemory;
                wadData.Add(preloaded.TryGetValue(i, out inMemory) ? inMemory : File.ReadAllBytes(wadNames[i]));
            }

            // Main WAD: builds the directory, everything else overrides/appends.
            ReadDirectory(0, 0);

            for (int i = 1; i < wadNames.Count; ++i)
            {
                string ext = Path.GetExtension(wadNames[i]);
                if (string.Equals(ext, ".lmp", StringComparison.OrdinalIgnoreCase))
                {
                    // A raw lump: replaces a resource of the same name, or adds one.
                    string baseName = Path.GetFileNameWithoutExtension(wadNames[i]);
                    int k = IndexOf(baseName);
                    if (k >= 0)
                    {
                        Entries[k].Offset = 0;
                        Entries[k].Length = wadData[i].Length;
                        Entries[k].File = i;
                    }
                    else
                    {
                        AddEntry(baseName, 0, wadData[i].Length, i);
                    }
                    continue;
                }

                int n, dirOfs;
                CheckSignature(i, out n, out dirOfs);

                byte[] d = wadData[i];
                for (int j = 0; j < n; ++j)
                {
                    int p = dirOfs + j * 16;
                    int ofs = BitConverter.ToInt32(d, p);
                    int len = BitConverter.ToInt32(d, p + 4);
                    string name = TrimName(d, p + 8);

                    int k = IndexOf(name);
                    if (k >= 0)
                    {
                        Entries[k].Offset = ofs;
                        Entries[k].Length = len;
                        Entries[k].File = i;
                    }
                    else
                    {
                        AddEntry(name, ofs, len, i);
                    }
                }
            }

            ApplyLooseLumps();
            AllocRes();
        }

        /// <summary>
        /// Applies resources that came from a PK3 without a WAD around them: a
        /// loose lump replaces a resource of the same name, or adds a new one.
        /// The bytes are appended as one extra "file" so offsets stay valid.
        /// </summary>
        private static void ApplyLooseLumps()
        {
            if (looseLumps.Count == 0) return;

            // One synthetic file holding every loose lump back to back, so the
            // entry offsets are plain indexes into it.
            var blob = new List<byte>();
            var offsets = new int[looseLumps.Count];

            for (int i = 0; i < looseLumps.Count; ++i)
            {
                offsets[i] = blob.Count;
                blob.AddRange(looseLumps[i].Data);
            }

            wadData.Add(blob.ToArray());
            wadNames.Add("pk3:loose");
            int file = wadData.Count - 1;

            for (int i = 0; i < looseLumps.Count; ++i)
            {
                string name = looseLumps[i].Name;
                int length = looseLumps[i].Data.Length;
                int k = IndexOf(name);

                if (k >= 0)
                {
                    Entries[k].Offset = offsets[i];
                    Entries[k].Length = length;
                    Entries[k].File = file;
                }
                else
                {
                    AddEntry(name, offsets[i], length, file);
                }
            }

            looseLumps.Clear();
        }

        static void ReadDirectory(int fileIndex, int entryIndex)
        {
            int n, dirOfs;
            CheckSignature(fileIndex, out n, out dirOfs);

            byte[] d = wadData[fileIndex];
            for (int j = 0; j < n; ++j)
            {
                int p = dirOfs + j * 16;
                int ofs = BitConverter.ToInt32(d, p);
                int len = BitConverter.ToInt32(d, p + 4);
                AddEntry(TrimName(d, p + 8), ofs, len, fileIndex);
            }
        }

        static void CheckSignature(int fileIndex, out int count, out int dirOfs)
        {
            byte[] d = wadData[fileIndex];
            if (d.Length < 12)
                throw new Fatal("Нет подписи IWAD или PWAD: " + wadNames[fileIndex]);

            string id = Latin1.GetString(d, 0, 4);
            if (id != "IWAD" && id != "PWAD")
                throw new Fatal("Нет подписи IWAD или PWAD: " + wadNames[fileIndex]);

            count = BitConverter.ToInt32(d, 4);
            dirOfs = BitConverter.ToInt32(d, 8);
        }

        static void AddEntry(string name, int ofs, int len, int file)
        {
            if (Entries.Count >= MaxEntries)
                throw new Fatal("Слишком много элементов WAD'а");
            Entry e = new Entry();
            e.Name = name;
            e.Offset = ofs;
            e.Length = len;
            e.File = file;
            Entries.Add(e);
        }

        static string TrimName(byte[] d, int at)
        {
            int n = 0;
            while (n < 8 && d[at + n] != 0) ++n;
            return Latin1.GetString(d, at, n);
        }

        static int IndexOf(string name)
        {
            for (int i = 0; i < Entries.Count; ++i)
                if (NameEq(Entries[i].Name, name, 8)) return i;
            return -1;
        }

        /// <summary>strncasecmp() on a NUL-padded 8-byte name.</summary>
        public static bool NameEq(string a, string b, int max)
        {
            if (a == null || b == null) return a == b;

            for (int i = 0; i < max; ++i)
            {
                char ca = i < a.Length ? a[i] : '\0';
                char cb = i < b.Length ? b[i] : '\0';
                if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 32);
                if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 32);
                if (ca != cb) return false;
                if (ca == '\0') return true;
            }
            return true;
        }

        static void AllocRes()
        {
            DStart = GetResId("D_START");
            DEnd = GetResId("D_END");
            MStart = GetResId("M_START");
            MEnd = GetResId("M_END");
            SStart = GetResId("S_START");
            SEnd = GetResId("S_END");
        }

        /// <summary>F_getresid: the resource must exist.</summary>
        public static int GetResId(string name)
        {
            int i = FindRes(name);
            if (i < 0) throw new Fatal("F_getresid: ресурс " + name + " не найден");
            return i;
        }

        /// <summary>F_findres: -1 when the resource is missing.</summary>
        public static int FindRes(string name)
        {
            for (int i = 0; i < Entries.Count; ++i)
                if (NameEq(Entries[i].Name, name, 8))
                {
                    if (audit != null) audit.Note(name, true);
                    return i;
                }

            if (audit != null) audit.Note(name, false);
            return -1;
        }

        // ---- resource audit -------------------------------------------------
        //
        // --wadreq needs the complete list of what the game asks for, and the only
        // way to get a complete list is to let the game ask. Guessing it from the
        // source gives a list that is always one lump short, and the missing lump is
        // a Fatal on someone else's machine.

        /// <summary>Where lookups are recorded while an audit runs; null normally.</summary>
        private static ResourceAudit audit;

        public static void BeginAudit()
        {
            audit = new ResourceAudit();
        }

        public static ResourceAudit EndAudit()
        {
            ResourceAudit result = audit;
            audit = null;
            return result;
        }

        /// <summary>Records a sprite set name, so the audit knows what art is needed.</summary>
        internal static void NoteSprite(string name, bool found)
        {
            if (audit != null) audit.NoteSprite(name, found);
        }

        public static string GetResName(int r)
        {
            return Entries[r].Name;
        }

        public static int GetResLen(int r)
        {
            return Entries[r].Length;
        }

        public static void LoadRes(int r, byte[] dst, int dstOfs, int ofs, int len)
        {
            Entry e = Entries[r];
            byte[] src = wadData[e.File];
            if (ofs < 0 || len < 0 || e.Offset + ofs + len > src.Length || dstOfs + len > dst.Length)
                throw new Fatal("Ошибка при загрузке ресурса " + e.Name);
            Buffer.BlockCopy(src, e.Offset + ofs, dst, dstOfs, len);
        }

        public static byte[] LoadRes(int r)
        {
            byte[] b = new byte[Entries[r].Length];
            LoadRes(r, b, 0, 0, b.Length);
            return b;
        }

        /// <summary>F_getsprid: returns the resource id, bit 0x8000 = mirrored.</summary>
        public static int GetSprId(string name, int s, int d)
        {
            int id = TryGetSprId(name, s, d);
            if (id < 0)
            {
                throw new Fatal(string.Format("F_getsprid: изображение {0}{1}{2} не найдено",
                    name, (char)('A' + s), (char)('0' + d)));
            }

            return id;
        }

        /// <summary>
        /// The sprite lookup without the fatal error: -1 when the frame is missing.
        ///
        /// A mod may replace a sprite set with fewer frames, and a tool that only
        /// wants a picture of a thing must not die because frame B of it is absent.
        /// The game keeps using <see cref="GetSprId"/>, where a missing sprite is a
        /// real error.
        /// </summary>
        public static int TryGetSprId(string name, int s, int d)
        {
            // The audit asks "is this sprite set in the WAD", not "is frame F7
            // rotation 2 there", so presence is checked once per set and the answer
            // does not depend on which frame the caller happened to want.
            if (audit != null) NoteSprite(name, HasSpriteSet(name));

            int frame = 'A' + s;
            int dir = '0' + d;

            for (int i = SStart + 1; i < SEnd; ++i)
            {
                string n = Entries[i].Name;
                if (!NameEq(n, name, 4)) continue;
                if (CharAt(n, 4) != frame && CharAt(n, 6) != frame) continue;

                char a = CharAt(n, 4) == frame ? CharAt(n, 5) : '\0';
                char b = CharAt(n, 6) == frame ? CharAt(n, 7) : '\0';

                if (a == '0') return i;
                if (b == '0') return i | 0x8000;
                if (a == dir) return i;
                if (b == dir) return i | 0x8000;
            }

            return -1;
        }

        /// <summary>True when any lump in the sprite range starts with this four-letter name.</summary>
        private static bool HasSpriteSet(string name)
        {
            for (int i = SStart + 1; i < SEnd; ++i)
                if (NameEq(Entries[i].Name, name, 4)) return true;

            return false;
        }

        static char CharAt(string s, int i)
        {
            return i < s.Length ? s[i] : '\0';
        }

        /// <summary>F_nextmus: the next music lump inside M_START..M_END.</summary>
        public static string NextMus(string current)
        {
            if (MStart <= 0 || MEnd <= MStart || MEnd >= Entries.Count) return "MENU";

            int i = FindRes(current);
            if (i <= MStart || i >= MEnd) i = MStart;

            // The loop below used to be unbounded: a music section with nothing
            // but excluded names ("MENU", "INTERMUS", "DMI...") spun forever.
            int limit = MEnd - MStart;

            for (++i; limit > 0; ++i, --limit)
            {
                if (i >= MEnd) i = MStart + 1;

                string n = Entries[i].Name;
                if (NameEq(n, "MENU", 8) || NameEq(n, "INTERMUS", 8) ||
                    NameEq(n, "\u008A\u008E\u008D\u0085\u0096", 8))
                    continue;
                if (!NameEq(n, "DMI", 3)) return n;
            }

            return "MENU";
        }

        public static string RandMus(string current)
        {
            int n = Glob.Rand(10);
            string s = current;
            for (int i = 0; i < n; ++i) s = NextMus(s);
            return s;
        }
    }

    /// <summary>ERR_fatal from error.c: a message the player must see, then exit.</summary>
    public sealed class Fatal : Exception
    {
        public Fatal(string message) : base(message) { }
    }
}
