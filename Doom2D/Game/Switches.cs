using System;
using Doom2D.Engine;

namespace Doom2D.Game
{
    /// <summary>switch.h — switch types.</summary>
    public static class SwType
    {
        public const int None = 0, Exit = 1, Exits = 2, OpenDoor = 3, ShutDoor = 4, ShutTrap = 5;
        public const int Door = 6, Door5 = 7, Press = 8, Tele = 9, Secret = 10, LiftUp = 11,
                         LiftDown = 12, Trap = 13, Lift = 14;
    }

    /// <summary>
    /// switch.c — doors, lifts, traps and teleports.
    ///
    /// A "door" here is a flood fill over identical cells, which is why the
    /// recursion in door() has to stay: Doom2D maps mark a whole door leaf with
    /// the same cell value.
    /// </summary>
    public static class Switches
    {
        public const int MaxSw = 100;

        sealed class Sw
        {
            public byte X, Y;
            public byte T, Tm;
            public byte A, B, C, D;
            public byte F;
        }

        static readonly Sw[] sw = new Sw[MaxSw];
        static Res.Locked sndSwn, sndSwx, sndNoWay, sndBdo, sndBdc, sndNotele;
        static int swsnd;

        public static int Secrets;

        static Switches()
        {
            for (int i = 0; i < MaxSw; ++i) sw[i] = new Sw();
        }

        public static void Save(BinWriter w)
        {
            int n = MaxSw;
            while (--n > 0 && sw[n].T == 0) { }
            ++n;
            w.Int(n);
            for (int i = 0; i < n; ++i)
            {
                w.Byte(sw[i].X); w.Byte(sw[i].Y); w.Byte(sw[i].T); w.Byte(sw[i].Tm);
                w.Byte(sw[i].A); w.Byte(sw[i].B); w.Byte(sw[i].C); w.Byte(sw[i].D);
                w.Byte(sw[i].F);
            }
            w.Int(Secrets);
        }

        public static void Load(BinReader r)
        {
            Init();
            int n = r.Int();
            for (int i = 0; i < n && i < MaxSw; ++i)
            {
                sw[i].X = r.Byte(); sw[i].Y = r.Byte(); sw[i].T = r.Byte(); sw[i].Tm = r.Byte();
                sw[i].A = r.Byte(); sw[i].B = r.Byte(); sw[i].C = r.Byte(); sw[i].D = r.Byte();
                sw[i].F = r.Byte();
            }
            Secrets = r.Int();
        }

        public static bool Load(BinReader r, MapBlock blk)
        {
            if (blk.T != MapBlockType.Switch2) return false;

            Secrets = 0;
            for (int i = 0; i < MaxSw && blk.Sz > 0; ++i, blk.Sz -= 9)
            {
                sw[i].X = r.Byte(); sw[i].Y = r.Byte(); sw[i].T = r.Byte(); sw[i].Tm = r.Byte();
                sw[i].A = r.Byte(); sw[i].B = r.Byte(); sw[i].C = r.Byte(); sw[i].D = r.Byte();
                sw[i].F = r.Byte();

                sw[i].Tm = 0; sw[i].D = 0;
                sw[i].F |= 0x80;
                if (sw[i].T == SwType.Secret) ++Secrets;
            }
            return true;
        }

        public static void Alloc()
        {
            sndSwn = Misc.GetSnd("SWTCHN");
            sndSwx = Misc.GetSnd("SWTCHX");
            sndNoWay = Misc.GetSnd("NOWAY");
            sndBdo = Misc.GetSnd("BDOPN");
            sndBdc = Misc.GetSnd("BDCLS");
            sndNotele = Misc.GetSnd("NOTELE");
        }

        public static void Init()
        {
            for (int i = 0; i < MaxSw; ++i) sw[i].T = 0;
            swsnd = 0;
        }

        static byte cht, chto, chf;
        static bool fCh;

        static void Door(byte x, byte y)
        {
            if (x >= World.FLDW || y >= World.FLDH) return;
            if (World.Fld[y * World.FLDW + x] != cht) return;

            int x0 = x, xe = x + 1;
            for (; x0 > 0 && World.Fld[y * World.FLDW + x0 - 1] == cht; --x0) { }
            for (; xe < World.FLDW && World.Fld[y * World.FLDW + xe] == cht; ++xe) { }

            for (int i = x0; i < xe; ++i)
            {
                World.Fld[y * World.FLDW + i] = chto;
                if (fCh) World.FldF[y * World.FLDW + i] = chf;
            }
            for (int i = x0; i < xe; ++i)
            {
                if (y > 0) Door((byte)i, (byte)(y - 1));
                Door((byte)i, (byte)(y + 1));
            }
        }

        public static void WaterTrap(Obj o)
        {
            int y = o.Y;
            if (y >= World.FLDH * World.CELH + o.H) return;
            int x = o.X;
            if (x < 0 || o.X > World.FLDW * World.CELW) return;

            int sx = (x - o.R) / World.CELW;
            int sy = (y - o.H + 1) / World.CELH;
            int xe = (x + o.R) / World.CELW;
            int ye = (y - o.H / 2) / World.CELH;

            for (int i = sx; i <= xe; ++i)
                for (int j = sy; j <= ye; ++j)
                {
                    if (i < 0 || i >= World.FLDW || j < 0 || j >= World.FLDH) continue;
                    if (World.Fld[j * World.FLDW + i] == 5)
                    {
                        cht = 5; chto = 255; fCh = false;
                        Door((byte)i, (byte)j);
                    }
                }
        }

        public static void Untrap(byte t)
        {
            for (int i = 0; i < World.Fld.Length; ++i)
                if (World.Fld[i] == 255) World.Fld[i] = t;
        }

        static void OpenDoor(int i)
        {
            swsnd = Misc.Sound(sndBdo, 128);
            byte j = World.FldF[sw[i].B * World.FLDW + sw[i].A];
            cht = 2; chto = 3; chf = 0; fCh = true;
            Door(sw[i].A, sw[i].B);
            World.FldF[sw[i].B * World.FLDW + sw[i].A] = j;
            Bmap.NeedRemap = true;
        }

        static bool ShutDoor(int i)
        {
            cht = 3; chto = 255; chf = World.FldF[sw[i].B * World.FLDW + sw[i].A]; fCh = true;
            Door(sw[i].A, sw[i].B);
            cht = 255;
            if (ChkTrap(0, 0, -3, Hit.Some) != 0)
            {
                byte j = World.FldF[sw[i].B * World.FLDW + sw[i].A];
                chto = 3; chf = 0; fCh = true;
                Door(sw[i].A, sw[i].B);
                World.FldF[sw[i].B * World.FLDW + sw[i].A] = j;
                return false;
            }
            chto = 2;
            Door(sw[i].A, sw[i].B);
            Bmap.NeedRemap = true;
            swsnd = Misc.Sound(sndBdc, 128);
            return true;
        }

        public static void Act()
        {
            if (swsnd != 0) --swsnd;

            for (int i = 0; i < MaxSw; ++i)
            {
                if (sw[i].T == 0) continue;
                if (sw[i].Tm != 0) --sw[i].Tm;

                switch (sw[i].T)
                {
                    case SwType.Door5:
                    case SwType.Door:
                    case SwType.ShutDoor:
                        if (sw[i].D == 0) break;
                        if (World.Fld[sw[i].B * World.FLDW + sw[i].A] != 3) { sw[i].D = 0; break; }
                        if (--sw[i].D == 0) if (!ShutDoor(i)) sw[i].D = 9;
                        break;
                    case SwType.Trap:
                        if (sw[i].D == 0) break;
                        if (World.Fld[sw[i].B * World.FLDW + sw[i].A] != 2) { sw[i].D = 0; break; }
                        if (--sw[i].D == 0) { OpenDoor(i); sw[i].Tm = 18; }
                        break;
                }
            }
        }

        static int DoorTime(int t)
        {
            if (t == SwType.Door5) return 90;
            return 0;
        }

        public static void CheatOpen()
        {
            for (int i = 0; i < MaxSw; ++i)
                if (sw[i].T != 0 && sw[i].Tm == 0)
                    switch (sw[i].T)
                    {
                        case SwType.Door:
                        case SwType.Door5:
                        case SwType.OpenDoor:
                            if (World.Fld[sw[i].B * World.FLDW + sw[i].A] != 2) break;
                            Press(sw[i].X * World.CELW + 4, sw[i].Y * World.CELH + 4, 1, 1, 0xFF, -3);
                            break;
                    }
        }

        public static int Press(int x, int y, int r, int h, byte t, int o)
        {
            int sx = (x - r) / World.CELW;
            int sy = (y - h + 1) / World.CELH;
            int xe = (x + r) / World.CELW;
            int ye = y / World.CELH;
            int p = 0;

            for (int i = 0; i < MaxSw; ++i)
            {
                if (sw[i].T == 0 || sw[i].Tm != 0) continue;

                if (!(sw[i].X >= sx && sw[i].X <= xe && sw[i].Y >= sy && sw[i].Y <= ye)) continue;
                if (((sw[i].F & 0x8F) & t) == 0) continue;
                if ((sw[i].F & 0x70) != 0 && (sw[i].F & (t & 0x70)) != (sw[i].F & 0x70)) continue;

                int cell = sw[i].B * World.FLDW + sw[i].A;

                switch (sw[i].T)
                {
                    case SwType.Exit:
                        Game.Exit = 1; sw[i].Tm = 9; swsnd = Misc.Sound(sndSwx, 128); break;
                    case SwType.Exits:
                        Game.Exit = 2; sw[i].Tm = 9; swsnd = Misc.Sound(sndSwx, 128); break;
                    case SwType.Door:
                    case SwType.Door5:
                        switch (World.Fld[cell])
                        {
                            case 2:
                                OpenDoor(i); sw[i].Tm = 9; sw[i].D = (byte)DoorTime(sw[i].T); break;
                            case 3:
                                if (ShutDoor(i)) { sw[i].Tm = 9; sw[i].D = 0; }
                                else
                                {
                                    if (swsnd == 0) swsnd = Misc.Sound(sndNoWay, 128);
                                    sw[i].D = 2;
                                }
                                break;
                        }
                        break;
                    case SwType.Press:
                        sw[i].Tm = 9;
                        Press(sw[i].A * 8 + 4, sw[i].B * 8 + 12, 8, 16, (byte)((t & 0x70) | 0x80), o);
                        break;
                    case SwType.Tele:
                        if (o < -2) break;
                        if (Misc.CanFit(sw[i].A * 8 + 4, sw[i].B * 8 + 7, r, h) == 0)
                        {
                            if (swsnd == 0) swsnd = Misc.Sound(sndNotele, 128);
                            break;
                        }
                        Monsters.TeleObj(o, sw[i].A * 8 + 4, sw[i].B * 8 + 7);
                        sw[i].Tm = 1;
                        break;
                    case SwType.OpenDoor:
                        if (World.Fld[cell] != 2) break;
                        OpenDoor(i);
                        sw[i].Tm = 1;
                        break;
                    case SwType.ShutDoor:
                        if (World.Fld[cell] != 3) break;
                        if (ShutDoor(i)) { sw[i].Tm = 1; sw[i].D = 0; }
                        else
                        {
                            if (swsnd == 0) swsnd = Misc.Sound(sndNoWay, 128);
                            sw[i].D = 2;
                        }
                        break;
                    case SwType.ShutTrap:
                    case SwType.Trap:
                        if (World.Fld[cell] != 3) break;
                        cht = 3; chto = 255; chf = World.FldF[cell]; fCh = true;
                        Door(sw[i].A, sw[i].B);
                        ChkTrap(1, 100, -3, Hit.Trap);
                        cht = 255; chto = 2;
                        Door(sw[i].A, sw[i].B);
                        Bmap.NeedRemap = true;
                        swsnd = Misc.Sound(sndSwn, 128);
                        sw[i].Tm = 1; sw[i].D = 20;
                        break;
                    case SwType.Lift:
                        if (World.Fld[cell] == 10) { cht = 10; chto = 9; fCh = false; }
                        else if (World.Fld[cell] == 9) { cht = 9; chto = 10; fCh = false; }
                        else break;
                        Door(sw[i].A, sw[i].B);
                        Bmap.NeedRemap = true;
                        swsnd = Misc.Sound(sndSwx, 128);
                        sw[i].Tm = 9;
                        break;
                    case SwType.LiftUp:
                        if (World.Fld[cell] != 10) break;
                        cht = 10; chto = 9; fCh = false;
                        Door(sw[i].A, sw[i].B);
                        Bmap.NeedRemap = true;
                        swsnd = Misc.Sound(sndSwx, 128);
                        sw[i].Tm = 1;
                        break;
                    case SwType.LiftDown:
                        if (World.Fld[cell] != 9) break;
                        cht = 9; chto = 10; fCh = false;
                        Door(sw[i].A, sw[i].B);
                        Bmap.NeedRemap = true;
                        swsnd = Misc.Sound(sndSwx, 128);
                        sw[i].Tm = 1;
                        break;
                    case SwType.Secret:
                        if (o != -1 && o != -2) break;
                        if (o == -1) ++Game.Pl1.Secrets; else ++Game.Pl2.Secrets;
                        sw[i].Tm = 1; sw[i].T = 0;
                        break;
                }

                if (sw[i].Tm != 0)
                {
                    int at = sw[i].Y * World.FLDW + sw[i].X;
                    World.FldB[at] = World.WalSwp[World.FldB[at]];
                    p = 1;
                }
                if (sw[i].Tm == 1) sw[i].Tm = 0;
            }
            return p;
        }

        /// <summary>Z_chktrap: is anybody standing in the cells that just changed?</summary>
        public static int ChkTrap(int t, int d, int o, int ht)
        {
            return Monsters.ChkTrap(t, d, o, ht);
        }

        /// <summary>Diagnostics: puts the player on the exit switch, for the headless level test.</summary>
        public static void PlacePlayerOnExit(PlayerData p)
        {
            for (int i = 0; i < MaxSw; ++i)
                if (sw[i].T == SwType.Exit || sw[i].T == SwType.Exits)
                {
                    p.O.X = sw[i].X * World.CELW + 4;
                    p.O.Y = sw[i].Y * World.CELH + 4;
                    return;
                }
        }

        public static void Dump()
        {
            for (int i = 0; i < MaxSw; ++i)
                if (sw[i].T != 0)
                    Console.WriteLine(string.Format("sw{0}: t={1} at {2},{3} -> {4},{5} f=0x{6:X2}",
                        i, sw[i].T, sw[i].X, sw[i].Y, sw[i].A, sw[i].B, sw[i].F));
        }
    }
}
