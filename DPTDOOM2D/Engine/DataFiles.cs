//
// DPT.DOOM 2D — what this installation is made of.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using DPTDOOM2D.Game;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Collects the files that take part in a session, in load order, and turns
    /// them into the manifest the handshake compares.
    ///
    /// The hashing, the wire format and the comparison live in
    /// <see cref="Shared.DataManifest"/>, because the launcher and the master need
    /// them too. This class only knows which files the game actually loaded —
    /// which is the part that must not be guessed from the command line: a mod
    /// added by the launcher arrives exactly like a -file, and a map pack is
    /// loaded on top of both.
    /// </summary>
    public static class DataFiles
    {
        /// <summary>Absolute paths in the order they were loaded; the order changes which resource wins.</summary>
        public static readonly List<string> Files = new List<string>();

        /// <summary>
        /// The data sets the game can boot on, in the order the launcher lists them.
        ///
        /// The list itself lives in Shared: the launcher offers the choice and the game
        /// resolves it, and two copies of the list would eventually disagree.
        /// </summary>
        public static string[] SetFiles
        {
            get { return Shared.DataSets.Files; }
        }

        /// <summary>Human-readable name: DOOM2D, FREEDOOM2D, AIDOOM2D.</summary>
        public static string SetTitle(string fileName)
        {
            return Shared.DataSets.Title(fileName);
        }

        /// <summary>
        /// The path of a data set, from whatever the config or the command line said.
        ///
        /// Accepts "freedoom2d", "FREEDOOM2D.WAD" and a full path alike, and falls back
        /// to doom2d.wad when nothing was asked for. The fallback matters: a config
        /// written before this option existed has no `main_wad` line, and it must still
        /// start the game rather than fail.
        /// </summary>
        public static string MainWadPath(string dataDirectory, string requested)
        {
            return Shared.DataSets.Resolve(dataDirectory, requested);
        }

        /// <summary>
        /// Which data sets are actually installed, for the launcher and for the error
        /// a missing one produces.
        /// </summary>
        public static List<string> InstalledSets(string dataDirectory)
        {
            return Shared.DataSets.Installed(dataDirectory);
        }

        public static string DescribeSets(string dataDirectory)
        {
            return Shared.DataSets.DescribeInstalled(dataDirectory);
        }

        /// <summary>Remembers a file that took part in the load, once.</summary>
        public static void Add(string path)
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
        public static List<Shared.DataManifest.Entry> Build()
        {
            var entries = new List<Shared.DataManifest.Entry>();

            for (int i = 0; i < Files.Count; ++i)
            {
                Shared.DataManifest.Entry entry = Shared.DataManifest.HashFile(Files[i]);
                if (entry != null) entries.Add(entry);
            }

            // The compiled actor tables, not the text: this is what the simulation
            // reads, and it covers built-in data, actors.txt on disk and an
            // ACTORDEF lump inside a mod with one number.
            entries.Add(Shared.DataManifest.Inline("actors", Actors.Digest()));

            return entries;
        }
    }
}
