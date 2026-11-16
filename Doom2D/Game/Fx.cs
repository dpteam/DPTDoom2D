using System;
using System.Globalization;
using Doom2D.Engine;

namespace Doom2D.Game
{
    /// <summary>fx.c — teleport fog, item fog and bubbles.</summary>
    public static class Fx
    {
        public const int MaxFx = 300;

        const int None = 0, TFog = 1, IFog = 2, Bubl = 3;

        sealed class FxEntry
        {
            public int X, Y, Xv, Yv;
            public int T, S;
        }

        static readonly FxEntry[] fx = new FxEntry[MaxFx];
        static VgaImage[] spr;
        static byte[] sprd;
        static Res.Locked[] bsnd;
        static int bubsn, last;

        // fx.c builds dmap from a fixed sine table and a shuffle; both are reproduced.
        const int SinP = 256;
        const int SinPM = SinP - 1;
        const int SinW = SinP / 100;

        static readonly int[] stdsin = LoadSin();
        static readonly int[] sintab = new int[SinP];
        static readonly byte[] dmap = new byte[32 * 32];

        static Fx()
        {
            for (int i = 0; i < MaxFx; ++i) fx[i] = new FxEntry();
        }

        static int[] LoadSin()
        {
            // fx1sin.dat is 256 integers, one per line.
            int[] t = new int[SinP];
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
            using (System.IO.Stream s = asm.GetManifestResourceStream("Doom2D.fx1sin.dat"))
            {
                if (s == null) return t;
                using (System.IO.StreamReader r = new System.IO.StreamReader(s))
                {
                    int i = 0;
                    string line;
                    while ((line = r.ReadLine()) != null && i < SinP)
                    {
                        int v;
                        if (int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                            t[i++] = v;
                    }
                }
            }
            return t;
        }

        static int Isin(int a) { return sintab[a & SinPM]; }

        static void SetAmp(int a)
        {
            for (int i = 0; i < SinP; ++i) sintab[i] = stdsin[i] * a;
        }

        /// <summary>FX_trans1: the screen-warp transition between levels.</summary>
        public static void Trans1(int t)
        {
            SetAmp(t);
            int k = t;
            t *= SinW;
            for (int y = 0, p = 0; y < 200; ++y)
                for (int x = 0; x < 320; ++x, ++p)
                {
                    if (dmap[(y & 31) * 32 + (x & 31)] >= k)
                    {
                        int u = x + (Isin(y + t) >> 16);
                        int v = y + (Isin(u + t) >> 16);
                        if (u < 0 || u >= 320 || v < 0 || v >= 200) Vga.Buffer[p] = 0;
                        else Vga.Buffer[p] = TransScr1[v * 320 + u];
                    }
                    else Vga.Buffer[p] = TransScr2[p];
                }
        }

        public static byte[] TransScr1 = new byte[64000];
        public static byte[] TransScr2 = new byte[64000];

        static void InitFx1Sin()
        {
            for (int i = 0; i < dmap.Length; ++i) dmap[i] = 0;

            int rr = 32 * 32;
            for (int i = 1; i < 64; ++i)
            {
                for (int l = 32 * 32 / 64; l != 0; --l, --rr)
                {
                    int r = Glob.Rand(rr);
                    int j = 0;
                    for (; r != 0; --r, ++j)
                        while (dmap[j] != 0) ++j;
                    while (dmap[j] != 0) ++j;
                    dmap[j] = (byte)i;
                }
            }
        }

        public static void Save(BinWriter w)
        {
            int n = 0;
            for (int i = 0; i < MaxFx; ++i) if (fx[i].T != 0) ++n;
            w.Int(n);
            for (int i = 0; i < MaxFx; ++i)
                if (fx[i].T != 0)
                {
                    w.Int(fx[i].X); w.Int(fx[i].Y); w.Int(fx[i].Xv); w.Int(fx[i].Yv);
                    w.Byte((byte)fx[i].T); w.Byte((byte)fx[i].S);
                }
        }

        public static void Load(BinReader r)
        {
            Init();
            int n = r.Int();
            for (int i = 0; i < n && i < MaxFx; ++i)
            {
                fx[i].X = r.Int(); fx[i].Y = r.Int(); fx[i].Xv = r.Int(); fx[i].Yv = r.Int();
                fx[i].T = r.Byte(); fx[i].S = r.Byte();
            }
        }

        public static void Alloc()
        {
            spr = new VgaImage[15];
            sprd = new byte[15];
            for (int i = 0; i < 10; ++i) spr[i] = Misc.GetSpr("TFOG", i, 0, sprd, i);
            for (int i = 10; i < 15; ++i) spr[i] = Misc.GetSpr("IFOG", i - 10, 0, sprd, i);

            bsnd = new Res.Locked[2];
            bsnd[0] = Misc.GetSnd("BUBL1");
            bsnd[1] = Misc.GetSnd("BUBL2");
            InitFx1Sin();
        }

        public static void Init()
        {
            for (int i = 0; i < MaxFx; ++i) fx[i].T = 0;
            bubsn = 0;
            last = 0;
        }

        public static void Act()
        {
            bubsn = 0;
            for (int i = 0; i < MaxFx; ++i)
            {
                switch (fx[i].T)
                {
                    case TFog:
                        if (++fx[i].S >= 20) fx[i].T = 0;
                        break;
                    case IFog:
                        if (++fx[i].S >= 10) fx[i].T = 0;
                        break;
                    case Bubl:
                        fx[i].Yv -= 5;
                        fx[i].Xv = Misc.Dec(fx[i].Xv, 20);
                        fx[i].X += fx[i].Xv;
                        fx[i].Y += fx[i].Yv;
                        int b = World.Fld[(fx[i].Y >> 11) * World.FLDW + (fx[i].X >> 11)];
                        if (b < 5 || b > 7) fx[i].T = 0;
                        break;
                }
            }
        }

        public static void Draw()
        {
            for (int i = 0; i < MaxFx; ++i)
            {
                int s = -1;
                switch (fx[i].T)
                {
                    case TFog: s = fx[i].S / 2; break;
                    case IFog: s = fx[i].S / 2 + 10; break;
                    case Bubl:
                        Vga.Dot((fx[i].X >> 8) - World.WX + World.WD / 2,
                                (fx[i].Y >> 8) - World.WY + World.HT / 2 + 1 + World.WO,
                                (byte)(0xC0 + fx[i].S));
                        continue;
                }
                if (s >= 0) Misc.DrawSpr(fx[i].X, fx[i].Y, spr[s], sprd[s] != 0);
            }
        }

        static int FindFree()
        {
            for (int i = 0; i < MaxFx; ++i) if (fx[i].T == 0) return i;
            for (int i = 0; i < MaxFx; ++i) if (fx[i].T == IFog) return i;
            if (++last >= MaxFx) last = 0;
            return last;
        }

        public static void Tfog(int x, int y)
        {
            int i = FindFree();
            fx[i].T = TFog; fx[i].S = 0;
            fx[i].X = x; fx[i].Y = y;
        }

        public static void Ifog(int x, int y)
        {
            int i = FindFree();
            fx[i].T = IFog; fx[i].S = 0;
            fx[i].X = x; fx[i].Y = y;
        }

        public static void Bubble(int x, int y, int xv, int yv, int n)
        {
            if (bubsn == 0) { Misc.Sound(bsnd[Glob.Rand(2)], 128); bubsn = 1; }
            for (; n > 0; --n)
            {
                int i = FindFree();
                fx[i].T = Bubl; fx[i].S = Glob.Rand(4);
                fx[i].X = (x << 8) + Glob.Rand(513) - 256;
                fx[i].Y = (y << 8) + Glob.Rand(513) - 256;
                fx[i].Xv = xv; fx[i].Yv = yv - Glob.Rand(256) - 768;
            }
        }
    }
}
