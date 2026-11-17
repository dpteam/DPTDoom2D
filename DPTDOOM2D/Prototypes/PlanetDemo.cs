//
// DPT.DOOM 2D — prototype: the planet generator.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Prototypes
{
    /// <summary>
    /// PLANET — a procedural world with climate, ported from
    /// oldprog/watcom/PROG/PLANET/PLANET.C.
    ///
    /// The map is 256x256 and wraps in both directions. Continents are grown from
    /// random seeds, refined by midpoint interpolation, then handed to a coarse
    /// 16x16 atmosphere grid that carries temperature and moisture: the wind
    /// blows from cold to warm, rain falls where moist air rises over land, and
    /// the terrain type follows from height, moisture and latitude.
    ///
    /// This is the world generator behind the terrain experiments that later
    /// became Doom2D's sky and level ideas — the same climate pass that
    /// LANDGEN.CPP feeds into its heightmap.
    ///
    /// Arrows scroll, Space switches between the terrain and the rainfall map.
    /// </summary>
    internal sealed class PlanetDemo : ProtoHost.Prototype
    {
        private const int MSZ = 256;          // map size
        private const int MMSK = MSZ - 1;
        private const int SQ = MSZ / 16;      // atmosphere cell, 16 pixels
        private const int ASZ = MSZ / SQ;     // 16x16 atmosphere grid
        private const int AMSK = ASZ - 1;
        private const int SeaLevel = 29;
        private const int MaxCv = SQ * SQ * 5;
        private const int VlK = 2;

        // Terrain types, in the original's enum order.
        private const int Land = 0, Sea = 1, River = 2, Snow = 3, Ice = 4, Grass = 5,
                          Forest = 6, Swamp = 7, SubSwamp = 8, Desert = 9, Rock = 10;

        private struct Cell
        {
            public int A;      // height
            public int O;      // moisture
        }

        private struct Atmo
        {
            public int T, V, Lt, Lv, C, O, K;
            public int X, Y, Tx, Ty;
        }

        private readonly Cell[,] mn = new Cell[MSZ, MSZ];
        private readonly Atmo[,] at = new Atmo[ASZ, ASZ];

        private int contNum = 5, contMin = 8, contMax = 15;
        private int dayPos;

        private bool showRain;
        private int scrollX, scrollY;

        public override string Id { get { return "planet"; } }
        public override string Title { get { return "PLANET — world generator"; } }
        public override string Source { get { return "oldprog/watcom/PROG/PLANET"; } }

        public override string Description
        {
            get { return "Генератор планеты: континенты, климат, ветер, осадки. 256x256, замыкается по краям."; }
        }

        public override void Init()
        {
            BuildPalette();
            Glob.Reset(1996);
            Make();
        }

        /// <summary>
        /// The DOS program used palette entries 64..72 of the default VGA palette;
        /// those colours are not guaranteed anywhere else, so they are defined
        /// here with the same meaning: deep water, grass, ice, snow, forest,
        /// marsh, desert, swamp, rock.
        /// </summary>
        private static void BuildPalette()
        {
            SetColor(0, 0, 0, 0);
            SetColor(45, 24, 44, 16);    // bare land
            SetColor(64, 4, 10, 44);     // sea
            SetColor(65, 16, 44, 16);    // grass
            SetColor(66, 52, 58, 60);    // ice
            SetColor(67, 60, 62, 63);    // snow
            SetColor(68, 6, 30, 10);     // forest
            SetColor(69, 20, 40, 30);    // sub-swamp
            SetColor(70, 56, 46, 20);    // desert
            SetColor(71, 14, 26, 20);    // swamp
            SetColor(72, 34, 34, 34);    // rock

            for (int i = 0; i < 16; ++i)
                SetColor(10 + i, 8 + i * 3, 12 + i * 3, 30 + i * 2);

            Palette.SetGamma(0);
        }

        private static void SetColor(int index, int r, int g, int b)
        {
            Palette.Main[index * 3] = (byte)r;
            Palette.Main[index * 3 + 1] = (byte)g;
            Palette.Main[index * 3 + 2] = (byte)b;
        }

        // ------------------------------------------------------------- generation

        private static int Adj(int a, int s)
        {
            a += Glob.Rand(s * 2 + 1) - s;
            if (a < 0) return -a;
            if (a > 63) return 127 - a;
            return a;
        }

        private void SpawnCont(int x, int y, int p)
        {
            if (p <= 0) return;
            if (mn[x, y].A >= 32) return;

            mn[x, y].A = (Glob.Rand(32)) + 32;

            SpawnCont((x - SQ) & MMSK, y, p - (Glob.Rand(8)) - 1);
            SpawnCont((x + SQ) & MMSK, y, p - (Glob.Rand(8)) - 1);
            SpawnCont(x, (y - SQ) & MMSK, p - (Glob.Rand(8)) - 1);
            SpawnCont(x, (y + SQ) & MMSK, p - (Glob.Rand(8)) - 1);
        }

        private void Make()
        {
            int x, y, s;

            for (x = 0; x < MSZ; x += SQ)
                for (y = 0; y < MSZ; y += SQ)
                    mn[x, y].A = Glob.Rand(32);

            for (s = 0; s < contNum; ++s)
            {
                do
                {
                    x = (Glob.Rand(MMSK) & MMSK) / SQ * SQ;
                    y = (Glob.Rand(MMSK) & MMSK) / SQ * SQ;
                }
                while (mn[x, y].A >= 32);

                SpawnCont(x, y, Glob.Rand(contMax - contMin + 1) + contMin);
            }

            // Midpoint refinement: each pass halves the cell and averages the four
            // corners with the weights the original used.
            for (s = SQ; s > 1; )
            {
                if (s == 2)
                {
                    for (x = 0; x < MSZ; x += s)
                        for (y = 0; y < MSZ; y += s)
                            if (mn[x, y].A >= 37 && mn[x, y].A < 55)
                                mn[x, y].A = (mn[x, y].A - 37) / 2 + 37;
                }

                for (x = 0; x < MSZ; x += s)
                {
                    for (y = 0; y < MSZ; y += s)
                    {
                        int x1 = (x + s) & MMSK;
                        int y1 = (y + s) & MMSK;

                        mn[x, y].O = Adj((mn[x, y].A * 9 + mn[x1, y].A * 3 + mn[x, y1].A * 3 + mn[x1, y1].A) / 16, s);
                        mn[(x + s / 2) & MMSK, y].O = Adj((mn[x1, y].A * 9 + mn[x, y].A * 3 + mn[x1, y1].A * 3 + mn[x, y1].A) / 16, s);
                        mn[x, (y + s / 2) & MMSK].O = Adj((mn[x, y1].A * 9 + mn[x, y].A * 3 + mn[x1, y1].A * 3 + mn[x1, y].A) / 16, s);
                        mn[(x + s / 2) & MMSK, (y + s / 2) & MMSK].O = Adj((mn[x1, y1].A * 9 + mn[x1, y].A * 3 + mn[x, y1].A * 3 + mn[x, y].A) / 16, s);
                    }
                }

                s /= 2;

                for (x = 0; x < MSZ; x += s)
                    for (y = 0; y < MSZ; y += s)
                        mn[x, y].A = mn[x, y].O;
            }

            // One smoothing pass: every cell becomes the average of its neighbours.
            for (x = 0; x < MSZ; ++x)
                for (y = 0; y < MSZ; ++y)
                    mn[x, y].O = mn[x, y].A;

            for (x = 0; x < MSZ; ++x)
            {
                for (y = 0; y < MSZ; ++y)
                {
                    mn[x, y].A = (mn[(x - 1) & MMSK, y].O + mn[x, (y - 1) & MMSK].O +
                                  mn[(x + 1) & MMSK, y].O + mn[x, (y + 1) & MMSK].O +
                                  mn[(x - 1) & MMSK, (y - 1) & MMSK].O + mn[(x + 1) & MMSK, (y - 1) & MMSK].O +
                                  mn[(x - 1) & MMSK, (y + 1) & MMSK].O + mn[(x + 1) & MMSK, (y + 1) & MMSK].O) / 8;
                }
            }

            // Atmosphere: temperature and moisture summed per cell.
            for (x = 0; x < ASZ; ++x)
                for (y = 0; y < ASZ; ++y)
                    at[x, y] = new Atmo();

            for (x = 0; x < MSZ; ++x)
            {
                for (y = 0; y < MSZ; ++y)
                {
                    at[x / SQ, y / SQ].Lv += mn[x, y].A <= SeaLevel ? 6 : 1;

                    s = 34 - mn[x, y].A + 15;
                    if (mn[x, y].A < 34) s = 15 - s / 4;

                    at[x / SQ, y / SQ].Lt += s;
                    at[x / SQ, y / SQ].K += Math.Max(mn[x, y].A, 34);
                }
            }

            for (x = 0; x < ASZ; ++x)
            {
                for (y = 0; y < ASZ; ++y)
                {
                    at[x, y].T = at[x, y].Lt;
                    at[x, y].V = at[x, y].Lv;
                    at[x, y].K = (63 - at[x, y].K / (SQ * SQ)) / 8;
                }
            }

            CalcWind();

            for (s = 0; s < 200; ++s) MoveAtmo();

            // Rainfall: accumulate what the atmosphere drops, then smooth it.
            var p = new int[MSZ, MSZ];
            var q = new int[MSZ, MSZ];

            for (s = 0; s < 100; ++s)
            {
                MoveAtmo();

                for (x = 0; x < MSZ; x += SQ)
                {
                    for (y = 0; y < MSZ; y += SQ)
                    {
                        int x1 = p[x, y] + at[x / SQ, y / SQ].O / (SQ * SQ);
                        if (x1 > 127) x1 = 127;
                        p[x, y] = x1;
                    }
                }
            }

            for (s = SQ; s > 1; )
            {
                for (x = 0; x < MSZ; x += s)
                {
                    for (y = 0; y < MSZ; y += s)
                    {
                        int x1 = (x + s) & MMSK;
                        int y1 = (y + s) & MMSK;

                        q[x, y] = Adj((p[x, y] * 9 + p[x1, y] * 3 + p[x, y1] * 3 + p[x1, y1]) / 16, s);
                        q[(x + s / 2) & MMSK, y] = Adj((p[x1, y] * 9 + p[x, y] * 3 + p[x1, y1] * 3 + p[x, y1]) / 16, s);
                        q[x, (y + s / 2) & MMSK] = Adj((p[x, y1] * 9 + p[x, y] * 3 + p[x1, y1] * 3 + p[x1, y]) / 16, s);
                        q[(x + s / 2) & MMSK, (y + s / 2) & MMSK] = Adj((p[x1, y1] * 9 + p[x1, y] * 3 + p[x, y1] * 3 + p[x, y]) / 16, s);
                    }
                }

                s /= 2;

                for (x = 0; x < MSZ; ++x)
                    for (y = 0; y < MSZ; ++y)
                        p[x, y] = q[x, y];
            }

            for (x = 0; x < MSZ; ++x)
                for (y = 0; y < MSZ; ++y)
                    mn[x, y].O = p[x, y];
        }

        private void CalcWind()
        {
            var b = new bool[3, 3];

            for (int x = 0; x < ASZ; ++x)
            {
                for (int y = 0; y < ASZ; ++y)
                {
                    int bt = at[x, y].T + at[x, y].V * VlK;
                    int bn = 0;
                    Array.Clear(b, 0, b.Length);

                    for (int sx = -1; sx <= 1; ++sx)
                    {
                        for (int sy = -1; sy <= 1; ++sy)
                        {
                            if (sx == 0 && sy == 0) continue;

                            int dt = at[(x + sx) & AMSK, (y + sy) & AMSK].T
                                + at[(x + sx) & AMSK, (y + sy) & AMSK].V * VlK
                                + (Glob.Rand(4));

                            if (dt > bt) continue;
                            if (dt != bt) { bt = dt; bn = 0; Array.Clear(b, 0, b.Length); }

                            b[sx + 1, sy + 1] = true;
                            ++bn;
                        }
                    }

                    int delta = ((at[x, y].T + at[x, y].V * VlK - bt) * at[x, y].K) >> 3;

                    if (bn == 0 || delta == 0)
                    {
                        at[x, y].X = 0;
                        at[x, y].Y = 0;
                        continue;
                    }

                    int pick = Glob.Rand(bn) + 1;
                    int step = (delta + 4) / 5;

                    for (int sx = 0; sx < 3; ++sx)
                    {
                        for (int sy = 0; sy < 3; ++sy)
                        {
                            if (!b[sx, sy]) continue;
                            if (--pick != 0) continue;

                            at[x, y].X = (sx - 1) * step;
                            at[x, y].Y = (sy - 1) * step;
                            sx = 3;
                            break;
                        }
                    }
                }
            }
        }

        private void MoveAtmo()
        {
            dayPos = (dayPos + 1) & AMSK;

            for (int x = 0; x < ASZ; ++x)
            {
                for (int y = 0; y < ASZ; ++y)
                {
                    int m = Math.Max(Math.Abs(at[x, y].X), Math.Abs(at[x, y].Y));
                    if (m == 0) continue;

                    int sx = (x + at[x, y].X / m) & AMSK;
                    int sy = (y + at[x, y].Y / m) & AMSK;

                    if (m >= 10) m = 8; else m = (m * 4 + 4) / 5;

                    at[sx, sy].T = (at[sx, sy].T * (16 - m) + at[x, y].T * m) >> 4;
                    at[sx, sy].V = (at[sx, sy].V * (16 - m) + at[x, y].V * m) >> 4;
                    at[sx, sy].C = (at[sx, sy].C * (16 - m) + at[x, y].C * m) >> 4;

                    m = Math.Abs(x - dayPos);
                    if (m > ASZ / 2) m = ASZ - m;
                    m = ASZ / 2 - m;

                    at[x, y].T = (at[x, y].T * 7 + at[x, y].Lt + 15 * m / (ASZ / 2) * SQ * SQ) >> 3;
                    at[x, y].V = (at[x, y].V * 7 + at[x, y].Lv) >> 3;

                    m = SQ * SQ * 3;

                    if (at[x, y].V > m)
                    {
                        at[x, y].C += at[x, y].V - m;
                        at[x, y].V = m;
                    }

                    if (at[x, y].C > MaxCv)
                    {
                        if (at[x, y].Lv > SQ * SQ * 3 / 2 && (Glob.Rand(8)) != 0) continue;

                        m = (at[x, y].C - MaxCv) * (Glob.Rand(8)) / 8;
                        at[x, y].C -= m / 2;
                        at[x, y].O = m;
                    }
                }
            }

            CalcWind();
        }

        /// <summary>Terrain type from height, moisture and latitude (gettype()).</summary>
        private static int TypeOf(int a, int o)
        {
            if (a > SeaLevel)
            {
                if (a >= 53) return a > 56 ? Ice : Snow;
                if (a >= 44) return (o > 30 && o < 60) ? Forest : Rock;
                if (o < 10) return Desert;
                if (o < 20) return Land;
                if (o < 40) return Forest;
                if (o < 50) return Grass;
                if (o < 55) return SubSwamp;
                return Swamp;
            }

            return Sea;
        }

        private static byte ColorOf(int type)
        {
            switch (type)
            {
                case Sea: return 64;
                case River: return 64;
                case Land: return 45;
                case Ice: return 66;
                case Snow: return 67;
                case Grass: return 65;
                case Forest: return 68;
                case SubSwamp: return 69;
                case Desert: return 70;
                case Swamp: return 71;
                case Rock: return 72;
                default: return 0;
            }
        }

        // ---------------------------------------------------------------- frame

        public override void Frame(int frame)
        {
            if (Kbd.Down(Kbd.SDLK_LEFT)) scrollX -= 2;
            if (Kbd.Down(Kbd.SDLK_RIGHT)) scrollX += 2;
            if (Kbd.Down(Kbd.SDLK_UP)) scrollY -= 2;
            if (Kbd.Down(Kbd.SDLK_DOWN)) scrollY += 2;
            if (Kbd.WasPressed(Kbd.SDLK_SPACE)) showRain = !showRain;

            byte[] buffer = Vga.Buffer;

            for (int y = 0; y < 200; ++y)
            {
                int v = ((y * 2) + scrollY) & MMSK;

                for (int x = 0; x < 320; ++x)
                {
                    int u = ((x * 2) + scrollX) & MMSK;

                    byte color = showRain
                        ? (byte)(10 + Math.Min(15, mn[u, v].O * 16 / 128))
                        : ColorOf(TypeOf(mn[u, v].A, mn[u, v].O));

                    buffer[y * 320 + x] = color;
                }
            }

            ProtoHost.Text(4, 4, "PLANET", 255);
            ProtoHost.Text(4, 190, showRain ? "SPACE - TERRAIN" : "SPACE - RAIN", 255);
            ProtoHost.Text(240, 190, "ARROWS - SCROLL", 255);
        }
    }
}
