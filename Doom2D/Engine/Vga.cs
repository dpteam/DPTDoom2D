using System;

namespace Doom2D.Engine
{
    /// <summary>
    /// vga.c — the software renderer.
    ///
    /// Doom2D draws into an 8-bit palette framebuffer and relies on colour
    /// indices for everything: distance shading is a colormap lookup, smoke is
    /// MIXMAP, player colours are indices 0x70..0x7F. Reproducing that in XNA
    /// means keeping the byte framebuffer and blitting it once per frame with a
    /// palette, which is exactly what this class does (the XNA side lives in
    /// Screen.cs).
    ///
    /// rembo runs the game at 400x300 logical pixels and doubles every pixel on
    /// the way to the 800x600 window; we keep the same logical size and let the
    /// sprite batch do the doubling with PointClamp.
    /// </summary>
    public static class Vga
    {
        /// <summary>SCRW/SCRH: the logical screen (vga.c divides the window by HQ).</summary>
        public static int SCRW = 400;
        public static int SCRH = 300;

        /// <summary>The framebuffer, one palette index per pixel.</summary>
        public static byte[] Buffer = new byte[SCRW * SCRH];

        // Clip rectangle, inclusive (cx1..cx2, cy1..cy2) as in vga.c.
        static int cx1, cx2, cy1, cy2;

        static int offx, offy;

        public static void Init(int width, int height)
        {
            SCRW = width;
            SCRH = height;
            Buffer = new byte[SCRW * SCRH];
            Offset(0, 0);
            SetRect(0, SCRW, 0, SCRH);
        }

        public static int OffsetX { get { return offx; } }
        public static int OffsetY { get { return offy; } }

        public static void Offset(int ox, int oy)
        {
            offx = ox;
            offy = oy;
        }

        /// <summary>V_center: centre the 320x200 artwork on the logical screen.</summary>
        public static void Center(bool on)
        {
            if (on) Offset(SCRW / 2 - 320 / 2, SCRH / 2 - 200 / 2);
            else Offset(0, 0);
        }

        /// <summary>V_setrect: clip rectangle, x/y/w/h in logical pixels.</summary>
        public static void SetRect(int x, int w, int y, int h)
        {
            cx1 = x;
            cx2 = x + w - 1;
            cy1 = y;
            cy2 = y + h - 1;
            if (cx1 < 0) cx1 = 0;
            if (cx2 >= SCRW) cx2 = SCRW - 1;
            if (cy1 < 0) cy1 = 0;
            if (cy2 >= SCRH) cy2 = SCRH - 1;
        }

        public static void PutPixel(int x, int y, byte c)
        {
            if (x >= cx1 && x <= cx2 && y >= cy1 && y <= cy2)
                Buffer[y * SCRW + x] = c;
        }

        public static byte GetPixel(int x, int y)
        {
            if (x >= cx1 && x <= cx2 && y >= cy1 && y <= cy2)
                return Buffer[y * SCRW + x];
            return 0;
        }

        public static void MapPixel(int x, int y, byte[] cmap)
        {
            byte c = GetPixel(x, y);
            PutPixel(x, y, cmap[c]);
        }

        /// <summary>V_dot.</summary>
        public static void Dot(int x, int y, byte c)
        {
            PutPixel(x, y, c);
        }

        /// <summary>V_clr / draw_rect: fill a rectangle with one colour. Note: ignores V_offset.</summary>
        public static void Clr(int x, int w, int y, int h, byte c)
        {
            int x0 = x, x1 = x + w - 1;
            int y0 = y, y1 = y + h - 1;
            if (x0 < cx1) x0 = cx1;
            if (x1 > cx2) x1 = cx2;
            if (y0 < cy1) y0 = cy1;
            if (y1 > cy2) y1 = cy2;
            if (x0 > x1 || y0 > y1) return;

            for (int py = y0; py <= y1; ++py)
            {
                int row = py * SCRW;
                for (int px = x0; px <= x1; ++px) Buffer[row + px] = c;
            }
        }

        /// <summary>draw_spr: the one place all sprite blitting goes through.</summary>
        /// <param name="d">bit 0 = mirrored horizontally, bit 1 = mirrored vertically.</param>
        /// <param name="c">player colour: remaps indices 0x70..0x7F, 0 = no remap.</param>
        public static void DrawSpr(int x, int y, VgaImage i, int d, int c)
        {
            if (i == null) return;

            x += offx;
            y += offy;
            if ((d & 1) != 0) x = x - i.W + i.SX; else x -= i.SX;
            if ((d & 2) != 0) y = y - i.H + i.SY; else y -= i.SY;

            if (!(x + i.W >= cx1 && x <= cx2 && y + i.H >= cy1 && y <= cy2)) return;

            byte[] px = i.Pixels;
            for (int ly = 0; ly < i.H; ++ly)
            {
                int ry = (d & 2) != 0 ? (i.H - ly - 1) : ly;
                int srcRow = ly * i.W;
                int dstRow = y + ry;
                for (int lx = 0; lx < i.W; ++lx)
                {
                    byte t = px[srcRow + lx];
                    if (t == 0) continue;
                    if (c != 0 && t >= 0x70 && t <= 0x7F) t = (byte)(t - 0x70 + c);
                    int rx = (d & 1) != 0 ? (i.W - lx - 1) : lx;
                    PutPixel(x + rx, dstRow, t);
                }
            }
        }

        /// <summary>V_pic.</summary>
        public static void Pic(int x, int y, VgaImage i)
        {
            DrawSpr(x, y, i, 0, 0);
        }

        /// <summary>V_spr.</summary>
        public static void Spr(int x, int y, VgaImage i)
        {
            DrawSpr(x, y, i, 0, 0);
        }

        /// <summary>V_spr2: mirrored horizontally.</summary>
        public static void Spr2(int x, int y, VgaImage i)
        {
            DrawSpr(x, y, i, 1, 0);
        }

        /// <summary>V_manspr: a "man" sprite with the player colour remap.</summary>
        public static void ManSpr(int x, int y, VgaImage i, int c)
        {
            DrawSpr(x, y, i, 0, c);
        }

        /// <summary>V_manspr2: mirrored + colour remap.</summary>
        public static void ManSpr2(int x, int y, VgaImage i, int c)
        {
            DrawSpr(x, y, i, 1, c);
        }

        /// <summary>V_rotspr: used by the tiled sky.</summary>
        public static void RotSpr(int x, int y, VgaImage i, int d)
        {
            x += i.W * ((d & 1) != 0 ? 1 : 0);
            y += i.H * ((d & 2) != 0 ? 1 : 0);
            DrawSpr(x, y, i, d, 0);
        }

        /// <summary>Per-pixel effect callback (spr_f in vga.h).</summary>
        public delegate void SprFunc(int x, int y, byte c);

        /// <summary>V_sprf: runs a per-pixel function over the opaque pixels of a sprite.</summary>
        public static void SprF(int x, int y, VgaImage i, SprFunc f)
        {
            if (i == null) return;
            x -= i.SX;
            y -= i.SY;

            byte[] px = i.Pixels;
            int p = 0;
            for (int cy = y; cy < y + i.H; ++cy)
            {
                for (int cx = x; cx < x + i.W; ++cx)
                {
                    byte c = px[p++];
                    if (c != 0) f(cx, cy, c);
                }
            }
        }

        /// <summary>V_remap_rect: apply a colormap to a screen rectangle.</summary>
        public static void RemapRect(int x, int y, int w, int h, byte[] cmap)
        {
            for (int px = x; px < x + w; ++px)
                for (int py = y; py < y + h; ++py)
                    MapPixel(px, py, cmap);
        }

        /// <summary>V_remap_rect with an explicit colormap row (clrmap + row*256).</summary>
        public static void RemapRect(int x, int y, int w, int h, byte[] cmap, int row)
        {
            int at = row * 256;
            for (int px = x; px < x + w; ++px)
                for (int py = y; py < y + h; ++py)
                {
                    byte c = GetPixel(px, py);
                    PutPixel(px, py, cmap[at + c]);
                }
        }

        /// <summary>smoke_sprf: smoke dissolves the pixel through MIXMAP.</summary>
        public static void SmokeSprFunc(int x, int y, byte c)
        {
            byte t = GetPixel(x, y);
            c = (byte)(c + Palette.Bright[t]);
            c = (byte)(c + 0x60);
            c ^= 0xF;
            PutPixel(x, y, Palette.MixMap[(c << 8) | t]);
        }

        static readonly byte[] flameTab = new byte[16];

        /// <summary>flame_sprf: fire recolours the pixel through the flame table.</summary>
        public static void FlameSprFunc(int x, int y, byte c)
        {
            byte t = GetPixel(x, y);
            c = (byte)(c + Palette.Bright[t]);
            PutPixel(x, y, flameTab[c & 15]);
        }

        public static byte[] FlameTable { get { return flameTab; } }
    }
}
