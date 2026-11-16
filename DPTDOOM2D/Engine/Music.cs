using System;
using System.IO;
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

                // Render into the shared accumulator with the music's own gain.
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

        /// <summary>F_loadmus: resolve the track file; missing tracks are not fatal.</summary>
        public static void Load(string name)
        {
            if (Disabled) return;

            lock (sync)
            {
                current = name;
                player = null;
                module = null;

                string path = TrackPath(name);
                if (!File.Exists(path)) return;

                try
                {
                    module = XmModule.Load(path);
                    if (module != null) player = new XmPlayer(module);
                }
                catch
                {
                    module = null;
                    player = null;
                }
            }
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

            count = time * 60 * 1000 / Glob.Delay;
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
            if (count > 0 && --count == 0)
            {
                string next = Wad.NextMus(Encode(current));
                Load(next);
                Start(Game.Game.MusicTime);
            }
        }

        static string Encode(string name)
        {
            return name ?? "MENU";
        }

        public static string TrackPath(string name)
        {
            string dir = Path.Combine(Program.DataDirectory, "Music");
            return Path.Combine(dir, name ?? "");
        }

        public static string Current { get { return current; } }

        /// <summary>True when a module is loaded and playing (F2 overlay).</summary>
        public static bool IsPlaying
        {
            get { lock (sync) { return player != null && !player.Finished; } }
        }

        public static string TrackName
        {
            get { lock (sync) { return module != null ? module.Name : ""; } }
        }
    }
}
