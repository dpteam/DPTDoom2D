using System;
using System.Collections.Generic;
using System.IO;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Game;

namespace DPTDOOM2D
{
    /// <summary>
    /// main.c — startup and the 20 Hz main loop.
    /// </summary>
    public static class Program
    {
        public const string Title = "DPT.DOOM 2D";

        public static string DataDirectory;

        /// <summary>Wired by <see cref="GameHost"/> so the menus can drive the window.</summary>
        public static Action ToggleFullscreenRequested;
        public static Action ScreenshotRequested;

        public static bool Fullscreen;

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
            Log.Start();
            Log.Info("DPT.DOOM 2D starting, " + Environment.OSVersion.VersionString
                + ", .NET " + Environment.Version);

            try
            {
                int code = Run(args);
                Log.Info("exit code " + code);
                return code;
            }
            catch (Fatal e)
            {
                Log.Error("fatal: " + e.Message);
                Console.Error.WriteLine("DPT.DOOM 2D: " + e.Message);
                return 1;
            }
            catch (Exception e)
            {
                // Anything that escapes a handler lands here: print it, log the
                // stack, and exit with a code instead of a Windows crash dialog.
                Log.Error("unhandled exception", e);
                Console.Error.WriteLine("DPT.DOOM 2D: " + Log.Describe(e));
                Console.Error.WriteLine(e.StackTrace);
                return 2;
            }
            finally
            {
                Log.Info("log written to " + (Log.FilePath ?? "(console only)"));
                Log.Stop();
            }
        }

        static int Run(string[] args)
        {
            DataDirectory = LocateData();

            // config.c: options first, then default.cfg fills in the rest.
            Config.ParseArgs(args);
            Config.Load();
            Config.Normalize();

            for (int i = 0; i < args.Length; ++i)
            {
                if (args[i] == "--dump" && i + 2 < args.Length)
                    return Dump(args[i + 1], args[i + 2]);
                if (args[i] == "--list")
                    return ListResources();
                if (args[i] == "--walls" && i + 1 < args.Length)
                    return Walls(int.Parse(args[i + 1]));
                if (args[i] == "--maps")
                    return ListMaps();
                if (args[i] == "--sounds")
                    return CheckSounds();
                if (args[i] == "--cheats")
                {
                    Boot();
                    return Headless.CheatCheck();
                }
                if (args[i] == "--controls")
                    return Headless.ControlsCheck();
                if (args[i] == "--actorcheck")
                {
                    Boot();
                    return Headless.ActorCheck();
                }
                if (args[i] == "--datacheck")
                    return Headless.ManifestCheck();
                if (args[i] == "--modbuild" && i + 2 < args.Length)
                {
                    var inputs = new string[args.Length - i - 2];
                    Array.Copy(args, i + 2, inputs, 0, inputs.Length);
                    return ModKit.Build(args[i + 1], inputs);
                }
                if (args[i] == "--modlist" && i + 1 < args.Length)
                    return ModKit.List(args[i + 1]);
                if (args[i] == "--modunpack" && i + 2 < args.Length)
                    return ModKit.Unpack(args[i + 1], args[i + 2]);
                if (args[i] == "--proto-list")
                    return Prototypes.ProtoCatalog.List();
                if (args[i] == "--proto" && i + 1 < args.Length)
                    return RunPrototype(args[i + 1]);
                if (args[i] == "--wadbuild" && i + 2 < args.Length)
                {
                    // --wadbuild <list.lst> takes the original MAKEWAD interface;
                    // --wadbuild <out.wad> <file>... names the lumps after the files.
                    if (string.Equals(Path.GetExtension(args[i + 1]), ".lst", StringComparison.OrdinalIgnoreCase))
                        return Prototypes.WadBuilder.BuildFromList(args[i + 1]);

                    var files = new string[args.Length - i - 2];
                    Array.Copy(args, i + 2, files, 0, files.Length);
                    return Prototypes.WadBuilder.BuildFromFiles(args[i + 1], files);
                }
                if (args[i] == "--wadlist" && i + 1 < args.Length)
                    return Prototypes.WadBuilder.List(args[i + 1]);
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
                if (args[i] == "--music" && i + 1 < args.Length)
                {
                    Boot();
                    return Headless.MusicCheck(args[i + 1]);
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

            using (GameHost host = new GameHost())
            {
                ToggleFullscreenRequested = host.ToggleFullscreen;
                ScreenshotRequested = host.Screenshot;
                Fullscreen = Config.Fullscreen;
                host.Run();
            }

            return 0;
        }

        /// <summary>
        /// --proto &lt;id&gt;: runs one of the DOS-era prototypes in its own window.
        ///
        /// These do not need doom2d.wad: each brings its own artwork and palette,
        /// so a prototype can be tried on a fresh checkout.
        /// </summary>
        static int RunPrototype(string id)
        {
            Prototypes.ProtoCatalog.Item item = Prototypes.ProtoCatalog.Find(id);

            if (item == null)
            {
                Console.WriteLine("prototype '" + id + "' not found; try --proto-list");
                return 1;
            }

            if (item.Create == null)
            {
                Console.WriteLine(item.Title + " is a console tool:");
                Console.WriteLine("  " + item.Description);
                return 0;
            }

            return Prototypes.ProtoHost.Run(item.Create(), Config.Scale);
        }

        /// <summary>Boots the engine far enough to read the WAD, without opening a window.</summary>
        static void Boot()
        {
            Wad.Startup();
            string wad = Path.Combine(DataDirectory, "doom2d.wad");
            if (!File.Exists(wad))
                throw new Fatal("Не найден doom2d.wad (искал в " + DataDirectory + ")");
            Wad.AddWad(wad);

            // The manifest is what a peer has to agree on before a session starts,
            // so every file that takes part in the load is recorded here.
            DataManifest.AddFile(wad);

            // -file <wad> from the command line, exactly as in files.c.
            AddExtraWads();

            Wad.InitWads();
            Palette.Load();
            Palette.SetGamma(Config.Gamma);

            // Actor data comes from WAD lumps (a PK3 ships actors.txt as a loose
            // file), so it is read after the WADs are open and before anything
            // builds a sprite table.
            Actors.Load();
        }

        /// <summary>
        /// Adds the extra WADs: the ones named with -file, then the map pack
        /// chosen with -map-pack out of Data/Maps.
        ///
        /// The Maps folder is a *library*, not a load list: every pack in it
        /// defines its own MAP01.., so loading all of them at once would leave
        /// whichever happened to be read last. One pack is loaded per run, which
        /// is also what makes custom maps a drop-in feature — copy a WAD into the
        /// folder and it shows up in the launcher's list.
        ///
        /// A -file may be a WAD, a loose LMP, or a PK3/ZIP container; the
        /// container is unpacked here so the rest of the engine only ever sees
        /// WADs and lumps.
        /// </summary>
        public static void AddExtraWads()
        {
            for (int i = 0; i < Config.ExtraWads.Count; ++i)
            {
                string path = Config.ExtraWads[i];
                if (!File.Exists(path))
                {
                    Log.Warn("wad not found: " + path);
                    continue;
                }

                try
                {
                    AddContainer(path);
                    DataManifest.AddFile(path);
                }
                catch (Exception e)
                {
                    Log.Error("cannot load " + path, e);
                }
            }

            if (string.IsNullOrEmpty(Config.MapPack)) return;

            string pack = FindMapPack(Config.MapPack);
            if (pack == null)
            {
                Log.Warn("map pack not found: " + Config.MapPack);
                return;
            }

            try
            {
                AddContainer(pack);
                DataManifest.AddFile(pack);
                Log.Info("map pack: " + Path.GetFileName(pack));
            }
            catch (Exception e)
            {
                Log.Error("cannot load map pack " + pack, e);
            }
        }

        /// <summary>
        /// Loads one file as a WAD, an LMP, or a PK3 that may hold both.
        ///
        /// Inside a PK3 every *.wad is added in path order (so later ones win,
        /// like a -file chain) and every other file becomes a loose lump named
        /// after the file, which is how a single sprite or map gets patched
        /// without building a WAD at all.
        /// </summary>
        private static void AddContainer(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension != ".pk3" && extension != ".zip")
            {
                Wad.AddWad(path);
                Log.Info("wad: " + Path.GetFileName(path));
                return;
            }

            byte[] data = File.ReadAllBytes(path);

            if (!Pk3.LooksLikeZip(data))
            {
                Log.Warn("pk3: " + Path.GetFileName(path) + " is not a ZIP archive, skipped");
                return;
            }

            List<Pk3.Entry> entries = Pk3.ReadDirectory(data);
            if (entries == null)
            {
                Log.Warn("pk3: " + Path.GetFileName(path) + " has no readable directory, skipped");
                return;
            }

            int wads = 0, lumps = 0;

            // Sorted so the load order does not depend on how the archive happens
            // to be laid out; a mod that patches another ships a name that sorts
            // after it.
            entries.Sort(delegate (Pk3.Entry a, Pk3.Entry b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < entries.Count; ++i)
            {
                Pk3.Entry entry = entries[i];
                if (entry.Name.EndsWith("/", StringComparison.Ordinal)) continue;

                string name = Path.GetFileName(entry.Name);
                if (name.Length == 0) continue;

                string ext = Path.GetExtension(name).ToLowerInvariant();

                byte[] payload = Pk3.Read(data, entry);
                if (payload == null || payload.Length == 0) continue;

                if (ext == ".wad")
                {
                    Wad.AddWadFromBytes(Path.GetFileName(path) + ":" + entry.Name, payload);
                    ++wads;
                }
                else if (string.Equals(name, "actors.txt", StringComparison.OrdinalIgnoreCase))
                {
                    // The actor language is a loose text file, not a WAD lump, so
                    // it is renamed to the lump name the registry looks for. A mod
                    // ships it as actors.txt; nothing else has to know.
                    Wad.AddLumpFromBytes("ACTORDEF", payload);
                    ++lumps;
                }
                else if (ext == ".lmp" || ext == ".vgaimg" || ext == ".dmi" || ext == ".dmm")
                {
                    Wad.AddLumpFromBytes(Path.GetFileNameWithoutExtension(name), payload);
                    ++lumps;
                }
            }

            Log.Info("pk3: " + Path.GetFileName(path) + " (" + wads + " wad, " + lumps + " lump)");

            if (wads == 0 && lumps == 0)
                Log.Warn("pk3: " + Path.GetFileName(path) + " has nothing Doom2D can load");
        }

        /// <summary>Where custom map packs live.</summary>
        public static string MapsDirectory
        {
            get { return Path.Combine(DataDirectory, "Maps"); }
        }

        /// <summary>Every WAD in the map library, sorted; empty when there is none.</summary>
        public static string[] ListMapPacks()
        {
            try
            {
                if (!Directory.Exists(MapsDirectory)) return new string[0];

                string[] files = Directory.GetFiles(MapsDirectory, "*.wad");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files;
            }
            catch (Exception e)
            {
                Log.Warn("cannot list " + MapsDirectory + ": " + Log.Describe(e));
                return new string[0];
            }
        }

        /// <summary>Resolves a pack name (with or without ".wad", any case) to a path.</summary>
        public static string FindMapPack(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            string[] files = ListMapPacks();

            for (int i = 0; i < files.Length; ++i)
                if (string.Equals(Path.GetFileName(files[i]), name, StringComparison.OrdinalIgnoreCase))
                    return files[i];

            string withExtension = name.EndsWith(".wad", StringComparison.OrdinalIgnoreCase) ? name : name + ".wad";

            for (int i = 0; i < files.Length; ++i)
                if (string.Equals(Path.GetFileName(files[i]), withExtension, StringComparison.OrdinalIgnoreCase))
                    return files[i];

            return null;
        }

        static string LocateData()
        {
            string exe = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = { Path.Combine(exe, "Data"), exe, Directory.GetCurrentDirectory() };
            foreach (string dir in candidates)
                if (File.Exists(Path.Combine(dir, "doom2d.wad"))) return dir;
            return Path.Combine(exe, "Data");
        }

        /// <summary>--dump &lt;resource&gt; &lt;file.bmp&gt;: render one lump, for eyeballing WAD parsing.</summary>
        static int Dump(string resource, string output)
        {
            Boot();

            Vga.Init(320, 200);
            Vga.SetRect(0, 320, 0, 200);
            Vga.Clr(0, 320, 0, 200, 0);

            VgaImage img = Res.Img(Wad.GetResId(resource));
            Vga.Pic(0, 0, img);

            WriteBmp(output, 320, 200);
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

        /// <summary>--maps: the custom map library, then the maps in the loaded WAD set.</summary>
        static int ListMaps()
        {
            string[] packs = ListMapPacks();

            if (packs.Length > 0)
            {
                Console.WriteLine("custom map packs in " + MapsDirectory + ":");
                for (int i = 0; i < packs.Length; ++i)
                    Console.WriteLine("  " + Path.GetFileName(packs[i]));
                Console.WriteLine("  (pick one with -map-pack <name>)");
                Console.WriteLine();
            }

            Boot();

            int count = 0;
            for (int i = 0; i < Wad.ResCount; ++i)
            {
                string name = Wad.GetResName(i);
                if (!name.StartsWith("MAP", StringComparison.Ordinal) || name.Length != 5) continue;

                int number;
                if (!int.TryParse(name.Substring(3), out number)) continue;

                Console.WriteLine(string.Format("{0} {1,8} bytes", name, Wad.GetResLen(i)));
                ++count;
            }

            Console.WriteLine(count + " map(s)");
            return count > 0 ? 0 : 1;
        }

        /// <summary>--sounds: every sound lump has to parse into playable data.</summary>
        static int CheckSounds()
        {
            Boot();

            int total, failed, looped, peak;
            int ok = Sound.Diagnose(out total, out failed, out looped, out peak);

            Console.WriteLine(string.Format("sounds: {0} total, {1} ok, {2} failed, {3} looped, peak {4}",
                total, ok, failed, looped, peak));

            return failed == 0 && ok > 0 ? 0 : 1;
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

        public static void WriteBmp(string path, int w, int h)
        {
            int rowSize = (w * 3 + 3) & ~3;
            int dataSize = rowSize * h;

            using (FileStream fs = File.Create(path))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((byte)'B'); bw.Write((byte)'M');
                bw.Write(54 + dataSize); bw.Write(0); bw.Write(54);
                bw.Write(40); bw.Write(w); bw.Write(h);
                bw.Write((short)1); bw.Write((short)24);
                bw.Write(0); bw.Write(dataSize);
                bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);

                byte[] src = Vga.Buffer;
                byte[] row = new byte[rowSize];
                for (int y = h - 1; y >= 0; --y)
                {
                    int at = 0;
                    for (int x = 0; x < w; ++x)
                    {
                        int i = src[y * w + x];
                        row[at++] = (byte)Math.Min(255, Palette.Std[i * 3 + 2] * 4);
                        row[at++] = (byte)Math.Min(255, Palette.Std[i * 3 + 1] * 4);
                        row[at++] = (byte)Math.Min(255, Palette.Std[i * 3] * 4);
                    }
                    bw.Write(row, 0, rowSize);
                }
            }
        }
    }
}
