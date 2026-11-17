using System;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Game
{
    /// <summary>items.h — item types, as stored in the map's TH_* range and in memory.</summary>
    public static class ItemType
    {
        public const int None = 0;
        public const int Clip = 1, Shel = 2, Rocket = 3, Cell = 4, Ammo = 5, SBox = 6, RBox = 7, CelP = 8;
        public const int Stim = 9, Medi = 10, BPack = 11, CSaw = 12, SGun = 13, SGun2 = 14, MGun = 15;
        public const int Laun = 16, Plas = 17, BFG = 18, Arm1 = 19, Arm2 = 20, Mega = 21, Invl = 22, Aqua = 23;
        public const int KeyR = 24, KeyG = 25, KeyB = 26, Suit = 27, Super = 28;
        public const int RTorch = 29, GTorch = 30, BTorch = 31, Gor1 = 32, FCan = 33, Gun2 = 34;
    }

    /// <summary>things.h — the TH_* types written in map files.</summary>
    public static class ThingType
    {
        public const int None = 0;
        public const int Plr1 = 1, Plr2 = 2, DmStart = 3;

        public const int Clip = 100;
        // Clip..Gun2 are consecutive, matching ItemType + 99.
        public const int LastItem = 135;

        public const int Demon = 200;
        public const int LastMonster = 220;

        public const int Dir = 1;
        public const int Dm = 16;
    }

    /// <summary>items.c — everything lying on the floor.</summary>
    public static class Items
    {
        public const int MaxItem = 300;

        sealed class Item
        {
            public Obj O = new Obj();
            public int T;
            public int S;
        }

        static readonly Item[] it = new Item[MaxItem];
        static VgaImage[] spr;
        static byte[] sprd;
        static Res.Locked[] snd;
        static int tsndtm, rsndtm;

        public static int RTime = 1092;

        static Items()
        {
            for (int i = 0; i < MaxItem; ++i) it[i] = new Item();
        }

        public static void Save(BinWriter w)
        {
            int n = MaxItem;
            while (--n > 0 && it[n].T == 0) { }
            ++n;
            w.Int(n);
            for (int i = 0; i < n; ++i)
            {
                w.Int(it[i].O.X); w.Int(it[i].O.Y); w.Int(it[i].O.XV); w.Int(it[i].O.YV);
                w.Int(it[i].O.VX); w.Int(it[i].O.VY); w.Int(it[i].O.R); w.Int(it[i].O.H);
                w.Int(it[i].T); w.Int(it[i].S);
            }
            w.Int(RTime);
        }

        public static void Load(BinReader r)
        {
            Init();
            int n = r.Int();
            for (int i = 0; i < n && i < MaxItem; ++i)
            {
                it[i].O.X = r.Int(); it[i].O.Y = r.Int(); it[i].O.XV = r.Int(); it[i].O.YV = r.Int();
                it[i].O.VX = r.Int(); it[i].O.VY = r.Int(); it[i].O.R = r.Int(); it[i].O.H = r.Int();
                it[i].T = r.Int(); it[i].S = r.Int();
            }
            RTime = r.Int();
        }

        public static void Alloc()
        {
            string[] nm = { "ITEMUP", "WPNUP", "GETPOW", "ITMBK" };
            string[] snm = {
                "CLIP","SHEL","ROCK","CELL","AMMO","SBOX","BROK","CELP",
                "STIM","MEDI","BPAK",
                "CSAW","SHOT","SGN2","MGUN","LAUN","PLAS","BFUG"
            };
            string[] n4 = { "SOUL", "SMRT", "SMGT", "SMBT" };
            string[] n3 = { "GOR1", "FCAN" };

            spr = new VgaImage[58];
            sprd = new byte[58];

            int i = 0;
            for (; i < 18; ++i) spr[i] = Misc.GetSpr(snm[i], 0, 0, sprd, i);
            for (; i < 20; ++i)
            {
                spr[i] = Misc.GetSpr("ARM1", i - 18, 0, sprd, i);
                spr[i + 2] = Misc.GetSpr("ARM2", i - 18, 0, sprd, i);
            }
            i += 2;
            for (; i < 26; ++i) spr[i] = Misc.GetSpr("MEGA", i - 22, 0, sprd, i);
            for (; i < 30; ++i) spr[i] = Misc.GetSpr("PINV", i - 26, 0, sprd, i);

            spr[30] = Misc.GetSpr("AQUA", 0, 0, sprd, 30);
            spr[31] = Misc.GetSpr("KEYR", 0, 0, sprd, 31);
            spr[32] = Misc.GetSpr("KEYG", 0, 0, sprd, 32);
            spr[33] = Misc.GetSpr("KEYB", 0, 0, sprd, 33);
            spr[34] = Misc.GetSpr("SUIT", 0, 0, sprd, 34);

            int n = 35;
            for (int j = 0; j < 4; ++j)
                for (i = 0; i < 4; ++i, ++n) spr[n] = Misc.GetSpr(n4[j], i, 0, sprd, n);
            for (int j = 0; j < 2; ++j)
                for (i = 0; i < 3; ++i, ++n) spr[n] = Misc.GetSpr(n3[j], i, 0, sprd, n);
            spr[57] = Misc.GetSpr("GUN2", 0, 0, sprd, 57);

            snd = new Res.Locked[4];
            for (i = 0; i < 4; ++i) snd[i] = Misc.GetSnd(nm[i]);

            for (i = 0; i < MaxItem; ++i) { it[i].O.R = 10; it[i].O.H = 8; }
        }

        public static void Init()
        {
            for (int i = 0; i < MaxItem; ++i)
            {
                it[i].T = ItemType.None;
                it[i].O.XV = it[i].O.YV = it[i].O.VX = it[i].O.VY = 0;
            }
            tsndtm = rsndtm = 0;
        }

        /// <summary>IT_load: the MB_THING block, which also carries the spawn points.</summary>
        public static bool Load(BinReader r, MapBlock blk)
        {
            if (blk.T != MapBlockType.Thing) return false;

            int m = 0;
            bool overflow = false;

            for (int i = 0; blk.Sz > 0; ++i, blk.Sz -= 8)
            {
                short x = r.Short();
                short y = r.Short();
                short t = r.Short();
                ushort f = r.UShort();

                // A map may carry more things than the fixed table holds (the
                // custom packs do). Drop the surplus instead of walking off the
                // array: the level still plays, just with fewer decorations.
                if (i >= MaxItem) { overflow = true; continue; }

                it[i].O.X = x; it[i].O.Y = y;
                it[i].T = t; it[i].S = f;
                if (it[i].T == 0) break;
                m = i + 1;
            }

            if (overflow)
                Log.Warn("map has more than " + MaxItem + " things, the surplus was ignored");

            // Count the spawn points before deciding anything. Most of the custom
            // packs in Data/Maps are deathmatch-only: they carry DM starts and no
            // single-player start at all, and the original engine simply died on
            // them ("Предмет игрок_1 не найден"). A map that loads and plays is
            // more useful than a fatal error, so the mode follows the map.
            int dmStarts = 0;
            bool hasPlr1 = false, hasPlr2 = false;

            for (int i = 0; i < m; ++i)
            {
                if (it[i].T == ThingType.Plr1) hasPlr1 = true;
                else if (it[i].T == ThingType.Plr2) hasPlr2 = true;
                else if (it[i].T == ThingType.DmStart) ++dmStarts;
            }

            if (!Game.Dm && !hasPlr1 && dmStarts > 0)
            {
                Game.Dm = true;
                Log.Warn("map has no single-player start, switching to deathmatch");
            }

            // DM-only things are removed outside deathmatch, as in the original.
            for (int i = 0; i < m; ++i)
                if ((it[i].S & ThingType.Dm) != 0 && !Game.Dm) it[i].T = 0;

            // Player start 1.
            int j = -1;
            for (int i = 0; i < m; ++i) if (it[i].T == ThingType.Plr1) { j = i; it[i].T = 0; }
            if (!Game.Dm)
            {
                if (j == -1)
                {
                    // No start and no DM starts either: put the player in the
                    // middle of the map rather than refusing to load it.
                    Log.Warn("map has no player start, spawning at the centre");
                    Game.DmPos[0].X = World.FLDW * World.CELW / 2;
                    Game.DmPos[0].Y = World.FLDH * World.CELH / 2;
                    Game.DmPos[0].D = 0;
                }
                else
                {
                    Game.DmPos[0].X = it[j].O.X; Game.DmPos[0].Y = it[j].O.Y;
                    Game.DmPos[0].D = it[j].S & ThingType.Dir;
                }
            }

            // Player start 2.
            j = -1;
            for (int i = 0; i < m; ++i) if (it[i].T == ThingType.Plr2) { j = i; it[i].T = 0; }
            if (!Game.Dm && Game.TwoPl)
            {
                if (j == -1)
                {
                    // Cooperative on a map with one start: both players share it,
                    // which is what the original did for the second player when
                    // the map had no PLR2 thing.
                    Log.Warn("map has no second player start, both players share one");
                    Game.DmPos[1].X = Game.DmPos[0].X;
                    Game.DmPos[1].Y = Game.DmPos[0].Y;
                    Game.DmPos[1].D = Game.DmPos[0].D;
                }
                else
                {
                    Game.DmPos[1].X = it[j].O.X; Game.DmPos[1].Y = it[j].O.Y;
                    Game.DmPos[1].D = it[j].S & ThingType.Dir;
                }
            }

            // Deathmatch starts.
            j = 0;
            for (int i = 0; i < m; ++i)
                if (it[i].T == ThingType.DmStart)
                {
                    if (Game.Dm)
                    {
                        Game.DmPos[j].X = it[i].O.X; Game.DmPos[j].Y = it[i].O.Y;
                        Game.DmPos[j].D = it[i].S & ThingType.Dir;
                    }
                    it[i].T = 0; ++j;
                }
            if (Game.Dm && j < 1)
            {
                // A deathmatch map with no DM start at all: fall back to the
                // single-player start if there is one, otherwise the centre.
                Log.Warn("map has no deathmatch starts, using a fallback spawn");
                Game.DmPos[0].X = World.FLDW * World.CELW / 2;
                Game.DmPos[0].Y = World.FLDH * World.CELH / 2;
                Game.DmPos[0].D = 0;
                j = 1;
            }

            if (Game.Dm)
            {
                Game.DmPNum = j;
                Game.DmPl1P = 0;
                // With a single spawn point the loop below would spin forever
                // looking for a second, different one.
                Game.DmPl2P = j > 1 ? 1 : 0;
            }
            else { Game.DmPl1P = 0; Game.DmPl2P = 1; Game.DmPNum = 2; }

            Player.Spawn(Game.Pl1, Game.DmPos[Game.DmPl1P].X, Game.DmPos[Game.DmPl1P].Y, Game.DmPos[Game.DmPl1P].D);
            if (Game.TwoPl)
                Player.Spawn(Game.Pl2, Game.DmPos[Game.DmPl2P].X, Game.DmPos[Game.DmPl2P].Y, Game.DmPos[Game.DmPl2P].D);

            for (int i = 0; i < m; ++i)
            {
                if (it[i].T >= ThingType.Clip && it[i].T < ThingType.Demon)
                {
                    it[i].S = 0;
                    it[i].T = it[i].T - ThingType.Clip + ItemType.Clip;
                    if (it[i].T >= ItemType.KeyR && it[i].T <= ItemType.KeyB) it[i].T |= 0x8000;
                }
                else if (it[i].T >= ThingType.Demon)
                {
                    Monsters.Spawn(it[i].O.X, it[i].O.Y, (byte)(it[i].S & ThingType.Dir),
                        it[i].T - ThingType.Demon + Monsters.MnDemon);
                    it[i].T = 0;
                }
            }
            return true;
        }

        static void TakeSnd(int t)
        {
            if (tsndtm != 0) return;
            t &= 0x7FFF;
            if (t <= ItemType.CelP || (t >= ItemType.BPack && t <= ItemType.BFG) || t == ItemType.Gun2)
            { tsndtm = Misc.Sound(snd[1], 128); return; }
            if (t == ItemType.Mega || t == ItemType.Invl || t == ItemType.Super)
            { tsndtm = Misc.Sound(snd[2], 192); return; }
            tsndtm = Misc.Sound(snd[0], 256);
        }

        public static void Act()
        {
            if (tsndtm != 0) --tsndtm;
            if (rsndtm != 0) --rsndtm;

            for (int i = 0; i < MaxItem; ++i)
            {
                if (it[i].T == 0) continue;

                if (it[i].S < 0)
                {
                    if (++it[i].S == -8)
                    {
                        Fx.Ifog(it[i].O.X, it[i].O.Y);
                        if (rsndtm == 0) rsndtm = Misc.Sound(snd[3], 128);
                    }
                }
                else
                {
                    switch (it[i].T)
                    {
                        case ItemType.Arm1:
                        case ItemType.Arm2:
                            if (++it[i].S >= 18) it[i].S = 0;
                            break;
                        case ItemType.Mega:
                        case ItemType.Invl:
                        case ItemType.Super:
                        case ItemType.RTorch:
                        case ItemType.GTorch:
                        case ItemType.BTorch:
                            if (++it[i].S >= 8) it[i].S = 0;
                            break;
                        case ItemType.Gor1:
                        case ItemType.FCan:
                            if (++it[i].S >= 6) it[i].S = 0;
                            break;
                    }

                    if ((it[i].T & 0x8000) != 0)
                    {
                        int j = Misc.MoveObj(it[i].O);
                        if ((j & Z.FallOut) != 0) { it[i].T = 0; continue; }
                        else if ((j & Z.HitWater) != 0) Misc.Splash(it[i].O, it[i].O.R + it[i].O.H);
                    }

                    if (Misc.Overlap(it[i].O, Game.Pl1.O))
                        if (Player.Give(Game.Pl1, it[i].T & 0x7FFF) != 0)
                        {
                            TakeSnd(it[i].T);
                            int t = it[i].T & 0x7FFF;
                            if (Game.TwoPl && t >= ItemType.KeyR && t <= ItemType.KeyB) continue;
                            it[i].S = -RTime;
                            if (it[i].S == 0 || (it[i].T & 0x8000) != 0) it[i].T = 0;
                            continue;
                        }

                    if (Game.TwoPl && Misc.Overlap(it[i].O, Game.Pl2.O))
                        if (Player.Give(Game.Pl2, it[i].T & 0x7FFF) != 0)
                        {
                            TakeSnd(it[i].T);
                            int t = it[i].T & 0x7FFF;
                            if (t >= ItemType.KeyR && t <= ItemType.KeyB) continue;
                            it[i].S = -RTime;
                            if (it[i].S == 0 || (it[i].T & 0x8000) != 0) it[i].T = 0;
                            continue;
                        }
                }
            }
        }

        /// <summary>How many items are on the floor, for the F2 overlay.</summary>
        public static int Count { get { int n = 0; for (int i = 0; i < MaxItem; ++i) if (it[i].T != 0) ++n; return n; } }

        public static void Draw()
        {
            for (int i = 0; i < MaxItem; ++i)
            {
                int s = -1;
                if (it[i].T != 0 && it[i].S >= 0)
                {
                    int t = it[i].T & 0x7FFF;
                    switch (t)
                    {
                        case ItemType.Arm1: s = it[i].S / 9 + 18; break;
                        case ItemType.Arm2: s = it[i].S / 9 + 20; break;
                        case ItemType.Mega: s = it[i].S / 2 + 22; break;
                        case ItemType.Invl: s = it[i].S / 2 + 26; break;
                        case ItemType.Super:
                        case ItemType.RTorch:
                        case ItemType.GTorch:
                        case ItemType.BTorch:
                            s = it[i].S / 2 + (it[i].T - ItemType.Super) * 4 + 35; break;
                        case ItemType.Gor1:
                        case ItemType.FCan:
                            s = it[i].S / 2 + (it[i].T - ItemType.Gor1) * 3 + 51; break;
                        case ItemType.Aqua: s = 30; break;
                        case ItemType.Suit: s = 34; break;
                        case ItemType.KeyR:
                        case ItemType.KeyG:
                        case ItemType.KeyB:
                            s = (it[i].T & 0x7FFF) - ItemType.KeyR + 31; break;
                        case ItemType.Gun2: s = 57; break;
                        default: s = (it[i].T & 0x7FFF) - 1; break;
                    }
                }
                if (s >= 0) Misc.DrawSpr(it[i].O.X, it[i].O.Y, spr[s], sprd[s] != 0);
            }
        }

        public static void Spawn(int x, int y, int t)
        {
            for (int i = 0; i < MaxItem; ++i)
                if (it[i].T == 0)
                {
                    it[i].T = t | 0x8000; it[i].S = 0;
                    it[i].O.X = x; it[i].O.Y = y;
                    it[i].O.XV = it[i].O.YV = it[i].O.VX = it[i].O.VY = 0;
                    it[i].O.R = 10; it[i].O.H = 8;
                    return;
                }
        }

        static readonly int[] an = { 10, 4, 1, 40, 50, 25, 5, 100 };

        public static void DropAmmo(int t, int n, int x, int y)
        {
            while (true)
            {
                int a = an[t - ItemType.Clip];
                for (; n >= a; n -= a)
                    Spawn(x + Glob.Rand(7) - 3, y - Glob.Rand(7), t);
                if (t < ItemType.Ammo) break;
                t -= 4;
            }
        }
    }
}
