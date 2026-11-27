using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DPTDOOM2D.Audio;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// music.c — background music.
    ///
    /// Doom2D ships its 31 tracks as FastTracker 2 modules next to the executable
    /// and rembo handed them to SDL_mixer's mikmod backend. Here they are played by
    /// the port's own replayer (Audio/XmPlayer.cs) straight into the same mixer the
    /// sound effects use, so there is no extra library and no extra device.
    ///
    /// Two sources, in this order:
    ///
    ///   1. <c>Data/Music/&lt;track&gt;</c> — an XM conversion, the better sounding one.
    ///   2. The DMM song of the same name inside doom2d.wad — the original data,
    ///      converted to XM in memory by <see cref="DmmModule"/>.
    ///
    /// The second path matters more than it looks: track names in the maps are
    /// CP1251 ("Конец"), while the files on disk are transliterated ("KONEC").
    /// Without the transliteration table only the Latin-named tracks ever played,
    /// which is why the soundtrack used to start and stop depending on the map.
    /// </summary>
    public static class Music
    {
        public static int MusVol = 50;

        static string current;
        static XmModule module;
        static XmPlayer player;
        static int count;
        static readonly object sync = new object();

        public static bool Disabled { get { return MusVol == 0; } }

        public static void Init()
        {
            AudioSystem.Mixer.MusicRenderer = Render;
            count = 0;
        }

        /// <summary>The mixer calls this from its audio thread.</summary>
        static bool Render(float[] destination, int frames)
        {
            lock (sync)
            {
                if (player == null) return false;

                float gain = MusVol / 128f;
                if (gain <= 0.001f) return false;

                // Render into a scratch buffer with the music's own gain, then add
                // it to the shared accumulator the effects already filled.
                var scratch = scratchBuffer;
                if (scratch == null || scratch.Length < frames * AudioOut.Channels)
                {
                    scratch = new float[frames * AudioOut.Channels];
                    scratchBuffer = scratch;
                }

                Array.Clear(scratch, 0, frames * AudioOut.Channels);
                if (!player.Render(scratch, frames)) return false;

                for (int i = 0; i < frames * AudioOut.Channels; ++i) destination[i] += scratch[i] * gain;
                return true;
            }
        }

        [ThreadStatic] static float[] scratchBuffer;

        /// <summary>
        /// Renders the loaded track into a buffer with no audio device attached,
        /// for the headless <c>--music</c> check. Same code path as playback.
        /// </summary>
        public static bool RenderForTest(float[] destination, int frames)
        {
            lock (sync)
            {
                return player != null && player.Render(destination, frames);
            }
        }

        /// <summary>F_loadmus: load a track by its WAD name. Missing tracks are not fatal.</summary>
        public static void Load(string name)
        {
            if (Disabled) return;
            if (string.IsNullOrEmpty(name)) return;

            lock (sync)
            {
                current = name;
                player = null;
                module = null;

                try
                {
                    string path = ResolveTrackFile(name);
                    if (path != null)
                    {
                        XmModule loaded = XmModule.Load(path);
                        if (loaded != null)
                        {
                            module = loaded;
                            player = new XmPlayer(module);
                            return;
                        }

                        Log.Warn("music: " + path + " is not a readable XM module");
                    }

                    XmModule fromWad = LoadFromWad(name);
                    if (fromWad != null)
                    {
                        module = fromWad;
                        player = new XmPlayer(module);
                        return;
                    }

                    Log.Warn("music: no track for '" + Describe(name) + "'");
                }
                catch (Exception e)
                {
                    Log.Error("music: cannot load '" + Describe(name) + "'", e);
                    player = null;
                    module = null;
                }
            }
        }

        /// <summary>
        /// Converts the DMM song of that name out of the WAD. The samples are
        /// separate DMI lumps in the same music section, so they are resolved by
        /// name as the converter asks for them.
        /// </summary>
        private static XmModule LoadFromWad(string name)
        {
            int id = FindMusicLump(name);
            if (id < 0) return null;

            byte[] data = Wad.LoadRes(id);
            if (!DmmModule.LooksLikeDmm(data)) return null;

            return DmmModule.Load(data, ResolveSample);
        }

        /// <summary>
        /// Finds a music lump by name, falling back to the transliterated spelling
        /// so "KONEC" finds the lump whose name is the CP1251 "Конец".
        /// </summary>
        private static int FindMusicLump(string name)
        {
            int id = Wad.FindRes(name);
            if (id > Wad.MStart && id < Wad.MEnd) return id;

            if (Wad.MStart <= 0 || Wad.MEnd <= Wad.MStart) return -1;

            string wanted = Transliterate(name);
            for (int i = Wad.MStart + 1; i < Wad.MEnd; ++i)
            {
                string lump = Wad.GetResName(i);
                if (string.Equals(Transliterate(lump), wanted, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private static byte[] ResolveSample(string sampleName)
        {
            if (string.IsNullOrEmpty(sampleName)) return null;

            int id = Wad.FindRes(sampleName);
            if (id < 0) return null;

            return Wad.LoadRes(id);
        }

        /// <summary>
        /// Finds the XM file for a track name, transliterating CP1251 names the
        /// way rembo's music.c did: 'Конец' is the file 'KONEC'.
        /// </summary>
        public static string ResolveTrackFile(string name)
        {
            string directory = Path.Combine(Program.DataDirectory, "Music");
            if (!Directory.Exists(directory)) return null;

            string latin = Transliterate(name);
            string[] candidates = { name, latin, name.ToUpperInvariant(), latin.ToUpperInvariant() };

            for (int i = 0; i < candidates.Length; ++i)
            {
                if (string.IsNullOrEmpty(candidates[i])) continue;

                string path = Path.Combine(directory, candidates[i]);
                if (File.Exists(path)) return path;
            }

            return null;
        }

        private static readonly string[] UpperTable = {
            "A", "B", "V", "G", "D", "E", "ZH", "Z", "I", "J", "K", "L", "M", "N", "O", "P",
            "R", "S", "T", "U", "F", "H", "C", "CH", "SH", "SCH", "X", "Y", "J", "E", "JU", "JA"
        };

        /// <summary>CP1251 Cyrillic to the ASCII spelling rembo used for file names.</summary>
        public static string Transliterate(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var text = new StringBuilder(name.Length + 8);

            for (int i = 0; i < name.Length; ++i)
            {
                char c = name[i];

                if (c < 0x80) { text.Append(c); continue; }

                // Upper case 0x80..0x9F, lower case 0xA0..0xBF.
                int upper = c >= 0x80 && c <= 0x9F ? c : (c >= 0xA0 && c <= 0xBF ? c - 0x20 : -1);
                if (upper >= 0)
                {
                    text.Append(UpperTable[upper - 0x80]);
                    continue;
                }

                // Lower case 0xE0..0xEF maps to the same letters as 0x90..0x9F.
                if (c >= 0xE0 && c <= 0xEF)
                {
                    text.Append(UpperTable[c - 0xE0 + 0x10]);
                    continue;
                }

                text.Append('_');
            }

            return text.ToString();
        }

        public static void Free()
        {
            lock (sync)
            {
                if (player != null) player.Stop();
                player = null;
                module = null;
                current = null;
            }
            count = 0;
        }

        public static void Start(int time)
        {
            if (Disabled) return;

            lock (sync)
            {
                if (player == null && module != null) player = new XmPlayer(module);
            }

            if (time > 0) count = time * 60 * 1000 / Glob.Delay;
            else count = 0;
        }

        public static void Stop()
        {
            lock (sync)
            {
                if (player != null) player.Stop();
            }
            count = 0;
        }

        public static void Volume(int v)
        {
            MusVol = v < 0 ? 0 : (v > 128 ? 128 : v);
            if (MusVol == 0) Stop();
        }

        /// <summary>S_updatemusic: advances the per-track timer.</summary>
        public static void Update()
        {
            if (count <= 0) return;
            if (--count > 0) return;

            try
            {
                string next = Wad.NextMus(Encode(current));
                Load(next);
                Start(Game.Game.MusicTime);
            }
            catch (Exception e)
            {
                // A broken track must not stop the game: log it and try again in
                // a few seconds rather than every tick.
                Log.Error("music: cannot advance the track", e);
                count = 20 * 10;
            }
        }

        /// <summary>Skips to the next (or previous) track. Returns the new name.</summary>
        public static string Skip(int direction)
        {
            string next = current;

            try
            {
                string from = Encode(current);
                if (direction > 0) next = Wad.NextMus(from);
                else
                {
                    // Walking forward is the only direction F_nextmus has, so go
                    // all the way round and stop one short.
                    next = from;
                    for (int i = 0; i < 64; ++i)
                    {
                        string candidate = Wad.NextMus(next);
                        if (candidate == from) break;
                        next = candidate;
                    }
                }

                Load(next);
                Start(Game.Game.MusicTime);
            }
            catch (Exception e)
            {
                Log.Error("music: cannot skip the track", e);
            }

            return next;
        }

        static string Encode(string name)
        {
            return string.IsNullOrEmpty(name) ? "MENU" : name;
        }

        /// <summary>
        /// Printable form of a track name, for logs and the overlay.
        ///
        /// Track names come from the WAD as CP1251 ("Конец"), so they are
        /// transliterated the same way the file names are: the overlay then shows
        /// "KONEC" instead of a row of question marks.
        /// </summary>
        public static string Describe(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";

            bool ascii = true;
            for (int i = 0; i < name.Length; ++i)
                if (name[i] < 0x20 || name[i] > 0x7E) { ascii = false; break; }

            if (ascii) return name;

            return Transliterate(name);
        }

        public static string TrackPath(string name)
        {
            string resolved = ResolveTrackFile(name);
            return resolved ?? Path.Combine(Path.Combine(Program.DataDirectory, "Music"), name ?? "");
        }

        public static string Current { get { return current; } }

        /// <summary>True when a module is loaded and playing (F2 overlay).</summary>
        public static bool IsPlaying
        {
            get { lock (sync) { return player != null && !player.Finished; } }
        }

        /// <summary>
        /// Voices sounding right now, and how many the module has.
        ///
        /// A tracker voice is one note. If the count exceeds the channel count the
        /// same module is being played twice into one buffer, which is exactly what
        /// "the music sounds doubled" means.
        /// </summary>
        public static int ActiveVoices
        {
            get { lock (sync) { return player == null ? 0 : player.ActiveVoices; } }
        }

        /// <summary>Channels in the loaded module.</summary>
        public static int ChannelCount
        {
            get { lock (sync) { return module == null ? 0 : module.ChannelCount; } }
        }

        public static string TrackName
        {
            get { lock (sync) { return module != null ? module.Name : ""; } }
        }

        /// <summary>Where the current track came from: "file" or "wad".</summary>
        public static string Source
        {
            get
            {
                lock (sync)
                {
                    if (module == null) return "none";
                    return ResolveTrackFile(current) != null ? "file" : "wad";
                }
            }
        }
    }
}
