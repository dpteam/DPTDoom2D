//
// DPT.DOOM 2D — in-game overlay.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Game;
using DPTDOOM2D.Win32;
using DGame = DPTDOOM2D.Game.Game;

namespace DPTDOOM2D.Ui
{
    /// <summary>
    /// The F2 overlay: a monitor plus the settings that are worth changing while
    /// playing (scale, gamma, volumes, music track, fullscreen).
    ///
    /// Modelled on NesLifter's overlay: tabs along the top, arrows to move and
    /// change, F2 to close. The Monitor tab is passive — you can watch it while
    /// still playing — every other tab takes the keyboard so an arrow key changes
    /// a setting instead of moving the player.
    ///
    /// Settings write straight into the running game, so there is no "apply"
    /// step and no chance of the overlay and the engine disagreeing.
    /// </summary>
    internal sealed class Overlay
    {
        private static readonly string[] TabNames = { "Monitor", "Video", "Audio", "Game", "Log" };

        private readonly Win32Window window;

        public bool Visible;
        public int Tab;
        public int Row;

        public int Scale = 2;
        public bool ShowFps = true;
        public bool Fullscreen;

        private string log = "";
        private readonly System.Collections.Generic.List<string> logLines = new System.Collections.Generic.List<string>();

        public Overlay(Win32Window window)
        {
            this.window = window;
        }

        public bool LocksInput { get { return Visible && Tab != 0; } }

        public void Toggle()
        {
            Visible = !Visible;
            if (!Visible) Row = 0;
        }

        public void Log(string line)
        {
            logLines.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            if (logLines.Count > 12) logLines.RemoveAt(0);
            log = string.Join("\n", logLines.ToArray());
        }

        public void NextTab(int delta)
        {
            Tab = (Tab + delta + TabNames.Length) % TabNames.Length;
            Row = 0;
        }

        private int RowCount()
        {
            switch (Tab)
            {
                case 1: return 5;   // Video
                case 2: return 4;   // Audio
                case 3: return 3;   // Game
                default: return 0;
            }
        }

        /// <summary>Handles one key. Returns true when the overlay consumed it.</summary>
        public bool HandleKey(int sym)
        {
            if (sym == Kbd.SDLK_F2)
            {
                Toggle();
                return true;
            }

            if (!Visible) return false;

            switch (sym)
            {
                case Kbd.SDLK_TAB:
                    NextTab(1);
                    return true;

                case Kbd.SDLK_UP:
                    Row = Math.Max(0, Row - 1);
                    return LocksInput;

                case Kbd.SDLK_DOWN:
                    {
                        int count = RowCount();
                        Row = count == 0 ? 0 : Math.Min(count - 1, Row + 1);
                        return LocksInput;
                    }

                case Kbd.SDLK_LEFT:
                    Change(-1);
                    return LocksInput;

                case Kbd.SDLK_RIGHT:
                case Kbd.SDLK_RETURN:
                    Change(1);
                    return LocksInput;
            }

            return LocksInput;
        }

        private static readonly int[] ScaleChoices = { 1, 2, 3, 4, 5, 6, 0 };

        private void Change(int direction)
        {
            switch (Tab)
            {
                case 1:
                    if (Row == 0)
                    {
                        int at = Array.IndexOf(ScaleChoices, Scale);
                        if (at < 0) at = 1;
                        at = (at + direction + ScaleChoices.Length) % ScaleChoices.Length;
                        Scale = ScaleChoices[at];
                        window.ApplyScale(Scale);
                    }
                    else if (Row == 1)
                    {
                        Palette.SetGamma(PaletteLevel + direction);
                        PaletteLevel = Math.Max(0, Math.Min(4, PaletteLevel + direction));
                    }
                    else if (Row == 2) ShowFps = !ShowFps;
                    else if (Row == 3)
                    {
                        Fullscreen = !Fullscreen;
                        window.SetFullscreen(Fullscreen);
                    }
                    else if (Row == 4)
                    {
                        // Nearest-neighbour is the only sensible mode for pixel art;
                        // the row exists so the choice is visible, not adjustable.
                        
                    }
                    break;

                case 2:
                    if (Row == 0) Sound.Volume(Math.Max(0, Math.Min(128, Sound.SndVol + direction * 8)));
                    else if (Row == 1) Music.Volume(Math.Max(0, Math.Min(128, Music.MusVol + direction * 8)));
                    else if (Row == 2) SkipTrack(direction);
                    else if (Row == 3)
                    {
                        AudioSystem.Mixer.Muted = !AudioSystem.Mixer.Muted;
                    }
                    break;

                case 3:
                    if (Row == 0) DGame.Cheat = !DGame.Cheat;
                    else if (Row == 1) Player.Fly = !Player.Fly;
                    else if (Row == 2) Player.Immortal = !Player.Immortal;
                    break;
            }
        }

        public int PaletteLevel;

        private void SkipTrack(int direction)
        {
            string name = Music.Current;
            string next = direction > 0 ? Wad.NextMus(name) : name;

            if (direction > 0)
            {
                Music.Load(next);
            }

            Music.Start(DGame.MusicTime);
            Log("music: " + (next ?? "?"));
        }

        // ------------------------------------------------------------------ draw

        public void Draw(IntPtr dc, int width, int height, int fps)
        {
            if (!Visible) return;

            const int Pad = 10;
            const int LineHeight = 18;

            int panelW = Math.Min(width - Pad * 2, 470);
            int panelH = Math.Min(height - Pad * 2, 330);
            int x = (width - panelW) / 2;
            int y = (height - panelH) / 2;

            window.FillRect(dc, x, y, panelW, panelH, 240, 12, 16, 26);
            window.FillRect(dc, x, y, panelW, 2, 255, 255, 77, 90);
            window.FillRect(dc, x, y + panelH - 2, panelW, 2, 255, 255, 77, 90);

            uint accent = NativeMethods.Rgb(255, 77, 90);
            uint text = NativeMethods.Rgb(230, 230, 230);
            uint muted = NativeMethods.Rgb(150, 150, 160);

            // tab strip
            int tx = x + Pad;
            for (int i = 0; i < TabNames.Length; ++i)
            {
                bool active = i == Tab;
                if (active) window.FillRect(dc, tx - 4, y + 8, TabNames[i].Length * 9 + 10, LineHeight + 2, 255, 40, 46, 70);
                window.DrawText(dc, TabNames[i], tx, y + 8, active ? accent : muted, active, 14);
                tx += TabNames[i].Length * 9 + 18;
            }

            int line = y + 40;

            switch (Tab)
            {
                case 0: DrawMonitor(dc, x + Pad, line, panelW - Pad * 2, fps); break;
                case 1: DrawVideo(dc, x + Pad, line, accent, text); break;
                case 2: DrawAudio(dc, x + Pad, line, accent, text); break;
                case 3: DrawGame(dc, x + Pad, line, accent, text); break;
                case 4: DrawLog(dc, x + Pad, line, text); break;
            }

            string hint = LocksInput ? "TAB switch   arrows change   F2 close" : "TAB switch   F2 close   (game input active)";
            window.DrawText(dc, hint, x + Pad, y + panelH - 26, muted, false, 13);
        }

        private void DrawMonitor(IntPtr dc, int x, int y, int width, int fps)
        {
            uint text = NativeMethods.Rgb(230, 230, 230);
            uint muted = NativeMethods.Rgb(150, 150, 160);

            string[] lines = {
                "fps            " + fps,
                "map            " + DGame.Map,
                "state          " + StateName(DGame.State),
                "players        " + (DGame.TwoPl ? "2" : "1"),
                "player x,y     " + DGame.Pl1.O.X + ", " + DGame.Pl1.O.Y,
                "life / armor   " + DGame.Pl1.Life + " / " + DGame.Pl1.Armor,
                "weapon         " + DGame.Pl1.Wpn + "  ammo " + DGame.Pl1.Ammo,
                "monsters       " + Monsters.Count,
                "items          " + Items.Count,
                "music          " + (Music.IsPlaying ? Music.TrackName : "off"),
                "scale          " + (Scale == 0 ? "fit" : Scale + "x"),
            };

            for (int i = 0; i < lines.Length; ++i)
            {
                window.DrawText(dc, lines[i], x, y + i * 18, i < 2 ? text : muted, false, 14);
            }
        }

        private void DrawVideo(IntPtr dc, int x, int y, uint accent, uint text)
        {
            string[] labels = {
                "Scale", "Brightness", "Show FPS", "Fullscreen", "Filtering"
            };
            string[] values = {
                Scale == 0 ? "fit to window" : Scale + "x",
                PaletteLevel.ToString(),
                ShowFps ? "on" : "off",
                Fullscreen ? "on" : "off",
                "nearest"
            };

            DrawRows(dc, x, y, labels, values, accent, text);
        }

        private void DrawAudio(IntPtr dc, int x, int y, uint accent, uint text)
        {
            string[] labels = { "Sound volume", "Music volume", "Next track", "Mute" };
            string[] values = {
                Sound.SndVol.ToString(),
                Music.MusVol.ToString(),
                Music.Current ?? "-",
                AudioSystem.Mixer.Muted ? "on" : "off"
            };

            DrawRows(dc, x, y, labels, values, accent, text);
        }

        private void DrawGame(IntPtr dc, int x, int y, uint accent, uint text)
        {
            string[] labels = { "Cheats in deathmatch", "Fly", "Immortal" };
            string[] values = {
                DGame.Cheat ? "on" : "off",
                Player.Fly ? "on" : "off",
                Player.Immortal ? "on" : "off"
            };

            DrawRows(dc, x, y, labels, values, accent, text);
        }

        private void DrawLog(IntPtr dc, int x, int y, uint text)
        {
            if (logLines.Count == 0)
            {
                window.DrawText(dc, "(empty)", x, y, NativeMethods.Rgb(150, 150, 160), false, 14);
                return;
            }

            for (int i = 0; i < logLines.Count; ++i)
                window.DrawText(dc, logLines[i], x, y + i * 16, text, false, 13);
        }

        private void DrawRows(IntPtr dc, int x, int y, string[] labels, string[] values, uint accent, uint text)
        {
            for (int i = 0; i < labels.Length; ++i)
            {
                bool selected = i == Row;
                int lineY = y + i * 20;

                if (selected) window.FillRect(dc, x - 4, lineY - 2, 440, 19, 255, 34, 40, 62);

                window.DrawText(dc, (selected ? "> " : "  ") + labels[i], x, lineY, selected ? accent : text, selected, 14);
                window.DrawText(dc, values[i], x + 260, lineY, text, false, 14);
            }
        }

        private static string StateName(int state)
        {
            switch (state)
            {
                case 0: return "title";
                case 1: return "game";
                case 2: return "intermission";
                case 6: return "end screen";
                default: return state.ToString();
            }
        }
    }
}
