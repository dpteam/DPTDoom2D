//
// DPT.DOOM 2D — Standard MIDI file reader, for turning a song into a DMM.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;

namespace DPTDOOM2D.Audio
{
    /// <summary>One note as it appears in a MIDI file, in seconds from the start.</summary>
    public struct MidiNote
    {
        public double Start;
        public double Length;
        public int Channel;
        public int Note;
        public int Velocity;
        public int Program;
    }

    /// <summary>
    /// A minimal Standard MIDI File reader: format 0 and 1, note on/off, tempo.
    ///
    /// Everything else — controllers, pitch bend, sysex, markers — is skipped. That
    /// is deliberate: a DMM song has notes, an instrument, a volume and a delay, and
    /// anything a MIDI file carries beyond that has nowhere to go. Reading it would
    /// only produce a converter that claims to support more than it does.
    /// </summary>
    public static class MidiFile
    {
        public static bool LooksLikeMidi(byte[] data)
        {
            return data != null && data.Length > 14
                && data[0] == (byte)'M' && data[1] == (byte)'T' && data[2] == (byte)'h' && data[3] == (byte)'d';
        }

        /// <summary>
        /// Reads every note, sorted by start time. Returns an empty list for a file
        /// this reader cannot follow rather than throwing: a broken song must not
        /// take a whole conversion down.
        /// </summary>
        public static List<MidiNote> Read(byte[] data)
        {
            var notes = new List<MidiNote>();
            if (!LooksLikeMidi(data)) return notes;

            int headerLength = ReadInt32(data, 4);
            if (headerLength < 6) return notes;

            int format = ReadInt16(data, 8);
            int tracks = ReadInt16(data, 10);
            int division = ReadInt16(data, 12);

            if (division <= 0) return notes;

            // A negative division is SMPTE timecode, which is not what a song uses.
            int at = 8 + headerLength;

            for (int t = 0; t < tracks && at + 8 <= data.Length; ++t)
            {
                // Skip to the next track chunk; some files carry junk between them.
                while (at + 8 <= data.Length
                    && !(data[at] == 'M' && data[at + 1] == 'T' && data[at + 2] == 'r' && data[at + 3] == 'k'))
                    ++at;

                if (at + 8 > data.Length) break;

                int length = ReadInt32(data, at + 4);
                int end = at + 8 + length;

                if (length < 0 || end > data.Length) end = data.Length;

                ReadTrack(data, at + 8, end, division, notes);
                at = end;
            }

            notes.Sort(delegate (MidiNote a, MidiNote b) { return a.Start.CompareTo(b.Start); });
            return notes;
        }

        private static void ReadTrack(byte[] data, int at, int end, int division, List<MidiNote> notes)
        {
            int tick = 0;
            double seconds = 0;
            int microsecondsPerQuarter = 500000;   // 120 bpm, the MIDI default
            int running = 0;

            // Open notes per channel+key, so a note off can find its start.
            var open = new Dictionary<int, double>();

            while (at < end)
            {
                int delta = ReadVariable(data, ref at, end);
                tick += delta;
                seconds += (double)delta / division * microsecondsPerQuarter / 1000000.0;

                if (at >= end) break;

                int status = data[at];

                if (status < 0x80)
                {
                    // Running status: the previous status byte still applies.
                    if (running == 0) { ++at; continue; }
                    status = running;
                }
                else
                {
                    ++at;
                    if (status < 0xF0) running = status;
                }

                if (status == 0xFF)
                {
                    if (at >= end) break;

                    int type = data[at++];
                    int length = ReadVariable(data, ref at, end);

                    if (type == 0x51 && length == 3 && at + 2 < end)
                    {
                        microsecondsPerQuarter = (data[at] << 16) | (data[at + 1] << 8) | data[at + 2];
                        if (microsecondsPerQuarter <= 0) microsecondsPerQuarter = 500000;
                    }

                    at += length;
                    if (type == 0x2F) break;    // end of track
                    continue;
                }

                if (status == 0xF0 || status == 0xF7)
                {
                    int length = ReadVariable(data, ref at, end);
                    at += length;
                    continue;
                }

                int command = status & 0xF0;
                int channel = status & 0x0F;

                switch (command)
                {
                    case 0x90:  // note on
                        {
                            if (at + 1 >= end) return;

                            int note = data[at++];
                            int velocity = data[at++];

                            if (velocity == 0)
                            {
                                CloseNote(open, channel, note, seconds, notes);
                                break;
                            }

                            open[(channel << 8) | note] = seconds;
                            break;
                        }

                    case 0x80:  // note off
                        {
                            if (at + 1 >= end) return;

                            int note = data[at++];
                            ++at;

                            CloseNote(open, channel, note, seconds, notes);
                            break;
                        }

                    case 0xA0:  // aftertouch
                    case 0xB0:  // controller
                    case 0xE0:  // pitch bend
                        at += 2;
                        break;

                    case 0xC0:  // program change
                    case 0xD0:  // channel pressure
                        ++at;
                        break;

                    default:
                        // Unknown command: stop rather than walk off the track.
                        return;
                }
            }
        }

        private static void CloseNote(Dictionary<int, double> open, int channel, int note,
            double now, List<MidiNote> notes)
        {
            int key = (channel << 8) | note;

            double start;
            if (!open.TryGetValue(key, out start)) return;
            open.Remove(key);

            var item = new MidiNote();
            item.Start = start;
            item.Length = Math.Max(0.01, now - start);
            item.Channel = channel;
            item.Note = note;

            // Velocity was not stored with the open note; a fixed one keeps the
            // conversion honest instead of inventing dynamics the file may not have.
            item.Velocity = 100;
            notes.Add(item);
        }

        private static int ReadVariable(byte[] data, ref int at, int end)
        {
            int value = 0;

            for (int i = 0; i < 4 && at < end; ++i)
            {
                byte b = data[at++];
                value = (value << 7) | (b & 0x7F);

                if ((b & 0x80) == 0) break;
            }

            return value;
        }

        private static int ReadInt16(byte[] data, int at)
        {
            if (at + 1 >= data.Length) return 0;
            return (data[at] << 8) | data[at + 1];
        }

        private static int ReadInt32(byte[] data, int at)
        {
            if (at + 3 >= data.Length) return 0;
            return (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
        }
    }
}
