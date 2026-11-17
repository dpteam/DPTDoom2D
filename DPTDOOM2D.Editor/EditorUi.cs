//
// DPT.DOOM 2D — the editor's UI chrome, drawn with GDI on top of the frame.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using DPTDOOM2D.Win32;

namespace DPTDOOM2D.Editor
{
    /// <summary>
    /// Draws the panels, labels and prompts on top of the presented frame.
    ///
    /// The map itself is drawn into the 8-bit framebuffer with the game's textures,
    /// exactly like the original editor. Text cannot be: the WAD's font is ASCII, and
    /// a Russian UI needs Cyrillic. GDI draws it at window resolution, which is also
    /// why the labels stay sharp at 3x and 4x.
    ///
    /// Every coordinate here is in 320x200 framebuffer space and goes through
    /// <see cref="Win32Window.FrameToClient"/>, so the text lands on the pixels the
    /// blitter put there — including the letterbox offsets in pixel-perfect mode.
    /// </summary>
    internal sealed class EditorUi
    {
        /// <summary>Logical y of the first tool row in the right panel.</summary>
        public const int ToolTop = 26;

        /// <summary>Logical height of one clickable row.</summary>
        public const int RowHeight = 13;

        /// <summary>Logical y of the first layer toggle.</summary>
        public const int ToggleTop = 26 + 4 * RowHeight + 12;

        private readonly Win32Window window;

        private int factorX, factorY, offX, offY;
        private int font, smallFont;

        public EditorUi(Win32Window window)
        {
            this.window = window;
        }

        /// <summary>Recomputes the layout once per frame: the window may be resized.</summary>
        public void Measure()
        {
            window.FrameLayout(out factorX, out factorY, out offX, out offY);

            int scale = factorX > 0 ? factorX : 1;
            if (factorY < scale && factorY > 0) scale = factorY;

            font = Math.Max(9, scale * 6);
            smallFont = Math.Max(8, scale * 5);
        }

        private int X(int logical) { return offX + logical * factorX; }
        private int Y(int logical) { return offY + logical * factorY; }
        private int W(int logical) { return logical * factorX; }
        private int H(int logical) { return logical * factorY; }

        /// <summary>Draws the whole chrome: panel, palette frame, status, prompts.</summary>
        public void Draw(IntPtr dc, EditorScreen screen)
        {
            Measure();

            DrawPanel(dc, screen);
            DrawPaletteFrame(dc, screen);

            if (screen.PromptActive) DrawPrompt(dc, screen);
            if (screen.Help) DrawHelp(dc);
        }

        private void DrawPanel(IntPtr dc, EditorScreen screen)
        {
            uint text = NativeMethods.Rgb(232, 234, 240);
            uint muted = NativeMethods.Rgb(150, 156, 172);
            uint accent = NativeMethods.Rgb(255, 96, 108);

            window.FillRect(dc, X(EditorScreen.PanelX), Y(EditorScreen.PanelY),
                W(EditorScreen.PanelW), H(EditorScreen.PanelH), 255, 22, 24, 32);

            window.FillRect(dc, X(EditorScreen.PanelX), Y(EditorScreen.PanelY),
                W(1), H(EditorScreen.PanelH), 255, 70, 76, 96);

            int x = EditorScreen.PanelX + 6;

            window.DrawText(dc, screen.MapName.Length == 0 ? "карта" : screen.MapName,
                X(x), Y(6), accent, true, font);

            string[] tools = { "1  задний", "2  передний", "3  типы", "4  объекты" };

            for (int i = 0; i < tools.Length; ++i)
            {
                int rowY = ToolTop + i * RowHeight;
                bool active = (int)screen.Tool == i;

                if (active)
                    window.FillRect(dc, X(EditorScreen.PanelX + 3), Y(rowY - 2),
                        W(EditorScreen.PanelW - 6), H(RowHeight - 1), 255, 60, 66, 92);

                window.DrawText(dc, tools[i], X(x), Y(rowY), active ? accent : text, active, smallFont);
            }

            string[] toggles =
            {
                (screen.ShowBack ? "B  задний: вкл" : "B  задний: выкл"),
                (screen.ShowFront ? "F  передний: вкл" : "F  передний: выкл"),
                (screen.ShowTypes ? "T  типы: вкл" : "T  типы: выкл")
            };

            for (int i = 0; i < toggles.Length; ++i)
                window.DrawText(dc, toggles[i], X(x), Y(ToggleTop + i * RowHeight),
                    screen.ShowBack && i == 0 || screen.ShowFront && i == 1 || screen.ShowTypes && i == 2
                        ? text : muted, false, smallFont);

            // What the brush currently is: a name is worth more than an index.
            int infoY = ToggleTop + 3 * RowHeight + 10;

            string brushLine;

            switch (screen.Tool)
            {
                case EditorTool.Type:
                    brushLine = screen.CellName;
                    break;

                case EditorTool.Things:
                    brushLine = screen.CurrentThingName;
                    break;

                default:
                    brushLine = screen.Brush <= 0 ? "стереть" : screen.CurrentWallName;
                    break;
            }

            window.DrawText(dc, "кисть: " + brushLine, X(x), Y(infoY), text, false, smallFont);

            window.DrawText(dc, screen.Status, X(x), Y(infoY + 14), muted, false, smallFont);

            if (screen.FileName.Length != 0)
                window.DrawText(dc, System.IO.Path.GetFileName(screen.FileName),
                    X(x), Y(infoY + 26), muted, false, smallFont);

            // Footer hints, at the bottom of the panel.
            window.DrawText(dc, "F1 справка", X(x), Y(EditorScreen.ScreenH - 34), muted, false, smallFont);
            window.DrawText(dc, "F5 играть", X(x), Y(EditorScreen.ScreenH - 22), muted, false, smallFont);
            window.DrawText(dc, "Esc выход", X(x), Y(EditorScreen.ScreenH - 10), muted, false, smallFont);
        }

        private void DrawPaletteFrame(IntPtr dc, EditorScreen screen)
        {
            // The pictures are in the framebuffer; only the frame, the selection and
            // the scroll hint are drawn here, so the strip does not look like part of
            // the map and the highlight stays crisp at any scale.
            window.FillRect(dc, X(EditorScreen.PaletteX), Y(EditorScreen.PaletteY),
                W(EditorScreen.PaletteW), H(1), 255, 70, 76, 96);

            if (screen.Tool == EditorTool.Type)
            {
                DrawTypePalette(dc, screen);
                return;
            }

            int selected = screen.SelectedPaletteIndex;
            int visible = EditorScreen.TilesPerRow * (EditorScreen.PaletteH / EditorScreen.Tile);

            if (selected >= screen.PaletteScroll && selected < screen.PaletteScroll + visible)
            {
                int at = selected - screen.PaletteScroll;
                int x = EditorScreen.PaletteX + (at % EditorScreen.TilesPerRow) * EditorScreen.Tile;
                int y = EditorScreen.PaletteY + (at / EditorScreen.TilesPerRow) * EditorScreen.Tile;

                DrawOutline(dc, x, y, EditorScreen.Tile - 2, EditorScreen.Tile - 2);
            }

            int count = screen.PaletteCount();

            if (count > visible)
            {
                string hint = (screen.PaletteScroll / EditorScreen.TilesPerRow + 1) + "/"
                    + ((count + visible - 1) / visible);

                window.DrawText(dc, hint, X(EditorScreen.PaletteW - 26),
                    Y(EditorScreen.PaletteY + 2), NativeMethods.Rgb(200, 200, 210), false, smallFont);
            }
        }

        /// <summary>The Type tool's palette: names, not swatches — a colour tells nothing.</summary>
        private void DrawTypePalette(IntPtr dc, EditorScreen screen)
        {
            uint text = NativeMethods.Rgb(226, 228, 236);
            uint accent = NativeMethods.Rgb(255, 96, 108);

            for (int i = 0; i < EditorScreen.CellValues.Length; ++i)
            {
                int column = i / 6;
                int row = i % 6;

                int x = EditorScreen.PaletteX + 4 + column * (EditorScreen.PaletteW / 2);
                int y = EditorScreen.PaletteY + 3 + row * 9;

                bool active = EditorScreen.CellValues[i] == screen.CellBrush;

                if (active)
                    window.FillRect(dc, X(x - 2), Y(y - 1), W(EditorScreen.PaletteW / 2 - 4), H(9),
                        255, 60, 66, 92);

                window.DrawText(dc, EditorScreen.CellNames[i], X(x), Y(y),
                    active ? accent : text, active, smallFont);
            }
        }

        private void DrawOutline(IntPtr dc, int x, int y, int w, int h)
        {
            window.FillRect(dc, X(x), Y(y), W(w), H(1), 255, 255, 96, 108);
            window.FillRect(dc, X(x), Y(y + h - 1), W(w), H(1), 255, 255, 96, 108);
            window.FillRect(dc, X(x), Y(y), W(1), H(h), 255, 255, 96, 108);
            window.FillRect(dc, X(x + w - 1), Y(y), W(1), H(h), 255, 255, 96, 108);
        }

        private void DrawPrompt(IntPtr dc, EditorScreen screen)
        {
            int boxH = 22;
            int boxY = EditorScreen.PaletteY - boxH - 2;

            window.FillRect(dc, X(0), Y(boxY), W(EditorScreen.PanelX), H(boxH), 240, 16, 18, 26);
            window.FillRect(dc, X(0), Y(boxY), W(EditorScreen.PanelX), H(1), 255, 255, 96, 108);

            window.DrawText(dc, screen.PromptLabel, X(4), Y(boxY + 3),
                NativeMethods.Rgb(200, 204, 216), false, smallFont);

            window.DrawText(dc, screen.PromptValue + "_", X(4), Y(boxY + 12),
                NativeMethods.Rgb(255, 240, 140), true, smallFont);
        }

        private void DrawHelp(IntPtr dc)
        {
            // Sized from the text: the box used to be a guessed 220x150 and the last
            // lines spilled over its edge, which looks like a drawing bug.
            int width = 260;
            int lineHeight = 9;
            int height = EditorScreen.HelpLines.Length * lineHeight + 14;

            int x = (EditorScreen.ScreenW - width) / 2;
            int y = (EditorScreen.ScreenH - height) / 2;

            window.FillRect(dc, X(x), Y(y), W(width), H(height), 248, 14, 16, 24);
            window.FillRect(dc, X(x), Y(y), W(width), H(2), 255, 255, 96, 108);
            window.FillRect(dc, X(x), Y(y + height - 2), W(width), H(2), 255, 255, 96, 108);

            uint text = NativeMethods.Rgb(228, 230, 238);
            int lineY = y + 6;

            for (int i = 0; i < EditorScreen.HelpLines.Length; ++i)
            {
                window.DrawText(dc, EditorScreen.HelpLines[i], X(x + 8), Y(lineY),
                    i == 0 ? NativeMethods.Rgb(255, 96, 108) : text, i == 0, smallFont);

                lineY += lineHeight;
            }
        }
    }
}
