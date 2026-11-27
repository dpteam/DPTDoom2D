//
// DPT.DOOM 2D — prototype: the smoke tunnel.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Prototypes
{
    /// <summary>
    /// TRUBA — a smoke tunnel, ported from oldprog/watcom/PROG/TRUBA/MAIN.C.
    ///
    /// Two thousand particles drift up a tunnel and are drawn with the original
    /// smoke sprites. This is the demo that became Doom2D's smoke.c: the same
    /// idea — a particle ages, shrinks through a sprite sequence, and is drawn
    /// with a "smoke" sprite function that fades instead of blitting.
    ///
    /// The physics are the original's, including the 24.8 fixed point and the
    /// odd-looking "if the particle is above y=150, push it right" wind rule.
    /// </summary>
    internal sealed class TrubaDemo : ProtoHost.Prototype
    {
        private const int PNum = 2000;

        private struct Particle
        {
            public int X, Y, XV, YV, T;
        }

        private readonly Particle[] pt = new Particle[PNum];
        private int last;

        private VgaImage back;
        private VgaImage truba;
        private VgaImage[] smoke;

        public override string Id { get { return "truba"; } }
        public override string Title { get { return "TRUBA — smoke tunnel"; } }
        public override string Source { get { return "oldprog/watcom/PROG/TRUBA"; } }

        public override string Description
        {
            get { return "Дым в трубе: 2000 частиц, спрайты SMOKE.VGA. Отсюда вырос smoke.c в Doom2D."; }
        }

        public override void Init()
        {
            VgaContainer.Container art = ProtoHost.LoadAsset("TRUBA.VGA");
            VgaContainer.Container smokeArt = ProtoHost.LoadAsset("SMOKE.VGA");

            if (art != null)
            {
                VgaContainer.ApplyPalette(art);
                back = art.Get("back");
                truba = art.Get("truba");
            }

            smoke = new VgaImage[10];
            if (smokeArt != null)
            {
                for (int i = 0; i < 10; ++i)
                    smoke[i] = smokeArt.Get("SMOK" + (char)('A' + i) + "0");
            }

            for (int i = 0; i < PNum; ++i) pt[i].T = 0;
            last = 0;
        }

        /// <summary>dec() from the original: step towards zero by b.</summary>
        private static int Dec(int a, int b)
        {
            if (System.Math.Abs(a) <= b) return 0;
            return a < 0 ? a + b : a - b;
        }

        private void Move()
        {
            // Fifteen new particles a frame, exactly as the original spawned them.
            for (int i = 0; i < 15; ++i)
            {
                pt[last].X = Glob.Rand(30 * 256) + (160 - 15) * 256;
                pt[last].Y = Glob.Rand(20 * 256) + (200 - 20) * 256;
                pt[last].XV = Glob.Rand(200) - 100;
                pt[last].YV = Glob.Rand(800) - 1800;
                pt[last].T = Glob.Rand(10) + 50;

                if (++last >= PNum) last = 0;
            }

            for (int i = 0; i < PNum; ++i)
            {
                if (pt[i].T == 0) continue;

                if ((pt[i].Y >> 8) < 150) pt[i].XV += Glob.Rand(15) + 20;

                pt[i].XV = Dec(pt[i].XV, 3);
                pt[i].YV = Dec(pt[i].YV, 3);
                pt[i].X += pt[i].XV;
                pt[i].Y += pt[i].YV;

                --pt[i].T;
            }
        }

        public override void Frame(int frame)
        {
            Move();

            Vga.Clr(0, 320, 0, 200, 0);

            if (back != null) Vga.Pic(0, 0, back);

            for (int i = 0; i < PNum; ++i)
            {
                if (pt[i].T == 0) continue;

                int s = pt[i].T / 4;
                if (s > 7) s = 7;
                s = 9 - s;

                VgaImage image = smoke != null && s < smoke.Length ? smoke[s] : null;
                if (image == null) continue;

                Vga.SprF(pt[i].X >> 8, pt[i].Y >> 8, image, Vga.SmokeSprFunc);
            }

            if (truba != null) Vga.Spr(0, 0, truba);

            ProtoHost.Text(4, 4, "TRUBA", 255);
            ProtoHost.Text(4, 190, "ESC - EXIT   F11 - FULLSCREEN", 255);
        }
    }
}
