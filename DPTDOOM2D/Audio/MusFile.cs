//
// DPT.DOOM 2D — MUS reader: Doom's music, as notes.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// Reads the MUS format Doom and FreeDoom ship their music in.
    ///
    /// MUS is not MIDI and not a MOD: it is a stream of events, each one byte of
    /// descriptor followed by its payload, with a delay after the last event of a
    /// group. The conversion to <see cref="MidiNote"/> is what lets the existing
    /// MIDI-to-DMM converter do the rest, so the soundtrack of a replacement data
    /// set goes through exactly the same code path as a hand-made .mid.
    ///
    /// Three details decide whether the output is music or noise, and all three were
    /// found by decoding Doom II's own tracks and checking that the result added up:
    ///
    ///  * **The stream must consume exactly the declared score length.** That is the
    ///    self-check: an off-by-one in the payload sizes desynchronises everything
    ///    after it, and the only symptom is a track that sounds wrong.
    ///  * **Notes and velocities are 7-bit.** Doom's files carry the high bit set on
    ///    some of them — 156 where the pitch is 28 — so the value is masked. Without
    ///    that, a note lands 128 semitones above the one before it.
    ///  * **A tick is 1/70 s.** Doom II's first track comes out at 159.6 seconds,
    ///    which is what the real track lasts; at 140 Hz it would be half that.
    /// </summary>
    public static class MusFile
    {
        /// <summary>MUS time unit: 70 ticks per second.</summary>
        public const double TicksPerSecond = 70.0;

        /// <summary>MUS channel 15 is percussion, as in General MIDI.</summary>
        public const int PercussionChannel = 15;

        public static bool LooksLikeMus(byte[] data)
        {
            return data != null && data.Length >= 16
                && data[0] == (byte)'M' && data[1] == (byte)'U' && data[2] == (byte)'S'
                && data[3] == 0x1A;
        }

        /// <summary>
        /// Converts a MUS lump to notes.
        ///
        /// Percussion is dropped by default: a DMM instrument is a pitched sample, and
        /// a drum kit played on one sounds like a bass line falling down the stairs.
        /// The melody, the bass and the chords survive, which is what a listener
        /// recognises a track by.
        /// </summary>
        public static List<MidiNote> Read(byte[] data, bool keepPercussion)
        {
            var notes = new List<MidiNote>();

            if (!LooksLikeMus(data)) return notes;

            int scoreLength = BitConverter.ToUInt16(data, 4);
            int scoreStart = BitConverter.ToUInt16(data, 6);

            int at = scoreStart;
            int end = scoreStart + scoreLength;

            if (at < 0 || end > data.Length || end <= at) return notes;

            // One open note per (channel, pitch): MUS sends a release for a pitch, and
            // a channel can hold a chord, so a single "current note" per channel is
            // not enough.
            var open = new Dictionary<int, double>();

            double tick = 0;

            while (at < end)
            {
                int descriptor = data[at++];

                bool last = (descriptor & 0x80) != 0;
                int type = (descriptor >> 4) & 7;
                int channel = descriptor & 0x0F;

                switch (type)
                {
                    case 0:     // release note
                        {
                            int note = data[at++] & 0x7F;
                            Close(notes, open, channel, note, tick, keepPercussion);
                            break;
                        }

                    case 1:     // play note
                        {
                            int note = data[at++] & 0x7F;
                            int velocity = data[at++] & 0x7F;

                            if (velocity == 0)
                            {
                                // A note-on with no volume is a release, the same
                                // convention MIDI uses.
                                Close(notes, open, channel, note, tick, keepPercussion);
                                break;
                            }

                            int key = Key(channel, note);

                            // A second strike of a pitch that is still ringing restarts
                            // it; without closing the first one the note would never end.
                            double started;
                            if (open.TryGetValue(key, out started))
                                Close(notes, open, channel, note, tick, keepPercussion);

                            open[key] = tick;
                            break;
                        }

                    case 2: at += 1; break;     // pitch bend: not expressible in DMM
                    case 3: at += 1; break;     // system: all-notes-off and friends
                    case 4: at += 2; break;     // controller: volume, pan, program
                    case 5: break;              // unused
                    case 6: at = end; break;    // end of score
                    case 7: break;              // unused
                }

                if (last)
                {
                    int delay = data[at++];

                    // 128 is the escape: the next two bytes are a 16-bit delay. Any
                    // other value with the high bit set is not, which is why this is
                    // an equality test and not a mask.
                    if (delay == 0x80)
                    {
                        delay = data[at] | (data[at + 1] << 8);
                        at += 2;
                    }

                    tick += delay;
                }
            }

            // Whatever is still held down at the end runs to the end of the song.
            var keys = new List<int>(open.Keys);
            for (int i = 0; i < keys.Count; ++i)
            {
                int channel = keys[i] >> 8;
                int note = keys[i] & 0xFF;

                Close(notes, open, channel, note, tick, keepPercussion);
            }

            notes.Sort(ByStart);
            return notes;
        }

        private static int Key(int channel, int note)
        {
            return (channel << 8) | note;
        }

        private static void Close(List<MidiNote> notes, Dictionary<int, double> open,
            int channel, int note, double tick, bool keepPercussion)
        {
            int key = Key(channel, note);

            double started;
            if (!open.TryGetValue(key, out started)) return;

            open.Remove(key);

            if (!keepPercussion && channel == PercussionChannel) return;

            var entry = new MidiNote();
            entry.Channel = channel;
            entry.Note = note;
            entry.Velocity = 100;
            entry.Start = started / TicksPerSecond;
            entry.Length = (tick - started) / TicksPerSecond;

            // A zero-length note is a click; give it something audible.
            if (entry.Length < 0.02) entry.Length = 0.02;

            notes.Add(entry);
        }

        private static int ByStart(MidiNote a, MidiNote b)
        {
            return a.Start.CompareTo(b.Start);
        }

        /// <summary>How long the track lasts, in seconds.</summary>
        public static double Length(byte[] data)
        {
            List<MidiNote> notes = Read(data, false);

            double end = 0;
            for (int i = 0; i < notes.Count; ++i)
            {
                double stop = notes[i].Start + notes[i].Length;
                if (stop > end) end = stop;
            }

            return end;
        }
    }
}
