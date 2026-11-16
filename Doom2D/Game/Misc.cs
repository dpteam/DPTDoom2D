using System;
using Doom2D.Engine;

namespace Doom2D.Game
{
    /// <summary>
    /// miscc.c — the shared physics and the status-bar text.
    ///
    /// Z_moveobj is the whole movement model: gravity, water drag, stepping in
    /// at most 7 pixels so nothing tunnels through an 8-pixel cell, and the
    /// collisions that are reported back as Z_* bits.
    /// </summary>
    public static class Misc
    {
        public const int MaxYv = 30;
        public const int MaxAir = 1091;

        /// <summary>z_dot: blood/spark dots ignore the "no liquid" rule for walls.</summary>
        public static bool ZDot;

        /// <summary>wfront: the foreground wall index of the last water cell entered.</summary>
        public static int WFront;

        public static int Prx, Pry;

        static VgaImage[] sth;
        static VgaImage[] bfh, sfh;
        static Res.Locked[] bulsnd;
        static VgaImage stone, stone2;
        static VgaImage[] keys;

        // ---------------------------------------------------------------- sprites

        public static Res.Locked GetSprLocked(string name, int s, int d, out bool mirrored)
        {
            int h = Wad.GetSprId(name, s, d);
            mirrored = (h & 0x8000) != 0;
            return Res.Lock(h);
        }

        public static VgaImage GetSpr(string name, int s, int d)
        {
            return Res.Img(Wad.GetSprId(name, s, d));
        }

        public static VgaImage GetSpr(string name, int s, int d, byte[] dirs, int at)
        {
            bool m;
            Res.Locked l = GetSprLocked(name, s, d, out m);
            dirs[at] = (byte)(m ? 1 : 0);
            return l == null ? null : l.Image;
        }

        public static Res.Locked GetSnd(string name)
        {
            return Res.Lock(Wad.GetResId("DS" + name));
        }

        /// <summary>Z_sound: returns a duration in ticks, used to pace repeat sounds.</summary>
        public static int Sound(Res.Locked s, int v)
        {
            if (s == null) return 0;
            Doom2D.Engine.Sound.Play(s, -1, 1024, v);
            return Wad.GetResLen(s.Id) / 605;
        }

        // ---------------------------------------------------------------- HUD font

        public static void InitSt()
        {
            string[] nm = {
                "STTNUM0","STTNUM1","STTNUM2","STTNUM3","STTNUM4",
                "STTNUM5","STTNUM6","STTNUM7","STTNUM8","STTNUM9",
                "STTMINUS","STTPRCNT",
                "FISTA0","CSAWA0","PISTA0","SHOTA0","SGN2A0","MGUNA0","LAUNA0",
                "PLASA0","BFUGA0","GUN2A0"
            };

            stone = Res.Img(Wad.GetResId("STONE"));
            stone2 = Res.Img(Wad.GetResId("STONE2"));
            keys = new VgaImage[3];
            keys[0] = Res.Img(Wad.GetResId("KEYRA0"));
            keys[1] = Res.Img(Wad.GetResId("KEYGA0"));
            keys[2] = Res.Img(Wad.GetResId("KEYBA0"));

            sth = new VgaImage[22];
            for (int i = 0; i < 22; ++i) sth[i] = Res.Img(Wad.GetResId(nm[i]));

            bfh = new VgaImage[160 - '!'];
            sfh = new VgaImage[160 - '!'];
            for (int i = '!'; i < 160; ++i)
                bfh[i - '!'] = Res.Img(Wad.FindRes("STBF_" + (char)i));
            for (int i = '!'; i < 160; ++i)
                sfh[i - '!'] = Res.Img(Wad.FindRes(string.Format("STCFN{0:000}", i)));

            for (int i = '0'; i <= '9'; ++i)
                bfh[i - '!'] = Res.Img(Wad.GetResId("WINUM" + (char)i));
            bfh[':' - '!'] = Res.Img(Wad.GetResId("WICOLON"));

            bulsnd = new Res.Locked[2];
            bulsnd[0] = GetSnd("BUL1");
            bulsnd[1] = GetSnd("BUL2");
        }

        public static void PutBfCh(int c)
        {
            VgaImage p = (c > 32 && c < 160) ? bfh[c - '!'] : null;
            if (p != null) { Vga.Spr(Prx, Pry, p); Prx += p.W - 1; }
            else Prx += 12;
        }

        public static void PutSfCh(int c)
        {
            VgaImage p = (c > 32 && c < 160) ? sfh[c - '!'] : null;
            if (p != null) { Vga.Spr(Prx, Pry, p); Prx += p.W - 1; }
            else Prx += 7;
        }

        public static void Gotoxy(int x, int y) { Prx = x; Pry = y; }

        public static void PrintBf(string s, params object[] args)
        {
            string buf = args.Length == 0 ? s : string.Format(s, args);
            for (int i = 0; i < buf.Length; ++i)
                switch (buf[i])
                {
                    case '\n': Pry += 13; Prx = 0; break;
                    case '\r': Prx = 0; break;
                    default: PutBfCh(buf[i]); break;
                }
        }

        public static void PrintSf(string s, params object[] args)
        {
            string buf = args.Length == 0 ? s : string.Format(s, args);
            for (int i = 0; i < buf.Length; ++i)
                switch (buf[i])
                {
                    case '\n': Pry += 8; Prx = 0; break;
                    case '\r': Prx = 0; break;
                    default: PutSfCh(buf[i]); break;
                }
        }

        public static void DrawSpr(int x, int y, VgaImage p, bool dir)
        {
            int sx = x - World.WX + World.WD / 2;
            int sy = y - World.WY + World.HT / 2 + 1 + World.WO;
            if (dir) Vga.Spr2(sx, sy, p); else Vga.Spr(sx, sy, p);
        }

        public static void DrawManSpr(int x, int y, VgaImage p, bool dir, byte color)
        {
            int sx = x - World.WX + World.WD / 2;
            int sy = y - World.WY + World.HT / 2 + 1 + World.WO;
            if (dir) Vga.ManSpr2(sx, sy, p, color); else Vga.ManSpr(sx, sy, p, color);
        }

        /// <summary>Z_clrst: the stone panel behind the status bar.</summary>
        public static void ClrSt()
        {
            Vga.Pic(Vga.SCRW - 120, World.WO, stone);
            int y = stone.H;
            while (y < World.HT)
            {
                Vga.Pic(Vga.SCRW - 120, World.WO + y, stone2);
                y += stone.H;
            }
        }

        public static void DrawStLives(int n)
        {
            Vga.SetRect(Vga.SCRW - 40, 30, World.WO, 40); ClrSt();
            Vga.Spr(Vga.SCRW - 35, World.WO + 17, sth[n]);
        }

        public static void DrawStKeys(byte k)
        {
            Vga.SetRect(Vga.SCRW - 120, 70, World.WO + 77, 23); ClrSt();
            k >>= 4;
            int x = Vga.SCRW - 75;
            for (int n = 0; n < 3; ++n, k >>= 1, x += 9)
                if ((k & 1) != 0) Vga.Spr(x, World.WO + 91, keys[n]);
        }

        public static void DrawStAir(int a)
        {
            Vga.SetRect(Vga.SCRW - 120, 120, World.WO + 49, 2); ClrSt();
            if (a <= 0) return;
            if (a > MaxAir) a = MaxAir;
            a = a * 100 / MaxAir;
            if (a == 0) return;
            Vga.Clr(Vga.SCRW - 110, a, World.WO + 49, 2, 0xC8);
        }

        public static void DrawStPrcnt(int y, int n)
        {
            Vga.SetRect(Vga.SCRW - 120, 70, y * 19 + 7 + World.WO, 19); ClrSt();
            string s = string.Format("{0,3}%", n);
            int x = Vga.SCRW - 110;
            for (int i = 0; i < s.Length; ++i, x += 14)
            {
                int c = CharToNum(s[i]);
                if (c >= 0) Vga.Spr(x, y * 19 + 7 + World.WO, sth[c]);
            }
        }

        public static void DrawStNum(int n)
        {
            Vga.SetRect(Vga.SCRW - 50, 50, World.WO + 77, 23); ClrSt();
            if (!Game.Dm) return;
            string s = n.ToString();
            int x = (115 - s.Length * 14) + Vga.SCRW - 120;
            for (int i = 0; i < s.Length; ++i, x += 14)
            {
                int c = CharToNum(s[i]);
                if (c >= 0) Vga.Spr(x, World.WO + 77 + 5, sth[c]);
            }
        }

        public static void DrawStWpn(int n, int a)
        {
            Vga.SetRect(Vga.SCRW - 120, 120, World.WO + 58, 23); ClrSt();
            if (n >= 0) Vga.Spr(Vga.SCRW - 88, World.WO + 58 + 19, sth[n + 12]);
            if (n >= 2)
            {
                string s = a.ToString();
                int x = Vga.SCRW - 10 - s.Length * 14;
                for (int i = 0; i < s.Length; ++i, x += 14)
                {
                    int c = CharToNum(s[i]);
                    if (c >= 0) Vga.Spr(x, World.WO + 58 + 2, sth[c]);
                }
            }
        }

        static int CharToNum(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c == '-') return 10;
            if (c == '%') return 11;
            return -1;
        }

        // ---------------------------------------------------------------- physics

        public static int Sign(int a) { return a > 0 ? 1 : (a < 0 ? -1 : 0); }

        public static int Dec(int a, int b)
        {
            if (Math.Abs(a) <= b) return 0;
            if (a > 0) return a - b;
            if (a < 0) return a + b;
            return 0;
        }

        static byte CellAt(int x, int y)
        {
            if (x < 0 || x >= World.FLDW || y < 0 || y >= World.FLDH) return 0;
            return World.Fld[y * World.FLDW + x];
        }

        static int FlagAt(int fldf, int fldb)
        {
            return World.WalF[fldf] | World.WalF[fldb];
        }

        public static int CanStand(int x, int y, int r)
        {
            int i = (x - r) / World.CELW;
            int xe = (x + r) / World.CELW;
            int ye = (y + 1) / World.CELH;
            if (ye >= World.FLDH || ye < 0) return 0;
            if (i < 0) i = 0;
            if (xe >= World.FLDW) xe = World.FLDW - 1;
            for (; i <= xe; ++i)
            {
                byte c = World.Fld[ye * World.FLDW + i];
                if (c == 1 || c == 2 || c == 4)
                {
                    if (!ZDot) return 1;
                    if ((FlagAt(World.FldF[ye * World.FLDW + i], World.FldB[ye * World.FLDW + i]) & 2) == 0) return 1;
                }
            }
            return 0;
        }

        public static int HitCeil(int x, int y, int r, int h)
        {
            int i = (x - r) / World.CELW;
            int xe = (x + r) / World.CELW;
            int ye = (y - h + 1) / World.CELH;
            if (ye >= World.FLDH || ye < 0) return 0;
            if (i < 0) i = 0;
            if (xe >= World.FLDW) xe = World.FLDW - 1;
            for (; i <= xe; ++i)
            {
                byte c = World.Fld[ye * World.FLDW + i];
                if (c == 1 || c == 2)
                {
                    if (!ZDot) return 1;
                    if ((FlagAt(World.FldF[ye * World.FLDW + i], World.FldB[ye * World.FLDW + i]) & 2) == 0) return 1;
                }
            }
            return 0;
        }

        public static int CanFit(int x, int y, int r, int h)
        {
            int sx = (x - r) / World.CELW;
            int sy = (y - h + 1) / World.CELH;
            if (sx < 0) sx = 0;
            if (sy < 0) sy = 0;
            int xe = (x + r) / World.CELW;
            int ye = y / World.CELH;
            if (xe >= World.FLDW) xe = World.FLDW - 1;
            if (ye >= World.FLDH) ye = World.FLDH - 1;

            for (int i = sx; i <= xe; ++i)
                for (int j = sy; j <= ye; ++j)
                {
                    byte c = World.Fld[j * World.FLDW + i];
                    if (c == 1 || c == 2)
                    {
                        if (!ZDot) return 0;
                        if ((FlagAt(World.FldF[j * World.FLDW + i], World.FldB[j * World.FLDW + i]) & 2) == 0) return 0;
                    }
                }
            return 1;
        }

        public static int InLift(int x, int y, int r, int h)
        {
            int sx = (x - r) / World.CELW;
            int sy = (y - h + 1) / World.CELH;
            if (sx < 0) sx = 0;
            if (sy < 0) sy = 0;
            int xe = (x + r) / World.CELW;
            int ye = (y - 1) / World.CELH;
            if (xe >= World.FLDW) xe = World.FLDW - 1;
            if (ye >= World.FLDH) ye = World.FLDH - 1;

            for (int i = sx; i <= xe; ++i)
                for (int j = sy; j <= ye; ++j)
                {
                    byte c = World.Fld[j * World.FLDW + i];
                    if (c == 9 || c == 10) return c - 8;
                }
            return 0;
        }

        public static int IsBlocked(int x, int y, int r, int h, int xv)
        {
            int sx = (x - r) / World.CELW;
            int sy = (y - h + 1) / World.CELH;
            if (sx < 0) sx = 0;
            if (sy < 0) sy = 0;
            int xe = (x + r) / World.CELW;
            int ye = (y - 1) / World.CELH;
            if (xv < 0) xe = sx;
            else if (xv > 0) sx = xe;
            if (xe >= World.FLDW) xe = World.FLDW - 1;
            if (ye >= World.FLDH) ye = World.FLDH - 1;

            for (int i = sx; i <= xe; ++i)
                for (int j = sy; j <= ye; ++j)
                    if (World.Fld[j * World.FLDW + i] == 8) return 1;
            return 0;
        }

        public static int IsTrapped(int x, int y, int r, int h)
        {
            int sx = (x - r) / World.CELW;
            int sy = (y - h + 1) / World.CELH;
            if (sx < 0) sx = 0;
            if (sy < 0) sy = 0;
            int xe = (x + r) / World.CELW;
            int ye = (y - 1) / World.CELH;
            if (xe >= World.FLDW) xe = World.FLDW - 1;
            if (ye >= World.FLDH) ye = World.FLDH - 1;

            for (int i = sx; i <= xe; ++i)
                for (int j = sy; j <= ye; ++j)
                    if (World.Fld[j * World.FLDW + i] == 255) return 1;
            return 0;
        }

        public static void SetSpeed(Obj o, int s)
        {
            int m = Math.Max(Math.Abs(o.XV), Math.Abs(o.YV));
            if (m == 0) m = 1;
            o.XV = o.XV * s / m;
            o.YV = o.YV * s / m;
        }

        public static int InWater(int x, int y, int r, int h)
        {
            int sx = (x - r) / World.CELW;
            int sy = (y - h + 1) / World.CELH;
            if (sx < 0) sx = 0;
            if (sy < 0) sy = 0;
            int xe = (x + r) / World.CELW;
            int ye = (y - h / 2) / World.CELH;
            if (xe >= World.FLDW) xe = World.FLDW - 1;
            if (ye >= World.FLDH) ye = World.FLDH - 1;

            for (int i = sx; i <= xe; ++i)
                for (int j = sy; j <= ye; ++j)
                {
                    byte c = World.Fld[j * World.FLDW + i];
                    if (c >= 5 && c <= 7) { WFront = World.FldF[j * World.FLDW + i]; return 1; }
                }
            return 0;
        }

        static readonly byte[] acidTab = { 0, 5, 10, 20 };

        public static int GetAcid(int x, int y, int r, int h)
        {
            int a = 0;
            int sx = (x - r) / World.CELW;
            int sy = (y - h + 1) / World.CELH;
            if (sx < 0) sx = 0;
            if (sy < 0) sy = 0;
            int xe = (x + r) / World.CELW;
            int ye = y / World.CELH;
            if (xe >= World.FLDW) xe = World.FLDW - 1;
            if (ye >= World.FLDH) ye = World.FLDH - 1;

            for (int i = sx; i <= xe; ++i)
                for (int j = sy; j <= ye; ++j)
                {
                    byte c = World.Fld[j * World.FLDW + i];
                    if (c == 6) a |= 1;
                    else if (c == 7) a |= 2;
                }
            return acidTab[a];
        }

        public static int CanBreathe(int x, int y, int r, int h)
        {
            int sx = (x - r) / World.CELW;
            int sy = (y - h + 1) / World.CELH;
            if (sx < 0) sx = 0;
            if (sy < 0) sy = 0;
            int xe = (x + r) / World.CELW;
            int ye = (y - h / 2) / World.CELH;
            if (xe >= World.FLDW) xe = World.FLDW - 1;
            if (ye >= World.FLDH) ye = World.FLDH - 1;
            if (sx > xe || sy > ye) return 1;

            for (int i = sx; i <= xe; ++i)
                for (int j = sy; j <= ye; ++j)
                {
                    byte c = World.Fld[j * World.FLDW + i];
                    if (c == 0 || c == 3 || c == 9 || c == 10) return 1;
                }
            return 0;
        }

        public static bool Overlap(Obj a, Obj b)
        {
            if (a.X - a.R > b.X + b.R) return false;
            if (a.X + a.R < b.X - b.R) return false;
            if (a.Y <= b.Y - b.H) return false;
            if (a.Y - a.H >= b.Y) return false;
            return true;
        }

        public static void KickObj(Obj o, int x, int y, int pwr)
        {
            int dx = o.X - x;
            int dy = o.Y - o.H / 2 - y;
            int m = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (m == 0) m = 1;
            o.VX += dx * pwr / m;
            o.VY += dy * pwr / m;
        }

        public static int CanSee(int x, int y, int xd, int yd)
        {
            int sx, sy;
            xd -= x; yd -= y;
            if (xd > 0) sx = 1; else if (xd < 0) sx = -1; else sx = 0;
            if (yd > 0) sy = 1; else if (yd < 0) sy = -1; else sy = 0;
            if (xd == 0 && yd == 0) return 1;

            xd = Math.Abs(xd); yd = Math.Abs(yd);
            int d = xd > yd ? xd : yd;
            long xe = 0, ye = 0;

            for (int i = 0; i <= d; )
            {
                if (x < 0 || x >= World.FLDW * 8 || y < 0 || y >= World.FLDH * 8) return 0;

                if ((Bmap.At(x >> 5, y >> 5) & Bmap.Wall) != 0)
                {
                    int c = CellAt(x >> 3, y >> 3);
                    if (c == 1 || c == 2) return 0;
                    xe += xd << 3;
                    if (xe >= d) { x += (int)(xe / d) * sx; xe %= d; }
                    ye += yd << 3;
                    if (ye >= d) { y += (int)(ye / d) * sy; ye %= d; }
                    i += 8;
                }
                else
                {
                    int m, s;
                    if (sx == 0) m = 0;
                    else { m = x & 31; if (sx > 0) m ^= 31; ++m; }
                    if (sy == 0) s = 0;
                    else { s = y & 31; if (sy > 0) s ^= 31; ++s; }
                    if ((s < m && s != 0) || m == 0) m = s;
                    i += m;
                    x += (int)((xd * m + xe) / d) * sx; xe = (xd * m + xe) % d;
                    y += (int)((yd * m + ye) / d) * sy; ye = (yd * m + ye) % d;
                }
            }
            return 1;
        }

        public static int Look(Obj a, Obj b, int d)
        {
            if (Sign(b.X - a.X) != d * 2 - 1) return 0;
            return CanSee(a.X, a.Y - a.H / 2, b.X, b.Y - b.H / 2);
        }

        static void WVel(Obj p, bool x)
        {
            int v = x ? p.XV : p.YV;
            int a = Math.Abs(v) + 1;
            if (a > 5)
            {
                if (x) p.XV = Dec(p.XV, a / 2 - 2);
                else p.YV = Dec(p.YV, a / 2 - 2);
            }
        }

        /// <summary>Z_moveobj: the movement/collision kernel.</summary>
        public static int MoveObj(Obj p)
        {
            int st = 0;

            int x = p.X, y = p.Y, r = p.R, h = p.H;

            switch (InLift(x, y, r, h))
            {
                case 0:
                    if (++p.YV > MaxYv) --p.YV;
                    break;
                case 1:
                    if (--p.YV < -5) ++p.YV;
                    break;
                case 2:
                    if (p.YV > 5) { --p.YV; break; }
                    ++p.YV;
                    break;
            }

            bool inw = InWater(x, y, r, h) != 0;
            if (inw)
            {
                st |= Z.InWater;
                WVel(p, true);
                WVel(p, false);
                WVel2(p, true);
                WVel2(p, false);
            }

            p.VX = Dec(p.VX, 1);
            p.VY = Dec(p.VY, 1);

            int xv = p.XV + p.VX;
            int yv = p.YV + p.VY;

            while (xv != 0 || yv != 0)
            {
                if (x < -100 || x >= World.FLDW * 8 + 100 || y < -100 || y >= World.FLDH * 8 + 100)
                    st |= Z.FallOut;

                int lx = x;
                x += Math.Abs(xv) <= 7 ? xv : (xv > 0 ? 7 : -7);
                if (Monsters.ZMon) if (IsBlocked(x, y, r, h, xv) != 0) st |= Z.Block;
                if (CanFit(x, y, r, h) == 0)
                {
                    if (xv == 0) x = lx;
                    else if (xv < 0) x = ((lx - r) & 0xFFF8) + r;
                    else x = ((lx + r) & 0xFFF8) - r + 7;
                    xv = p.XV = p.VX = 0;
                    st |= Z.HitWall;
                }
                xv -= Math.Abs(xv) <= 7 ? xv : (xv > 0 ? 7 : -7);

                int ly = y;
                y += Math.Abs(yv) <= 7 ? yv : (yv > 0 ? 7 : -7);
                if (yv >= 8) --y;

                if (yv < 0 && HitCeil(x, y, r, h) != 0)
                {
                    y = ((ly - h + 1) & 0xFFF8) + h - 1;
                    yv = p.VY = 1; p.YV = 0;
                    st |= Z.HitCeil;
                }
                if (yv > 0 && CanStand(x, y, r) != 0)
                {
                    y = ((y + 1) & 0xFFF8) - 1;
                    yv = p.YV = p.VY = 0;
                    st |= Z.HitLand;
                }
                yv -= Math.Abs(yv) <= 7 ? yv : (yv > 0 ? 7 : -7);
            }

            p.X = x; p.Y = y;

            if (InWater(x, y, r, h) != 0)
            {
                st |= Z.InWater;
                if (!inw) st |= Z.HitWater;
            }
            else if (inw) st |= Z.HitAir;

            return st;
        }

        static void WVel2(Obj p, bool x)
        {
            int v = x ? p.VX : p.VY;
            int a = Math.Abs(v) + 1;
            if (a > 5)
            {
                if (x) p.VX = Dec(p.VX, a / 2 - 2);
                else p.VY = Dec(p.VY, a / 2 - 2);
            }
        }

        public static void Splash(Obj p, int n)
        {
            Sound(bulsnd[0], 128);
            Dots.Water(p.X, p.Y - p.H / 2, p.XV + p.VX, p.YV + p.VY, n,
                World.WalWater[WFront] - 1);
        }

        public static void CalcTime(uint t, out int h, out int m, out int s)
        {
            t = t * Glob.Delay;
            t = t / 1000;
            s = (int)(t % 60);
            t -= (uint)s;
            t /= 60;
            m = (int)(t % 60);
            t -= (uint)m;
            t /= 60;
            h = (int)t;
        }

        // ---------------------------------------------------- forwarded to monsters

        public static int Hit(Obj o, int d, int own, int t) { return Monsters.HitArea(o, d, own, t); }
        public static int HitObj(int obj, int d, int own, int t) { return Monsters.HitObj(obj, d, own, t); }
        public static void Explode(int x, int y, int rad, int own) { Monsters.Explode(x, y, rad, own); }
        public static void Bfg9000(int x, int y, int own) { Monsters.Bfg9000(x, y, own); }
        public static int ChkTrap(int t, int d, int o, int ht) { return Switches.ChkTrap(t, d, o, ht); }
        public static void WaterTrap(Obj o) { Switches.WaterTrap(o); }
        public static void Untrap(byte t) { Switches.Untrap(t); }
    }
}
