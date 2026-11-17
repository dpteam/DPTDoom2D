//
// DPT.DOOM 2D — the data sets the game can boot on.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;

namespace DPTDOOM2D.Shared
{
    /// <summary>
    /// The three interchangeable data sets, and the rules for finding one.
    ///
    /// All three have the same lump names and the same formats — that is the whole
    /// point of the FreeDoom converter — so the engine never needs to know which one
    /// it got. Only the file name differs, which is why this is a list of names and
    /// not a mode in the code.
    ///
    /// It lives in Shared because the launcher and the game both need it: the
    /// launcher to offer a choice, the game to resolve what was chosen. Two copies of
    /// this list would eventually disagree, and the symptom would be a set that the
    /// launcher offers and the game cannot find.
    /// </summary>
    public static class DataSets
    {
        /// <summary>In the order the launcher lists them.</summary>
        public static readonly string[] Files =
        {
            "doom2d.wad",
            "freedoom2d.wad",
            "aidoom2d.wad"
        };

        /// <summary>The name a player sees: DOOM2D, FREEDOOM2D, AIDOOM2D.</summary>
        public static string Title(string fileName)
        {
            string bare = Path.GetFileNameWithoutExtension(fileName ?? "");

            return bare.Length == 0 ? "" : bare.ToUpperInvariant();
        }

        /// <summary>
        /// Resolves whatever the config or the command line said into a path.
        ///
        /// Accepts "freedoom2d", "FREEDOOM2D.WAD" and a full path alike. An empty
        /// request falls back to doom2d.wad: a config written before this option
        /// existed has no `main_wad` line, and it must still start the game rather
        /// than fail.
        /// </summary>
        public static string Resolve(string dataDirectory, string requested)
        {
            string name = (requested ?? "").Trim();

            if (name.Length == 0) name = Files[0];

            // A path that exists is used as it is: a player may keep a set elsewhere.
            if (File.Exists(name)) return name;

            if (!name.EndsWith(".wad", StringComparison.OrdinalIgnoreCase)) name += ".wad";

            return Path.Combine(dataDirectory, name);
        }

        /// <summary>Which sets are actually installed.</summary>
        public static List<string> Installed(string dataDirectory)
        {
            var found = new List<string>();

            if (string.IsNullOrEmpty(dataDirectory)) return found;

            for (int i = 0; i < Files.Length; ++i)
                if (File.Exists(Path.Combine(dataDirectory, Files[i]))) found.Add(Files[i]);

            return found;
        }

        /// <summary>For the error a missing set produces.</summary>
        public static string DescribeInstalled(string dataDirectory)
        {
            List<string> found = Installed(dataDirectory);

            if (found.Count == 0) return "ни одного (Data пуста)";

            var names = new List<string>();
            for (int i = 0; i < found.Count; ++i) names.Add(Title(found[i]));

            return string.Join(", ", names.ToArray());
        }
    }
}
