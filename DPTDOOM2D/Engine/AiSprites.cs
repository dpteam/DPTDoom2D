//
// DPT.DOOM 2D — AIDooM2D: the sprites, the HUD digits and the menu graphics.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Draws every picture AIDooM2D needs that is not a wall.
    ///
    /// The shapes are built from a handful of primitives — an ellipse, a box, a
    /// capsule — placed in normalised coordinates, so one description works at 8x8 and
    /// at 52x37. That matters because the sizes are not ours: the game asks for a
    /// sprite of exactly the size the reference set has, and a monster drawn for the
    /// wrong footprint stands in the wrong place.
    ///
    /// Colour is chosen from the set's name where the name is meaningful — a red key
    /// must be red — and from a hash of it otherwise, so twenty monsters come out in
    /// several different colours instead of twenty shades of brown.
    /// </summary>
    internal static class AiSprites
    {
        // ---- kinds -----------------------------------------------------------

        private enum Shape
        {
            Humanoid,
            Blob,
            Item,
            Weapon,
            Effect,
            Projectile
        }

        private static Shape ShapeOf(string set)
        {
            string s = (set ?? "").ToUpperInvariant();

            // Weapons the player carries: drawn as a gun seen from the side.
            if (s.StartsWith("PWP", StringComparison.Ordinal)) return Shape.Weapon;

            // Muzzle flashes, smoke, fire, fog: round and soft.
            if (s == "BEXP" || s == "FIRE" || s == "SMOK" || s == "TFOG" ||
                s == "IFOG" || s == "FLAM" || s == "PUFF") return Shape.Effect;

            // Things that fly across the screen.
            if (s == "MISL" || s == "PLSS" || s == "PLSE" || s == "APLS" ||
                s == "APBX" || s == "BFS1" || s == "BFE1" || s == "BFE2" ||
                s == "BAL1" || s == "BAL2" || s == "BAL7" || s == "ROCK" ||
                s == "SHRD") return Shape.Projectile;

            // Monsters. The list is the actor registry's, so a new actor added there
            // still gets a body rather than a box.
            if (s == "TROO" || s == "POSS" || s == "SPOS" || s == "SARG" ||
                s == "BOSS" || s == "BOS2" || s == "HEAD" || s == "SKUL" ||
                s == "SKEL" || s == "CYBR" || s == "SPID" || s == "PAIN" ||
                s == "SSWV" || s == "CPOS" || s == "PLAY" || s == "MANF" ||
                s == "FISH" || s == "ROBO" || s == "BSPI" || s == "VILE" ||
                s == "FATT" || s == "FATB") return Shape.Humanoid;

            // Small creatures and debris.
            if (s == "GOR1" || s == "GOR2" || s == "GOR3" || s == "GOR4" ||
                s == "GOR5" || s == "FCAN" || s == "SMOK") return Shape.Blob;

            return Shape.Item;
        }

        /// <summary>
        /// The palette block a set draws from.
        ///
        /// Semantic names win: a red key that is not red is a bug a player notices
        /// immediately. Everything else is hashed into one of the ramps.
        /// </summary>
        private static int BaseOf(string set, uint seed)
        {
            string s = (set ?? "").ToUpperInvariant();

            switch (s)
            {
                case "KEYR": return 176;
                case "KEYG": return 112;
                case "KEYB": return 192;
                case "SUIT": return 112;
                case "AQUA": return 192;
                case "MEDI":
                case "STIM":
                case "BPAK": return 80;
                case "SOUL": return 240;
                case "MEGA": return 240;
                case "PINV": return 192;
                case "INVU": return 192;
                case "CSAW":
                case "MGUN":
                case "LAUN":
                case "PLAS":
                case "BFUG":
                case "SHOT":
                case "SGN2": return 80;
                case "CLIP":
                case "SHEL":
                case "CELL":
                case "AMMO":
                case "SBOX":
                case "BROK":
                case "CELP": return 64;
                case "PLAY": return 112;
                case "FIRE":
                case "FLAM": return 208;
                case "BEXP": return 160;
                case "TFOG":
                case "IFOG":
                case "SMOK": return 80;
            }

            // Everything else: one of the monster ramps, by hash.
            int[] blocks = { 16, 64, 112, 128, 144, 192, 208, 240 };
            return blocks[AiArt.Hash(s, seed) % (uint)blocks.Length];
        }

        // ---- primitives ------------------------------------------------------

        private static bool Ellipse(double u, double v, double cu, double cv, double ru, double rv)
        {
            if (ru <= 0 || rv <= 0) return false;

            double du = (u - cu) / ru;
            double dv = (v - cv) / rv;

            return du * du + dv * dv <= 1.0;
        }

        private static bool Box(double u, double v, double x0, double y0, double x1, double y1)
        {
            return u >= x0 && u <= x1 && v >= y0 && v <= y1;
        }

        // ---- sprites ---------------------------------------------------------

        /// <summary>
        /// One sprite frame.
        ///
        /// <paramref name="letter"/> advances the shape slightly so a walk cycle does
        /// not look frozen: the legs swap and the body bobs. It is not animation, but a
        /// monster that is a different picture on every frame reads as moving.
        /// </summary>
        public static byte[] Sprite(string set, int letter, int w, int h, byte[] pal, uint seed)
        {
            if (w <= 0 || h <= 0) return AiArt.Pack(1, 1, 0, 0, new byte[1]);

            Shape shape = ShapeOf(set);
            int baseIndex = BaseOf(set, seed);

            AiArt.Rand rnd = new AiArt.Rand(AiArt.Hash(set + ":" + letter, seed));

            var pixels = new byte[w * h];

            // The walk cycle: 0 or 1, alternating every frame letter.
            int phase = letter & 1;

            for (int y = 0; y < h; ++y)
            {
                // v is 0 at the top and 1 at the bottom, so shapes are described the
                // way a picture is looked at.
                double v = (y + 0.5) / h;

                for (int x = 0; x < w; ++x)
                {
                    double u = (x + 0.5) / w;

                    int shade = Body(shape, u, v, phase, rnd);

                    if (shade < 0) continue;

                    if (shade > 15) shade = 15;

                    pixels[y * w + x] = (byte)(baseIndex + shade);
                }
            }

            // A one-pixel outline: without it a body of one flat colour disappears
            // against a wall of the same hue.
            Outline(pixels, w, h, (byte)Math.Max(0, baseIndex - 2));

            // A hot spot at the feet, which is where the engine anchors a sprite.
            return AiArt.Pack(w, h, w / 2, h - 1, pixels);
        }

        /// <summary>Returns the shade of a pixel, or -1 for "not part of the shape".</summary>
        private static int Body(Shape shape, double u, double v, int phase, AiArt.Rand rnd)
        {
            switch (shape)
            {
                case Shape.Humanoid:
                    {
                        // Legs: two capsules that swap with the walk phase.
                        double legOffset = phase == 0 ? 0.04 : -0.04;

                        if (v > 0.62)
                        {
                            if (Ellipse(u, v, 0.40 + legOffset, 0.80, 0.10, 0.18)) return 4;
                            if (Ellipse(u, v, 0.60 - legOffset, 0.80, 0.10, 0.18)) return 3;
                            return -1;
                        }

                        // Torso.
                        if (Ellipse(u, v, 0.50, 0.46, 0.20, 0.22)) return 5;

                        // Arms.
                        if (Ellipse(u, v, 0.28, 0.46, 0.07, 0.16)) return 3;
                        if (Ellipse(u, v, 0.72, 0.46, 0.07, 0.16)) return 3;

                        // Head.
                        if (Ellipse(u, v, 0.50, 0.20, 0.14, 0.14)) return 6;

                        // Eyes: two dark pixels, which is what makes it read as a face.
                        if (Ellipse(u, v, 0.45, 0.18, 0.03, 0.03)) return 1;
                        if (Ellipse(u, v, 0.55, 0.18, 0.03, 0.03)) return 1;

                        return -1;
                    }

                case Shape.Blob:
                    {
                        double wobble = phase == 0 ? 0.02 : -0.02;

                        if (Ellipse(u, v, 0.5, 0.55 + wobble, 0.34, 0.30)) return 4 + rnd.Below(2);
                        return -1;
                    }

                case Shape.Item:
                    {
                        // A box with a lighter lid: readable at eight pixels.
                        if (Box(u, v, 0.20, 0.30, 0.80, 0.85)) return 4;
                        if (Box(u, v, 0.20, 0.30, 0.80, 0.42)) return 8;
                        if (Box(u, v, 0.42, 0.45, 0.58, 0.70)) return 2;
                        return -1;
                    }

                case Shape.Weapon:
                    {
                        // A gun seen from behind: barrel up and to the left.
                        if (Box(u, v, 0.30, 0.30, 0.62, 0.90)) return 3;
                        if (Box(u, v, 0.30, 0.30, 0.85, 0.45)) return 5;
                        if (Box(u, v, 0.72, 0.28, 0.85, 0.40)) return 8;
                        return -1;
                    }

                case Shape.Effect:
                    {
                        // A puff: a bright core fading to a dark rim.
                        double d = Math.Sqrt((u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5));

                        if (d > 0.46) return -1;
                        if (d < 0.14) return 10;
                        if (d < 0.26) return 8;
                        if (d < 0.36) return 5;

                        return 2;
                    }

                default: // Projectile
                    {
                        if (Ellipse(u, v, 0.5, 0.5, 0.30, 0.30)) return 8;
                        if (Ellipse(u, v, 0.5, 0.5, 0.18, 0.18)) return 10;
                        return -1;
                    }
            }
        }

        /// <summary>
        /// Draws a darker border around every opaque pixel.
        ///
        /// Done as a second pass rather than per shape because every shape wants it and
        /// none of them wants to know about it.
        /// </summary>
        private static void Outline(byte[] pixels, int w, int h, byte colour)
        {
            var edge = new bool[pixels.Length];

            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    if (pixels[y * w + x] != 0) continue;

                    // A transparent pixel touching ink becomes part of the outline.
                    bool touches =
                        (x > 0 && pixels[y * w + x - 1] != 0) ||
                        (x + 1 < w && pixels[y * w + x + 1] != 0) ||
                        (y > 0 && pixels[(y - 1) * w + x] != 0) ||
                        (y + 1 < h && pixels[(y + 1) * w + x] != 0);

                    edge[y * w + x] = touches;
                }
            }

            for (int i = 0; i < pixels.Length; ++i)
                if (edge[i]) pixels[i] = colour;
        }

        // ---- text ------------------------------------------------------------

        /// <summary>
        /// A glyph as a vgaimg, with the font's ink in one palette colour.
        ///
        /// The font module draws the shape; this only decides the colour, because a
        /// font that carried its own colours could not be tinted for the menu and the
        /// HUD at once.
        /// </summary>
        public static byte[] Glyph(int c, int w, int h, int colour)
        {
            byte[] ink = AiFont.Render(c, w, h);

            var pixels = new byte[w * h];

            if (ink != null)
            {
                for (int i = 0; i < ink.Length; ++i)
                    if (ink[i] != 0) pixels[i] = (byte)colour;
            }

            return AiArt.Pack(w, h, 0, 0, pixels);
        }

        /// <summary>A digit or sign for the HUD, drawn on a transparent background.</summary>
        public static byte[] HudGlyph(int c, int w, int h, int colour)
        {
            return Glyph(c, w, h, colour);
        }

        // ---- menu graphics ---------------------------------------------------

        /// <summary>
        /// The small pieces the menus are built from: the selector arrows, the skull
        /// cursor and the thermometer.
        ///
        /// They are identified by name, and an unknown name gets a plain plate — a menu
        /// with a hole in it is worse than a menu with a box.
        /// </summary>
        public static byte[] MenuPiece(string name, int w, int h, byte[] pal, uint seed)
        {
            var pixels = new byte[w * h];
            var nearest = new AiArt.Nearest(pal);

            string n = (name ?? "").ToUpperInvariant();

            int baseIndex = n.StartsWith("M_SKULL", StringComparison.Ordinal) ? 80 : 96;

            for (int y = 0; y < h; ++y)
            {
                double v = (y + 0.5) / h;

                for (int x = 0; x < w; ++x)
                {
                    double u = (x + 0.5) / w;
                    int shade = -1;

                    if (n == "M_SKULL1" || n == "M_SKULL2")
                    {
                        // A skull: a round cranium, two sockets and a jaw.
                        if (Ellipse(u, v, 0.5, 0.42, 0.36, 0.36)) shade = 6;
                        if (Ellipse(u, v, 0.36, 0.42, 0.10, 0.10)) shade = 0;
                        if (Ellipse(u, v, 0.64, 0.42, 0.10, 0.10)) shade = 0;
                        if (Box(u, v, 0.42, 0.68, 0.58, 0.82)) shade = 5;
                    }
                    else if (n == "M_LSCNTR" || n == "M_LSLEFT" || n == "M_LSRGHT")
                    {
                        // The selector: a bar, with a notch at one end.
                        if (Box(u, v, 0.1, 0.2, 0.9, 0.8)) shade = 8;

                        if (n == "M_LSLEFT" && Box(u, v, 0.1, 0.2, 0.4, 0.8)) shade = 10;
                        if (n == "M_LSRGHT" && Box(u, v, 0.6, 0.2, 0.9, 0.8)) shade = 10;
                        if (n == "M_LSCNTR" && Box(u, v, 0.4, 0.2, 0.6, 0.8)) shade = 10;
                    }
                    else if (n.StartsWith("M_THERM", StringComparison.Ordinal))
                    {
                        // A thermometer bulb and stem.
                        if (Ellipse(u, v, 0.5, 0.82, 0.34, 0.18)) shade = 8;
                        if (Box(u, v, 0.36, 0.05, 0.64, 0.80)) shade = 8;
                        if (Box(u, v, 0.42, 0.20, 0.58, 0.78)) shade = 11;
                    }
                    else
                    {
                        if (Box(u, v, 0.1, 0.1, 0.9, 0.9)) shade = 6;
                    }

                    if (shade < 0) continue;

                    pixels[y * w + x] = (byte)(baseIndex + shade);
                }
            }

            Outline(pixels, w, h, (byte)Math.Max(0, baseIndex - 2));

            return AiArt.Pack(w, h, 0, 0, pixels);
        }
    }
}
