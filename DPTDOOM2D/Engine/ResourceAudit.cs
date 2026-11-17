//
// DPT.DOOM 2D — resource audit: what the game asks a WAD for.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.Text;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Every resource the game looked up while the audit ran, and whether it was
    /// there.
    ///
    /// This exists because of FreeDooM2D and AIDooM2D. A replacement data set has to
    /// contain everything the game demands, and a list written by reading the source
    /// is always one lump short — the missing one is a `Fatal` on a player's machine
    /// and nowhere in the tests. So the list is not written, it is recorded: the game
    /// runs, asks for what it asks for, and the audit says what that was.
    /// </summary>
    public sealed class ResourceAudit
    {
        private sealed class Item
        {
            public string Name;
            public bool Sprite;
            public bool Found;
            public int Count;
        }

        private readonly Dictionary<string, Item> items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);

        public void Note(string name, bool found)
        {
            Note(name, found, false);
        }

        public void NoteSprite(string name, bool found)
        {
            Note(name, found, true);
        }

        private void Note(string name, bool found, bool sprite)
        {
            if (string.IsNullOrEmpty(name)) return;

            string key = sprite ? "SPR:" + name : name;

            Item item;
            if (!items.TryGetValue(key, out item))
            {
                item = new Item();
                item.Name = name;
                item.Sprite = sprite;
                item.Found = found;
                items[key] = item;
            }

            ++item.Count;

            // A name is missing only if no lookup of it ever succeeded: a mod may
            // supply a lump after the first miss, and the game retries.
            if (found) item.Found = true;
        }

        public int Total { get { return items.Count; } }

        /// <summary>
        /// Missing sprite sets. A missing lump can be survived — the game draws a
        /// blank glyph — but a missing sprite set is a Fatal the moment a monster
        /// spawns, so this is the number the build check cares about.
        /// </summary>
        public int MissingSprites
        {
            get
            {
                int count = 0;
                foreach (Item item in items.Values) if (!item.Found && item.Sprite) ++count;
                return count;
            }
        }

        public int Missing
        {
            get
            {
                int count = 0;
                foreach (Item item in items.Values) if (!item.Found) ++count;
                return count;
            }
        }

        /// <summary>
        /// The names that were asked for and not found, sorted.
        ///
        /// This is the list a converter has to satisfy: not "what the WAD has", but
        /// "what the game wanted and did not get".
        /// </summary>
        public List<string> MissingNames()
        {
            var names = new List<string>();

            foreach (Item item in items.Values)
                if (!item.Found) names.Add(item.Sprite ? "SPR:" + item.Name : item.Name);

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Everything asked for, sorted by kind and then name.</summary>
        public List<string> AllNames()
        {
            var names = new List<string>();

            foreach (Item item in items.Values)
                names.Add((item.Sprite ? "SPR:" : "") + item.Name);

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>
        /// A report grouped by kind: lumps, sprites, and the ones that were missing.
        ///
        /// The grouping matters for a converter: a missing sprite is fixed by adding
        /// art, a missing lump by adding a lump, and the two come from different
        /// places in FreeDoom. Consecutive numbered names — the font glyphs, mostly —
        /// collapse into one line, or the missing list is sixty lines of STCFN0xx and
        /// the one interesting entry is lost in the middle of it.
        /// </summary>
        public string Report()
        {
            var lumps = new List<string>();
            var sprites = new List<string>();
            var missing = new List<string>();

            foreach (Item item in items.Values)
            {
                if (!item.Found) missing.Add((item.Sprite ? "спрайт " : "") + item.Name);
                else if (item.Sprite) sprites.Add(item.Name);
                else lumps.Add(item.Name);
            }

            lumps.Sort(StringComparer.OrdinalIgnoreCase);
            sprites.Sort(StringComparer.OrdinalIgnoreCase);
            missing.Sort(StringComparer.OrdinalIgnoreCase);

            var text = new StringBuilder();

            text.Append("lumps: ").Append(lumps.Count);
            text.Append(", sprites: ").Append(sprites.Count);
            text.Append(", missing: ").Append(missing.Count);
            text.AppendLine();

            if (missing.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("-- нет в WAD (игра это пережила, значит не критично) --");

                List<string> collapsed = Collapse(missing);
                for (int i = 0; i < collapsed.Count; ++i) text.AppendLine("  " + collapsed[i]);
            }

            text.AppendLine();
            text.AppendLine("-- лампы, которые нужны --");
            List<string> lumpLines = Collapse(lumps);
            for (int i = 0; i < lumpLines.Count; ++i) text.AppendLine("  " + lumpLines[i]);

            text.AppendLine();
            text.AppendLine("-- наборы спрайтов --");
            List<string> spriteLines = Collapse(sprites);
            for (int i = 0; i < spriteLines.Count; ++i) text.AppendLine("  " + spriteLines[i]);

            return text.ToString();
        }

        /// <summary>
        /// The same report, but "found" means "present in this other WAD".
        ///
        /// This is the mode a converter needs: run the game on the real data, record
        /// what it asks for, then ask a candidate file which of those it already has.
        /// The remainder is the shopping list — for FreeDooM2D it is the handful of
        /// lumps Doom has no equivalent for.
        /// </summary>
        public string ReportAgainst(List<string> candidateNames, string candidate)
        {
            var present = new List<string>();
            var absent = new List<string>();

            var wanted = new List<string>();

            foreach (Item item in items.Values)
                wanted.Add((item.Sprite ? "SPR:" : "") + item.Name);

            wanted.Sort(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < wanted.Count; ++i)
            {
                string name = wanted[i];
                bool sprite = name.StartsWith("SPR:", StringComparison.Ordinal);

                if (sprite)
                {
                    // A sprite set is present when any lump of it is: the candidate
                    // may have two rotations where Doom2D wants more.
                    string bare = name.Substring(4);
                    bool any = false;

                    for (int n = 0; n < candidateNames.Count; ++n)
                        if (candidateNames[n].Length >= 4
                            && string.Compare(candidateNames[n], 0, bare, 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            any = true;
                            break;
                        }

                    (any ? present : absent).Add("спрайт " + bare);
                }
                else
                {
                    bool any = false;

                    for (int n = 0; n < candidateNames.Count; ++n)
                        if (NameMatches(candidateNames[n], name))
                        {
                            any = true;
                            break;
                        }

                    (any ? present : absent).Add(name);
                }
            }

            var text = new StringBuilder();

            text.Append("нужно: ").Append(wanted.Count);
            text.Append(", есть в ").Append(candidate).Append(": ").Append(present.Count);
            text.Append(", нет: ").Append(absent.Count);
            text.AppendLine();

            if (absent.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("-- взять из doom2d.wad --");
                List<string> lines = Collapse(absent);
                for (int i = 0; i < lines.Count; ++i) text.AppendLine("  " + lines[i]);
            }

            return text.ToString();
        }

        /// <summary>
        /// Collapses runs of names that differ only by a trailing number:
        /// STCFN096, STCFN097, ... becomes "STCFN096..STCFN127 (32 шт.)".
        ///
        /// Sorted input only, which is what the report has. A run needs three names
        /// to be worth collapsing — two is not shorter than two.
        /// </summary>
        public static List<string> Collapse(List<string> names)
        {
            var result = new List<string>();
            int at = 0;

            while (at < names.Count)
            {
                string name = names[at];
                int digits = TrailingDigits(name);

                if (digits == 0)
                {
                    result.Add(name);
                    ++at;
                    continue;
                }

                string prefix = name.Substring(0, name.Length - digits);
                int first = int.Parse(name.Substring(name.Length - digits));

                int end = at + 1;
                int last = first;

                while (end < names.Count)
                {
                    string next = names[end];
                    int nextDigits = TrailingDigits(next);

                    if (nextDigits != digits) break;
                    if (!next.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) break;

                    int number = int.Parse(next.Substring(next.Length - nextDigits));
                    if (number != last + 1) break;

                    last = number;
                    ++end;
                }

                int run = end - at;

                if (run >= 3)
                {
                    result.Add(prefix + first.ToString(new string('0', digits))
                        + ".." + prefix + last.ToString(new string('0', digits))
                        + " (" + run + " шт.)");
                }
                else
                {
                    for (int i = at; i < end; ++i) result.Add(names[i]);
                }

                at = end;
            }

            return result;
        }

        /// <summary>
        /// The same comparison the WAD loader uses: up to eight characters, case
        /// insensitive, a short name padded with NULs.
        ///
        /// Not String.Equals: the loader's NameEq is what decides whether a lump is
        /// found at runtime, so a report that used a different rule would disagree
        /// with the game about which file is complete.
        /// </summary>
        public static bool NameMatches(string a, string b)
        {
            for (int i = 0; i < 8; ++i)
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

        private static int TrailingDigits(string name)
        {            int count = 0;

            for (int i = name.Length - 1; i >= 0 && char.IsDigit(name[i]); --i) ++count;

            // A name that is all digits is a number, not a series.
            return count == name.Length ? 0 : count;
        }
    }
}
