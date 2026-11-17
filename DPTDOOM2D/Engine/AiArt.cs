//
// DPT.DOOM 2D — AIDooM2D: the palette, the lookup tables and the walls.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Generates the parts of a data set that are colour rather than shape.
    ///
    /// AIDooM2D's promise is that not one pixel in it was drawn by somebody else, so
    /// nothing here reads the reference WAD. The palette is built from named ramps,
    /// the tables are computed from it, and the walls are drawn by code.
    ///
    /// Two things in the palette are **not** free choices, because the game hard-codes
    /// palette indices:
    ///
    ///  * `0x50` and `0x60` are the light and dark greys the menu and the HUD draw text
    ///    with, so those two blocks stay grey whatever the seed does.
    ///  * `0x70` is player one's colour, `0xA0` the "on" colour of a HUD bar, `0xB0` the
    ///    red of a health bar, `0xC0`/`0xC8` blues and `0xFB` magenta — all written into
    ///    the game as literals.
    ///
    /// The layout of the 256 entries therefore follows Doom2D's: sixteen blocks of
    /// sixteen, each a ramp from a bright colour to a deep one. What the seed changes is
    /// the accent hues and the overall brightness, not the structure.
    /// </summary>
    internal static class AiArt
    {
        // ---- deterministic random -------------------------------------------

        /// <summary>
        /// A tiny LCG, deliberately not <see cref="Random"/>.
        ///
        /// The data set has to be byte-identical on every machine for a given seed,
        /// because two players with "the same" AIDooM2D must agree or the data
        /// manifest refuses the session. System.Random guarantees no such thing.
        /// </summary>
        internal struct Rand
        {
            private uint state;

            public Rand(uint seed)
            {
                state = seed == 0 ? 0x9E3779B9u : seed;
            }

            public uint Next()
            {
                state = state * 1664525u + 1013904223u;
                return state;
            }

            /// <summary>0 .. n-1.</summary>
            public int Below(int n)
            {
                return n <= 0 ? 0 : (int)(Next() % (uint)n);
            }

            public int Range(int low, int high)
            {
                return low >= high ? low : low + Below(high - low + 1);
            }
        }

        /// <summary>FNV-1a over the name, mixed with the seed: stable across runtimes.</summary>
        public static uint Hash(string text, uint seed)
        {
            uint hash = 2166136261u ^ seed;

            for (int i = 0; text != null && i < text.Length; ++i)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }

            return hash == 0 ? 1u : hash;
        }

        // ---- palette ---------------------------------------------------------

        /// <summary>
        /// One ramp: sixteen entries from <paramref name="bright"/> down to
        /// <paramref name="deep"/>.
        ///
        /// A ramp rather than a single colour because the artwork shades with a fixed
        /// offset — a monster's body is `base+2` and its outline `base-4` — so each
        /// colour needs room below it in the same hue.
        /// </summary>
        private static void Ramp(byte[] pal, int block, int br, int bg, int bb, int dr, int dg, int db)
        {
            for (int i = 0; i < 16; ++i)
            {
                int at = (block * 16 + i) * 3;

                // Two thirds of the way down is where a ramp stops reading as one
                // colour, so the fall-off is slightly convex.
                int t = i * i / 15;
                if (t > 15) t = 15;

                pal[at] = (byte)(br + (dr - br) * t / 15);
                pal[at + 1] = (byte)(bg + (dg - bg) * t / 15);
                pal[at + 2] = (byte)(bb + (db - bb) * t / 15);
            }
        }

        /// <summary>The 768-byte PLAYPAL: 6-bit RGB, the same units Doom2D stores.</summary>
        public static byte[] Palette(uint seed)
        {
            var pal = new byte[768];

            Rand rnd = new Rand(seed);

            // A small global shift keeps two seeds from looking identical without
            // moving any block far enough to stop being what the game expects.
            int shift = rnd.Range(-3, 3);

            // Block 0: black and the darkest browns. Index 0 is the transparent
            // colour of every sprite, so it must stay black.
            pal[0] = 0; pal[1] = 0; pal[2] = 0;
            for (int i = 1; i < 16; ++i)
            {
                int v = i * 2;
                pal[i * 3] = (byte)(v);
                pal[i * 3 + 1] = (byte)Math.Max(0, v - 1);
                pal[i * 3 + 2] = (byte)Math.Max(0, v / 2);
            }

            Ramp(pal, 1, 40, 18, 14, 14, 4, 4);      // red brown
            Ramp(pal, 2, 30, 8, 8, 6, 2, 2);         // deep red
            Ramp(pal, 3, 63, 52, 44, 30, 20, 16);    // skin
            Ramp(pal, 4, 50, 32, 18, 18, 10, 6);     // brown

            // 0x50 is the light grey of every menu label: fixed, not seeded.
            Ramp(pal, 5, 59, 59, 59, 34, 34, 34);

            // 0x60 is the dark grey of the status bar background: fixed too.
            Ramp(pal, 6, 33, 33, 33, 6, 6, 6);

            // 0x70 is player one, and the game draws him with it.
            Ramp(pal, 7, 30 + shift, 63, 28, 4, 18, 4);

            Ramp(pal, 8, 48, 42, 36, 20, 17, 14);    // tan
            Ramp(pal, 9, 40, 33, 25, 12, 9, 7);      // dark tan
            Ramp(pal, 10, 63, 63, 30, 34, 28, 8);    // 0xA0 yellow
            Ramp(pal, 11, 63, 4, 4, 24, 0, 0);       // 0xB0 red

            // 0xC0 light blue, 0xC8 deep blue: the water and the HUD bar.
            Ramp(pal, 12, 58, 58, 63, 0, 0, 50);

            Ramp(pal, 13, 63, 40, 10, 30, 14, 2);    // orange
            Ramp(pal, 14, 63, 50, 12, 28, 20, 4);    // gold
            Ramp(pal, 15, 63, 0, 60, 24, 0, 24);     // 0xFB magenta

            return pal;
        }

        /// <summary>
        /// A fast "closest palette index" lookup, quantised to five bits per channel.
        ///
        /// The generator asks this millions of times — every pixel of every wall and
        /// sprite goes through it — so a 32x32x32 cube is built once instead of
        /// scanning 256 entries per pixel. Five bits is the same quantisation the VGA
        /// hardware used, so nothing visible is lost.
        /// </summary>
        internal sealed class Nearest
        {
            private readonly byte[] cube = new byte[32 * 32 * 32];

            public Nearest(byte[] pal)
            {
                for (int r = 0; r < 32; ++r)
                {
                    for (int g = 0; g < 32; ++g)
                    {
                        for (int b = 0; b < 32; ++b)
                        {
                            cube[(r << 10) | (g << 5) | b] = Find(pal, r * 8, g * 8, b * 8);
                        }
                    }
                }
            }

            private static byte Find(byte[] pal, int r, int g, int b)
            {
                int best = 0;
                int bestDistance = int.MaxValue;

                for (int i = 0; i < 256; ++i)
                {
                    int dr = pal[i * 3] * 4 - r;
                    int dg = pal[i * 3 + 1] * 4 - g;
                    int db = pal[i * 3 + 2] * 4 - b;

                    // Green weighs most, as it does in every palette matcher: the eye
                    // is far more sensitive to it than to red or blue.
                    int distance = dr * dr * 3 + dg * dg * 5 + db * db * 2;
                    if (distance >= bestDistance) continue;

                    bestDistance = distance;
                    best = i;
                }

                return (byte)best;
            }

            public byte Get(int r, int g, int b)
            {
                if (r < 0) r = 0; if (r > 255) r = 255;
                if (g < 0) g = 0; if (g > 255) g = 255;
                if (b < 0) b = 0; if (b > 255) b = 255;

                return cube[((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3)];
            }

            /// <summary>A palette entry scaled by a brightness factor, in percent.</summary>
            public byte Shade(byte[] pal, int index, int percent)
            {
                return Get(
                    pal[index * 3] * 4 * percent / 100,
                    pal[index * 3 + 1] * 4 * percent / 100,
                    pal[index * 3 + 2] * 4 * percent / 100);
            }
        }

        // ---- lookup tables ---------------------------------------------------

        /// <summary>
        /// COLORMAP: twelve 256-byte rows.
        ///
        /// The game uses them as a remap over pixels that are already drawn — water
        /// shading, the lightning flash, the editor's selection rectangle — so each row
        /// is "the same picture, darker". Row 0 is the identity because a caller that
        /// passes row 0 must get its pixels back unchanged.
        ///
        /// Rows 8..11 are the water levels the map names as `_WATER_0`..`_WATER_3`;
        /// they are blue-shifted rather than merely darkened, because a river that is
        /// only a dark copy of the floor does not read as water.
        /// </summary>
        public static byte[] ColorMap(byte[] pal)
        {
            var map = new byte[256 * 12];
            var nearest = new Nearest(pal);

            for (int i = 0; i < 256; ++i) map[i] = (byte)i;

            // Rows 1..5: plain darkening, 85% down to 25%.
            int[] levels = { 85, 68, 52, 38, 25 };

            for (int row = 1; row <= 5; ++row)
                Remap(map, row, pal, nearest, levels[row - 1], 100, 100, 100);

            // Row 6: the editor's selection. Darker and slightly cold, so a rectangle
            // dragged over the map is visible without hiding what is under it.
            Remap(map, 6, pal, nearest, 55, 85, 90, 115);

            // Row 7 is unused by the original; fill it like row 6 so nothing is left
            // at zero, which would paint everything black if a map ever asked for it.
            Remap(map, 7, pal, nearest, 60, 85, 90, 115);

            // Rows 8..11: the four water depths.
            for (int i = 0; i < 4; ++i)
                Remap(map, 8 + i, pal, nearest, 80 - i * 14, 70, 95, 150 + i * 20);

            return map;
        }

        private static void Remap(byte[] map, int row, byte[] pal, Nearest nearest,
            int percent, int rScale, int gScale, int bScale)
        {
            for (int i = 0; i < 256; ++i)
            {
                map[row * 256 + i] = nearest.Get(
                    pal[i * 3] * 4 * percent / 100 * rScale / 100,
                    pal[i * 3 + 1] * 4 * percent / 100 * gScale / 100,
                    pal[i * 3 + 2] * 4 * percent / 100 * bScale / 100);
            }
        }

        /// <summary>
        /// MIXMAP: a 256x256 dissolve table.
        ///
        /// The renderer reads it as `mix[colour][other]` and expects the average of the
        /// two, which is how smoke fades a pixel towards what is behind it. Index 0 is
        /// transparent in a sprite, so mixing with it must not turn a pixel black: the
        /// table averages the two colours and lets the caller decide.
        /// </summary>
        public static byte[] MixMap(byte[] pal)
        {
            var map = new byte[256 * 256];
            var nearest = new Nearest(pal);

            for (int a = 0; a < 256; ++a)
            {
                int at = a * 256;

                for (int b = 0; b < 256; ++b)
                {
                    map[at + b] = nearest.Get(
                        (pal[a * 3] + pal[b * 3]) * 2,
                        (pal[a * 3 + 1] + pal[b * 3 + 1]) * 2,
                        (pal[a * 3 + 2] + pal[b * 3 + 2]) * 2);
                }
            }

            return map;
        }

        // ---- output ----------------------------------------------------------

        /// <summary>Packs pixels into the vgaimg the game reads.</summary>
        public static byte[] Pack(int w, int h, int sx, int sy, byte[] pixels)
        {
            byte[] data = new byte[8 + w * h];

            BitConverter.GetBytes((ushort)w).CopyTo(data, 0);
            BitConverter.GetBytes((ushort)h).CopyTo(data, 2);
            BitConverter.GetBytes((short)sx).CopyTo(data, 4);
            BitConverter.GetBytes((short)sy).CopyTo(data, 6);

            Buffer.BlockCopy(pixels, 0, data, 8, Math.Min(pixels.Length, w * h));

            return data;
        }

        // ---- walls -----------------------------------------------------------

        /// <summary>What a wall looks like. Chosen from the name, see <see cref="KindOf"/>.</summary>
        public enum WallKind
        {
            Brick,
            Stone,
            Metal,
            Panel,
            Door,
            Exit,
            Water,
            Trap
        }

        /// <summary>
        /// Maps a wall name to a look.
        ///
        /// The names are Doom2D's convention — `SQR01_1` is a floor tile, `SW01_0` a
        /// switch, `GDOORKR1` a door — and the engine itself only distinguishes them by
        /// name, so the generator does the same. A name it does not recognise gets a
        /// brick, which is the least surprising thing a wall can be.
        /// </summary>
        public static WallKind KindOf(string name)
        {
            string lower = (name ?? "").ToLowerInvariant();

            if (lower.IndexOf("door", StringComparison.Ordinal) >= 0) return WallKind.Door;
            if (lower.IndexOf("exit", StringComparison.Ordinal) >= 0) return WallKind.Exit;
            if (lower.StartsWith("sw", StringComparison.Ordinal)) return WallKind.Panel;
            if (lower.IndexOf("trap", StringComparison.Ordinal) >= 0) return WallKind.Trap;
            if (lower.IndexOf("water", StringComparison.Ordinal) >= 0) return WallKind.Water;
            if (lower.IndexOf("comp", StringComparison.Ordinal) >= 0) return WallKind.Metal;
            if (lower.IndexOf("wall", StringComparison.Ordinal) >= 0) return WallKind.Stone;
            if (lower.IndexOf("crate", StringComparison.Ordinal) >= 0) return WallKind.Panel;
            if (lower.IndexOf("kvas", StringComparison.Ordinal) >= 0) return WallKind.Panel;

            return WallKind.Brick;
        }

        /// <summary>
        /// One wall tile.
        ///
        /// Every pattern is built with modular arithmetic on the pixel coordinates, so
        /// it wraps: a brick at x=15 is the same brick that continues at x=0. That is
        /// not a nicety — a Doom2D wall is stamped once per cell and the cells tile, so
        /// a seam shows up as a grid of lines across the whole level.
        /// </summary>
        public static byte[] Wall(string name, int w, int h, byte[] pal, uint seed)
        {
            WallKind kind = KindOf(name);
            var nearest = new Nearest(pal);

            // Each kind draws from a different part of the palette, so a level built
            // from several kinds reads as several materials.
            int baseIndex;

            switch (kind)
            {
                case WallKind.Door: baseIndex = 64 + 4; break;      // brown
                case WallKind.Exit: baseIndex = 176 + 2; break;     // red
                case WallKind.Water: baseIndex = 192 + 4; break;    // blue
                case WallKind.Metal: baseIndex = 80 + 4; break;     // grey
                case WallKind.Panel: baseIndex = 208 + 4; break;    // orange
                case WallKind.Trap: baseIndex = 32 + 6; break;      // deep red
                case WallKind.Stone: baseIndex = 128 + 4; break;    // tan
                default: baseIndex = 16 + 4; break;                 // red brown
            }

            Rand rnd = new Rand(Hash(name, seed));
            var pixels = new byte[w * h];

            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    int shade;

                    switch (kind)
                    {
                        case WallKind.Brick:
                            {
                                // Running bond: every other row is offset by half a brick.
                                int row = y / 4;
                                int offset = (row & 1) * 4;
                                int bx = (x + offset) % 8;
                                int by = y % 4;

                                if (by == 3 || bx == 7) shade = 1;        // mortar
                                else shade = 4 + (by == 0 ? 1 : 0) + rnd.Below(2);
                                break;
                            }

                        case WallKind.Stone:
                            {
                                // Large irregular blocks: a coarse noise field with
                                // darker joints every eight pixels.
                                int joint = (x % 8 == 0 || (y % 8 == 0 && ((x / 8) & 1) == 0)) ? 1 : 0;
                                shade = joint != 0 ? 2 : 4 + rnd.Below(3);
                                break;
                            }

                        case WallKind.Metal:
                            {
                                // A panel with rivets in the corners.
                                int edge = (x == 0 || y == 0 || x == w - 1 || y == h - 1) ? 1 : 0;
                                int rivet = ((x == 2 || x == w - 3) && (y == 2 || y == h - 3)) ? 1 : 0;
                                shade = edge != 0 ? 1 : (rivet != 0 ? 8 : 4);
                                break;
                            }

                        case WallKind.Panel:
                            {
                                // A switch plate: a frame with a raised centre.
                                int frame = (x < 2 || y < 2 || x >= w - 2 || y >= h - 2) ? 1 : 0;
                                int centre = (x > 3 && x < w - 4 && y > 3 && y < h - 4) ? 1 : 0;
                                shade = frame != 0 ? 1 : (centre != 0 ? 7 : 4);
                                break;
                            }

                        case WallKind.Door:
                            {
                                // Vertical leaves with a seam down the middle.
                                int seam = Math.Abs(x - w / 2) <= 0 ? 1 : 0;
                                int stripe = (x % 4 == 0) ? 1 : 0;
                                shade = seam != 0 ? 1 : (stripe != 0 ? 2 : 5);
                                break;
                            }

                        case WallKind.Exit:
                            {
                                // A bright arrow band across a dark field.
                                int band = (y > h / 3 && y < h * 2 / 3) ? 1 : 0;
                                shade = band != 0 ? 10 : 3;
                                break;
                            }

                        case WallKind.Water:
                            {
                                int wave = (int)(4 + 3 * Math.Sin((x * 0.9 + y * 0.4)));
                                shade = wave + rnd.Below(2);
                                break;
                            }

                        default: // Trap
                            {
                                int bar = (x % 6 == 0 || y % 6 == 0) ? 1 : 0;
                                shade = bar != 0 ? 1 : 5 + rnd.Below(2);
                                break;
                            }
                    }

                    if (shade < 0) shade = 0;
                    if (shade > 15) shade = 15;

                    pixels[y * w + x] = (byte)(baseIndex + shade);
                }
            }

            return Pack(w, h, 0, 0, pixels);
        }

        // ---- sky and menu art -------------------------------------------------

        /// <summary>
        /// A sky: 256x256, drawn as layered noise so it reads as cloud or rock.
        ///
        /// The editor and the game both draw it at a parallax offset, so it has to look
        /// plausible at any position — hence no landmarks, just a gradient with texture.
        /// </summary>
        public static byte[] Sky(byte[] pal, uint seed, int variant)
        {
            const int size = 256;

            var nearest = new Nearest(pal);
            Rand rnd = new Rand(Hash("sky" + variant, seed));
            var pixels = new byte[size * size];

            // Two coarse noise fields at different scales, added: the big one gives
            // the shape, the small one the grain.
            var coarse = Noise(rnd, 16, 16);
            var fine = Noise(rnd, 64, 64);

            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    int big = Sample(coarse, 16, 16, x, y, size);
                    int small = Sample(fine, 64, 64, x, y, size);

                    // Top of the sky is darker: it is above the horizon.
                    int vertical = 40 - y * 40 / size;

                    int value = 20 + big * 3 / 4 + small / 4 + vertical;

                    int r, g, b;

                    if (variant == 2)
                    {
                        // Thunderstorm: blue grey.
                        r = value / 2; g = value * 2 / 3; b = value;
                    }
                    else
                    {
                        // Overcast brown, the colour the original's horizon is.
                        r = value; g = value * 3 / 4; b = value / 2;
                    }

                    pixels[y * size + x] = nearest.Get(r, g, b);
                }
            }

            return Pack(size, size, 0, 0, pixels);
        }

        private static int[] Noise(Rand rnd, int w, int h)
        {
            var field = new int[w * h];
            for (int i = 0; i < field.Length; ++i) field[i] = rnd.Below(64);
            return field;
        }

        /// <summary>Bilinear sample of a coarse field, wrapping — a seamless sky.</summary>
        private static int Sample(int[] field, int fw, int fh, int x, int y, int size)
        {
            int fx = x * fw / size;
            int fy = y * fh / size;

            int tx = (x * fw * 256 / size) & 0xFF;
            int ty = (y * fh * 256 / size) & 0xFF;

            int x0 = fx % fw, x1 = (fx + 1) % fw;
            int y0 = fy % fh, y1 = (fy + 1) % fh;

            int a = field[y0 * fw + x0], b = field[y0 * fw + x1];
            int c = field[y1 * fw + x0], d = field[y1 * fw + x1];

            int top = a + (b - a) * tx / 256;
            int bottom = c + (d - c) * tx / 256;

            return top + (bottom - top) * ty / 256;
        }

        /// <summary>
        /// A 320x200 picture: the title, the intermission and the ending screen.
        ///
        /// Text is left to the caller — these are backgrounds. A generated set that
        /// shipped a title with words on it would have to keep those words in sync with
        /// the engine's own strings, and they would be in the wrong language for half
        /// the players.
        /// </summary>
        public static byte[] Backdrop(byte[] pal, uint seed, int variant)
        {
            const int w = 320, h = 200;

            var nearest = new Nearest(pal);
            Rand rnd = new Rand(Hash("backdrop" + variant, seed));
            var pixels = new byte[w * h];

            var coarse = Noise(rnd, 8, 8);

            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    int big = Sample(coarse, 8, 8, x, y, Math.Max(w, h));

                    // A vignette: bright in the middle, dark at the edges, so any text
                    // drawn on top stays readable.
                    int dx = (x - w / 2) * 200 / (w / 2);
                    int dy = (y - h / 2) * 200 / (h / 2);
                    int vignette = 100 - (Math.Abs(dx) + Math.Abs(dy)) / 3;

                    int value = (10 + big / 3) * vignette / 100;

                    int r, g, b;

                    switch (variant)
                    {
                        case 1: r = value / 2; g = value * 3 / 4; b = value; break;   // intermission: cold
                        case 2: r = value; g = value / 2; b = value / 3; break;       // ending: red
                        default: r = value; g = value * 3 / 4; b = value / 2; break;  // title: brown
                    }

                    pixels[y * w + x] = nearest.Get(r, g, b);
                }
            }

            return Pack(w, h, 0, 0, pixels);
        }

        /// <summary>
        /// The status bar plate: 120x100 of brushed metal with rivets.
        ///
        /// Two variants exist because the game draws a second plate behind the numbers;
        /// giving them the same picture would make the bar look like one flat slab.
        /// </summary>
        public static byte[] Stone(byte[] pal, uint seed, int variant)
        {
            const int w = 120, h = 100;

            var nearest = new Nearest(pal);
            Rand rnd = new Rand(Hash("stone" + variant, seed));
            var pixels = new byte[w * h];

            int baseIndex = variant == 0 ? 96 : 96;

            // The plate is dark grey on purpose. The original's status bar is built from
            // palette 0x60..0x61 — rgb 32 and 31 out of 63 — and a first version of this
            // used the light-grey block instead, which made the whole HUD look washed
            // out next to the game's own art.
            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    // Horizontal brushing: noise that varies along y only.
                    int grain = rnd.Below(3);

                    int edge = (x == 0 || y == 0 || x == w - 1 || y == h - 1) ? 2 : 0;
                    int rivet = ((x % 30 == 5 || x % 30 == 25) && (y % 30 == 5 || y % 30 == 25)) ? -3 : 0;

                    // A shallow ramp: 0 is the brightest end of the block, so a small
                    // index keeps the plate near rgb 32 and the rivets a shade lighter.
                    int shade = 1 + grain + edge + rivet + variant;
                    if (shade < 0) shade = 0;
                    if (shade > 15) shade = 15;

                    pixels[y * w + x] = (byte)(baseIndex + shade);
                }
            }

            return Pack(w, h, 0, 0, pixels);
        }
    }
}
