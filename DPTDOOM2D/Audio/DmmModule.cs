//
// DPT.DOOM 2D — Doom Music Module (DMM) loader.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// Reads the DMM songs stored in doom2d.wad and turns them into an
    /// <see cref="XmModule"/> the existing replayer can play.
    ///
    /// This is the fallback for when the Music folder is missing or a track has
    /// no XM conversion: the WAD has always carried the whole soundtrack, so the
    /// game never has to run silent just because a file is absent.
    ///
    /// The format (documented in refs/D2D_Also/"DMM Format Info.txt" and in
    /// dmm2xm's source, which is the authoritative reader):
    ///
    ///   "DMM\0"   version u8   patternCount u8   noteCount u16
    ///   notes     noteCount * 4 bytes: note, instrument, volume, delay
    ///   orders    count u8, then count bytes (255 = restart at the next entry)
    ///   samples   count u8, then count * 16 bytes: unused, name[15]
    ///
    /// The notes are not a row-major pattern: they are **eight parallel
    /// channels**, each a list of events terminated by a volume==0x80 note. Within
    /// a channel every event's delay is a number of ticks (1/64 s, 0 meaning 256)
    /// before the next event. That is why the reader below walks channels rather
    /// than rows.
    ///
    /// Sample data does not live in the song: each instrument names a DMI lump
    /// elsewhere in the WAD ("DMI0001"), which the caller supplies.
    /// </summary>
    internal static class DmmModule
    {
        /// <summary>Ticks per second in a DMM song.</summary>
        public const int TicksPerSecond = 64;

        /// <summary>DMM is a fixed eight-channel format.</summary>
        public const int Channels = 8;

        private const int MaxRowsPerPattern = 256;

        private sealed class Event
        {
            public int Row;
            public int Channel;
            public int Note;        // DMM note number, 24 = C-0; 0 = note cut
            public int Instrument;  // 1-based, 0 = none
            public int Volume;      // 0..127
            public bool Cut;
        }

        public static bool LooksLikeDmm(byte[] data)
        {
            return data != null && data.Length > 12
                && data[0] == (byte)'D' && data[1] == (byte)'M' && data[2] == (byte)'M'
                && data[3] == 0;
        }

        /// <summary>
        /// Converts a DMM song. <paramref name="sampleLoader"/> is called with the
        /// instrument name and must return the DMI lump bytes, or null.
        /// </summary>
        public static XmModule Load(byte[] data, Func<string, byte[]> sampleLoader)
        {
            if (!LooksLikeDmm(data)) return null;

            int at = 4;
            int version = data[at++];
            int patternCount = data[at++];
            int noteCount = BitConverter.ToUInt16(data, at); at += 2;

            if (patternCount <= 0 || patternCount > 64) return null;
            if (noteCount <= 0 || at + noteCount * 4 > data.Length) return null;

            // ---- notes -------------------------------------------------------
            var notes = new byte[noteCount * 4];
            Buffer.BlockCopy(data, at, notes, 0, notes.Length);
            at += notes.Length;

            // ---- order list --------------------------------------------------
            var orders = new List<int>();
            if (at < data.Length)
            {
                int count = data[at++];
                for (int i = 0; i < count && at < data.Length; ++i) orders.Add(data[at++]);
            }

            // ---- instruments -------------------------------------------------
            var names = new List<string>();
            if (at < data.Length)
            {
                int count = data[at++];
                for (int i = 0; i < count; ++i)
                {
                    if (at + 16 > data.Length) break;

                    // 1 unused byte, then a 15-byte name.
                    int length = 0;
                    while (length < 15 && data[at + 1 + length] != 0) ++length;
                    names.Add(System.Text.Encoding.ASCII.GetString(data, at + 1, length));

                    at += 16;
                }
            }

            var module = new XmModule();
            module.Name = "DMM";
            module.ChannelCount = Channels;
            module.LinearFrequency = true;

            // One row = one DMM tick: at speed 1 the XM replayer advances a row
            // every 2.5/bpm seconds, so bpm 160 gives exactly 1/64 s per row.
            module.InitialSpeed = 1;
            module.InitialBpm = TicksPerSecond * 5 / 2;

            // ---- samples -----------------------------------------------------
            module.Instruments = new XmModule.XmInstrument[names.Count];
            for (int i = 0; i < names.Count; ++i)
            {
                var instrument = new XmModule.XmInstrument();
                byte[] dmi = sampleLoader == null ? null : sampleLoader(names[i]);

                if (dmi != null)
                {
                    var sample = ReadDmi(dmi);
                    if (sample != null)
                    {
                        instrument.Samples = new XmModule.XmSample[] { sample };
                        for (int k = 0; k < instrument.KeyMap.Length; ++k) instrument.KeyMap[k] = 0;
                    }
                }

                module.Instruments[i] = instrument;
            }

            // ---- patterns ----------------------------------------------------
            var patterns = new List<XmModule.XmPattern>();
            var order = new List<byte>();

            int note = 0;
            for (int p = 0; p < patternCount && note < noteCount; ++p)
            {
                var events = new List<Event>();
                int length = ReadPattern(notes, ref note, events);
                if (length <= 0) length = 1;

                int first = patterns.Count;
                BuildPatterns(module, events, length, patterns);

                if (first < 256) order.Add((byte)first);
            }

            module.Patterns = patterns.ToArray();

            // The DMM order list is authoritative when it is present; 255 means
            // "restart at the entry after this one", which is the loop point.
            module.Length = 0;
            module.RestartPosition = 0;

            bool ordered = false;
            for (int i = 0; i < orders.Count; ++i)
            {
                int value = orders[i];
                if (value == 255)
                {
                    module.RestartPosition = module.Length;
                    ordered = true;
                    continue;
                }

                if (value >= patternCount) continue;
                if (module.Length >= 256) break;

                // Map the DMM pattern to its first XM pattern.
                if (value < order.Count) module.Orders[module.Length++] = order[value];
            }

            if (!ordered || module.Length == 0)
            {
                // No usable order list: play the patterns in sequence.
                module.Length = Math.Min(order.Count, 256);
                for (int i = 0; i < module.Length; ++i) module.Orders[i] = order[i];
                module.RestartPosition = 0;
            }

            if (module.RestartPosition >= module.Length) module.RestartPosition = 0;

            return module;
        }

        /// <summary>
        /// Reads one DMM pattern: eight parallel channels of events, each channel
        /// closed by a volume==0x80 note. Returns the pattern length in ticks,
        /// which is the longest channel.
        /// </summary>
        private static int ReadPattern(byte[] notes, ref int at, List<Event> events)
        {
            int totalNotes = notes.Length / 4;
            int row = 0;
            int longest = 0;
            int channel = 0;

            while (at < totalNotes && channel < Channels)
            {
                int note = notes[at * 4];
                int instrument = notes[at * 4 + 1];
                int volume = notes[at * 4 + 2];
                int delay = notes[at * 4 + 3];
                ++at;

                if (volume == 0x80)
                {
                    // End of this channel's data: the next note starts a new voice.
                    if (row > longest) longest = row;
                    ++channel;
                    row = 0;
                    continue;
                }

                if (note == 0xFE)
                {
                    // A rest: skip that many ticks without playing anything.
                    int rest = (instrument << 8) | delay;
                    row += rest == 0 ? 256 : rest;
                    continue;
                }

                if (note != 0xFF)
                {
                    var e = new Event();
                    e.Row = row;
                    e.Channel = channel;
                    e.Note = note;
                    e.Instrument = instrument;
                    e.Volume = volume;
                    e.Cut = volume == 0;
                    events.Add(e);
                }

                row += delay == 0 ? 256 : delay;
            }

            if (row > longest) longest = row;

            // Guard against a malformed file: a pattern cannot be endless.
            if (longest > TicksPerSecond * 600) longest = TicksPerSecond * 600;
            return longest;
        }

        /// <summary>Splits a pattern timeline into XM patterns of at most 256 rows.</summary>
        private static void BuildPatterns(XmModule module, List<Event> events, int length,
            List<XmModule.XmPattern> patterns)
        {
            int patternCount = (length + MaxRowsPerPattern - 1) / MaxRowsPerPattern;
            if (patternCount < 1) patternCount = 1;

            var made = new XmModule.XmPattern[patternCount];
            for (int i = 0; i < patternCount; ++i)
            {
                var pattern = new XmModule.XmPattern();
                pattern.Rows = MaxRowsPerPattern;
                int cells = pattern.Rows * Channels;
                pattern.Note = new byte[cells];
                pattern.Instrument = new byte[cells];
                pattern.Volume = new byte[cells];
                pattern.Effect = new byte[cells];
                pattern.Parameter = new byte[cells];
                made[i] = pattern;
            }

            for (int i = 0; i < events.Count; ++i)
            {
                Event e = events[i];
                if (e.Row < 0) continue;

                int index = e.Row / MaxRowsPerPattern;
                if (index >= patternCount) index = patternCount - 1;

                int row = e.Row % MaxRowsPerPattern;
                XmModule.XmPattern pattern = made[index];

                int channel = e.Channel;
                if (channel < 0) channel = 0;
                if (channel >= Channels) channel = Channels - 1;

                int cell = row * Channels + channel;

                if (e.Cut)
                {
                    pattern.Note[cell] = 97; // key off
                    continue;
                }

                int xmNote = e.Note - 23; // DMM note 24 is XM note 1 (C-0)
                if (xmNote < 1) xmNote = 1;
                if (xmNote > 96) xmNote = 96;

                pattern.Note[cell] = (byte)xmNote;
                pattern.Instrument[cell] = (byte)Math.Min(255, e.Instrument);

                // Set volume through effect C: the replayer applies the pattern's
                // volume column before the note starts and the sample's default
                // would overwrite it.
                pattern.Effect[cell] = 0xC;
                pattern.Parameter[cell] = (byte)Math.Min(64, e.Volume / 2);
            }

            // Trim the last pattern so the song does not end with silence.
            int usedRows = length - (patternCount - 1) * MaxRowsPerPattern;
            if (usedRows > 0 && usedRows < MaxRowsPerPattern) made[patternCount - 1].Rows = usedRows;

            for (int i = 0; i < patternCount; ++i) patterns.Add(made[i]);
        }

        /// <summary>
        /// Reads a DMI sample: an 8-byte header (length, rate, loop start, loop
        /// length, all 16-bit) followed by signed 8-bit data.
        /// </summary>
        private static XmModule.XmSample ReadDmi(byte[] dmi)
        {
            if (dmi == null || dmi.Length < 8) return null;

            int length = BitConverter.ToUInt16(dmi, 0);
            int rate = BitConverter.ToUInt16(dmi, 2);
            int loopStart = BitConverter.ToUInt16(dmi, 4);
            int loopLength = BitConverter.ToUInt16(dmi, 6);

            if (length <= 0 || 8 + length > dmi.Length) return null;
            if (rate < 4000 || rate > 48000) rate = 11025;

            var sample = new XmModule.XmSample();
            sample.Data = new sbyte[length];
            for (int i = 0; i < length; ++i) sample.Data[i] = (sbyte)dmi[8 + i];

            sample.Volume = 64;
            sample.Panning = 128;
            sample.Loop = loopLength > 0 && loopStart + loopLength <= length;
            sample.LoopStart = loopStart;
            sample.LoopLength = loopLength;

            // A DMM note is pitched from the sample's own rate, while XM pitches
            // from 8363 Hz at C-4. Split the ratio into whole semitones and a
            // finetune so nothing is lost to rounding.
            double semitones = 48.0 + 12.0 * Math.Log((double)rate / 8363.0, 2.0);
            int relative = (int)Math.Round(semitones);
            int finetune = (int)Math.Round((semitones - relative) * 128.0);

            sample.RelativeNote = relative;
            sample.Finetune = finetune;

            return sample;
        }
    }
}
