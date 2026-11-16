namespace DPTDOOM2D.Game
{
    /// <summary>bmap.c — the coarse block map used for line-of-sight and monster AI.</summary>
    public static class Bmap
    {
        public const int Wall = 1;
        public const int Plr1 = 2;
        public const int Plr2 = 4;
        public const int Monster = 8;

        public const int BW = World.FLDW / 4;
        public const int BH = World.FLDH / 4;

        public static readonly byte[] Map = new byte[BW * BH];
        public static bool NeedRemap = true;

        public static byte At(int x, int y)
        {
            if (x < 0 || x >= BW || y < 0 || y >= BH) return 0;
            return Map[y * BW + x];
        }

        public static void Mark(Obj o, byte f)
        {
            int xs = (o.X - o.R) >> 5;
            int xe = (o.X + o.R) >> 5;
            int ys = (o.Y - o.H) >> 5;
            int ye = o.Y >> 5;

            if (xs < 0) xs = 0;
            if (xe >= BW) xe = BW - 1;
            if (ys < 0) ys = 0;
            if (ye >= BH) ye = BH - 1;

            for (int y = ys; y <= ye; ++y)
                for (int x = xs; x <= xe; ++x)
                    Map[y * BW + x] |= f;
        }

        public static void Clear(byte f)
        {
            for (int i = 0; i < Map.Length; ++i) Map[i] &= (byte)~f;
        }

        public static void RemapFld()
        {
            Clear(Wall);
            for (int x = 0; x < World.FLDW; ++x)
                for (int y = 0; y < World.FLDH; ++y)
                {
                    byte c = World.Fld[y * World.FLDW + x];
                    if (c == 1 || c == 2) Map[(y / 4) * BW + x / 4] |= Wall;
                }
            NeedRemap = false;
        }
    }
}
