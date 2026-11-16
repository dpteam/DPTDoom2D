namespace Doom2D.Game
{
    /// <summary>view.h — obj_t: everything that moves has one.</summary>
    public class Obj
    {
        public int X, Y;      // position (y is the feet, not the centre)
        public int XV, YV;    // velocity from input/AI
        public int VX, VY;    // velocity from being pushed
        public int R, H;      // radius, height
    }

    /// <summary>misc.h — the bits Z_moveobj reports.</summary>
    public static class Z
    {
        public const int HitWall = 1;
        public const int HitCeil = 2;
        public const int HitLand = 4;
        public const int FallOut = 8;
        public const int InWater = 16;
        public const int HitWater = 32;
        public const int HitAir = 64;
        public const int Block = 128;
    }

    /// <summary>view.h — damage kinds.</summary>
    public static class Hit
    {
        public const int Some = 0;
        public const int Rocket = 1;
        public const int Bfg = 2;
        public const int Trap = 3;
        public const int Water = 4;
        public const int Electro = 5;
        public const int Flame = 6;
    }

    /// <summary>view.h — cell types stored in fld[][].</summary>
    public static class Cell
    {
        public const int Empty = 0;
        public const int Wall = 1;
        public const int WallSwapped = 2;
        public const int Water = 3;
        public const int WaterTop = 4;   // "water surface"
        public const int Acid = 5;
        public const int AcidDeep = 6;
        public const int Lava = 7;
        public const int Trap = 8;
        public const int LiftUp = 9;
        public const int LiftDown = 10;
        public const int TrapClosed = 255;
    }
}
