//
// DPT.DOOM 2D — MIDI to DMM: a song from outside becomes a lump this game plays.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// Turns a MIDI file into a DMM song.
    ///
    /// This is the FreeDooM2D piece. FreeDoom's music is MIDI, Doom2D's is DMM, and
    /// without a converter a replacement data set plays in silence — which is not a
    /// small thing in a game whose soundtrack is half of its atmosphere.
    ///
    /// What carries over: notes, timing, note length (as a cut), channel assignment.
    /// What does not: velocity curves, pitch bend, controllers, program changes
    /// beyond "which instrument index". A DMM channel has a note, an instrument, a
    /// volume and a delay; pretending otherwise would produce a converter that
    /// claims more than the target format can hold.
    ///
    /// Instruments are named DMI0001..: a DMM song refers to its samples by name, and
    /// the caller supplies the DMI lumps. The converter names them so that a WAD
    /// built from FreeDoom samples lines up with the names the song asks for.
    /// </summary>
    public static class MidiToDmm
    {
        /// <summary>DMM ticks per second; the same constant the reader uses.</summary>
        public const int TicksPerSecond = DmmModule.TicksPerSecond;

        /// <summary>
        /// One DMM pattern is at most 256 rows for the replayer, so a long song is
        /// cut into patterns of this many ticks. Eight seconds is the usual length
        /// of a phrase and keeps the order list readable.
        /// </summary>
        public const int PatternTicks = TicksPerSecond * 8;

        public static DmmSong Convert(byte[] midi)
        {
            List<MidiNote> notes = MidiFile.Read(midi);

            var song = new DmmSong();
            song.Version = 0;

            if (notes.Count == 0)
            {
                song.Patterns.Add(new DmmPattern());
                song.Orders.Add(0);
                return song;
            }

            // Which MIDI channels carry notes: those become DMM channels, in order.
            var channels = new List<int>();
            for (int i = 0; i < notes.Count; ++i)
            {
                int channel = notes[i].Channel;
                if (!channels.Contains(channel)) channels.Add(channel);
            }

            channels.Sort();

            if (channels.Count > DmmModule.Channels)
                channels.RemoveRange(DmmModule.Channels, channels.Count - DmmModule.Channels);

            // One instrument per MIDI channel: a DMM instrument is a sample, and a
            // MIDI program is a patch — mapping one to the other exactly is a
            // soundfont problem, not a format problem.
            song.Instruments.Add("DMI0001");

            // The song ends where the last note ends. Computing it as a ceiling of
            // seconds times ticks rounds 64.0000001 up to 65 and gives every pattern
            // one silent tick on the end — a rounding artefact that reads like a
            // timing bug in the converter.
            int totalTicks = 1;

            for (int i = 0; i < notes.Count; ++i)
            {
                int end = (int)Math.Round((notes[i].Start + notes[i].Length) * TicksPerSecond);
                if (end > totalTicks) totalTicks = end;
            }

            int patternCount = (totalTicks + PatternTicks - 1) / PatternTicks;
            if (patternCount < 1) patternCount = 1;
            if (patternCount > 255) patternCount = 255;

            var patterns = new DmmPattern[patternCount];
            for (int i = 0; i < patternCount; ++i) patterns[i] = new DmmPattern();

            for (int i = 0; i < notes.Count; ++i)
            {
                MidiNote note = notes[i];

                int channel = channels.IndexOf(note.Channel);
                if (channel < 0) continue;

                int start = (int)Math.Round(note.Start * TicksPerSecond);
                int end = (int)Math.Round((note.Start + note.Length) * TicksPerSecond);

                int pattern = start / PatternTicks;
                if (pattern >= patternCount) pattern = patternCount - 1;

                int row = start - pattern * PatternTicks;

                var on = new DmmEvent();
                on.At = row;
                on.Note = Math.Max(0, Math.Min(127, note.Note + 12));   // MIDI 0 is DMM 12
                on.Instrument = 1;
                on.Volume = Math.Max(1, Math.Min(127, note.Velocity));

                patterns[pattern].Channels[channel].Add(on);

                // A note that ends inside the same pattern gets a cut; one that
                // crosses into the next pattern is left to ring, which is what a
                // channel-per-pattern format can express without a lot of surgery.
                int endPattern = end / PatternTicks;
                if (endPattern != pattern) continue;

                int endRow = end - pattern * PatternTicks;

                var off = new DmmEvent();
                off.At = endRow;
                off.Note = on.Note;
                off.Instrument = 0;
                off.Volume = 0;             // 0 is a note cut in DMM

                patterns[pattern].Channels[channel].Add(off);
            }

            for (int i = 0; i < patternCount; ++i)
            {
                // The pattern's length is what the reader measures, so it is set from
                // the music and not left to the longest channel by accident.
                int end = Math.Min(PatternTicks, totalTicks - i * PatternTicks);
                patterns[i].Length = end > 0 ? end : 1;

                song.Patterns.Add(patterns[i]);
                song.Orders.Add((byte)i);
            }

            // 255 means "loop back to here", and the reader looks for it to set the
            // restart point: without it the song simply stops at the end.
            song.Orders.Add(255);

            return song;
        }

        /// <summary>
        /// Converts and writes in one step, then reads the result back through the
        /// real loader. Returns null when the bytes do not load, so a converter bug
        /// is a failed check and not a silent piece of music nobody can hear.
        /// </summary>
        public static byte[] ConvertAndVerify(byte[] midi, out string summary)
        {
            DmmSong song = Convert(midi);
            byte[] data = DmmWriter.Write(song);

            XmModule module = DmmModule.Load(data, null);

            if (module == null)
            {
                summary = "не читается обратно";
                return null;
            }

            summary = module.Patterns.Length + " паттернов, " + module.Length
                + " в порядке, " + data.Length + " байт";

            return data;
        }
    }
}
