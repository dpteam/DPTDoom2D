using System;
using System.IO;

namespace Doom2D.Engine
{
    /// <summary>
    /// music.c — background music.
    ///
    /// Doom2D ships its tracks as FastTracker 2 (XM) modules next to the
    /// executable, and rembo handed them to SDL_mixer's mikmod backend. The
    /// replay engine is not ported yet, so this class keeps the whole
    /// load/start/stop/volume bookkeeping correct and stays silent: the game
    /// logic, the menu and the save files all behave as if music were playing.
    ///
    /// Porting it means writing an XM player (pattern effects + sample mixing)
    /// on top of DynamicSoundEffectInstance; see PORTING.md.
    /// </summary>
    public static class Music
    {
        public static int MusVol = 50;

        static string current;
        static int count;

        public static bool Disabled { get { return MusVol == 0; } }

        public static void Init()
        {
            count = 0;
        }

        /// <summary>F_loadmus: resolve the track file; missing tracks are not fatal.</summary>
        public static void Load(string name)
        {
            if (Disabled) return;
            current = name;
        }

        public static void Free()
        {
            Stop();
            current = null;
        }

        public static void Start(int time)
        {
            if (Disabled) return;
            count = time * 60 * 1000 / Glob.Delay;
        }

        public static void Stop()
        {
            count = 0;
        }

        public static void Volume(int v)
        {
            MusVol = v < 0 ? 0 : (v > 128 ? 128 : v);
            if (MusVol == 0) Stop();
        }

        public static void Update()
        {
            if (count > 0 && --count == 0) Start(Doom2D.Game.Game.MusicTime);
        }

        public static string TrackPath(string name)
        {
            string dir = Path.Combine(Program.DataDirectory, "Music");
            return Path.Combine(dir, name ?? "");
        }

        public static string Current { get { return current; } }
    }
}
