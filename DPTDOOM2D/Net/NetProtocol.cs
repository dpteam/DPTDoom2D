//
// DPT.DOOM 2D — network protocol.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;

namespace DPTDOOM2D.Net
{
    /// <summary>
    /// The lockstep wire format, version 1.
    ///
    /// One TCP connection carries everything: the handshake, per-tick input, chat
    /// and state checksums. Every frame is
    ///
    ///   "D2" | type u8 | length u16 | payload
    ///
    /// so a peer can resynchronise on the next frame boundary after a truncated
    /// read instead of dying on a misaligned stream.
    ///
    /// The model is lockstep: both machines run the same tick with the same inputs
    /// and the same RNG seed, so nothing but input has to travel. A world state
    /// never crosses the wire — only a checksum, so a desync can be detected and
    /// reported instead of silently drifting.
    /// </summary>
    public static class NetProtocol
    {
        public const byte Magic0 = (byte)'D';
        public const byte Magic1 = (byte)'2';

        /// <summary>Bumped whenever the game's simulation changes in a way that
        /// would desync against an older build.</summary>
        public const int Version = 1;

        public const int MaxFrameSize = 4096;

        // ---- frame types -----------------------------------------------------
        public const byte Hello = 1;        // guest -> host: version, name
        public const byte Welcome = 2;      // host -> guest: version, player, seed, map, rules
        public const byte Refuse = 3;       // host -> guest: reason code, text
        public const byte Tic = 4;          // both: tick number, input bits, checksum
        public const byte Chat = 5;         // both: UTF-8 text
        public const byte Bye = 6;          // both: reason code
        public const byte Ping = 7;         // both: monotonic counter
        public const byte Pong = 8;         // both: the same counter

        // ---- refusal codes ---------------------------------------------------
        public const byte RefuseVersion = 1;
        public const byte RefuseFull = 2;
        public const byte RefusePassword = 3;
        public const byte RefuseBanned = 4;
        public const byte RefuseBusy = 5;
        public const byte RefuseData = 6;

        public static string DescribeRefusal(byte code)
        {
            switch (code)
            {
                case RefuseVersion: return "version mismatch - both players need the same build";
                case RefuseFull: return "the session is full";
                case RefusePassword: return "wrong password";
                case RefuseBanned: return "you are banned from this host";
                case RefuseBusy: return "the host is already in a game";
                case RefuseData: return "the host has different game data";
                default: return "refused (code " + code + ")";
            }
        }

        /// <summary>
        /// The per-tick input. Doom2D reads nine keys per player, so they fit in
        /// two bytes; the look direction is already derived from up/down.
        /// </summary>
        [Flags]
        public enum InputBits : ushort
        {
            None = 0,
            Left = 1 << 0,
            Right = 1 << 1,
            Up = 1 << 2,
            Down = 1 << 3,
            Fire = 1 << 4,
            Jump = 1 << 5,
            Use = 1 << 6,
            NextWeapon = 1 << 7,
            PrevWeapon = 1 << 8,
            Menu = 1 << 9,
            MenuConfirm = 1 << 10,
            MenuUp = 1 << 11,
            MenuDown = 1 << 12
        }

        // ---- framing ---------------------------------------------------------

        /// <summary>Writes a frame with a 16-bit length: magic, type, length, payload.</summary>
        public static byte[] Pack(byte type, byte[] payload)
        {
            int length = payload == null ? 0 : payload.Length;
            if (length > MaxFrameSize) length = MaxFrameSize;

            byte[] frame = new byte[5 + length];
            frame[0] = Magic0;
            frame[1] = Magic1;
            frame[2] = type;
            frame[3] = (byte)(length & 0xFF);
            frame[4] = (byte)((length >> 8) & 0xFF);
            if (length > 0) Buffer.BlockCopy(payload, 0, frame, 5, length);
            return frame;
        }

        public const int HeaderSize = 5;

        public static bool TryReadHeader(byte[] header, out byte type, out int length)
        {
            type = 0;
            length = 0;

            if (header.Length < HeaderSize) return false;
            if (header[0] != Magic0 || header[1] != Magic1) return false;

            type = header[2];
            length = header[3] | (header[4] << 8);

            return length >= 0 && length <= MaxFrameSize;
        }

        // ---- payload helpers -------------------------------------------------

        public static void PutU16(byte[] buffer, int at, int value)
        {
            buffer[at] = (byte)(value & 0xFF);
            buffer[at + 1] = (byte)((value >> 8) & 0xFF);
        }

        public static int GetU16(byte[] buffer, int at)
        {
            return buffer[at] | (buffer[at + 1] << 8);
        }

        public static void PutU32(byte[] buffer, int at, uint value)
        {
            buffer[at] = (byte)(value & 0xFF);
            buffer[at + 1] = (byte)((value >> 8) & 0xFF);
            buffer[at + 2] = (byte)((value >> 16) & 0xFF);
            buffer[at + 3] = (byte)((value >> 24) & 0xFF);
        }

        public static uint GetU32(byte[] buffer, int at)
        {
            return (uint)(buffer[at] | (buffer[at + 1] << 8) | (buffer[at + 2] << 16) | (buffer[at + 3] << 24));
        }

        /// <summary>Null-padded ASCII, for names and map ids.</summary>
        public static void PutText(byte[] buffer, int at, int length, string text)
        {
            Array.Clear(buffer, at, length);
            if (string.IsNullOrEmpty(text)) return;

            int count = Math.Min(text.Length, length - 1);
            for (int i = 0; i < count; ++i) buffer[at + i] = (byte)text[i];
        }

        public static string GetText(byte[] buffer, int at, int length)
        {
            int end = at;
            while (end < at + length && buffer[end] != 0) ++end;
            return System.Text.Encoding.ASCII.GetString(buffer, at, end - at);
        }

        /// <summary>
        /// A cheap world checksum: enough to catch a desync within a few ticks,
        /// cheap enough to run every tick.
        ///
        /// FNV-1a over the things that must match: both players' positions and
        /// state, every monster's position and health, and the RNG state.
        /// </summary>
        public static uint Checksum()
        {
            uint hash = 2166136261u;

            hash = Mix(hash, (uint)Engine.Rng.State);
            hash = Mix(hash, (uint)Game.Game.Map);
            hash = Mix(hash, (uint)Game.Game.Time);

            hash = MixPlayer(hash, Game.Game.Pl1);
            if (Game.Game.TwoPl) hash = MixPlayer(hash, Game.Game.Pl2);

            hash = Game.Monsters.MixInto(hash);

            return hash;
        }

        private static uint MixPlayer(uint hash, Game.PlayerData p)
        {
            hash = Mix(hash, (uint)p.O.X);
            hash = Mix(hash, (uint)p.O.Y);
            hash = Mix(hash, (uint)p.O.XV);
            hash = Mix(hash, (uint)p.O.YV);
            hash = Mix(hash, (uint)p.Life);
            hash = Mix(hash, (uint)p.Armor);
            hash = Mix(hash, (uint)p.St);
            hash = Mix(hash, (uint)p.Wpns);
            return hash;
        }

        public static uint Mix(uint hash, uint value)
        {
            hash ^= value & 0xFF;
            hash *= 16777619u;
            hash ^= (value >> 8) & 0xFF;
            hash *= 16777619u;
            hash ^= (value >> 16) & 0xFF;
            hash *= 16777619u;
            hash ^= (value >> 24) & 0xFF;
            hash *= 16777619u;
            return hash;
        }
    }
}
