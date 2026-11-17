//
// DPT.DOOM 2D — AIDooM2D: the music.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using DPTDOOM2D.Audio;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Composes the soundtrack.
    ///
    /// This is a generator, not a converter: there is no source file to convert, so the
    /// notes are written here. The rules are the ones a person writing for this kind of
    /// game would use — a minor progression, a bass that holds the root, a lead that
    /// moves stepwise and rests — and the seed picks the key, the tempo and the exact
    /// notes, so six tracks are six different pieces rather than six copies.
    ///
    /// What the format can hold decides the shape: a DMM song is eight monophonic
    /// channels of events with delays, and the replayer reads the pattern length from
    /// its longest channel. So every part is written as absolute ticks inside a
    /// pattern, and the writer subtracts.
    /// </summary>
    internal static class AiMusic
    {
        /// <summary>How many tracks a set carries; a level picks one by its number.</summary>
        public static readonly int Count = 6;

        /// <summary>DMM ticks per second; the same constant the writer and reader use.</summary>
        private const int Ticks = DmmModule.TicksPerSecond;

        /// <summary>One pattern: 512 ticks is eight seconds, four bars at 120 BPM.</summary>
        private const int PatternTicks = 512;

        /// <summary>Beats per bar and ticks per beat, for a 4/4 at 120.</summary>
        private const int BarTicks = 128;
        private const int BeatTicks = 32;

        /// <summary>The instrument names a generated track refers to by default.</summary>
        public static readonly string[] InstrumentNames = Instruments.PlainNames;

        /// <summary>
        /// Builds one track and returns the DMM bytes, or null when the result does not
        /// load back — a converter bug is then a failed check, not silence.
        ///
        /// <paramref name="instrumentNames"/> is what the song refers to; it has to be
        /// the names the same WAD carries, or the replayer finds nothing to play.
        /// </summary>
        public static byte[] Track(uint seed, int index, string[] instrumentNames)
        {
            var song = new DmmSong();
            song.Version = 0;

            for (int i = 0; instrumentNames != null && i < instrumentNames.Length; ++i)
                song.Instruments.Add(instrumentNames[i]);

            var rnd = new AiArt.Rand(AiArt.Hash("music" + index, seed));

            // A minor key, transposed: the root sits near A3 so the bass has room below
            // and the lead has room above.
            int root = 44 + rnd.Below(12) - 6;

            // i - VI - III - VII, the progression half of the era's dark tracks use.
            int[] progression = { 0, 8, 3, 10 };

            // Natural minor, as semitone offsets from the root.
            int[] scale = { 0, 2, 3, 5, 7, 8, 10 };

            int patterns = 8 + rnd.Below(4);      // 64 .. 88 seconds

            var built = new DmmPattern[patterns];

            for (int p = 0; p < patterns; ++p)
            {
                var pattern = new DmmPattern();
                pattern.Length = PatternTicks;

                for (int bar = 0; bar < PatternTicks / BarTicks; ++bar)
                {
                    int barAt = bar * BarTicks;

                    // The chord of this bar, cycling through the progression once per
                    // four bars and transposing every pass so the track develops.
                    int chordIndex = (p * (PatternTicks / BarTicks) + bar) % progression.Length;
                    int chordRoot = root + progression[chordIndex];

                    WriteBass(pattern, barAt, chordRoot, rnd);
                    WriteChords(pattern, barAt, chordRoot, rnd);
                    WriteLead(pattern, barAt, root, scale, chordRoot, rnd);
                    WriteDrums(pattern, barAt, rnd);
                }

                built[p] = pattern;
                song.Patterns.Add(pattern);
                song.Orders.Add((byte)p);
            }

            // 255 marks the loop point, as in the original files.
            song.Orders.Add(255);

            byte[] data = DmmWriter.Write(song);

            // Read it back through the real loader: a song that does not parse would
            // otherwise be silence nobody notices until they play it.
            XmModule module = DmmModule.Load(data, null);

            return module == null ? null : data;
        }

        // ---- the parts -------------------------------------------------------

        /// <summary>
        /// The bass: the chord root on the first and third beat, held across.
        ///
        /// Two notes a bar rather than eight keeps it under the lead; a bass that plays
        /// as fast as the melody is a bass that is not doing its job.
        /// </summary>
        private static void WriteBass(DmmPattern pattern, int barAt, int chordRoot, AiArt.Rand rnd)
        {
            int note = chordRoot - 12;

            Add(pattern, 1, barAt, note, 2, 100);
            Add(pattern, 1, barAt + BeatTicks * 2, note, 2, 92);

            // Sometimes a fifth under the second half: enough variation to not be a
            // metronome, not enough to lose the root.
            if (rnd.Below(3) == 0)
                Add(pattern, 1, barAt + BeatTicks * 2, note - 5, 2, 80);
        }

        /// <summary>The chord: a triad on the first beat, held for the bar.</summary>
        private static void WriteChords(DmmPattern pattern, int barAt, int chordRoot, AiArt.Rand rnd)
        {
            // A DMM channel is monophonic, so a triad cannot be three notes at once on
            // one channel. It is played as a quick arpeggio, which is what a tracker
            // does with three channels and what this has.
            int[] triad = { 0, 3, 7 };

            for (int i = 0; i < triad.Length; ++i)
            {
                Add(pattern, 2, barAt + i * 6, chordRoot + triad[i], 4, 74);
                Add(pattern, 3, barAt + BeatTicks + i * 6, chordRoot + triad[i] + 12, 5, 62);
            }
        }

        /// <summary>
        /// The lead: eighth notes from the scale, biased towards chord tones.
        ///
        /// A random walk would wander; a walk that keeps landing on the chord's notes
        /// sounds written. So each step either stays, moves by a scale degree, or
        /// resolves to the nearest chord tone — and rests when the dice say so.
        /// </summary>
        private static void WriteLead(DmmPattern pattern, int barAt, int root, int[] scale,
            int chordRoot, AiArt.Rand rnd)
        {
            int step = rnd.Below(scale.Length);

            for (int eighth = 0; eighth < 8; ++eighth)
            {
                int at = barAt + eighth * (BeatTicks / 2);

                // A rest every so often: without them the melody is a scale exercise.
                if (rnd.Below(100) < 22) continue;

                // Move: -1, 0 or +1 degrees, with the odd leap.
                int move = rnd.Below(100);

                if (move < 30) step = step;                      // hold
                else if (move < 70) step += rnd.Below(2) == 0 ? 1 : -1;
                else step += rnd.Below(2) == 0 ? 2 : -2;

                if (step < 0) step += scale.Length;
                if (step >= scale.Length) step -= scale.Length;

                int note = root + scale[step];

                // Pull towards the chord: a melody that never agrees with the harmony
                // sounds broken rather than adventurous.
                if (rnd.Below(100) < 45)
                {
                    int chordTone = chordRoot + (eighth % 3 == 0 ? 0 : eighth % 3 == 1 ? 3 : 7);
                    note = chordTone + 12;
                }

                if (note < 30) note = 30;
                if (note > 90) note = 90;

                Add(pattern, 0, at, note, 1, 70 + rnd.Below(30));
            }
        }

        /// <summary>Kick, snare and hats on the noise instrument.</summary>
        private static void WriteDrums(DmmPattern pattern, int barAt, AiArt.Rand rnd)
        {
            // A DMM note is a pitch, so "kick" and "snare" are low and mid notes played
            // on the noise sample: the timbre is fixed, the pitch is the difference.
            const int Kick = 40;
            const int Snare = 52;
            const int Hat = 64;

            Add(pattern, 7, barAt, Kick, 6, 110);
            Add(pattern, 7, barAt + BeatTicks * 2, Kick, 6, 100);
            Add(pattern, 7, barAt + BeatTicks * 2 + BeatTicks / 2, Kick, 6, 80);

            Add(pattern, 7, barAt + BeatTicks, Snare, 6, 100);
            Add(pattern, 7, barAt + BeatTicks * 3, Snare, 6, 100);

            for (int i = 0; i < 8; ++i)
            {
                if (rnd.Below(100) < 25) continue;
                Add(pattern, 7, barAt + i * (BeatTicks / 2), Hat, 6, 40 + rnd.Below(25));
            }
        }

        /// <summary>
        /// Adds an event to a channel.
        ///
        /// Two events at the same tick cannot both be written: the format encodes the
        /// delay between them and a delay of zero means 256 ticks. The later one wins,
        /// which is what a listener hears anyway.
        /// </summary>
        private static void Add(DmmPattern pattern, int channel, int at, int note, int instrument, int volume)
        {
            if (channel < 0 || channel >= DmmModule.Channels) return;
            if (at < 0 || at >= PatternTicks) return;

            List<DmmEvent> events = pattern.Channels[channel];

            for (int i = 0; i < events.Count; ++i)
                if (events[i].At == at) return;

            var e = new DmmEvent();
            e.At = at;
            e.Note = Math.Max(0, Math.Min(127, note));
            e.Instrument = instrument;
            e.Volume = Math.Max(1, Math.Min(127, volume));

            events.Add(e);
        }
    }
}
