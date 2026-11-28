//
// DPT.DOOM 2D — in-game overlay.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Game;
using DPTDOOM2D.Win32;
using DGame = DPTDOOM2D.Game.Game;

namespace DPTDOOM2D.Ui
{
    /// <summary>
    /// The F2 overlay: a monitor plus the settings that are worth changing while
    /// playing (scale, stretch mode, gamma, volumes, music track, fullscreen).
    ///
    /// Modelled on NesLifter's overlay: tabs along the top, arrows to move and
    /// change, F2 to close. The Monitor tab is passive — you can watch it while
    /// still playing — every other tab takes the keyboard so an arrow key changes
    /// a setting instead of moving the player.
    ///
    /// The layout is computed from the client size on every frame rather than
    /// fixed: at 1x the window is only 320x200, so the panel fills it edge to
    /// edge and the font drops a couple of points; on a 6x window the same code
    /// centres a comfortable 560x380 box. Text is measured with GDI and centred
    /// inside its tab, and every value is right-aligned, so nothing is ever
    /// clipped by a guessed column width.
    ///
    /// Settings write straight into the running game, so there is no "apply"
    /// step and no chance of the overlay and the engine disagreeing.
    /// </summary>
    internal sealed class Overlay
    {
        private static readonly string[] TabNames =
        {
            "Monitor", "Stats", "Debug", "Video", "Audio", "Keys", "Log"
        };

        // Tab indices as constants: the switch statements below are the only place
        // that knows what a number means, and an inserted tab used to renumber every
        // case in the file.
        private const int TabMonitor = 0, TabStats = 1, TabDebug = 2, TabVideo = 3,
                          TabAudio = 4, TabKeys = 5, TabLog = 6;

        private readonly Win32Window window;

        public bool Visible;
        public int Tab;
        public int Row;

        /// <summary>Player whose bindings the Keys tab edits: 1 or 2.</summary>
        public int KeyPlayer = 1;

        /// <summary>Row waiting for a key press, or -1. See <see cref="HandleKey"/>.</summary>
        private int captureRow = -1;

        public int Scale = 2;
        public bool ShowFps = true;
        public bool Fullscreen;

        /// <summary>0 = pixel perfect, 1 = nearest stretch, 2 = smooth stretch.</summary>
        public int Stretch = 1;

        public int PaletteLevel;

        private readonly List<string> logLines = new List<string>();

        // ---- frame statistics ------------------------------------------------
        //
        // A ring buffer rather than an average: a stutter is a spike, and an average
        // hides exactly the thing a developer opens this tab to see. 120 samples at
        // 20 fps is six seconds of history.

        private const int StatsWindow = 120;

        private readonly float[] frameHistory = new float[StatsWindow];
        private readonly float[] tickHistory = new float[StatsWindow];

        private int statsAt;
        private int statsCount;

        /// <summary>Records one frame. Called by the game loop, not by the overlay.</summary>
        public void Sample(double frameMs, double tickMs)
        {
            frameHistory[statsAt] = (float)frameMs;
            tickHistory[statsAt] = (float)tickMs;

            statsAt = (statsAt + 1) % StatsWindow;
            if (statsCount < StatsWindow) ++statsCount;
        }

        /// <summary>Newest sample first, for the summary line.</summary>
        private float Newest(float[] history)
        {
            if (statsCount == 0) return 0;

            return history[(statsAt - 1 + StatsWindow) % StatsWindow];
        }

        private float Average(float[] history)
        {
            if (statsCount == 0) return 0;

            float total = 0;
            for (int i = 0; i < statsCount; ++i) total += history[i];

            return total / statsCount;
        }

        private float Peak(float[] history)
        {
            float best = 0;

            for (int i = 0; i < statsCount; ++i)
                if (history[i] > best) best = history[i];

            return best;
        }

        // ---- debug tab -------------------------------------------------------
        //
        // Rows are a mix of selectors and actions. A selector keeps a value that an
        // action then applies: warping the moment the map number changes would lose
        // the level on a mistyped arrow key, and this tab is opened mid-level.

        private const int DebugRows = 13;

        private const int RowMap = 0, RowWarp = 1, RowExit = 2, RowMonster = 3,
                          RowSpawnMonster = 4, RowItem = 5, RowSpawnItem = 6,
                          RowKillAll = 7, RowRefill = 8, RowReload = 9,
                          RowCheat = 10, RowFly = 11, RowImmortal = 12;

        /// <summary>Map the Warp row will go to.</summary>
        private int debugMap = 1;

        /// <summary>Monster type the Spawn row places; an index into <see cref="DebugActions.MonsterNames"/>.</summary>
        private int debugMonster = 0;

        /// <summary>Item type the Spawn row places; an index into <see cref="DebugActions.ItemNames"/>.</summary>
        private int debugItem = 0;

        private static string[] MonsterNames { get { return DebugActions.MonsterNames; } }
        private static string[] ItemNames { get { return DebugActions.ItemNames; } }

        /// <summary>How many levels this data set has.</summary>
        private static int MapCount() { return DebugActions.MapCount(); }

        /// <summary>
        /// Runs one debug action.
        ///
        /// The work itself lives in <see cref="DebugActions"/> so that `--debugcheck` can
        /// exercise it; this method only decides which action the selected row means.
        /// </summary>
        private void RunDebug(int row, int direction)
        {
            // The three toggles work outside a level as well: they are settings, not
            // actions, and switching them on the title screen then starting a game is
            // a normal thing to want. Everything else needs a level to act on.
            switch (row)
            {
                case RowCheat:
                    DGame.Cheat = !DGame.Cheat;
                    AddLog("debug: читы в deathmatch " + (DGame.Cheat ? "вкл" : "выкл"));
                    return;

                case RowFly:
                    Player.Fly = !Player.Fly;
                    AddLog("debug: полёт " + (Player.Fly ? "вкл" : "выкл"));
                    return;

                case RowImmortal:
                    Player.Immortal = !Player.Immortal;
                    AddLog("debug: бессмертие " + (Player.Immortal ? "вкл" : "выкл"));
                    return;
            }

            if (!DebugActions.InGame)
            {
                AddLog("debug: только во время игры");
                return;
            }

            switch (row)
            {
                case RowMap:
                    {
                        // Both arrows step the selector, in opposite directions, and
                        // wrap. The right arrow used to be the only direction that
                        // existed, so pressing left moved forward too — a selector
                        // that only goes one way is a selector you cannot correct a
                        // mistake with.
                        int count = MapCount();

                        if (direction < 0) debugMap = debugMap <= 1 ? count : debugMap - 1;
                        else debugMap = debugMap >= count ? 1 : debugMap + 1;

                        break;
                    }

                case RowWarp:
                    {
                        int count = MapCount();
                        if (debugMap > count) debugMap = count;

                        DebugActions.Warp(debugMap);
                        AddLog("debug: warp MAP" + debugMap);
                        break;
                    }

                case RowExit:
                    DebugActions.ExitLevel();
                    AddLog("debug: exit level");
                    break;

                case RowMonster:
                    debugMonster = Step(debugMonster, MonsterNames.Length, direction);
                    break;

                case RowSpawnMonster:
                    {
                        int spawned = DebugActions.SpawnMonster(debugMonster);

                        AddLog(spawned < 0
                            ? "debug: нет места или лимит монстров"
                            : "debug: + " + MonsterNames[debugMonster]);
                        break;
                    }

                case RowItem:
                    debugItem = Step(debugItem, ItemNames.Length, direction);
                    break;

                case RowSpawnItem:
                    {
                        bool placed = DebugActions.SpawnItem(debugItem);

                        AddLog(placed
                            ? "debug: + " + ItemNames[debugItem]
                            : "debug: нет места");
                        break;
                    }

                case RowKillAll:
                    AddLog("debug: убито " + DebugActions.KillAll());
                    break;

                case RowRefill:
                    DebugActions.Refill();
                    AddLog("debug: refill");
                    break;

                case RowReload:
                    DebugActions.Reload();
                    AddLog("debug: reload MAP" + DGame.Map);
                    break;
            }
        }

        public Overlay(Win32Window window)
        {
            this.window = window;
        }

        /// <summary>
        /// True while the overlay takes the keyboard away from the game.
        ///
        /// Monitor and Stats are read-only: you open them *while* playing to watch a
        /// graph, so stealing the arrow keys would defeat the purpose. Every other tab
        /// edits something, and an arrow key there must change a value, not move the
        /// player.
        /// </summary>
        public bool LocksInput
        {
            get { return Visible && Tab != TabMonitor && Tab != TabStats; }
        }

        public void Toggle()
        {
            Visible = !Visible;
            if (!Visible) { Row = 0; captureRow = -1; }
        }

        public void AddLog(string line)
        {
            if (line == null) return;

            logLines.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            if (logLines.Count > 40) logLines.RemoveAt(0);
        }

        public void NextTab(int delta)
        {
            Tab = (Tab + delta + TabNames.Length) % TabNames.Length;
            Row = 0;
            captureRow = -1;
        }

        private int RowCount()
        {
            switch (Tab)
            {
                case TabStats: return 0;    // passive, no rows to select
                case TabDebug: return DebugRows;
                case TabVideo: return 5;
                case TabAudio: return 4;
                case TabKeys: return 2 + Controls.Actions.Length;
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

            // The Keys tab waits for a key to bind: the next press is the answer,
            // whatever it is. Escape abandons the capture instead of binding it.
            if (captureRow >= 0)
            {
                if (sym != Kbd.SDLK_ESCAPE) BindKey(captureRow, sym);
                captureRow = -1;
                return true;
            }

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
            try
            {
                ChangeSetting(direction);
            }
            catch (Exception e)
            {
                // A setting must never take the game down with it: the most
                // likely failure here is a music track that does not exist.
                AddLog("error: " + Log.Describe(e));
                Log.Error("overlay: changing a setting failed", e);
            }
        }

        private void ChangeSetting(int direction)
        {
            switch (Tab)
            {
                case TabDebug:
                    RunDebug(Row, direction);
                    break;

                case TabVideo:
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
                        Stretch = (Stretch + direction + 3) % 3;
                        window.Stretch = (Win32Window.StretchMode)Stretch;
                    }
                    else if (Row == 2)
                    {
                        PaletteLevel = Math.Max(0, Math.Min(4, PaletteLevel + direction));
                        Palette.SetGamma(PaletteLevel);
                        Config.Gamma = PaletteLevel;
                    }
                    else if (Row == 3) ShowFps = !ShowFps;
                    else if (Row == 4)
                    {
                        Fullscreen = !Fullscreen;
                        window.SetFullscreen(Fullscreen);
                    }
                    break;

                case TabAudio:
                    if (Row == 0)
                    {
                        Sound.Volume(Math.Max(0, Math.Min(128, Sound.SndVol + direction * 8)));
                    }
                    else if (Row == 1)
                    {
                        Music.Volume(Math.Max(0, Math.Min(128, Music.MusVol + direction * 8)));
                    }
                    else if (Row == 2)
                    {
                        string next = Music.Skip(direction);
                        AddLog("music: " + Music.Describe(next) + " (" + Music.Source + ")");
                    }
                    else if (Row == 3)
                    {
                        AudioSystem.Mixer.Muted = !AudioSystem.Mixer.Muted;
                    }
                    break;

                case TabKeys:
                    if (Row == 0) CycleLayout();
                    else if (Row == 1) KeyPlayer = KeyPlayer == 1 ? 2 : 1;
                    else if (direction != 0) captureRow = Row;
                    break;
            }
        }

        /// <summary>
        /// Switches between the two presets. There are only two, so either arrow
        /// goes to the other one; the keys are reset, which is what a preset means.
        /// </summary>
        private void CycleLayout()
        {
            int preset = Controls.BasePreset;
            Controls.Apply(preset == 1 ? 2 : 1);
            AddLog("keys: layout " + Controls.PresetName(Controls.BasePreset));
            SaveControls();
        }

        /// <summary>Binds the key that was just pressed to the row being captured.</summary>
        private void BindKey(int row, int sym)
        {
            PlayerData p = KeyPlayer == 2 ? DGame.Pl2 : DGame.Pl1;
            string action = Controls.Actions[row - 2];

            Controls.Set(p, action, sym);
            // BasePreset stays: these keys are preset N with an edit, and the launcher
            // asking for preset N again must keep the edit rather than drop it.

            AddLog("keys: " + (KeyPlayer == 2 ? "P2 " : "P1 ")
                + Controls.ActionName(action) + " = " + Controls.KeyName(sym));
            SaveControls();
        }

        private void SaveControls()
        {
            if (!Config.SaveControls())
                AddLog("keys: could not write Storage/" + Config.CfgFile + " (read-only folder?)");
        }

        // ------------------------------------------------------------------ draw

        /// <summary>Metrics for one frame: everything the drawing code needs.</summary>
        private struct Layout
        {
            public int X, Y, Width, Height;      // panel
            public int Pad;
            public int Font, SmallFont, LineHeight;
            public int TabFont, TabHeight;
            public bool Compact;
        }

        private Layout Measure(IntPtr dc, int width, int height)
        {
            var l = new Layout();

            // "Compact" is the 1x window: 320x200 leaves no room for margins, so
            // the panel goes full bleed and the text drops a couple of points.
            l.Compact = width < 520 || height < 380;

            l.Font = l.Compact ? 11 : 14;
            l.SmallFont = l.Compact ? 10 : 13;
            l.LineHeight = l.Compact ? 13 : 19;
            l.Pad = l.Compact ? 5 : 12;

            int margin = l.Compact ? 0 : 10;
            l.Width = width - margin * 2;
            l.Height = height - margin * 2;

            if (!l.Compact)
            {
                if (l.Width > 580) l.Width = 580;
                if (l.Height > 400) l.Height = 400;
            }

            l.X = (width - l.Width) / 2;
            l.Y = (height - l.Height) / 2;

            // Tabs: pick the largest font whose strip still fits the panel.
            l.TabFont = l.Compact ? 11 : 14;
            for (int attempt = 0; attempt < 4; ++attempt)
            {
                if (TabStripWidth(dc, l.TabFont) <= l.Width - l.Pad * 2) break;
                l.TabFont -= 1;
                if (l.TabFont < 9) break;
            }

            int textW, textH;
            window.MeasureText(dc, "Ag", true, l.TabFont, out textW, out textH);
            l.TabHeight = textH + (l.Compact ? 5 : 8);

            return l;
        }

        private int TabStripWidth(IntPtr dc, int font)
        {
            int total = 0;
            for (int i = 0; i < TabNames.Length; ++i)
            {
                int w, h;
                window.MeasureText(dc, TabNames[i], true, font, out w, out h);
                total += w + 14 + 4;      // box padding + gap
            }
            return total;
        }

        public void Draw(IntPtr dc, int width, int height, int fps)
        {
            if (!Visible) return;

            Layout l = Measure(dc, width, height);

            window.FillRect(dc, l.X, l.Y, l.Width, l.Height, 240, 12, 16, 26);
            window.FillRect(dc, l.X, l.Y, l.Width, 2, 255, 255, 77, 90);
            window.FillRect(dc, l.X, l.Y + l.Height - 2, l.Width, 2, 255, 255, 77, 90);

            uint accent = NativeMethods.Rgb(255, 77, 90);
            uint text = NativeMethods.Rgb(230, 230, 230);
            uint muted = NativeMethods.Rgb(150, 150, 160);

            DrawTabs(dc, l, accent, muted);

            int contentX = l.X + l.Pad;
            int contentY = l.Y + l.TabHeight + (l.Compact ? 5 : 10);
            int contentW = l.Width - l.Pad * 2;
            int hintHeight = l.Compact ? 0 : l.LineHeight + 6;
            int contentH = l.Height - (contentY - l.Y) - hintHeight;

            switch (Tab)
            {
                case TabMonitor: DrawMonitor(dc, l, contentX, contentY, contentW, contentH, fps); break;
                case TabStats: DrawStats(dc, l, contentX, contentY, contentW, contentH, fps, accent, text, muted); break;
                case TabDebug: DrawDebug(dc, l, contentX, contentY, contentW, contentH, accent, text); break;
                case TabVideo: DrawVideo(dc, l, contentX, contentY, contentW, accent, text); break;
                case TabAudio: DrawAudio(dc, l, contentX, contentY, contentW, accent, text); break;
                case TabKeys: DrawKeys(dc, l, contentX, contentY, contentW, contentH, accent, text); break;
                case TabLog: DrawLog(dc, l, contentX, contentY, contentW, contentH, text); break;
            }

            // At 1x there is no room for the hint row; the tab strip and F2 are
            // the whole story anyway.
            if (l.Compact) return;

            string hint;
            if (Tab == TabKeys && captureRow >= 0)
                hint = "press a key to bind it   ESC cancels";
            else if (Tab == TabKeys)
                hint = "TAB switch   arrows change   Enter rebinds   F2 close";
            else if (Tab == TabDebug)
                hint = "TAB switch   arrows run the action   F2 close";
            else
                hint = LocksInput
                    ? "TAB switch   arrows change   F2 close"
                    : "TAB switch   F2 close   (game input active)";

            window.DrawText(dc, hint, l.X + l.Pad, l.Y + l.Height - l.LineHeight - 6,
                muted, false, l.SmallFont);
        }

        /// <summary>
        /// Tab strip: each label is measured and centred inside its own box, and
        /// the box grows with the text instead of assuming 9 pixels per character.
        /// </summary>
        private void DrawTabs(IntPtr dc, Layout l, uint accent, uint muted)
        {
            int x = l.X + l.Pad;
            int y = l.Y + (l.Compact ? 4 : 8);

            for (int i = 0; i < TabNames.Length; ++i)
            {
                int textW, textH;
                window.MeasureText(dc, TabNames[i], true, l.TabFont, out textW, out textH);

                int boxW = textW + 14;
                int boxH = textH + (l.Compact ? 3 : 6);
                bool active = i == Tab;

                if (active)
                {
                    window.FillRect(dc, x, y, boxW, boxH, 255, 40, 46, 70);
                    window.FillRect(dc, x, y + boxH - 1, boxW, 1, 255, 255, 77, 90);
                }

                // Centre horizontally and vertically: (box - text) / 2 on both axes.
                int tx = x + (boxW - textW) / 2;
                int ty = y + (boxH - textH) / 2;

                window.DrawText(dc, TabNames[i], tx, ty, active ? accent : muted, active, l.TabFont);

                x += boxW + 4;
            }
        }

        private void DrawMonitor(IntPtr dc, Layout l, int x, int y, int width, int height, int fps)
        {
            uint text = NativeMethods.Rgb(230, 230, 230);
            uint muted = NativeMethods.Rgb(150, 150, 160);

            var lines = new List<string>();

            lines.Add("fps            " + fps);
            lines.Add("map            " + DGame.Map);
            lines.Add("state          " + StateName(DGame.State));
            lines.Add("players        " + (DGame.TwoPl ? "2" : "1"));
            lines.Add("player x,y     " + DGame.Pl1.O.X + ", " + DGame.Pl1.O.Y);
            lines.Add("life / armor   " + DGame.Pl1.Life + " / " + DGame.Pl1.Armor);
            lines.Add("weapon         " + DGame.Pl1.Wpn + "  ammo " + DGame.Pl1.Ammo);
            lines.Add("monsters       " + Monsters.Count);
            lines.Add("items          " + Items.Count);
            lines.Add("track          " + (Music.IsPlaying ? Music.TrackName : "off")
                + "  [" + Music.Source + "]");
            lines.Add("scale          " + (Scale == 0 ? "fit" : Scale + "x")
                + "  " + Config.StretchName(Stretch));
            if (Net.NetInput.Active) lines.Add("net            " + Net.NetInput.Status());

            int maxLines = Math.Max(1, height / l.LineHeight);
            int count = Math.Min(lines.Count, maxLines);

            for (int i = 0; i < count; ++i)
                window.DrawText(dc, lines[i], x, y + i * l.LineHeight, i < 2 ? text : muted, false, l.Font);
        }

        /// <summary>
        /// Stats tab: what the machine is doing, as numbers and as a graph.
        ///
        /// The graph is the point. A frame time that averages 5 ms but spikes to 40
        /// twice a second reads as "runs fine" in any single number and as obvious
        /// stutter to a player, so the history is drawn rather than summarised.
        /// </summary>
        private void DrawStats(IntPtr dc, Layout l, int x, int y, int width, int height,
            int fps, uint accent, uint text, uint muted)
        {
            float frameNow = Newest(frameHistory);
            float frameAvg = Average(frameHistory);
            float framePeak = Peak(frameHistory);

            float tickNow = Newest(tickHistory);
            float tickPeak = Peak(tickHistory);

            int line = y;

            window.DrawText(dc, "fps            " + fps, x, line, text, false, l.Font);
            line += l.LineHeight;

            window.DrawText(dc, string.Format("work       {0:0.0} ms  avg {1:0.0}  peak {2:0.0}",
                frameNow, frameAvg, framePeak), x, line, muted, false, l.Font);
            line += l.LineHeight;

            window.DrawText(dc, string.Format("game tick  {0:0.0} ms  peak {1:0.0}   budget 50",
                tickNow, tickPeak), x, line, muted, false, l.Font);
            line += l.LineHeight + 4;

            // The graph takes whatever height is left after the numbers, but never
            // less than a few rows: a graph two pixels tall is not a graph.
            int graphTop = line;
            int reserved = l.LineHeight * 3;

            int graphHeight = height - (graphTop - y) - reserved;
            if (graphHeight < 16) graphHeight = 16;

            DrawGraph(dc, l, x, graphTop, width, graphHeight, accent);

            line = graphTop + graphHeight + 4;

            // Below the graph: the counts that explain a spike when it happens.
            string entities = "monsters " + Monsters.Count + "   items " + Items.Count
                + "   smoke " + Smoke.Count + "   dots " + Dots.Count;

            window.DrawText(dc, entities, x, line, muted, false, l.SmallFont);
            line += l.LineHeight;

            string audio = "audio " + (AudioSystem.Available ? "on" : "off")
                + "   voices " + AudioSystem.Mixer.ActiveVoices
                + "   music " + (Music.IsPlaying ? "playing" : "off");

            window.DrawText(dc, audio, x, line, muted, false, l.SmallFont);
            line += l.LineHeight;

            long memory = GC.GetTotalMemory(false) / 1024;

            window.DrawText(dc, "memory " + memory + " KB   gen0 " + GC.CollectionCount(0)
                + "   gen1 " + GC.CollectionCount(1), x, line, muted, false, l.SmallFont);
        }

        /// <summary>
        /// Frame times as a bar per sample, oldest on the left.
        ///
        /// The vertical scale is fixed to the tick budget rather than to the peak: a
        /// graph that rescales itself to whatever it is showing hides the difference
        /// between "smooth at 5 ms" and "stuttering at 5 ms average". 50 ms is the
        /// tick, so anything above the top of the box is a dropped frame.
        /// </summary>
        private void DrawGraph(IntPtr dc, Layout l, int x, int y, int width, int height, uint accent)
        {
            const float Ceiling = 50f;      // one tick at 20 Hz

            window.FillRect(dc, x, y, width, height, 255, 20, 24, 36);

            // A baseline at the tick length: the height a healthy frame sits at.
            int baseline = y + height - 1;

            window.FillRect(dc, x, baseline, width, 1, 255, 60, 66, 90);

            if (statsCount == 0) return;

            int columns = Math.Min(width, statsCount);

            for (int i = 0; i < columns; ++i)
            {
                // Oldest first: the ring buffer's head is the newest sample.
                int at = (statsAt - columns + i + StatsWindow * 2) % StatsWindow;
                float ms = frameHistory[at];

                int bar = (int)(ms * (height - 1) / Ceiling);
                if (bar > height - 1) bar = height - 1;
                if (bar < 1) bar = 1;

                // A frame that overran the tick is drawn in the accent colour, so a
                // stutter is visible without reading the numbers.
                bool over = ms > Ceiling;

                int colX = x + i * width / columns;
                int colW = Math.Max(1, width / columns - 1);

                if (over)
                    window.FillRect(dc, colX, y + height - 1 - bar, colW, bar, 255, 255, 77, 90);
                else
                    window.FillRect(dc, colX, y + height - 1 - bar, colW, bar, 255, 77, 120, 160);
            }

            // A caption in the corner: the scale is meaningless without it.
            window.DrawText(dc, "0-50 ms", x + 2, y + 1, NativeMethods.Rgb(110, 115, 135),
                false, l.SmallFont);
        }

        /// <summary>
        /// Debug tab: selectors on the left of the value column, actions that run the
        /// moment an arrow key is pressed.
        ///
        /// The distinction is drawn rather than explained: a selector shows a value, an
        /// action shows "run →". Pressing either arrow on an action runs it, because a
        /// debug action has no direction — the arrow is just the key that is already
        /// under the finger.
        /// </summary>
        private void DrawDebug(IntPtr dc, Layout l, int x, int y, int width, int height,
            uint accent, uint text)
        {
            string[] labels =
            {
                "Map", "Warp to map", "Exit level",
                "Monster", "Spawn monster", "Item", "Spawn item",
                "Kill all monsters", "Refill player", "Reload level",
                "Cheats in deathmatch", "Fly", "Immortal"
            };

            string[] values =
            {
                "MAP" + debugMap.ToString("00"),
                "run \u2192",
                "run \u2192",
                MonsterNames[debugMonster],
                "run \u2192",
                ItemNames[debugItem],
                "run \u2192",
                "run \u2192",
                "run \u2192",
                "run \u2192",
                DGame.Cheat ? "on" : "off",
                Player.Fly ? "on" : "off",
                Player.Immortal ? "on" : "off"
            };

            int lineHeight = l.LineHeight + 4;
            int visible = Math.Max(1, height / lineHeight);
            int first = Row >= visible ? Row - visible + 1 : 0;
            if (first + labels.Length > labels.Length) first = Math.Max(0, labels.Length - visible);

            int shown = Math.Min(visible, labels.Length - first);

            string[] visibleLabels = new string[shown];
            string[] visibleValues = new string[shown];
            Array.Copy(labels, first, visibleLabels, 0, shown);
            Array.Copy(values, first, visibleValues, 0, shown);

            DrawRowsWindow(dc, l, x, y, width, visibleLabels, visibleValues, accent, text, first);

            // A line under the list: what the selected row would do, in words. A tab
            // full of "run →" is otherwise a guess-the-button game.
            int hintY = y + shown * lineHeight + 4;

            if (hintY + l.LineHeight > y + height) return;

            window.DrawText(dc, DescribeDebugRow(Row), x, hintY,
                NativeMethods.Rgb(150, 150, 160), false, l.SmallFont);
        }

        /// <summary>
        /// Moves a selector index by one in the given direction, wrapping at both ends.
        ///
        /// Shared by the debug selectors so that "left" means the same thing in all of
        /// them: the map row used to step forward on either arrow, which made a wrong
        /// choice impossible to undo without going all the way round.
        /// </summary>
        private static int Step(int value, int count, int direction)
        {
            if (count <= 0) return 0;

            if (direction < 0) return value <= 0 ? count - 1 : value - 1;

            return value >= count - 1 ? 0 : value + 1;
        }

        /// <summary>One sentence about the selected debug row.</summary>
        private string DescribeDebugRow(int row)
        {
            switch (row)
            {
                case RowMap: return "choose the level to warp to";
                case RowWarp: return "leave this level and start MAP" + debugMap.ToString("00");
                case RowExit: return "finish the level as if you had reached the exit";
                case RowMonster: return "choose what to spawn";
                case RowSpawnMonster: return "place " + MonsterNames[debugMonster] + " in front of you";
                case RowItem: return "choose what to drop";
                case RowSpawnItem: return "place " + ItemNames[debugItem] + " in front of you";
                case RowKillAll: return "remove every monster from the level";
                case RowRefill: return "100 hp, 100 armour, all weapons and keys, full ammo";
                case RowReload: return "restart the level you are on";
                case RowCheat: return "allow the cheat codes in a deathmatch";
                case RowFly: return "fly through walls and ceilings";
                case RowImmortal: return "take no damage";
                default: return "";
            }
        }

        private void DrawVideo(IntPtr dc, Layout l, int x, int y, int width, uint accent, uint text)        {
            string[] labels = { "Scale", "Stretch", "Brightness", "Show FPS", "Fullscreen" };
            string[] values = {
                Scale == 0 ? "fit to window" : Scale + "x",
                Config.StretchName(Stretch),
                PaletteLevel + " / 4",
                ShowFps ? "on" : "off",
                Fullscreen ? "on" : "off"
            };

            DrawRows(dc, l, x, y, width, labels, values, accent, text);
        }

        private void DrawAudio(IntPtr dc, Layout l, int x, int y, int width, uint accent, uint text)
        {
            string[] labels = { "Sound volume", "Music volume", "Change track", "Mute" };
            string[] values = {
                Sound.SndVol.ToString(),
                Music.MusVol.ToString(),
                Music.Describe(Music.Current),
                AudioSystem.Mixer.Muted ? "on" : "off"
            };

            DrawRows(dc, l, x, y, width, labels, values, accent, text);
        }

        /// <summary>
        /// Keys tab: a layout preset, which player is being edited, then the nine
        /// actions. The list scrolls when the panel is too short for all eleven rows
        /// (the 1x window), always keeping the selected row on screen.
        /// </summary>
        private void DrawKeys(IntPtr dc, Layout l, int x, int y, int width, int height,
            uint accent, uint text)
        {
            int count = 2 + Controls.Actions.Length;
            string[] labels = new string[count];
            string[] values = new string[count];

            labels[0] = "Layout";
            values[0] = Controls.PresetName(Controls.MatchPreset());

            labels[1] = "Player";
            values[1] = "P" + KeyPlayer;

            PlayerData p = KeyPlayer == 2 ? DGame.Pl2 : DGame.Pl1;
            for (int i = 0; i < Controls.Actions.Length; ++i)
            {
                labels[2 + i] = Controls.ActionName(Controls.Actions[i]);
                values[2 + i] = Controls.KeyName(Controls.Get(p, Controls.Actions[i]));
            }

            if (captureRow >= 0) values[captureRow] = "<press a key>";

            int lineHeight = l.LineHeight + 4;
            int visible = Math.Max(1, height / lineHeight);
            int first = Row >= visible ? Row - visible + 1 : 0;
            if (first + visible > count) first = Math.Max(0, count - visible);

            int shown = Math.Min(visible, count - first);
            string[] visibleLabels = new string[shown];
            string[] visibleValues = new string[shown];
            Array.Copy(labels, first, visibleLabels, 0, shown);
            Array.Copy(values, first, visibleValues, 0, shown);

            DrawRowsWindow(dc, l, x, y, width, visibleLabels, visibleValues, accent, text, first);
        }

        private void DrawLog(IntPtr dc, Layout l, int x, int y, int width, int height, uint text)
        {
            if (logLines.Count == 0)
            {
                window.DrawText(dc, "(empty)", x, y, NativeMethods.Rgb(150, 150, 160), false, l.Font);
                return;
            }

            int maxLines = Math.Max(1, height / l.LineHeight);
            int first = Math.Max(0, logLines.Count - maxLines);
            int shown = logLines.Count - first;

            for (int i = 0; i < shown; ++i)
            {
                string line = Fit(dc, logLines[first + i], width, l.SmallFont);
                window.DrawText(dc, line, x, y + i * l.LineHeight, text, false, l.SmallFont);
            }
        }

        /// <summary>Trims a line with an ellipsis until it fits the given width.</summary>
        private string Fit(IntPtr dc, string line, int width, int font)
        {
            int w, h;
            window.MeasureText(dc, line, false, font, out w, out h);
            if (w <= width) return line;

            string trimmed = line;
            while (trimmed.Length > 1)
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
                window.MeasureText(dc, trimmed + "...", false, font, out w, out h);
                if (w <= width) return trimmed + "...";
            }

            return "";
        }

        /// <summary>Label on the left, value right-aligned in the panel.</summary>
        private void DrawRows(IntPtr dc, Layout l, int x, int y, int width,
            string[] labels, string[] values, uint accent, uint text)
        {
            DrawRowsWindow(dc, l, x, y, width, labels, values, accent, text, 0);
        }

        /// <summary>
        /// Same, but the first entry stands for row <paramref name="firstRow"/> of the
        /// full list: the Keys tab scrolls, and the highlight has to follow the real
        /// row number rather than the position on screen.
        /// </summary>
        private void DrawRowsWindow(IntPtr dc, Layout l, int x, int y, int width,
            string[] labels, string[] values, uint accent, uint text, int firstRow)
        {
            for (int i = 0; i < labels.Length; ++i)
            {
                bool selected = firstRow + i == Row;
                int lineY = y + i * (l.LineHeight + 4);

                if (selected)
                    window.FillRect(dc, x - (l.Compact ? 2 : 4), lineY - 2, width + (l.Compact ? 4 : 8),
                        l.LineHeight + 3, 255, 34, 40, 62);

                window.DrawText(dc, (selected ? "> " : "  ") + labels[i], x, lineY,
                    selected ? accent : text, selected, l.Font);

                int valueW, valueH;
                window.MeasureText(dc, values[i], false, l.Font, out valueW, out valueH);
                window.DrawText(dc, values[i], x + width - valueW, lineY, text, false, l.Font);
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
