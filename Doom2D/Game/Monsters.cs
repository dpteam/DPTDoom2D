using System;
using Doom2D.Engine;

namespace Doom2D.Game
{
    /// <summary>monster.c — every hostile thing, plus the damage model they share.</summary>
    public static class Monsters
    {
        public const int MaxMn = 200;

        public const int MnNone = 0, MnDemon = 1, MnImp = 2, MnZomby = 3, MnSerg = 4, MnCyber = 5,
                         MnCGun = 6, MnBaron = 7, MnKnight = 8, MnCaco = 9, MnSoul = 10, MnPain = 11,
                         MnSpider = 12, MnBsp = 13, MnMancub = 14, MnSkel = 15, MnVile = 16, MnFish = 17,
                         MnBarrel = 18, MnRobo = 19, MnMan = 20;
        public const int MnLast = 21;
        public const int MnPlDead = 100, MnPlMess = 101;

        const int MnTn = MnLast - MnDemon; // 20

        const int Sleep = 0, Go = 1, Run = 2, Climb = 3, Die = 4, Dead = 5, Attack = 6,
                  ShootSt = 7, Pain = 8, Wait = 9, Revive = 10, RunOut = 11;

        const int ManColor = 0xD0;
        const int MaxAtm = 90;

        sealed class Mn
        {
            public readonly Obj O = new Obj();
            public byte T, D, St, Ftime;
            public int Fobj;
            public int S;
            public string Ap;
            public int Aim, Life, Pain, Ac, Tx, Ty, Ammo;
            public int Atm;
        }

        struct MnSz
        {
            public int R, H, L, Mp, Rv, Jv, Sp, MinP;
        }

        public static bool NoMon = true;

        static readonly Mn[] mn = new Mn[MaxMn];
        static VgaImage[][] spr;
        static byte[][] sprd;
        static VgaImage[] fspr, plSpr, sgun;
        static Res.Locked fsnd, pauksnd, trupsnd, firsnd, slopsnd, swgsnd, pchsnd, telesnd;
        static Res.Locked[] impsitsnd, impdthsnd, positsnd, podthsnd, gsnd;
        static Res.Locked[][] snd;
        static int mnum, gsndt;

        /// <summary>z_mon: monsters are moving, so traps block them.</summary>
        public static bool ZMon;

        static readonly MnSz[] mnsz = BuildSizes();

        static MnSz[] BuildSizes()
        {
            // rad  ht  life  pain  rv  jv  slop  min_pain
            int[][] d = {
                new[] {  0,  0,    0,    0,  0,  0,    0,    0 }, // none
                new[] { 15, 28,   60,   20,  7, 10,    0,   10 }, // demon
                new[] { 10, 28,   25,   15,  3, 10,   30,    0 }, // imp
                new[] { 10, 28,   15,   10,  3, 10,   30,    0 }, // zomby
                new[] { 10, 28,   20,   10,  3, 10,   30,    0 }, // sergeant
                new[] { 20, 55,  500,   70,  5, 10,    0,   50 }, // cyberdemon
                new[] { 12, 28,   60,   20,  3, 10,   30,   10 }, // chaingunner
                new[] { 12, 32,  150,   40,  3, 10,    0,   30 }, // baron of hell
                new[] { 12, 32,   75,   40,  3, 10,    0,   30 }, // hell knight
                new[] { 15, 28,  100,   10,  4,  4,    0,    0 }, // cacodemon
                new[] {  8, 18,   60,   10,  4,  4,    0,    0 }, // lost soul
                new[] { 15, 28,  100,   10,  4,  4,    0,    0 }, // pain elemental
                new[] { 64, 50,  500,   70,  4, 10,    0,   50 }, // spider mastermind
                new[] { 25, 27,  150,   20,  4, 10,    0,    0 }, // arachnotron
                new[] { 18, 30,  200,   40,  3,  7,    0,   20 }, // mancubus
                new[] { 17, 36,  200,   40,  6, 11,    0,   20 }, // revenant
                new[] { 17, 36,  150,   30,  7, 12,    0,   10 }, // archvile
                new[] {  5,  5,   35,   20, 14,  6,    0,   10 }, // fish
                new[] {  5, 17,   20,    0,  7,  6,    0,    0 }, // barrel
                new[] { 17, 38,   20,   40,  3,  6,    0,   20 }, // robot
                new[] {  8, 26,  400,   70,  8, 10,   30,   50 }  // man
            };
            MnSz[] t = new MnSz[d.Length];
            for (int i = 0; i < d.Length; ++i)
                t[i] = new MnSz { R = d[i][0], H = d[i][1], L = d[i][2], Mp = d[i][3], Rv = d[i][4], Jv = d[i][5], Sp = d[i][6], MinP = d[i][7] };
            return t;
        }

        static readonly string[] sleepAnim = {
            "AAABBB","AAABBB","AAABBB","AAABBB","AAABBB","AAABBB","AAABBB","AAABBB",
            "A","AAABBB","AAABBB","AAABBB","AAABBB","AAABBB","AAABBB","AAABBB","AAABBB",
            "A","A","AAABBB"
        };
        static readonly string[] goAnim = {
            "AABBCCDD","AABBCCDD","AABBDDAACCDD","AABBDDAACCDD","AABBDDCCDDBB",
            "AABBDDAACCDD","AABBCCDD","AABBCCDD","A","AABB","AABBCCBB",
            "AABBCCDDEEFF","AABBCCDDEEFF","AABBCCDDEEFF","AABBCCDDEEFF","AABBCCDDEEFF",
            "AABB","A","DDEEFFGGHHIIJJKKLLAABBCC","ACDABD"
        };
        static readonly string[] painAnim = {
            "H","H","G","G","G","G","H","H","F","E","G","I","I","J","L","Q","EECCDDCC",
            "A","D","G"
        };
        static readonly string[] waitAnim = {
            "A","A","A","A","A","A","A","A","A","AABB","A","A","A","I","K","A","A",
            "A","D","E"
        };
        static readonly string[] attackAnim = {
            "EEFFGG","EEFFGG","EEEEEF","EEEEEF","EEEEEF","EF","EEFFGG","EEFFGG",
            "BBCCDD","CCDD","DDEEFF","GH","GH","GGGGHH","GGHHII",
            "QQGGGHHHIIJJKKLLMMNNOOPP","BBFFAA","A","OOPPQQ","EEEEFF"
        };
        static readonly string[] dieAnim = {
            "IIIJJJKKKLLLMMM","IIIJJJKKKLLL","HHHIIIJJJKKK","HHHIIIJJJKKK",
            "HHHIIIJJJKKKLLLMMMNNNOOO","HHHIIIJJJKKKLLLMMM",
            "IIIJJJKKKLLLMMMNNN","IIIJJJKKKLLLMMMNNN","GGGHHHIIIJJJKKK",
            "FFFGGGHHHIIIJJJKKK","HHHIIIJJJKKKLLLMMM",
            "JJJJKKKKLLLLMMMMNNNNOOOOPPPPQQQQRRRR","JJJKKKLLLMMMNNNOOO",
            "KKKLLLMMMNNNOOOPPPRRRSSS","MMMNNNOOOPPP","RRRSSSTTTUUUVVVWWWXXXYYY",
            "DDDD","CCCDDDEEEFFFGGG","D","HHHHIIIIJJJJKKKKLLLLMMMM"
        };
        static readonly string[] slopAnim = {
            "","NNNOOOPPPRRRSSSTTT","MMMNNNOOOPPPRRRSSSTTT","MMMNNNOOOPPPRRRSSSTTT","",
            "OOOPPPQQQRRRSSS","","","","","","","","","","","","","","OOPPQQRRSSTTUUVV"
        };
        static readonly string[] deadAnim = {
            "N","M","L","L","P","N","O","O","L","","","S","P","T","Q","Z","C","","D","N"
        };
        static readonly string[] messAnim = {
            "","U","U","U","","T","","","","","","","","","","","","","","W"
        };

        static Monsters()
        {
            for (int i = 0; i < MaxMn; ++i) mn[i] = new Mn();
        }

        // ------------------------------------------------------------- save/load

        public static void Save(BinWriter w)
        {
            int n = MaxMn;
            while (--n > 0 && mn[n].T == 0) { }
            ++n;
            w.Int(n);
            for (int i = 0; i < n; ++i)
            {
                Mn m = mn[i];
                w.Int(m.O.X); w.Int(m.O.Y); w.Int(m.O.XV); w.Int(m.O.YV);
                w.Int(m.O.VX); w.Int(m.O.VY); w.Int(m.O.R); w.Int(m.O.H);
                w.Byte(m.T); w.Byte(m.D); w.Byte(m.St); w.Byte(m.Ftime);
                w.Int(m.Fobj); w.Int(m.S);
                w.Int(m.Ap == null ? -1 : AnimIndex(m.Ap));
                w.Int(m.Aim); w.Int(m.Life); w.Int(m.Pain); w.Int(m.Ac);
                w.Int(m.Tx); w.Int(m.Ty); w.Int(m.Ammo);
                w.Short(m.Atm);
            }
            w.Int(mnum); w.Int(gsndt);
        }

        static int AnimIndex(string ap)
        {
            // The save format stores a pointer; here the animation is identified by
            // its text, so a loaded game can rebuild the same frame sequence.
            for (int t = 0; t < MnTn; ++t)
            {
                if (ap == sleepAnim[t]) return t * 10 + 1;
                if (ap == goAnim[t]) return t * 10 + 2;
                if (ap == painAnim[t]) return t * 10 + 3;
                if (ap == waitAnim[t]) return t * 10 + 4;
                if (ap == attackAnim[t]) return t * 10 + 5;
                if (ap == dieAnim[t]) return t * 10 + 6;
                if (ap == slopAnim[t]) return t * 10 + 7;
                if (ap == deadAnim[t]) return t * 10 + 8;
                if (ap == messAnim[t]) return t * 10 + 9;
            }
            if (ap == "KKKKJJ") return 1001;
            if (ap == "MN") return 1002;
            if (ap == "[[\\\\]]") return 1003;
            return -1;
        }

        static string AnimByIndex(int v)
        {
            if (v == 1001) return "KKKKJJ";
            if (v == 1002) return "MN";
            if (v == 1003) return "[[\\\\]]";
            if (v < 0) return "";
            int t = v / 10, k = v % 10;
            if (t < 0 || t >= MnTn) return "";
            switch (k)
            {
                case 1: return sleepAnim[t];
                case 2: return goAnim[t];
                case 3: return painAnim[t];
                case 4: return waitAnim[t];
                case 5: return attackAnim[t];
                case 6: return dieAnim[t];
                case 7: return slopAnim[t];
                case 8: return deadAnim[t];
                case 9: return messAnim[t];
            }
            return "";
        }

        public static void Load(BinReader r)
        {
            Init();
            int n = r.Int();
            for (int i = 0; i < n && i < MaxMn; ++i)
            {
                Mn m = mn[i];
                m.O.X = r.Int(); m.O.Y = r.Int(); m.O.XV = r.Int(); m.O.YV = r.Int();
                m.O.VX = r.Int(); m.O.VY = r.Int(); m.O.R = r.Int(); m.O.H = r.Int();
                m.T = r.Byte(); m.D = r.Byte(); m.St = r.Byte(); m.Ftime = r.Byte();
                m.Fobj = r.Int(); m.S = r.Int();
                m.Ap = AnimByIndex(r.Int());
                m.Aim = r.Int(); m.Life = r.Int(); m.Pain = r.Int(); m.Ac = r.Int();
                m.Tx = r.Int(); m.Ty = r.Int(); m.Ammo = r.Int();
                m.Atm = r.Short();
            }
            mnum = r.Int(); gsndt = r.Int();
        }

        // ----------------------------------------------------------------- setup

        public static void Alloc()
        {
            string[][] sn = {
                new[]{"DMACT","DMPAIN","SGTATK","SGTSIT","SGTDTH"},
                new[]{"BGACT","POPAIN","CLAW","",""},
                new[]{"POSACT","POPAIN","","",""},
                new[]{"POSACT","POPAIN","","",""},
                new[]{"","DMPAIN","HOOF","CYBSIT","CYBDTH"},
                new[]{"POSACT","POPAIN","","",""},
                new[]{"","DMPAIN","","BRSSIT","BRSDTH"},
                new[]{"","DMPAIN","","KNTSIT","KNTDTH"},
                new[]{"DMACT","DMPAIN","","CACSIT","CACDTH"},
                new[]{"DMACT","DMPAIN","SKLATK","SKLATK","FIRXPL"},
                new[]{"DMACT","PEPAIN","","PESIT","PEDTH"},
                new[]{"","DMPAIN","METAL","SPISIT","SPIDTH"},
                new[]{"BSPACT","DMPAIN","BSPWLK","BSPSIT","BSPDTH"},
                new[]{"DMACT","MNPAIN","MANATK","MANSIT","MANDTH"},
                new[]{"SKEACT","POPAIN","SKEATK","SKESIT","SKEDTH"},
                new[]{"VILACT","VIPAIN","VILATK","VILSIT","VILDTH"},
                new[]{"","","BITE1","",""},
                new[]{"","","","","BAREXP"},
                new[]{"BSPACT","","BSPWLK","BSPSIT","BSPDTH"},
                new[]{"HAHA1","PLPAIN","","STOP1","PDIEHI"}
            };
            string[] msn = {
                "SARG","TROO","POSS","SPOS","CYBR","CPOS","BOSS","BOS2","HEAD","SKUL",
                "PAIN","SPID","BSPI","FATT","SKEL","VILE","FISH","BAR1","ROBO","PLAY"
            };
            int[] mms = {
                14*2,21*2,21*2,21*2,16*2,20*2,15*2,15*2,12*2,11*2,13*2,19*2,16*2,
                20*2,17*2,29*2,6*2,2*2,17*2,23*2
            };

            sgun = new VgaImage[2];
            sgun[0] = Res.Img(Wad.GetSprId("PWP4", 0, 1));
            sgun[1] = Res.Img(Wad.GetSprId("PWP4", 1, 1));

            spr = new VgaImage[MnTn][];
            sprd = new byte[MnTn][];
            snd = new Res.Locked[MnTn][];

            for (int j = 0; j < MnTn; ++j)
            {
                spr[j] = new VgaImage[29 * 2];
                sprd[j] = new byte[29 * 2];
                snd[j] = new Res.Locked[5];

                for (int i = 0; i < mms[j]; ++i)
                    spr[j][i] = Misc.GetSpr(msn[j], i / 2, (i & 1) + 1, sprd[j], i);

                if (j == MnBarrel - 1)
                    for (int i = 4; i < 14; ++i)
                        spr[j][i] = Misc.GetSpr("BEXP", i / 2 - 2, (i & 1) + 1, sprd[j], i);

                for (int i = 0; i < 5; ++i)
                    snd[j][i] = sn[j][i].Length != 0 ? Misc.GetSnd(sn[j][i]) : null;
            }

            fspr = new VgaImage[8];
            for (int i = 0; i < 8; ++i) fspr[i] = Res.Img(Wad.GetSprId("FIRE", i, 0));

            plSpr = new VgaImage[2];
            plSpr[0] = Res.Img(Wad.GetSprId("PLAY", 'N' - 'A', 0));
            plSpr[1] = Res.Img(Wad.GetSprId("PLAY", 'W' - 'A', 0));

            impsitsnd = new[] { Misc.GetSnd("BGSIT1"), Misc.GetSnd("BGSIT2") };
            impdthsnd = new[] { Misc.GetSnd("BGDTH1"), Misc.GetSnd("BGDTH2") };
            positsnd = new[] { Misc.GetSnd("POSIT1"), Misc.GetSnd("POSIT2"), Misc.GetSnd("POSIT3") };
            podthsnd = new[] { Misc.GetSnd("PODTH1"), Misc.GetSnd("PODTH2"), Misc.GetSnd("PODTH3") };

            fsnd = Misc.GetSnd("FLAME");
            firsnd = Misc.GetSnd("FIRSHT");
            slopsnd = Misc.GetSnd("SLOP");
            swgsnd = Misc.GetSnd("SKESWG");
            pchsnd = Misc.GetSnd("SKEPCH");
            telesnd = Misc.GetSnd("TELEPT");
            pauksnd = Misc.GetSnd("PAUK1");
            trupsnd = Misc.GetSnd("UTRUP");

            gsnd = new Res.Locked[4];
            for (int i = 0; i < 4; ++i) gsnd[i] = Misc.GetSnd("GOOD" + (char)('1' + i));
        }

        public static void Init()
        {
            for (int i = 0; i < MaxMn; ++i) { mn[i].T = 0; mn[i].St = Sleep; }
            gsndt = mnum = 0;
        }

        static char ApAt(Mn m, int at)
        {
            if (m.Ap == null) return '\0';
            return at >= 0 && at < m.Ap.Length ? m.Ap[at] : '\0';
        }

        static void SetSt(int i, int st)
        {
            string a;

            switch (mn[i].St)
            {
                case Die:
                case Dead:
                    if (st != Dead && st != Revive) return;
                    break;
            }

            mn[i].Ac = 0;
            int t = mn[i].T - 1;
            mn[i].St = (byte)st;

            switch (st)
            {
                case Sleep: a = sleepAnim[t]; break;
                case Pain: a = painAnim[t]; break;
                case Wait: a = waitAnim[t]; break;
                case Climb:
                case Run:
                case RunOut:
                case Go: a = goAnim[t]; break;
                case ShootSt:
                    if (t == MnSkel - 1) { a = "KKKKJJ"; break; }
                    if (t == MnRobo - 1) { a = "MN"; break; }
                    goto case Attack;
                case Attack:
                    a = attackAnim[t];
                    if (st == Attack && t == MnVile - 1) a = "[[\\\\]]";
                    break;
                case Die:
                    if (Game.Map == 9 && t == MnBsp - 1) Misc.Sound(pauksnd, 128);
                    a = dieAnim[t]; break;
                case Dead:
                    a = deadAnim[t];
                    if (mn[i].Ap == slopAnim[t]) a = messAnim[t];
                    if (t == MnBarrel - 1) mn[i].T = 0;
                    break;
                case Revive:
                    a = mn[i].Ap == messAnim[t] ? slopAnim[t] : dieAnim[t];
                    mn[i].Ac = a.Length - 1;
                    mn[i].O.R = mnsz[t + 1].R; mn[i].O.H = mnsz[t + 1].H;
                    mn[i].Life = mnsz[t + 1].L; mn[i].Ammo = mn[i].Pain = 0;
                    ++mnum;
                    break;
                default:
                    a = "";
                    break;
            }
            mn[i].Ap = a;
        }

        public static int Spawn(int x, int y, byte d, int t)
        {
            int i;

            if (Game.Dm && NoMon && t < MnPlDead) return -1;

            for (i = 0; i < MaxMn; ++i) if (mn[i].T == 0) goto ok;
            for (i = 0; i < MaxMn; ++i) if (mn[i].T >= MnPlDead) goto ok;
            return -1;

        ok:
            mn[i].O.X = x; mn[i].O.Y = y;
            mn[i].O.XV = mn[i].O.YV = mn[i].O.VX = mn[i].O.VY = 0;
            mn[i].D = d; mn[i].T = (byte)t;
            mn[i].St = Sleep;

            if (t < MnPlDead)
            {
                mn[i].O.R = mnsz[t].R; mn[i].O.H = mnsz[t].H;
                mn[i].Life = mnsz[t].L;
                SetSt(i, Sleep);
                mn[i].S = Glob.Rand(18);
                ++mnum;
            }
            else
            {
                mn[i].O.R = 8; mn[i].O.H = 6; mn[i].Life = 0; mn[i].St = Dead;
            }

            mn[i].Aim = -3; mn[i].Atm = 0;
            mn[i].Pain = 0;
            mn[i].Ammo = 0;
            mn[i].Ftime = 0;
            return i;
        }

        public static int SpawnDeadPl(Obj o, byte c, int t)
        {
            int i = Spawn(o.X, o.Y, c, t + MnPlDead);
            if (i == -1) return -1;
            mn[i].O.X = o.X; mn[i].O.Y = o.Y; mn[i].O.XV = o.XV; mn[i].O.YV = o.YV;
            mn[i].O.VX = o.VX; mn[i].O.VY = o.VY; mn[i].O.R = o.R; mn[i].O.H = o.H;
            return i;
        }

        static bool IsFriend(int a, int b)
        {
            if (a == MnBarrel || b == MnBarrel) return true;
            if (a == b)
                switch (a)
                {
                    case MnImp: case MnDemon:
                    case MnBaron: case MnKnight:
                    case MnCaco: case MnSoul:
                    case MnMancub: case MnSkel:
                    case MnFish:
                        return true;
                }
            if (a == MnSoul && b == MnPain) return true;
            if (b == MnSoul && a == MnPain) return true;
            return false;
        }

        static bool FindNewPrey(int i)
        {
            bool a = !Player.IsDead(Game.Pl1);
            bool b = Game.TwoPl && !Player.IsDead(Game.Pl2);

            if (a)
            {
                if (b)
                    mn[i].Aim = (Math.Abs(mn[i].O.X - Game.Pl1.O.X) + Math.Abs(mn[i].O.Y - Game.Pl1.O.Y)
                        <= Math.Abs(mn[i].O.X - Game.Pl2.O.X) + Math.Abs(mn[i].O.Y - Game.Pl2.O.Y)) ? -1 : -2;
                else mn[i].Aim = -1;
            }
            else
            {
                if (b) mn[i].Aim = -2;
                else
                {
                    int best = 32000;
                    mn[i].Aim = -3;
                    for (int n = 0; n < MaxMn; ++n)
                        if (mn[n].T != 0 && mn[n].St != Dead && n != i && !IsFriend(mn[n].T, mn[i].T))
                        {
                            int l = Math.Abs(mn[i].O.X - mn[n].O.X) + Math.Abs(mn[i].O.Y - mn[n].O.Y);
                            if (l < best) { mn[i].Aim = n; best = l; }
                        }
                    if (mn[i].Aim < 0) { mn[i].Atm = MaxAtm; return false; }
                    mn[i].Atm = 0;
                }
            }
            return true;
        }

        /// <summary>Z_getobjpos: resolves an owner id (-1, -2 or a monster index).</summary>
        public static int GetObjPos(int i, Obj o)
        {
            if (i == -1)
            {
                Copy(o, Game.Pl1.O);
                return Player.IsDead(Game.Pl1) ? 0 : 1;
            }
            if (Game.TwoPl && i == -2)
            {
                Copy(o, Game.Pl2.O);
                return Player.IsDead(Game.Pl2) ? 0 : 1;
            }
            if (i >= 0 && i < MaxMn && mn[i].T != 0 && mn[i].St != Dead)
            {
                Copy(o, mn[i].O);
                return 1;
            }
            return 0;
        }

        static void Copy(Obj dst, Obj src)
        {
            dst.X = src.X; dst.Y = src.Y; dst.XV = src.XV; dst.YV = src.YV;
            dst.VX = src.VX; dst.VY = src.VY; dst.R = src.R; dst.H = src.H;
        }

        static Res.Locked WakeupSnd(int t)
        {
            switch (t)
            {
                case MnImp: return impsitsnd[Glob.Rand(2)];
                case MnZomby: case MnSerg: case MnCGun: return positsnd[Glob.Rand(3)];
            }
            return snd[t - 1][3];
        }

        static Res.Locked DthSnd(int t)
        {
            switch (t)
            {
                case MnImp: return impdthsnd[Glob.Rand(2)];
                case MnZomby: case MnSerg: case MnCGun: return podthsnd[Glob.Rand(3)];
            }
            return snd[t - 1][4];
        }

        static bool CanShoot(int t)
        {
            switch (t)
            {
                case MnDemon: case MnFish: case MnBarrel: return false;
            }
            return true;
        }

        static bool Shoot(int i, Obj o, bool n)
        {
            if (mn[i].Ammo < 0) return false;

            if (!n)
                switch (mn[i].T)
                {
                    case MnFish: case MnBarrel: case MnDemon:
                        return false;
                    case MnCGun:
                    case MnBsp:
                    case MnRobo:
                        if (++mn[i].Ammo >= 50) mn[i].Ammo = mn[i].T == MnRobo ? -200 : -50;
                        break;
                    case MnMan:
                        break;
                    case MnMancub:
                        if (++mn[i].Ammo >= 5) mn[i].Ammo = -50;
                        break;
                    case MnSpider:
                        if (++mn[i].Ammo >= 100) mn[i].Ammo = -50;
                        break;
                    case MnCyber:
                        if (Glob.Rand(2) != 0) return false;
                        if (++mn[i].Ammo >= 10) mn[i].Ammo = -50;
                        break;
                    case MnBaron:
                    case MnKnight:
                        if (Glob.Rand(8) != 0) return false;
                        break;
                    case MnSkel:
                        if (Glob.Rand(32) != 0) return false;
                        break;
                    case MnVile:
                        if (Glob.Rand(8) != 0) return false;
                        break;
                    case MnPain:
                        if (Glob.Rand(8) != 0) return false;
                        break;
                    default:
                        if (Glob.Rand(16) != 0) return false;
                        break;
                }

            if (Misc.Look(mn[i].O, o, mn[i].D) == 0) return false;

            mn[i].Atm = 0;
            mn[i].Tx = o.X + (o.XV + o.VX) * 6;
            mn[i].Ty = o.Y - o.H / 2 + (o.YV + o.VY) * 6;
            if (Math.Abs(mn[i].Tx - mn[i].O.X) < Math.Abs(mn[i].Ty - mn[i].O.Y + mn[i].O.H / 2)) return false;

            int m, xd, yd;

            switch (mn[i].T)
            {
                case MnImp: case MnBaron: case MnKnight: case MnCaco:
                    SetSt(i, ShootSt); Misc.Sound(firsnd, 128); break;
                case MnSkel:
                    SetSt(i, ShootSt); Misc.Sound(snd[MnSkel - 1][2], 128); break;
                case MnVile:
                    mn[i].Tx = o.X; mn[i].Ty = o.Y;
                    SetSt(i, ShootSt); Misc.Sound(fsnd, 128);
                    Misc.Sound(snd[MnVile - 1][2], 128); break;
                case MnSoul:
                    SetSt(i, Attack); Misc.Sound(snd[MnSoul - 1][2], 128);
                    yd = mn[i].Ty - mn[i].O.Y + mn[i].O.H / 2; xd = mn[i].Tx - mn[i].O.X;
                    m = Math.Max(Math.Abs(xd), Math.Abs(yd));
                    if (m == 0) m = 1;
                    mn[i].O.XV = xd * 16 / m; mn[i].O.YV = yd * 16 / m;
                    break;
                case MnMancub:
                    if (mn[i].Ammo == 1) Misc.Sound(snd[MnMancub - 1][2], 128);
                    goto case MnZomby;
                case MnZomby: case MnSerg: case MnBsp: case MnRobo:
                case MnCyber: case MnCGun: case MnSpider:
                case MnPain: case MnMan:
                    SetSt(i, ShootSt); break;
                default:
                    return false;
            }
            return true;
        }

        static bool Kick(int i, Obj o)
        {
            switch (mn[i].T)
            {
                case MnFish: SetSt(i, Attack); return true;
                case MnDemon: SetSt(i, Attack); Misc.Sound(snd[0][2], 128); return true;
                case MnImp: SetSt(i, Attack); Misc.Sound(snd[1][2], 128); return true;
                case MnSkel: SetSt(i, Attack); Misc.Sound(swgsnd, 128); return true;
                case MnRobo: SetSt(i, Attack); Misc.Sound(swgsnd, 128); return true;
                case MnBaron: case MnKnight: case MnCaco: case MnMancub:
                    return Shoot(i, o, true);
            }
            return false;
        }

        static int IsCorpse(Obj o, bool n)
        {
            if (!n && Glob.Rand(8) != 0) return -3;

            for (int i = 0; i < MaxMn; ++i)
                if (mn[i].T != 0 && mn[i].St == Dead && Misc.Overlap(o, mn[i].O))
                    switch (mn[i].T)
                    {
                        case MnSoul: case MnPain:
                        case MnCyber: case MnSpider:
                        case MnPlDead: case MnPlMess:
                        case MnVile: case MnBarrel:
                            continue;
                        default:
                            return i;
                    }
            return -3;
        }

        public static void Act()
        {
            Obj o = new Obj();
            int pt_x = 0, pt_xs = 1, pt_y = 0, pt_ys = 1;

            if (Math.Abs(pt_x += pt_xs) > 123) pt_xs = -pt_xs;
            if (Math.Abs(pt_y += pt_ys) > 50) pt_ys = -pt_ys;

            if (gsndt > 0) if (--gsndt == 0) Misc.Sound(gsnd[Glob.Rand(4)], 128);

            for (int i = 0; i < MaxMn; ++i)
            {
                int t = mn[i].T;
                if (t == 0) continue;

                switch (t)
                {
                    case MnFish:
                        if (Misc.InWater(mn[i].O.X, mn[i].O.Y, mn[i].O.R, mn[i].O.H) == 0) break;
                        goto case MnSoul;
                    case MnSoul:
                    case MnPain:
                    case MnCaco:
                        if (mn[i].St != Die && mn[i].St != Dead) --mn[i].O.YV;
                        break;
                }

                ZMon = true;
                int st = Misc.MoveObj(mn[i].O);
                ZMon = false;
                Bmap.Mark(mn[i].O, Bmap.Monster);

                if ((st & Z.FallOut) != 0)
                {
                    if (t == MnRobo) Game.Exit = 1;
                    mn[i].T = 0; --mnum; continue;
                }
                if ((st & Z.HitWater) != 0) Misc.Splash(mn[i].O, mn[i].O.R + mn[i].O.H);

                Switches.Press(mn[i].O.X, mn[i].O.Y, mn[i].O.R, mn[i].O.H, 8, i);

                if (mn[i].Ftime != 0)
                {
                    --mn[i].Ftime;
                    Smoke.Flame(mn[i].O.X, mn[i].O.Y - mn[i].O.H / 2,
                        mn[i].O.XV + mn[i].O.VX, mn[i].O.YV + mn[i].O.VY,
                        mn[i].O.R / 2, mn[i].O.H / 2, Glob.Rand(401) - 200, -500, 1, mn[i].Fobj);
                }
                if ((st & Z.InWater) != 0) mn[i].Ftime = 0;
                if (mn[i].St == Dead) continue;

                if ((st & Z.InWater) != 0 && (Glob.Rand(32) & 31) == 0)
                    switch (t)
                    {
                        case MnFish:
                            if ((Glob.Rand(4) & 3) != 0) break;
                            goto case MnRobo;
                        case MnRobo:
                        case MnBarrel:
                        case MnPlDead:
                        case MnPlMess:
                            Fx.Bubble(mn[i].O.X + ((Glob.Rand(2) & 1) * 2 - 1) * Glob.Rand(mn[i].O.R + 1),
                                mn[i].O.Y - Glob.Rand(mn[i].O.H + 1), 0, 0, 1);
                            break;
                        default:
                            Fx.Bubble(mn[i].O.X, mn[i].O.Y - mn[i].O.H * 3 / 4, 0, 0, 5);
                            break;
                    }

                if (t == MnBarrel)
                {
                    if (ApAt(mn[i], ++mn[i].Ac) == '\0')
                    {
                        mn[i].Ac = 0;
                        if (mn[i].St == Die || mn[i].St == Dead) mn[i].T = 0;
                    }
                    else if (mn[i].St == Die && mn[i].Ac == 2)
                        Misc.Explode(mn[i].O.X, mn[i].O.Y - 8, 30, mn[i].Aim);
                    continue;
                }

                if (t == MnSoul && (st & Z.HitAir) != 0) Misc.SetSpeed(mn[i].O, 16);
                if (mn[i].Ammo < 0) ++mn[i].Ammo;
                if (mn[i].O.YV < 0)
                    if ((st & Z.InWater) != 0) mn[i].O.YV = -4;
                ++mn[i].Atm;

                switch (mn[i].St)
                {
                    case Pain:
                        if (mn[i].Pain >= mnsz[t].Mp)
                        { mn[i].Pain = mnsz[t].Mp; Misc.Sound(snd[t - 1][1], 128); }
                        if ((mn[i].Pain -= 5) <= mnsz[t].MinP)
                        { SetSt(i, Go); mn[i].Pain = 0; mn[i].Ammo = -9; }
                        break;

                    case Sleep:
                        if (++mn[i].S >= 18) mn[i].S = 0; else break;
                        if (Misc.Look(mn[i].O, Game.Pl1.O, mn[i].D) != 0)
                        { SetSt(i, Go); mn[i].Aim = -1; mn[i].Atm = 0; Misc.Sound(WakeupSnd(t), 128); }
                        if (Game.TwoPl && Misc.Look(mn[i].O, Game.Pl2.O, mn[i].D) != 0)
                        { SetSt(i, Go); mn[i].Aim = -2; mn[i].Atm = 0; Misc.Sound(WakeupSnd(t), 128); }
                        break;

                    case Wait:
                        if (--mn[i].S < 0) SetSt(i, Go);
                        break;

                    case Go:
                        if ((st & Z.Block) != 0) { mn[i].D ^= 1; SetSt(i, RunOut); mn[i].S = 40; break; }
                        if (t == MnVile) if (IsCorpse(mn[i].O, false) >= 0)
                        { SetSt(i, Attack); mn[i].O.XV = 0; break; }

                        if (GetObjPos(mn[i].Aim, o) == 0 || mn[i].Atm > MaxAtm)
                        {
                            if (!FindNewPrey(i))
                            {
                                mn[i].Aim = -3;
                                o.X = mn[i].O.X + pt_x; o.Y = mn[i].O.Y + pt_y;
                                o.XV = o.VX = o.YV = o.VY = o.R = 0; o.H = 1;
                            }
                            else GetObjPos(mn[i].Aim, o);
                        }

                        if (Misc.Overlap(mn[i].O, o))
                        {
                            mn[i].Atm = 0;
                            if (Kick(i, o)) break;
                        }

                        int sx = o.X - mn[i].O.X;
                        int sy = o.Y - o.H / 2 - mn[i].O.Y + mn[i].O.H / 2;

                        if ((st & Z.Block) == 0 && Math.Abs(sx) < 20)
                            if (t != MnFish) { SetSt(i, Run); mn[i].S = 15; mn[i].D = (byte)Glob.Rand(2); break; }

                        if ((st & Z.HitWall) != 0)
                        {
                            if (Switches.Press(mn[i].O.X, mn[i].O.Y, mn[i].O.R, mn[i].O.H, 2, i) != 0)
                            { SetSt(i, Wait); mn[i].S = 4; break; }
                            switch (t)
                            {
                                case MnCaco: case MnSoul: case MnPain: case MnFish:
                                    break;
                                default:
                                    if (Misc.CanStand(mn[i].O.X, mn[i].O.Y, mn[i].O.R) != 0)
                                    { mn[i].O.YV = -mnsz[t].Jv; SetSt(i, Climb); break; }
                                    break;
                            }
                            break;
                        }

                        mn[i].D = (byte)(sx > 0 ? 1 : 0);
                        if (CanShoot(t))
                            if (Math.Abs(sx) > Math.Abs(sy)) if (Shoot(i, o, false)) break;

                        switch (t)
                        {
                            case MnFish:
                                if ((st & Z.InWater) == 0)
                                {
                                    if (Misc.CanStand(mn[i].O.X, mn[i].O.Y, mn[i].O.R) != 0)
                                    {
                                        mn[i].O.YV = -6;
                                        mn[i].O.VX += Glob.Rand(17) - 8;
                                    }
                                    SetSt(i, Pain); mn[i].Pain += 50; break;
                                }
                                goto case MnCaco;
                            case MnCaco:
                            case MnSoul:
                            case MnPain:
                                if (Math.Abs(sy) > 4) mn[i].O.YV = sy < 0 ? -4 : 4; else mn[i].O.YV = 0;
                                if (t == MnFish && mn[i].O.YV < 0)
                                    if (Misc.InWater(mn[i].O.X, mn[i].O.Y - 8, mn[i].O.R, mn[i].O.H) == 0)
                                    { mn[i].O.YV = 0; SetSt(i, Run); mn[i].D = (byte)Glob.Rand(2); mn[i].S = 20; }
                                break;
                            default:
                                if (sy < -20)
                                    if (Misc.CanStand(mn[i].O.X, mn[i].O.Y, mn[i].O.R) != 0)
                                        if ((Glob.Rand(4) & 3) == 0) mn[i].O.YV = -mnsz[t].Jv;
                                break;
                        }

                        if (++mn[i].S >= 8)
                        {
                            mn[i].S = 0;
                            if ((Glob.Rand(8) & 7) == 0) Misc.Sound(snd[t - 1][0], 128);
                        }
                        mn[i].O.XV = (mn[i].D != 0 ? 1 : -1) * mnsz[t].Rv;
                        if ((st & Z.InWater) != 0) mn[i].O.XV /= 2;
                        else if (t == MnFish) mn[i].O.XV = 0;
                        break;

                    case Run:
                        if ((st & Z.Block) != 0) { SetSt(i, RunOut); mn[i].D ^= 1; mn[i].S = 40; break; }
                        if (--mn[i].S <= 0 || ((st & Z.HitWall) != 0 && mn[i].O.YV + mn[i].O.VY == 0))
                        {
                            SetSt(i, Go); mn[i].S = 0;
                            if ((st & (Z.HitWall | Z.Block)) != 0) mn[i].D ^= 1;
                            if ((Glob.Rand(8) & 7) == 0) Misc.Sound(snd[t - 1][0], 128);
                        }
                        mn[i].O.XV = (mn[i].D != 0 ? 1 : -1) * mnsz[t].Rv;
                        if ((st & Z.InWater) != 0) mn[i].O.XV /= 2;
                        else if (t == MnFish) mn[i].O.XV = 0;
                        break;

                    case RunOut:
                        if ((st & Z.Block) == 0 && mn[i].S > 0) mn[i].S = 0;
                        if (--mn[i].S <= -18)
                        {
                            SetSt(i, Go); mn[i].S = 0;
                            if ((st & (Z.HitWall | Z.Block)) != 0) mn[i].D ^= 1;
                            if ((Glob.Rand(8) & 7) == 0) Misc.Sound(snd[t - 1][0], 128);
                        }
                        mn[i].O.XV = (mn[i].D != 0 ? 1 : -1) * mnsz[t].Rv;
                        if ((st & Z.InWater) != 0) mn[i].O.XV /= 2;
                        else if (t == MnFish) mn[i].O.XV = 0;
                        break;

                    case Climb:
                        if (mn[i].O.YV + mn[i].O.VY >= 0 || (st & Z.HitWall) == 0)
                        {
                            SetSt(i, Go); mn[i].S = 0;
                            if ((st & (Z.HitWall | Z.Block)) != 0) { mn[i].D ^= 1; SetSt(i, Run); mn[i].S = 15; }
                        }
                        mn[i].O.XV = (mn[i].D != 0 ? 1 : -1) * mnsz[t].Rv;
                        if ((st & Z.InWater) != 0) mn[i].O.XV /= 2;
                        else if (t == MnFish) mn[i].O.XV = 0;
                        break;

                    case Attack:
                    case ShootSt:
                        if (t == MnSoul)
                        {
                            if ((st & (Z.HitWall | Z.HitCeil | Z.HitLand)) != 0) SetSt(i, Go);
                            break;
                        }
                        if (t != MnFish) mn[i].O.XV = Misc.Dec(mn[i].O.XV, 1);
                        if (t == MnVile && mn[i].St == ShootSt)
                        {
                            if (GetObjPos(mn[i].Aim, o) == 0) { SetSt(i, Go); break; }
                            if (Misc.Look(mn[i].O, o, mn[i].D) == 0) { SetSt(i, Go); break; }
                            if (Misc.InWater(o.X, o.Y, o.R, o.H) != 0) { SetSt(i, Go); break; }
                            mn[i].Tx = o.X; mn[i].Ty = o.Y;
                            Misc.HitObj(mn[i].Aim, 2, i, Hit.Some);
                        }
                        break;
                }

                if (mn[i].St == Revive)
                {
                    if (--mn[i].Ac == 0) SetSt(i, Go);
                }
                else ++mn[i].Ac;

                if (ApAt(mn[i], mn[i].Ac) == '\0')
                    switch (mn[i].St)
                    {
                        case Attack:
                            switch (t)
                            {
                                case MnSoul:
                                    mn[i].Ac = 0;
                                    goto case MnImp;
                                case MnImp:
                                case MnDemon:
                                    if (Misc.Hit(mn[i].O, 15, i, Hit.Some) != 0)
                                        if (t == MnSoul) SetSt(i, Go);
                                    break;
                                case MnFish:
                                    if (Misc.Hit(mn[i].O, 10, i, Hit.Some) != 0)
                                        Misc.Sound(snd[MnFish - 1][2], 128);
                                    break;
                                case MnSkel:
                                case MnRobo:
                                    Copy(o, mn[i].O);
                                    o.XV = mn[i].D != 0 ? 50 : -50;
                                    if (Misc.Hit(o, 50, i, Hit.Some) != 0) Misc.Sound(pchsnd, 128);
                                    break;
                                case MnVile:
                                    int sx = IsCorpse(mn[i].O, true);
                                    if (sx == -3) break;
                                    if (mn[sx].T == 0 || mn[sx].St != Dead) break;
                                    SetSt(sx, Revive); Misc.Sound(slopsnd, 128);
                                    Weapons.HitXv = Weapons.HitYv = 0;
                                    HitMn(i, 5, -3, Hit.Some);
                                    break;
                            }
                            if (t != MnSoul && mn[i].St != Die) SetSt(i, Go);
                            break;

                        case ShootSt:
                            switch (t)
                            {
                                case MnImp:
                                    Weapons.Ball1(mn[i].O.X + (mn[i].D * 2 - 1) * mn[i].O.R, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i);
                                    break;
                                case MnZomby:
                                    Weapons.Pistol(mn[i].O.X + (mn[i].D * 2 - 1) * mn[i].O.R, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i);
                                    break;
                                case MnSerg:
                                    Weapons.Shotgun(mn[i].O.X + (mn[i].D * 2 - 1) * mn[i].O.R, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i);
                                    break;
                                case MnMan:
                                    Weapons.DShotgun(mn[i].O.X + (mn[i].D * 2 - 1) * mn[i].O.R, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i);
                                    mn[i].Ammo = -36; break;
                                case MnCyber:
                                    Weapons.RocketShot(mn[i].O.X + (mn[i].D * 2 - 1) * mn[i].O.R, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i);
                                    break;
                                case MnSkel:
                                    Weapons.Revf(mn[i].O.X + (mn[i].D * 2 - 1) * mn[i].O.R, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i, mn[i].Aim);
                                    break;
                                case MnCGun:
                                case MnSpider:
                                    Weapons.MGun(mn[i].O.X + (mn[i].D * 2 - 1) * mn[i].O.R, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i);
                                    break;
                                case MnBsp:
                                    Weapons.APlasma(mn[i].O.X + (mn[i].D * 2 - 1) * mn[i].O.R, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i);
                                    break;
                                case MnRobo:
                                    Weapons.PlasmaShot(mn[i].O.X + (mn[i].D * 2 - 1) * 15, mn[i].O.Y - 30, mn[i].Tx, mn[i].Ty, i);
                                    break;
                                case MnMancub:
                                    Weapons.ManFire(mn[i].O.X + (mn[i].D * 2 - 1) * mn[i].O.R, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i);
                                    break;
                                case MnBaron:
                                case MnKnight:
                                    Weapons.Ball7(mn[i].O.X, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i);
                                    break;
                                case MnCaco:
                                    Weapons.Ball2(mn[i].O.X, mn[i].O.Y - mn[i].O.H / 2, mn[i].Tx, mn[i].Ty, i);
                                    break;
                                case MnPain:
                                    {
                                        int sp = Spawn(mn[i].O.X, mn[i].O.Y, mn[i].D, MnSoul);
                                        if (sp == -1) break;
                                        mn[sp].Aim = mn[i].Aim;
                                        GetObjPos(mn[sp].Aim, o);
                                        mn[sp].Atm = 0;
                                        Shoot(sp, o, true);
                                        break;
                                    }
                            }
                            if (t == MnCGun || t == MnSpider || t == MnBsp || t == MnMancub || t == MnRobo)
                            {
                                if (GetObjPos(mn[i].Aim, o) == 0) FindNewPrey(i);
                                else if (Shoot(i, o, false)) break;
                            }
                            SetSt(i, Go);
                            break;

                        case Die:
                            SetSt(i, Dead);
                            if (t == MnPain || t == MnSoul) mn[i].Ftime = 0;
                            if (t == MnPain)
                            {
                                int sp = Spawn(mn[i].O.X - 15, mn[i].O.Y, 0, MnSoul);
                                if (sp == -1) break;
                                SetSt(sp, Go);
                                sp = Spawn(mn[i].O.X + 15, mn[i].O.Y, 1, MnSoul);
                                if (sp == -1) break;
                                SetSt(sp, Go);
                                sp = Spawn(mn[i].O.X, mn[i].O.Y - 10, 1, MnSoul);
                                if (sp == -1) break;
                                SetSt(sp, Go);
                            }
                            break;

                        default:
                            mn[i].Ac = 0;
                            break;
                    }

                switch (mn[i].St)
                {
                    case Go: case Run: case Climb: case RunOut:
                        if (t == MnCyber || t == MnSpider || t == MnBsp)
                        {
                            if (mn[i].Ac == 0 || mn[i].Ac == 6) Misc.Sound(snd[t - 1][2], 128);
                        }
                        else if (t == MnRobo)
                            if (mn[i].Ac == 0 || mn[i].Ac == 12) Misc.Sound(snd[t - 1][2], 128);
                        break;
                }
            }
        }

        public static void Mark()
        {
            for (int i = 0; i < MaxMn; ++i) if (mn[i].T != 0) Bmap.Mark(mn[i].O, Bmap.Monster);
        }

        public static void Draw()
        {
            for (int i = 0; i < MaxMn; ++i)
            {
                if (mn[i].T == 0) continue;

                if (mn[i].T >= MnPlDead)
                {
                    Misc.DrawManSpr(mn[i].O.X, mn[i].O.Y, plSpr[mn[i].T - MnPlDead], false, mn[i].D);
                    continue;
                }

                if ((mn[i].T != MnSoul && mn[i].T != MnPain) || mn[i].St != Dead)
                {
                    char c = ApAt(mn[i], mn[i].Ac);
                    int idx = (c - 'A') * 2 + mn[i].D;

                    if (mn[i].T != MnMan)
                        Misc.DrawSpr(mn[i].O.X, mn[i].O.Y, spr[mn[i].T - 1][idx], sprd[mn[i].T - 1][idx] != 0);
                    else
                    {
                        if (c == 'E' || c == 'F')
                            Misc.DrawSpr(mn[i].O.X, mn[i].O.Y, sgun[c - 'E'], mn[i].D != 0);
                        Misc.DrawManSpr(mn[i].O.X, mn[i].O.Y, spr[mn[i].T - 1][idx], sprd[mn[i].T - 1][idx] != 0, ManColor);
                    }
                }

                if (mn[i].T == MnVile && mn[i].St == ShootSt)
                    Misc.DrawSpr(mn[i].Tx, mn[i].Ty, fspr[mn[i].Ac / 3], false);
            }
        }

        /// <summary>MN_hit: the damage a monster takes. Returns 1 when the hit landed.</summary>
        public static int HitMn(int n, int d, int o, int t)
        {
            int i;

            if (mn[n].St == Dead || mn[n].St == Die) return 0;

            if (o == n)
            {
                if (t != Doom2D.Game.Hit.Rocket && t != Doom2D.Game.Hit.Electro) return 0;
                if (mn[n].T == MnCyber || mn[n].T == MnBarrel) return 1;
            }

            if (o >= 0)
            {
                if (mn[o].T == MnSoul && mn[n].T == MnPain) return 0;
                if (mn[o].T == mn[n].T)
                    switch (mn[n].T)
                    {
                        case MnImp: case MnDemon:
                        case MnBaron: case MnKnight:
                        case MnCaco: case MnSoul:
                        case MnMancub: case MnSkel:
                        case MnFish:
                            return 0;
                    }
            }

            if (t == Doom2D.Game.Hit.Flame)
                if (mn[n].Ftime != 0 && mn[n].Fobj == o) { if ((Game.Time & 31) != 0) return 1; }
                else { mn[n].Ftime = 255; mn[n].Fobj = o; }

            if (t == Doom2D.Game.Hit.Electro)
                if (mn[n].T == MnFish)
                { SetSt(n, Run); mn[n].S = 20; mn[n].D = (byte)Glob.Rand(2); return 1; }

            if (t == Doom2D.Game.Hit.Trap) mn[n].Life = -100;
            if (mn[n].T == MnRobo) d = 0;
            if ((mn[n].Life -= d) <= 0) --mnum;
            if (mn[n].Pain == 0) mn[n].Pain = 3;
            mn[n].Pain += d;

            if (mn[n].St != Pain)
            {
                if (mn[n].Pain >= mnsz[mn[n].T].MinP) SetSt(n, Pain);
            }

            if (mn[n].T != MnBarrel)
                Dots.Blood(mn[n].O.X, mn[n].O.Y - mn[n].O.H / 2, Weapons.HitXv, Weapons.HitYv, d * 2);

            mn[n].Aim = o; mn[n].Atm = 0;

            if (mn[n].Life <= 0)
            {
                if (mn[n].T != MnBarrel)
                    if (o == -1) ++Game.Pl1.Kills;
                    else if (o == -2) ++Game.Pl2.Kills;

                SetSt(n, Die);
                switch (mn[n].T)
                {
                    case MnZomby: i = ItemType.Clip; break;
                    case MnSerg: i = ItemType.SGun; break;
                    case MnCGun: i = ItemType.MGun; break;
                    case MnMan: i = ItemType.KeyR; break;
                    default: i = 0; break;
                }
                if (i != 0) Items.Spawn(mn[n].O.X, mn[n].O.Y, i);

                mn[n].O.XV = 0; mn[n].O.H = 6;
                if (mn[n].Life <= -mnsz[mn[n].T].Sp)
                    switch (mn[n].T)
                    {
                        case MnImp: case MnZomby: case MnSerg: case MnCGun:
                        case MnMan:
                            mn[n].Ap = slopAnim[mn[n].T - 1];
                            Misc.Sound(slopsnd, 128);
                            break;
                        case MnBsp:
                            if (Game.Map == 9) break;
                            goto default;
                        default:
                            Misc.Sound(DthSnd(mn[n].T), 128);
                            break;
                    }
                else if (mn[n].T != MnBsp || Game.Map != 9) Misc.Sound(DthSnd(mn[n].T), 128);

                mn[n].Life = 0;
            }
            else if (mn[n].St == Sleep) { SetSt(n, Go); mn[n].Pain = mnsz[mn[n].T].Mp; }

            return 1;
        }

        static bool HitBox(Obj o, int x, int y)
        {
            return y <= o.Y && y > o.Y - o.H && x >= o.X - o.R && x <= o.X + o.R;
        }

        /// <summary>Z_gunhit: a hitscan pixel found something at (x,y).</summary>
        public static int GunHit(int x, int y, int o, int xv, int yv)
        {
            if (o != -1 && HitBox(Game.Pl1.O, x, y))
                if (Player.HitPlayer(Game.Pl1, 3, o, Hit.Some) != 0)
                { Game.Pl1.O.VX += xv; Game.Pl1.O.VY += yv; return -1; }

            if (Game.TwoPl && o != -2 && HitBox(Game.Pl2.O, x, y))
                if (Player.HitPlayer(Game.Pl2, 3, o, Hit.Some) != 0)
                { Game.Pl2.O.VX += xv; Game.Pl2.O.VY += yv; return -2; }

            for (int i = 0; i < MaxMn; ++i)
                if (mn[i].T != 0 && o != i && HitBox(mn[i].O, x, y))
                    if (HitMn(i, 3, o, Hit.Some) != 0)
                    { mn[i].O.VX += xv; mn[i].O.VY += yv; return 1; }

            return 0;
        }

        static void GoodSnd()
        {
            if (!Game.Dm) return;
            gsndt = 18;
        }

        /// <summary>Z_hit: an area hit against players and monsters.</summary>
        public static int HitArea(Obj o, int d, int own, int t)
        {
            Weapons.HitXv = o.XV + o.VX;
            Weapons.HitYv = o.YV + o.VY;

            if (Misc.Overlap(o, Game.Pl1.O))
                if (Player.HitPlayer(Game.Pl1, d, own, t) != 0)
                {
                    Game.Pl1.O.VX += (o.XV + o.VX) * (t == Doom2D.Game.Hit.Bfg ? 8 : 1) / 4;
                    Game.Pl1.O.VY += (o.YV + o.VY) * (t == Doom2D.Game.Hit.Bfg ? 8 : 1) / 4;
                    if (t == Doom2D.Game.Hit.Bfg) GoodSnd();
                    return -1;
                }

            if (Game.TwoPl && Misc.Overlap(o, Game.Pl2.O))
                if (Player.HitPlayer(Game.Pl2, d, own, t) != 0)
                {
                    Game.Pl2.O.VX += (o.XV + o.VX) * (t == Doom2D.Game.Hit.Bfg ? 8 : 1) / 4;
                    Game.Pl2.O.VY += (o.YV + o.VY) * (t == Doom2D.Game.Hit.Bfg ? 8 : 1) / 4;
                    if (t == Doom2D.Game.Hit.Bfg) GoodSnd();
                    return -2;
                }

            for (int i = 0; i < MaxMn; ++i)
                if (mn[i].T != 0 && Misc.Overlap(o, mn[i].O))
                    if (HitMn(i, d, own, t) != 0)
                    {
                        mn[i].O.VX += (o.XV + o.VX) * (t == Doom2D.Game.Hit.Bfg ? 8 : 1) / 4;
                        mn[i].O.VY += (o.YV + o.VY) * (t == Doom2D.Game.Hit.Bfg ? 8 : 1) / 4;
                        return 1;
                    }

            return 0;
        }

        public static void KilledP()
        {
            for (int i = 0; i < MaxMn; ++i)
                if (mn[i].T == MnMan)
                    if (mn[i].St != Dead && mn[i].St != Die && mn[i].St != Sleep)
                        Misc.Sound(trupsnd, 128);
        }

        public static int HitObj(int obj, int d, int own, int t)
        {
            Weapons.HitXv = Weapons.HitYv = 0;

            if (obj == -1) return Player.HitPlayer(Game.Pl1, d, own, t);
            if (obj == -2 && Game.TwoPl) return Player.HitPlayer(Game.Pl2, d, own, t);
            if (obj < 0 || obj >= MaxMn) return 0;
            if (mn[obj].T != 0) return HitMn(obj, d, own, t);
            return 0;
        }

        public static void Explode(int x, int y, int rad, int own)
        {
            if (x < -100 || x > World.FLDW * World.CELW + 100) return;
            if (y < -100 || y > World.FLDH * World.CELH + 100) return;

            long r = (long)rad * rad;
            int dx, dy, m;

            dx = Game.Pl1.O.X - x; dy = Game.Pl1.O.Y - Game.Pl1.O.H / 2 - y;
            if ((long)dx * dx + (long)dy * dy < r)
            {
                m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (m == 0) m = 1;
                Game.Pl1.O.VX += Weapons.HitXv = dx * 10 / m;
                Game.Pl1.O.VY += Weapons.HitYv = dy * 10 / m;
                Player.HitPlayer(Game.Pl1, 100 * (rad - m) / rad, own, Doom2D.Game.Hit.Rocket);
            }

            if (Game.TwoPl)
            {
                dx = Game.Pl2.O.X - x; dy = Game.Pl2.O.Y - Game.Pl2.O.H / 2 - y;
                if ((long)dx * dx + (long)dy * dy < r)
                {
                    m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (m == 0) m = 1;
                    Game.Pl2.O.VX += Weapons.HitXv = dx * 10 / m;
                    Game.Pl2.O.VY += Weapons.HitYv = dy * 10 / m;
                    Player.HitPlayer(Game.Pl2, 100 * (rad - m) / rad, own, Doom2D.Game.Hit.Rocket);
                }
            }

            for (int i = 0; i < MaxMn; ++i)
                if (mn[i].T != 0)
                {
                    dx = mn[i].O.X - x; dy = mn[i].O.Y - mn[i].O.H / 2 - y;
                    if ((long)dx * dx + (long)dy * dy < r)
                    {
                        m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        if (m == 0) m = 1;
                        mn[i].O.VX += Weapons.HitXv = dx * 10 / m;
                        mn[i].O.VY += Weapons.HitYv = dy * 10 / m;
                        HitMn(i, mn[i].O.R * 10 * (rad - m) / rad, own, Doom2D.Game.Hit.Rocket);
                    }
                }
        }

        public static void Bfg9000(int x, int y, int own)
        {
            Weapons.HitXv = Weapons.HitYv = 0;
            if (x < -100 || x > World.FLDW * World.CELW + 100) return;
            if (y < -100 || y > World.FLDH * World.CELH + 100) return;

            int dx = Game.Pl1.O.X - x;
            int dy = Game.Pl1.O.Y - Game.Pl1.O.H / 2 - y;
            if (own != -1 && (long)dx * dx + (long)dy * dy < 16000)
                if (Misc.CanSee(x, y, Game.Pl1.O.X, Game.Pl1.O.Y - Game.Pl1.O.H / 2) != 0)
                    if (Player.HitPlayer(Game.Pl1, 50, own, Hit.Some) != 0)
                        Weapons.BfgHitShot(Game.Pl1.O.X, Game.Pl1.O.Y - Game.Pl1.O.H / 2, own);

            if (Game.TwoPl)
            {
                dx = Game.Pl2.O.X - x; dy = Game.Pl2.O.Y - Game.Pl2.O.H / 2 - y;
                if (own != -2 && (long)dx * dx + (long)dy * dy < 16000)
                    if (Misc.CanSee(x, y, Game.Pl2.O.X, Game.Pl2.O.Y - Game.Pl2.O.H / 2) != 0)
                        if (Player.HitPlayer(Game.Pl2, 50, own, Hit.Some) != 0)
                            Weapons.BfgHitShot(Game.Pl2.O.X, Game.Pl2.O.Y - Game.Pl2.O.H / 2, own);
            }

            for (int i = 0; i < MaxMn; ++i)
                if (mn[i].T != 0 && own != i)
                {
                    dx = mn[i].O.X - x; dy = mn[i].O.Y - mn[i].O.H / 2 - y;
                    if ((long)dx * dx + (long)dy * dy < 16000)
                        if (Misc.CanSee(x, y, mn[i].O.X, mn[i].O.Y - mn[i].O.H / 2) != 0)
                            if (HitMn(i, 50, own, Hit.Some) != 0)
                                Weapons.BfgHitShot(mn[i].O.X, mn[i].O.Y - mn[i].O.H / 2, own);
                }
        }

        public static int ChkTrap(int t, int d, int o, int ht)
        {
            Weapons.HitXv = Weapons.HitYv = 0;
            int s = 0;

            if (Misc.IsTrapped(Game.Pl1.O.X, Game.Pl1.O.Y, Game.Pl1.O.R, Game.Pl1.O.H) != 0)
            {
                s = 1;
                if (t != 0) Player.HitPlayer(Game.Pl1, d, o, ht);
            }
            if (Game.TwoPl && Misc.IsTrapped(Game.Pl2.O.X, Game.Pl2.O.Y, Game.Pl2.O.R, Game.Pl2.O.H) != 0)
            {
                s = 1;
                if (t != 0) Player.HitPlayer(Game.Pl2, d, o, ht);
            }
            for (int i = 0; i < MaxMn; ++i)
                if (mn[i].T != 0 && mn[i].St != Dead)
                    if (Misc.IsTrapped(mn[i].O.X, mn[i].O.Y, mn[i].O.R, mn[i].O.H) != 0)
                    {
                        s = 1;
                        if (t != 0) HitMn(i, d, o, ht);
                    }
            return s;
        }

        public static void TeleObj(int o, int x, int y)
        {
            Obj p;
            if (o == -1) p = Game.Pl1.O;
            else if (o == -2) p = Game.Pl2.O;
            else if (o >= 0 && o < MaxMn) p = mn[o].O;
            else return;

            Fx.Tfog(p.X, p.Y);
            p.X = x; p.Y = y;
            Fx.Tfog(p.X, p.Y);
            Misc.Sound(telesnd, 128);
        }

        public static void Warning(int l, int t, int r, int b)
        {
            for (int i = 0; i < MaxMn; ++i)
                if (mn[i].T != 0 && mn[i].T != MnCaco && mn[i].T != MnSoul
                    && mn[i].T != MnPain && mn[i].T != MnFish)
                    if (mn[i].St != Die && mn[i].St != Dead && mn[i].St != Sleep)
                        if (mn[i].O.X + mn[i].O.R >= l && mn[i].O.X - mn[i].O.R <= r
                            && mn[i].O.Y >= t && mn[i].O.Y - mn[i].O.H <= b)
                            if (Misc.CanStand(mn[i].O.X, mn[i].O.Y, mn[i].O.R) != 0)
                                mn[i].O.YV = -mnsz[mn[i].T].Jv;
        }
    }
}
