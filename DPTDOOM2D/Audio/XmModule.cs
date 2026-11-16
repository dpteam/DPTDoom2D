//
// DPT.DOOM 2D — FastTracker 2 module loader.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.IO;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// A FastTracker 2 (XM) module: header, orders, patterns, instruments with
    /// samples.
    ///
    /// Two details of the format are easy to get wrong and both are handled here
    /// explicitly: sample lengths are in **bytes** (not sample points), and the
    /// envelope block is two 48-byte point tables followed by ten flag bytes.
    /// </summary>
    internal sealed class XmModule
    {
        public string Name = "";
        public int ChannelCount = 4;
        public int Length;
        public int RestartPosition;
        public readonly byte[] Orders = new byte[256];
        public int InitialSpeed = 6;
        public int InitialBpm = 125;
        public bool LinearFrequency;
        public XmPattern[] Patterns = new XmPattern[0];
        public XmInstrument[] Instruments = new XmInstrument[0];

        internal sealed class XmPattern
        {
            public int Rows = 64;
            public byte[] Note = new byte[0];
            public byte[] Instrument = new byte[0];
            public byte[] Volume = new byte[0];
            public byte[] Effect = new byte[0];
            public byte[] Parameter = new byte[0];
        }

        internal sealed class XmEnvelope
        {
            public readonly int[] PointX = new int[12];
            public readonly int[] PointY = new int[12];
            public int PointCount;
            public int SustainStart, SustainEnd, LoopStart, LoopEnd;
            public bool Enabled, Sustain, Loop;
        }

        internal sealed class XmSample
        {
            /// <summary>Decoded sample points (one per value, 16-bit collapsed to 8).</summary>
            public sbyte[] Data = new sbyte[0];
            /// <summary>Loop bounds in sample points, already converted from bytes.</summary>
            public int LoopStart;
            public int LoopLength;
            public bool Loop;
            public bool SixteenBit;
            public int Volume = 64;
            public int Finetune;
            public int Panning = 128;
            public int RelativeNote;
        }

        internal sealed class XmInstrument
        {
            public readonly int[] KeyMap = new int[96];
            public XmEnvelope Volume = new XmEnvelope();
            public XmEnvelope Panning = new XmEnvelope();
            public int Fadeout;
            public int VibratoType, VibratoSweep, VibratoDepth, VibratoRate;
            public XmSample[] Samples = new XmSample[0];
        }

        public static XmModule Load(string path)
        {
            return Load(File.ReadAllBytes(path));
        }

        public static XmModule Load(byte[] data)
        {
            const string signature = "Extended Module: ";
            if (data == null || data.Length < 80) return null;

            for (int i = 0; i < signature.Length; ++i)
                if (data[i] != (byte)signature[i]) return null;

            var module = new XmModule();

            module.Name = Latin1(data, 17, 20).TrimEnd('\0', ' ');

            int version = BitConverter.ToUInt16(data, 58);
            int headerSize = BitConverter.ToInt32(data, 60);
            int at = 64;

            module.Length = BitConverter.ToUInt16(data, at); at += 2;
            module.RestartPosition = BitConverter.ToUInt16(data, at); at += 2;
            module.ChannelCount = BitConverter.ToUInt16(data, at); at += 2;
            int patternCount = BitConverter.ToUInt16(data, at); at += 2;
            int instrumentCount = BitConverter.ToUInt16(data, at); at += 2;
            int flags = BitConverter.ToUInt16(data, at); at += 2;
            module.InitialSpeed = BitConverter.ToUInt16(data, at); at += 2;
            module.InitialBpm = BitConverter.ToUInt16(data, at); at += 2;

            module.LinearFrequency = (flags & 1) != 0;

            if (version < 0x0104)
            {
                // 1.02/1.03 files have no order table: the first pattern is the song.
                module.Length = 1;
                module.Orders[0] = 0;
            }
            else
            {
                Array.Copy(data, 64, module.Orders, 0, 256);
            }

            // The song header size counts from offset 60, so the first pattern
            // starts at 60 + headerSize (the 4-byte size field included).
            at = 60 + headerSize;

            if (module.ChannelCount <= 0 || module.ChannelCount > 64) module.ChannelCount = 4;
            if (patternCount < 0 || patternCount > 256) patternCount = 0;
            if (instrumentCount < 0 || instrumentCount > 128) instrumentCount = 0;

            module.Patterns = new XmPattern[patternCount];
            for (int p = 0; p < patternCount; ++p)
            {
                if (at + 9 > data.Length) { module.Patterns[p] = new XmPattern(); continue; }

                int patternHeaderSize = BitConverter.ToInt32(data, at);
                int packedType = data[at + 4];
                int rows = BitConverter.ToUInt16(data, at + 5);
                int packedSize = BitConverter.ToUInt16(data, at + 7);
                int start = at + patternHeaderSize;

                if (rows <= 0 || rows > 256) rows = 64;
                if (patternHeaderSize < 9) patternHeaderSize = 9;

                var pattern = new XmPattern();
                pattern.Rows = rows;
                int cells = rows * module.ChannelCount;
                pattern.Note = new byte[cells];
                pattern.Instrument = new byte[cells];
                pattern.Volume = new byte[cells];
                pattern.Effect = new byte[cells];
                pattern.Parameter = new byte[cells];

                int pos = start;
                int end = Math.Min(data.Length, start + packedSize);

                // FT2 always writes packing type 0 and always packs: a cell is
                // either one byte of flags plus the fields it names, or five plain
                // bytes. Treating type 0 as "unpacked" (as the old 1.02 format
                // used it) desynchronises the whole pattern.
                for (int c = 0; c < cells && pos < end; ++c)
                {
                    byte b = data[pos++];

                    if ((b & 0x80) != 0)
                    {
                        if ((b & 0x01) != 0 && pos < end) pattern.Note[c] = data[pos++];
                        if ((b & 0x02) != 0 && pos < end) pattern.Instrument[c] = data[pos++];
                        if ((b & 0x04) != 0 && pos < end) pattern.Volume[c] = data[pos++];
                        if ((b & 0x08) != 0 && pos < end) pattern.Effect[c] = data[pos++];
                        if ((b & 0x10) != 0 && pos < end) pattern.Parameter[c] = data[pos++];
                    }
                    else
                    {
                        pattern.Note[c] = b;
                        if (pos < end) pattern.Instrument[c] = data[pos++];
                        if (pos < end) pattern.Volume[c] = data[pos++];
                        if (pos < end) pattern.Effect[c] = data[pos++];
                        if (pos < end) pattern.Parameter[c] = data[pos++];
                    }
                }

                module.Patterns[p] = pattern;
                at = start + packedSize;
            }

            module.Instruments = new XmInstrument[instrumentCount];
            for (int n = 0; n < instrumentCount; ++n)
            {
                if (at + 29 > data.Length) { module.Instruments[n] = new XmInstrument(); continue; }

                int instrumentSize = BitConverter.ToInt32(data, at);
                int start = at;
                int sampleCount = BitConverter.ToUInt16(data, at + 27);

                var instrument = new XmInstrument();

                if (sampleCount > 0 && sampleCount <= 32)
                {
                    int p = at + 29;
                    int sampleHeaderSize = BitConverter.ToInt32(data, p); p += 4;

                    Array.Copy(data, p, instrument.KeyMap, 0, 96); p += 96;

                    ReadEnvelopes(data, ref p, instrument.Volume, instrument.Panning);

                    instrument.VibratoType = data[p];
                    instrument.VibratoSweep = data[p + 1];
                    instrument.VibratoDepth = data[p + 2];
                    instrument.VibratoRate = data[p + 3];
                    p += 4;

                    instrument.Fadeout = BitConverter.ToUInt16(data, p);

                    // The instrument header ends 2 bytes after the fadeout, and the
                    // sample headers follow it — they are not counted in the
                    // instrument size. Deriving the position from the size keeps
                    // this right for the 2-byte and the 22-byte reserved variants.
                    int headersAt = start + instrumentSize;
                    if (headersAt <= 0 || headersAt > data.Length) headersAt = p + 2 + 2;

                    instrument.Samples = new XmSample[sampleCount];

                    for (int s = 0; s < sampleCount; ++s)
                    {
                        int h = headersAt + s * sampleHeaderSize;
                        if (h + 40 > data.Length) { instrument.Samples[s] = new XmSample(); continue; }

                        var sample = new XmSample();

                        // These three are byte counts, not sample points.
                        int lengthBytes = BitConverter.ToInt32(data, h);
                        int loopStartBytes = BitConverter.ToInt32(data, h + 4);
                        int loopLengthBytes = BitConverter.ToInt32(data, h + 8);

                        sample.Volume = data[h + 12];
                        sample.Finetune = (sbyte)data[h + 13];
                        int type = data[h + 14];
                        sample.Loop = (type & 0x03) != 0;
                        sample.SixteenBit = (type & 0x10) != 0;
                        sample.Panning = data[h + 15];
                        sample.RelativeNote = (sbyte)data[h + 16];

                        int bytesPerPoint = sample.SixteenBit ? 2 : 1;
                        int points = lengthBytes / bytesPerPoint;
                        if (points < 0) points = 0;
                        if (points > 4 * 1024 * 1024) points = 0;

                        sample.LoopStart = loopStartBytes / bytesPerPoint;
                        sample.LoopLength = loopLengthBytes / bytesPerPoint;

                        instrument.Samples[s] = sample;
                        instrument.Samples[s].Data = new sbyte[points];
                    }

                    int sampleDataAt = headersAt + sampleCount * sampleHeaderSize;

                    for (int s = 0; s < sampleCount; ++s)
                    {
                        XmSample sample = instrument.Samples[s];
                        int bytesPerPoint = sample.SixteenBit ? 2 : 1;
                        int bytes = sample.Data.Length * bytesPerPoint;

                        if (sampleDataAt + bytes <= data.Length)
                            Decode(data, sampleDataAt, sample.Data, sample.SixteenBit);

                        sampleDataAt += bytes;
                    }

                    // The next instrument starts after the sample data.
                    at = sampleDataAt;
                }
                else
                {
                    at = start + instrumentSize;
                }

                module.Instruments[n] = instrument;
            }

            return module;
        }

        /// <summary>
        /// The envelope block: twelve points of four bytes each (X word, Y word)
        /// for volume, then the same for panning, then ten one-byte flags.
        /// Reading all the X values and then all the Y values — the obvious loop —
        /// walks off the end of the header.
        /// </summary>
        private static void ReadEnvelopes(byte[] data, ref int p, XmEnvelope volume, XmEnvelope panning)
        {
            for (int i = 0; i < 12; ++i)
            {
                volume.PointX[i] = BitConverter.ToUInt16(data, p + i * 4);
                volume.PointY[i] = BitConverter.ToUInt16(data, p + i * 4 + 2);
                panning.PointX[i] = BitConverter.ToUInt16(data, p + 48 + i * 4);
                panning.PointY[i] = BitConverter.ToUInt16(data, p + 48 + i * 4 + 2);
            }

            volume.PointCount = data[p + 96];
            panning.PointCount = data[p + 97];
            volume.SustainStart = data[p + 98];
            volume.LoopStart = data[p + 99];
            volume.LoopEnd = data[p + 100];
            panning.SustainStart = data[p + 101];
            panning.LoopStart = data[p + 102];
            panning.LoopEnd = data[p + 103];

            int volumeFlags = data[p + 104];
            int panningFlags = data[p + 105];

            volume.Enabled = (volumeFlags & 1) != 0;
            volume.Sustain = (volumeFlags & 2) != 0;
            volume.Loop = (volumeFlags & 4) != 0;

            panning.Enabled = (panningFlags & 1) != 0;
            panning.Sustain = (panningFlags & 2) != 0;
            panning.Loop = (panningFlags & 4) != 0;

            volume.SustainEnd = volume.SustainStart;
            panning.SustainEnd = panning.SustainStart;

            p += 106;
        }

        /// <summary>XM stores samples as deltas: each value is relative to the previous one.</summary>
        private static void Decode(byte[] data, int at, sbyte[] destination, bool sixteenBit)
        {
            int previous = 0;

            if (sixteenBit)
            {
                int count = Math.Min(destination.Length, (data.Length - at) / 2);
                for (int i = 0; i < count; ++i)
                {
                    previous += (short)BitConverter.ToUInt16(data, at + i * 2);
                    int value = previous >> 8;
                    if (value > 127) value = 127;
                    else if (value < -128) value = -128;
                    destination[i] = (sbyte)value;
                }
            }
            else
            {
                int count = Math.Min(destination.Length, data.Length - at);
                for (int i = 0; i < count; ++i)
                {
                    previous += (sbyte)data[at + i];
                    destination[i] = (sbyte)previous;
                }
            }
        }

        private static string Latin1(byte[] data, int at, int count)
        {
            return System.Text.Encoding.GetEncoding(28591).GetString(data, at, count);
        }
    }
}
