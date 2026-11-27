//
// DPT.DOOM 2D — FreeDooM2D: a free data set built from a Doom-format WAD.
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
    /// Builds a Doom2D data set out of FreeDoom (or any Doom-format WAD).
    ///
    /// The problem this solves: Doom2D's own WAD is 4.4 MB of Prikol Software's
    /// artwork, and it is the only reason the port is not entirely free. FreeDoom is
    /// a free replacement for Doom's data, and Doom2D inherited Doom's sprite names,
    /// its font and its sound names — so most of a replacement can be made from it.
    ///
    /// Three rules decide what comes from where, and they are not interchangeable:
    ///
    ///  * **Sizes come from Doom2D, art comes from Doom.** Doom's imp is 41x57 and
    ///    Doom2D's is 18x25: the game is a platformer with 16-pixel cells, and the art
    ///    was redrawn smaller. Copying Doom's sprites at full size puts a monster
    ///    three cells wide in a two-cell corridor, so every picture is scaled to the
    ///    size the game's own picture has. Only the size is borrowed, never a pixel.
    ///
    ///  * **Walls come from flats, not textures.** A Doom2D wall is a tile that has to
    ///    repeat without a seam; a Doom wall texture is drawn once and is full of
    ///    edges. Doom's flats are seamless by definition. Doors and switches are the
    ///    exception — a flat makes them unrecognisable — so those are composed from
    ///    the texture's patches.
    ///
    ///  * **Anything Doom has no equivalent for comes from Doom2D.** Doom2D's fish,
    ///    its robot, its doors and its menu art exist nowhere else. That is the one
    ///    place the two sets are mixed, and the reason the result ships under GPLv2
    ///    with both copyrights named — see the licence note in docs/FREEDOOM.md.
    ///
    /// Levels are always Doom2D's: a Doom level is THINGS/LINEDEFS/SECTORS for a
    /// different game, and converting one is not a resampling problem.
    /// </summary>
    public static class FreeDoom
    {
        /// <summary>What the conversion did, for the log and the checks.</summary>
        public sealed class Report
        {
            public int Lumps;
            public int SpritesConverted;
            public int SpritesCopied;
            public int WallsConverted;
            public int WallsCopied;
            public int SoundsConverted;
            public int SoundsCopied;
            public int TracksConverted;
            public int TracksKept;
            public int GlyphsConverted;
            public int GlyphsCopied;
            public int Instruments;
            public long Bytes;

            public string Summary()
            {
                return string.Format(
                    "lumps {0}: sprites {1}/{2}, walls {3}/{4}, sounds {5}/{6}, " +
                    "tracks {7} new + {8} kept, glyphs {9}/{10}, instruments {11}, {12} KB",
                    Lumps,
                    SpritesConverted, SpritesConverted + SpritesCopied,
                    WallsConverted, WallsConverted + WallsCopied,
                    SoundsConverted, SoundsConverted + SoundsCopied,
                    TracksConverted, TracksKept,
                    GlyphsConverted, GlyphsConverted + GlyphsCopied,
                    Instruments,
                    Math.Max(1, Bytes / 1024));
            }
        }

        /// <summary>
        /// Converts <paramref name="doomPath"/> into a data set at
        /// <paramref name="outputPath"/>, taking the sizes, the maps and whatever Doom
        /// lacks from <paramref name="doom2dPath"/>.
        /// </summary>
        public static Report Convert(string doomPath, string doom2dPath, string outputPath)
        {
            var source = new DoomWad(doomPath);
            var reference = new DoomWad(doom2dPath);

            // The palette the pictures have to land in is Doom2D's: it is the one the
            // game installs, and the one COLORMAP and MIXMAP were built against.
            InstallPalette(reference);

            byte[] paletteMap = DoomGfx.BuildPaletteMap(source.Palette);

            Dictionary<string, DoomGfx.TextureDef> textures = DoomGfx.ReadTextures(source);
            List<string> patchNames = null;

            var report = new Report();
            var output = new List<KeyValuePair<string, byte[]>>();

            List<string> spriteRange = SpriteNames(reference);
            List<string> wallNames = WallNames(reference);
            List<int> musicLumps = MusicLumps(reference);

            var flats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; ++i)
            {
                string name = source.NameOf(i);
                if (name.Length == 0) continue;
                flats[name] = i;
            }

            // Which Doom flat a wall is built from, resolved once per wall name so the
            // same wall looks the same on every map.
            var wallSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int musicAt = 0;
            var instruments = Instruments.Generate();

            var names = reference.Names();

            for (int i = 0; i < names.Count; ++i)
            {
                string name = names[i];
                if (name.Length == 0) continue;

                byte[] data = null;

                if (IsSprite(spriteRange, name))
                {
                    data = ConvertSprite(source, reference, name, paletteMap, report);
                }
                else if (IsGlyph(name))
                {
                    data = ConvertGlyph(source, reference, name, paletteMap, report);
                }
                else if (IsSound(name))
                {
                    data = ConvertSound(source, reference, name, report);
                }
                else if (musicLumps.Contains(i))
                {
                    data = ConvertTrack(source, reference, musicLumps, ref musicAt, report);
                }
                else if (wallNames.Contains(name))
                {
                    data = ConvertWall(source, reference, name, textures, ref patchNames,
                        flats, wallSource, paletteMap, report);
                }
                else if (IsInstrument(name))
                {
                    // Doom2D's instrument samples are copied as they are: a track that
                    // could not be converted still names them, and a silent track is
                    // worse than one whose timbre is the original's. Converted tracks
                    // use the generated AIDMI* instruments instead.
                    data = reference.Read(i);
                }

                // Everything else — the maps, the tables, the menu art — is Doom2D's,
                // either because Doom has no equivalent or because it is not a picture.
                if (data == null) data = reference.Read(i);
                if (data == null) continue;

                output.Add(new KeyValuePair<string, byte[]>(name, data));
            }

            // The generated instruments go in after the copied lumps, so they are the
            // ones the game finds when a converted song asks for DMI0001.
            for (int i = 0; i < instruments.Length; ++i)
            {
                output.Add(new KeyValuePair<string, byte[]>(
                    Instruments.Names[i], DmiWriter.Write(instruments[i])));
            }

            report.Instruments = instruments.Length;
            report.Lumps = output.Count;

            if (!WadBuilder.WriteLumps(outputPath, output, "IWAD"))
                throw new Fatal("не удалось записать " + outputPath);

            report.Bytes = new FileInfo(outputPath).Length;
            return report;
        }

        // ---- the reference --------------------------------------------------

        private static void InstallPalette(DoomWad reference)
        {
            byte[] palette = reference.Palette;

            if (palette == null || palette.Length < 768)
                throw new Fatal("в наборе нет PLAYPAL");

            Buffer.BlockCopy(palette, 0, Palette.Main, 0, 768);
            Palette.RebuildBright();
            Palette.SetGamma(0);
        }

        /// <summary>The lumps between S_START and S_END: the game's sprite set.</summary>
        private static List<string> SpriteNames(DoomWad wad)
        {
            var names = new List<string>();

            int start = wad.Find("S_START");
            int end = wad.Find("S_END");

            if (start < 0 || end <= start) return names;

            for (int i = start + 1; i < end; ++i)
            {
                string name = wad.NameOf(i);
                if (name.Length > 0) names.Add(name);
            }

            return names;
        }

        private static bool IsSprite(List<string> range, string name)
        {
            for (int i = 0; i < range.Count; ++i)
                if (string.Compare(range[i], name, StringComparison.OrdinalIgnoreCase) == 0) return true;

            return false;
        }

        /// <summary>
        /// Every wall name the nineteen levels use.
        ///
        /// Read out of the maps rather than out of the WAD's directory: a wall is only
        /// a wall if a map names it, and Doom2D's directory also holds music, fonts and
        /// sounds. The maps are the definition.
        /// </summary>
        private static List<string> WallNames(DoomWad wad)
        {
            var names = new List<string>();

            for (int map = 1; map <= 32; ++map)
            {
                int index = wad.Find(string.Format("MAP{0:00}", map));
                if (index < 0) continue;

                MapFile level;

                try { level = MapFile.Read(wad.Read(index), "MAP" + map); }
                catch (Fatal) { continue; }

                for (int i = 1; i < 256; ++i)
                {
                    string name = level.WallName[i];
                    if (string.IsNullOrEmpty(name)) continue;
                    if (names.Contains(name)) continue;

                    names.Add(name);
                }
            }

            return names;
        }

        /// <summary>The indices of the lumps that hold a DMM song.</summary>
        private static List<int> MusicLumps(DoomWad wad)
        {
            var found = new List<int>();

            for (int i = 0; i < wad.Count; ++i)
            {
                if (wad.LengthOf(i) < 16) continue;

                byte[] head = wad.Read(i);
                if (head == null || head.Length < 4) continue;

                if (head[0] == 'D' && head[1] == 'M' && head[2] == 'M' && head[3] == 0)
                    found.Add(i);
            }

            return found;
        }

        private static bool IsGlyph(string name)
        {
            return name.StartsWith("STCFN", StringComparison.Ordinal)
                || name.StartsWith("STBF_", StringComparison.Ordinal)
                || name.StartsWith("WINUM", StringComparison.Ordinal)
                || name == "WICOLON";
        }

        private static bool IsSound(string name)
        {
            // DS* is the game's sound namespace. DSSTART/DSEND and the like are
            // markers, not samples; the length check below drops them.
            return name.StartsWith("DS", StringComparison.Ordinal) && name.Length > 2;
        }

        private static bool IsInstrument(string name)
        {
            if (!name.StartsWith("DMI", StringComparison.Ordinal) || name.Length != 7) return false;

            for (int i = 3; i < 7; ++i)
                if (name[i] < '0' || name[i] > '9') return false;

            return true;
        }

        // ---- pictures -------------------------------------------------------

        /// <summary>
        /// The lump a Doom2D sprite is converted from, by name. For checks.
        ///
        /// The rotation a sprite comes from is not something a finished WAD can be
        /// asked about — the picture is resized and recoloured, and a front view
        /// downscaled to eighteen pixels wide looks a lot like a profile. So the
        /// converter reports its choice instead, and the check compares that.
        /// </summary>
        public static string RotationSource(string doomPath, string spriteName)
        {
            var source = new DoomWad(doomPath);

            int lump = FindRotation(source, spriteName);
            return lump < 0 ? null : source.NameOf(lump);
        }

        private static byte[] ConvertSprite(DoomWad source, DoomWad reference, string name,
            byte[] paletteMap, Report report)
        {
            int lump = FindRotation(source, name);
            if (lump < 0) { ++report.SpritesCopied; return null; }

            DoomImage picture = DoomGfx.ReadPatch(source.Segment(lump));
            if (picture == null) { ++report.SpritesCopied; return null; }

            int[] size = Header(reference, name);
            if (size == null) { ++report.SpritesCopied; return null; }

            DoomImage sized = DoomGfx.Resize(picture, size[0], size[1], paletteMap);
            if (sized == null) { ++report.SpritesCopied; return null; }

            ++report.SpritesConverted;
            return DoomGfx.ToVgaImage(sized);
        }

        /// <summary>
        /// The Doom lump whose picture is the second rotation of a Doom2D sprite.
        ///
        /// Doom2D stores two rotations in one lump: TROOA1A2 holds rotation 1, and
        /// rotation 2 is that same picture mirrored at draw time (Vga.Spr2). Rotation
        /// 1 is therefore the **profile** — the one a side view uses — because its
        /// mirror is the other side.
        ///
        /// Doom stores all eight rotations separately, and rotation 1 is the one facing
        /// the camera. Copying it made every monster in FreeDooM2D stare straight ahead
        /// while walking left and right: the game mirrored a front view, which looks
        /// like a monster sliding sideways. Rotation 3 is the side view, and Doom's own
        /// convention already stores it with 7 as its mirror — exactly the pair Doom2D
        /// wants.
        ///
        /// Falls back to the front view for the single-rotation frames (I, J, K, L and
        /// the like), where Doom has only rotation 0 and there is no profile to take.
        /// </summary>
        private static int FindRotation(DoomWad source, string name)
        {
            // TROOA1A2 -> base TROO, frame A; TROOI0 -> base TROO, frame I.
            if (name.Length < 6) return source.Find(name);

            string baseName = name.Substring(0, 4);
            char frame = name[4];

            // Doom's side view, in the spellings a WAD may use. FreeDoom pairs the
            // mirror images into one lump the way Doom2D does ("TROOA3A7"), so the
            // plain "TROOA3" is not there; a WAD built by Doom's own tools has the two
            // rotations separately. Try the paired form first, then either half.
            string[] order =
            {
                baseName + frame + "3A7",
                baseName + frame + "3",
                baseName + frame + "7",
                baseName + frame + "1",     // the front view: wrong, but better than nothing
                baseName + frame + "5"
            };

            for (int i = 0; i < order.Length; ++i)
            {
                int id = source.Find(order[i]);
                if (id >= 0) return id;
            }

            // A pack with unusual names: take whatever the old rule found.
            string from = name.Length == 8 ? name.Substring(0, 6) : name;
            return source.Find(from);
        }

        private static byte[] ConvertGlyph(DoomWad source, DoomWad reference, string name,
            byte[] paletteMap, Report report)
        {
            int lump = source.Find(name);

            if (lump >= 0)
            {
                DoomImage glyph = DoomGfx.ReadPatch(source.Segment(lump));
                if (glyph != null)
                {
                    // The font is the same size in both games — Doom2D inherited it —
                    // so this is a colour map, not a resize.
                    DoomImage mapped = DoomGfx.MapColours(glyph, paletteMap);
                    ++report.GlyphsConverted;
                    return DoomGfx.ToVgaImage(mapped);
                }
            }

            ++report.GlyphsCopied;
            return null;
        }

        private static byte[] ConvertWall(DoomWad source, DoomWad reference, string name,
            Dictionary<string, DoomGfx.TextureDef> textures, ref List<string> patchNames,
            Dictionary<string, int> flats, Dictionary<string, string> wallSource,
            byte[] paletteMap, Report report)
        {
            int[] size = Header(reference, name);
            if (size == null) { ++report.WallsCopied; return null; }

            string chosen;
            if (!wallSource.TryGetValue(name, out chosen))
            {
                chosen = PickWallArt(name, source, textures);
                wallSource[name] = chosen;
            }

            if (string.IsNullOrEmpty(chosen)) { ++report.WallsCopied; return null; }

            DoomImage picture = LoadArt(source, chosen, textures, ref patchNames, flats, paletteMap);
            if (picture == null) { ++report.WallsCopied; return null; }

            DoomImage sized;

            if (picture.W == size[0] && picture.H == size[1])
            {
                sized = picture;
            }
            else if (size[0] * 2 <= picture.W && size[1] * 2 <= picture.H)
            {
                // A big flat squeezed into an 8x8 tile becomes a colour block; sampling
                // keeps the pattern, which is what a wall is for.
                sized = DoomGfx.ResizeNearest(picture, size[0], size[1]);
            }
            else
            {
                sized = DoomGfx.Resize(picture, size[0], size[1], paletteMap);
            }

            if (sized == null) { ++report.WallsCopied; return null; }

            ++report.WallsConverted;
            return DoomGfx.ToVgaImage(sized);
        }

        /// <summary>
        /// Chooses the Doom artwork for a Doom2D wall.
        ///
        /// The names are the only clue available: Doom2D's walls are called SW01_0,
        /// GDOORKR1, SQR01_1 and so on, with no description anywhere. The categories
        /// below are the ones a player can tell apart at a glance — a door has to look
        /// like a door — and everything else gets a stone or metal tile chosen by the
        /// name's hash, so a level does not come out in one flat colour.
        /// </summary>
        private static string PickWallArt(string name, DoomWad source,
            Dictionary<string, DoomGfx.TextureDef> textures)
        {
            string lower = name.ToLowerInvariant();

            if (lower.StartsWith("_water"))
                return null;    // drawn by a palette remap, not by a picture

            if (lower.Contains("door"))
                return FirstTexture(textures, "BIGDOOR1", "DOOR1", "BIGDOOR2", "DOOR3");

            if (lower.StartsWith("sw") || lower.Contains("trap"))
                return FirstTexture(textures, "SW1BRN1", "SW1COMP", "SW2SLAD", "SW1STONE");

            if (lower.Contains("exit"))
                return FirstTexture(textures, "EXITSIGN", "SW2EXIT", "EXITDOOR");

            if (lower.Contains("comp"))
                return FirstTexture(textures, "COMPUTE1", "COMPTILE", "COMPUTER", "STARTAN3");

            if (lower.Contains("water"))
                return FirstFlat(source, "FWATER1", "NUKAGE1", "SLIME01", "WATER1");

            if (lower.Contains("lava"))
                return FirstFlat(source, "LAVA1", "NUKAGE3", "SLIME15", "LAVA2");

            if (lower.Contains("kvas") || lower.Contains("crate"))
                return FirstFlat(source, "BROWN1", "STONE2", "METAL1", "WOOD1");

            // Everything else: a wall, a floor, a support. Picking by hash of the name
            // gives a level several materials instead of one.
            string[] candidates =
            {
                "STONE1", "STONE2", "STONE3", "BROWN1", "STARTAN3", "GRAY1",
                "METAL1", "METAL2", "SUPPORT2", "STEP1", "TEKWALL1", "SILVER1"
            };

            int at = 0;
            for (int i = 0; i < name.Length; ++i) at = (at * 31 + name[i]) & 0x7FFFFFFF;

            for (int i = 0; i < candidates.Length; ++i)
            {
                string candidate = candidates[(at + i) % candidates.Length];

                if (source.Has(candidate) || textures.ContainsKey(candidate))
                    return candidate;
            }

            return FirstFlat(source, "FLOOR0_1", "FLAT1", "STONE1", "BROWN1");
        }

        private static string FirstTexture(Dictionary<string, DoomGfx.TextureDef> textures,
            params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; ++i)
                if (textures.ContainsKey(candidates[i])) return candidates[i];

            return candidates.Length > 0 ? candidates[0] : null;
        }

        private static string FirstFlat(DoomWad source, params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; ++i)
                if (source.Has(candidates[i])) return candidates[i];

            return candidates.Length > 0 ? candidates[0] : null;
        }

        /// <summary>Loads a named flat or texture as an image.</summary>
        private static DoomImage LoadArt(DoomWad source, string name,
            Dictionary<string, DoomGfx.TextureDef> textures, ref List<string> patchNames,
            Dictionary<string, int> flats, byte[] paletteMap)
        {
            int lump;

            if (flats.TryGetValue(name, out lump))
            {
                int size;
                byte[] flat = source.ReadFlat(lump, out size);

                if (flat != null) return DoomGfx.FlatToImage(flat, size, paletteMap);
            }

            DoomGfx.TextureDef texture;
            if (!textures.TryGetValue(name, out texture)) return null;

            if (patchNames == null) patchNames = PatchNames(source);

            return DoomGfx.ComposeTexture(source, texture, patchNames, paletteMap);
        }

        private static List<string> PatchNames(DoomWad wad)
        {
            int index = wad.Find("PNAMES");
            if (index < 0) return new List<string>();

            byte[] raw = wad.Read(index);
            if (raw == null || raw.Length < 4) return new List<string>();

            int count = BitConverter.ToInt32(raw, 0);
            if (count < 0 || 4 + count * 8 > raw.Length) return new List<string>();

            var names = new List<string>(count);

            for (int i = 0; i < count; ++i)
                names.Add(System.Text.Encoding.ASCII.GetString(raw, 4 + i * 8, 8).TrimEnd('\0'));

            return names;
        }

        private static int[] Header(DoomWad wad, string name)
        {
            int lump = wad.Find(name);
            if (lump < 0) return null;

            byte[] raw = wad.Read(lump);
            if (raw == null || raw.Length < 8) return null;

            int w = BitConverter.ToUInt16(raw, 0);
            int h = BitConverter.ToUInt16(raw, 2);

            if (w <= 0 || h <= 0) return null;

            return new[] { w, h };
        }

        // ---- sound ----------------------------------------------------------

        private static byte[] ConvertSound(DoomWad source, DoomWad reference, string name,
            Report report)
        {
            int lump = source.Find(name);

            byte[] pcm;
            int rate;

            if (lump >= 0 && source.ReadSound(lump, out pcm, out rate))
            {
                // Doom's samples are unsigned 8-bit; DMI is signed. FromPcm8 does the
                // flip, and the header writer takes care of the rest.
                DmiSample sample = DmiSample.FromPcm8(pcm, rate);

                ++report.SoundsConverted;
                return DmiWriter.Write(sample);
            }

            ++report.SoundsCopied;
            return null;
        }

        // ---- music ----------------------------------------------------------

        /// <summary>
        /// Replaces one DMM track with a converted MUS one.
        ///
        /// The names do not correspond — Doom2D's tracks are called СТРАХ and ПРИКОЛ,
        /// Doom's D_RUNNIN and D_DOOM — so tracks are paired in the order they appear.
        /// That is arbitrary but it is also deterministic, and the alternative is a
        /// hand-written table that would be wrong for any other FreeDoom release.
        ///
        /// A track that does not survive the conversion keeps Doom2D's own version:
        /// some of Doom's ambient pieces are sparse enough to be silence, and a track
        /// that cannot be expressed in DMM is better left as it was than replaced with
        /// two notes. <see cref="Report.TracksKept"/> counts those.
        /// </summary>
        private static byte[] ConvertTrack(DoomWad source, DoomWad reference,
            List<int> musicLumps, ref int musicAt, Report report)
        {
            List<string> tracks = MusicTracks(source);

            if (tracks.Count == 0) return null;

            string track = tracks[musicAt % tracks.Count];
            ++musicAt;

            byte[] raw = source.Read(track);
            if (raw == null) return null;

            // Doom ships MUS, FreeDoom ships MIDI. Both readers produce the same
            // MidiNote list, so only this line has to know which is which — and
            // assuming MUS is what made the first FreeDoom build come out with
            // "tracks 0 new" and Prikol's soundtrack in a set that claims to be free.
            List<MidiNote> notes = LooksLikeMidi(raw)
                ? MidiFile.Read(raw)
                : MusFile.Read(raw, false);

            // DMM holds at most 64 patterns of 512 ticks, which is a little over eight
            // minutes. A longer song is cut at the limit rather than refused: the part
            // a listener hears is at the start.
            int keep = 0;

            for (int i = 0; i < notes.Count; ++i)
                if (notes[i].Start < MaxTrackSeconds) ++keep;

            if (keep < notes.Count) notes.RemoveRange(keep, notes.Count - keep);

            if (notes.Count < MinNotes)
            {
                ++report.TracksKept;
                return null;
            }

            string[] instruments = ChannelInstruments();

            DmmSong song = MidiToDmm.Convert(notes, instruments);
            byte[] data = DmmWriter.Write(song);

            // Read it back through the real loader: a converter bug is then a failed
            // check, not a silent piece of music nobody can hear.
            XmModule module = DmmModule.Load(data, null);

            if (module == null)
            {
                ++report.TracksKept;
                return null;
            }

            ++report.TracksConverted;
            return data;
        }

        /// <summary>
        /// How much of a track is kept: 64 patterns of 512 ticks is 8.5 minutes, and a
        /// game track is three. Cutting here is what keeps a sparse ambient piece from
        /// overflowing the format.
        /// </summary>
        private const double MaxTrackSeconds = 480.0;

        /// <summary>
        /// Fewer notes than this and the track is not music, it is a handful of hits
        /// spread over minutes — a converter artefact, not something to ship.
        /// </summary>
        private const int MinNotes = 30;

        /// <summary>
        /// Which generated instrument each MIDI channel plays.
        ///
        /// General MIDI's numbering is a hint: low programs are pianos and organs,
        /// 32-39 are basses, 40-47 strings, 80-87 leads. That is enough to keep the
        /// bass under the melody instead of above it, which is the one thing that makes
        /// a converted track sound wrong.
        /// </summary>
        private static string[] ChannelInstruments()
        {
            var map = new string[16];

            for (int i = 0; i < map.Length; ++i) map[i] = "AIDMI01";

            map[0] = "AIDMI01";     // lead
            map[1] = "AIDMI02";     // bass
            map[2] = "AIDMI04";     // chords
            map[3] = "AIDMI05";     // soft pad
            map[4] = "AIDMI03";     // saw pad
            map[5] = "AIDMI01";
            map[6] = "AIDMI04";
            map[7] = "AIDMI05";

            return map;
        }

        /// <summary>
        /// The lumps that hold music, in either format a Doom data set uses.
        ///
        /// Doom and Doom II ship MUS; FreeDoom ships standard MIDI. Both are handled,
        /// because a converter that only knows one of them silently produces a data set
        /// with no soundtrack of its own — and then falls back to the reference's music,
        /// which is exactly what a "free" set must not do.
        /// </summary>
        private static List<string> MusicTracks(DoomWad source)
        {
            var tracks = new List<string>();

            for (int i = 0; i < source.Count; ++i)
            {
                if (source.LengthOf(i) < 16) continue;

                byte[] head = source.Read(i);
                if (head == null || head.Length < 4) continue;

                bool mus = head[0] == 'M' && head[1] == 'U' && head[2] == 'S' && head[3] == 0x1A;
                if (mus) { tracks.Add(source.NameOf(i)); continue; }

                if (LooksLikeMidi(head)) tracks.Add(source.NameOf(i));
            }

            return tracks;
        }

        /// <summary>A standard MIDI file starts with the "MThd" chunk.</summary>
        private static bool LooksLikeMidi(byte[] data)
        {
            return data != null && data.Length >= 14
                && data[0] == 'M' && data[1] == 'T' && data[2] == 'h' && data[3] == 'd';
        }
    }
}
