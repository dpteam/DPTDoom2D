using System;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Game
{
    /// <summary>dots.c — blood, sparks and water splashes. One pixel each.</summary>
    public static class Dots
    {
        public const int MaxDot = 400;

        const int MaxIni = 50;
        const int MaxSr = 20;

        const int BlXV = 4, BlYV = 4, BlMinT = 10, BlMaxT = 14;
        const int SpV = 2, SpMinT = 5, SpMaxT = 7;

        sealed class Dot
        {
            public Obj O = new Obj();
            public byte C, T;
        }

        struct Ini { public int Xv, Yv; public byte C, T; }

        static readonly Dot[] dot = new Dot[MaxDot];
        static readonly Ini[] blIni = new Ini[MaxIni];
        static readonly Ini[] spIni = new Ini[MaxIni];
        static readonly int[] sxr = new int[MaxSr];
        static readonly int[] syr = new int[MaxSr];
        static int blR, spR, srR, ldot;

        static Dots()
        {
            for (int i = 0; i < MaxDot; ++i) dot[i] = new Dot();
        }

        /// <summary>How many bullet marks and sparks are alive, for the F2 stats tab.</summary>
        public static int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxDot; ++i) if (dot[i].T != 0) ++n;
                return n;
            }
        }

        public static void Save(BinWriter w)
        {
            int n = 0;
            for (int i = 0; i < MaxDot; ++i) if (dot[i].T != 0) ++n;
            w.Int(n);
            for (int i = 0; i < MaxDot; ++i)
                if (dot[i].T != 0)
                {
                    w.Int(dot[i].O.X); w.Int(dot[i].O.Y); w.Int(dot[i].O.XV); w.Int(dot[i].O.YV);
                    w.Int(dot[i].O.VX); w.Int(dot[i].O.VY); w.Int(dot[i].O.R); w.Int(dot[i].O.H);
                    w.Byte(dot[i].C); w.Byte(dot[i].T);
                }
        }

        public static void Load(BinReader r)
        {
            Init();
            int n = r.Int();
            for (int i = 0; i < n && i < MaxDot; ++i)
            {
                dot[i].O.X = r.Int(); dot[i].O.Y = r.Int(); dot[i].O.XV = r.Int(); dot[i].O.YV = r.Int();
                dot[i].O.VX = r.Int(); dot[i].O.VY = r.Int(); dot[i].O.R = r.Int(); dot[i].O.H = r.Int();
                dot[i].C = r.Byte(); dot[i].T = r.Byte();
            }
        }

        public static void Init()
        {
            for (int i = 0; i < MaxDot; ++i) { dot[i].T = 0; dot[i].O.R = 0; dot[i].O.H = 1; }
            ldot = 0;
        }

        static void IncDot()
        {
            if (++ldot >= MaxDot) ldot = 0;
        }

        public static void Alloc()
        {
            for (int i = 0; i < MaxIni; ++i)
            {
                blIni[i].Xv = Glob.Rand(BlXV * 2 + 1) - BlXV;
                blIni[i].Yv = -Glob.Rand(BlYV);
                blIni[i].C = (byte)(0xB0 + Glob.Rand(16));
                blIni[i].T = (byte)(Glob.Rand(BlMaxT - BlMinT + 1) + BlMinT);
                spIni[i].Xv = Glob.Rand(SpV * 2 + 1) - SpV;
                spIni[i].Yv = Glob.Rand(SpV * 2 + 1) - SpV;
                spIni[i].C = (byte)(0xA0 + Glob.Rand(6));
                spIni[i].T = (byte)(Glob.Rand(SpMaxT - SpMinT + 1) + SpMinT);
            }
            for (int i = 0; i < MaxSr; ++i)
            {
                sxr[i] = Glob.Rand(5) - 2;
                syr[i] = Glob.Rand(5) - 2;
            }
            blR = spR = srR = 0;
        }

        public static void Act()
        {
            Misc.ZDot = true;
            for (int i = 0; i < MaxDot; ++i)
            {
                if (dot[i].T == 0) continue;
                Obj o = dot[i].O;

                int xv = o.XV + o.VX;
                int yv = o.YV + o.VY;
                int s = Misc.MoveObj(o);
                if (dot[i].T < 254) --dot[i].T;

                if ((s & (Z.InWater | Z.FallOut)) != 0) { dot[i].T = 0; continue; }

                if ((s & Z.HitLand) != 0)
                {
                    if (o.XV == 0)
                    {
                        if (yv > 2)
                        {
                            if (xv == 0) o.VX = (Glob.Rand(2) != 0) ? -1 : 1;
                            else o.VX = Glob.Sign(o.VX);
                            if (Glob.Rand(yv) == 0) o.VX *= 2;
                            o.YV = yv - 2;
                        }
                    }
                    o.XV = 0;
                    if (dot[i].T > 4 && dot[i].T != 255) dot[i].T = 4;
                }
                if ((s & Z.HitWall) != 0)
                {
                    o.VX = Glob.Sign(xv) * 2;
                    o.VY = Glob.Sign(o.VY);
                    if (o.VY >= 0 && (Glob.Rand(4) & 3) != 0) --o.VY;
                    if (o.VY >= 0 && (Glob.Rand(2) & 1) != 0) --o.VY;
                }
                if ((s & Z.HitCeil) != 0) { o.XV = 0; o.YV = Glob.Rand(100) != 0 ? -2 : 0; }
            }
            Misc.ZDot = false;
        }

        public static void Draw()
        {
            for (int i = 0; i < MaxDot; ++i)
                if (dot[i].T != 0)
                    Vga.Dot(dot[i].O.X - World.WX + World.WD / 2,
                            dot[i].O.Y - World.WY + World.HT / 2 + 1 + World.WO,
                            dot[i].C);
        }

        public static void Add(int x, int y, int xv, int yv, byte c, byte t)
        {
            if (Misc.CanFit(x, y, 0, 1) == 0) return;
            int i = ldot;
            dot[i].O.X = x; dot[i].O.Y = y;
            dot[i].O.XV = xv; dot[i].O.YV = yv;
            dot[i].C = c; dot[i].T = t;
            dot[i].O.VX = dot[i].O.VY = 0;
            IncDot();
        }

        public static void Blood(int x, int y, int xv, int yv, int n)
        {
            for (int k = n; k != 0; --k)
            {
                int dx = x + sxr[srR], dy = y + syr[srR];
                if (Misc.CanFit(x, y, 0, 1) == 0) continue;
                int i = ldot;
                dot[i].O.X = dx; dot[i].O.Y = dy;
                dot[i].O.XV = blIni[blR].Xv + Misc.Dec(xv, 3);
                dot[i].O.YV = blIni[blR].Yv + Misc.Dec(yv, 3) - 3;
                dot[i].C = blIni[blR].C;
                dot[i].T = 255;
                dot[i].O.VX = dot[i].O.VY = 0;
                if (++blR >= MaxIni) blR = 0;
                if (++srR >= MaxSr) srR = 0;
                IncDot();
            }
        }

        public static void Spark(int x, int y, int xv, int yv, int n)
        {
            for (int k = n; k != 0; --k)
            {
                int dx = x + sxr[srR], dy = y + syr[srR];
                if (Misc.CanFit(x, y, 0, 1) == 0) continue;
                int i = ldot;
                dot[i].O.X = dx; dot[i].O.Y = dy;
                dot[i].O.XV = spIni[spR].Xv - xv / 4;
                dot[i].O.YV = spIni[spR].Yv - yv / 4;
                dot[i].C = spIni[spR].C;
                dot[i].T = spIni[spR].T;
                dot[i].O.VX = dot[i].O.VY = 0;
                if (++spR >= MaxIni) spR = 0;
                if (++srR >= MaxSr) srR = 0;
                IncDot();
            }
        }

        static readonly byte[] ct = { 0xC0, 0x70, 0xB0 };

        public static void Water(int x, int y, int xv, int yv, int n, int c)
        {
            if (c < 0 || c >= 3) return;
            c = ct[c];
            for (int k = n; k != 0; --k)
            {
                int dx = x + sxr[srR], dy = y + syr[srR];
                if (Misc.CanFit(x, y, 0, 1) == 0) continue;
                int i = ldot;
                dot[i].O.X = dx; dot[i].O.Y = dy;
                dot[i].O.XV = blIni[blR].Xv - Misc.Dec(xv, 3);
                dot[i].O.YV = blIni[blR].Yv - Math.Abs(yv);
                dot[i].C = (byte)(blIni[blR].C - 0xB0 + c);
                dot[i].T = 254;
                dot[i].O.VX = dot[i].O.VY = 0;
                if (++blR >= MaxIni) blR = 0;
                if (++srR >= MaxSr) srR = 0;
                IncDot();
            }
        }
    }
}
