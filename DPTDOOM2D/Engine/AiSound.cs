//
// DPT.DOOM 2D — AIDooM2D: the sounds.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Synthesises every sound the game asks for.
    ///
    /// AIDooM2D cannot ship a single sample it did not make, so each `DS*` lump is
    /// built from noise and oscillators here. The synthesis is not a placeholder: the
    /// game plays a monster's death rattle and a gunshot through the same mixer, and a
    /// data set whose gunshot is a beep is a data set nobody plays twice.
    ///
    /// What decides the sound is the **name**, because that is the only information
    /// available. `DSPLDTH1` is a player death, `DSBAREXP` an explosion, `DSPISTOL` a
    /// weapon — the naming scheme Doom established and Doom2D kept. The rest of the
    /// character comes from a hash of the name, so the four player deaths differ.
    /// </summary>
    internal static class AiSound
    {
        /// <summary>Everything is made at the rate the game's own samples use.</summary>
        public const int Rate = 11025;

        /// <summary>How a name maps onto a family of sounds.</summary>
        private enum Family
        {
            Voice,          // monster vocalisation
            Explosion,
            Shot,
            Melee,
            Blip,           // menu and interface
            Chime,          // picking something up
            Liquid,         // bubbles, slime, fire
            Machine         // doors, lifts, switches
        }

        private static Family FamilyOf(string name)
        {
            string n = (name ?? "").ToUpperInvariant();

            if (n.StartsWith("DS", StringComparison.Ordinal)) n = n.Substring(2);

            if (n.EndsWith("SIT", StringComparison.Ordinal) ||
                n.EndsWith("ACT", StringComparison.Ordinal) ||
                n.EndsWith("PAIN", StringComparison.Ordinal) ||
                n.EndsWith("DTH", StringComparison.Ordinal) ||
                n.EndsWith("DIE", StringComparison.Ordinal) ||
                n.EndsWith("DTH1", StringComparison.Ordinal) ||
                n.EndsWith("DTH2", StringComparison.Ordinal) ||
                n.EndsWith("DTH3", StringComparison.Ordinal) ||
                n.EndsWith("DTH4", StringComparison.Ordinal) ||
                n.EndsWith("DTH5", StringComparison.Ordinal) ||
                n.EndsWith("SIT1", StringComparison.Ordinal) ||
                n.EndsWith("SIT2", StringComparison.Ordinal) ||
                n.EndsWith("SIT3", StringComparison.Ordinal) ||
                n == "PEDTH" || n == "PEPAIN" || n == "PESIT" ||
                n == "PLDETH" || n == "PLPAIN" || n == "PLFALL" ||
                n == "MANDTH" || n == "MANATK" || n == "MANSIT" ||
                n == "HAHA1" || n == "PAUK1" || n == "UTRUP" ||
                n == "THUND1" || n == "THUND2") return Family.Voice;

            if (n.IndexOf("XPL", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("EXP", StringComparison.Ordinal) >= 0 ||
                n == "BAREXP" || n == "FIRXPL" || n == "RXPLOD") return Family.Explosion;

            if (n.IndexOf("PISTOL", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SHOT", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SHOTGN", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("MGUN", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("PLASMA", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("BFG", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("LAUNC", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SGTATK", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SKEATK", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SKLATK", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("VILATK", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SPIATK", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("BOSIT", StringComparison.Ordinal) >= 0) return Family.Shot;

            if (n.IndexOf("PUNCH", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("CLAW", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("BITE", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SAW", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("HOOF", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("METAL", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SLOP", StringComparison.Ordinal) >= 0) return Family.Melee;

            if (n.IndexOf("ITEMUP", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("GETPOW", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SUDI", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("TUDI", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("WPNUP", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("TSTEND", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("STOP", StringComparison.Ordinal) >= 0) return Family.Chime;

            if (n.IndexOf("BUBL", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("BUL", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("BURN", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("FLAME", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SPARK", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SLOP", StringComparison.Ordinal) >= 0) return Family.Liquid;

            if (n.IndexOf("DOOR", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("SWTCH", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("BDOPN", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("BDCLS", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("DOROPN", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("DORCLS", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("LIFT", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("TELEPT", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("NOTELE", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("RADIO", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("STNMOV", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("OOF", StringComparison.Ordinal) >= 0) return Family.Machine;

            return Family.Blip;
        }

        /// <summary>Builds the sample for one lump name. Deterministic for a given seed.</summary>
        public static Audio.DmiSample Generate(string name, uint seed)
        {
            Family family = FamilyOf(name);

            var rnd = new AiArt.Rand(AiArt.Hash(name ?? "", seed));

            // Length and pitch come from the name, so the four player deaths differ
            // and a gunshot is not the same length as a door.
            int variance = rnd.Below(100);

            switch (family)
            {
                case Family.Voice: return Voice(rnd, variance);
                case Family.Explosion: return Explosion(rnd, variance);
                case Family.Shot: return Shot(rnd, variance);
                case Family.Melee: return Melee(rnd, variance);
                case Family.Chime: return Chime(rnd, variance);
                case Family.Liquid: return Liquid(rnd, variance);
                case Family.Machine: return Machine(rnd, variance);
                default: return Blip(rnd, variance);
            }
        }

        // ---- the families ----------------------------------------------------

        /// <summary>
        /// A monster: a low buzz with a formant wobble over it.
        ///
        /// Two oscillators an octave apart plus noise through a one-pole filter is the
        /// cheapest thing that reads as a throat rather than a tone.
        /// </summary>
        private static Audio.DmiSample Voice(AiArt.Rand rnd, int variance)
        {
            double seconds = 0.28 + variance * 0.004;      // 0.28 .. 0.68 s
            double f0 = 70 + rnd.Below(90);                // a growl, not a note

            int length = Samples(seconds);
            var data = new sbyte[length];

            double phase = 0, phase2 = 0;
            double noiseState = 0;

            for (int i = 0; i < length; ++i)
            {
                double t = (double)i / length;

                // The pitch drops at the end: that is what makes it sound like it is
                // dying rather than singing.
                double bend = 1.0 - t * 0.35;

                phase += f0 * bend / Rate;
                phase2 += f0 * 2.01 * bend / Rate;

                double tone = Math.Sin(phase * 2 * Math.PI) * 0.6
                    + Math.Sin(phase2 * 2 * Math.PI) * 0.25;

                // A crude formant: noise that follows the tone's envelope.
                noiseState = noiseState * 0.7 + (rnd.Below(200) - 100) * 0.3;
                double noise = noiseState / 100.0 * 0.35;

                double envelope = Math.Min(1.0, t * 12) * Math.Exp(-t * 3.0);

                data[i] = Clamp((tone + noise) * envelope * 100);
            }

            return Sample(data);
        }

        /// <summary>An explosion: filtered noise with a fast attack and a long tail.</summary>
        private static Audio.DmiSample Explosion(AiArt.Rand rnd, int variance)
        {
            double seconds = 0.55 + variance * 0.006;
            int length = Samples(seconds);

            var data = new sbyte[length];

            double state = 0;

            for (int i = 0; i < length; ++i)
            {
                double t = (double)i / length;

                state = state * 0.82 + (rnd.Below(200) - 100) * 0.18;

                // The filter opens then closes, which is what gives a boom its "body".
                double envelope = Math.Min(1.0, t * 40) * Math.Exp(-t * 4.5);

                data[i] = Clamp(state / 100.0 * envelope * 110);
            }

            return Sample(data);
        }

        /// <summary>A gunshot: a click, a noise burst and a low thump under it.</summary>
        private static Audio.DmiSample Shot(AiArt.Rand rnd, int variance)
        {
            double seconds = 0.16 + variance * 0.002;
            int length = Samples(seconds);

            var data = new sbyte[length];

            double state = 0;
            double thumpPhase = 0;
            double thumpHz = 90 + rnd.Below(60);

            for (int i = 0; i < length; ++i)
            {
                double t = (double)i / length;

                state = state * 0.55 + (rnd.Below(200) - 100) * 0.45;
                thumpPhase += thumpHz / Rate;

                double noise = state / 100.0 * Math.Exp(-t * 14) * 1.1;
                double thump = Math.Sin(thumpPhase * 2 * Math.PI) * Math.Exp(-t * 9) * 0.5;

                // A click on the very first sample: without it a shot has no attack.
                double click = i < 12 ? (12 - i) / 12.0 * 0.6 : 0;

                data[i] = Clamp((noise + thump + click) * 105);
            }

            return Sample(data);
        }

        /// <summary>A punch or a bite: a short thump with a little grit.</summary>
        private static Audio.DmiSample Melee(AiArt.Rand rnd, int variance)
        {
            double seconds = 0.11 + variance * 0.0015;
            int length = Samples(seconds);

            var data = new sbyte[length];

            double phase = 0;
            double hz = 120 + rnd.Below(80);

            for (int i = 0; i < length; ++i)
            {
                double t = (double)i / length;

                phase += hz / Rate;
                double body = Math.Sin(phase * 2 * Math.PI) * Math.Exp(-t * 16);
                double grit = (rnd.Below(200) - 100) / 100.0 * Math.Exp(-t * 30) * 0.4;

                data[i] = Clamp((body + grit) * 100);
            }

            return Sample(data);
        }

        /// <summary>Two notes, the second a fifth up: the sound of picking something up.</summary>
        private static Audio.DmiSample Chime(AiArt.Rand rnd, int variance)
        {
            double seconds = 0.22 + variance * 0.002;
            int length = Samples(seconds);

            var data = new sbyte[length];

            double baseHz = 520 + rnd.Below(240);
            double phase = 0;

            for (int i = 0; i < length; ++i)
            {
                double t = (double)i / length;

                // The pitch steps up halfway through.
                double hz = t < 0.5 ? baseHz : baseHz * 1.5;

                phase += hz / Rate;

                double envelope = Math.Min(1.0, t * 30) * Math.Exp(-t * 6);
                double tone = Math.Sin(phase * 2 * Math.PI);

                data[i] = Clamp(tone * envelope * 90);
            }

            return Sample(data);
        }

        /// <summary>Bubbles and fire: sparse noise bursts over a soft hiss.</summary>
        private static Audio.DmiSample Liquid(AiArt.Rand rnd, int variance)
        {
            double seconds = 0.30 + variance * 0.004;
            int length = Samples(seconds);

            var data = new sbyte[length];

            double hiss = 0;
            double bubblePhase = 0;
            double bubbleHz = 300 + rnd.Below(300);

            for (int i = 0; i < length; ++i)
            {
                double t = (double)i / length;

                hiss = hiss * 0.9 + (rnd.Below(200) - 100) * 0.1;

                // A slow tremolo turns the hiss into separate bubbles.
                bubblePhase += bubbleHz / Rate;
                double tremolo = 0.5 + 0.5 * Math.Sin(bubblePhase * 2 * Math.PI);

                double envelope = Math.Min(1.0, t * 10) * Math.Exp(-t * 3);

                data[i] = Clamp(hiss / 100.0 * tremolo * envelope * 95);
            }

            return Sample(data);
        }

        /// <summary>A door or a lift: a low rumble that starts and stops abruptly.</summary>
        private static Audio.DmiSample Machine(AiArt.Rand rnd, int variance)
        {
            double seconds = 0.34 + variance * 0.005;
            int length = Samples(seconds);

            var data = new sbyte[length];

            double state = 0;
            double phase = 0;
            double hz = 60 + rnd.Below(40);

            for (int i = 0; i < length; ++i)
            {
                double t = (double)i / length;

                state = state * 0.93 + (rnd.Below(200) - 100) * 0.07;
                phase += hz / Rate;

                // A flat envelope with a short fade at both ends: a machine does not
                // decay, it stops.
                double envelope = Math.Min(1.0, Math.Min(t, 1 - t) * 14);
                double rumble = state / 100.0 * 0.8 + Math.Sin(phase * 2 * Math.PI) * 0.2;

                data[i] = Clamp(rumble * envelope * 95);
            }

            return Sample(data);
        }

        /// <summary>A short interface beep.</summary>
        private static Audio.DmiSample Blip(AiArt.Rand rnd, int variance)
        {
            double seconds = 0.07 + variance * 0.001;
            int length = Samples(seconds);

            var data = new sbyte[length];

            double hz = 700 + rnd.Below(500);
            double phase = 0;

            for (int i = 0; i < length; ++i)
            {
                double t = (double)i / length;

                phase += hz / Rate;

                double envelope = Math.Min(1.0, t * 40) * Math.Exp(-t * 12);

                data[i] = Clamp(Math.Sin(phase * 2 * Math.PI) * envelope * 80);
            }

            return Sample(data);
        }

        // ---- helpers ---------------------------------------------------------

        private static int Samples(double seconds)
        {
            int count = (int)(seconds * Rate);

            // The DMI header holds the length in sixteen bits.
            if (count < 8) count = 8;
            if (count > Audio.DmiWriter.MaxLength) count = Audio.DmiWriter.MaxLength;

            return count;
        }

        private static Audio.DmiSample Sample(sbyte[] data)
        {
            var sample = new Audio.DmiSample();
            sample.Rate = Rate;
            sample.Data = data;
            return sample;
        }

        private static sbyte Clamp(double value)
        {
            if (value > 127) return 127;
            if (value < -128) return -128;
            return (sbyte)value;
        }
    }
}
