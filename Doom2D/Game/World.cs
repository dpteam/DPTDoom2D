using System;
using Doom2D.Engine;

namespace Doom2D.Game
{
    /// <summary>
    /// view.c — the map: wall palette, the three cell layers, and drawing them.
    ///
    /// fldb/fld/fldf are the background layer, the type layer and the foreground
    /// layer. A cell holds an index into walp[] (the wall palette); indices 1..3
    /// are not images but water, drawn as a colormap tint over the background.
    /// </summary>
    public static class World
    {
        public const int FLDW = 100;
        public const int FLDH = 100;
        public const int CELW = 8;
        public const int CELH = 8;

        public static int WD; // viewport width in pixels
        public static int HT; // viewport height in pixels

        public static int WO, WX, WY;
        public static int SkyType = 1;
        public static bool WHoriz = true;

        public static readonly byte[] FldB = new byte[FLDW * FLDH];
        public static readonly byte[] Fld = new byte[FLDW * FLDH];
        public static readonly byte[] FldF = new byte[FLDW * FLDH];

        /// <summary>walp[256]: a wall image, or null for water (WalWater says which).</summary>
        public static readonly VgaImage[] WalP = new VgaImage[256];
        public static readonly int[] WalWater = new int[256];
        public static readonly int[] WalH = new int[256];
        public static readonly int[] WalF = new int[256];
        public static readonly byte[] WalSwp = new byte[256];

        static readonly byte[] WalAni = new byte[256];
        static readonly int[,] aniH = new int[5, 5];
        static readonly byte[] aniC = new byte[5];

        static VgaImage horiz;
        static VgaImage[] ltN = new VgaImage[4];

        public static int LtTime, LtType, LtSide, LtYPos;
        public static bool LtForce;

        /// <summary>game.c: LT_DELAY / LT_HITTIME, the lightning cycle.</summary>
        public const int LtDelay = 8;
        public const int LtHitTime = 6;

        static Res.Locked[] ltSnd;

        public static void LoadLightningSounds()
        {
            ltSnd = new Res.Locked[2];
            ltSnd[0] = Misc.GetSnd("THUND1");
            ltSnd[1] = Misc.GetSnd("THUND2");
        }

        public static Res.Locked LtSnd(int i)
        {
            return ltSnd[i];
        }

        public static void Save(BinWriter w)
        {
            w.Int(SkyType);
            for (int i = 1; i < 256; ++i) w.Name8(GetName(i));
            for (int i = 0; i < 256; ++i) w.Int(WalF[i]);
            w.Bytes(WalSwp);
            w.Bytes(FldB);
            w.Bytes(Fld);
            w.Bytes(FldF);
        }

        public static void Load(BinReader r)
        {
            SkyType = r.Int();
            for (int i = 1; i < 256; ++i)
            {
                WalAni[i] = 0;
                string s = r.Name8();
                if (s.Length == 0) { WalH[i] = -1; WalP[i] = null; continue; }
                WalAni[i] = GetAni(s);
                if (Wad.NameEq(s, "_WATER_", 7))
                {
                    WalH[i] = -2;
                    WalWater[i] = (s.Length > 7 ? s[7] - '0' : 0) + 1;
                }
                else
                {
                    WalH[i] = Wad.GetResId(s);
                    WalP[i] = Res.Img(WalH[i]);
                }
            }
            for (int i = 0; i < 256; ++i) WalF[i] = r.Int();
            for (int i = 1; i < 256; ++i) if ((WalF[i] & 1) != 0) WalH[i] |= 0x8000;
            r.ReadInto(WalSwp);
            r.ReadInto(FldB);
            r.ReadInto(Fld);
            r.ReadInto(FldF);
            SetSky("RSKY" + SkyType);
        }

        static string GetName(int n)
        {
            if (WalH[n] == -1) return "";
            if (WalH[n] == -2) return "_WATER_" + (WalWater[n] - 1);
            return Wad.GetResName(WalH[n] & 0x7FFF);
        }

        static byte GetAni(string n)
        {
            if (Wad.NameEq(n, "WALL22_1", 8)) return 1;
            if (Wad.NameEq(n, "WALL58_1", 8)) return 2;
            if (Wad.NameEq(n, "W73A_1", 8)) return 3;
            if (Wad.NameEq(n, "RP2_1", 8)) return 4;
            return 0;
        }

        static void SetSky(string name)
        {
            horiz = Res.Img(Wad.GetResId(name));
        }

        public static void Adjust()
        {
            int maxX = FLDW * CELW - WD / 2;
            int maxY = FLDH * CELH - HT / 2;

            if (WX < WD / 2) WX = WD / 2;
            if (WY < HT / 2) WY = HT / 2;
            if (WX > maxX) WX = maxX;
            if (WY > maxY) WY = maxY;
        }

        public static void Draw()
        {
            Adjust();
            Vga.SetRect(0, WD, WO + 1, HT);

            if (WHoriz)
            {
                int x = 0, d = 0;
                do
                {
                    int y = WO;
                    d &= ~2;
                    do
                    {
                        Vga.RotSpr(x, y, horiz, d);
                        y += horiz.H;
                        d ^= 2;
                    } while (y < HT + WO);
                    x += horiz.W;
                    d ^= 1;
                } while (x < WD);

                if (SkyType == 2 && LtTime < 0)
                {
                    VgaImage img = ltN[LtType * 2 + (LtTime < -5 ? 0 : 1)];
                    if (LtSide == 0) Vga.Spr(0, WO + LtYPos, img);
                    else Vga.Spr2(WD - 1, WO + LtYPos, img);
                }
            }
            else Vga.Clr(0, WD, WO + 1, HT, 0x97);

            DrawFld(FldB, true);
            Dots.Draw();
            Items.Draw();
            Player.Draw(Game.Pl1);
            if (Game.TwoPl) Player.Draw(Game.Pl2);
            Monsters.Draw();
            Weapons.Draw();
            Smoke.Draw();
            Fx.Draw();
            DrawFld(FldF, false);

            if (SkyType == 2 && (LtTime == -4 || LtTime == -2))
                Vga.RemapRect(0, WO + 1, WD, HT, Palette.ClrMap, 11);
        }

        /// <summary>Z_drawfld: one cell layer. Water is a tint, not an image.</summary>
        public static void DrawFld(byte[] fld, bool background)
        {
            for (int y = 0; y < FLDH; ++y)
            {
                for (int x = 0; x < FLDW; ++x)
                {
                    byte p = fld[y * FLDW + x];
                    if (p == 0) continue;

                    int sx = x * CELW - WX + WD / 2;
                    int sy = y * CELH - WY + HT / 2 + 1 + WO;

                    VgaImage pic = WalP[p];
                    if (pic == null)
                    {
                        // Water: tint whatever is already there with colormap 8..10.
                        if (!background)
                            Vga.RemapRect(sx, sy, CELW, CELH, Palette.ClrMap, WalWater[p] + 7);
                    }
                    else Vga.Pic(sx, sy, pic);
                }
            }
        }

        public static void Init()
        {
            string[][] anm = new string[4][];
            anm[0] = new[] { "WALL22_1", "WALL23_1", "WALL23_2", null, null };
            anm[1] = new[] { "WALL58_1", "WALL58_2", "WALL58_3", null, null };
            anm[2] = new[] { "W73A_1", "W73A_2", null, null, null };
            anm[3] = new[] { "RP2_1", "RP2_2", "RP2_3", "RP2_4", null };

            for (int i = 1; i < 5; ++i)
            {
                int j = 0;
                for (; j < 5 && anm[i - 1][j] != null; ++j)
                    aniH[i, j] = Wad.GetResId(anm[i - 1][j]);
                for (; j < 5; ++j) aniH[i, j] = -1;
            }
            for (int i = 0; i < 5; ++i) aniC[i] = 0;

            Dots.Init();
            Smoke.Init();
            Fx.Init();
            Weapons.Init();
            Items.Init();
            Switches.Init();
            Player.Init();
            Monsters.Init();

            SetSky("RSKY1");
            Sound.FreeChunks();
        }

        public static void Act()
        {
            if (Game.Time % 3 != 0) return;
            for (int i = 1; i < 256; ++i)
            {
                int a = WalAni[i];
                if (a == 0) continue;
                if (aniH[a, ++aniC[a]] == -1) aniC[a] = 0;
                WalP[i] = Res.Img(aniH[a, aniC[a]]);
            }
        }

        /// <summary>W_load: map block handlers. Returns false for blocks this module does not own.</summary>
        public static bool Load(byte[] d, ref int pos, MapBlock blk)
        {
            switch (blk.T)
            {
                case MapBlockType.WallNames:
                    for (int i = 0; i < 256; ++i) { WalH[i] = -1; WalSwp[i] = (byte)i; WalAni[i] = 0; WalP[i] = null; WalWater[i] = 0; }

                    int i2 = 1;
                    int end = pos + blk.Sz;
                    for (; i2 < 256 && pos < end; ++i2)
                    {
                        string n = ReadName(d, ref pos);
                        int t = d[pos++];

                        if (Wad.NameEq(n, "_WATER_", 7))
                        {
                            WalWater[i2] = (n.Length > 7 ? n[7] - '0' : 0) + 1;
                            WalH[i2] = -2;
                            WalP[i2] = null;
                            continue;
                        }
                        WalH[i2] = Wad.GetResId(n);
                        WalP[i2] = Res.Img(WalH[i2]);
                        if (n.Length > 4 && n[0] == 'S' && n[1] == 'W' && n[4] == '_') WalSwp[i2] = 0;
                        WalF[i2] = t != 0 ? 1 : 0;
                        if (t != 0) WalH[i2] |= 0x8000;
                        if (Wad.NameEq(n, "VTRAP01", 8)) WalF[i2] |= 2;
                        WalAni[i2] = GetAni(n);
                    }

                    // Pair every switch wall with its "pressed" twin.
                    int j = i2;
                    for (int i = 1; i < 256; ++i)
                    {
                        if (WalSwp[i] != 0) continue;
                        if (j >= 256) break;

                        string n = Wad.GetResName(WalH[i] & 0x7FFF);
                        char[] ch = n.ToCharArray();
                        if (ch.Length > 5) ch[5] = (char)(ch[5] ^ 1);
                        string twin = new string(ch);
                        if (twin.Length > 8) twin = twin.Substring(0, 8);

                        int g = Wad.GetResId(twin) | (WalH[i] & 0x8000);
                        int k = 1;
                        for (; k < 256; ++k) if (WalH[k] == g) break;
                        if (k >= 256)
                        {
                            k = j++;
                            WalH[k] = g;
                            WalP[k] = Res.Img(g);
                            WalF[k] = (g & 0x8000) != 0 ? 1 : 0;
                        }
                        WalSwp[i] = (byte)k;
                        WalSwp[k] = (byte)i;
                    }
                    return true;

                case MapBlockType.Back:
                    return UnpackLayer(d, ref pos, blk, FldB);
                case MapBlockType.WType:
                    return UnpackLayer(d, ref pos, blk, Fld);
                case MapBlockType.Front:
                    return UnpackLayer(d, ref pos, blk, FldF);

                case MapBlockType.Sky:
                    SkyType = 0;
                    SkyType = BitConverter.ToInt16(d, pos);
                    pos += 2;
                    SetSky("RSKY" + SkyType);
                    return true;
            }
            return false;
        }

        static string ReadName(byte[] d, ref int pos)
        {
            int n = 0;
            while (n < 8 && d[pos + n] != 0) ++n;
            string s = System.Text.Encoding.GetEncoding(28591).GetString(d, pos, n);
            pos += 8;
            return s;
        }

        static bool UnpackLayer(byte[] d, ref int pos, MapBlock blk, byte[] dst)
        {
            switch (blk.St)
            {
                case 0:
                    Buffer.BlockCopy(d, pos, dst, 0, FLDW * FLDH);
                    pos += FLDW * FLDH;
                    return true;
                case 1:
                    UnpackRle(d, pos, blk.Sz, dst);
                    pos += blk.Sz;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>unpack(): run-length, 0xFF followed by a word count and a byte.</summary>
        public static void UnpackRle(byte[] src, int pos, int len, byte[] dst)
        {
            int o = 0;
            int end = pos + len;
            while (pos < end)
            {
                byte b = src[pos++];
                if (b == 255)
                {
                    int n = BitConverter.ToUInt16(src, pos); pos += 2;
                    byte v = src[pos++];
                    for (int i = 0; i < n; ++i) dst[o++] = v;
                }
                else dst[o++] = b;
            }
        }

        /// <summary>Loads the lightning sprites; G_init calls this.</summary>
        public static void LoadLightning()
        {
            for (int i = 0; i < 2; ++i)
            {
                string s = "LTN" + (char)('1' + i);
                for (int j = 0; j < 2; ++j)
                    ltN[i * 2 + j] = Res.Img(Wad.GetSprId(s, j, 0));
            }
        }

        public static VgaImage Lightning(int type, bool bright)
        {
            return ltN[type * 2 + (bright ? 0 : 1)];
        }

        public static void SetLightningImages(VgaImage[] imgs)
        {
            ltN = imgs;
        }
    }

    public static class MapBlockType
    {
        public const int Comment = -1;
        public const int End = 0;
        public const int WallNames = 1;
        public const int Back = 2;
        public const int WType = 3;
        public const int Front = 4;
        public const int Thing = 5;
        public const int Switch = 6;
        public const int Music = 7;
        public const int Sky = 8;
        public const int Switch2 = 9;
    }

    public sealed class MapBlock
    {
        public int T;
        public int St;
        public int Sz;
    }
}
