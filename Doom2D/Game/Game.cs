using System;
using System.Collections.Generic;
using System.IO;
using Doom2D.Engine;

namespace Doom2D.Game
{
    /// <summary>game.c — the state machine, map loading, HUD and save files.</summary>
    public static class Game
    {
        public const int GsTitle = 0, GsGame = 1, GsInter = 2, GsDarken = 3, GsEndAnim = 4,
                         GsEnd2Anim = 5, GsEndScr = 6, GsBVideo = 7, GsEVideo = 8, GsEnd3Anim = 9;

        public const int PlFlash = 90;
        const int Getime = 1092;

        public static bool TwoPl;
        public static bool Dm;
        public static int State = GsTitle;
        public static int Exit;
        public static int Map = 1;
        public static int Warp;
        public static bool MusicRandom = true;
        public static int MusicTime = 3;
        public static int MusicFade = 5;
        public static byte[] MusicName = new byte[8];
        public static bool Cheat;

        public static int StateTimer = 1092;
        public static uint Time;

        public static int DmPNum, DmPl1P, DmPl2P;
        public static readonly Pos[] DmPos = new Pos[100];

        public struct Pos { public int X, Y, D; }

        public static readonly PlayerData Pl1 = new PlayerData();
        public static readonly PlayerData Pl2 = new PlayerData();

        static VgaImage[] scrnH;
        static Res.Locked telepsnd;

        public static int Trans, TransT;

        static readonly MapBlock blk = new MapBlock();

        static Game()
        {
            for (int i = 0; i < DmPos.Length; ++i) DmPos[i] = new Pos();
            MusicName[0] = (byte)'M'; MusicName[1] = (byte)'E'; MusicName[2] = (byte)'N';
            MusicName[3] = (byte)'U';
        }

        // ------------------------------------------------------------ transitions

        static void SetTrans(int st)
        {
            switch (State)
            {
                case GsEndAnim: case GsEnd2Anim: case GsDarken:
                case GsBVideo: case GsEVideo: case GsEnd3Anim:
                    State = st;
                    return;
            }
            State = st;
            switch (st)
            {
                case GsEndAnim: case GsEnd2Anim: case GsDarken:
                case GsBVideo: case GsEVideo: case GsEnd3Anim:
                    return;
            }
            Trans = 1; TransT = 0;
        }

        // ------------------------------------------------------------- save files

        static void SaveOne(BinWriter w)
        {
            w.Byte((byte)(TwoPl ? 1 : 0)); w.Byte((byte)(Dm ? 1 : 0));
            w.Byte((byte)Exit); w.Byte((byte)Map);
            w.Int((int)Time); w.Int(DmPl1P); w.Int(DmPl2P);
            w.Int(DmPNum);
            for (int i = 0; i < DmPNum && i < DmPos.Length; ++i)
            {
                w.Int(DmPos[i].X); w.Int(DmPos[i].Y); w.Byte((byte)DmPos[i].D);
            }
            w.Byte((byte)(Cheat ? 1 : 0));
            w.Bytes(MusicName);

            World.Save(w);
            Dots.Save(w);
            Smoke.Save(w);
            Fx.Save(w);
            Items.Save(w);
            Monsters.Save(w);
            Player.Save(w);
            Switches.Save(w);
            Weapons.Save(w);
        }

        static void LoadOne(BinReader r)
        {
            TwoPl = r.Byte() != 0; Dm = r.Byte() != 0;
            Exit = r.Byte(); Map = r.Byte();
            Time = (uint)r.Int(); DmPl1P = r.Int(); DmPl2P = r.Int();
            DmPNum = r.Int();
            for (int i = 0; i < DmPNum && i < DmPos.Length; ++i)
            {
                Pos p = new Pos();
                p.X = r.Int(); p.Y = r.Int(); p.D = r.Byte();
                DmPos[i] = p;
            }
            Cheat = r.Byte() != 0;
            r.ReadInto(MusicName);

            World.Load(r);
            Dots.Load(r);
            Smoke.Load(r);
            Fx.Load(r);
            Items.Load(r);
            Monsters.Load(r);
            Player.Load(r);
            Switches.Load(r);
            Weapons.Load(r);
        }

        public static void SaveGame(int slot, string name)
        {
            BinWriter w = new BinWriter();
            byte[] nm = new byte[24];
            byte[] src = System.Text.Encoding.GetEncoding(1251).GetBytes(name ?? "");
            Array.Copy(src, nm, Math.Min(24, src.Length));
            w.Bytes(nm);
            w.Short(3);
            SaveOne(w);
            File.WriteAllBytes(SavePath(slot), w.ToArray());
        }

        public static bool LoadGame(int slot)
        {
            string path = SavePath(slot);
            if (!File.Exists(path)) return false;

            byte[] d = File.ReadAllBytes(path);
            BinReader r = new BinReader(d, 24);
            if (r.Short() != 3) return false;

            LoadOne(r);
            return true;
        }

        public static string SavePath(int slot)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage", "Saves");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "savgame" + slot + ".dat");
        }

        /// <summary>F_getsavnames: the seven slots and whether each holds a version 3 save.</summary>
        public static void GetSavNames(out string[] names, out bool[] ok)
        {
            names = new string[7];
            ok = new bool[7];

            for (int i = 0; i < 7; ++i)
            {
                names[i] = "";
                ok[i] = false;
                string p = SavePath(i);
                if (!File.Exists(p)) continue;

                byte[] d = File.ReadAllBytes(p);
                if (d.Length < 26) continue;

                names[i] = System.Text.Encoding.GetEncoding(1251).GetString(d, 0, 24).TrimEnd('\0');
                ok[i] = BitConverter.ToInt16(d, 24) == 3;
            }
        }

        // -------------------------------------------------------------- map loading

        public static bool LoadBlock(byte[] d, ref int pos, MapBlock b)
        {
            // The C code tries G_load, W_load, IT_load, SW_load in that order.
            if (b.T == MapBlockType.Music)
            {
                Buffer.BlockCopy(d, pos, MusicName, 0, 8);
                pos += 8;
                string n = Latin1(MusicName);
                if (MusicRandom) n = Wad.RandMus(n);
                Music.Load(n);
                return true;
            }
            if (World.Load(d, ref pos, b)) return true;
            if (Items.Load(new BinReader(d, pos), b)) { pos += b.Sz; return true; }
            if (Switches.Load(new BinReader(d, pos), b)) { pos += b.Sz; return true; }
            return false;
        }

        static string Latin1(byte[] b)
        {
            int n = 0;
            while (n < b.Length && b[n] != 0) ++n;
            return System.Text.Encoding.GetEncoding(28591).GetString(b, 0, n);
        }

        public static void LoadMap(string name)
        {
            World.Init();

            int r = Wad.GetResId(name);
            byte[] d = Wad.LoadRes(r);

            // map_header_t: char id[8] = "Doom2D\x1A", short ver.
            if (d.Length < 10
                || System.Text.Encoding.GetEncoding(28591).GetString(d, 0, 6) != "Doom2D"
                || d[6] != 0x1A)
                throw new Fatal(name + " не является уровнем");

            int pos = 10;
            while (pos + 8 <= d.Length)
            {
                blk.T = BitConverter.ToInt16(d, pos);
                blk.St = BitConverter.ToInt16(d, pos + 2);
                blk.Sz = BitConverter.ToInt32(d, pos + 4);
                pos += 8;

                if (blk.T == MapBlockType.End) break;
                if (blk.T == MapBlockType.Comment) { pos += blk.Sz; continue; }

                int end = pos + blk.Sz;
                if (!LoadBlock(d, ref pos, blk))
                    throw new Fatal(string.Format("Неизвестный блок {0}({1}) в уровне {2}", blk.T, blk.St, name));
                pos = end;
            }
        }

        // ------------------------------------------------------------------ startup

        public static void Init()
        {
            scrnH = new VgaImage[3];
            scrnH[0] = Res.Img(Wad.GetResId("TITLEPIC"));
            scrnH[1] = Res.Img(Wad.GetResId("INTERPIC"));
            scrnH[2] = Res.Img(Wad.GetResId("ENDPIC"));

            telepsnd = Misc.GetSnd("TELEPT");
            World.LoadLightning();

            Dots.Alloc();
            Smoke.Alloc();
            Fx.Alloc();
            Weapons.Alloc();
            Items.Alloc();
            Switches.Alloc();
            Player.Alloc();
            Monsters.Alloc();
            Misc.InitSt();

            Menu.Init();

            // main.c: the default bindings, applied before the config file is read.
            Pl1.Ku = Kbd.SDLK_KP8;
            Pl1.Kd = Kbd.SDLK_KP5;
            Pl1.Kl = Kbd.SDLK_KP4;
            Pl1.Kr = Kbd.SDLK_KP6;
            Pl1.Kf = Kbd.SDLK_PAGEDOWN;
            Pl1.Kj = Kbd.SDLK_DELETE;
            Pl1.Kwl = Kbd.SDLK_HOME;
            Pl1.Kwr = Kbd.SDLK_END;
            Pl1.Kp = Kbd.SDLK_KP8;
            Pl1.Id = -1;

            Pl2.Ku = 'e';
            Pl2.Kd = 'd';
            Pl2.Kl = 's';
            Pl2.Kr = 'f';
            Pl2.Kf = 'a';
            Pl2.Kj = 'q';
            Pl2.Kwl = '1';
            Pl2.Kwr = '2';
            Pl2.Kp = 'e';
            Pl2.Id = -2;

            Pl1.Color = 0x70;
            Pl2.Color = 0x60;
            Trans = 0;
        }

        public static void Start()
        {
            Music.Free();
            LoadMap(string.Format("MAP{0:00}", Map));

            SetTrans(GsGame);
            Vga.SetRect(0, Vga.SCRW, 0, Vga.SCRH);
            Vga.Clr(0, Vga.SCRW, 0, Vga.SCRH, 0);

            if (TwoPl)
            {
                World.WO = 0; Misc.ClrSt();
                World.WO = Vga.SCRH / 2; Misc.ClrSt();
            }
            else { World.WO = 0; Misc.ClrSt(); }

            Pl1.DrawSt = 0xFF;
            if (TwoPl) Pl2.DrawSt = 0xFF;

            Exit = 0;
            Items.RTime = Dm ? 1092 : 0;
            Player.Immortal = false;
            Player.Jump = 10;
            Time = 0;
            World.LtTime = 1000;
            World.LtForce = true;
            if (!TwoPl) Pl1.Lives = 3;

            Bmap.RemapFld();
            Bmap.Clear(Bmap.Plr1 | Bmap.Plr2 | Bmap.Monster);
            Bmap.Mark(Pl1.O, Bmap.Plr1);
            if (TwoPl) Bmap.Mark(Pl2.O, Bmap.Plr2);
            Monsters.Mark();

            Music.Start(MusicTime);
        }

        public static void LoadSavedGame(int slot)
        {
            Music.Free();
            World.Init();
            if (!LoadGame(slot)) return;

            SetTrans(GsGame);
            Vga.SetRect(0, Vga.SCRW, 0, Vga.SCRH);
            Vga.Clr(0, Vga.SCRW, 0, Vga.SCRH, 0);

            if (TwoPl)
            {
                World.WO = 0; Misc.ClrSt();
                World.WO = Vga.SCRH / 2; Misc.ClrSt();
            }
            else { World.WO = 0; Misc.ClrSt(); }

            Pl1.DrawSt = 0xFF;
            if (TwoPl) Pl2.DrawSt = 0xFF;

            Bmap.RemapFld();
            Bmap.Clear(Bmap.Plr1 | Bmap.Plr2 | Bmap.Monster);
            Bmap.Mark(Pl1.O, Bmap.Plr1);
            if (TwoPl) Bmap.Mark(Pl2.O, Bmap.Plr2);
            Monsters.Mark();

            Music.Start(MusicTime);
        }

        // -------------------------------------------------------------------- ticks

        public static void Act()
        {
            Trans = 0;

            if (Menu.Act()) return;

            switch (State)
            {
                case GsTitle:
                case GsEndScr:
                    return;

                case GsInter:
                    if (Kbd.Keys[Kbd.SDLK_SPACE] || Kbd.Keys[Kbd.SDLK_RETURN] || Kbd.Keys[Kbd.SDLK_KP_ENTER])
                        Start();
                    return;
            }

            if (World.SkyType == 2)
            {
                if (World.LtTime > World.LtDelay || World.LtForce)
                {
                    if ((Glob.Rand(32) & 31) == 0 || World.LtForce)
                    {
                        World.LtForce = false;
                        World.LtTime = -World.LtHitTime;
                        World.LtType = Glob.Rand(2);
                        World.LtSide = Glob.Rand(2) & 1;
                        World.LtYPos = Glob.Rand(32) & 31;
                        Misc.Sound(World.LtSnd(Glob.Rand(2) & 1), 128);
                    }
                }
                else ++World.LtTime;
            }

            ++Time;

            Pl1.Hit = 0; Pl1.HitO = -3;
            if (TwoPl) { Pl2.Hit = 0; Pl2.HitO = -3; }

            Menu.Code();

            World.Act();
            Items.Act();
            Switches.Act();

            if (TwoPl)
            {
                if (pcnt) { Player.Act(Pl1); Player.Act(Pl2); }
                else { Player.Act(Pl2); Player.Act(Pl1); }
                pcnt = !pcnt;
            }
            else Player.Act(Pl1);

            Monsters.Act();

            if (Bmap.NeedRemap) Bmap.RemapFld();
            Bmap.Clear(Bmap.Plr1 | Bmap.Plr2 | Bmap.Monster);
            Bmap.Mark(Pl1.O, Bmap.Plr1);
            if (TwoPl) Bmap.Mark(Pl2.O, Bmap.Plr2);
            Monsters.Mark();

            Weapons.Act();
            Dots.Act();
            Smoke.Act();
            Fx.Act();

            if (TwoPl)
            {
                Player.Damage(Pl1); Player.Damage(Pl2);
                if ((Pl1.F & Player.PLF_PNSND) == 0 && Pl1.Pain != 0) Player.Cry(Pl1);
                if ((Pl2.F & Player.PLF_PNSND) == 0 && Pl2.Pain != 0) Player.Cry(Pl2);
                if ((Pl1.Pain -= 5) < 0) { Pl1.Pain = 0; Pl1.F &= unchecked((byte)~Player.PLF_PNSND); }
                if ((Pl2.Pain -= 5) < 0) { Pl2.Pain = 0; Pl2.F &= unchecked((byte)~Player.PLF_PNSND); }
            }
            else
            {
                Player.Damage(Pl1);
                if ((Pl1.F & Player.PLF_PNSND) == 0 && Pl1.Pain != 0) Player.Cry(Pl1);
                if ((Pl1.Pain -= 5) < 0) { Pl1.Pain = 0; Pl1.F &= unchecked((byte)~Player.PLF_PNSND); }
            }

            if (Exit == 1)
            {
                switch (Map)
                {
                    case 19: State = GsEndAnim; break;
                    case 31:
                    case 32: Map = 16; SetTrans(GsInter); break;
                    default: ++Map; SetTrans(GsInter); break;
                }
                Music.Free();
                if (State == GsInter) Music.Load("INTERMUS");
                else { Music.Load("\u008A\u008E\u008D\u0085\u0096"); Music.Volume(128); }
                Music.Start(0);
            }
            else if (Exit == 2)
            {
                switch (Map)
                {
                    case 31: Map = 32; SetTrans(GsInter); break;
                    case 32: Map = 16; SetTrans(GsInter); break;
                    default: Map = 31; SetTrans(GsInter); break;
                }
                Music.Free();
                Music.Load("INTERMUS");
                Music.Start(0);
            }
        }

        static bool pcnt;

        public static void RespawnPlayer(PlayerData p)
        {
            int i;
            if (DmPNum == 2)
            {
                if (p == Pl1) { DmPl1P ^= 1; i = DmPl1P; }
                else { DmPl2P ^= 1; i = DmPl2P; }
                p.O.X = DmPos[i].X; p.O.Y = DmPos[i].Y; p.D = DmPos[i].D;
                Fx.Tfog(DmPos[i].X, DmPos[i].Y);
                Misc.Sound(telepsnd, 128);
                return;
            }
            do { i = Glob.Rand(DmPNum); } while (i == DmPl1P || i == DmPl2P);
            p.O.X = DmPos[i].X; p.O.Y = DmPos[i].Y; p.D = DmPos[i].D;
            if (p == Pl1) DmPl1P = i; else DmPl2P = i;
            Fx.Tfog(DmPos[i].X, DmPos[i].Y);
            Misc.Sound(telepsnd, 128);
        }

        // -------------------------------------------------------------------- drawing

        static void DrawView(PlayerData p)
        {
            if (p.LookY < -Vga.SCRH / 4) p.LookY = -Vga.SCRH / 4;
            else if (p.LookY > Vga.SCRH / 4) p.LookY = Vga.SCRH / 4;

            World.WX = p.O.X;
            World.WY = p.O.Y - 12 + p.LookY;
            World.Draw();
            Player.DrawSt(p);
        }

        static int GetPuSt(int t)
        {
            if (t >= PlFlash) return 1;
            if (((t / 9) & 1) != 0) return 0;
            return 1;
        }

        static void PlInfo(PlayerData p, int y)
        {
            uint t = (uint)(p.Kills * 10920 / (int)(Time == 0 ? 1 : Time));
            Misc.Gotoxy(25, y); Misc.PrintBf("KILLS");
            Misc.Gotoxy(25, y + 15); Misc.PrintBf("KPM");
            Misc.Gotoxy(25, y + 30); Misc.PrintBf("SECRETS {0} / {1}", p.Secrets, Switches.Secrets);
            Misc.Gotoxy(255, y); Misc.PrintBf("{0}", p.Kills);
            Misc.Gotoxy(255, y + 15); Misc.PrintBf("{0}.{1}", t / 10, t % 10);
        }

        public static void Draw()
        {
            if (Trans != 0) return;

            switch (State)
            {
                case GsEndAnim: case GsEnd2Anim: case GsDarken:
                case GsBVideo: case GsEVideo: case GsEnd3Anim:
                    return;

                case GsTitle:
                    Vga.Center(true);
                    Vga.Pic(0, 0, scrnH[0]);
                    Vga.Center(false);
                    break;

                case GsEndScr:
                    Vga.Center(true);
                    Vga.Clr(0, Vga.SCRW, 0, Vga.SCRH, 0);
                    Vga.Pic(0, 0, scrnH[2]);
                    Vga.Center(false);
                    break;

                case GsInter:
                    Vga.Center(true);
                    Vga.Clr(0, Vga.SCRW, 0, Vga.SCRH, 0);
                    Vga.Pic(0, 0, scrnH[1]);
                    Misc.Gotoxy(60, 20); Misc.PrintBf("LEVEL COMPLETE");

                    int hr, mn, sc;
                    Misc.CalcTime(Time, out hr, out mn, out sc);
                    Misc.Gotoxy(115, 40); Misc.PrintBf("TIME {0}:{1:00}:{2:00}", hr, mn, sc);

                    int h = 60;
                    if (TwoPl)
                    {
                        Misc.Gotoxy(80, h); Misc.PrintBf("PLAYER ONE");
                        Misc.Gotoxy(80, h + 70); Misc.PrintBf("PLAYER TWO");
                        h += Vga.SCRH / 10;
                    }
                    PlInfo(Pl1, h);
                    if (TwoPl) PlInfo(Pl2, h + 70);
                    Vga.Center(false);
                    break;
            }

            Vga.Center(true);
            if (State != GsGame)
            {
                Menu.Draw();
                return;
            }
            Vga.Center(false);

            if (TwoPl)
            {
                World.WO = 0; World.WD = Vga.SCRW - 120; World.HT = Vga.SCRH / 2 - 2;
                DrawView(Pl1);
                World.WO = Vga.SCRH / 2; World.WD = Vga.SCRW - 120; World.HT = Vga.SCRH / 2 - 2;
                DrawView(Pl2);
            }
            else
            {
                World.WO = 0; World.WD = Vga.SCRW - 120; World.HT = Vga.SCRH - 2;
                DrawView(Pl1);
            }

            Vga.Center(true);
            if (Menu.Draw())
            {
                Pl1.DrawSt = 0xFF;
                Pl2.DrawSt = 0xFF;
                return;
            }
            Vga.Center(false);

            int flash;
            if (Pl1.Invl != 0) flash = GetPuSt(Pl1.Invl) * 6;
            else if (Pl1.Pain < 15) flash = 0;
            else if (Pl1.Pain < 35) flash = 1;
            else if (Pl1.Pain < 55) flash = 2;
            else if (Pl1.Pain < 75) flash = 3;
            else if (Pl1.Pain < 95) flash = 4;
            else flash = 5;

            if (flash != 0)
                Vga.RemapRect(0, 1, Vga.SCRW - 120, TwoPl ? Vga.SCRH / 2 - 2 : Vga.SCRH - 2,
                    Palette.ClrMap, flash);

            Pl1.DrawSt = 0xFF;

            if (TwoPl)
            {
                if (Pl2.Invl != 0) flash = GetPuSt(Pl2.Invl) * 6;
                else if (Pl2.Pain < 15) flash = 0;
                else if (Pl2.Pain < 35) flash = 1;
                else if (Pl2.Pain < 55) flash = 2;
                else if (Pl2.Pain < 75) flash = 3;
                else if (Pl2.Pain < 95) flash = 4;
                else flash = 5;

                if (flash != 0)
                    Vga.RemapRect(0, Vga.SCRH / 2 + 1, Vga.SCRW - 120, Vga.SCRH / 2 - 2,
                        Palette.ClrMap, flash);

                Pl2.DrawSt = 0xFF;
            }
        }
    }
}
