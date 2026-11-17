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
