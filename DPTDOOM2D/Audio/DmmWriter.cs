//
// DPT.DOOM 2D — Doom Music Module writer.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// One event in a DMM channel.
    ///
    /// <see cref="At"/> is an absolute tick inside the pattern, not a delay. The file
    /// format stores a delay *after* each event and encodes zero as 256, so building
    /// songs in absolute time and letting the writer do the subtraction is the only
    /// way to keep that off-by-256 out of every caller. The first version of this
    /// writer got it wrong and every note landed 256 ticks late.
    /// </summary>
    public sealed class DmmEvent
    {
        /// <summary>Absolute tick within the pattern, 0-based.</summary>
        public int At;

        /// <summary>DMM note: 24 is C-0, so a MIDI note is DMM note - 12.</summary>
        public int Note;

        /// <summary>1-based instrument, 0 for none.</summary>
        public int Instrument;

        /// <summary>0..127; 0 is a note cut.</summary>
        public int Volume;

        /// <summary>A rest: holds the channel silent up to <see cref="At"/>.</summary>
        public bool Rest;
    }

    /// <summary>One DMM pattern: eight parallel channels.</summary>
    public sealed class DmmPattern
    {
        public readonly List<DmmEvent>[] Channels = new List<DmmEvent>[DmmModule.Channels];

        /// <summary>
        /// Length in ticks. The writer pads every channel to this, because the reader
        /// takes the pattern length from its longest channel.
        /// </summary>
        public int Length;

        public DmmPattern()
        {
            for (int i = 0; i < Channels.Length; ++i) Channels[i] = new List<DmmEvent>();
        }
    }

    /// <summary>A DMM song in memory, ready to be written.</summary>
    public sealed class DmmSong
    {
        public int Version;

        public readonly List<DmmPattern> Patterns = new List<DmmPattern>();

        /// <summary>Order list; 255 is the loop point, as in the original files.</summary>
        public readonly List<byte> Orders = new List<byte>();

        /// <summary>Instrument names, 15 characters at most: DMI lump names.</summary>
        public readonly List<string> Instruments = new List<string>();
    }

    /// <summary>
    /// Writes the format <see cref="DmmModule"/> reads.
    ///
    /// The reader has existed since the first release — it is how the WAD's own
    /// soundtrack plays. A writer is what turns a song from anywhere else (a MIDI
    /// file, a generator) into a lump this game can play, which is the whole point of
    /// FreeDooM2D and AIDooM2D: those data sets have no DMM of their own.
    ///
    /// Layout, from the reader's own documentation:
    ///
    ///   "DMM\0"   version u8   patternCount u8   noteCount u16
    ///   notes     noteCount * 4: note, instrument, volume, delay
    ///   orders    count u8, then count bytes (255 = restart here)
    ///   samples   count u8, then count * 16: unused, name[15]
    ///
    /// One trap in that layout: a delay of 0 means 256 ticks, not "no wait". A note
    /// at the very start of a pattern therefore needs delay 1, and the writer emits
    /// rests rather than zeroes when it has to.
    /// </summary>
    public static class DmmWriter
    {
        /// <summary>Longest delay one event can express; 0 in the file means 256.</summary>
        private const int MaxDelay = 255;

        public static byte[] Write(DmmSong song)
        {
            if (song == null) throw new ArgumentNullException("song");

            var notes = new List<byte>(song.Patterns.Count * 64);

            for (int p = 0; p < song.Patterns.Count; ++p)
                AppendPattern(notes, song.Patterns[p]);

            var output = new List<byte>(notes.Count + 64);

            output.Add((byte)'D');
            output.Add((byte)'M');
            output.Add((byte)'M');
            output.Add(0);
            output.Add((byte)song.Version);
            output.Add((byte)Math.Min(255, song.Patterns.Count));

            output.Add((byte)(notes.Count / 4 & 0xFF));
            output.Add((byte)((notes.Count / 4 >> 8) & 0xFF));

            output.AddRange(notes);

            // Order list. A pattern that is not mentioned still exists; the player
            // follows this list, and 255 restarts the song at the next entry.
            int orderCount = Math.Min(255, song.Orders.Count);
            output.Add((byte)orderCount);
            for (int i = 0; i < orderCount; ++i) output.Add(song.Orders[i]);

            // Instrument names: one unused byte, then fifteen characters.
            int instrumentCount = Math.Min(255, song.Instruments.Count);
            output.Add((byte)instrumentCount);

            for (int i = 0; i < instrumentCount; ++i)
            {
                output.Add(0);

                string name = song.Instruments[i] ?? "";
                for (int c = 0; c < 15; ++c)
                    output.Add(c < name.Length ? (byte)name[c] : (byte)0);
            }

            return output.ToArray();
        }

        /// <summary>
        /// Writes the eight channels of one pattern, each closed by a volume 0x80
        /// event, which is how the reader knows a channel has ended.
        /// </summary>
        private static void AppendPattern(List<byte> notes, DmmPattern pattern)
        {
            int length = pattern.Length;

            for (int c = 0; c < DmmModule.Channels; ++c)
            {
                List<DmmEvent> channel = c < pattern.Channels.Length
                    ? pattern.Channels[c]
                    : new List<DmmEvent>();

                if (channel.Count > 0) channel.Sort(ByTime);

                int at = 0;

                for (int e = 0; e < channel.Count; ++e)
                {
                    DmmEvent ev = channel[e];

                    // An event at or past the pattern's end belongs to the next
                    // pattern; writing it here would stretch this one by a tick and
                    // shift the whole song.
                    if (length > 0 && ev.At >= length) continue;

                    // A channel is monophonic: two events on the same tick cannot both
                    // be encoded, because a delay of 0 would be read as 256. The later
                    // one wins, which is what a listener hears anyway.
                    if (ev.At <= at && e > 0) continue;

                    int gap = ev.At - at;
                    if (gap < 0) gap = 0;

                    EmitGap(notes, gap);

                    if (ev.Rest)
                    {
                        at = ev.At;
                        continue;
                    }

                    notes.Add((byte)Math.Max(0, Math.Min(255, ev.Note)));
                    notes.Add((byte)Math.Max(0, Math.Min(255, ev.Instrument)));
                    notes.Add((byte)Math.Max(0, Math.Min(127, ev.Volume)));

                    // The delay that follows the event: the gap to the next one is
                    // written by the next iteration, so this one carries the time up
                    // to where the reader will be when it reads the next event.
                    notes.Add(1);

                    at = ev.At + 1;
                }

                // Pad to the pattern length so every channel ends together: the
                // reader takes the pattern length from the longest one.
                if (length > at) EmitGap(notes, length - at);

                // Terminator: volume 0x80.
                notes.Add(0);
                notes.Add(0);
                notes.Add(0x80);
                notes.Add(0);
            }
        }

        private static int ByTime(DmmEvent a, DmmEvent b)
        {
            return a.At.CompareTo(b.At);
        }

        /// <summary>
        /// Writes a wait of <paramref name="ticks"/> before the next event.
        ///
        /// Rests are the only way to express more than 255 ticks: the format's delay
        /// byte is small and 0 means 256, so a long gap is a rest event followed by
        /// the remainder.
        /// </summary>
        private static void EmitGap(List<byte> notes, int ticks)
        {
            while (ticks > 0)
            {
                int step = ticks > MaxDelay ? MaxDelay : ticks;

                // A rest is note 0xFE with a 16-bit length split across the
                // instrument and delay bytes.
                notes.Add(0xFE);
                notes.Add((byte)((step >> 8) & 0xFF));
                notes.Add(0);
                notes.Add((byte)(step & 0xFF));

                ticks -= step;
            }
        }
    }
}
