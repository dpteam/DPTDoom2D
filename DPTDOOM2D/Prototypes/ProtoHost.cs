//
// DPT.DOOM 2D — host for the prototype showcase.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Diagnostics;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Win32;

namespace DPTDOOM2D.Prototypes
{
    /// <summary>
    /// Runs one prototype in its own window.
    ///
    /// Every prototype here was a DOS program in the mid-90s: VGA mode 13h, a
    /// 320x200 palette framebuffer, a 70 Hz timer, the keyboard. The port already
    /// has all four (Vga, Palette, Win32Window, Kbd), so a prototype only has to
    /// describe what it draws and how it moves — the loop, the window and the
    /// scaling are shared.
    ///
    /// Prototypes get their own window rather than drawing into the game's, so a
    /// half-finished demo cannot disturb the game's state, and closing the window
    /// is the whole of "exit".
    /// </summary>
    internal static class ProtoHost
    {
        /// <summary>A prototype: three callbacks and a bit of metadata.</summary>
        internal abstract class Prototype
        {
            /// <summary>Identifier used by --proto and by the launcher.</summary>
            public abstract string Id { get; }

            /// <summary>Name shown in the launcher.</summary>
            public abstract string Title { get; }

            /// <summary>Where the original lived, for the showcase's "about".</summary>
            public abstract string Source { get; }

            /// <summary>One line about what it is.</summary>
            public abstract string Description { get; }

            /// <summary>Called once, after the palette is installed.</summary>
            public virtual void Init() { }

            /// <summary>One frame. <paramref name="frame"/> counts from zero.</summary>
            public abstract void Frame(int frame);

            /// <summary>True when the prototype wants to quit on its own.</summary>
            public virtual bool Finished { get { return false; } }
        }

        /// <summary>
        /// Runs the prototype until Esc, the window closes, or it finishes.
        /// </summary>
        public static int Run(Prototype prototype, int scale)
        {
            Vga.Init(320, 200);
            Vga.SetRect(0, 320, 0, 200);

            prototype.Init();

            using (var window = new Win32Window("DPT.DOOM 2D - " + Ascii(prototype.Title), 320, 200,
                scale, Config.Fullscreen))
            using (var presenter = new Presenter(320, 200))
            {
                // The prototype gets the same stretch mode the game was started with,
                // not a hardcoded one. PixelPerfect is the right default for 320x200 —
                // an integer factor keeps the pixels square — but a player who chose
                // "fill the screen" in the game got letterboxed prototypes, which looks
                // like the program is ignoring its own settings.
                window.Stretch = Config.StretchMode;

                bool fullscreen = Config.Fullscreen;

                long tick = Stopwatch.GetTimestamp();
                long tickLength = Stopwatch.Frequency / 30;   // the old timer was ~70 Hz; 30 is plenty

                int frame = 0;
                bool running = true;

                while (running && !window.Closed)
                {
                    window.PumpMessages();
                    if (window.Closed) break;

                    Kbd.Update(window);

                    if (Kbd.WasPressed(Kbd.SDLK_ESCAPE)) break;

                    // F11 and the game's menu item do the same thing. A prototype has
                    // no menu, so without a key it could only be fullscreen if it was
                    // launched that way.
                    if (Kbd.WasPressed(Kbd.SDLK_F11))
                    {
                        fullscreen = !fullscreen;
                        window.SetFullscreen(fullscreen);
                    }

                    try
                    {
                        prototype.Frame(frame);
                    }
                    catch (Exception e)
                    {
                        Log.Error("prototype " + prototype.Id + " failed at frame " + frame, e);
                        return 1;
                    }

                    // A short hint at the start, because a prototype has no menu and
                    // no documentation on screen: without it, F11 exists and nobody
                    // knows. Three seconds is long enough to read and short enough not
                    // to sit on top of the demo.
                    if (frame < 90) Hint(frame);

                    presenter.Present(window);
                    ++frame;

                    if (prototype.Finished) break;

                    long now = Stopwatch.GetTimestamp();
                    long remaining = tick + tickLength - now;
                    if (remaining > 0)
                    {
                        int ms = (int)(remaining * 1000 / Stopwatch.Frequency);
                        if (ms > 0) System.Threading.Thread.Sleep(ms);
                    }

                    tick += tickLength;
                    if (Stopwatch.GetTimestamp() - tick > tickLength * 4) tick = Stopwatch.GetTimestamp();
                }
            }

            return 0;
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// The two keys every prototype answers to, in the corner of the screen.
        ///
        /// Drawn by the host rather than by each prototype: the keys belong to the
        /// host, and a prototype that forgot to mention them would hide the only way
        /// to leave the window without a title bar.
        /// </summary>
        private static void Hint(int frame)
        {
            const string text = "ESC EXIT   F11 FULLSCREEN";

            // 4 pixels per glyph in the 3x5 font, plus the gap.
            int width = text.Length * 4;
            int x = 320 - width - 2;
            int y = 200 - 8;

            // A box behind it, so the text reads over whatever the demo draws. The
            // colour is the darkest palette entry, which every palette has.
            Vga.Clr(x - 1, width + 2, y - 1, 7, 0);

            // A short fade in the last second, so it does not vanish mid-read.
            if (frame > 60 && (frame & 2) == 0) return;

            Text(x, y, text, 255);
        }

        /// <summary>
        /// Window titles cannot carry the Cyrillic and typographic characters the
        /// catalogue uses: Win32's ANSI title on some setups, and every screenshot
        /// tool that compares against a plain ASCII string, choke on them.
        /// </summary>
        private static string Ascii(string text)
        {
            var builder = new System.Text.StringBuilder(text.Length);

            for (int i = 0; i < text.Length; ++i)
            {
                char c = text[i];
                builder.Append(c > 127 ? '-' : c);
            }

            return builder.ToString();
        }

        /// <summary>Loads one of the bundled prototype artwork containers.</summary>
        public static VgaContainer.Container LoadAsset(string fileName)
        {
            byte[] data = ProtoAssets.Get(fileName);

            if (data == null)
            {
                Log.Warn("prototype asset missing: " + fileName);
                return null;
            }

            try
            {
                return VgaContainer.Load(data);
            }
            catch (Exception e)
            {
                Log.Error("cannot read " + fileName, e);
                return null;
            }
        }

        /// <summary>
        /// Draws text with a built-in 3x5 font.
        ///
        /// Prototypes must run without doom2d.wad, so they cannot borrow the
        /// game's status-bar glyphs. The font is written as five-row strings so a
        /// glyph can be checked by eye: a packed bit table is a classic way to end
        /// up with mirrored letters that nobody notices until a screenshot.
        /// </summary>
        public static void Text(int x, int y, string text, byte color)
        {
            for (int i = 0; i < text.Length; ++i)
            {
                char c = char.ToUpperInvariant(text[i]);
                if (c == ' ') { x += 4; continue; }

                string glyph = Glyph(c);
                if (glyph == null) { x += 4; continue; }

                for (int row = 0; row < 5; ++row)
                {
                    for (int col = 0; col < 3; ++col)
                        if (glyph[row * 3 + col] == '#') Vga.PutPixel(x + col, y + row, color);
                }

                x += 4;
            }
        }

        /// <summary>Five rows of three characters; '#' is ink.</summary>
        private static string Glyph(char c)
        {
            switch (c)
            {
                case 'A': return "###" + "#.#" + "###" + "#.#" + "#.#";
                case 'B': return "##." + "#.#" + "##." + "#.#" + "##.";
                case 'C': return "###" + "#.." + "#.." + "#.." + "###";
                case 'D': return "##." + "#.#" + "#.#" + "#.#" + "##.";
                case 'E': return "###" + "#.." + "##." + "#.." + "###";
                case 'F': return "###" + "#.." + "##." + "#.." + "#..";
                case 'G': return "###" + "#.." + "#.#" + "#.#" + "###";
                case 'H': return "#.#" + "#.#" + "###" + "#.#" + "#.#";
                case 'I': return "###" + ".#." + ".#." + ".#." + "###";
                case 'J': return "..#" + "..#" + "..#" + "#.#" + "###";
                case 'K': return "#.#" + "#.#" + "##." + "#.#" + "#.#";
                case 'L': return "#.." + "#.." + "#.." + "#.." + "###";
                case 'M': return "#.#" + "###" + "###" + "#.#" + "#.#";
                case 'N': return "##." + "#.#" + "#.#" + "#.#" + "#.#";
                case 'O': return "###" + "#.#" + "#.#" + "#.#" + "###";
                case 'P': return "###" + "#.#" + "###" + "#.." + "#..";
                case 'Q': return "###" + "#.#" + "###" + "#.#" + "###";
                case 'R': return "##." + "#.#" + "##." + "#.#" + "#.#";
                case 'S': return "###" + "#.." + "###" + "..#" + "###";
                case 'T': return "###" + ".#." + ".#." + ".#." + ".#.";
                case 'U': return "#.#" + "#.#" + "#.#" + "#.#" + "###";
                case 'V': return "#.#" + "#.#" + "#.#" + "#.#" + ".#.";
                case 'W': return "#.#" + "#.#" + "###" + "###" + "#.#";
                case 'X': return "#.#" + "#.#" + ".#." + "#.#" + "#.#";
                case 'Y': return "#.#" + "#.#" + ".#." + ".#." + ".#.";
                case 'Z': return "###" + "..#" + ".#." + "#.." + "###";
                case '0': return "###" + "#.#" + "#.#" + "#.#" + "###";
                case '1': return ".#." + "##." + ".#." + ".#." + "###";
                case '2': return "###" + "..#" + "###" + "#.." + "###";
                case '3': return "###" + "..#" + "###" + "..#" + "###";
                case '4': return "#.#" + "#.#" + "###" + "..#" + "..#";
                case '5': return "###" + "#.." + "###" + "..#" + "###";
                case '6': return "###" + "#.." + "###" + "#.#" + "###";
                case '7': return "###" + "..#" + "..#" + "..#" + "..#";
                case '8': return "###" + "#.#" + "###" + "#.#" + "###";
                case '9': return "###" + "#.#" + "###" + "..#" + "###";
                case '-': return "..." + "..." + "###" + "..." + "...";
                case ':': return "..." + ".#." + "..." + ".#." + "...";
                case '.': return "..." + "..." + "..." + "..." + ".#.";
                case '/': return "..#" + "..#" + ".#." + "#.." + "#..";
                case '%': return "#.#" + "..#" + ".#." + "#.." + "#.#";
                case '!': return ".#." + ".#." + ".#." + "..." + ".#.";
                case '?': return "###" + "..#" + ".##" + "..." + ".#.";
                case ',': return "..." + "..." + "..." + ".#." + "#..";
                case '(': return ".#." + "#.." + "#.." + "#.." + ".#.";
                case ')': return ".#." + "..#" + "..#" + "..#" + ".#.";
                case '+': return "..." + ".#." + "###" + ".#." + "...";
                case '=': return "..." + "###" + "..." + "###" + "...";
                default: return null;
            }
        }
    }
}
