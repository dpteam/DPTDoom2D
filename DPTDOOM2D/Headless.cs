using System;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Audio;
using DPTDOOM2D.Game;
using DGame = DPTDOOM2D.Game.Game;

namespace DPTDOOM2D
{
    /// <summary>
    /// A window-free run of the real game loop, used to verify the port.
    ///
    /// Everything except the window and audio is the shipping code path: the same
    /// WAD loader, the same 20 Hz tick, the same software renderer. The audio
    /// calls are inert because Sound is never initialised.
    /// </summary>
    public static class Headless
    {
        /// <summary>
        /// Fixed seed for every headless run: a given command always produces the
        /// same frames, so the documentation images and the CI checks are stable.
        /// </summary>
        public const int Seed = 20261117;
        /// <summary>--simulate &lt;ticks&gt; &lt;out.bmp&gt; [map] [keys] [2p|dm]</summary>
        public static int Run(int ticks, string output, int map, string keys, bool twoPl = false, bool dm = false)
        {
            Glob.Reset(Seed);
            Vga.Init(320, 200);
            Vga.SetRect(0, 320, 0, 200);

            DGame.Map = map;
            DGame.TwoPl = twoPl || dm;
            DGame.Dm = dm;
            DGame.State = DGame.GsGame;

            DGame.Init();
            World.LoadLightningSounds();

            // The menu calls PL_reset() before the first G_start(); a headless run
            // has to do the same, or the spawn is not treated as a respawn and the
            // player keeps the zeroed fields it started life with.
            Player.Reset();

            // Start() loads the map and spawns everything through the normal path.
            DGame.Start();

            ApplyKeyScript(keys);
            ApplyReleaseScript(keys);

            for (int i = 0; i < ticks; ++i)
            {
                Kbd.UpdateScripted(i);
                // Menu.Act() consumes LastKey; KeyProc() needs to see it first, exactly
                // as Doom2DGame.Update() does for the real window.
                if (Kbd.LastKey != 0) Menu.KeyProc(Kbd.LastKey);
                DGame.Act();
            }

            Vga.Clr(0, 320, 0, 200, 0);
            DGame.Draw();

            Program.WriteBmp(output, 320, 200);
            Console.WriteLine(string.Format(
                "map {0}: {1} ticks, player at {2},{3} life {4} ammo {5} wpn {6}, state {7}",
                map, ticks, DGame.Pl1.O.X, DGame.Pl1.O.Y, DGame.Pl1.Life, DGame.Pl1.Ammo, DGame.Pl1.Wpn, DGame.State));
            return 0;
        }

        /// <summary>--shell &lt;ticks&gt; &lt;out.bmp&gt; [keys]: the front end, from the title screen.</summary>
        public static int Shell(int ticks, string output, string keys)
        {
            Glob.Reset(Seed);
            Vga.Init(320, 200);
            Vga.SetRect(0, 320, 0, 200);

            DGame.State = DGame.GsTitle;
            DGame.Init();
            World.LoadLightningSounds();

            ApplyKeyScript(keys);
            ApplyReleaseScript(keys);

            for (int i = 0; i < ticks; ++i)
            {
                Kbd.UpdateScripted(i);
                if (Kbd.LastKey != 0) Menu.KeyProc(Kbd.LastKey);
                DGame.Act();
                if (Environment.GetEnvironmentVariable("D2D_TRACE") != null)
                    Console.WriteLine("t{0}: state={1} menu={2} map={3} life={4}",
                        i, DGame.State, Menu.Open, DGame.Map, DGame.Pl1.Life);
            }

            Vga.Clr(0, 320, 0, 200, 0);
            DGame.Draw();
            Program.WriteBmp(output, 320, 200);
            Console.WriteLine(string.Format("shell: {0} ticks, state {1}, menu open {2}",
                ticks, DGame.State, Menu.Open));
            return 0;
        }

        /// <summary>--saveload &lt;ticks&gt;: play, save, quit, load, verify the state survives.</summary>
        public static int SaveLoad(int ticks)
        {
            Glob.Reset(Seed);
            Vga.Init(320, 200);
            Vga.SetRect(0, 320, 0, 200);

            DGame.Map = 1;
            DGame.State = DGame.GsGame;
            DGame.Init();
            Player.Reset();
            DGame.Start();

            Kbd.Script(2, Kbd.SDLK_KP6);
            Kbd.Script(20, Kbd.SDLK_PAGEDOWN);
            Kbd.Script(60, Kbd.SDLK_PAGEDOWN);

            for (int i = 0; i < ticks; ++i)
            {
                Kbd.UpdateScripted(i);
                DGame.Act();
            }

            int x = DGame.Pl1.O.X, ammo = DGame.Pl1.Ammo, life = DGame.Pl1.Life;
            int kills = DGame.Pl1.Kills;

            DGame.SaveGame(0, "port test");
            Console.WriteLine(string.Format("saved: x={0} ammo={1} life={2} kills={3}", x, ammo, life, kills));

            // Wipe the world, then load it back.
            DGame.Map = 7;
            DGame.Pl1.O.X = 0; DGame.Pl1.O.Y = 0;
            DGame.Pl1.Ammo = 0; DGame.Pl1.Life = 1; DGame.Pl1.Kills = 0;

            bool ok = DGame.LoadGame(0);
            Console.WriteLine(string.Format("loaded: ok={0} map={1} x={2} ammo={3} life={4} kills={5}",
                ok, DGame.Map, DGame.Pl1.O.X, DGame.Pl1.Ammo, DGame.Pl1.Life, DGame.Pl1.Kills));

            bool match = ok && DGame.Map == 1 && DGame.Pl1.O.X == x && DGame.Pl1.Ammo == ammo
                && DGame.Pl1.Life == life && DGame.Pl1.Kills == kills;
            Console.WriteLine(match ? "saveload: OK" : "saveload: MISMATCH");
            return match ? 0 : 1;
        }

        /// <summary>--levelend &lt;map&gt;: stand on the exit switch and confirm the map changes.</summary>
        public static int LevelEnd(int map)
        {
            Glob.Reset(Seed);
            Vga.Init(320, 200);
            Vga.SetRect(0, 320, 0, 200);

            DGame.Map = map;
            DGame.State = DGame.GsGame;
            DGame.Init();
            Player.Reset();
            DGame.Start();

            // Walk to the exit switch rather than hunting for it: this is a check of
            // the switch/door/intermission code, not of the level design.
            Switches.PlacePlayerOnExit(DGame.Pl1);

            for (int i = 0; i < 600; ++i)
            {
                if (i % 2 == 0) Kbd.Script(i, Kbd.SDLK_KP8); else Kbd.Script(i, Kbd.SDLK_KP6);
                Kbd.UpdateScripted(i);
                DGame.Act();
                if (DGame.State != DGame.GsGame)
                {
                    // One more tick: the original blanks a single frame while the
                    // transition flag is set, then draws the intermission screen.
                    Kbd.UpdateScripted(i + 1);
                    DGame.Act();

                    Console.WriteLine(string.Format("map {0}: exit fired at tick {1}, state {2}, next map {3}",
                        map, i, DGame.State, DGame.Map));
                    Vga.Clr(0, 320, 0, 200, 0);
                    DGame.Draw();
                    Program.WriteBmp("inter.bmp", 320, 200);
                    return 0;
                }
            }
            Console.WriteLine(string.Format("map {0}: no exit after 600 ticks", map));
            return 1;
        }

        /// <summary>--switches &lt;map&gt;: every switch the map defines.</summary>
        public static int SwitchList(int map)
        {
            Glob.Reset(Seed);
            Vga.Init(320, 200);
            Vga.SetRect(0, 320, 0, 200);

            DGame.Map = map;
            DGame.State = DGame.GsGame;
            DGame.Init();
            Player.Reset();
            DGame.Start();

            Switches.Dump();
            return 0;
        }

        /// <summary>--music &lt;track&gt;: parse a track and render a second of it, for the audio path.</summary>
        public static int MusicCheck(string track)
        {
            Vga.Init(320, 200);
            Glob.Reset(Seed);

            string path = Music.TrackPath(track);
            if (!System.IO.File.Exists(path))
            {
                Console.WriteLine("music: " + path + " not found");
                return 1;
            }

            var module = Audio.XmModule.Load(path);
            if (module == null)
            {
                Console.WriteLine("music: " + track + " is not a valid XM module");
                return 1;
            }

            var player = new Audio.XmPlayer(module);
            var buffer = new float[AudioOut.Channels * 512];
            int frames = 0;
            double sum = 0;
            int peak = 0;

            if (Environment.GetEnvironmentVariable("D2D_MUSIC_DEBUG") != null)
            {
                Console.WriteLine("length=" + module.Length + " orders=" + module.Orders[0] + "," + module.Orders[1]
                    + " speed=" + module.InitialSpeed + " bpm=" + module.InitialBpm
                    + " linear=" + module.LinearFrequency);
                for (int i = 0; i < module.Instruments.Length; ++i)
                {
                    var ins = module.Instruments[i];
                    int points = ins.Samples.Length > 0 ? ins.Samples[0].Data.Length : 0;
                    Console.WriteLine("  inst " + i + ": samples=" + ins.Samples.Length + " points=" + points
                        + " keymap0=" + ins.KeyMap[0]);
                }
                if (module.Patterns.Length > 0)
                {
                    var pat = module.Patterns[0];
                    Console.WriteLine("  pattern0 rows=" + pat.Rows + " cells=" + pat.Note.Length);
                    for (int c = 0; c < Math.Min(12, pat.Note.Length); ++c)
                        Console.Write("    cell " + c + ": note=" + pat.Note[c] + " ins=" + pat.Instrument[c]
                            + " vol=" + pat.Volume[c] + " fx=" + pat.Effect[c] + "/" + pat.Parameter[c] + "\n");
                }
            }

            for (int block = 0; block < 43; ++block) // ~1 second at 22050 Hz
            {
                Array.Clear(buffer, 0, buffer.Length);
                player.Render(buffer, 512);
                for (int i = 0; i < buffer.Length; ++i)
                {
                    double value = Math.Abs(buffer[i]);
                    sum += value;
                    if (value > peak / 32768.0) peak = (int)(value * 32768);
                }
                frames += 512;
            }

            Console.WriteLine(string.Format(
                "music {0}: '{1}', {2} ch, {3} patterns, {4} instruments, avg {5:0.0000}, peak {6}, voices {7}",
                track, module.Name, module.ChannelCount, module.Patterns.Length,
                module.Instruments.Length, sum / (frames * AudioOut.Channels), peak,
                player.ActiveVoices));

            return peak > 0 ? 0 : 1;
        }

        /// <summary>Keys are scripted as "tick:key,tick:key" so a run is reproducible.</summary>
        static void ApplyKeyScript(string script)
        {
            if (string.IsNullOrEmpty(script)) return;

            foreach (string part in script.Split(','))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                int tick;
                if (!int.TryParse(part.Substring(0, colon), out tick)) continue;
                string name = part.Substring(colon + 1).Trim();

                int sym = name.Length == 1 ? name.ToLowerInvariant()[0] : KeyByName(name);
                Kbd.Script(tick, sym);
            }
        }

        /// <summary>Releases are written as "tick:-key" so a key press can be repeated.</summary>
        static void ApplyReleaseScript(string script)
        {
            if (string.IsNullOrEmpty(script)) return;

            foreach (string part in script.Split(','))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                int tick;
                if (!int.TryParse(part.Substring(0, colon), out tick)) continue;
                string name = part.Substring(colon + 1).Trim();
                if (name.Length < 2 || name[0] != '-') continue;
                name = name.Substring(1);

                int sym = name.Length == 1 ? name.ToLowerInvariant()[0] : KeyByName(name);
                Kbd.ScriptRelease(tick, sym);
            }
        }

        /// <summary>Names map to the default bindings from main.c, not to the arrow keys.</summary>
        static int KeyByName(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "left": return Kbd.SDLK_KP4;
                case "right": return Kbd.SDLK_KP6;
                case "up": return Kbd.SDLK_KP8;
                case "down": return Kbd.SDLK_KP5;
                case "fire": return Kbd.SDLK_PAGEDOWN;
                case "jump": return Kbd.SDLK_DELETE;
                case "use": return Kbd.SDLK_KP8;
                case "next": return Kbd.SDLK_END;
                case "prev": return Kbd.SDLK_HOME;
                case "space": return Kbd.SDLK_SPACE;
                case "enter": return Kbd.SDLK_RETURN;
                case "esc": return Kbd.SDLK_ESCAPE;
                case "y": return 'y';
                case "n": return 'n';
                default: return 0;
            }
        }

    }
}
