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
            Program.AddExtraWads();
            Wad.InitWads();

            Log.Info("data: " + Program.DataDirectory + ", " + Wad.Entries.Count + " resources");

            Palette.Load();
            Palette.SetGamma(Config.Gamma);

            DGame.Warp = Config.Warp;
            Monsters.NoMon = Config.NoMonsters;

            Sound.Init();
            Sound.Volume(Sound.SndVol);
            Music.Volume(Music.MusVol);
            Music.Init();

            // main.c: F_loadmus("MENU"); S_startmusic(0) — the title screen has
            // music, and the track stays on until a map loads its own.
            Music.Load("MENU");
            Music.Start(0);

            DGame.Init();

            // The config file's key bindings are applied *after* Game.Init(),
            // because Init() writes the defaults from main.c and would otherwise
            // overwrite everything the player set in default.cfg.
            Config.ApplyBindings();

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
            window.Stretch = Config.StretchMode;

            presenter = new Presenter(Vga.SCRW, Vga.SCRH);

            overlay = new Overlay(window);
            overlay.Scale = Config.Scale;
            overlay.Fullscreen = Config.Fullscreen;
            overlay.Stretch = (int)Config.StretchMode;
            overlay.PaletteLevel = Config.Gamma;
            window.OverlayPainter = PaintOverlay;

            overlay.AddLog("DPT.DOOM 2D started");
            overlay.AddLog("video: " + Config.StretchName(overlay.Stretch)
                + ", " + (Config.Scale == 0 ? "fit" : Config.Scale + "x")
                + (Config.Fullscreen ? ", fullscreen" : ""));
            overlay.AddLog("audio: " + (AudioSystem.Available ? "on" : "off")
                + ", music " + (Music.MusVol == 0 ? "muted" : Music.MusVol.ToString()));
            if (Net.NetInput.Active) overlay.AddLog("net: " + Net.NetInput.Status());
        }

        public void Run()
        {
            long tick = Stopwatch.GetTimestamp();
            long tickLength = Stopwatch.Frequency / 20;   // 20 ticks per second

            while (running && !window.Closed)
            {
                window.PumpMessages();
                if (window.Closed) break;

                // A single bad frame must not kill the game: a broken sprite or a
                // malformed custom map ends up here, gets logged, and the loop
                // carries on instead of taking the window down.
                try
                {
                    Kbd.Update(window);

                    // The overlay sees keys first: F2 opens it, and while a settings
                    // tab is focused the game does not get the arrow keys. Whatever
                    // the overlay does not consume becomes the game's "last key",
                    // which is how menu.c reads input.
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
                }
                catch (Exception e)
                {
                    Log.Error("tick failed", e);
                    overlay.AddLog("error: " + Log.Describe(e));

                    // If the failure repeats every tick the log fills up and the
                    // game becomes unusable; give the player a chance to quit.
                    if (++consecutiveFailures > 20)
                        throw new Fatal("слишком много ошибок подряд: " + Log.Describe(e));
                }

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

        private int consecutiveFailures;

        /// <summary>Overlay painting, isolated so a drawing bug cannot stop the game.</summary>
        private void PaintOverlay(IntPtr dc, int width, int height)
        {
            try
            {
                overlay.Draw(dc, width, height, fps);
            }
            catch (Exception e)
            {
                Log.Error("overlay: draw failed", e);
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
                consecutiveFailures = 0;
                return;
            }

            Kbd.SaveLocal();

            Net.NetInput.PublishInput(netTick, DGame.Pl1);

            if (!Net.NetInput.WaitForTick(netTick))
            {
                // The peer is gone: stop the session and keep playing locally so
                // the player is not left staring at a frozen window.
                overlay.AddLog("network session ended");
                Net.NetInput.Stop();
                DGame.Act();
                return;
            }

            DGame.Act();

            Net.NetInput.PublishChecksum(netTick, Net.NetProtocol.Checksum());

            if (Net.NetInput.Desync) overlay.AddLog("DESYNC detected - see the Log tab");
            if (netTick % 20 == 0 && Net.NetInput.Session != null) Net.NetInput.Session.SendPing();

            netTick++;
            consecutiveFailures = 0;
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
            try
            {
                string dir = Path.Combine(Program.DataDirectory, "Screenshots");
                Directory.CreateDirectory(dir);

                int n = 1;
                string path;
                do { path = Path.Combine(dir, string.Format("shot{0:0000}.bmp", n++)); }
                while (File.Exists(path));

                presenter.SaveBmp(path);
                overlay.AddLog("screenshot: " + Path.GetFileName(path));
                Log.Info("screenshot: " + path);
            }
            catch (Exception e)
            {
                Log.Error("screenshot failed", e);
            }
        }

        public void Dispose()
        {
            running = false;

            try { Music.Free(); } catch (Exception e) { Log.Warn("music shutdown: " + Log.Describe(e)); }
            try { Sound.Done(); } catch (Exception e) { Log.Warn("sound shutdown: " + Log.Describe(e)); }
            try { Net.NetInput.Stop(); } catch (Exception e) { Log.Warn("net shutdown: " + Log.Describe(e)); }

            presenter.Dispose();
            window.Dispose();
        }
    }
}
