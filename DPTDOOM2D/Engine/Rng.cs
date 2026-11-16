//
// DPT.DOOM 2D — deterministic random numbers.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// The game's random number generator.
    ///
    /// Lockstep multiplayer only works if both machines make the same random
    /// decisions in the same order, so this is a fixed LCG (the MSVC rand()
    /// constants) rather than System.Random, whose algorithm is not guaranteed
    /// across runtimes. Every gameplay call goes through <see cref="Rand"/>.
    ///
    /// The seed is part of the session handshake: the host picks it, the guest
    /// adopts it, and a desync then shows up as a state checksum mismatch instead
    /// of as two players seeing different monsters.
    /// </summary>
    public static class Rng
    {
        private const uint Multiplier = 214013u;
        private const uint Increment = 2531011u;

        private static uint state = 1u;

        public static uint State { get { return state; } }

        /// <summary>Reseeds the generator. Called at startup and on every new map.</summary>
        public static void Seed(uint value)
        {
            state = value == 0 ? 1u : value;
        }

        /// <summary>myrand(a) — rand()%a, with the same bias the original had.</summary>
        public static int Rand(int a)
        {
            if (a <= 0) return 0;

            state = state * Multiplier + Increment;
            int value = (int)((state >> 16) & 0x7FFF);
            return value % a;
        }

        /// <summary>A fresh seed, for the host to hand to the guest.</summary>
        public static uint NewSeed()
        {
            uint seed = (uint)System.Environment.TickCount;
            seed = seed * 2654435761u + 0x9E3779B9u;
            return seed == 0 ? 1u : seed;
        }
    }
}
