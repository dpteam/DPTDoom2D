using System;
using Doom2D.Engine;

namespace Doom2D.Game
{
    /// <summary>smoke.c — smoke puffs and flame. Positions are 24.8 fixed point.</summary>
    public static class Smoke
    {
        public const int MaxSmok = 500;

        const int MaxSr = 20;
        const int Smsn = 10;
        const int Flsn = 8;

        sealed class Sm
        {
            public int X, Y, Xv, Yv;
            public byte T, S;
            public int O;
        }

        static readonly Sm[] sm = new Sm[MaxSmok];
        static readonly int[] sxr = new int[MaxSr];
        static readonly int[] syr = new int[MaxSr];
        static int srR, lsm;

        static VgaImage[] spr, fspr;
        static Res.Locked burnsnd;
        static int burntm;

        static Smoke()
        {
            for (int i = 0; i < MaxSmok; ++i) sm[i] = new Sm();
        }

        public static void Save(BinWriter w)
        {
            int n = 0;
            for (int i = 0; i < MaxSmok; ++i) if (sm[i].T != 0) ++n;
            w.Int(n);
            for (int i = 0; i < MaxSmok; ++i)
                if (sm[i].T != 0)
                {
                    w.Int(sm[i].X); w.Int(sm[i].Y); w.Int(sm[i].Xv); w.Int(sm[i].Yv);
                    w.Byte(sm[i].T); w.Byte(sm[i].S); w.Short(sm[i].O);
                }
        }

        public static void Load(BinReader r)
        {
            Init();
            int n = r.Int();
            for (int i = 0; i < n && i < MaxSmok; ++i)
            {
                sm[i].X = r.Int(); sm[i].Y = r.Int(); sm[i].Xv = r.Int(); sm[i].Yv = r.Int();
                sm[i].T = r.Byte(); sm[i].S = r.Byte(); sm[i].O = r.Short();
            }
        }

        public static void Init()
        {
            for (int i = 0; i < MaxSmok; ++i) sm[i].T = 0;
            lsm = 0;
            burntm = 0;
        }

        public static void Alloc()
        {
            burnsnd = Misc.GetSnd("BURN");
            spr = new VgaImage[Smsn];
            for (int i = 0; i < Smsn; ++i) spr[i] = Misc.GetSpr("SMOK", i, 0);
            fspr = new VgaImage[Flsn];
            for (int i = 0; i < Flsn; ++i) fspr[i] = Misc.GetSpr("FLAM", i, 0);
            for (int i = 0; i < MaxSr; ++i)
            {
                sxr[i] = Glob.Rand(513) - 256;
                syr[i] = Glob.Rand(513) - 256;
            }
            srR = 0;
        }

        static void IncLast()
        {
            if (++lsm >= MaxSmok) lsm = 0;
        }

        public static void Act()
        {
            if (burntm != 0) --burntm;

            for (int i = 0; i < MaxSmok; ++i)
            {
                Sm s = sm[i];
                if (s.T == 0) continue;

                if (s.S != 0)
                {
                    int ox = s.X, oy = s.Y;
                    s.Xv = Misc.Dec(s.Xv, 20);
                    s.Yv = Misc.Dec(s.Yv, 20);
                    s.X += s.Xv / 2; s.Y += s.Yv / 2;
                    if (Misc.CanFit(s.X >> 8, (s.Y >> 8) + 3, 3, 7) == 0 ||
                        Misc.InWater(s.X >> 8, (s.Y >> 8) + 3, 3, 7) != 0)
                    {
                        s.X = ox; s.Y = oy;
                    }

                    ox = s.X; oy = s.Y;
                    s.X += s.Xv / 2; s.Y += s.Yv / 2;
                    if (Misc.CanFit(s.X >> 8, (s.Y >> 8) + 3, 3, 7) == 0 ||
                        Misc.InWater(s.X >> 8, (s.Y >> 8) + 3, 3, 7) != 0)
                    {
                        s.X = ox; s.Y = oy;
                    }

                    if (s.O != -3)
                    {
                        Obj o = new Obj();
                        o.X = s.X >> 8; o.Y = s.Y >> 8;
                        o.XV = s.Xv >> 10; o.YV = s.Yv >> 10;
                        o.VX = o.VY = 0;
                        if ((Game.Time & 3) == 0) Misc.Hit(o, 1, s.O, Hit.Flame);
                    }
                }
                else
                {
                    int ox = s.X, oy = s.Y;
                    s.Xv = Misc.Dec(s.Xv, 20);
                    s.Yv = Misc.Dec(s.Yv, 20);
                    s.X += s.Xv; s.Y += s.Yv;
                    if (Misc.CanFit(s.X >> 8, (s.Y >> 8) + 3, 3, 7) == 0 ||
                        Misc.InWater(s.X >> 8, (s.Y >> 8) + 3, 3, 7) != 0)
                    {
                        s.X = ox; s.Y = oy;
                    }
                }
                --s.T;
            }
        }

        public static void Draw()
        {
            for (int i = 0; i < MaxSmok; ++i)
            {
                Sm s = sm[i];
                if (s.T == 0) continue;

                int frame;
                switch (s.S)
                {
                    case 0:
                        frame = s.T;
                        if (frame >= (Smsn - 1) * 3) frame = 0; else frame = Smsn - 1 - frame / 3;
                        Vga.SprF((s.X >> 8) - World.WX + World.WD / 2,
                                 (s.Y >> 8) - World.WY + World.HT / 2 + 1 + World.WO,
                                 spr[frame], Vga.SmokeSprFunc);
                        break;
                    case 1:
                        frame = s.T;
                        if (frame >= Flsn - 1) frame = 0; else frame = Flsn - 1 - frame;
                        Vga.SprF((s.X >> 8) - World.WX + World.WD / 2,
                                 (s.Y >> 8) - World.WY + World.HT / 2 + 1 + World.WO,
                                 fspr[frame], Vga.FlameSprFunc);
                        break;
                }
            }
        }

        public static void Add(int x, int y, int xv, int yv, byte t, byte s, int o)
        {
            if (Misc.CanFit(x >> 8, (y >> 8) + 3, 3, 7) == 0) return;
            if (Misc.InWater(x >> 8, (y >> 8) + 3, 3, 7) != 0)
            {
                Fx.Bubble(x >> 8, y >> 8, xv, yv, 1);
                return;
            }
            int i = lsm;
            sm[i].X = x; sm[i].Y = y;
            sm[i].Xv = xv; sm[i].Yv = yv;
            sm[i].T = t; sm[i].S = s;
            sm[i].O = o;
            IncLast();
        }

        public static void Gas(int x0, int y0, int xr, int yr, int xv, int yv, int k)
        {
            xv = -xv; yv = -yv;
            int sxv = xv * k, syv = yv * k;
            k = Math.Max(Math.Abs(xv), Math.Abs(yv));
            if (k == 0) return;

            for (int i = 0; i <= k; i += 3)
            {
                int x = ((xv * i / k + x0) << 8) + sxr[srR] * xr;
                int y = ((yv * i / k + y0) << 8) + syr[srR] * yr;
                if (++srR >= MaxSr) srR = 0;
                Add(x, y, sxv, syv, (byte)(Smsn * 3), 0, -3);
            }
        }

        public static void Flame(int x0, int y0, int ox, int oy, int xr, int yr, int xv, int yv, int k, int o)
        {
            int sxv = xv * k, syv = yv * k;
            xv = xv - (ox << 8); yv = yv - (oy << 8);
            k = Math.Max(Math.Abs(xv), Math.Abs(yv));
            if (k == 0) return;
            if (burntm == 0) burntm = Misc.Sound(burnsnd, 128);

            for (int i = 0; i <= k; i += 200)
            {
                int x = xv * i / k + (x0 << 8) + sxr[srR] * xr;
                int y = yv * i / k + (y0 << 8) + syr[srR] * yr;
                if (++srR >= MaxSr) srR = 0;
                Add(x, y, sxv, syv, (byte)Flsn, 1, o);
            }
        }
    }
}
