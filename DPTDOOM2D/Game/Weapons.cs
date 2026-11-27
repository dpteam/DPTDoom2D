using System;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Game
{
    /// <summary>weapons.c — projectiles, hitscan and the player's guns.</summary>
    public static class Weapons
    {
        public const int MaxWpn = 300;

        public const int WNone = 0, WRocket = 1, WPlasma = 2, WAPlasma = 3, WBall1 = 4, WBall2 = 5,
                         WBall7 = 6, WBfgBall = 7, WBfgHit = 8, WManf = 9, WRevf = 10, WFire = 11;

        sealed class Weapon
        {
            public Obj O = new Obj();
            public byte T, S;
            public int Own;
            public int Target;
        }

        static readonly Weapon[] wp = new Weapon[MaxWpn];
        static Res.Locked[] snd;
        static VgaImage[] spr;
        static byte[] sprd;

        /// <summary>hit_xv/hit_yv: the direction the last hitscan was travelling.</summary>
        public static int HitXv, HitYv;

        static Weapons()
        {
            for (int i = 0; i < MaxWpn; ++i) wp[i] = new Weapon();
        }

        public static void Save(BinWriter w)
        {
            int n = MaxWpn;
            while (--n > 0 && wp[n].T == 0) { }
            ++n;
            w.Int(n);
            for (int i = 0; i < n; ++i)
            {
                w.Int(wp[i].O.X); w.Int(wp[i].O.Y); w.Int(wp[i].O.XV); w.Int(wp[i].O.YV);
                w.Int(wp[i].O.VX); w.Int(wp[i].O.VY); w.Int(wp[i].O.R); w.Int(wp[i].O.H);
                w.Byte(wp[i].T); w.Byte(wp[i].S); w.Int(wp[i].Own); w.Short(wp[i].Target);
            }
        }

        public static void Load(BinReader r)
        {
            Init();
            int n = r.Int();
            for (int i = 0; i < n && i < MaxWpn; ++i)
            {
                wp[i].O.X = r.Int(); wp[i].O.Y = r.Int(); wp[i].O.XV = r.Int(); wp[i].O.YV = r.Int();
                wp[i].O.VX = r.Int(); wp[i].O.VY = r.Int(); wp[i].O.R = r.Int(); wp[i].O.H = r.Int();
                wp[i].T = r.Byte(); wp[i].S = r.Byte(); wp[i].Own = r.Int(); wp[i].Target = r.Short();
            }
        }

        public static void Alloc()
        {
            string[] nm = {
                "PISTOL","SHOTGN","DSHTGN","RLAUNC","RXPLOD","PLASMA","FIRSHT","FIRXPL",
                "BAREXP","PUNCH","SAWHIT","MGUN","SPARK1","SPARK2"
            };

            spr = new VgaImage[49 * 2];
            sprd = new byte[49 * 2];

            int i = 0;
            for (; i < 4; ++i) { spr[i * 2] = Misc.GetSpr("MISL", i, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("MISL", i, 2, sprd, i * 2 + 1); }
            for (; i < 6; ++i) { spr[i * 2] = Misc.GetSpr("PLSS", i - 4, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("PLSS", i - 4, 2, sprd, i * 2 + 1); }
            for (; i < 11; ++i) { spr[i * 2] = Misc.GetSpr("PLSE", i - 6, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("PLSE", i - 6, 2, sprd, i * 2 + 1); }
            for (; i < 13; ++i) { spr[i * 2] = Misc.GetSpr("APLS", i - 11, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("APLS", i - 11, 2, sprd, i * 2 + 1); }
            for (; i < 18; ++i) { spr[i * 2] = Misc.GetSpr("APBX", i - 13, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("APBX", i - 13, 2, sprd, i * 2 + 1); }
            for (; i < 20; ++i) { spr[i * 2] = Misc.GetSpr("BFS1", i - 18, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("BFS1", i - 18, 2, sprd, i * 2 + 1); }
            for (; i < 26; ++i) { spr[i * 2] = Misc.GetSpr("BFE1", i - 20, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("BFE1", i - 20, 2, sprd, i * 2 + 1); }
            for (; i < 30; ++i) { spr[i * 2] = Misc.GetSpr("BFE2", i - 26, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("BFE2", i - 26, 2, sprd, i * 2 + 1); }
            for (; i < 32; ++i) { spr[i * 2] = Misc.GetSpr("MISL", i - 30 + 4, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("MISL", i - 30 + 4, 2, sprd, i * 2 + 1); }
            for (; i < 37; ++i) { spr[i * 2] = Misc.GetSpr("BAL1", i - 32, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("BAL1", i - 32, 2, sprd, i * 2 + 1); }
            for (; i < 42; ++i) { spr[i * 2] = Misc.GetSpr("BAL7", i - 37, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("BAL7", i - 37, 2, sprd, i * 2 + 1); }
            for (; i < 47; ++i) { spr[i * 2] = Misc.GetSpr("BAL2", i - 42, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("BAL2", i - 42, 2, sprd, i * 2 + 1); }
            for (; i < 49; ++i) { spr[i * 2] = Misc.GetSpr("MANF", i - 47, 1, sprd, i * 2); spr[i * 2 + 1] = Misc.GetSpr("MANF", i - 47, 2, sprd, i * 2 + 1); }

            snd = new Res.Locked[14];
            for (i = 0; i < 14; ++i) snd[i] = Misc.GetSnd(nm[i]);
        }

        public static void Init()
        {
            for (int i = 0; i < MaxWpn; ++i) wp[i].T = WNone;
        }

        public static void Act()
        {
            Obj o = new Obj();

            for (int i = 0; i < MaxWpn; ++i)
            {
                if (wp[i].T == WNone) continue;

                if (wp[i].T == WRocket || wp[i].T == WRevf)
                    Smoke.Gas(wp[i].O.X + Misc.Sign(wp[i].O.XV) * 2,
                              wp[i].O.Y - wp[i].O.H / 2, 3, 3,
                              wp[i].O.XV + wp[i].O.VX, wp[i].O.YV + wp[i].O.VY, 64);

                --wp[i].O.YV;
                int st = Misc.MoveObj(wp[i].O);
                if ((st & Z.FallOut) != 0) { wp[i].T = WNone; continue; }

                if ((st & Z.HitWater) != 0)
                    switch (wp[i].T)
                    {
                        case WPlasma:
                        case WAPlasma:
                        case WBfgBall:
                            break;
                        default:
                            Misc.Splash(wp[i].O, wp[i].O.R + wp[i].O.H);
                            break;
                    }

                switch (wp[i].T)
                {
                    case WRevf:
                        if (Monsters.GetObjPos(wp[i].Target, o) != 0)
                            Throw(i, wp[i].O.X, wp[i].O.Y - 2,
                                  o.X + o.XV + o.VX, o.Y + o.YV + o.VY, 2, 5, 12);
                        goto case WRocket;

                    case WRocket:
                        if (wp[i].S >= 2) { if (++wp[i].S >= 8) wp[i].T = WNone; break; }
                        if ((st & Z.HitAir) != 0) Misc.SetSpeed(wp[i].O, 12);
                        if ((st & (Z.HitWall | Z.HitCeil | Z.HitLand)) != 0)
                        {
                            wp[i].S = 2; wp[i].O.XV = wp[i].O.YV = 0;
                            Misc.Sound(snd[4], 128);
                            Misc.Explode(wp[i].O.X, wp[i].O.Y, 30, wp[i].Own);
                            break;
                        }
                        else if (Misc.Hit(wp[i].O, 10, wp[i].Own, Hit.Some) != 0)
                        {
                            wp[i].S = 2; wp[i].O.XV = wp[i].O.YV = 0;
                            Misc.Sound(snd[4], 128);
                            Misc.Explode(wp[i].O.X, wp[i].O.Y - wp[i].O.H / 2, 30, wp[i].Own);
                            break;
                        }
                        Player.BfgFly(wp[i].O.X, wp[i].O.Y - wp[i].O.H / 2, wp[i].Own);
                        break;

                    case WPlasma:
                    case WAPlasma:
                        if ((st & Z.InWater) != 0)
                        {
                            Misc.Sound(snd[12], 128);
                            Switches.WaterTrap(wp[i].O);
                            Switches.ChkTrap(1, 10, wp[i].Own, Hit.Electro);
                            Switches.Untrap(5);
                            wp[i].T = WNone;
                            break;
                        }
                        goto case WBall1;

                    case WBall1:
                    case WBall7:
                    case WBall2:
                    case WManf:
                        {
                            bool fast = wp[i].T == WBall1 || wp[i].T == WBall7 || wp[i].T == WBall2 || wp[i].T == WManf;
                            if (wp[i].S >= 2)
                            {
                                if (++wp[i].S >= (fast ? 8 : 12)) wp[i].T = WNone;
                                break;
                            }
                            if ((st & Z.HitAir) != 0) Misc.SetSpeed(wp[i].O, 16);
                            if ((st & (Z.HitWall | Z.HitCeil | Z.HitLand)) != 0)
                            {
                                wp[i].S = 2; wp[i].O.XV = wp[i].O.YV = 0;
                                Misc.Sound(snd[7], 128);
                                break;
                            }
                            else if (Misc.Hit(wp[i].O, (wp[i].T == WBall7 || wp[i].T == WManf) ? 40 : (wp[i].T == WBall2 ? 20 : 5),
                                              wp[i].Own, Hit.Some) != 0)
                            {
                                wp[i].S = 2; wp[i].O.XV = wp[i].O.YV = 0;
                                Misc.Sound(snd[7], 128);
                                break;
                            }
                            wp[i].S ^= 1;
                            break;
                        }

                    case WBfgBall:
                        if ((st & Z.InWater) != 0)
                        {
                            Misc.Sound(snd[8], 40); Misc.Sound(snd[13], 128);
                            Switches.WaterTrap(wp[i].O);
                            Switches.ChkTrap(1, 1000, wp[i].Own, Hit.Electro);
                            Switches.Untrap(5);
                            wp[i].T = WNone;
                            break;
                        }
                        if (wp[i].S >= 2) { if (++wp[i].S >= 14) wp[i].T = WNone; break; }
                        else if ((st & (Z.HitWall | Z.HitCeil | Z.HitLand)) != 0)
                        {
                            Misc.Bfg9000(wp[i].O.X, wp[i].O.Y, wp[i].Own);
                            wp[i].S = 2; wp[i].O.XV = wp[i].O.YV = 0;
                            Misc.Sound(snd[8], 128);
                            break;
                        }
                        else if (Misc.Hit(wp[i].O, 100, wp[i].Own, Hit.Bfg) != 0)
                        {
                            Misc.Bfg9000(wp[i].O.X, wp[i].O.Y, wp[i].Own);
                            wp[i].S = 2; wp[i].O.XV = wp[i].O.YV = 0;
                            Misc.Sound(snd[8], 128);
                            break;
                        }
                        Player.BfgFly(wp[i].O.X, wp[i].O.Y - wp[i].O.H / 2, wp[i].Own);
                        wp[i].S ^= 1;
                        break;

                    case WBfgHit:
                        if (++wp[i].S >= 8) wp[i].T = WNone;
                        break;
                }
            }
        }

        public static void Draw()
        {
            for (int i = 0; i < MaxWpn; ++i)
            {
                int s = -1, d = 0;

                switch (wp[i].T)
                {
                    case WNone:
                    default:
                        break;

                    case WRevf:
                    case WRocket:
                        d = wp[i].S;
                        if (d < 2)
                        {
                            d = wp[i].O.XV > 0 ? 1 : 0;
                            int x = Math.Abs(wp[i].O.XV);
                            int y = wp[i].O.YV;
                            s = 0;
                            if (y < 0) { if (-y >= x) s = 30; }
                            else if (y > 0) if (y >= x / 2) s = 31;
                        }
                        else { s = (d - 2) / 2 + 1; d = 0; }
                        break;

                    case WManf:
                        s = wp[i].S;
                        if (s >= 2) { s /= 2; break; }
                        goto case WPlasma;

                    case WPlasma:
                    case WAPlasma:
                    case WBall1:
                    case WBall7:
                    case WBall2:
                        s = wp[i].S;
                        if (s >= 2) s = s / 2 + 1;
                        switch (wp[i].T)
                        {
                            case WPlasma: s += 4; break;
                            case WAPlasma: s += 11; break;
                            case WBall1: s += 32; break;
                            case WBall2: s += 42; break;
                            case WBall7: s += 37; d = wp[i].O.XV >= 0 ? 1 : 0; break;
                            case WManf: s += 47; d = wp[i].O.XV >= 0 ? 1 : 0; break;
                        }
                        break;

                    case WBfgBall:
                        s = wp[i].S;
                        if (s >= 2) s = s / 2 + 1;
                        s += 18;
                        break;

                    case WBfgHit:
                        s = wp[i].S / 2 + 26;
                        break;
                }

                if (s >= 0) Misc.DrawSpr(wp[i].O.X, wp[i].O.Y, spr[s * 2 + d], sprd[s * 2 + d] != 0);
            }
        }

        /// <summary>WP_gun: the hitscan walk.</summary>
        public static void Gun(int x, int y, int xd, int yd, int o, int v)
        {
            int f = Bmap.Monster | Bmap.Wall;
            if (o != -1) f |= Bmap.Plr1;
            if (o != -2) f |= Bmap.Plr2;

            int sx, sy;
            xd -= x; yd -= y;
            if (xd > 0) sx = 1; else if (xd < 0) sx = -1; else sx = 0;
            if (yd > 0) sy = 1; else if (yd < 0) sy = -1; else sy = 0;
            if (xd == 0 && yd == 0) return;

            xd = Math.Abs(xd); yd = Math.Abs(yd);
            int d = xd > yd ? xd : yd;
            HitXv = xd * 10 / d * sx;
            HitYv = yd * 10 / d * sy;

            long xe = 0, ye = 0;
            int lx = x, ly = y;

            for (; ; )
            {
                if (x < 0 || x >= World.FLDW * 8 || y < 0 || y >= World.FLDH * 8) break;

                int m = Bmap.At(x >> 5, y >> 5);
                if ((m & f) != 0)
                {
                    if ((m & Bmap.Wall) != 0)
                    {
                        int cx = x >> 3, cy = y >> 3;
                        byte c = World.Fld[cy * World.FLDW + cx];
                        if (c == 1 || c == 2)
                        {
                            for (x = lx, y = ly, xe = ye = 0; ; )
                            {
                                c = World.Fld[(y >> 3) * World.FLDW + (x >> 3)];
                                if (c == 1 || c == 2) break;
                                lx = x; ly = y;
                                xe += xd; if (xe >= d) { xe -= d; x += sx; }
                                ye += yd; if (ye >= d) { ye -= d; y += sy; }
                            }
                            Dots.Spark(lx, ly, sx * 10, sy * 10, 1);
                            break;
                        }
                    }
                    if ((m & (Bmap.Monster | Bmap.Plr1 | Bmap.Plr2)) != 0)
                        if (Monsters.GunHit(x, y, o, sx * v, sy * v) != 0) break;

                    lx = x; ly = y;
                    xe += xd << 3;
                    if (xe >= d) { x += (int)(xe / d) * sx; xe %= d; }
                    ye += yd << 3;
                    if (ye >= d) { y += (int)(ye / d) * sy; ye %= d; }
                }
                else
                {
                    if (sx == 0) m = 0;
                    else { m = x & 31; if (sx > 0) m ^= 31; ++m; }
                    int s;
                    if (sy == 0) s = 0;
                    else { s = y & 31; if (sy > 0) s ^= 31; ++s; }
                    if ((s < m && s != 0) || m == 0) m = s;

                    lx = x; ly = y;
                    x += (int)((xd * m + xe) / d) * sx; xe = (xd * m + xe) % d;
                    y += (int)((yd * m + ye) / d) * sy; ye = (yd * m + ye) % d;
                }
            }
        }

        public static void Punch(int x, int y, int d, int own)
        {
            Obj o = new Obj();
            o.X = x; o.Y = y; o.R = 12; o.H = 26;
            o.XV = o.YV = o.VX = o.VY = 0;
            if (Misc.Hit(o, d, own, Hit.Some) != 0) Misc.Sound(snd[9], 128);
        }

        public static int Chainsaw(int x, int y, int d, int own)
        {
            Obj o = new Obj();
            o.X = x; o.Y = y; o.R = 12; o.H = 26;
            o.XV = o.YV = o.VX = o.VY = 0;
            return Misc.Hit(o, d, own, Hit.Some) != 0 ? 1 : 0;
        }

        static void Throw(int i, int x, int y, int xd, int yd, int r, int h, int s)
        {
            wp[i].O.X = x; wp[i].O.Y = y + h / 2;
            yd -= y; xd -= x;
            int m = Math.Max(Math.Abs(xd), Math.Abs(yd));
            if (m == 0) m = 1;
            wp[i].O.XV = xd * s / m; wp[i].O.YV = yd * s / m;
            wp[i].O.R = r; wp[i].O.H = h;
            wp[i].O.VX = wp[i].O.VY = 0;
        }

        static int Free()
        {
            for (int i = 0; i < MaxWpn; ++i) if (wp[i].T == WNone) return i;
            return -1;
        }

        /// <summary>
        /// How many projectiles of this type are alive, for the checks.
        ///
        /// The weapon array is private because nothing outside should poke at a
        /// projectile; a counter is the smallest thing that lets a test ask "did it
        /// spawn" without handing the whole array out.
        /// </summary>
        public static int CountOf(int type)
        {
            int n = 0;
            for (int i = 0; i < MaxWpn; ++i) if (wp[i].T == type) ++n;
            return n;
        }

        /// <summary>Where a projectile of this type is, or false when there is none.</summary>
        public static bool PositionOf(int type, out int x, out int y)
        {
            for (int i = 0; i < MaxWpn; ++i)
            {
                if (wp[i].T != type) continue;

                x = wp[i].O.X;
                y = wp[i].O.Y;
                return true;
            }

            x = y = 0;
            return false;
        }

        /// <summary>
        /// The image a projectile index resolves to, for the checks.
        ///
        /// A projectile whose sprite index lands on a null image is invisible while
        /// being perfectly alive: it flies, it hits, it makes a sound, and the player
        /// sees nothing. That is not something a "does the game crash" test notices.
        /// </summary>
        public static VgaImage SpriteAt(int index)
        {
            return index >= 0 && index < spr.Length ? spr[index] : null;
        }

        public static void RocketShot(int x, int y, int xd, int yd, int o)
        {
            int i = Free();
            if (i < 0) return;
            Misc.Sound(snd[3], 128);
            wp[i].T = WRocket; wp[i].S = (byte)(xd >= x ? 1 : 0);
            wp[i].Own = o;
            Throw(i, x, y, xd, yd, 2, 5, 12);
        }

        public static void Revf(int x, int y, int xd, int yd, int o, int t)
        {
            int i = Free();
            if (i < 0) return;
            Misc.Sound(snd[3], 128);
            wp[i].T = WRevf; wp[i].S = (byte)(xd >= x ? 1 : 0);
            wp[i].Own = o; wp[i].Target = t;
            Throw(i, x, y, xd, yd, 2, 5, 12);
        }

        public static void PlasmaShot(int x, int y, int xd, int yd, int o)
        {
            int i = Free();
            if (i < 0) return;
            Misc.Sound(snd[5], 64);
            wp[i].T = WPlasma; wp[i].S = 0; wp[i].Own = o;
            Throw(i, x, y, xd, yd, 2, 5, 16);
        }

        public static void Ball1(int x, int y, int xd, int yd, int o)
        {
            int i = Free();
            if (i < 0) return;
            wp[i].T = WBall1; wp[i].S = 0; wp[i].Own = o;
            Throw(i, x, y, xd, yd, 2, 5, 16);
        }

        public static void Ball2(int x, int y, int xd, int yd, int o)
        {
            int i = Free();
            if (i < 0) return;
            wp[i].T = WBall2; wp[i].S = 0; wp[i].Own = o;
            Throw(i, x, y, xd, yd, 2, 5, 16);
        }

        public static void Ball7(int x, int y, int xd, int yd, int o)
        {
            int i = Free();
            if (i < 0) return;
            wp[i].T = WBall7; wp[i].S = 0; wp[i].Own = o;
            Throw(i, x, y, xd, yd, 2, 5, 16);
        }

        public static void APlasma(int x, int y, int xd, int yd, int o)
        {
            int i = Free();
            if (i < 0) return;
            Misc.Sound(snd[5], 64);
            wp[i].T = WAPlasma; wp[i].S = 0; wp[i].Own = o;
            Throw(i, x, y, xd, yd, 2, 5, 16);
        }

        public static void ManFire(int x, int y, int xd, int yd, int o)
        {
            int i = Free();
            if (i < 0) return;
            Misc.Sound(snd[6], 128);
            wp[i].T = WManf; wp[i].S = 0; wp[i].Own = o;
            Throw(i, x, y, xd, yd, 5, 11, 16);
        }

        public static void BfgShot(int x, int y, int xd, int yd, int o)
        {
            int i = Free();
            if (i < 0) return;
            wp[i].T = WBfgBall; wp[i].S = 0; wp[i].Own = o;
            Throw(i, x, y, xd, yd, 5, 12, 16);
        }

        public static void BfgHitShot(int x, int y, int o)
        {
            int i = Free();
            if (i < 0) return;
            wp[i].T = WBfgHit; wp[i].S = 0;
            wp[i].O.X = x; wp[i].O.Y = y;
            wp[i].O.XV = wp[i].O.YV = 0;
            wp[i].O.R = 0; wp[i].O.H = 1;
            wp[i].O.VX = wp[i].O.VY = 0;
            wp[i].Own = o;
        }

        public static void Pistol(int x, int y, int xd, int yd, int o)
        {
            Misc.Sound(snd[0], 96);
            Gun(x, y, xd, yd, o, 1);
            if (Game.Dm)
            {
                Gun(x, y + 1, xd, yd + 1, o, 1);
                Gun(x, y - 1, xd, yd - 1, o, 1);
            }
        }

        public static void MGun(int x, int y, int xd, int yd, int o)
        {
            Misc.Sound(snd[11], 128);
            Gun(x, y, xd, yd, o, 1);
        }

        public static void Shotgun(int x, int y, int xd, int yd, int o)
        {
            Misc.Sound(snd[1], 128);
            for (int i = 0; i < 10; ++i)
            {
                int j = Glob.Rand(9) - 4;
                Gun(x, y + j, xd, yd + j, o, i & 1);
            }
        }

        public static void DShotgun(int x, int y, int xd, int yd, int o)
        {
            Misc.Sound(snd[2], 128);
            for (int i = Game.Dm ? 25 : 20; i >= 0; --i)
            {
                int j = Glob.Rand(21) - 10;
                Gun(x, y + j, xd, yd + j, o, (i % 3) != 0 ? 0 : 1);
            }
        }

        public static void Ognemet(int x, int y, int xd, int yd, int xv, int yv, int o)
        {
            int m = Math.Abs(xd - x);
            if (m == 0) m = Math.Abs(yd - y);
            Smoke.Flame(x, y, xv, yv, 2, 2, (xd - x) * 3000 / m, (yd - y) * 3000 / m, 1, o);
        }
    }
}
