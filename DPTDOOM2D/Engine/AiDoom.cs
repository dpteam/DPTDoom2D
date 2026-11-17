//
// DPT.DOOM 2D — AIDooM2D: a data set generated entirely by this program.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using DPTDOOM2D.Audio;
using DPTDOOM2D.Game;
using DPTDOOM2D.Prototypes;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Builds a complete data set without borrowing a single pixel, sample or note.
    ///
    /// This is the third of the three sets. `doom2d.wad` is the original, `freedoom2d.wad`
    /// is built from FreeDoom, and this one is drawn by code — which is the only one of
    /// the three that is entirely ours.
    ///
    /// The hard part is not the art, it is the **list of names**. The engine asks for a
    /// resource by name and several of those lookups throw when it is missing, so a data
    /// set is complete or it does not boot. Rather than maintain a hand-written list that
    /// drifts, the generator asks the reference set what the engine looks at: the sprite
    /// lumps inside `S_START`..`S_END` and the `DS*` sounds. Those are the engine's
    /// contract — names and sizes — and only the contents are generated here. Everything
    /// that is not in that list (the levels, the walls, the music) is our own naming.
    ///
    /// Determinism is not a nicety: the data manifest compares the sets two players load,
    /// so `--genwad 7` has to produce the same bytes on every machine. Nothing in this
    /// file touches the clock, the locale or System.Random.
    /// </summary>
    public static class AiDoom
    {
        /// <summary>What the build did, for the log and for `--aicheck`.</summary>
        public sealed class Report
        {
            public int Lumps;
            public int Sprites;
            public int Walls;
            public int Sounds;
            public int Tracks;
            public int Glyphs;
            public int Maps;
            public long Bytes;

            public string Summary()
            {
                return string.Format(
                    "lumps {0}: sprites {1}, walls {2}, sounds {3}, tracks {4}, " +
                    "glyphs {5}, maps {6}, {7} KB",
                    Lumps, Sprites, Walls, Sounds, Tracks, Glyphs, Maps,
                    Math.Max(1, Bytes / 1024));
            }
        }

        /// <summary>The menu pieces the engine draws by name.</summary>
        private static readonly string[] MenuPieces =
        {
            "M_LSCNTR", "M_LSLEFT", "M_LSRGHT",
            "M_SKULL1", "M_SKULL2",
            "M_THERML", "M_THERMM", "M_THERMO", "M_THERMR"
        };

        /// <summary>
        /// Builds the set and writes it.
        ///
        /// The reference WAD is the one the game booted on: it is used for the sprite
        /// names and their sizes, and for nothing else.
        /// </summary>
        public static Report Build(uint seed, string outputPath)
        {
            var report = new Report();
            var output = new List<KeyValuePair<string, byte[]>>();

            byte[] pal = AiArt.Palette(seed);

            // ---- colour ------------------------------------------------------
            output.Add(Item("PLAYPAL", pal));
            output.Add(Item("COLORMAP", AiArt.ColorMap(pal)));
            output.Add(Item("MIXMAP", AiArt.MixMap(pal)));

            // ---- the screens -------------------------------------------------
            output.Add(Item("TITLEPIC", AiArt.Backdrop(pal, seed, 0)));
            output.Add(Item("INTERPIC", AiArt.Backdrop(pal, seed, 1)));
            output.Add(Item("ENDPIC", AiArt.Backdrop(pal, seed, 2)));

            output.Add(Item("STONE", AiArt.Stone(pal, seed, 0)));
            output.Add(Item("STONE2", AiArt.Stone(pal, seed, 1)));

            output.Add(Item("RSKY1", AiArt.Sky(pal, seed, 1)));
            output.Add(Item("RSKY2", AiArt.Sky(pal, seed, 2)));

            // ---- the fonts ---------------------------------------------------
            int glyphs = 0;

            // Small font: 8x7, the same size the engine's own text uses.
            for (int c = 33; c <= 159; ++c)
            {
                output.Add(Item(string.Format("STCFN{0:000}", c), AiSprites.Glyph(c, 8, 7, 0x50)));
                ++glyphs;
            }

            // Big font: 15x12, used for the menu titles.
            for (int c = 33; c <= 159; ++c)
            {
                output.Add(Item("STBF_" + (char)c, AiSprites.Glyph(c, 15, 12, 0x50)));
                ++glyphs;
            }

            // HUD digits.
            for (int c = '0'; c <= '9'; ++c)
            {
                output.Add(Item("STTNUM" + (char)c, AiSprites.HudGlyph(c, 14, 16, 0xB4)));
                output.Add(Item("WINUM" + (char)c, AiSprites.HudGlyph(c, 11, 12, 0x50)));
                ++glyphs;
            }

            output.Add(Item("STTMINUS", AiSprites.HudGlyph('-', 8, 6, 0xB4)));
            output.Add(Item("STTPRCNT", AiSprites.HudGlyph('%', 14, 16, 0xB4)));
            output.Add(Item("WICOLON", AiSprites.HudGlyph(':', 5, 10, 0x50)));
            glyphs += 3;

            report.Glyphs = glyphs;

            // ---- the menu graphics -------------------------------------------
            for (int i = 0; i < MenuPieces.Length; ++i)
            {
                int w, h;

                switch (MenuPieces[i])
                {
                    case "M_SKULL1":
                    case "M_SKULL2": w = 20; h = 19; break;
                    case "M_THERMM": w = 9; h = 13; break;
                    case "M_THERMO": w = 5; h = 13; break;
                    case "M_THERML":
                    case "M_THERMR": w = 6; h = 13; break;
                    default: w = 8; h = 14; break;
                }

                output.Add(Item(MenuPieces[i], AiSprites.MenuPiece(MenuPieces[i], w, h, pal, seed)));
            }

            // ---- the walls ---------------------------------------------------
            for (int i = 1; i < AiLevels.WallNames.Length; ++i)
            {
                string name = AiLevels.WallNames[i];
                if (string.IsNullOrEmpty(name)) continue;

                // A 16x16 tile covers two cells, which is what most of the original's
                // walls do and what makes a corridor look like one surface.
                int size = name == "AIWALL02" || name == "AIWALL05" ? 32 : 16;

                output.Add(Item(name, AiArt.Wall(name, size, size, pal, seed)));
                ++report.Walls;
            }

            // ---- the levels --------------------------------------------------
            for (int map = 1; map <= AiLevels.Count; ++map)
            {
                MapFile level = AiLevels.Build(seed, map);

                output.Add(Item(string.Format("MAP{0:00}", map), level.Write()));
                ++report.Maps;
            }

            // ---- the sounds --------------------------------------------------
            // Inside D_START..D_END, because the engine finds the sound range by those
            // markers and throws when they are absent.
            output.Add(Item("D_START", new byte[0]));

            List<string> sounds = ReferenceSounds();

            for (int i = 0; i < sounds.Count; ++i)
            {
                output.Add(Item(sounds[i], DmiWriter.Write(AiSound.Generate(sounds[i], seed))));
                ++report.Sounds;
            }

            output.Add(Item("D_END", new byte[0]));

            // ---- the music ---------------------------------------------------
            output.Add(Item("M_START", new byte[0]));

            // Three names the engine asks for by hand: the menu's own track, the
            // intermission's, and the one it loads when the campaign ends. A set
            // without them is silent in the front end, and `Game.cs` names the last
            // one in CP866 — "Конец" — so it is spelled the same way here.
            string[] fixedTracks = { "MENU", "INTERMUS", "\u008A\u008E\u008D\u0085\u0096" };

            for (int i = 0; i < fixedTracks.Length; ++i)
            {
                byte[] track = AiMusic.Track(seed, i, Instruments.PlainNames);
                if (track == null) continue;

                output.Add(Item(fixedTracks[i], track));
                ++report.Tracks;
            }

            for (int i = 0; i < AiMusic.Count; ++i)
            {
                byte[] track = AiMusic.Track(seed, fixedTracks.Length + i, Instruments.PlainNames);
                if (track == null) continue;

                output.Add(Item("AIMUS" + (i + 1), track));
                ++report.Tracks;
            }

            // The instruments the tracks name. Generated, like everything else, and
            // under the DMI names so the engine's "next track" walk skips them.
            DmiSample[] instruments = Instruments.Generate();

            for (int i = 0; i < instruments.Length; ++i)
                output.Add(Item(Instruments.PlainNames[i], DmiWriter.Write(instruments[i])));

            output.Add(Item("M_END", new byte[0]));

            // ---- the sprites -------------------------------------------------
            output.Add(Item("S_START", new byte[0]));

            List<KeyValuePair<string, int[]>> sprites = ReferenceSprites();

            for (int i = 0; i < sprites.Count; ++i)
            {
                string name = sprites[i].Key;
                int w = sprites[i].Value[0];
                int h = sprites[i].Value[1];

                // The frame letter decides the walk phase; taking it from the name keeps
                // a cycle in step without knowing which frame is which.
                int letter = name.Length >= 5 ? name[4] - 'A' : 0;

                output.Add(Item(name, AiSprites.Sprite(name.Substring(0, 4), letter, w, h, pal, seed)));
                ++report.Sprites;
            }

            output.Add(Item("S_END", new byte[0]));

            report.Lumps = output.Count;

            if (!WadBuilder.WriteLumps(outputPath, output, "IWAD"))
                throw new Fatal("не удалось записать " + outputPath);

            report.Bytes = new FileInfo(outputPath).Length;

            return report;
        }

        private static KeyValuePair<string, byte[]> Item(string name, byte[] data)
        {
            return new KeyValuePair<string, byte[]>(name, data);
        }

        /// <summary>
        /// The sprite lumps the engine expects: names and sizes, read from the reference.
        ///
        /// Names are a contract, not artwork: `TryGetSprId` matches a four-letter set, a
        /// frame letter and a rotation, and a set that is one letter short is a `Fatal`
        /// the first time that monster spawns. Reading them from the reference is what
        /// keeps this generator from quietly falling out of date with the engine.
        /// </summary>
        private static List<KeyValuePair<string, int[]>> ReferenceSprites()
        {
            var sprites = new List<KeyValuePair<string, int[]>>();

            int start = Wad.FindRes("S_START");
            int end = Wad.FindRes("S_END");

            if (start < 0 || end <= start)
                throw new Fatal("в наборе нет S_START/S_END: неоткуда взять имена спрайтов");

            for (int i = start + 1; i < end; ++i)
            {
                string name = Wad.GetResName(i);
                if (name.Length < 5) continue;

                byte[] raw = Wad.LoadRes(i);
                if (raw == null || raw.Length < 8) continue;

                int w = BitConverter.ToUInt16(raw, 0);
                int h = BitConverter.ToUInt16(raw, 2);

                if (w <= 0 || h <= 0 || w > 200 || h > 200) continue;

                sprites.Add(new KeyValuePair<string, int[]>(name, new[] { w, h }));
            }

            return sprites;
        }

        /// <summary>The `DS*` sounds the engine expects, by name.</summary>
        private static List<string> ReferenceSounds()
        {
            var sounds = new List<string>();

            int start = Wad.FindRes("D_START");
            int end = Wad.FindRes("D_END");

            if (start < 0 || end <= start)
                throw new Fatal("в наборе нет D_START/D_END: неоткуда взять имена звуков");

            for (int i = start + 1; i < end; ++i)
            {
                string name = Wad.GetResName(i);

                // The range also holds a few things that are not sounds (an instrument
                // or a track that was filed here); only the DS prefix is a sound.
                if (!name.StartsWith("DS", StringComparison.Ordinal) || name.Length < 3) continue;

                sounds.Add(name);
            }

            sounds.Sort(StringComparer.Ordinal);
            return sounds;
        }
    }
}
