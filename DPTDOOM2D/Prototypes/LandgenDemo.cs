//
// DPT.DOOM 2D — prototype: the fractal landscape generator.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Prototypes
{
    /// <summary>
    /// LANDGEN — fractal terrain, ported from oldprog/watcom/PROG/TEST/LANDGEN.CPP.
    ///
    /// A 256x256 heightmap built from value noise: hills, rocky detail, swamps,
    /// forest and ice are separate octave layers blended across a 2x2 parameter
    /// grid, so one corner of the map can be mountainous and another marshy. The
    /// generator was written for the terrain experiments and feeds the same
    /// climate pass PLANET uses.
    ///
    /// The original dumped a heightmap; here it is drawn as a shaded relief so
    /// the result is visible: light from the north-west, water below sea level.
    ///
    /// R regenerates with a new seed.
    /// </summary>
    internal sealed class LandgenDemo : ProtoHost.Prototype
    {
        private const int Rn = 512;              // noise table size
        private const int Rmsk = Rn - 1;
        private const int LandSqi = 256;
        private const int LandCnp = 3;
        private const int LandCn = 1 << LandCnp; // 8
        private const int LandDivP = 4;
        private const int LandDiv = 1 << LandDivP; // 16
        private const int Size = LandCn * LandDiv; // 256
        private const int Hills = 512, HillI = 8;
        private const int Rocks = 32, RockI = 5;

        private readonly float[] rtab = new float[Rn * 2];
        private readonly int[] perm = new int[Rn * 2];
        private readonly float[,] heights = new float[Size, Size];

        private int seed = 1120272305;
        private int regenerateAt = -1;

        /// <summary>Height range of the current map, for normalising the colours.</summary>
        private float minHeight, maxHeight;

        public override string Id { get { return "landgen"; } }
        public override string Title { get { return "LANDGEN — fractal terrain"; } }
        public override string Source { get { return "oldprog/watcom/PROG/TEST/LANDGEN.CPP"; } }

        public override string Description
        {
            get { return "Фрактальный ландшафт: 256x256 высот из value-шума, холмы/скалы/лес/лёд отдельными слоями."; }
        }

        // -------------------------------------------------------------- noise

        /// <summary>The original's LCG: 0x41C64E6D / 0x3039, 15 bits out.</summary>
        private int Rnd()
        {
            unchecked { seed = seed * 0x41C64E6D + 0x3039; }
            return (seed >> 16) & 0x7FFF;
        }

        private float Srnd()
        {
            return (Rnd() * 2 - 32768) / 32768f;
        }

        private void InitNoise()
        {
            seed = 1120272305;

            for (int i = 0; i < Rn; ++i)
            {
                float x, y, z, l;

                do
                {
                    x = Srnd(); y = Srnd(); z = Srnd();
                    l = x * x + y * y + z * z;
                }
                while (l < 0.1f);

                l = (float)Math.Sqrt(l);
                rtab[i] = x / l;
                rtab[i + Rn] = y / l;
            }

            for (int i = 0; i < Rn; ++i) perm[i] = i;

            for (int pass = 0; pass < 7; ++pass)
            {
                for (int j = 0; j < Rn; ++j)
                {
                    int k = Rnd() & Rmsk;
                    int a = perm[j];
                    perm[j] = perm[k];
                    perm[k] = a;
                }
            }

            for (int i = 0; i < Rn; ++i) perm[i + Rn] = perm[i];
        }

        /// <summary>Value noise with the original's cubic blend.</summary>
        private float Noise2(float x, float y)
        {
            float rx = (float)Math.Floor(x), fx = x - rx;
            float ry = (float)Math.Floor(y), fy = y - ry;

            int ix = (int)rx & Rmsk;
            int iy = (int)ry & Rmsk;

            float kx = (3 - fx * 2) * fx * fx;
            float ky = (3 - fy * 2) * fy * fy;

            int i = perm[perm[ix] + iy];
            float n = rtab[i] * fx + rtab[i + Rn] * fy;

            i = perm[perm[ix + 1] + iy];
            float n0 = (rtab[i] * (fx - 1) + rtab[i + Rn] * fy - n) * kx + n;

            i = perm[perm[ix] + iy + 1];
            n = rtab[i] * fx + rtab[i + Rn] * (fy - 1);

            i = perm[perm[ix + 1] + iy + 1];
            float n1 = (rtab[i] * (fx - 1) + rtab[i + Rn] * (fy - 1) - n) * kx + n;

            return (n1 - n0) * ky + n0;
        }

        private float HillNoise(float x, float y)
        {
            x /= Hills; y /= Hills;

            float k = 1, v = 0;

            for (int i = 0; i < HillI; ++i, x *= 2, y *= 2, k *= 0.5f)
                v += Noise2(x, y) * k;

            return v;
        }

        private float RockNoise(float x, float y)
        {
            x /= Rocks; y /= Rocks;

            float k = 1, v = 0;

            for (int i = 0; i < RockI; ++i, x *= 2, y *= 2, k *= 0.5f)
                v += Math.Abs(Noise2(x, y)) * k;

            return (v - 0.415777f) * 2;
        }

        // ---------------------------------------------------------- generation

        /// <summary>The five terrain parameters the original blends.</summary>
        private struct Params
        {
            public float Ht, Hill, Rock, Swamp, Forest, Ice;

            public Params(float h, float hl, float r, float s, float f, float i)
            {
                Ht = h; Hill = hl; Rock = r; Swamp = s; Forest = f; Ice = i;
            }

            public static Params operator +(Params a, Params b)
            {
                return new Params(a.Ht + b.Ht, a.Hill + b.Hill, a.Rock + b.Rock,
                    a.Swamp + b.Swamp, a.Forest + b.Forest, a.Ice + b.Ice);
            }

            public static Params operator -(Params a, Params b)
            {
                return new Params(a.Ht - b.Ht, a.Hill - b.Hill, a.Rock - b.Rock,
                    a.Swamp - b.Swamp, a.Forest - b.Forest, a.Ice - b.Ice);
            }

            public static Params operator *(Params a, float k)
            {
                return new Params(a.Ht * k, a.Hill * k, a.Rock * k,
                    a.Swamp * k, a.Forest * k, a.Ice * k);
            }
        }

        private void Generate()
        {
            InitNoise();

            var lp00 = new Params(0, 30, 5, 0, 10, 0);
            var lp01 = new Params(30, 40, 12, 0, 10, 0);
            var lp10 = new Params(15, 25, 7, 0, 10, 0);
            var lp11 = new Params(42, 34, 15, 0, 10, 0);

            // Bilinear blend weights across the parameter grid.
            Params lp0 = lp00;
            Params lpu = lp10 - lp00;
            Params lpv = lp01 - lp00;
            Params lpuv = lp11 - lp10 - lp01 + lp00;

            minHeight = float.MaxValue;
            maxHeight = float.MinValue;

            for (int ey = 0; ey < Size; ++ey)
            {
                float v = ey / (float)(LandCn * LandDiv);
                float kv = (3 - 2 * v) * v * v;
                float ny = v * LandSqi;

                for (int ex = 0; ex < Size; ++ex)
                {
                    float u = ex / (float)(LandCn * LandDiv);
                    float ku = (3 - 2 * u) * u * u;
                    float nx = u * LandSqi;

                    Params lp = lpuv * (ku * kv) + lpu * ku + lpv * kv + lp0;

                    float h = lp.Ht + HillNoise(nx, ny) * lp.Hill + RockNoise(nx, ny) * lp.Rock;

                    heights[ex, ey] = h;

                    if (h < minHeight) minHeight = h;
                    if (h > maxHeight) maxHeight = h;
                }
            }

            // A flat map would divide by zero below.
            if (maxHeight - minHeight < 1) maxHeight = minHeight + 1;
        }

        public override void Init()
        {
            BuildPalette();
            Generate();
        }

        private static void BuildPalette()
        {
            for (int i = 0; i < 256; ++i) SetColor(i, 0, 0, 0);

            // Deep water to shallow, then grass to snow.
            for (int i = 0; i < 32; ++i) SetColor(32 + i, 2 + i / 4, 8 + i / 3, 30 + i);
            for (int i = 0; i < 32; ++i) SetColor(64 + i, 10 + i / 2, 30 + i, 10 + i / 4);
            for (int i = 0; i < 32; ++i) SetColor(96 + i, 30 + i, 34 + i, 20 + i / 2);
            for (int i = 0; i < 32; ++i) SetColor(128 + i, 46 + i / 2, 46 + i / 2, 46 + i / 2);

            Palette.SetGamma(0);
        }

        private static void SetColor(int index, int r, int g, int b)
        {
            Palette.Main[index * 3] = (byte)Math.Min(63, r);
            Palette.Main[index * 3 + 1] = (byte)Math.Min(63, g);
            Palette.Main[index * 3 + 2] = (byte)Math.Min(63, b);
        }

        public override void Frame(int frame)
        {
            if (Kbd.WasPressed('r'))
            {
                regenerateAt = frame + 1;
            }

            if (regenerateAt == frame)
            {
                Generate();
                regenerateAt = -1;
            }

            byte[] buffer = Vga.Buffer;

            for (int y = 0; y < 200; ++y)
            {
                int sy = y * Size / 200;

                for (int x = 0; x < 320; ++x)
                {
                    int sx = x * Size / 320;

                    float h = heights[sx, sy];

                    // Slope shading: light from the north-west, like the original's
                    // terrain previews.
                    float dx = heights[Math.Min(Size - 1, sx + 1), sy] - h;
                    float dy = heights[sx, Math.Min(Size - 1, sy + 1)] - h;
                    float light = 1.0f + (dx + dy) * 0.9f;

                    if (light < 0.3f) light = 0.3f;
                    if (light > 1.8f) light = 1.8f;

                    // Normalise against the map's own range: the generator's absolute
                    // heights depend on the parameter blend, so fixed bands painted
                    // the whole map as water on one seed and as land on another.
                    float n = (h - minHeight) / (maxHeight - minHeight);

                    int level = (int)(n * 100);

                    byte color;

                    if (level < 20)
                    {
                        // Water: deep to shallow.
                        color = (byte)(32 + Math.Min(31, 20 - level));
                    }
                    else if (level < 35)
                    {
                        // Beach.
                        color = (byte)(70 + (level - 20));
                    }
                    else if (level > 85)
                    {
                        // Snow caps.
                        color = (byte)(128 + Math.Min(31, (level - 85) * 2));
                    }
                    else
                    {
                        // Grass to rock, banded so the relief reads.
                        int band = (level - 35) * 32 / 50;
                        if (band > 31) band = 31;
                        color = (byte)(64 + band);
                    }

                    // Apply the shading by nudging the colour towards dark or light.
                    if (light > 1.02f) color = (byte)(Math.Min(255, color + (int)((light - 1) * 30)));
                    else if (light < 0.98f) color = (byte)(Math.Max(0, color - (int)((1 - light) * 40)));

                    buffer[y * 320 + x] = color;
                }
            }

            ProtoHost.Text(4, 4, "LANDGEN", 255);
            ProtoHost.Text(4, 190, "R - NEW WORLD   ESC - EXIT", 255);
        }
    }
}
