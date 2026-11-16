using System;

namespace Doom2D.Engine
{
    /// <summary>glob.h — the few globals that every module needs.</summary>
    public static class Glob
    {
        /// <summary>DELAY 50: the game runs at 20 ticks per second.</summary>
        public const int Delay = 50;

        /// <summary>myrand(a) — rand()%a from the original.</summary>
        static readonly Random rng = new Random();

        public static int Rand(int a)
        {
            return a <= 0 ? 0 : rng.Next(a);
        }

        public static int Sign(int a)
        {
            return a > 0 ? 1 : (a < 0 ? -1 : 0);
        }

        public static int Min(int a, int b) { return a < b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
    }
}
