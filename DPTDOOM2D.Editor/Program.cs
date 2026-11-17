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
                Boot();

                // --render <map> <out.png>: the map without a window. It is how the
                // drawing gets checked — a wrong palette or a missing texture is
                // obvious in a picture and invisible in a log.
                if (args.Length >= 3 && args[0] == "--render")
                    return RenderToFile(args[1], args[2]);

                // --save <map> <out.wad>: the editor's save path without the window.
                // It is the check that a map written here loads in the game.
                if (args.Length >= 3 && args[0] == "--save")
                    return SaveToFile(args[1], args[2]);

                string open = args.Length > 0 ? args[0] : null;
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
            Palette.SetGamma(0);

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

        /// <summary>The editor's own loop: input, draw, present. Twenty frames a second.</summary>
        private static int Run(string open)
        {
            Vga.Init(EditorScreen.ScreenW, EditorScreen.ScreenH);
            Vga.SetRect(0, EditorScreen.ScreenW, 0, EditorScreen.ScreenH);

            var screen = new EditorScreen();
            screen.Load(InitialMap(open), NameOf(open), "");

            var window = new Win32Window("DPT.DOOM 2D — редактор карт",
                EditorScreen.ScreenW, EditorScreen.ScreenH, 3, false);

            // Pixel perfect: the map is drawn at 16 pixels a cell and the whole
            // point is that a wall looks like the wall. Stretching would smear it.
            window.Stretch = Win32Window.StretchMode.PixelPerfect;

            var ui = new EditorUi(window);
            var presenter = new Presenter(EditorScreen.ScreenW, EditorScreen.ScreenH);

            window.OverlayPainter = delegate (IntPtr dc, int width, int height)
            {
                try { ui.Draw(dc, screen); }
                catch (Exception e) { Log.Error("editor: ui draw failed", e); }
            };

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
                    return MapFile.Read(data, Path.GetFileName(open));
                }

                int res = Wad.FindRes(open);
                if (res >= 0) return MapFile.Read(Wad.LoadRes(res), open);
            }

            int map01 = Wad.FindRes("MAP01");
            if (map01 < 0)
                throw new Fatal("В WAD нет карты MAP01");

            return MapFile.Read(Wad.LoadRes(map01), "MAP01");
        }

        private static string NameOf(string open)
        {
            if (string.IsNullOrEmpty(open)) return "MAP01";

            string name = Path.GetFileNameWithoutExtension(open).ToUpperInvariant();
            return name.Length == 0 ? "MAP01" : name;
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

        /// <summary>Renders a whole map to a PNG through the editor's canvas.</summary>
        private static int RenderToFile(string mapName, string output)
        {
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

            MapCanvas.Draw(map, 0, 0, 0, 0, width, height, true, true, true);
            MapCanvas.WriteBmp(output, width, height);

            Console.WriteLine("render: " + mapName + " -> " + Path.GetFileName(output)
                + " (" + map.Summary() + ")");

            return 0;
        }
    }
}
