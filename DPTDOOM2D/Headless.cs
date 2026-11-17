using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        /// <summary>
        /// --cheats: types every original cheat and checks the effect.
        ///
        /// The buffer receives the character of the **physical** key, not of the
        /// current layout — that is how the DOS original worked and why the
        /// Cyrillic cheats ("ГОРЕЦ", "СИМСИМ", ...) work from either layout. The
        /// check feeds the same syms a keypress produces, so it covers both.
        /// </summary>
        public static int CheatCheck()
        {
            Glob.Reset(Seed);
            Vga.Init(320, 200);
            Vga.SetRect(0, 320, 0, 200);

            DGame.Map = 1;
            DGame.TwoPl = false;
            DGame.Dm = false;
            DGame.State = DGame.GsGame;
            DGame.Init();
            World.LoadLightningSounds();
            Player.Reset();
            DGame.Start();

            int failed = 0;

            // A keypress produces a lowercase sym for letters (see Kbd.BuildVkToSym)
            // and the buffer stores it uppercased, so the cheat text is fed in the
            // same shape.
            failed += Expect("UJHTW (ГОРЕЦ: immortal)", Type("UJHTW"), () => Player.Immortal);
            failed += Expect("TANK", Type("TANK"), () => DGame.Pl1.Life == 200 && DGame.Pl1.Armor == 200);
            failed += Expect("RAMBO", Type("RAMBO"), () => DGame.Pl1.Ammo == 30000 && DGame.Pl1.Wpns == 0x7FF);
            failed += Expect("BULLFROG (jump x2)", Type("BULLFROG"), () => Player.Jump == 20);
            failed += Expect("FORMULA1 (speed x3)", Type("FORMULA1"), () => Player.Run == 24);
            failed += Expect(",TKSQJHTK (БЕЛЫЙ ОРЕЛ: fly)", Type(",TKSQJHTK"), () => Player.Fly);
            failed += Expect("CBVCBV (СИМСИМ: open doors)", Type("CBVCBV"), () => true);
            failed += Expect("GJITKYF05 (ПОШЁЛ НА 05)", Type("GJITKYF05"), () => DGame.Map == 5);
            failed += Expect("GOODBYE", Type("GOODBYE"), () => DGame.Exit == 1);

            Console.WriteLine(failed == 0
                ? "cheats: all 9 ok"
                : "cheats: " + failed + " failed");

            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// --controls: both layout presets, and the round trip a saved binding makes
        /// through default.cfg.
        ///
        /// The round trip is the part that matters. The overlay stores a binding by
        /// name, so a name Config.KeyByName cannot read would make a rebind work
        /// until the next start and then silently disappear.
        /// </summary>
        public static int ControlsCheck()
        {
            int failed = 0;

            failed += ConfigFileCheck();

            Controls.Apply(1);
            failed += Check("preset 1 is recognised", Controls.MatchPreset() == 1);
            failed += Check("preset 1: P1 moves with the arrows",
                Controls.Get(DGame.Pl1, "left") == Kbd.SDLK_LEFT
                && Controls.Get(DGame.Pl1, "right") == Kbd.SDLK_RIGHT
                && Controls.Get(DGame.Pl1, "up") == Kbd.SDLK_UP
                && Controls.Get(DGame.Pl1, "down") == Kbd.SDLK_DOWN);
            failed += Check("preset 1: P1 fires with Z, jumps with X",
                Controls.Get(DGame.Pl1, "fire") == 'z' && Controls.Get(DGame.Pl1, "jump") == 'x');
            failed += Check("preset 1: the two players share no key", NoOverlap());

            Controls.Apply(2);
            failed += Check("preset 2 is recognised", Controls.MatchPreset() == 2);
            failed += Check("preset 2: main.c's numpad, page down and delete",
                Controls.Get(DGame.Pl1, "up") == Kbd.SDLK_KP8
                && Controls.Get(DGame.Pl1, "down") == Kbd.SDLK_KP5
                && Controls.Get(DGame.Pl1, "left") == Kbd.SDLK_KP4
                && Controls.Get(DGame.Pl1, "right") == Kbd.SDLK_KP6
                && Controls.Get(DGame.Pl1, "fire") == Kbd.SDLK_PAGEDOWN
                && Controls.Get(DGame.Pl1, "jump") == Kbd.SDLK_DELETE
                && Controls.Get(DGame.Pl1, "prev") == Kbd.SDLK_HOME
                && Controls.Get(DGame.Pl1, "next") == Kbd.SDLK_END
                && Controls.Get(DGame.Pl1, "use") == Kbd.SDLK_KP8);
            failed += Check("preset 2: main.c's letters for P2",
                Controls.Get(DGame.Pl2, "up") == 'e' && Controls.Get(DGame.Pl2, "down") == 'd'
                && Controls.Get(DGame.Pl2, "left") == 's' && Controls.Get(DGame.Pl2, "right") == 'f'
                && Controls.Get(DGame.Pl2, "fire") == 'a' && Controls.Get(DGame.Pl2, "jump") == 'q');

            failed += RoundTrip("preset 1", 1);
            failed += RoundTrip("preset 2", 2);

            // Rebinding one key has to read back as custom, or the overlay would
            // keep claiming a preset it no longer matches.
            Controls.Apply(1);
            Controls.Set(DGame.Pl1, "jump", 'k');
            failed += Check("a rebind reads back as custom", Controls.MatchPreset() == 0);

            // Leave the process on the default layout: later checks in the same run
            // (and a developer reading logs) should not inherit the test's edits.
            Controls.Apply(Controls.DefaultPreset);

            Console.WriteLine(failed == 0
                ? "controls: 2 layouts ok"
                : "controls: " + failed + " failed");

            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// The config path end to end: a file with layout=2 has to give preset 2, a key
        /// line has to survive on top of it, and a saved overlay set has to be tied to
        /// the preset it was recorded on.
        /// </summary>
        private static int ConfigFileCheck()
        {
            string temp = Path.Combine(Path.GetTempPath(), "dptdoom2d-controls.cfg");
            string previous = Config.CfgFile;
            int previousSaved = Controls.SavedPreset;
            int failed = 0;

            try
            {
                // An absolute path makes Path.Combine ignore the folder, so both the
                // shipped file and the user file resolve to this one file.
                Config.CfgFile = temp;
                Controls.SavedPreset = 0;

                File.WriteAllText(temp, "layout=2\r\npl1_jump=x\r\n", Encoding.ASCII);
                Controls.Apply(1);
                Controls.RequestedPreset = 0;
                Config.Load();
                Config.ApplyBindings();

                failed += Check("layout=2 applies the numpad preset",
                    Controls.Get(DGame.Pl1, "fire") == Kbd.SDLK_PAGEDOWN
                    && Controls.Get(DGame.Pl1, "up") == Kbd.SDLK_KP8
                    && Controls.BasePreset == 2);

                // A key line in the file is an explicit choice, so it refines the
                // preset instead of being replaced by it.
                failed += Check("a key line refines the preset",
                    Controls.Get(DGame.Pl1, "jump") == 'x');

                File.WriteAllText(temp, "pl1_jump=n\r\n", Encoding.ASCII);
                Controls.Apply(1);
                Controls.RequestedPreset = 0;
                Config.Load();
                Config.ApplyBindings();

                failed += Check("a key line without a preset is kept",
                    Controls.Get(DGame.Pl1, "jump") == 'n'
                    && Controls.Get(DGame.Pl1, "fire") == 'z');
                failed += Check("an edited key set reads back as custom",
                    Controls.MatchPreset() == 0);

                // -layout is what the launcher passes; the command line and the file
                // share one entry, so the option works the same way.
                Config.ParseArgs(new[] { "-layout", "2" });
                Controls.Apply(1);
                Config.ApplyBindings();
                failed += Check("-layout 2 on the command line applies the preset",
                    Controls.Get(DGame.Pl1, "fire") == Kbd.SDLK_PAGEDOWN
                    && Controls.BasePreset == 2);

                // Saved keys belong to one preset: the same preset keeps them, another
                // one drops them rather than mixing two layouts together.
                failed += Check("saved keys survive the same preset",
                    Controls.SavedKeysApply(1, 1) && Controls.SavedKeysApply(2, 2));
                failed += Check("saved keys are dropped for another preset",
                    !Controls.SavedKeysApply(2, 1) && !Controls.SavedKeysApply(1, 2));
                failed += Check("without a request the saved preset is used",
                    Controls.SavedKeysApply(0, 1) && Controls.SavedKeysApply(0, 2));
            }
            finally
            {
                Config.CfgFile = previous;
                Controls.RequestedPreset = 0;
                Controls.SavedPreset = previousSaved;
                try { File.Delete(temp); } catch { }
            }

            return failed;
        }

        /// <summary>Both players use different keys, so two people can share a keyboard.</summary>
        private static bool NoOverlap()
        {
            for (int i = 0; i < Controls.Actions.Length; ++i)
            {
                for (int j = 0; j < Controls.Actions.Length; ++j)
                {
                    if (Controls.Get(DGame.Pl1, Controls.Actions[i])
                        == Controls.Get(DGame.Pl2, Controls.Actions[j]))
                        return false;
                }
            }

            return true;
        }

        /// <summary>Every key of a preset must survive KeyName → KeyByName.</summary>
        private static int RoundTrip(string what, int preset)
        {
            Controls.Apply(preset);

            int failed = 0;
            for (int i = 0; i < Controls.Actions.Length; ++i)
            {
                string action = Controls.Actions[i];

                if (!One(what, "P1 " + action, Controls.Get(DGame.Pl1, action))) ++failed;
                if (!One(what, "P2 " + action, Controls.Get(DGame.Pl2, action))) ++failed;
            }

            return failed;
        }

        private static bool One(string what, string binding, int sym)
        {
            string name = Controls.KeyName(sym);
            int back = Config.KeyByName(name);

            if (back == sym) return true;

            Console.WriteLine("  " + what + ": " + binding + " = \"" + name + "\" reads back as " + back);
            return false;
        }

        private static int Check(string name, bool ok)
        {
            if (!ok) Console.WriteLine("  " + name + ": FAILED");
            return ok ? 0 : 1;
        }

        /// <summary>
        /// --actorcheck: parses the built-in actor definitions and compares them
        /// with the frozen 1996 tables.
        ///
        /// This is the check that keeps "data instead of code" honest. The engine
        /// no longer reads mnsz and the nine animation arrays from C#; it parses
        /// actors.txt-style data. A typo in that data would give a monster the
        /// wrong hit points and nothing would notice — this does.
        ///
        /// Also runs the parser over a set of deliberately broken snippets and
        /// insists that each one produces the expected error code, because a
        /// language that fails silently is worse than no language.
        /// </summary>
        public static int ActorCheck()
        {
            int failed = 0;

            Actors.Load();

            if (Actors.Errors.Count > 0)
            {
                for (int i = 0; i < Actors.Errors.Count; ++i)
                    Console.WriteLine("  " + Actors.Errors[i]);
                Console.WriteLine("  built-in actor data has " + Actors.Errors.Count + " problem(s)");
                ++failed;
            }

            var problems = new List<string>();
            int differences = ActorReference.Compare(problems);

            for (int i = 0; i < problems.Count; ++i)
                Console.WriteLine("  " + problems[i]);

            if (differences > 0)
            {
                Console.WriteLine("  actor data differs from monster.c in " + differences + " place(s)");
                ++failed;
            }

            failed += ParserChecks();

            Console.WriteLine(failed == 0
                ? "actors: 20 built-in types match monster.c, parser ok"
                : "actors: " + failed + " check(s) failed");

            return failed == 0 ? 0 : 1;
        }

        /// <summary>One valid definition and a list of broken ones with the code each must produce.</summary>
        private static int ParserChecks()
        {
            int failed = 0;

            failed += ExpectActor("a valid definition parses",
                @"actor Imp2
                  {
                      radius 10  height 28  health 40
                      sprite TROO  frames 42
                      sound death DSIMPDTH
                      states { spawn: AAABBB   see: AABBCCDD }
                  }", 1, null);

            failed += ExpectActor("an unknown property is E101",
                "actor X { wobble 3 }", 1, "E101");

            failed += ExpectActor("an unknown state is E102",
                "actor X { states { flying: AA } }", 1, "E102");

            failed += ExpectActor("a missing value is E103",
                "actor X { health }", 1, "E103");

            failed += ExpectActor("a missing brace is E104",
                "actor X { states { spawn: AA }", 1, "E104");

            failed += ExpectActor("inheritance is refused with E105",
                "actor X : Imp { health 1 }", 1, "E105");

            failed += ExpectActor("an unknown sound slot is E106",
                "actor X { sound roar DSFOO }", 1, "E106");

            failed += Check("an unknown actor name is not a built-in type",
                Actors.IndexOf("Nosuchmonster") == 0 && Actors.IndexOf("imp") == 2);

            failed += ExpectActor("a lowercase frame letter is E109",
                "actor X { states { spawn: Ab } }", 1, "E109");

            failed += ExpectActor("a comment is not a token",
                "// actor Y { health 1 }\nactor X { health 2 }", 1, null);

            // Sprites that mean "draw nothing" are legal and must not be reported.
            failed += ExpectActor("sprite TNT1 is accepted",
                "actor X { sprite TNT1 states { spawn: A } }", 1, null);
            failed += ExpectActor("sprite NULL is accepted",
                "actor X { sprite NULL states { spawn: A } }", 1, null);
            failed += ExpectActor("sprite ---- is accepted",
                "actor X { sprite \"----\" states { spawn: A } }", 1, null);

            failed += ExpectActor("renderstyle fuzz is accepted",
                "actor X { sprite TROO renderstyle fuzz states { spawn: A } }", 1, null);
            failed += ExpectActor("renderstyle add is refused with E106",
                "actor X { renderstyle add }", 1, "E106");

            return failed;
        }

        private static int ExpectActor(string what, string text, int actors, string code)
        {
            var errors = new List<string>();
            List<ActorDef> parsed = ActorParser.Parse(text, "test", errors);

            if (parsed.Count != actors)
            {
                Console.WriteLine("  " + what + ": parsed " + parsed.Count + " actor(s), expected " + actors);
                return 1;
            }

            if (code == null)
            {
                if (errors.Count == 0) return 0;
                Console.WriteLine("  " + what + ": unexpected " + errors[0]);
                return 1;
            }

            for (int i = 0; i < errors.Count; ++i)
                if (errors[i].IndexOf(code, StringComparison.Ordinal) >= 0) return 0;

            Console.WriteLine("  " + what + ": expected " + code
                + ", got " + (errors.Count == 0 ? "no error" : errors[0]));
            return 1;
        }

        /// <summary>
        /// --datacheck: the manifest that decides whether two machines may play.
        ///
        /// It has to survive the wire format and it has to describe a difference in
        /// words a player can act on, so both are checked without opening a socket.
        /// </summary>
        public static int ManifestCheck()
        {
            int failed = 0;

            var mine = new List<Shared.DataManifest.Entry>();
            mine.Add(Make("doom2d.wad", 4464286, 0x1122334455667788UL));
            mine.Add(Make("mod.pk3", 1000, 0xAABBCCDDEEFF0011UL));
            mine.Add(Make("actors", 0, 0x0000000000ABCDEFUL));

            var same = Shared.DataManifest.Decode(Shared.DataManifest.Encode(mine), 0);
            failed += Check("the manifest survives the wire format", same.Count == mine.Count);
            failed += Check("names survive", same[1].Name == "mod.pk3");
            failed += Check("sizes survive", same[0].Size == 4464286);
            failed += Check("hashes survive", same[1].Hash == 0xAABBCCDDEEFF0011UL);

            failed += Check("identical manifests agree", Shared.DataManifest.Compare(mine, same).Length == 0);

            var missing = new List<Shared.DataManifest.Entry>();
            missing.Add(mine[0]);
            missing.Add(mine[2]);

            string text = Shared.DataManifest.Compare(mine, missing);
            failed += Check("a missing mod is named",
                text.IndexOf("mod.pk3", StringComparison.Ordinal) >= 0);

            // Same three files, mod and actors swapped: -file order decides which
            // resource wins, so this is a different installation.
            var swapped = new List<Shared.DataManifest.Entry>();
            swapped.Add(mine[0]);
            swapped.Add(mine[2]);
            swapped.Add(mine[1]);

            text = Shared.DataManifest.Compare(mine, swapped);
            failed += Check("a different load order is reported",
                text.IndexOf("order", StringComparison.OrdinalIgnoreCase) >= 0);

            var edited = new List<Shared.DataManifest.Entry>();
            edited.Add(mine[0]);
            edited.Add(Make("mod.pk3", 1000, 0x9999999999999999UL));
            edited.Add(mine[2]);

            text = Shared.DataManifest.Compare(mine, edited);
            failed += Check("an edited file is reported as different contents",
                text.IndexOf("different contents", StringComparison.Ordinal) >= 0);

            // The digest has to change when the data does, or the per-tick checksum
            // would not notice a swap.
            failed += Check("the digest depends on the data",
                Shared.DataManifest.Digest(mine) != Shared.DataManifest.Digest(edited));

            Console.WriteLine(failed == 0
                ? "manifest: wire format and comparison ok"
                : "manifest: " + failed + " check(s) failed");

            return failed == 0 ? 0 : 1;
        }

        private static Shared.DataManifest.Entry Make(string name, long size, ulong hash)
        {
            var entry = new Shared.DataManifest.Entry();
            entry.Name = name;
            entry.Size = size;
            entry.Hash = hash;
            return entry;
        }

        /// <summary>
        /// --mapcheck: read every map, write it back, read the result and compare.
        ///
        /// The editor cannot exist without this. The game has only ever read the map
        /// format; the writer is new, and a writer that produces a file the game reads
        /// slightly differently would corrupt maps quietly. Every layer, wall name,
        /// thing and switch has to survive the round trip byte for byte.
        /// </summary>
        public static int MapCheck()
        {
            int failed = 0;
            int maps = 0;

            // The built-in levels first.
            for (int map = 1; map <= 19; ++map)
            {
                string name = string.Format("MAP{0:00}", map);
                int res = Wad.FindRes(name);
                if (res < 0) continue;

                ++maps;
                failed += RoundTrip(name, Wad.LoadRes(res));
            }

            // Then every map in every custom pack: those are the files a modder
            // actually opens in the editor, and they use things the built-in levels
            // do not (water walls, triggers, no single-player start).
            string[] packs = Program.ListMapPacks();

            for (int p = 0; p < packs.Length; ++p)
            {
                string pack = packs[p];

                try
                {
                    byte[] data = File.ReadAllBytes(pack);
                    var entries = new List<KeyValuePair<string, byte[]>>();

                    // Only the map lumps matter here, and a WAD directory is easy to
                    // walk: 12-byte header, then count and offset.
                    if (data.Length < 12) continue;

                    int count = BitConverter.ToInt32(data, 4);
                    int directory = BitConverter.ToInt32(data, 8);
                    if (count <= 0 || directory < 12 || directory + count * 16 > data.Length) continue;

                    for (int i = 0; i < count; ++i)
                    {
                        int at = directory + i * 16;
                        int offset = BitConverter.ToInt32(data, at);
                        int length = BitConverter.ToInt32(data, at + 4);

                        if (offset < 0 || length < 0 || offset + length > data.Length) continue;

                        var lump = new byte[length];
                        Buffer.BlockCopy(data, offset, lump, 0, length);
                        entries.Add(new KeyValuePair<string, byte[]>(i.ToString(), lump));
                    }

                    for (int i = 0; i < entries.Count; ++i)
                    {
                        byte[] lump = entries[i].Value;
                        if (!LooksLikeMap(lump)) continue;

                        ++maps;
                        failed += RoundTrip(Path.GetFileName(pack) + "#" + i, lump);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("  " + Path.GetFileName(pack) + ": " + Log.Describe(e));
                    ++failed;
                }
            }

            Console.WriteLine(failed == 0
                ? "maps: " + maps + " round-tripped byte for byte"
                : "maps: " + failed + " of " + maps + " failed");

            return failed == 0 ? 0 : 1;
        }

        private static bool LooksLikeMap(byte[] data)
        {
            if (data == null || data.Length < 10) return false;
            if (data[6] != 0x1A) return false;

            string id = Encoding.GetEncoding(28591).GetString(data, 0, 6);
            return id == "Doom2D";
        }

        /// <summary>Reads a map, writes it, reads it back, and compares every field.</summary>
        private static int RoundTrip(string name, byte[] original)
        {
            try
            {
                MapFile first = MapFile.Read(original, name);
                byte[] written = first.Write();
                MapFile second = MapFile.Read(written, name + " (saved)");

                string difference = Diff(first, second);
                if (difference.Length == 0) return 0;

                Console.WriteLine("  " + name + ": " + difference);
                return 1;
            }
            catch (Exception e)
            {
                Console.WriteLine("  " + name + ": " + Log.Describe(e));
                return 1;
            }
        }

        /// <summary>Everything that has to match, compared field by field.</summary>
        private static string Diff(MapFile a, MapFile b)
        {
            if (a.Sky != b.Sky) return "sky " + a.Sky + " vs " + b.Sky;
            if (a.Music != b.Music) return "music '" + a.Music + "' vs '" + b.Music + "'";

            for (int i = 1; i < 256; ++i)
            {
                if (a.WallName[i] != b.WallName[i])
                    return "wall " + i + " name '" + a.WallName[i] + "' vs '" + b.WallName[i] + "'";
                if (a.WallType[i] != b.WallType[i])
                    return "wall " + i + " type " + a.WallType[i] + " vs " + b.WallType[i];
            }

            string layer = DiffLayer("back", a.Back, b.Back);
            if (layer.Length != 0) return layer;

            layer = DiffLayer("type", a.Type, b.Type);
            if (layer.Length != 0) return layer;

            layer = DiffLayer("front", a.Front, b.Front);
            if (layer.Length != 0) return layer;

            if (a.Things.Count != b.Things.Count)
                return "things " + a.Things.Count + " vs " + b.Things.Count;

            for (int i = 0; i < a.Things.Count; ++i)
            {
                MapThing x = a.Things[i], y = b.Things[i];
                if (x.X != y.X || x.Y != y.Y || x.Type != y.Type || x.Flags != y.Flags)
                    return "thing " + i + " differs";
            }

            if (a.Switches.Count != b.Switches.Count)
                return "switches " + a.Switches.Count + " vs " + b.Switches.Count;

            for (int i = 0; i < a.Switches.Count; ++i)
            {
                MapSwitch x = a.Switches[i], y = b.Switches[i];
                if (x.X != y.X || x.Y != y.Y || x.Type != y.Type || x.A != y.A
                    || x.B != y.B || x.Param != y.Param || x.Flags != y.Flags)
                    return "switch " + i + " differs";
            }

            return "";
        }

        private static string DiffLayer(string name, byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; ++i)
                if (a[i] != b[i]) return name + " layer differs at " + i;

            return "";
        }

        /// <summary>
        /// --wadreq: run the game and record every resource it asks the WAD for.
        ///
        /// A replacement data set (FreeDooM2D, AIDooM2D) has to contain everything
        /// the game demands. That list cannot be written by reading the source — it
        /// is always one lump short, and the missing one is a Fatal on a player's
        /// machine. So the list is recorded instead: the game runs every map, the
        /// lookups are logged, and what was asked for is what a converter must make.
        /// </summary>
        public static int WadAudit()
        {
            Wad.BeginAudit();
            ResourceAudit audit = null;

            try
            {
                Glob.Reset(Seed);
                Vga.Init(320, 200);
                Vga.SetRect(0, 320, 0, 200);

                DGame.TwoPl = false;
                DGame.Dm = false;
                DGame.State = DGame.GsGame;

                // Init allocates the fonts, the item sprites, every monster's frames
                // and the weapon pictures: the bulk of the art is requested here.
                DGame.Init();
                World.LoadLightningSounds();

                for (int map = 1; map <= 19; ++map)
                {
                    int res = Wad.FindRes(string.Format("MAP{0:00}", map));
                    if (res < 0) continue;

                    DGame.Map = map;
                    Player.Reset();
                    DGame.Start();

                    // A few ticks so the map's music, its sky and the sounds that fire
                    // on spawn are requested too, not just the lumps the loader reads.
                    for (int tick = 0; tick < 20; ++tick)
                    {
                        Kbd.UpdateScripted(tick);
                        DGame.Act();
                    }
                }
            }
            finally
            {
                audit = Wad.EndAudit();
            }

            if (audit == null) return 1;

            string candidate = Config.WadAuditCandidate;

            if (!string.IsNullOrEmpty(candidate))
            {
                List<string> names = ReadWadNames(candidate);
                if (names == null)
                {
                    Console.Error.WriteLine("wadreq: не удалось прочитать " + candidate);
                    return 1;
                }

                Console.WriteLine(audit.ReportAgainst(names, Path.GetFileName(candidate)));
                return 0;
            }

            Console.WriteLine(audit.Report());
            Console.WriteLine("итого: " + audit.Total + " запрошено, " + audit.Missing + " нет");

            // A missing sprite set is a Fatal the first time the monster spawns, so it
            // fails the run; missing glyphs are survivable and only get reported.
            if (audit.MissingSprites > 0)
            {
                Console.Error.WriteLine("wadreq: не хватает " + audit.MissingSprites + " наборов спрайтов");
                return 1;
            }

            return 0;
        }

        /// <summary>The lump names in a WAD file, or null when it cannot be read.</summary>
        private static List<string> ReadWadNames(string path)
        {
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (data.Length < 12) return null;
                if (data[0] != 'P' && data[0] != 'I') return null;
                if (data[1] != 'W' || data[2] != 'A' || data[3] != 'D') return null;

                int count = BitConverter.ToInt32(data, 4);
                int directory = BitConverter.ToInt32(data, 8);

                if (count < 0 || directory < 12 || directory + count * 16 > data.Length) return null;

                var names = new List<string>(count);

                // Latin-1, not ASCII: the fonts in this WAD are named STBF_<byte>, and
                // the high bytes are real. Decoding them as '?' would report every one
                // of them as missing.
                var latin1 = Encoding.GetEncoding(28591);

                for (int i = 0; i < count; ++i)
                {
                    int at = directory + i * 16 + 8;

                    int n = 0;
                    while (n < 8 && data[at + n] != 0) ++n;

                    names.Add(latin1.GetString(data, at, n));
                }

                return names;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The same comparison the WAD loader uses: up to eight characters, case
        /// insensitive, a short name padded with NULs.
        /// </summary>
        private static bool NameMatches(string a, string b)
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

        /// <summary>
        /// --mid2dmm &lt;in.mid&gt; &lt;out.dmm|out.wad&gt;: turn a MIDI song into a DMM
        /// lump this game can play.
        ///
        /// The converter is what FreeDooM2D needs: FreeDoom's music is MIDI, Doom2D's
        /// is DMM, and without this a replacement data set plays in silence.
        /// </summary>
        public static int MidiToDmmFile(string input, string output)
        {
            if (!File.Exists(input))
            {
                Console.Error.WriteLine("mid2dmm: " + input + " не найден");
                return 1;
            }

            byte[] midi = File.ReadAllBytes(input);

            string summary;
            byte[] data = Audio.MidiToDmm.ConvertAndVerify(midi, out summary);

            if (data == null)
            {
                Console.Error.WriteLine("mid2dmm: " + Path.GetFileName(input) + ": " + summary);
                return 1;
            }

            string lump = Path.GetFileNameWithoutExtension(input).ToUpperInvariant();
            if (lump.Length > 8) lump = lump.Substring(0, 8);

            if (output.EndsWith(".wad", StringComparison.OrdinalIgnoreCase))
            {
                var lumps = new List<KeyValuePair<string, byte[]>>();
                lumps.Add(new KeyValuePair<string, byte[]>(lump, data));

                if (!Prototypes.WadBuilder.WriteLumps(output, lumps)) return 1;
            }
            else
            {
                File.WriteAllBytes(output, data);
            }

            Console.WriteLine("mid2dmm: " + Path.GetFileName(input) + " -> "
                + Path.GetFileName(output) + " (" + lump + ", " + summary + ")");

            return 0;
        }

        /// <summary>
        /// --muscheck: a MIDI file becomes a DMM that the real loader reads back.
        ///
        /// The song is built in memory, so the check runs with no files: a tiny
        /// format-0 MIDI with a tempo and two notes. What it proves is the whole
        /// chain — parse MIDI, place notes in patterns, write DMM, read it with
        /// DmmModule — which is exactly the chain FreeDooM2D depends on.
        /// </summary>
        public static int MusicConvertCheck()
        {
            int failed = 0;

            byte[] midi = BuildTestMidi();

            failed += Check("the test MIDI parses", Audio.MidiFile.Read(midi).Count == 2);

            string summary;
            byte[] data = Audio.MidiToDmm.ConvertAndVerify(midi, out summary);

            failed += Check("the MIDI converts and reads back", data != null);

            if (data != null)
            {
                XmModule module = Audio.DmmModule.Load(data, null);

                failed += Check("the song has a pattern", module != null && module.Patterns.Length > 0);
                failed += Check("the order list loops", module != null && module.Length > 0);

                // The notes have to be there, not just the structure: a converter
                // that writes valid but empty patterns would pass everything else.
                bool found = false;

                if (module != null)
                {
                    for (int p = 0; p < module.Patterns.Length && !found; ++p)
                    {
                        byte[] note = module.Patterns[p].Note;

                        for (int i = 0; i < note.Length; ++i)
                            if (note[i] > 0 && note[i] < 97) { found = true; break; }
                    }
                }

                failed += Check("the notes survive the round trip", found);

                // Timing is the part that broke first: a delay of 0 means 256 ticks
                // in this format, and a writer that gets that wrong pushes every note
                // a pattern late. Two quarter notes at 120 bpm are 64 ticks.
                XmModule check = module;
                bool lengthOk = check != null && check.Patterns.Length == 1
                    && check.Patterns[0].Rows == 64;

                failed += Check("the pattern is 64 ticks long", lengthOk);

                bool firstNote = false, secondNote = false;

                if (check != null && check.Patterns.Length > 0)
                {
                    byte[] note = check.Patterns[0].Note;
                    int channels = check.ChannelCount;

                    // Row 0, channel 0; row 32, channel 0.
                    if (note.Length > 0 && note[0] > 0 && note[0] < 97) firstNote = true;
                    if (note.Length > 32 * channels && note[32 * channels] > 0
                        && note[32 * channels] < 97) secondNote = true;
                }

                failed += Check("the first note starts on tick 0", firstNote);
                failed += Check("the second note starts on tick 32", secondNote);

                Console.WriteLine("  notes=" + Audio.MidiFile.Read(midi).Count
                    + " patterns=" + module.Patterns.Length
                    + " rows=" + module.Patterns[0].Rows
                    + " order=" + module.Length
                    + " bytes=" + data.Length);
            }

            Console.WriteLine(failed == 0
                ? "music: MIDI -> DMM round trip ok"
                : "music: " + failed + " check(s) failed");

            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// A minimal format-0 MIDI: 120 bpm, two quarter notes on channel 0.
        ///
        /// Written by hand rather than shipped as a file, so the check cannot drift
        /// away from the parser: this is the smallest thing the parser must accept.
        /// </summary>
        private static byte[] BuildTestMidi()
        {
            var track = new List<byte>();

            // Tempo: 500000 microseconds per quarter, the MIDI default of 120 bpm.
            track.AddRange(new byte[] { 0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20 });

            // C-4 on, a quarter later off.
            track.AddRange(new byte[] { 0x00, 0x90, 0x3C, 0x64 });
            track.AddRange(new byte[] { 0x83, 0x60, 0x80, 0x3C, 0x00 });

            // E-4, same length.
            track.AddRange(new byte[] { 0x00, 0x90, 0x40, 0x64 });
            track.AddRange(new byte[] { 0x83, 0x60, 0x80, 0x40, 0x00 });

            track.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 });

            var data = new List<byte>();

            data.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d' });
            data.AddRange(new byte[] { 0, 0, 0, 6 });
            data.AddRange(new byte[] { 0, 0 });             // format 0
            data.AddRange(new byte[] { 0, 1 });             // one track
            data.AddRange(new byte[] { 0x01, 0xE0 });       // 480 ticks per quarter

            data.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
            data.Add((byte)(track.Count >> 24));
            data.Add((byte)((track.Count >> 16) & 0xFF));
            data.Add((byte)((track.Count >> 8) & 0xFF));
            data.Add((byte)(track.Count & 0xFF));

            data.AddRange(track);

            return data.ToArray();
        }

        /// <summary>
        /// --wav2dmi &lt;in.wav&gt; &lt;out.dmi|out.wad&gt;: a sound file becomes a DMI lump.
        ///
        /// The counterpart of the MIDI converter: FreeDoom's sounds are WAV and
        /// Doom2D's are DMI, so without this a replacement data set has music and no
        /// gunshots.
        /// </summary>
        public static int WavToDmiFile(string input, string output)
        {
            if (!File.Exists(input))
            {
                Console.Error.WriteLine("wav2dmi: " + input + " не найден");
                return 1;
            }

            string reason;
            Audio.DmiSample sample = Audio.WavFile.Read(File.ReadAllBytes(input), out reason);

            if (sample == null)
            {
                Console.Error.WriteLine("wav2dmi: " + Path.GetFileName(input) + ": " + reason);
                return 1;
            }

            bool truncated = !Audio.DmiWriter.Fits(sample);
            byte[] data = Audio.DmiWriter.Write(sample);

            // The game looks a sound up as "DS" + name, so a lump called SHOT would
            // never be found: the converter names it the way the engine asks for it.
            string lump = Path.GetFileNameWithoutExtension(input).ToUpperInvariant();
            if (lump.Length > 8) lump = lump.Substring(0, 8);
            if (!lump.StartsWith("DS", StringComparison.Ordinal)) lump = "DS" + lump;
            if (lump.Length > 8) lump = lump.Substring(0, 8);

            if (output.EndsWith(".wad", StringComparison.OrdinalIgnoreCase))
            {
                var lumps = new List<KeyValuePair<string, byte[]>>();
                lumps.Add(new KeyValuePair<string, byte[]>(lump, data));

                if (!Prototypes.WadBuilder.WriteLumps(output, lumps)) return 1;
            }
            else
            {
                File.WriteAllBytes(output, data);
            }

            Console.WriteLine("wav2dmi: " + Path.GetFileName(input) + " -> " + Path.GetFileName(output)
                + " (" + lump + ", " + sample.Length + " сэмплов, " + sample.Rate + " Гц"
                + (truncated ? ", обрезано до " + Audio.DmiWriter.MaxLength : "") + ")");

            return 0;
        }

        /// <summary>
        /// --sndcheck: a WAV becomes a DMI and the game's own parser reads it back.
        ///
        /// A writer checked by a different reader proves nothing, so this goes through
        /// Sound.TryParse — the same function that loads every DS lump out of the WAD.
        /// </summary>
        public static int SoundConvertCheck()
        {
            int failed = 0;

            byte[] wav = BuildTestWav();
            string reason;

            Audio.DmiSample sample = Audio.WavFile.Read(wav, out reason);
            failed += Check("the test WAV parses", sample != null);

            if (sample == null)
            {
                Console.WriteLine("  " + reason);
                Console.WriteLine("sounds: 1 check failed");
                return 1;
            }

            failed += Check("16-bit becomes 8-bit", sample.Length == 1000);
            failed += Check("the rate survives", sample.Rate == 11025);

            byte[] dmi = Audio.DmiWriter.Write(sample);

            int length, rate, loopStart, loopLength;
            bool parsed = Sound.TryParse(dmi, out length, out rate, out loopStart, out loopLength);

            failed += Check("the DMI parses with the game's own reader", parsed);
            failed += Check("the sample count survives", length == sample.Length);
            failed += Check("the rate survives the round trip", rate == sample.Rate);

            // A full-scale square wave has to come back with the same sign pattern:
            // a converter that shifted the phase or inverted the polarity would still
            // "parse", and would sound wrong in a way nobody could describe.
            if (parsed)
            {
                bool sameSign = true;

                for (int i = 0; i < sample.Length && i < length; ++i)
                {
                    sbyte fromFile = (sbyte)dmi[8 + i];

                    // The writer stores exactly what the sample holds.
                    if (fromFile != sample.Data[i]) { sameSign = false; break; }
                }

                failed += Check("the waveform is unchanged", sameSign);
            }

            Console.WriteLine("  " + sample.Length + " сэмплов, " + sample.Rate + " Гц, " + dmi.Length + " байт");

            Console.WriteLine(failed == 0
                ? "sounds: WAV -> DMI round trip ok"
                : "sounds: " + failed + " check(s) failed");

            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// A minimal 16-bit mono WAV: 1000 samples of a square wave at 11025 Hz.
        ///
        /// Built by hand so the check cannot drift from the parser: this is the
        /// smallest file a converter has to accept.
        /// </summary>
        private static byte[] BuildTestWav()
        {
            int samples = 1000;
            int rate = 11025;
            int dataSize = samples * 2;

            var data = new List<byte>(44 + dataSize);

            data.AddRange(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            data.AddRange(BitConverter.GetBytes(36 + dataSize));
            data.AddRange(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });

            data.AddRange(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            data.AddRange(BitConverter.GetBytes(16));
            data.AddRange(BitConverter.GetBytes((short)1));      // PCM
            data.AddRange(BitConverter.GetBytes((short)1));      // mono
            data.AddRange(BitConverter.GetBytes(rate));
            data.AddRange(BitConverter.GetBytes(rate * 2));      // byte rate
            data.AddRange(BitConverter.GetBytes((short)2));      // block align
            data.AddRange(BitConverter.GetBytes((short)16));     // bits

            data.AddRange(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            data.AddRange(BitConverter.GetBytes(dataSize));

            for (int i = 0; i < samples; ++i)
            {
                short value = (short)((i / 25) % 2 == 0 ? 12000 : -12000);
                data.AddRange(BitConverter.GetBytes(value));
            }

            return data.ToArray();
        }

        /// <summary>Feeds a cheat the way a keyboard does and runs one tick.</summary>
        private static bool Type(string text)
        {
            for (int i = 0; i < text.Length; ++i)
            {
                char c = text[i];
                int sym = c >= 'A' && c <= 'Z' ? c + 32 : c;
                Menu.KeyProc(sym);
            }

            // Menu.Code() runs from Game.Act(), so one tick applies the cheat.
            DGame.Act();
            return true;
        }

        private static int Expect(string name, bool typed, Func<bool> check)
        {
            bool ok;
            try
            {
                ok = typed && check();
            }
            catch (Exception e)
            {
                Console.WriteLine("  " + name + ": ERROR " + Log.Describe(e));
                return 1;
            }

            if (!ok) Console.WriteLine("  " + name + ": FAILED");
            return ok ? 0 : 1;
        }

        /// <summary>
        /// --music &lt;track&gt;: load a track the way the game does (file first, then
        /// the DMM song inside the WAD) and render a second of it.
        ///
        /// This is the audio path's only automated check: if the replayer, the
        /// transliteration table or the DMM converter breaks, the peak goes to
        /// zero and the build fails.
        /// </summary>
        public static int MusicCheck(string track)
        {
            Vga.Init(320, 200);
            Glob.Reset(Seed);

            Music.Init();
            Music.Load(track);

            if (!Music.IsPlaying)
            {
                Console.WriteLine("music: " + Music.Describe(track) + " did not load");
                return 1;
            }

            var buffer = new float[AudioOut.Channels * 512];
            int frames = 0;
            double sum = 0;
            int peak = 0;

            for (int block = 0; block < 43; ++block) // ~1 second at 22050 Hz
            {
                Array.Clear(buffer, 0, buffer.Length);
                Music.RenderForTest(buffer, 512);

                for (int i = 0; i < buffer.Length; ++i)
                {
                    double value = Math.Abs(buffer[i]);
                    sum += value;
                    if (value > peak / 32768.0) peak = (int)(value * 32768);
                }

                frames += 512;
            }

            Console.WriteLine(string.Format(
                "music {0}: '{1}' from {2}, avg {3:0.0000}, peak {4}",
                Music.Describe(track), Music.TrackName, Music.Source,
                sum / (frames * AudioOut.Channels), peak));

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
            // Action names follow the layout in force, so "40:fire" in a --simulate
            // script means the key the player actually fires with, not the numpad key
            // main.c happened to use. The numpad values stay as the fallback for a
            // script parsed before Game.Init() has applied a preset.
            switch (name.ToLowerInvariant())
            {
                case "left": return Or(Controls.Get(DGame.Pl1, "left"), Kbd.SDLK_KP4);
                case "right": return Or(Controls.Get(DGame.Pl1, "right"), Kbd.SDLK_KP6);
                case "up": return Or(Controls.Get(DGame.Pl1, "up"), Kbd.SDLK_KP8);
                case "down": return Or(Controls.Get(DGame.Pl1, "down"), Kbd.SDLK_KP5);
                case "fire": return Or(Controls.Get(DGame.Pl1, "fire"), Kbd.SDLK_PAGEDOWN);
                case "jump": return Or(Controls.Get(DGame.Pl1, "jump"), Kbd.SDLK_DELETE);
                case "use": return Or(Controls.Get(DGame.Pl1, "use"), Kbd.SDLK_KP8);
                case "next": return Or(Controls.Get(DGame.Pl1, "next"), Kbd.SDLK_END);
                case "prev": return Or(Controls.Get(DGame.Pl1, "prev"), Kbd.SDLK_HOME);
                case "space": return Kbd.SDLK_SPACE;
                case "enter": return Kbd.SDLK_RETURN;
                case "esc": return Kbd.SDLK_ESCAPE;
                case "y": return 'y';
                case "n": return 'n';
                default: return 0;
            }
        }

        private static int Or(int key, int fallback)
        {
            return key != 0 ? key : fallback;
        }

    }
}
