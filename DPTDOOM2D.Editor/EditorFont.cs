//
// DPT.DOOM 2D — the editor's text, drawn with the editor's own 3x5 font.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Text;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Editor
{
    /// <summary>
    /// Draws text into the 8-bit framebuffer with the DOS editor's font.
    ///
    /// The editor did not use the WAD's status-bar font. It linked its own GUI
    /// library, and that library carried SMALLFNT.VGA: 3x5 glyphs on a 4-pixel
    /// advance, one image per CP866 code. That is why the original's labels are so
    /// small and so tight, and why a port that draws them with the game's 8x7 face
    /// looks wrong at a glance even though every string is in the right place.
    ///
    /// Three details come straight from the era's assembly (VGAFONT.ASM), and each
    /// of them changes the picture:
    ///
    ///  * The advance is `w - sx + 1`, not `w`: the cells overlap by a pixel.
    ///  * Index 0 is transparent and every other index is ink, so a glyph is a
    ///    stencil — the font's own black-on-white palette is never used.
    ///  * `vf_step` is 1, which is where the extra pixel in the advance comes from.
    ///
    /// Text is CP866, as in the original. The font has the whole Cyrillic range,
    /// upper and lower, so strings are not folded to capitals.
    /// </summary>
    internal static class EditorFont
    {
        private static VgaImage[] glyphs;

        /// <summary>Loads the font once. Missing codes stay null and take a cell.</summary>
        public static void Load()
        {
            if (glyphs != null) return;

            glyphs = new VgaImage[256];

            VgaContainer.Container font = null;

            try
            {
                byte[] raw = Convert.FromBase64String(EditorAssets.Font);
                font = VgaContainer.Load(raw);
            }
            catch (Exception e)
            {
                Log.Warn("editor: cannot read the embedded font (" + e.Message + ")");
            }

            if (font == null) return;

            for (int c = 0; c < 256; ++c)
                glyphs[c] = font.Get(c.ToString("000"));
        }

        /// <summary>The height of a line: the glyphs are five pixels tall.</summary>
        public static int Height
        {
            get { return 5; }
        }

        /// <summary>Width of a string in pixels, for centring and right-alignment.</summary>
        public static int Width(string text)
        {
            Load();
            if (string.IsNullOrEmpty(text)) return 0;

            byte[] bytes = Encode(text);
            int total = 0;

            for (int i = 0; i < bytes.Length; ++i) total += Advance(bytes[i]);

            return total;
        }

        /// <summary>
        /// Draws a string with every glyph in one colour, like V_puts with vf_color.
        ///
        /// The colour is a palette index; the caller passes gray[i] from the skin.
        /// </summary>
        public static void Draw(string text, int x, int y, byte color)
        {
            Load();
            if (string.IsNullOrEmpty(text)) return;

            byte[] bytes = Encode(text);

            for (int i = 0; i < bytes.Length; ++i)
            {
                VgaImage glyph = glyphs[bytes[i]];

                if (glyph != null) DrawGlyph(glyph, x, y, color);

                x += Advance(bytes[i]);
            }
        }

        public static void DrawCentered(string text, int centerX, int y, byte color)
        {
            Draw(text, centerX - Width(text) / 2, y, color);
        }

        /// <summary>
        /// V_putch for one character. Used by the checkbox, which draws its tick
        /// by hand at a computed position rather than by printing a string.
        /// </summary>
        public static void DrawChar(int c, int x, int y, byte color)
        {
            Load();

            VgaImage glyph = glyphs[c & 0xFF];

            if (glyph != null) DrawGlyph(glyph, x, y, color);
        }

        /// <summary>V_chrlen: the glyph's width minus its left offset, plus one.</summary>
        public static int Advance(int c)
        {
            Load();

            VgaImage glyph = glyphs[c & 0xFF];

            // A code with no glyph still takes a cell: V_chrlen falls back to the
            // font's default width, which is one 4-pixel cell.
            return glyph == null ? 4 : glyph.W - glyph.SX + 1;
        }

        private static void DrawGlyph(VgaImage glyph, int x, int y, byte color)
        {
            // V_spr offsets the image by its own sx/sy; the font's glyphs all use
            // zero, but the same code has to work if one ever does not.
            int ox = x - glyph.SX;
            int oy = y - glyph.SY;

            byte[] pixels = glyph.Pixels;

            for (int row = 0; row < glyph.H; ++row)
            {
                int at = row * glyph.W;
                int py = oy + row;

                if (py < clipY1 || py > clipY2) continue;

                for (int col = 0; col < glyph.W; ++col)
                {
                    // Index 0 is the stencil's hole; anything else is ink.
                    if (pixels[at + col] == 0) continue;

                    int px = ox + col;
                    if (px < clipX1 || px > clipX2) continue;

                    Vga.PutPixel(px, py, color);
                }
            }
        }

        private static int clipX1, clipY1, clipX2 = 319, clipY2 = 199;

        /// <summary>Limits text to a rectangle. Call before drawing, reset after.</summary>
        public static void Clip(int x, int y, int w, int h)
        {
            clipX1 = x;
            clipY1 = y;
            clipX2 = x + w - 1;
            clipY2 = y + h - 1;
        }

        public static void ClipAll()
        {
            clipX1 = 0;
            clipY1 = 0;
            clipX2 = 319;
            clipY2 = 199;
        }

        /// <summary>
        /// Russian text to CP866 bytes.
        ///
        /// No case folding: unlike the WAD's status-bar font, this one has both
        /// alphabets, and the original's labels use lower case ("вправо", "игрок
        /// наж."), so folding would be a visible change rather than a fix.
        /// </summary>
        private static byte[] Encode(string text)
        {
            return Encoding.GetEncoding(866).GetBytes(text);
        }
    }
}
