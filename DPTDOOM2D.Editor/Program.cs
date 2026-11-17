//
// DPT.DOOM 2D — map editor.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Game;
using DPTDOOM2D.Win32;

namespace DPTDOOM2D.Editor
{
    /// <summary>
    /// The map editor: a VGA program with its own window, like the DOS original.
    ///
    /// Not a WinForms application. The frame is the game's 320x200 palette
    /// framebuffer, the map is drawn with the game's textures, and the panels are
    /// painted on top of the same frame. The only thing that differs from the game
    /// is what the loop does with the input.
    /// </summary>
    internal static class Program
    {
        public static string DataDirectory;
        public static string MapsDirectory;

        private static int Main(string[] args)
        {
            try
            {
                ParseOptions(args);
                Boot();

                // --render <map> <out.png>: the map without a window. It is how the
                // drawing gets checked — a wrong palette or a missing texture is
                // obvious in a picture and invisible in a log.
                if (args.Length >= 3 && args[0] == "--render")
                    return RenderToFile(args[1], args[2]);

                // --shot <map> <out.bmp> [tab]: the whole editor screen without a
                // window, one frame at 320x200. The reference screenshots came out
                // of DOSBox, so the only way to compare them is with a picture that
                // has no window chrome and no scaling in it.
                if (args.Length >= 3 && args[0] == "--shot")
                    return ScreenToFile(args[1], args[2], args.Length > 3 ? args[3] : "0");

                // --save <map> <out.wad>: the editor's save path without the window.
                // It is the check that a map written here loads in the game.
                if (args.Length >= 3 && args[0] == "--save")
                    return SaveToFile(args[1], args[2]);

                // --newmap <out.wad>: the empty map a mapper starts from.
                if (args.Length >= 2 && args[0] == "--newmap")
                    return WriteNewMap(args[1]);

                // --undocheck: undo has to restore the map exactly, not approximately.
                if (args.Length >= 1 && args[0] == "--undocheck")
                    return UndoCheck();

                string open = Open;
                return Run(open);
            }
            catch (Fatal e)
            {
                Console.Error.WriteLine("editor: " + e.Message);
                return 1;
            }
            catch (Exception e)
            {
                Log.Error("editor failed", e);
                Console.Error.WriteLine("editor: " + Log.Describe(e));
                return 2;
            }
            finally
            {
                Log.Stop();
            }
        }

        /// <summary>
        /// Loads the WAD and the palette: without them there is nothing to draw with.
        /// Data is looked up next to the editor, next to the game, and in a dev tree.
        /// </summary>
        private static void Boot()
        {
            DataDirectory = LocateData();
            if (DataDirectory == null)
            {
                throw new Fatal(
                    "Не найдена папка Data с doom2d.wad.\n" +
                    "Редактор ищет её рядом с собой, рядом с игрой и в корне репозитория.");
            }

            MapsDirectory = Path.Combine(DataDirectory, "Maps");

            Wad.Startup();
            Wad.AddWad(Path.Combine(DataDirectory, "doom2d.wad"));
            Wad.InitWads();
            Palette.Load();

            // The DOS editor shipped with `gamma=1` in EDITOR.CFG and called
            // setgamma(gamma) at startup, so its greys are the brighter ramp, not
            // the raw palette. Rendering at 0 makes every widget visibly darker
            // than the reference screenshots — the panel face comes out 96 instead
            // of 116 — which is exactly how this was found.
            Palette.SetGamma(Gamma);

            // Actor data decides monster names and sprites; the editor shows both.
            Actors.Load();

            Log.Info("editor: data " + DataDirectory + ", " + Wad.Entries.Count + " resources");
        }

        private static string LocateData()
        {
            string exe = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates =
            {
                Path.Combine(exe, "Data"),
                Path.Combine(exe, "..", "DPTDOOM2D", "bin", "Release", "net40", "Data"),
                Path.Combine(exe, "..", "..", "..", "DPTDOOM2D", "bin", "Release", "net40", "Data"),
                Path.Combine(exe, "..", "..", "..", "..", "DPTDOOM2D", "bin", "Release", "net40", "Data"),
                Path.Combine(exe, "..", "..", "..", "..", "..", "DPTDOOM2D", "bin", "Release", "net40", "Data"),
                Path.Combine(exe, "..", "refs", "doom2d-rembo", "src")
            };

            foreach (string candidate in candidates)
            {
                string full;
                try { full = Path.GetFullPath(candidate); }
                catch { continue; }

                if (File.Exists(Path.Combine(full, "doom2d.wad"))) return full;
            }

            return null;
        }

        /// <summary>The game next to the editor, so F5 can start a playtest.</summary>
        public static string FindGame()
        {
            string exe = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates =
            {
                Path.Combine(exe, "dptdoom2d.exe"),
                Path.Combine(exe, "..", "DPTDOOM2D", "bin", "Release", "net40", "dptdoom2d.exe"),
                Path.Combine(exe, "..", "..", "..", "DPTDOOM2D", "bin", "Release", "net40", "dptdoom2d.exe"),
                Path.Combine(exe, "..", "..", "..", "..", "DPTDOOM2D", "bin", "Release", "net40", "dptdoom2d.exe")
            };

            foreach (string candidate in candidates)
            {
                string full;
                try { full = Path.GetFullPath(candidate); }
                catch { continue; }

                if (File.Exists(full)) return full;
            }

            return null;
        }

        /// <summary>
        /// Brightness, as the original's EDITOR.CFG carried it. 1 is what the 1996
        /// editor shipped with and what its screenshots show.
        /// </summary>
        private static int Gamma = 1;

        /// <summary>The map to open, from the first argument that is not a switch.</summary>
        private static string Open;

        /// <summary>The name the map gets in the saved WAD, set when it is read.</summary>
        private static string OpenName = "MAP01";

        /// <summary>Window scale: 1..6, or 0 to fit. Same meaning as the game's -scale.</summary>
        private static int Scale = 3;

        private static bool Fullscreen;

        private static Win32Window.StretchMode Stretch = Win32Window.StretchMode.Nearest;

        /// <summary>
        /// The same two options the game takes, so the editor can be stretched the
        /// same way. Parsed here rather than through Engine/Config: that file drags in
        /// the whole game configuration, and the editor needs two numbers.
        /// </summary>
        private static void ParseOptions(string[] args)
        {
            for (int i = 0; i < args.Length; ++i)
            {
                string s = args[i];

                if (s.Length < 2 || (s[0] != '-' && s[0] != '/'))
                {
                    // A map or a file, not a switch. Taking args[0] blindly made
                    // `-scale 2` open a map called "-SCALE" and save the level under
                    // that name — which the game then could not find.
                    if (Open == null) Open = s;
                    continue;
                }

                string name = s.Substring(1).ToLowerInvariant();

                if (name == "fullscr" || name == "fullscreen")
                {
                    Fullscreen = true;
                    continue;
                }

                if (name == "window")
                {
                    Fullscreen = false;
                    continue;
                }

                if (i + 1 >= args.Length) continue;

                string value = args[++i];

                if (name == "scale")
                {
                    int scale;
                    if (int.TryParse(value, out scale))
                        Scale = scale < 0 ? 0 : (scale > 6 ? 6 : scale);
                }
                else if (name == "gamma")
                {
                    int gamma;
                    if (int.TryParse(value, out gamma))
                        Gamma = gamma < 0 ? 0 : (gamma > 4 ? 4 : gamma);
                }
                else if (name == "stretch")
                {
                    switch (value.Trim().ToLowerInvariant())
                    {
                        case "pixel":
                        case "pixelperfect":
                        case "pixel-perfect":
                            Stretch = Win32Window.StretchMode.PixelPerfect;
                            break;

                        case "smooth":
                        case "linear":
                            Stretch = Win32Window.StretchMode.Smooth;
                            break;

                        default:
                            Stretch = Win32Window.StretchMode.Nearest;
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// The editor's loop: input, draw, present — the same shape as the game's.
        ///
        /// The window is a scaled view of one 320x200 frame, exactly like the game, so
        /// -scale and -stretch mean the same thing here. That is the point of drawing
        /// the whole UI into the framebuffer: at 1x and at 6x it is the same picture,
        /// and "stretch it like the game" needs no code of its own.
        /// </summary>
        private static int Run(string open)
        {
            Vga.Init(EditorScreen.ScreenW, EditorScreen.ScreenH);
            Vga.SetRect(0, EditorScreen.ScreenW, 0, EditorScreen.ScreenH);

            EditorSkin.Load();

            var screen = new EditorScreen();
            screen.Load(InitialMap(open), OpenName, "");

            var window = new Win32Window("DPT.DOOM 2D — редактор карт",
                EditorScreen.ScreenW, EditorScreen.ScreenH, Scale, Fullscreen);

            window.Stretch = Stretch;

            var presenter = new Presenter(EditorScreen.ScreenW, EditorScreen.ScreenH);

            while (!window.Closed && !screen.Quit)
            {
                window.PumpMessages();

                screen.Tick(window);

                Vga.Clr(0, EditorScreen.ScreenW, 0, EditorScreen.ScreenH, 0);
                screen.Draw(window);

                presenter.Present(window);

                System.Threading.Thread.Sleep(16);
            }

            window.Dispose();
            return 0;
        }

        private static MapFile InitialMap(string open)
        {
            if (!string.IsNullOrEmpty(open))
            {
                // A file path or a lump name; both are what a mapper would type.
                if (File.Exists(open))
                {
                    byte[] data = File.ReadAllBytes(open);
                    OpenName = NameOf(open);
                    return MapFile.Read(data, OpenName);
                }

                int res = Wad.FindRes(open);
                if (res >= 0)
                {
                    OpenName = open.ToUpperInvariant();
                    return MapFile.Read(Wad.LoadRes(res), OpenName);
                }
            }

            int map01 = Wad.FindRes("MAP01");
            if (map01 < 0)
                throw new Fatal("В WAD нет карты MAP01");

            OpenName = "MAP01";
            return MapFile.Read(Wad.LoadRes(map01), "MAP01");
        }

        private static string NameOf(string open)
        {
            if (string.IsNullOrEmpty(open)) return "MAP01";

            string name = Path.GetFileNameWithoutExtension(open).ToUpperInvariant();
            return name.Length == 0 ? "MAP01" : name;
        }

        /// <summary>Writes the empty map a new level starts from, so the game can try it.</summary>
        private static int WriteNewMap(string output)
        {
            MapFile map = MapFile.CreateEmpty(EditorScreen.PickWallForNewMap());

            var lumps = new List<KeyValuePair<string, byte[]>>();
            lumps.Add(new KeyValuePair<string, byte[]>("MAP01", map.Write()));

            if (!Prototypes.WadBuilder.WriteLumps(output, lumps))
            {
                Console.Error.WriteLine("newmap: не удалось записать " + output);
                return 1;
            }

            Console.WriteLine("newmap: " + Path.GetFileName(output) + " (" + map.Summary() + ")");
            return 0;
        }

        /// <summary>
        /// Undo has to put the map back exactly: the same bytes out, not "close enough".
        ///
        /// The stack is snapshots, so this also proves that a snapshot covers every
        /// field the writer emits — a layer, a thing, a switch, the sky, the music.
        /// A missed field would show up here as a different file.
        /// </summary>
        private static int UndoCheck()
        {
            int res = Wad.FindRes("MAP01");
            if (res < 0)
            {
                Console.Error.WriteLine("undocheck: нет MAP01");
                return 1;
            }

            MapFile map = MapFile.Read(Wad.LoadRes(res), "MAP01");
            byte[] before = map.Write();

            var undo = new UndoStack();
            undo.Push(map);

            // Change every kind of thing a stroke or a tool can touch.
            for (int i = 0; i < map.Back.Length; i += 7) map.Back[i] = 3;
            for (int i = 0; i < map.Type.Length; i += 11) map.Type[i] = 1;
            for (int i = 0; i < map.Front.Length; i += 13) map.Front[i] = 2;

            var thing = new MapThing();
            thing.X = 100; thing.Y = 100; thing.Type = MapThingType.Demon;
            map.Things.Add(thing);

            if (map.Things.Count > 0) map.Things.RemoveAt(0);

            var sw = new MapSwitch();
            sw.X = 5; sw.Y = 5; sw.Type = 3;
            map.Switches.Add(sw);

            map.Sky = 3;
            map.Music = "ZZZZZZZZ";

            if (!undo.Undo(map))
            {
                Console.WriteLine("undocheck: undo reported nothing to undo");
                return 1;
            }

            byte[] after = map.Write();

            if (before.Length != after.Length)
            {
                Console.WriteLine("undocheck: size " + before.Length + " -> " + after.Length);
                return 1;
            }

            for (int i = 0; i < before.Length; ++i)
            {
                if (before[i] == after[i]) continue;

                Console.WriteLine("undocheck: differs at byte " + i);
                return 1;
            }

            Console.WriteLine("undocheck: undo restored the map byte for byte (" + before.Length + " bytes)");
            return 0;
        }

        /// <summary>Reads a map and writes it back through the editor's save path.</summary>
        private static int SaveToFile(string mapName, string output)
        {
            int res = Wad.FindRes(mapName);
            if (res < 0)
            {
                Console.Error.WriteLine("save: карта " + mapName + " не найдена");
                return 1;
            }

            MapFile map = MapFile.Read(Wad.LoadRes(res), mapName);

            var lumps = new List<KeyValuePair<string, byte[]>>();
            lumps.Add(new KeyValuePair<string, byte[]>(mapName, map.Write()));

            if (!Prototypes.WadBuilder.WriteLumps(output, lumps))
            {
                Console.Error.WriteLine("save: не удалось записать " + output);
                return 1;
            }

            Console.WriteLine("save: " + mapName + " -> " + Path.GetFileName(output)
                + " (" + map.Summary() + ")");

            return 0;
        }

        /// <summary>
        /// Renders one editor frame to a BMP.
        ///
        /// This is the editor's picture without a window: 320x200, no scaling, no
        /// title bar. The reference screenshots in docs/images came out of DOSBox,
        /// so comparing against them means comparing pictures with the same
        /// properties — and a pixel-by-pixel diff is only possible this way.
        /// </summary>
        private static int ScreenToFile(string mapName, string output, string tab)
        {
            int res = Wad.FindRes(mapName);
            if (res < 0)
            {
                Console.Error.WriteLine("shot: карта " + mapName + " не найдена");
                return 1;
            }

            MapFile map = MapFile.Read(Wad.LoadRes(res), mapName);

            Vga.Init(EditorScreen.ScreenW, EditorScreen.ScreenH);
            Vga.SetRect(0, EditorScreen.ScreenW, 0, EditorScreen.ScreenH);

            EditorSkin.Load();

            var screen = new EditorScreen();
            screen.Load(map, mapName, "");

            int tool;
            if (int.TryParse(tab, out tool) && tool >= 0 && tool < 5)
                screen.Tool = (EditorTool)tool;

            // One frame, drawn exactly as the loop draws it. Draw needs a window only
            // for the mouse position, and a headless run has no mouse.
            screen.Draw(null);

            MapCanvas.WriteBmp(output, EditorScreen.ScreenW, EditorScreen.ScreenH);

            Console.WriteLine("shot: " + mapName + " tab " + screen.Tool + " -> " + Path.GetFileName(output));

            return 0;
        }

        /// <summary>Renders a whole map to a PNG through the editor's canvas.</summary>
        private static int RenderToFile(string mapName, string output)        {
            int res = Wad.FindRes(mapName);
            if (res < 0)
            {
                Console.Error.WriteLine("render: карта " + mapName + " не найдена");
                return 1;
            }

            MapFile map = MapFile.Read(Wad.LoadRes(res), mapName);

            int width = MapFile.Width * MapCanvas.CellSize;
            int height = MapFile.Height * MapCanvas.CellSize;

            Vga.Init(width, height);
            Vga.SetRect(0, width, 0, height);
            Vga.Clr(0, width, 0, height, 0);

            // A whole level, so the camera sits at its centre with nothing to clip.
            MapCanvas.Draw(map, width / 2, height / 2,
                0, 0, width, height,
                true, true, true, true, true, -1, -1, false, 0, 0, 0, 0);

            MapCanvas.WriteBmp(output, width, height);

            Console.WriteLine("render: " + mapName + " -> " + Path.GetFileName(output)
                + " (" + map.Summary() + ")");

            return 0;
        }
    }
}
