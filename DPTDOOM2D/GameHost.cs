//
// DPT.DOOM 2D — game host.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Diagnostics;
using System.IO;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Game;
using DPTDOOM2D.Ui;
using DPTDOOM2D.Win32;
using DGame = DPTDOOM2D.Game.Game;

namespace DPTDOOM2D
{
    /// <summary>
    /// main.c — the window, the 20 Hz loop and the input plumbing.
    ///
    /// The original ran a fixed 50 ms loop: act, draw, wait for the rest of the
    /// tick. That is exactly what this does, with a Win32 window and a DIB blit
    /// instead of a VGA mode.
    /// </summary>
    internal sealed class GameHost : IDisposable
    {
        private readonly Win32Window window;
        private readonly Presenter presenter;
        private readonly Overlay overlay;

        private bool running = true;
        private int frames;
        private int fps;
        private int fpsTimer;

        public GameHost()
        {
            Vga.Init(Vga.SCRW, Vga.SCRH);

            Wad.Startup();
            Wad.AddWad(Path.Combine(Program.DataDirectory, "doom2d.wad"));
            for (int i = 0; i < Config.ExtraWads.Count; ++i)
                if (File.Exists(Config.ExtraWads[i])) Wad.AddWad(Config.ExtraWads[i]);
            Wad.InitWads();
            Palette.Load();
            Palette.SetGamma(Config.Gamma);

            DGame.Warp = Config.Warp;
            Monsters.NoMon = Config.NoMonsters;

            Sound.Init();
            Sound.Volume(Sound.SndVol);
            Music.Volume(Music.MusVol);
            Music.Init();

            DGame.Init();
            World.LoadLightningSounds();

            // A network session starts before the window so a refused connection
            // is reported on the console instead of leaving an empty window open.
            if (Config.Net != null && Config.Net.Enabled)
            {
                if (!Net.NetInput.Start(Config.Net))
                    throw new Fatal("не удалось начать сетевую игру");

                // Both players share one world: the map and the mode come from the
                // host's welcome packet, not from this machine's menu.
                DGame.TwoPl = true;
                DGame.Dm = Net.NetInput.Session.Deathmatch;
                DGame.Map = Net.NetInput.Session.StartMap;
                Player.Reset();
                DGame.Start();
            }

            window = new Win32Window(Program.Title, Vga.SCRW, Vga.SCRH,
                Config.Scale, Config.Fullscreen);
            presenter = new Presenter(Vga.SCRW, Vga.SCRH);

            overlay = new Overlay(window);
            overlay.Scale = Config.Scale;
            overlay.Fullscreen = Config.Fullscreen;
            window.OverlayPainter = (dc, w, h) => overlay.Draw(dc, w, h, fps);

            overlay.Log("DPT.DOOM 2D started");
            overlay.Log("wad: doom2d.wad, music: " + (Music.IsPlaying ? "on" : "off"));
            if (Net.NetInput.Active) overlay.Log("net: " + Net.NetInput.Status());
        }

        public void Run()
        {
            long tick = Stopwatch.GetTimestamp();
            long tickLength = Stopwatch.Frequency / 20;   // 20 ticks per second

            while (running && !window.Closed)
            {
                window.PumpMessages();
                if (window.Closed) break;

                Kbd.Update(window);

                // The overlay sees keys first: F2 opens it, and while a settings
                // tab is focused the game does not get the arrow keys. Whatever the
                // overlay does not consume becomes the game's "last key", which is
                // how menu.c reads input.
                int gameKey = 0;
                for (int i = 0; i < Kbd.PressedThisTick.Count; ++i)
                {
                    int sym = Kbd.PressedThisTick[i];
                    if (overlay.HandleKey(sym)) continue;
                    if (gameKey == 0) gameKey = sym;
                }

                Kbd.LastKey = gameKey;

                if (!overlay.LocksInput)
                {
                    RunGameTick();
                    Music.Update();
                }

                Kbd.LastKey = 0;

                DGame.Draw();
                Present();
                // Sleep until the next tick, the way the DOS build waited on the
                // vertical retrace: no busy loop, no drift.
                long now = Stopwatch.GetTimestamp();
                long remaining = tick + tickLength - now;
                if (remaining > 0)
                {
                    int ms = (int)(remaining * 1000 / Stopwatch.Frequency);
                    if (ms > 0) System.Threading.Thread.Sleep(ms);
                    while (Stopwatch.GetTimestamp() < tick + tickLength) { }
                }

                tick += tickLength;

                // If the machine fell far behind, do not try to catch up by
                // running a burst of ticks: drop the backlog instead.
                if (Stopwatch.GetTimestamp() - tick > tickLength * 4) tick = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>
        /// One simulation tick, network-aware.
        ///
        /// In a lockstep session the order is fixed: send this machine's input,
        /// wait for the peer's input for the same tick, run the tick, then publish
        /// the resulting world checksum. The local key state is saved first because
        /// simulating the remote player temporarily replaces the key table.
        /// </summary>
        private void RunGameTick()
        {
            if (!Net.NetInput.Active)
            {
                DGame.Act();
                return;
            }

            Kbd.SaveLocal();

            Net.NetInput.PublishInput(netTick, DGame.Pl1);

            if (!Net.NetInput.WaitForTick(netTick))
            {
                // The peer is gone: stop the session and keep playing locally so
                // the player is not left staring at a frozen window.
                overlay.Log("network session ended");
                Net.NetInput.Stop();
                DGame.Act();
                return;
            }

            DGame.Act();

            Net.NetInput.PublishChecksum(netTick, Net.NetProtocol.Checksum());

            if (Net.NetInput.Desync) overlay.Log("DESYNC detected - see the Log tab");
            if (netTick % 20 == 0) Net.NetInput.Session.SendPing();

            netTick++;
        }

        private int netTick;

        private void Present()
        {
            frames++;
            fpsTimer++;
            if (fpsTimer >= 20)
            {
                fps = frames;
                frames = 0;
                fpsTimer = 0;
            }

            Vga.Center(false);
            presenter.Present(window);
        }

        /// <summary>Menu / overlay hook: switch between window and full screen.</summary>
        public void ToggleFullscreen()
        {
            overlay.Fullscreen = !overlay.Fullscreen;
            window.SetFullscreen(overlay.Fullscreen);
        }

        public void Screenshot()
        {
            string dir = Path.Combine(Program.DataDirectory, "Screenshots");
            Directory.CreateDirectory(dir);

            int n = 1;
            string path;
            do { path = Path.Combine(dir, string.Format("shot{0:0000}.bmp", n++)); }
            while (File.Exists(path));

            presenter.SaveBmp(path);
            overlay.Log("screenshot: " + Path.GetFileName(path));
        }

        public void Dispose()
        {
            running = false;
            Music.Free();
            Sound.Done();
            presenter.Dispose();
            window.Dispose();
        }
    }
}
