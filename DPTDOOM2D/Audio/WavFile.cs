//
// DPT.DOOM 2D — RIFF/WAVE reader, for turning a sound file into a DMI.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// The smallest WAV reader that gets a sound out of a replacement data set.
    ///
    /// Handles PCM only — 8-bit unsigned and 16-bit signed — because that is what
    /// FreeDoom ships and what a converted sound is. Everything else (float, ADPCM,
    /// compressed formats) is refused rather than half-read: a converter that plays
    /// noise is worse than one that says "unsupported".
    /// </summary>
    public static class WavFile
    {
        public static bool LooksLikeWav(byte[] data)
        {
            return data != null && data.Length > 12
                && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
                && data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E';
        }

        /// <summary>
        /// Reads a PCM WAV. Returns null with a reason when the file is not something
        /// this reader understands.
        /// </summary>
        public static DmiSample Read(byte[] data, out string reason)
        {
            reason = null;

            if (!LooksLikeWav(data))
            {
                reason = "не WAV";
                return null;
            }

            int at = 12;
            int format = 0, channels = 0, rate = 0, bits = 0;
            int dataAt = -1, dataLength = 0;

            while (at + 8 <= data.Length)
            {
                string id = new string(new char[] { (char)data[at], (char)data[at + 1],
                    (char)data[at + 2], (char)data[at + 3] });

                int length = BitConverter.ToInt32(data, at + 4);
                if (length < 0) { reason = "битый размер блока"; return null; }

                int body = at + 8;
                if (body + length > data.Length) length = data.Length - body;

                if (id == "fmt ")
                {
                    if (length < 16) { reason = "короткий fmt"; return null; }

                    format = BitConverter.ToUInt16(data, body);
                    channels = BitConverter.ToUInt16(data, body + 2);
                    rate = BitConverter.ToInt32(data, body + 4);
                    bits = BitConverter.ToUInt16(data, body + 14);
                }
                else if (id == "data")
                {
                    dataAt = body;
                    dataLength = length;
                }

                // Chunks are word-aligned.
                at = body + length + (length & 1);
            }

            if (dataAt < 0 || dataLength <= 0)
            {
                reason = "нет блока data";
                return null;
            }

            if (format != 1)
            {
                reason = "не PCM (формат " + format + ")";
                return null;
            }

            if (rate < 4000 || rate > 48000)
            {
                reason = "частота " + rate + " Гц не поддерживается";
                return null;
            }

            var pcm = new byte[dataLength];
            Buffer.BlockCopy(data, dataAt, pcm, 0, dataLength);

            // Stereo is mixed down: DMI is mono, and a shot played in one ear only
            // would be worse than a shot played in both.
            if (channels == 2)
            {
                if (bits == 16) pcm = Downmix16(pcm);
                else pcm = Downmix8(pcm);
            }
            else if (channels != 1)
            {
                reason = "каналов " + channels + ", поддерживается 1 или 2";
                return null;
            }

            DmiSample sample;

            if (bits == 16) sample = DmiSample.FromPcm16(pcm, rate, false);
            else if (bits == 8) sample = DmiSample.FromPcm8(pcm, rate);
            else
            {
                reason = "разрядность " + bits + ", поддерживается 8 или 16";
                return null;
            }

            return sample;
        }

        private static byte[] Downmix16(byte[] stereo)
        {
            int frames = stereo.Length / 4;
            var mono = new byte[frames * 2];

            for (int i = 0; i < frames; ++i)
            {
                int left = BitConverter.ToInt16(stereo, i * 4);
                int right = BitConverter.ToInt16(stereo, i * 4 + 2);
                int mixed = (left + right) / 2;

                mono[i * 2] = (byte)(mixed & 0xFF);
                mono[i * 2 + 1] = (byte)((mixed >> 8) & 0xFF);
            }

            return mono;
        }

        private static byte[] Downmix8(byte[] stereo)
        {
            int frames = stereo.Length / 2;
            var mono = new byte[frames];

            for (int i = 0; i < frames; ++i)
                mono[i] = (byte)((stereo[i * 2] + stereo[i * 2 + 1]) / 2);

            return mono;
        }
    }
}
