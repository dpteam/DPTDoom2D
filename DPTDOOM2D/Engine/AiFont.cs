//
// DPT.DOOM 2D — self-made vector text font for the AIDooM2D data set.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System.Collections.Generic;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// A stroke font drawn from scratch, so the AIDooM2D data set never copies
    /// a single pixel of the original charset.
    ///
    /// Every glyph is described as one or more polylines inside a 5x7 design
    /// box (x in 0..4, y in 0..6, y=0 at the top). Nothing here is a bitmap:
    /// <see cref="Render"/> scales the box to the requested size and walks the
    /// segments with Bresenham, so the same table produces a 5x7 HUD caption
    /// and a 40x56 title without a second set of assets. Strokes also survive
    /// a future re-rasteriser (thicker pen, outline, shear) that a bitmap
    /// could not, which is why the data is kept as geometry rather than as
    /// pre-rendered rows.
    ///
    /// The output is 1 = ink, 0 = background, matching the palette-index
    /// conventions of <see cref="Vga"/> so the caller can map it straight
    /// into the frame buffer.
    /// </summary>
    internal static class AiFont
    {
        private const int DesignWidthBox = 5;
        private const int DesignHeightBox = 7;

        /// <summary>One glyph: polylines flattened to x,y pairs, plus its ink extent.</summary>
        private sealed class Shape
        {
            public int[][] Lines;
            public int MaxX;
        }

        // Flat "code", "shape" pairs. Kept as strings because that is the form
        // a human can proof-read and fix; it is parsed once at startup.
        //
        // Shape grammar: polylines separated by '|', points inside a polyline
        // separated by spaces, each point written as "x,y". A polyline with a
        // single point is a dot (used by '.', ':' and the like). A shape that
        // starts with '=' is an alias for the ASCII code that follows, which
        // is how Cyrillic letters sharing a Latin skeleton avoid a second copy.
        private static readonly string[] Table =
        {
            // Punctuation, 33..47
            "33", "2,0 2,4|2,6",                                    // !
            "34", "1,0 1,2|3,0 3,2",                                // "
            "35", "1,0 1,6|3,0 3,6|0,2 4,2|0,4 4,4",                // #
            "36", "4,1 3,0 1,0 0,1 1,2 3,2 4,3 4,5 3,6 1,6 0,5|2,0 2,6", // $
            "37", "0,6 4,0|0,0 1,0 1,1 0,1 0,0|3,5 4,5 4,6 3,6 3,5", // %
            "38", "4,6 1,3 0,2 0,1 1,0 2,0 2,1 0,4 0,5 1,6 3,6 4,5", // &
            "39", "2,0 2,2",                                        // '
            "40", "3,0 1,2 1,4 3,6",                                // (
            "41", "1,0 3,2 3,4 1,6",                                // )
            "42", "2,0 2,4|0,1 4,3|0,3 4,1",                        // *
            "43", "2,1 2,5|0,3 4,3",                                // +
            "44", "2,5 2,6 1,6",                                    // ,
            "45", "0,3 4,3",                                        // -
            "46", "2,6",                                            // .
            "47", "0,6 4,0",                                        // /

            // Digits, 48..57
            "48", "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0",            // 0
            "49", "1,1 2,0 2,6|0,6 4,6",                            // 1
            "50", "0,1 1,0 3,0 4,1 4,2 0,6 4,6",                    // 2
            "51", "0,0 4,0 2,3 3,3 4,4 4,5 3,6 1,6 0,5",            // 3
            "52", "3,6 3,0 0,4 4,4",                                // 4
            "53", "4,0 0,0 0,3 3,3 4,4 4,5 3,6 1,6 0,5",            // 5
            "54", "3,0 1,1 0,3 0,5 1,6 3,6 4,5 4,4 3,3 1,3 0,4",    // 6
            "55", "0,0 4,0 1,6",                                    // 7
            "56", "1,0 3,0 4,1 4,2 3,3 1,3 0,2 0,1 1,0|1,3 3,3 4,4 4,5 3,6 1,6 0,5 0,4 1,3", // 8
            "57", "1,6 3,5 4,3 4,1 3,0 1,0 0,1 0,2 1,3 3,3 4,2",    // 9

            // Punctuation, 58..64
            "58", "2,2|2,5",                                        // :
            "59", "2,2|2,5 1,6",                                    // ;
            "60", "4,1 0,3 4,5",                                    // <
            "61", "0,2 4,2|0,4 4,4",                                // =
            "62", "0,1 4,3 0,5",                                    // >
            "63", "0,1 1,0 3,0 4,1 4,2 2,3 2,4|2,6",                // ?
            "64", "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0|2,2 3,2 3,4 2,4 2,2", // @

            // Uppercase Latin, 65..90
            "65", "0,6 0,2 2,0 4,2 4,6|0,3 4,3",                    // A
            "66", "0,0 0,6|0,0 3,0 4,1 4,2 3,3 0,3|0,3 3,3 4,4 4,5 3,6 0,6", // B
            "67", "4,1 3,0 1,0 0,1 0,5 1,6 3,6 4,5",                // C
            "68", "0,0 0,6 3,6 4,5 4,1 3,0 0,0",                    // D
            "69", "4,0 0,0 0,6 4,6|0,3 3,3",                        // E
            "70", "4,0 0,0 0,6|0,3 3,3",                            // F
            "71", "4,1 3,0 1,0 0,1 0,5 1,6 3,6 4,5 4,3 2,3",        // G
            "72", "0,0 0,6|4,0 4,6|0,3 4,3",                        // H
            "73", "1,0 3,0|2,0 2,6|1,6 3,6",                        // I
            "74", "3,0 3,5 2,6 1,6 0,5",                            // J
            "75", "0,0 0,6|4,0 0,3 4,6",                            // K
            "76", "0,0 0,6 4,6",                                    // L
            "77", "0,6 0,0 2,3 4,0 4,6",                            // M
            "78", "0,6 0,0 4,6 4,0",                                // N
            "79", "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0",            // O
            "80", "0,6 0,0 3,0 4,1 4,2 3,3 0,3",                    // P
            "81", "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0|2,4 4,6",    // Q
            "82", "0,6 0,0 3,0 4,1 4,2 3,3 0,3|2,3 4,6",            // R
            "83", "4,1 3,0 1,0 0,1 0,2 1,3 3,3 4,4 4,5 3,6 1,6 0,5", // S
            "84", "0,0 4,0|2,0 2,6",                                // T
            "85", "0,0 0,5 1,6 3,6 4,5 4,0",                        // U
            "86", "0,0 0,2 2,6 4,2 4,0",                            // V
            "87", "0,0 0,4 1,6 2,4 3,6 4,4 4,0",                    // W
            "88", "0,0 4,6|4,0 0,6",                                // X
            "89", "0,0 2,3 4,0|2,3 2,6",                            // Y
            "90", "0,0 4,0 0,6 4,6",                                // Z

            // Brackets and symbols, 91..96
            "91", "3,0 1,0 1,6 3,6",                                // [
            "92", "0,0 4,6",                                        // backslash
            "93", "1,0 3,0 3,6 1,6",                                // ]
            "94", "0,2 2,0 4,2",                                    // ^
            "95", "0,6 4,6",                                        // _
            "96", "1,0 2,1",                                        // `

            // Braces, 123..126
            "123", "3,0 2,1 2,2 1,3 2,4 2,5 3,6",                   // {
            "124", "2,0 2,6",                                       // |
            "125", "1,0 2,1 2,2 3,3 2,4 2,5 1,6",                   // }
            "126", "0,2 1,1 2,2 3,3 4,2",                           // ~

            // CP866 Cyrillic uppercase, 128..159, in code-page order.
            // Letters that share a Latin skeleton alias the Latin entry so the
            // two can never drift apart.
            "128", "=65",                                            // A
            "129", "4,0 0,0 0,6 3,6 4,5 4,4 3,3 0,3",                // Be
            "130", "=66",                                            // Ve
            "131", "4,0 0,0 0,6",                                    // Ge
            "132", "3,0 3,3 4,3 4,6|1,0 1,3 0,3 0,6|1,0 3,0|0,6 4,6", // De
            "133", "=69",                                            // Ie
            "134", "0,0 0,2 2,3 0,4 0,6|4,0 4,2 2,3 4,4 4,6|2,0 2,6", // Zhe
            "135", "0,0 4,0 2,3 3,3 4,4 4,5 3,6 1,6 0,5",            // Ze
            "136", "0,0 0,6 4,0 4,6",                                // I
            "137", "1,0 2,1 3,0|0,1 0,6 4,1 4,6",                    // Short I
            "138", "=75",                                            // Ka
            "139", "4,6 4,0 1,0 0,3 0,6",                            // El
            "140", "=77",                                            // Em
            "141", "=72",                                            // En
            "142", "=79",                                            // O
            "143", "0,6 0,0 4,0 4,6",                                // Pe
            "144", "=80",                                            // Er
            "145", "=67",                                            // Es
            "146", "=84",                                            // Te
            "147", "4,0 0,6|0,0 2,3",                                // U
            "148", "2,0 2,6|1,2 3,2 3,4 1,4 1,2",                    // Ef
            "149", "=88",                                            // Ha
            "150", "0,0 0,5 4,5 4,0|4,5 4,6",                        // Tse
            "151", "0,0 0,2 1,3 4,3|4,0 4,6",                        // Che
            "152", "0,0 0,5 4,5 4,0|2,0 2,5",                        // Sha
            "153", "0,0 0,5 4,5 4,0|2,0 2,5|4,5 4,6",                // Shcha
            "154", "0,0 1,0 1,2 3,2 4,3 4,4 3,5 1,5 1,0",            // Hard sign
            "155", "0,0 0,5 2,5 3,4 3,3 2,2 0,2|4,0 4,5",            // Yeru
            "156", "0,0 0,5 3,5 4,4 4,3 3,2 0,2",                    // Soft sign
            "157", "0,1 1,0 3,0 4,1 4,5 3,6 1,6 0,5|4,3 1,3",        // E
            "158", "0,0 0,6|1,3 2,3|2,1 3,1 4,2 4,4 3,5 2,5 2,1",    // Yu
            "159", "4,6 4,0 1,0 0,1 0,2 1,3 4,3|2,3 0,6",            // Ya
        };

        // Declared after Table on purpose: static field initialisers run in
        // textual order, so Build() is guaranteed to see a filled table.
        private static readonly Dictionary<int, Shape> Shapes = Build();

        /// <summary>True when this character has a shape.</summary>
        public static bool Has(int c)
        {
            return Find(c) != null;
        }

        /// <summary>
        /// Rasterises one character into a w*h grid: 1 = ink, 0 = background.
        /// Returns null when the character has no shape.
        /// </summary>
        public static byte[] Render(int c, int w, int h)
        {
            // A zero or negative target would make the scale arithmetic
            // meaningless, so it is rejected rather than silently clamped.
            if (w <= 0 || h <= 0) return null;

            Shape shape = Find(c);
            if (shape == null) return null;

            byte[] bitmap = new byte[w * h];

            // The pen has to grow with the target. A glyph designed on a 5x7 grid and
            // drawn at 14x16 with a one-pixel pen is a wireframe, not a letter — the
            // HUD's health numbers came out as outlines a player could not read. The
            // pen is the smaller of the two scale factors, so a wide-but-short box does
            // not produce a pen thicker than its rows can hold.
            int pen = Pen(w, h);

            // The box the glyph is stretched into is inset by the pen, so the last
            // column and row of a stroke still land inside the picture instead of being
            // clipped by half.
            int spanX = w - pen;
            int spanY = h - pen;

            for (int l = 0; l < shape.Lines.Length; l++)
            {
                int[] points = shape.Lines[l];
                if (points.Length == 0) continue;

                if (points.Length == 2)
                {
                    // Single point: a dot, which is what '.' and ':' need.
                    Plot(bitmap, w, h, pen,
                        Scale(points[0], spanX, DesignWidthBox - 1),
                        Scale(points[1], spanY, DesignHeightBox - 1));
                    continue;
                }

                int x0 = Scale(points[0], spanX, DesignWidthBox - 1);
                int y0 = Scale(points[1], spanY, DesignHeightBox - 1);
                for (int i = 2; i < points.Length; i += 2)
                {
                    int x1 = Scale(points[i], spanX, DesignWidthBox - 1);
                    int y1 = Scale(points[i + 1], spanY, DesignHeightBox - 1);
                    DrawLine(bitmap, w, h, pen, x0, y0, x1, y1);
                    x0 = x1;
                    y0 = y1;
                }
            }

            return bitmap;
        }

        /// <summary>
        /// How thick a stroke should be at this size.
        ///
        /// One pixel up to about 8x10, then one pixel per extra design cell. The number
        /// is derived from the design box rather than guessed: the glyphs are five cells
        /// wide and seven tall, so the horizontal and vertical scale factors are w/5 and
        /// h/7, and the pen is the smaller of the two.
        ///
        /// Rounded, not truncated. Truncating turned a 15x12 title into a one-pixel
        /// wireframe — 12/7 is 1.71, which truncates to 1 and throws away the whole
        /// difference between a small and a large glyph.
        /// </summary>
        private static int Pen(int w, int h)
        {
            int px = (w + DesignWidthBox / 2) / DesignWidthBox;
            int py = (h + DesignHeightBox / 2) / DesignHeightBox;

            int pen = px < py ? px : py;

            return pen < 1 ? 1 : pen;
        }

        /// <summary>Design width of the character in the 5x7 design box (2..5), or 0.</summary>
        public static int DesignWidth(int c)
        {
            Shape shape = Find(c);
            if (shape == null) return 0;

            // One blank column of right margin keeps consecutive glyphs from
            // touching when a caller lays a string out edge to edge.
            int width = shape.MaxX + 2;
            if (width < 2) width = 2;
            if (width > DesignWidthBox) width = DesignWidthBox;
            return width;
        }

        private static Shape Find(int c)
        {
            // The font is all-caps by design: the 1996 charset had no lowercase
            // bitmaps, so folding here keeps both cases on one shape instead of
            // duplicating 26 entries that could drift apart.
            if (c >= 'a' && c <= 'z') c -= 32;

            Shape shape;
            return Shapes.TryGetValue(c, out shape) ? shape : null;
        }

        /// <summary>Maps a design-box coordinate onto the target grid.</summary>
        private static int Scale(int value, int span, int designSpan)
        {
            // Round to nearest rather than truncate: a 5x7 target must reproduce
            // the design box exactly, and larger targets stay centred instead of
            // creeping toward the top-left corner.
            return (value * span + designSpan / 2) / designSpan;
        }

        private static void Plot(byte[] bitmap, int w, int h, int pen, int x, int y)
        {
            // A stroke is a square of `pen` pixels with its top-left at the scaled
            // point. Drawing a square rather than a single pixel is what makes a large
            // glyph look like a letter instead of a wireframe.
            for (int py = y; py < y + pen; ++py)
            {
                if (py < 0 || py >= h) continue;

                for (int px = x; px < x + pen; ++px)
                {
                    if (px < 0 || px >= w) continue;

                    bitmap[py * w + px] = 1;
                }
            }
        }

        private static void DrawLine(byte[] bitmap, int w, int h, int pen, int x0, int y0, int x1, int y1)
        {
            int dx = x1 - x0;
            if (dx < 0) dx = -dx;
            int dy = y1 - y0;
            if (dy < 0) dy = -dy;
            int stepX = x0 < x1 ? 1 : -1;
            int stepY = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            for (;;)
            {
                Plot(bitmap, w, h, pen, x0, y0);
                if (x0 == x1 && y0 == y1) break;

                int doubled = err * 2;
                if (doubled > -dy)
                {
                    err -= dy;
                    x0 += stepX;
                }
                if (doubled < dx)
                {
                    err += dx;
                    y0 += stepY;
                }
            }
        }

        private static Dictionary<int, Shape> Build()
        {
            Dictionary<int, Shape> map = new Dictionary<int, Shape>(Table.Length / 2);

            // Two passes: aliases may point at any Latin letter, so resolving
            // them only after the plain shapes exist keeps the table order free.
            int aliasCount = 0;
            int[] aliasFrom = new int[Table.Length / 2];
            int[] aliasTo = new int[Table.Length / 2];

            for (int i = 0; i + 1 < Table.Length; i += 2)
            {
                int code;
                if (!int.TryParse(Table[i], out code)) continue;

                string text = Table[i + 1];
                if (text.Length > 0 && text[0] == '=')
                {
                    int target;
                    if (!int.TryParse(text.Substring(1), out target)) continue;
                    aliasFrom[aliasCount] = code;
                    aliasTo[aliasCount] = target;
                    aliasCount++;
                    continue;
                }

                map[code] = ParseShape(text);
            }

            for (int i = 0; i < aliasCount; i++)
            {
                Shape shared;
                if (map.TryGetValue(aliasTo[i], out shared)) map[aliasFrom[i]] = shared;
            }

            return map;
        }

        private static Shape ParseShape(string text)
        {
            int lineCount = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '|') lineCount++;
            }

            int[][] lines = new int[lineCount][];
            int line = 0;
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i == text.Length || text[i] == '|')
                {
                    lines[line++] = ParsePoints(text, start, i);
                    start = i + 1;
                }
            }

            Shape shape = new Shape();
            shape.Lines = lines;
            shape.MaxX = 0;
            for (int l = 0; l < lines.Length; l++)
            {
                int[] points = lines[l];
                for (int p = 0; p < points.Length; p += 2)
                {
                    if (points[p] > shape.MaxX) shape.MaxX = points[p];
                }
            }
            return shape;
        }

        private static int[] ParsePoints(string text, int from, int to)
        {
            int count = 0;
            bool inToken = false;
            for (int i = from; i < to; i++)
            {
                if (text[i] != ' ')
                {
                    if (!inToken) count++;
                    inToken = true;
                }
                else
                {
                    inToken = false;
                }
            }

            int[] points = new int[count * 2];
            int at = 0;
            int pos = from;
            while (pos < to)
            {
                while (pos < to && text[pos] == ' ') pos++;
                if (pos >= to) break;

                int x = 0;
                while (pos < to && text[pos] >= '0' && text[pos] <= '9')
                {
                    x = x * 10 + (text[pos] - '0');
                    pos++;
                }
                if (pos < to && text[pos] == ',') pos++;

                int y = 0;
                while (pos < to && text[pos] >= '0' && text[pos] <= '9')
                {
                    y = y * 10 + (text[pos] - '0');
                    pos++;
                }

                if (at + 1 < points.Length)
                {
                    points[at++] = x;
                    points[at++] = y;
                }
            }

            return points;
        }
    }
}
