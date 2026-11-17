//
// DPT.DOOM 2D — generated instrument samples for converted music.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// Makes the DMI samples a converted song needs to be audible.
    ///
    /// A DMM song does not carry its instruments: it names DMI lumps, and the
    /// replayer loads them. Doom2D's own data set has ninety-one of them; a
    /// replacement data set has none, so a converted track would play in silence.
    ///
    /// These are generated rather than borrowed: four waveforms with a decay, which
    /// is enough for a melody, a bass and a chord, and it keeps AIDooM2D's promise —
    /// no sample in the file is somebody else's.
    ///
    /// **The base pitch is derived, not guessed.** The replayer computes
    /// `8363 * 2^((note-49)/12)` and steps the sample at that rate divided by the
    /// output rate, so a sample recorded at 11025 Hz is played back sharp by
    /// 8363/11025. Generating a waveform of <c>baseHz</c> therefore sounds
    /// <c>baseHz * 8363/11025</c> at DMM note 44 — and note 44 is where converted
    /// tracks sit, since Doom's music has a median around there. <see cref="BaseHz"/>
    /// solves that equation for A3 (220 Hz), so a converted soundtrack lands in the
    /// register a listener expects.
    /// </summary>
    public static class Instruments
    {
        /// <summary>Sample rate the game's DMI lumps use.</summary>
        public const int Rate = 11025;

        /// <summary>
        /// Fundamental of the generated waveform, chosen so DMM note 44 sounds A3.
        /// See the class comment for the arithmetic.
        /// </summary>
        public const double BaseHz = 220.0 * Rate / (8363.0 * 0.7491535384383408);

        /// <summary>
        /// Names of the generated instruments, in order.
        ///
        /// Not DMI0001..: those are Doom2D's own samples, and a track that could not be
        /// converted still refers to them. Overwriting them would silently change the
        /// timbre of the tracks that were kept.
        /// </summary>
        public static readonly string[] Names =
        {
            "AIDMI01",      // square lead
            "AIDMI02",      // sine bass
            "AIDMI03",      // saw pad
            "AIDMI04",      // bright pluck
            "AIDMI05",      // soft triangle
            "AIDMI06"       // noise-ish percussion stand-in
        };

        /// <summary>
        /// Builds every instrument. Deterministic: the same seed, the same bytes, on
        /// any machine, which the network data check depends on.
        /// </summary>
        public static DmiSample[] Generate()
        {
            var samples = new DmiSample[Names.Length];

            // A square: odd harmonics falling off as 1/n, which is a bright lead.
            samples[0] = Build(0.55, 0.9, Wave.Square);

            // A sine: the fundamental only, for the bass line.
            samples[1] = Build(0.75, 0.9, Wave.Sine);

            // A saw: every harmonic, which reads as a pad or a string.
            samples[2] = Build(0.85, 0.7, Wave.Saw);

            // A pluck: a bright attack that dies fast.
            samples[3] = Build(0.28, 1.6, Wave.Square);

            // A triangle: soft, for chords under the lead.
            samples[4] = Build(0.65, 0.8, Wave.Triangle);

            // Noise with a fast decay, so a percussion track has something to hit.
            samples[5] = BuildNoise(0.18);

            return samples;
        }

        private enum Wave
        {
            Sine,
            Square,
            Saw,
            Triangle
        }

        private static DmiSample Build(double seconds, double brightness, Wave wave)
        {
            var sample = new DmiSample();
            sample.Rate = Rate;

            int length = (int)(seconds * Rate);
            sample.Data = new sbyte[length];

            double step = BaseHz / Rate;

            for (int i = 0; i < length; ++i)
            {
                double phase = (i * step) % 1.0;
                double value = Sample(phase, wave);

                // A short attack removes the click at the start, and the decay is
                // exponential because that is what an instrument does.
                double t = (double)i / length;
                double envelope = Math.Min(1.0, i / (Rate * 0.005)) * Math.Exp(-t * 3.2 / brightness);

                sample.Data[i] = Clamp(value * envelope * 100);
            }

            return sample;
        }

        private static DmiSample BuildNoise(double seconds)
        {
            var sample = new DmiSample();
            sample.Rate = Rate;

            int length = (int)(seconds * Rate);
            sample.Data = new sbyte[length];

            // A tiny linear congruential generator rather than Random: the result has
            // to be the same on every machine, or two players with the "same" data
            // set disagree and the handshake refuses the session.
            uint state = 0x13579BDF;

            for (int i = 0; i < length; ++i)
            {
                state = state * 1664525 + 1013904223;

                double noise = ((state >> 16) & 0xFFFF) / 32768.0 - 1.0;
                double t = (double)i / length;
                double envelope = Math.Exp(-t * 6.0);

                sample.Data[i] = Clamp(noise * envelope * 90);
            }

            return sample;
        }

        private static double Sample(double phase, Wave wave)
        {
            switch (wave)
            {
                case Wave.Sine:
                    return Math.Sin(phase * 2 * Math.PI);

                case Wave.Square:
                    return phase < 0.5 ? 0.7 : -0.7;

                case Wave.Saw:
                    return (phase * 2.0 - 1.0) * 0.8;

                default:
                    return (phase < 0.5 ? phase * 4.0 - 1.0 : 3.0 - phase * 4.0) * 0.9;
            }
        }

        private static sbyte Clamp(double value)
        {
            if (value > 127) return 127;
            if (value < -128) return -128;
            return (sbyte)value;
        }
    }
}
