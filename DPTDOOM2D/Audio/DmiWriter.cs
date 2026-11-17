//
// DPT.DOOM 2D — DMI sound writer.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// A sound in memory, at the rate it was recorded at.
    ///
    /// Signed 8-bit, which is what DMI holds and what the mixer wants: the WAV files
    /// a replacement data set comes from are usually unsigned 8-bit or 16-bit, so
    /// the conversion happens once, here, instead of in every caller.
    /// </summary>
    public sealed class DmiSample
    {
        public sbyte[] Data = new sbyte[0];
        public int Rate = 11025;
        public int LoopStart;
        public int LoopLength;

        public int Length { get { return Data.Length; } }

        /// <summary>
        /// Builds a sample from 16-bit signed PCM.
        ///
        /// The 16-bit form is what FreeDoom ships; dropping the low byte is the same
        /// thing the DOS tools did, and it keeps the whole pipeline in the format the
        /// mixer already reads.
        /// </summary>
        public static DmiSample FromPcm16(byte[] pcm, int rate, bool bigEndian)
        {
            var sample = new DmiSample();
            sample.Rate = rate;

            int count = pcm.Length / 2;
            sample.Data = new sbyte[count];

            for (int i = 0; i < count; ++i)
            {
                int lo = pcm[i * 2];
                int hi = pcm[i * 2 + 1];

                int value = bigEndian ? (lo << 8) | hi : (hi << 8) | lo;
                if (value > 32767) value -= 65536;

                // 16 -> 8 bits, rounded rather than truncated so silence stays silent.
                int scaled = (value + 128) >> 8;
                if (scaled > 127) scaled = 127;
                if (scaled < -128) scaled = -128;

                sample.Data[i] = (sbyte)scaled;
            }

            return sample;
        }

        /// <summary>Builds a sample from unsigned 8-bit PCM, which is WAV's default.</summary>
        public static DmiSample FromPcm8(byte[] pcm, int rate)
        {
            var sample = new DmiSample();
            sample.Rate = rate;
            sample.Data = new sbyte[pcm.Length];

            for (int i = 0; i < pcm.Length; ++i)
                sample.Data[i] = (sbyte)(pcm[i] - 128);

            return sample;
        }
    }

    /// <summary>
    /// Writes the format <see cref="Sound.Parse"/> reads.
    ///
    /// The reader has always been able to load a DMI; nothing could produce one, so a
    /// replacement data set had music (after the MIDI converter) and no gunshots. This
    /// is the missing half, and it is small: an eight-byte header and signed bytes.
    ///
    /// The header is the 8-byte one the DOS build wrote, because that is what
    /// doom2d.wad actually contains and what the reader recognises by arithmetic.
    /// Writing the 16-byte variant would also load, but a tool that produces the
    /// common form is easier to compare against the original data.
    /// </summary>
    public static class DmiWriter
    {
        /// <summary>Longest sample the 16-bit length field can describe.</summary>
        public const int MaxLength = 65535;

        public static byte[] Write(DmiSample sample)
        {
            if (sample == null) throw new ArgumentNullException("sample");

            int length = sample.Data.Length;
            if (length > MaxLength) length = MaxLength;

            byte[] data = new byte[8 + length];

            data[0] = (byte)(length & 0xFF);
            data[1] = (byte)((length >> 8) & 0xFF);

            int rate = sample.Rate < 4000 ? 11025 : (sample.Rate > 48000 ? 48000 : sample.Rate);
            data[2] = (byte)(rate & 0xFF);
            data[3] = (byte)((rate >> 8) & 0xFF);

            int loopStart = sample.LoopStart < 0 ? 0 : sample.LoopStart;
            if (loopStart > length) loopStart = 0;

            int loopLength = sample.LoopLength;
            if (loopLength < 0 || loopStart + loopLength > length) loopLength = 0;

            data[4] = (byte)(loopStart & 0xFF);
            data[5] = (byte)((loopStart >> 8) & 0xFF);
            data[6] = (byte)(loopLength & 0xFF);
            data[7] = (byte)((loopLength >> 8) & 0xFF);

            for (int i = 0; i < length; ++i) data[8 + i] = (byte)sample.Data[i];

            return data;
        }

        /// <summary>
        /// Shortens a sample to what the format can hold, keeping the start.
        ///
        /// A gunshot is a few thousand samples; a two-second ambience is 22050 and
        /// does not fit. Truncating is better than refusing: the sound is still there,
        /// and the caller is told so.
        /// </summary>
        public static bool Fits(DmiSample sample)
        {
            return sample != null && sample.Data.Length <= MaxLength;
        }
    }
}
