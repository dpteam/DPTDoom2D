using System;

namespace DPTDOOM2D.Engine
{
    /// <summary>glob.h — the few globals that every module needs.</summary>
    public static class Glob
    {
        /// <summary>DELAY 50: the game runs at 20 ticks per second.</summary>
        public const int Delay = 50;

        /// <summary>
        /// myrand(a) — the original's rand()%a.
        ///
        /// Routed through <see cref="Rng"/> rather than System.Random because
        /// lockstep multiplayer needs both machines to produce the same sequence;
        /// see Engine/DeterministicRandom.cs.
        /// </summary>
        public static int Rand(int a)
        {
            return Rng.Rand(a);
        }

        /// <summary>
        /// Seeds the generator. Headless runs use a fixed seed so a command always
        /// produces the same frames; a network session takes the host's seed.
        /// </summary>
        public static void Reset(int seed)
        {
            Rng.Seed((uint)seed);
        }

        public static int Sign(int a)
        {
            return a > 0 ? 1 : (a < 0 ? -1 : 0);
        }

        public static int Min(int a, int b) { return a < b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
    }
}
