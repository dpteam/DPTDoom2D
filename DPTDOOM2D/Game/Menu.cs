using System;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Game
{
    /// <summary>
    /// menu.c — the front end, in-game menu, cheats and save-slot naming.
    ///
    /// The original drives this from an SDL key callback that records "the last
    /// key that went down"; here Kbd.LastKey plays that role, so the state machine
    /// below reads exactly like the C switch.
    /// </summary>
    public static class Menu
    {
        // Menu item actions, in the original order: the comparisons below rely on it.
        const int Cancel = 0, NewGame = 1, LoadGame = 2, SaveGame = 3, Options = 4, QuitGame = 5,
                  Quit = 6, EndGame = 7, EndGm = 8,
                  Plr1 = 9, Plr2 = 10, Coop = 11, DmType = 12, VolumeM = 13, Gamma = 14,
                  Load = 15, Save = 16, PlColor = 17, PlcEnd = 18, MusicM = 19, Interp = 20,
                  SVolM = 21, SVolP = 22, MVolM = 23, MVolP = 24, GammaM = 25, GammaP = 26,
                  Pl1Cm = 27, Pl1Cp = 28, Pl2Cm = 29, Pl2Cp = 30;

        const int MenuType = 0, MsgType = 1;

        sealed class MenuDef
        {
            public byte Type;
            public int N, Cur, X;
            public string Ttl;
            public string[] M;
            public byte[] T;
        }

        static readonly MenuDef mainMnu = new MenuDef { Type = MenuType, N = 5, Cur = 0, X = 80, Ttl = "MENU" };
        static readonly MenuDef optMnu = new MenuDef { Type = MenuType, N = 5, Cur = 0, X = 75, Ttl = "OPTIONS" };
        static readonly MenuDef ngplrMnu = new MenuDef { Type = MenuType, N = 2, Cur = 0, X = 90, Ttl = "NEW GAME" };
        static readonly MenuDef ngdmMnu = new MenuDef { Type = MenuType, N = 2, Cur = 0, X = 90, Ttl = "GAME TYPE" };
        static readonly MenuDef volMnu = new MenuDef { Type = MenuType, N = 2, Cur = 0, X = 40, Ttl = "VOLUME" };
        static readonly MenuDef plcolorMnu = new MenuDef { Type = MenuType, N = 2, Cur = 0, X = 90, Ttl = "COLOR" };
        static readonly MenuDef gammaMnu = new MenuDef { Type = MenuType, N = 1, Cur = 0, X = 85, Ttl = "BRIGHTNESS" };
        static readonly MenuDef loadMnu = new MenuDef { Type = MenuType, N = 7, Cur = 0, X = 85, Ttl = "LOAD GAME" };
        static readonly MenuDef saveMnu = new MenuDef { Type = MenuType, N = 7, Cur = 0, X = 85, Ttl = "SAVE GAME" };
        static readonly MenuDef quit1 = new MenuDef { Type = MsgType, Ttl = "ARE YOU SURE?" };
        static readonly MenuDef quit2 = new MenuDef { Type = MsgType, Ttl = "ARE YOU SURE?" };
        static readonly MenuDef quit3 = new MenuDef { Type = MsgType, Ttl = "ARE YOU SURE?" };
        static readonly MenuDef endgmMsg = new MenuDef { Type = MsgType, Ttl = "RESTART LEVEL?" };

        static readonly MenuDef[] qmsg = { quit1, quit2, quit3 };

        static MenuDef mnu;

        static bool gmRedraw;
        static int gmTm;
        static Res.Locked csnd1, csnd2, msnd1, msnd2, msnd3, msnd4, msnd5, msnd6;
        static int movsndt;
        static VgaImage[] msklh;
        static VgaImage mbarl, mbarm, mbarr, mbaro, mslotl, mslotm, mslotr;

        static readonly byte[] cbuf = new byte[32];
        static byte[] ibuf = new byte[24];
        static bool input;
        static int icur;

        static readonly byte[] panim = Ascii("BBDDAACCDDAABBDDAACCDDAABBDDAACCDDAAEEEEEFEFEFEFEFEFEFEFEFEFEEEEE");
        static int panimP;

        const int PColorN = 10;
        static readonly byte[] pcolortab = { 0x18, 0x20, 0x40, 0x58, 0x60, 0x70, 0x80, 0xB0, 0xC0, 0xD0 };
        static int p1color = 5, p2color = 4;

        static string[] savName;
        static bool[] savOk;

        static byte[] Ascii(string s)
        {
            byte[] b = new byte[s.Length];
            for (int i = 0; i < s.Length; ++i) b[i] = (byte)s[i];
            return b;
        }

        static Menu()
        {
            mainMnu.M = new[] { "NEW GAME", "LOAD GAME", "SAVE GAME", "OPTIONS", "EXIT" };
            mainMnu.T = new byte[] { NewGame, LoadGame, SaveGame, Options, QuitGame };

            optMnu.M = new[] { "RESTART", "VOLUME", "BRIGHTNESS", "MUSIC", "FULLSCREEN:" };
            optMnu.T = new byte[] { EndGame, VolumeM, Gamma, MusicM, Interp };

            ngplrMnu.M = new[] { "ONE PLAYER", "TWO PLAYERS" };
            ngplrMnu.T = new byte[] { Plr1, Plr2 };

            ngdmMnu.M = new[] { "COOPERATIVE", "DEATHMATCH" };
            ngdmMnu.T = new byte[] { Coop, DmType };

            volMnu.M = new[] { "SOUND", "MUSIC" };
            volMnu.T = new byte[] { SVolM, MVolM };

            plcolorMnu.M = new[] { "FIRST", "SECOND" };
            plcolorMnu.T = new byte[] { Pl1Cm, Pl2Cm };

            gammaMnu.M = new[] { "" };
            gammaMnu.T = new byte[] { GammaM };

            loadMnu.T = new byte[] { Load, Load, Load, Load, Load, Load, Load };
            saveMnu.T = new byte[] { Save, Save, Save, Save, Save, Save, Save };

            quit1.T = quit2.T = quit3.T = new byte[] { Quit, Cancel };
            endgmMsg.T = new byte[] { EndGm, Cancel };

            savName = new string[7];
            savOk = new bool[7];
            for (int i = 0; i < 7; ++i) savName[i] = "";
        }

        // ------------------------------------------------------------------- voice

        static Res.Locked voc;
        static int vocCh;

        public static void StopVoice()
        {
            if (voc != null)
            {
                if (vocCh >= 0) Sound.Stop(vocCh);
                vocCh = -1;
                voc = null;
            }
        }

        public static void Say(string name)
        {
            int r = Wad.FindRes(name);
            if (r < 0) return;
            StopVoice();
            voc = Res.Lock(r);
            vocCh = Sound.Play(voc, -1, 1024, 255);
        }

        // ----------------------------------------------------------------- cheats

        static readonly int[] qsndRes = new int[14];

        public static void Code()
        {
            Res.Locked s = csnd2;

            if (Tail("IDDQD")) { Player.HitPlayer(Game.Pl1, 400, 0, Hit.Some); if (Game.TwoPl) Player.HitPlayer(Game.Pl2, 400, 0, Hit.Some); s = csnd1; }
            else if (Tail("TANK"))
            {
                Game.Pl1.Life = Game.Pl1.Armor = 200; Game.Pl1.DrawSt |= Player.PL_DRAWARMOR | Player.PL_DRAWLIFE;
                if (Game.TwoPl) { Game.Pl2.Life = Game.Pl2.Armor = 200; Game.Pl2.DrawSt |= Player.PL_DRAWARMOR | Player.PL_DRAWLIFE; }
            }
            else if (Tail("BULLFROG")) Player.Jump = Player.Jump == 10 ? 20 : 10;
            else if (Tail("FORMULA1")) Player.Run = Player.Run == 8 ? 24 : 8;
            else if (Tail("RAMBO"))
            {
                Game.Pl1.Ammo = Game.Pl1.Shel = Game.Pl1.Rock = Game.Pl1.Cell = Game.Pl1.Fuel = 30000;
                Game.Pl1.Wpns = 0x7FF; Game.Pl1.DrawSt |= Player.PL_DRAWWPN | Player.PL_DRAWKEYS;
                Game.Pl1.Keys = 0x70;
                if (Game.TwoPl)
                {
                    Game.Pl2.Ammo = Game.Pl2.Shel = Game.Pl2.Rock = Game.Pl2.Cell = Game.Pl2.Fuel = 30000;
                    Game.Pl2.Wpns = 0x7FF; Game.Pl2.DrawSt |= Player.PL_DRAWWPN | Player.PL_DRAWKEYS;
                    Game.Pl2.Keys = 0x70;
                }
            }
            else if (Tail("UJHTW")) Player.Immortal = !Player.Immortal;
            else if (Tail(",TKSQJHTK")) Player.Fly = !Player.Fly;
            else if (Tail("CBVCBV")) Switches.CheatOpen();
            else if (Tail("GOODBYE")) Game.Exit = 1;
            else if (Tail("GJITKYF"))
            {
                if (cbuf[30] >= '0' && cbuf[30] <= '9' && cbuf[31] >= '0' && cbuf[31] <= '9')
                {
                    Game.Map = (cbuf[30] == '0') ? 0 : (cbuf[30] - '0') * 10;
                    Game.Map += (cbuf[31] == '0') ? 0 : (cbuf[31] - '0');
                    Game.Start();
                }
            }
            else return;

            for (int i = 0; i < cbuf.Length; ++i) cbuf[i] = 0;
            Misc.Sound(s, 128);
        }

        /// <summary>memcmp(cbuf + 32 - len, text, len) == 0.</summary>
        static bool Tail(string text)
        {
            if (text.Length > cbuf.Length) return false;
            int at = cbuf.Length - text.Length;
            for (int i = 0; i < text.Length; ++i)
                if (cbuf[at + i] != (byte)text[i]) return false;
            return true;
        }

        // ------------------------------------------------------------------- menus

        static void Set(MenuDef m)
        {
            mnu = m;
            gmRedraw = true;
        }

        public static void Command(int c)
        {
            switch (c)
            {
                case Cancel:
                    Set(null);
                    break;

                case Interp:
                    Program.ToggleFullscreen();
                    Set(mnu);
                    break;

                case MusicM:
                    Music.Free();
                    Game.MusicName = Wad.NextMus(Enc(Game.MusicName)).ToByte8();
                    Music.Load(Enc(Game.MusicName));
                    Music.Start(Game.MusicTime * 2);
                    Set(mnu);
                    break;

                case NewGame:
                    Say("_NEWGAME");
                    Set(ngplrMnu);
                    break;

                case Plr2:
                    Say("_2PLAYER");
                    Set(ngdmMnu);
                    break;

                case Plr1:
                    Say("_1PLAYER");
                    ngdmMnu.Cur = 0;
                    goto case Coop;

                case Coop:
                case DmType:
                    if (c == Coop) Say("_COOP");
                    else if (c == DmType) Say("_DM");
                    if (c != Plr1) { Set(plcolorMnu); break; }
                    goto case PlcEnd;

                case PlcEnd:
                    Game.TwoPl = ngplrMnu.Cur != 0;
                    Game.Dm = ngdmMnu.Cur != 0;
                    Game.Map = Game.Warp != 0 ? Game.Warp : 1;
                    Player.Reset();
                    if (Game.TwoPl)
                    {
                        Game.Pl1.Color = pcolortab[p1color];
                        Game.Pl2.Color = pcolortab[p2color];
                    }
                    else Game.Pl1.Color = 0x70;
                    Game.Start();
                    Set(null);
                    break;

                case Options:
                    Say("_RAZNOE");
                    Set(optMnu);
                    break;

                case LoadGame:
                    Say("_OLDGAME");
                    Game.GetSavNames(out savName, out savOk);
                    Set(loadMnu);
                    break;

                case SaveGame:
                    if (Game.State != Game.GsGame) break;
                    Say("_SAVEGAM");
                    Game.GetSavNames(out savName, out savOk);
                    Set(saveMnu);
                    break;

                case Save:
                    input = true;
                    ibuf = new byte[24];
                    byte[] src = System.Text.Encoding.GetEncoding(1251).GetBytes(savName[saveMnu.Cur]);
                    Array.Copy(src, ibuf, Math.Min(23, src.Length));
                    icur = 0;
                    while (icur < 24 && ibuf[icur] != 0) ++icur;
                    Set(mnu);
                    break;

                case Load:
                    if (!savOk[loadMnu.Cur]) break;
                    Game.LoadSavedGame(loadMnu.Cur);
                    Set(null);
                    break;

                case VolumeM:
                    Say("_VOLUME");
                    Set(volMnu);
                    break;

                case Gamma:
                    Say("_GAMMA");
                    Set(gammaMnu);
                    break;

                case QuitGame:
                    Say((Glob.Rand(2) & 1) != 0 ? "_EXIT1" : "_EXIT2");
                    Set(qmsg[Glob.Rand(3)]);
                    break;

                case EndGame:
                    if (Game.State != Game.GsGame) break;
                    Say("_RESTART");
                    Set(endgmMsg);
                    break;

                case Quit:
                    Music.Free();
                    StopVoice();
                    Environment.Exit(0);
                    break;

                case EndGm:
                    Player.Reset();
                    Game.Start();
                    Set(null);
                    break;

                case Pl1Cm:
                    if (--p1color < 0) p1color = PColorN - 1;
                    break;
                case Pl1Cp:
                    if (++p1color >= PColorN) p1color = 0;
                    break;
                case Pl2Cm:
                    if (--p2color < 0) p2color = PColorN - 1;
                    break;
                case Pl2Cp:
                    if (++p2color >= PColorN) p2color = 0;
                    break;

                case SVolM:
                    Sound.Volume(Sound.SndVol - 8); break;
                case SVolP:
                    Sound.Volume(Sound.SndVol + 8); break;
                case MVolM:
                    Music.Volume(Music.MusVol - 8); break;
                case MVolP:
                    Music.Volume(Music.MusVol + 8); break;
                case GammaM:
                    Palette.SetGamma(GammaLevel - 1); break;
                case GammaP:
                    Palette.SetGamma(GammaLevel + 1); break;
            }
        }

        static string Enc(byte[] b)
        {
            int n = 0;
            while (n < b.Length && b[n] != 0) ++n;
            return System.Text.Encoding.GetEncoding(28591).GetString(b, 0, n);
        }

        public static int GammaLevel;

        /// <summary>Whether a menu is on screen (used by the headless checks).</summary>
        public static bool Open { get { return mnu != null; } }

        // ---------------------------------------------------------------- keychars

        struct KeyChar { public int Sym; public byte Ch; }

        static readonly KeyChar[] keychar = {
            new KeyChar{Sym=Kbd.SDLK_SPACE,Ch=(byte)' '},
            new KeyChar{Sym='0',Ch=(byte)'0'}, new KeyChar{Sym='1',Ch=(byte)'1'},
            new KeyChar{Sym='2',Ch=(byte)'2'}, new KeyChar{Sym='3',Ch=(byte)'3'},
            new KeyChar{Sym='4',Ch=(byte)'4'}, new KeyChar{Sym='5',Ch=(byte)'5'},
            new KeyChar{Sym='6',Ch=(byte)'6'}, new KeyChar{Sym='7',Ch=(byte)'7'},
            new KeyChar{Sym='8',Ch=(byte)'8'}, new KeyChar{Sym='9',Ch=(byte)'9'},
            new KeyChar{Sym='_',Ch=(byte)'_'},
            new KeyChar{Sym='a',Ch=(byte)'A'}, new KeyChar{Sym='b',Ch=(byte)'B'},
            new KeyChar{Sym='c',Ch=(byte)'C'}, new KeyChar{Sym='d',Ch=(byte)'D'},
            new KeyChar{Sym='e',Ch=(byte)'E'}, new KeyChar{Sym='f',Ch=(byte)'F'},
            new KeyChar{Sym='g',Ch=(byte)'G'}, new KeyChar{Sym='h',Ch=(byte)'H'},
            new KeyChar{Sym='i',Ch=(byte)'I'}, new KeyChar{Sym='j',Ch=(byte)'J'},
            new KeyChar{Sym='k',Ch=(byte)'K'}, new KeyChar{Sym='l',Ch=(byte)'L'},
            new KeyChar{Sym='m',Ch=(byte)'M'}, new KeyChar{Sym='n',Ch=(byte)'N'},
            new KeyChar{Sym='o',Ch=(byte)'O'}, new KeyChar{Sym='p',Ch=(byte)'P'},
            new KeyChar{Sym='q',Ch=(byte)'Q'}, new KeyChar{Sym='r',Ch=(byte)'R'},
            new KeyChar{Sym='s',Ch=(byte)'S'}, new KeyChar{Sym='t',Ch=(byte)'T'},
            new KeyChar{Sym='u',Ch=(byte)'U'}, new KeyChar{Sym='v',Ch=(byte)'V'},
            new KeyChar{Sym='w',Ch=(byte)'W'}, new KeyChar{Sym='x',Ch=(byte)'X'},
            new KeyChar{Sym='y',Ch=(byte)'Y'}, new KeyChar{Sym='z',Ch=(byte)'Z'},
            new KeyChar{Sym=',',Ch=(byte)','}
        };

        public static byte GetKeyChar(int sym)
        {
            for (int i = 0; i < keychar.Length; ++i)
                if (keychar[i].Sym == sym) return keychar[i].Ch;
            return 0;
        }

        // ------------------------------------------------------------------- ticks

        public static bool Act()
        {
            if (mnu == plcolorMnu)
            {
                if (++panimP >= panim.Length) panimP = 0;
                Set(mnu);
            }
            if (movsndt > 0) --movsndt; else movsndt = 0;

            int lastkey = Kbd.LastKey;

            if (Game.State == Game.GsTitle && mnu == null && lastkey != 0)
            {
                Set(mainMnu);
                Misc.Sound(msnd3, 128);
                Kbd.LastKey = 0;
                return true;
            }

            if (input)
            {
                switch (lastkey)
                {
                    case Kbd.SDLK_RETURN:
                    case Kbd.SDLK_KP_ENTER:
                        Game.SaveGame(saveMnu.Cur, CStr(ibuf));
                        input = false;
                        Set(null);
                        break;
                    case Kbd.SDLK_ESCAPE:
                        input = false;
                        Set(mnu);
                        break;
                    case Kbd.SDLK_BACKSPACE:
                        if (icur > 0) { ibuf[--icur] = 0; Set(mnu); }
                        break;
                    default:
                        if (icur >= 23) break;
                        byte ch = GetKeyChar(lastkey);
                        if (ch == 0) break;
                        ibuf[icur] = ch; ibuf[++icur] = 0;
                        Set(mnu);
                        break;
                }
            }
            else
            {
                switch (lastkey)
                {
                    case Kbd.SDLK_ESCAPE:
                        if (mnu == null) { Set(mainMnu); Misc.Sound(msnd3, 128); }
                        else { Set(null); Misc.Sound(msnd4, 128); }
                        break;

                    case Kbd.SDLK_F5:
                        if (mnu != null) break;
                        Misc.Sound(msnd3, 128);
                        Say("_GAMMA");
                        Set(gammaMnu);
                        break;

                    case Kbd.SDLK_F4:
                        if (mnu != null) break;
                        Misc.Sound(msnd3, 128);
                        Say("_VOLUME");
                        Set(volMnu);
                        break;

                    // F2 belongs to the overlay (Ui/Overlay.cs), so the save and
                    // load screens moved to F6 and F7. F1 keeps the screenshot and
                    // F3..F5 keep volume, brightness and music, as in the original.
                    case Kbd.SDLK_F6:
                        if (mnu != null) break;
                        if (Game.State != Game.GsGame) break;
                        Misc.Sound(msnd3, 128);
                        Game.GetSavNames(out savName, out savOk);
                        Set(saveMnu);
                        break;

                    case Kbd.SDLK_F7:
                        if (mnu != null) break;
                        Misc.Sound(msnd3, 128);
                        Game.GetSavNames(out savName, out savOk);
                        Set(loadMnu);
                        break;

                    case Kbd.SDLK_F10:
                        if (mnu != null) break;
                        Misc.Sound(msnd3, 128);
                        Command(QuitGame);
                        break;

                    case Kbd.SDLK_UP:
                    case Kbd.SDLK_KP8:
                        if (mnu == null || mnu.Type != MenuType) break;
                        if (--mnu.Cur < 0) mnu.Cur = mnu.N - 1;
                        Set(mnu);
                        Misc.Sound(msnd1, 128);
                        break;

                    case Kbd.SDLK_DOWN:
                    case Kbd.SDLK_KP5:
                    case Kbd.SDLK_KP2:
                        if (mnu == null || mnu.Type != MenuType) break;
                        if (++mnu.Cur >= mnu.N) mnu.Cur = 0;
                        Set(mnu);
                        Misc.Sound(msnd1, 128);
                        break;

                    case Kbd.SDLK_LEFT:
                    case Kbd.SDLK_RIGHT:
                    case Kbd.SDLK_KP4:
                    case Kbd.SDLK_KP6:
                        if (mnu == null || mnu.Type != MenuType) break;
                        if (mnu.T[mnu.Cur] < SVolM) break;
                        Command(mnu.T[mnu.Cur] + ((lastkey == Kbd.SDLK_LEFT || lastkey == Kbd.SDLK_KP4) ? 0 : 1));
                        Set(mnu);
                        if (movsndt == 0)
                            movsndt = Misc.Sound((lastkey == Kbd.SDLK_LEFT || lastkey == Kbd.SDLK_KP4) ? msnd5 : msnd6, 255);
                        break;

                    case Kbd.SDLK_RETURN:
                    case Kbd.SDLK_SPACE:
                    case Kbd.SDLK_KP_ENTER:
                        if (mnu == null || mnu.Type != MenuType) break;
                        if (mnu.T[mnu.Cur] >= Pl1Cm) { Misc.Sound(msnd2, 128); Command(PlcEnd); break; }
                        if (mnu.T[mnu.Cur] >= SVolM) break;
                        Misc.Sound(msnd2, 128);
                        Command(mnu.T[mnu.Cur]);
                        break;

                    case 'y':
                        if (mnu == null || mnu.Type != MsgType) break;
                        Misc.Sound(msnd3, 128);
                        Command(mnu.T[0]);
                        break;

                    case 'n':
                        if (mnu == null || mnu.Type != MsgType) break;
                        Misc.Sound(msnd4, 128);
                        Command(mnu.T[1]);
                        break;

                    case Kbd.SDLK_F1:
                        Program.Screenshot();
                        break;
                }
            }

            Kbd.LastKey = 0;
            return mnu != null;
        }

        /// <summary>G_keyf: the cheat buffer shifts on every key press.</summary>
        public static void KeyProc(int k)
        {
            if (!Game.TwoPl || Game.Cheat)
            {
                for (int i = 0; i < 31; ++i) cbuf[i] = cbuf[i + 1];
                cbuf[31] = GetKeyChar(k);
            }
        }

        static string CStr(byte[] b)
        {
            int n = 0;
            while (n < b.Length && b[n] != 0) ++n;
            return System.Text.Encoding.GetEncoding(1251).GetString(b, 0, n);
        }

        public static void Init()
        {
            string[] nm = {
                "CYBSIT","KNTDTH","MNPAIN","PEPAIN","SLOP","MANSIT","BOSPN","VILACT",
                "PLFALL","BGACT","BGDTH2","POPAIN","SGTATK","VILDTH"
            };
            for (int i = 0; i < 14; ++i) qsndRes[i] = Wad.GetResId("DS" + nm[i]);

            csnd1 = Misc.GetSnd("HAHA1");
            csnd2 = Misc.GetSnd("RADIO");
            msnd1 = Misc.GetSnd("PSTOP");
            msnd2 = Misc.GetSnd("PISTOL");
            msnd3 = Misc.GetSnd("SWTCHN");
            msnd4 = Misc.GetSnd("SWTCHX");
            msnd5 = Misc.GetSnd("SUDI");
            msnd6 = Misc.GetSnd("TUDI");

            msklh = new VgaImage[2];
            msklh[0] = Res.Img(Wad.GetResId("M_SKULL1"));
            msklh[1] = Res.Img(Wad.GetResId("M_SKULL2"));
            mbarl = Res.Img(Wad.GetResId("M_THERML"));
            mbarm = Res.Img(Wad.GetResId("M_THERMM"));
            mbarr = Res.Img(Wad.GetResId("M_THERMR"));
            mbaro = Res.Img(Wad.GetResId("M_THERMO"));
            mslotl = Res.Img(Wad.GetResId("M_LSLEFT"));
            mslotm = Res.Img(Wad.GetResId("M_LSCNTR"));
            mslotr = Res.Img(Wad.GetResId("M_LSRGHT"));

            panimP = 0;
        }

        // ------------------------------------------------------------------- draw

        public static bool Draw()
        {
            ++gmTm;
            Vga.SetRect(0, Vga.SCRW, 0, Vga.SCRH);

            if (mnu == null && !gmRedraw) return false;
            gmRedraw = false;
            if (mnu == null) return true;

            if (mnu.Type == MenuType)
            {
                int y = (200 - mnu.N * 16 - 20) / 2;

                Misc.Gotoxy(mnu.X, y - 10);
                Misc.PrintBf(mnu.Ttl);

                for (int i = 0; i < mnu.N; ++i)
                {
                    if (mnu.T[i] == Load || mnu.T[i] == Save)
                    {
                        int j = y + i * 16 + 29;
                        Vga.Spr(mnu.X, j, mslotl);
                        for (int k = 8; k < 184; k += 8) Vga.Spr(mnu.X + k, j, mslotm);
                        Vga.Spr(mnu.X + 184, j, mslotr);

                        Misc.Gotoxy(mnu.X + 4, j - 8);
                        if (input && i == saveMnu.Cur) Misc.PrintSf(CStr(ibuf) + "_");
                        else Misc.PrintSf(savName[i]);
                    }
                    else
                    {
                        int x = mnu.X + (mnu.T[i] >= SVolM ? (mnu.T[i] >= Pl1Cm ? 50 : 152) : 0);
                        Misc.Gotoxy(x, y + i * 16 + 20);
                        Misc.PrintBf(mnu.M[i]);
                    }

                    if (mnu.T[i] == MusicM) Misc.PrintBf(" '{0}'", Enc(Game.MusicName));
                    else if (mnu.T[i] == Interp) Misc.PrintBf("{0}", Program.Fullscreen ? "ON" : "OFF");
                    else if (mnu.T[i] >= Pl1Cm)
                    {
                        int x = mnu.X + ((mnu.T[i] == Pl1Cm) ? 15 : 35);
                        Vga.ManSpr(x, y + i * 16 + 20 + 14,
                            Player.GetSpr(panim[panimP], 0),
                            pcolortab[(mnu.T[i] == Pl1Cm) ? p1color : p2color]);
                    }
                    else if (mnu.T[i] >= SVolM)
                    {
                        int j = y + i * 16 + 20;
                        Vga.Spr(mnu.X, j, mbarl);
                        for (int k = 8; k < 144; k += 8) Vga.Spr(mnu.X + k, j, mbarm);
                        Vga.Spr(mnu.X + 144, j, mbarr);

                        int k2 = 0;
                        switch (mnu.T[i])
                        {
                            case SVolM: k2 = Sound.SndVol; break;
                            case MVolM: k2 = Music.MusVol; break;
                            case GammaM: k2 = GammaLevel << 5; break;
                        }
                        Vga.Spr(mnu.X + 8 + k2, j, mbaro);
                    }
                }

                Vga.Spr(mnu.X - 25, y + mnu.Cur * 16 + 20 - 8, msklh[(gmTm / 6) & 1]);
            }
            else
            {
                Misc.Gotoxy((320 - mnu.Ttl.Length * 7) / 2, 90);
                Misc.PrintSf(mnu.Ttl);
                Misc.Gotoxy(136, 100);
                Misc.PrintSf("(Y/N)");
            }
            return true;
        }
    }

    static class ByteArrayExt
    {
        public static byte[] ToByte8(this string s)
        {
            byte[] b = new byte[8];
            byte[] src = System.Text.Encoding.GetEncoding(28591).GetBytes(s ?? "");
            Array.Copy(src, b, Math.Min(8, src.Length));
            return b;
        }
    }
}
