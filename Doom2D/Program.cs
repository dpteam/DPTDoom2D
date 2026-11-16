using System;
using System.IO;
using Doom2D.Engine;
using Doom2D.Game;

namespace Doom2D
{
    /// <summary>
    /// main.c — startup and the 20 Hz main loop.
    /// </summary>
    public static class Program
    {
        /// <summary>The game's own resolution; the window shows it scaled by an integer factor.</summary>
        public const int Scale = 2;

        public static string DataDirectory;

        /// <summary>Fullscreen is toggled from the options menu (INTERP).</summary>
        public static bool Fullscreen;

        public static event Action ToggleFullscreenRequested;
        public static event Action ScreenshotRequested;

        public static void ToggleFullscreen()
        {
            Fullscreen = !Fullscreen;
            if (ToggleFullscreenRequested != null) ToggleFullscreenRequested();
        }

        public static void Screenshot()
        {
            if (ScreenshotRequested != null) ScreenshotRequested();
        }

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Fatal e)
            {
                Console.Error.WriteLine("Doom 2D: " + e.Message);
                return 1;
            }
        }

        static int Run(string[] args)
        {
            DataDirectory = LocateData();

            // config.c: options first, then default.cfg fills in the rest.
            Config.ParseArgs(args);
            Config.Load();

            for (int i = 0; i < args.Length; ++i)
            {
                if (args[i] == "--dump" && i + 2 < args.Length)
                    return Dump(args[i + 1], args[i + 2]);
                if (args[i] == "--list")
                    return ListResources();
                if (args[i] == "--walls" && i + 1 < args.Length)
                    return Walls(int.Parse(args[i + 1]));
                if (args[i] == "--saveload")
                {
                    Boot();
                    return Headless.SaveLoad(i + 1 < args.Length ? int.Parse(args[i + 1]) : 120);
                }
                if (args[i] == "--levelend" && i + 1 < args.Length)
                {
                    Boot();
                    return Headless.LevelEnd(int.Parse(args[i + 1]));
                }
                if (args[i] == "--switches" && i + 1 < args.Length)
                {
                    Boot();
                    return Headless.SwitchList(int.Parse(args[i + 1]));
                }
                if (args[i] == "--shell" && i + 2 < args.Length)
                {
                    string keys0 = (i + 3 < args.Length) ? args[i + 3] : null;
                    Boot();
                    return Headless.Shell(int.Parse(args[i + 1]), args[i + 2], keys0);
                }
                if (args[i] == "--simulate" && i + 2 < args.Length)
                {
                    int ticks = int.Parse(args[i + 1]);
                    int map = (i + 3 < args.Length) ? int.Parse(args[i + 3]) : 1;
                    string keys = (i + 4 < args.Length) ? args[i + 4] : null;
                    bool twoPl = (i + 5 < args.Length) && args[i + 5] == "2p";
                    bool dm = (i + 5 < args.Length) && args[i + 5] == "dm";
                    Boot();
                    return Headless.Run(ticks, args[i + 2], map, keys, twoPl, dm);
                }
            }

            using (Doom2DGame game = new Doom2DGame())
                game.Run();
            return 0;
        }

        /// <summary>Boots the engine far enough to read the WAD, without opening a window.</summary>
        static void Boot()
        {
            Wad.Startup();
            string wad = Path.Combine(DataDirectory, "doom2d.wad");
            if (!File.Exists(wad))
                throw new Fatal("Не найден doom2d.wad (искал в " + DataDirectory + ")");
            Wad.AddWad(wad);

            // -file <wad> from the command line, exactly as in files.c.
            for (int i = 0; i < Config.ExtraWads.Count; ++i)
                if (File.Exists(Config.ExtraWads[i])) Wad.AddWad(Config.ExtraWads[i]);

            Wad.InitWads();
            Palette.Load();
            Palette.SetGamma(Config.Gamma);
        }

        static string LocateData()
        {
            string exe = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = { Path.Combine(exe, "Data"), exe, Directory.GetCurrentDirectory() };
            foreach (string dir in candidates)
                if (File.Exists(Path.Combine(dir, "doom2d.wad"))) return dir;
            return Path.Combine(exe, "Data");
        }

        /// <summary>--dump &lt;resource&gt; &lt;file.png&gt;: render one lump, for eyeballing WAD parsing.</summary>
        static int Dump(string resource, string output)
        {
            Boot();

            Vga.Init(320, 200);
            Vga.SetRect(0, 320, 0, 200);
            Vga.Clr(0, 320, 0, 200, 0);

            VgaImage img = Res.Img(Wad.GetResId(resource));
            Vga.Pic(0, 0, img);

            WritePng(output, 320, 200);
            Console.WriteLine(string.Format("{0}: {1}x{2}, hot spot {3},{4} -> {5}",
                resource, img.W, img.H, img.SX, img.SY, output));
            return 0;
        }

        static int ListResources()
        {
            Boot();
            for (int i = 0; i < Wad.ResCount; ++i)
                Console.WriteLine(string.Format("{0,5} {1,-8} {2,8} {3,8}",
                    i, Wad.GetResName(i), Wad.Entries[i].Offset, Wad.GetResLen(i)));
            return 0;
        }

        /// <summary>--walls &lt;map&gt;: what the map actually references.</summary>
        static int Walls(int map)
        {
            Boot();
            Game.Game.Map = map;
            Game.Game.TwoPl = false;
            Game.Game.Dm = false;
            Game.Game.Init();
            Player.Reset();
            Game.Game.Start();

            int[] back = new int[256], type = new int[256], front = new int[256];
            for (int i = 0; i < World.FldB.Length; ++i) back[World.FldB[i]]++;
            for (int i = 0; i < World.Fld.Length; ++i) type[World.Fld[i]]++;
            for (int i = 0; i < World.FldF.Length; ++i) front[World.FldF[i]]++;

            for (int i = 0; i < 256; ++i)
            {
                if (back[i] + type[i] + front[i] == 0) continue;
                string size = World.WalP[i] != null
                    ? World.WalP[i].W + "x" + World.WalP[i].H
                    : "water " + World.WalWater[i];
                string nm = World.WalH[i] >= 0 ? Wad.GetResName(World.WalH[i] & 0x7FFF) : "";
                Console.WriteLine(string.Format("{0,3} b={1,-6} t={2,-6} f={3,-6} {4,-10} {5}",
                    i, back[i], type[i], front[i], size, nm));
            }
            return 0;
        }

        static void WritePng(string path, int w, int h)
        {
            using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h))
            {
                for (int y = 0; y < h; ++y)
                    for (int x = 0; x < w; ++x)
                    {
                        byte idx = Vga.Buffer[y * w + x];
                        System.Drawing.Color c = System.Drawing.Color.FromArgb(
                            Palette.Std[idx * 3] * 4,
                            Palette.Std[idx * 3 + 1] * 4,
                            Palette.Std[idx * 3 + 2] * 4);
                        bmp.SetPixel(x, y, c);
                    }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
