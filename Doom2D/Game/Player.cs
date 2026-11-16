using System;
using Doom2D.Engine;

namespace Doom2D.Game
{
    /// <summary>player.h — one player.</summary>
    public sealed class PlayerData
    {
        public readonly Obj O = new Obj();

        public int LookY;
        public int St, S;
        public int Life, Armor, Hit, HitO;
        public int Pain, Air;
        public int Invl, Suit;
        public int D;

        public int Frag, Ammo, Shel, Rock, Cell, Fuel, Kills, Secrets;
        public byte Fire, CWpn, CSnd;
        public byte Amul;
        public int Wpns;
        public int Wpn;
        public byte F;
        public byte DrawSt;
        public byte Color;
        public int Id;
        public byte Keys;
        public int Lives;

        // Key bindings (the _keys struct in the C code; never saved).
        public int Ku, Kd, Kl, Kr, Kf, Kj, Kwl, Kwr, Kp;
    }

    /// <summary>player.c — movement, weapons, damage and the player sprite.</summary>
    public static class Player
    {
        public const int PlRad = 8;
        public const int PlHt = 26;
        public const int PlSwUp = 4;
        public const int PlFlyUp = 4;
        public const int PlAir = 360;
        public const int PlAquaAir = 1091;
        public const int PowerUpTime = 546;

        public const byte PL_DRAWLIFE = 1, PL_DRAWARMOR = 2, PL_DRAWWPN = 4, PL_DRAWFRAG = 8,
                          PL_DRAWAIR = 16, PL_DRAWKEYS = 32, PL_DRAWLIVES = 64;

        public const byte PLF_FIRE = 1, PLF_PNSND = 2, PLF_UP = 4, PLF_DOWN = 8;

        const int Stand = 0, Go = 1, Die = 2, Slop = 3, Dead = 4, Mess = 5, Out = 6, Fall = 7;

        public static bool Immortal, Fly;
        public static int Jump = 10, Run = 8;

        static readonly int[] wpIt = { 0, ItemType.CSaw, 0, ItemType.SGun, ItemType.SGun2, ItemType.MGun,
                                       ItemType.Laun, ItemType.Plas, ItemType.BFG, ItemType.Gun2, 0 };

        static int aitime;
        static Res.Locked[] aisnd, pdsnd, snd;
        static VgaImage[] spr, wpn;
        static byte[] sprd;
        static readonly byte[] goAnim = Ascii("BDACDA");
        static readonly byte[] dieAnim = Ascii("HHHHIIIIJJJJKKKKLLLLMMMM");
        static readonly byte[] slopAnim = Ascii("OOPPQQRRSSTTUUVVWW");

        static byte[] Ascii(string s)
        {
            byte[] b = new byte[s.Length + 1];
            for (int i = 0; i < s.Length; ++i) b[i] = (byte)s[i];
            return b;
        }

        // ------------------------------------------------------------- save/load

        static void SaveOne(BinWriter w, PlayerData p)
        {
            w.Int(p.O.X); w.Int(p.O.Y); w.Int(p.O.XV); w.Int(p.O.YV);
            w.Int(p.O.VX); w.Int(p.O.VY); w.Int(p.O.R); w.Int(p.O.H);
            w.Int(p.LookY); w.Int(p.St); w.Int(p.S);
            w.Int(p.Life); w.Int(p.Armor); w.Int(p.Hit); w.Int(p.HitO);
            w.Int(p.Pain); w.Int(p.Air);
            w.Int(p.Invl); w.Int(p.Suit);
            w.Byte((byte)p.D);
            w.Int(p.Frag); w.Int(p.Ammo); w.Int(p.Shel); w.Int(p.Rock);
            w.Int(p.Cell); w.Int(p.Fuel); w.Int(p.Kills); w.Int(p.Secrets);
            w.Byte(p.Fire); w.Byte(p.CWpn); w.Byte(p.CSnd);
            w.Byte(p.Amul);
            w.UShort(p.Wpns);
            w.Byte((byte)p.Wpn);
            w.Byte(p.F);
            w.Byte(p.DrawSt);
            w.Byte(p.Color);
            w.Int(p.Id);
            w.Byte(p.Keys);
            w.Byte((byte)p.Lives);
        }

        static void LoadOne(BinReader r, PlayerData p)
        {
            p.O.X = r.Int(); p.O.Y = r.Int(); p.O.XV = r.Int(); p.O.YV = r.Int();
            p.O.VX = r.Int(); p.O.VY = r.Int(); p.O.R = r.Int(); p.O.H = r.Int();
            p.LookY = r.Int(); p.St = r.Int(); p.S = r.Int();
            p.Life = r.Int(); p.Armor = r.Int(); p.Hit = r.Int(); p.HitO = r.Int();
            p.Pain = r.Int(); p.Air = r.Int();
            p.Invl = r.Int(); p.Suit = r.Int();
            p.D = r.Byte();
            p.Frag = r.Int(); p.Ammo = r.Int(); p.Shel = r.Int(); p.Rock = r.Int();
            p.Cell = r.Int(); p.Fuel = r.Int(); p.Kills = r.Int(); p.Secrets = r.Int();
            p.Fire = r.Byte(); p.CWpn = r.Byte(); p.CSnd = r.Byte();
            p.Amul = r.Byte();
            p.Wpns = r.UShort();
            p.Wpn = r.Byte();
            p.F = r.Byte();
            p.DrawSt = r.Byte();
            p.Color = r.Byte();
            p.Id = r.Int();
            p.Keys = r.Byte();
            p.Lives = (sbyte)r.Byte();
        }

        public static void Save(BinWriter w)
        {
            SaveOne(w, Game.Pl1);
            if (Game.TwoPl) SaveOne(w, Game.Pl2);
            w.Int(Jump); w.Int(Run); w.Byte((byte)(Immortal ? 1 : 0));
        }

        public static void Load(BinReader r)
        {
            LoadOne(r, Game.Pl1);
            if (Game.TwoPl) LoadOne(r, Game.Pl2);
            Jump = r.Int(); Run = r.Int(); Immortal = r.Byte() != 0;
        }

        // ------------------------------------------------------------------ setup

        public static void Init()
        {
            Immortal = false;
            Jump = 10; Run = 8;
            aitime = 0;
        }

        public static void Alloc()
        {
            string[] nm = {
                "OOF","PLPAIN","PLDETH","SLOP","PDIEHI",
                "BFG","SAWUP","SAWIDL","SAWFUL","SAWHIT","PLFALL"
            };

            spr = new VgaImage[27 * 2];
            sprd = new byte[27 * 2];
            for (int i = 0; i < 27; ++i)
            {
                spr[i * 2] = Misc.GetSpr("PLAY", i, 1, sprd, i * 2);
                spr[i * 2 + 1] = Misc.GetSpr("PLAY", i, 2, sprd, i * 2 + 1);
            }

            wpn = new VgaImage[11 * 6];
            for (int i = 1; i < 11; ++i)
            {
                string s = "PWP" + (char)((i < 10) ? ('0' + i) : ('A' - 10 + i));
                for (int j = 0; j < 6; ++j) wpn[i * 6 + j] = Misc.GetSpr(s, j, 1);
            }

            snd = new Res.Locked[11];
            for (int i = 0; i < 11; ++i) snd[i] = Misc.GetSnd(nm[i]);

            aisnd = new Res.Locked[3];
            for (int i = 0; i < 3; ++i) aisnd[i] = Misc.GetSnd("AI" + (char)('1' + i));

            pdsnd = new Res.Locked[5];
            for (int i = 0; i < 5; ++i) pdsnd[i] = Misc.GetSnd("PLDTH" + (char)('1' + i));
        }

        static int Nonz(int a) { return a != 0 ? a : 1; }

        static int FireDirY(PlayerData p)
        {
            if ((p.F & PLF_UP) != 0) return -42;
            if ((p.F & PLF_DOWN) != 0) return 19;
            return 0;
        }

        static void Fire(PlayerData p)
        {
            if (p.CWpn != 0) return;

            if (p.Wpn == 8)
            {
                if (p.Fire == 0)
                {
                    if (Kbd.Keys[p.Kf] && p.Cell >= 40)
                    {
                        Misc.Sound(snd[5], 128);
                        p.Fire = 21; p.Cell -= 40; p.DrawSt |= PL_DRAWWPN;
                    }
                    return;
                }
                if (p.Fire == 1) p.CWpn = 12;
                return;
            }
            else if (p.Wpn == 1)
            {
                if (p.CSnd == 0)
                {
                    if (!Kbd.Keys[p.Kf]) { Misc.Sound(snd[7], 128); p.CSnd = 13; return; }
                }
                if (Kbd.Keys[p.Kf] && p.Fire == 0)
                {
                    p.Fire = 2;
                    Weapons.Chainsaw(p.O.X + (p.D != 0 ? 4 : -4), p.O.Y, Game.Dm ? 9 : 3, p.Id);
                    if (p.CSnd == 0) { Misc.Sound(snd[8], 128); p.CSnd = 29; }
                }
                return;
            }
            else if (p.Fire != 0) return;

            if (Kbd.Keys[p.Kf] || p.Wpn == 8)
            {
                switch (p.Wpn)
                {
                    case 2: case 5:
                        if (p.Ammo == 0) return;
                        --p.Ammo; p.DrawSt |= PL_DRAWWPN; break;
                    case 3: case 9:
                        if (p.Shel == 0) return;
                        --p.Shel; p.DrawSt |= PL_DRAWWPN; break;
                    case 4:
                        if (p.Shel < 2) return;
                        p.Shel -= 2; p.DrawSt |= PL_DRAWWPN; break;
                    case 6:
                        if (p.Rock == 0) return;
                        --p.Rock; p.DrawSt |= PL_DRAWWPN; break;
                    case 7:
                        if (p.Cell == 0) return;
                        --p.Cell; p.DrawSt |= PL_DRAWWPN; break;
                    case 10:
                        if (p.Fuel == 0) return;
                        --p.Fuel; p.DrawSt |= PL_DRAWWPN; break;
                }

                int xd = p.O.X + (p.D != 0 ? 30 : -30);
                int yd = p.O.Y - 15 + FireDirY(p);

                if (p.Wpn == 10)
                    Weapons.Ognemet(p.O.X, p.O.Y - 15, xd, yd, p.O.XV + p.O.VX, p.O.YV + p.O.VY, p.Id);
                else if (p.Wpn >= 1) FireWeapon(p, xd, yd);
                else Weapons.Punch(p.O.X + (p.D != 0 ? 4 : -4), p.O.Y, 3, p.Id);

                p.Fire = (byte)FireTime[p.Wpn];
                if (p.Wpn >= 2) p.F |= PLF_FIRE;
            }
        }

        static readonly int[] FireTime = { 5, 2, 6, 18, 36, 2, 12, 2, 0, 2, 1 };

        static void FireWeapon(PlayerData p, int xd, int yd)
        {
            switch (p.Wpn)
            {
                case 1: case 2: case 10: Weapons.Pistol(p.O.X, p.O.Y - 15, xd, yd, p.Id); break;
                case 3: Weapons.Shotgun(p.O.X, p.O.Y - 15, xd, yd, p.Id); break;
                case 4: Weapons.DShotgun(p.O.X, p.O.Y - 15, xd, yd, p.Id); break;
                case 5: Weapons.MGun(p.O.X, p.O.Y - 15, xd, yd, p.Id); break;
                case 6: Weapons.RocketShot(p.O.X, p.O.Y - 15, xd, yd, p.Id); break;
                case 7: Weapons.PlasmaShot(p.O.X, p.O.Y - 15, xd, yd, p.Id); break;
                case 8: Weapons.BfgShot(p.O.X, p.O.Y - 15, xd, yd, p.Id); break;
                case 9: Weapons.Shotgun(p.O.X, p.O.Y - 15, xd, yd, p.Id); break;
            }
        }

        static void ChgWpn(PlayerData p)
        {
            if (p.CWpn != 0) return;
            if (p.Fire != 0 && p.Wpn != 1) return;

            if (Kbd.Keys[p.Kwl])
            {
                do { if (--p.Wpn < 0) p.Wpn = 10; } while ((p.Wpns & (1 << p.Wpn)) == 0);
                p.CWpn = 3;
            }
            else if (Kbd.Keys[p.Kwr])
            {
                do { if (++p.Wpn > 10) p.Wpn = 0; } while ((p.Wpns & (1 << p.Wpn)) == 0);
                p.CWpn = 3;
            }

            if (p.CWpn != 0)
            {
                p.DrawSt |= PL_DRAWWPN;
                p.Fire = 0;
                if (p.Wpn == 1) Misc.Sound(snd[6], 128);
            }
        }

        static void JumpAndBreathe(PlayerData p, int st)
        {
            if (Misc.CanBreathe(p.O.X, p.O.Y, p.O.R, p.O.H) != 0)
            {
                if (p.Air < PlAir) { p.Air = PlAir; p.DrawSt |= PL_DRAWAIR; }
            }
            else
            {
                if (--p.Air < -9)
                {
                    p.Air = 0;
                    HitPlayer(p, 10, -3, Doom2D.Game.Hit.Water);
                }
                else if ((p.Air & 31) == 0) Fx.Bubble(p.O.X, p.O.Y - 20, 0, 0, 5);
                p.DrawSt |= PL_DRAWAIR;
            }

            if (Kbd.Keys[p.Kj])
            {
                if (Fly) p.O.YV = -PlFlyUp;
                else
                {
                    if (Misc.CanStand(p.O.X, p.O.Y, p.O.R) != 0) p.O.YV = -Jump;
                    else if ((st & Z.InWater) != 0) p.O.YV = -PlSwUp;
                }
            }
        }

        public static bool IsDead(PlayerData p)
        {
            return p.St == Dead || p.St == Mess || p.St == Out;
        }

        public static void Restore(PlayerData p)
        {
            p.O.XV = p.O.YV = p.O.VX = p.O.VY = 0;
            p.O.R = PlRad; p.O.H = PlHt;
            p.Pain = 0;
            p.Invl = p.Suit = 0;

            switch (p.St)
            {
                case Dead: case Mess: case Out:
                case Die: case Slop: case Fall:
                    p.Life = 100; p.Armor = 0; p.Air = PlAir;
                    p.Wpns = 5;
                    p.Wpn = 2;
                    p.Ammo = 50; p.Fuel = p.Shel = p.Rock = p.Cell = 0;
                    p.Amul = 1;
                    break;
            }
            p.St = Stand;
            p.Fire = p.CWpn = p.CSnd = 0;
            p.F = 0;
            p.DrawSt = 0xFF;
            p.LookY = 0;
            p.Keys = (byte)(Game.Dm ? 0x70 : 0);
        }

        public static void Reset()
        {
            Game.Pl1.St = Game.Pl2.St = Dead;
            Game.Pl1.Frag = Game.Pl2.Frag = 0;
        }

        public static void Spawn(PlayerData p, int x, int y, int d)
        {
            Restore(p);
            p.O.X = x; p.O.Y = y; p.D = d;
            p.Kills = p.Secrets = 0;
        }

        public static int HitPlayer(PlayerData p, int d, int o, int t)
        {
            if (d == 0) return 0;

            switch (p.St)
            {
                case Die: case Slop: case Dead: case Mess: case Out: case Fall:
                    return 0;
            }

            if (t == Doom2D.Game.Hit.Trap) { if (!Immortal) { p.Armor = 0; p.Life = -100; } }
            else if (t != Doom2D.Game.Hit.Rocket && t != Doom2D.Game.Hit.Electro)
            {
                if (p.Id == -1) { if (o == -1) return 0; }
                else if (o == -2) return 0;
            }

            if (t != Doom2D.Game.Hit.Water && t != Doom2D.Game.Hit.Electro)
                Dots.Blood(p.O.X, p.O.Y - 15, Weapons.HitXv, Weapons.HitYv, d * 2);
            else if (t == Doom2D.Game.Hit.Water)
                Fx.Bubble(p.O.X, p.O.Y - 20, 0, 0, d / 2);

            if (Immortal || p.Invl != 0) return 1;

            p.Hit += d;
            p.HitO = o;
            return 1;
        }

        public static void Damage(PlayerData p)
        {
            if (p.Hit == 0 && p.Life > 0) return;

            switch (p.St)
            {
                case Die: case Slop: case Dead: case Mess: case Out: case Fall:
                    return;
            }

            int i = p.Hit * p.Life / Nonz(p.Armor * 3 / 4 + p.Life);
            p.Pain += p.Hit;
            p.DrawSt |= PL_DRAWLIFE | PL_DRAWARMOR;

            p.Armor -= p.Hit - i;
            if (p.Armor < 0) { p.Life += p.Armor; p.Armor = 0; }

            p.Life -= i;
            if (p.Life <= 0)
            {
                if (p.Life > -30) { p.St = Die; p.S = 0; Misc.Sound(pdsnd[Glob.Rand(5)], 128); }
                else { p.St = Slop; p.S = 0; Misc.Sound(snd[3], 128); }

                if (p.Amul > 1) Items.Spawn(p.O.X, p.O.Y, ItemType.BPack);
                if (!Game.Dm)
                {
                    if ((p.Keys & 16) != 0) Items.Spawn(p.O.X, p.O.Y, ItemType.KeyR);
                    if ((p.Keys & 32) != 0) Items.Spawn(p.O.X, p.O.Y, ItemType.KeyG);
                    if ((p.Keys & 64) != 0) Items.Spawn(p.O.X, p.O.Y, ItemType.KeyB);
                }
                p.Wpns >>= 1;
                for (i = 1; i < 11; ++i, p.Wpns >>= 1)
                    if (i != 2 && (p.Wpns & 1) != 0) Items.Spawn(p.O.X, p.O.Y, wpIt[i]);

                p.Wpns = 5; p.Wpn = 2;
                p.F |= PLF_PNSND;
                p.DrawSt |= PL_DRAWWPN;

                if (Game.Dm && Game.TwoPl)
                {
                    if (p.Id == -1)
                    {
                        if (p.HitO == -2) { ++Game.Pl2.Kills; ++Game.Pl2.Frag; }
                        else if (p.HitO == -1) --Game.Pl1.Frag;
                    }
                    else
                    {
                        if (p.HitO == -1) { ++Game.Pl1.Kills; ++Game.Pl1.Frag; }
                        else if (p.HitO == -2) --Game.Pl2.Frag;
                    }
                    Game.Pl1.DrawSt |= PL_DRAWFRAG;
                    Game.Pl2.DrawSt |= PL_DRAWFRAG;
                }
                p.Life = 0;
            }
        }

        public static void Cry(PlayerData p)
        {
            Misc.Sound(snd[p.Pain > 20 ? 1 : 0], 128);
            p.F |= PLF_PNSND;
        }

        public static int Give(PlayerData p, int t)
        {
            switch (p.St)
            {
                case Die: case Slop: case Dead: case Mess: case Out:
                    return 0;
            }

            int i;
            switch (t)
            {
                case ItemType.Stim:
                case ItemType.Medi:
                    if (p.Life >= 100) return 0;
                    p.Life += (t == ItemType.Medi) ? 25 : 10;
                    if (p.Life > 100) p.Life = 100;
                    p.DrawSt |= PL_DRAWLIFE; return 1;

                case ItemType.Clip:
                    if (p.Ammo >= 200 * p.Amul) return 0;
                    p.Ammo += 10;
                    if (p.Ammo > 200 * p.Amul) p.Ammo = 200 * p.Amul;
                    p.DrawSt |= PL_DRAWWPN; return 1;

                case ItemType.Ammo:
                    if (p.Ammo >= 200 * p.Amul) return 0;
                    p.Ammo += 50;
                    if (p.Ammo > 200 * p.Amul) p.Ammo = 200 * p.Amul;
                    p.DrawSt |= PL_DRAWWPN; return 1;

                case ItemType.Shel:
                    if (p.Shel >= 50 * p.Amul) return 0;
                    p.Shel += 4;
                    if (p.Shel > 50 * p.Amul) p.Shel = 50 * p.Amul;
                    p.DrawSt |= PL_DRAWWPN; return 1;

                case ItemType.SBox:
                    if (p.Shel >= 50 * p.Amul) return 0;
                    p.Shel += 25;
                    if (p.Shel > 50 * p.Amul) p.Shel = 50 * p.Amul;
                    p.DrawSt |= PL_DRAWWPN; return 1;

                case ItemType.Rocket:
                    if (p.Rock >= 50 * p.Amul) return 0;
                    ++p.Rock;
                    if (p.Rock > 50 * p.Amul) p.Rock = 50 * p.Amul;
                    p.DrawSt |= PL_DRAWWPN; return 1;

                case ItemType.RBox:
                    if (p.Rock >= 50 * p.Amul) return 0;
                    p.Rock += 5;
                    if (p.Rock > 50 * p.Amul) p.Rock = 50 * p.Amul;
                    p.DrawSt |= PL_DRAWWPN; return 1;

                case ItemType.Cell:
                    if (p.Cell >= 300 * p.Amul) return 0;
                    p.Cell += 40;
                    if (p.Cell > 300 * p.Amul) p.Cell = 300 * p.Amul;
                    p.DrawSt |= PL_DRAWWPN; return 1;

                case ItemType.CelP:
                    if (p.Cell >= 300 * p.Amul) return 0;
                    p.Cell += 100;
                    if (p.Cell > 300 * p.Amul) p.Cell = 300 * p.Amul;
                    p.DrawSt |= PL_DRAWWPN; return 1;

                case ItemType.BPack:
                    i = 0;
                    if (p.Amul == 1) { p.Amul = 2; i = 1; }
                    i |= Give(p, ItemType.Clip);
                    i |= Give(p, ItemType.Shel);
                    i |= Give(p, ItemType.Rocket);
                    i |= Give(p, ItemType.Cell);
                    return i;

                case ItemType.CSaw:
                    if ((p.Wpns & 2) == 0) { p.Wpns |= 2; p.DrawSt |= PL_DRAWWPN; return 1; }
                    return 0;

                case ItemType.Gun2:
                    i = Give(p, ItemType.Shel);
                    if ((p.Wpns & 512) == 0) { p.Wpns |= 512; i = 1; }
                    p.DrawSt |= PL_DRAWWPN; return i;

                case ItemType.SGun:
                    i = Give(p, ItemType.Shel);
                    if ((p.Wpns & 8) == 0) { p.Wpns |= 8; i = 1; }
                    p.DrawSt |= PL_DRAWWPN; return i;

                case ItemType.SGun2:
                    i = Give(p, ItemType.Shel);
                    if ((p.Wpns & 16) == 0) { p.Wpns |= 16; i = 1; }
                    p.DrawSt |= PL_DRAWWPN; return i;

                case ItemType.MGun:
                    i = Give(p, ItemType.Ammo);
                    if ((p.Wpns & 32) == 0) { p.Wpns |= 32; i = 1; }
                    p.DrawSt |= PL_DRAWWPN; return i;

                case ItemType.Laun:
                    i = Give(p, ItemType.Rocket);
                    i |= Give(p, ItemType.Rocket);
                    if ((p.Wpns & 64) == 0) { p.Wpns |= 64; i = 1; }
                    p.DrawSt |= PL_DRAWWPN; return i;

                case ItemType.Plas:
                    i = Give(p, ItemType.Cell);
                    if ((p.Wpns & 128) == 0) { p.Wpns |= 128; i = 1; }
                    p.DrawSt |= PL_DRAWWPN; return i;

                case ItemType.BFG:
                    i = Give(p, ItemType.Cell);
                    if ((p.Wpns & 256) == 0) { p.Wpns |= 256; i = 1; }
                    p.DrawSt |= PL_DRAWWPN; return i;

                case ItemType.Arm1:
                    if (p.Armor >= 100) return 0;
                    p.Armor = 100; p.DrawSt |= PL_DRAWARMOR; return 1;

                case ItemType.Arm2:
                    if (p.Armor >= 200) return 0;
                    p.Armor = 200; p.DrawSt |= PL_DRAWARMOR; return 1;

                case ItemType.Mega:
                    i = 0;
                    if (p.Life < 200) { p.Life = 200; p.DrawSt |= PL_DRAWLIFE; i = 1; }
                    if (p.Armor < 200) { p.Armor = 200; p.DrawSt |= PL_DRAWARMOR; i = 1; }
                    return i;

                case ItemType.Super:
                    if (p.Life < 200)
                    {
                        p.Life = Math.Min(p.Life + 100, 200);
                        p.DrawSt |= PL_DRAWLIFE; return 1;
                    }
                    return 0;

                case ItemType.Invl:
                    p.Invl = PowerUpTime; return 1;

                case ItemType.Suit:
                    p.Suit = PowerUpTime; return 1;

                case ItemType.Aqua:
                    if (p.Air >= PlAquaAir) return 0;
                    p.Air = PlAquaAir; p.DrawSt |= PL_DRAWAIR;
                    return 1;

                case ItemType.KeyR:
                    if ((p.Keys & 16) != 0) return 0;
                    p.Keys |= 16; p.DrawSt |= PL_DRAWKEYS; return 1;

                case ItemType.KeyG:
                    if ((p.Keys & 32) != 0) return 0;
                    p.Keys |= 32; p.DrawSt |= PL_DRAWKEYS; return 1;

                case ItemType.KeyB:
                    if ((p.Keys & 64) != 0) return 0;
                    p.Keys |= 64; p.DrawSt |= PL_DRAWKEYS; return 1;

                default:
                    return 0;
            }
        }

        public static void Act(PlayerData p)
        {
            int st;

            if (--aitime < 0) aitime = 0;

            Switches.Press(p.O.X, p.O.Y, p.O.R, p.O.H, (byte)(4 | p.Keys), p.Id);

            if (p.Suit == 0 && (Game.Time & 15) == 0)
                HitPlayer(p, Misc.GetAcid(p.O.X, p.O.Y, p.O.R, p.O.H), -3, Doom2D.Game.Hit.Some);

            if (p.St != Fall && p.St != Out)
            {
                st = Misc.MoveObj(p.O);
                if ((st & Z.FallOut) != 0 && p.O.Y >= World.FLDH * World.CELW + 50)
                {
                    switch (p.St)
                    {
                        case Dead: case Mess: case Die: case Slop:
                            p.S = 5; break;
                        default:
                            p.S = Misc.Sound(snd[10], 128);
                            if (Game.Dm) --p.Frag;
                            break;
                    }
                    p.St = Fall;
                }
            }
            else st = 0;

            if ((st & Z.HitWater) != 0) Misc.Splash(p.O, PlRad + PlHt);
            if ((p.F & PLF_FIRE) != 0 && p.Fire != 2) p.F -= PLF_FIRE;

            if (Kbd.Keys[p.Ku]) { p.F |= PLF_UP; p.LookY -= 5; }
            else
            {
                p.F &= unchecked((byte)~PLF_UP);
                if (Kbd.Keys[p.Kd]) { p.F |= PLF_DOWN; p.LookY += 5; }
                else { p.F &= unchecked((byte)~PLF_DOWN); p.LookY = Misc.Dec(p.LookY, 5); }
            }

            if (Kbd.Keys[p.Kp]) Switches.Press(p.O.X, p.O.Y, p.O.R, p.O.H, (byte)(1 | p.Keys), p.Id);

            if (p.Fire != 0) --p.Fire;
            if (p.CWpn != 0) --p.CWpn;
            if (p.CSnd != 0) --p.CSnd;
            if (p.Invl != 0) --p.Invl;
            if (p.Suit != 0) --p.Suit;

            switch (p.St)
            {
                case Die:
                    p.O.H = 7;
                    if (dieAnim[++p.S] == 0) { p.St = Dead; Monsters.KilledP(); }
                    p.O.XV = Misc.Dec(p.O.XV, 1);
                    break;

                case Slop:
                    p.O.H = 6;
                    if (slopAnim[++p.S] == 0) { p.St = Mess; Monsters.KilledP(); }
                    p.O.XV = Misc.Dec(p.O.XV, 1);
                    break;

                case Go:
                    ChgWpn(p); Fire(p); JumpAndBreathe(p, st);
                    if (Fly) Smoke.Gas(p.O.X, p.O.Y - 2, 2, 3, p.O.XV + p.O.VX, p.O.YV + p.O.VY, 128);
                    p.S += Math.Abs(p.O.XV) / 2;
                    if (p.S >= 24) p.S %= 24;

                    if (!Kbd.Keys[p.Kl] && !Kbd.Keys[p.Kr])
                    {
                        if (p.O.XV != 0) p.O.XV = Misc.Dec(p.O.XV, 1);
                        else p.St = Stand;
                        break;
                    }
                    if (p.O.XV < Run && Kbd.Keys[p.Kr]) { p.O.XV += Run >> 3; p.D = 1; }
                    else if (Run > 8) Smoke.Gas(p.O.X, p.O.Y - 2, 2, 3, p.O.XV + p.O.VX, p.O.YV + p.O.VY, 32);

                    if (p.O.XV > -Run && Kbd.Keys[p.Kl]) { p.O.XV -= Run >> 3; p.D = 0; }
                    else if (Run > 8) Smoke.Gas(p.O.X, p.O.Y - 2, 2, 3, p.O.XV + p.O.VX, p.O.YV + p.O.VY, 32);
                    break;

                case Stand:
                    ChgWpn(p); Fire(p); JumpAndBreathe(p, st);
                    if (Fly) Smoke.Gas(p.O.X, p.O.Y - 2, 2, 3, p.O.XV + p.O.VX, p.O.YV + p.O.VY, 128);
                    if (Kbd.Keys[p.Kl]) { p.St = Go; p.S = 0; p.D = 0; }
                    else if (Kbd.Keys[p.Kr]) { p.St = Go; p.S = 0; p.D = 1; }
                    break;

                case Dead:
                case Mess:
                case Out:
                    p.O.XV = Misc.Dec(p.O.XV, 1);
                    if (Kbd.Keys[p.Ku] || Kbd.Keys[p.Kd] || Kbd.Keys[p.Kl] || Kbd.Keys[p.Kr] ||
                        Kbd.Keys[p.Kf] || Kbd.Keys[p.Kj] || Kbd.Keys[p.Kp] || Kbd.Keys[p.Kwl] || Kbd.Keys[p.Kwr])
                    {
                        if (p.St != Out) Monsters.SpawnDeadPl(p.O, p.Color, p.St == Mess ? 1 : 0);
                        Restore(p);
                        if (Game.Dm) { Game.RespawnPlayer(p); break; }
                        if (!Game.TwoPl)
                        {
                            if (--p.Lives == 0) { Game.Start(); break; }
                            p.O.X = Game.DmPos[0].X; p.O.Y = Game.DmPos[0].Y; p.D = Game.DmPos[0].D;
                            p.DrawSt |= PL_DRAWLIVES;
                        }
                        if (p.Id == -1)
                        { p.O.X = Game.DmPos[0].X; p.O.Y = Game.DmPos[0].Y; p.D = Game.DmPos[0].D; }
                        else
                        { p.O.X = Game.DmPos[1].X; p.O.Y = Game.DmPos[1].Y; p.D = Game.DmPos[1].D; }
                    }
                    break;

                case Fall:
                    if (--p.S <= 0) p.St = Out;
                    break;
            }
        }

        static int StandSpr(PlayerData p)
        {
            if ((p.F & PLF_UP) != 0) return 'X';
            if ((p.F & PLF_DOWN) != 0) return 'Z';
            return 'E';
        }

        static int WpnSpr(PlayerData p)
        {
            if ((p.F & PLF_UP) != 0) return 'C';
            if ((p.F & PLF_DOWN) != 0) return 'E';
            return 'A';
        }

        static readonly int[] wyTab = { -1, -2, -1, 0 };

        public static void Draw(PlayerData p)
        {
            int s = 'A', w = 0, wx = 0, wy = 0;

            switch (p.St)
            {
                case Stand:
                    if ((p.F & PLF_FIRE) != 0) { s = StandSpr(p) + 1; w = WpnSpr(p) + 1; }
                    else if (p.Pain != 0) { s = 'G'; w = 'A'; wx = p.D != 0 ? 2 : -2; wy = 1; }
                    else { s = StandSpr(p); w = WpnSpr(p); }
                    break;
                case Dead:
                    s = 'N'; break;
                case Mess:
                    s = 'W'; break;
                case Go:
                    if (p.Pain != 0) { s = 'G'; w = 'A'; wx = p.D != 0 ? 2 : -2; wy = 1; }
                    else
                    {
                        s = goAnim[p.S / 8];
                        w = (p.F & PLF_FIRE) != 0 ? 'B' : 'A';
                        wx = p.D != 0 ? 2 : -2;
                        wy = 1 + wyTab[s - 'A'];
                    }
                    break;
                case Die:
                    s = dieAnim[p.S]; break;
                case Slop:
                    s = slopAnim[p.S]; break;
                case Out:
                    s = 0; break;
            }

            if (p.Wpn == 0) w = 0;
            if (w != 0) Misc.DrawSpr(p.O.X + wx, p.O.Y + wy, wpn[p.Wpn * 6 + (w - 'A')], p.D != 0);
            if (s != 0)
            {
                int idx = (s - 'A') * 2 + p.D;
                Misc.DrawManSpr(p.O.X, p.O.Y, spr[idx], sprd[idx] != 0, p.Color);
            }
        }

        public static VgaImage GetSpr(int s, int d)
        {
            return spr[(s - 'A') * 2 + d];
        }

        static void ChkBfg(PlayerData p, int x, int y)
        {
            if (aitime != 0) return;
            switch (p.St)
            {
                case Die: case Slop: case Fall: case Dead: case Mess: case Out:
                    return;
            }
            int dx = p.O.X - x;
            int dy = p.O.Y - p.O.H / 2 - y;
            if (dx * dx + dy * dy <= 1600) aitime = Misc.Sound(aisnd[Glob.Rand(3)], 128) * 4;
        }

        public static void BfgFly(int x, int y, int o)
        {
            if (o != -1) ChkBfg(Game.Pl1, x, y);
            if (Game.TwoPl && o != -2) ChkBfg(Game.Pl2, x, y);
            if (o == -1 || o == -2) Monsters.Warning(x - 50, y - 50, x + 50, y + 50);
        }

        public static void DrawSt(PlayerData p)
        {
            Vga.SetRect(World.WD, 120, World.WO, World.HT);
            Misc.ClrSt();

            if ((p.DrawSt & PL_DRAWAIR) != 0)
                if (p.Air < PlAir) Misc.DrawStAir(p.Air);

            if ((p.DrawSt & PL_DRAWLIFE) != 0) Misc.DrawStPrcnt(0, p.Life);
            if ((p.DrawSt & PL_DRAWARMOR) != 0) Misc.DrawStPrcnt(1, p.Armor);

            if ((p.DrawSt & PL_DRAWWPN) != 0)
            {
                int i = 0;
                switch (p.Wpn)
                {
                    case 2: case 5: i = p.Ammo; break;
                    case 3: case 4: case 9: i = p.Shel; break;
                    case 6: i = p.Rock; break;
                    case 10: i = p.Fuel; break;
                    case 7: case 8: i = p.Cell; break;
                }
                Misc.DrawStWpn(p.Wpn, i);
            }

            if ((p.DrawSt & PL_DRAWFRAG) != 0) Misc.DrawStNum(p.Frag);
            if ((p.DrawSt & PL_DRAWKEYS) != 0) Misc.DrawStKeys(p.Keys);
            if (!Game.TwoPl && (p.DrawSt & PL_DRAWLIVES) != 0) Misc.DrawStLives(p.Lives);
        }
    }
}
