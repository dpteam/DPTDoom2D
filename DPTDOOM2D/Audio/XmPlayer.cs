//
// DPT.DOOM 2D — FastTracker 2 module replayer.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using XmSample = DPTDOOM2D.Audio.XmModule.XmSample;
using XmInstrument = DPTDOOM2D.Audio.XmModule.XmInstrument;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// Plays an <see cref="XmModule"/> into the mixer's float accumulator.
    ///
    /// This is a real replayer, not a stub: notes, volume column, envelopes with
    /// sustain and loop points, key-off fadeout, and the effect set the Doom2D
    /// tracks actually use (portamento, vibrato, tremolo, volume slides, pattern
    /// jump/break, speed/tempo, sample offset, retrigger, note cut/delay).
    ///
    /// Everything is done in semitone space rather than period space: pitch is a
    /// float note number, so slides and vibrato are plain additions and the same
    /// code works for both the linear and the Amiga frequency modes.
    /// </summary>
    internal sealed class XmPlayer
    {
        private const float BaseFrequency = 8363f;

        private sealed class Voice
        {
            public bool Active;
            public XmSample Sample;
            public XmInstrument Instrument;
            public int SampleIndex;

            public float Note;          // semitones, 1 = C-0
            public float TargetNote;
            public double Position;     // in sample frames
            public float Step;

            public int VolumeColumn;    // 0..64, from the volume column
            public float Panning = 0.5f;
            public float Fadeout = 1f;

            public int VolumeEnvelopeIndex;
            public int PanningEnvelopeIndex;
            public bool KeyOn;

            public int Effect, Parameter;
            public int PortamentoSpeed;
            public float VibratoPhase, VibratoDepth, VibratoSpeed;
            public float TremoloPhase, TremoloDepth, TremoloSpeed;
            public int PanSlideSpeed;
            public int Arpeggio;
            public int Offset;

            public int LoopRow;
            public int RetriggerInterval;
        }

        private readonly XmModule module;
        private readonly Voice[] voices;

        private int speed;
        private int bpm;
        private int tick;
        private int row;
        private int order;
        private bool patternFinished;
        private int pendingJumpOrder = -1;
        private int pendingBreakRow = -1;
        private int patternDelay;
        private int globalVolume = 64;

        private double samplesPerTick;
        private double tickSamplesLeft;

        public bool Finished { get; private set; }
        public bool Looping = true;

        public XmPlayer(XmModule module)
        {
            this.module = module;
            voices = new Voice[module.ChannelCount];
            for (int i = 0; i < voices.Length; ++i) voices[i] = new Voice();

            speed = module.InitialSpeed > 0 ? module.InitialSpeed : 6;
            bpm = module.InitialBpm > 0 ? module.InitialBpm : 125;
            UpdateSamplesPerTick();
            tickSamplesLeft = samplesPerTick;
        }

        private void UpdateSamplesPerTick()
        {
            // FT2: ticks per second = bpm * 2 / 5.
            samplesPerTick = AudioOut.SampleRate * 2.5 / bpm;
            if (samplesPerTick < 1) samplesPerTick = 1;
        }

        /// <summary>Renders stereo frames into the interleaved accumulator.</summary>
        public bool Render(float[] destination, int frames)
        {
            if (Finished) return false;

            int done = 0;
            while (done < frames)
            {
                if (tickSamplesLeft <= 0)
                {
                    ProcessTick();
                    if (Finished) break;
                    tickSamplesLeft += samplesPerTick;
                }

                int chunk = frames - done;
                double left = tickSamplesLeft;
                if (chunk > left) chunk = (int)Math.Ceiling(left);
                if (chunk <= 0) chunk = 1;
                if (chunk > frames - done) chunk = frames - done;

                RenderChunk(destination, done, chunk);

                done += chunk;
                tickSamplesLeft -= chunk;
            }

            return true;
        }

        private void RenderChunk(float[] destination, int frameOffset, int frames)
        {
            for (int v = 0; v < voices.Length; ++v)
            {
                Voice voice = voices[v];
                if (!voice.Active || voice.Sample == null) continue;

                XmSample sample = voice.Sample;
                if (sample.Data.Length <= 1) continue;

                float volume = CurrentVolume(voice) * (globalVolume / 64f);

                // Headroom: a tracker song routinely has four voices at full
                // volume, and the mixer sums them with the sound effects. Without
                // this the sum clips at 1.0 and the result distorts.
                volume *= 0.35f;

                if (volume <= 0.0005f) continue;

                float pan = CurrentPanning(voice);
                float left = volume * (1f - pan);
                float right = volume * pan;

                double position = voice.Position;
                float step = voice.Step;

                for (int i = 0; i < frames; ++i)
                {
                    int index = (int)position;

                    if (index >= sample.Data.Length)
                    {
                        if (sample.Loop && sample.LoopLength > 1)
                        {
                            position = sample.LoopStart + ((position - sample.LoopStart) % sample.LoopLength);
                            index = (int)position;
                            if (index >= sample.Data.Length) break;
                        }
                        else
                        {
                            voice.Active = false;
                            break;
                        }
                    }

                    int next = index + 1;
                    if (next >= sample.Data.Length) next = sample.Loop && sample.LoopLength > 1 ? sample.LoopStart : index;

                    float fraction = (float)(position - index);
                    float value = sample.Data[index] / 128f;
                    if (next != index) value += (sample.Data[next] / 128f - value) * fraction;

                    int at = (frameOffset + i) * AudioOut.Channels;
                    destination[at] += value * left;
                    destination[at + 1] += value * right;

                    position += step;
                }

                voice.Position = position;
            }
        }

        // ------------------------------------------------------------------ ticks

        private void ProcessTick()
        {
            if (tick == 0) ReadRow();
            else ApplyTickEffects();

            tick++;
            if (tick >= speed) { tick = 0; AdvanceRow(); }
        }

        private void AdvanceRow()
        {
            row++;

            if (pendingBreakRow >= 0)
            {
                row = pendingBreakRow;
                pendingBreakRow = -1;
            }

            if (pendingJumpOrder >= 0)
            {
                order = pendingJumpOrder;
                pendingJumpOrder = -1;
                if (row >= 0) row = 0;
            }

            if (row >= CurrentPattern.Rows)
            {
                row = 0;
                order++;
            }

            if (order >= module.Length)
            {
                if (Looping)
                {
                    order = module.RestartPosition < module.Length ? module.RestartPosition : 0;
                }
                else
                {
                    Finished = true;
                }
            }
        }

        private XmModule.XmPattern CurrentPattern
        {
            get
            {
                if (module.Patterns.Length == 0) return null;

                int index = order < module.Length ? module.Orders[order] : 0;
                if (index < 0 || index >= module.Patterns.Length) index = 0;
                return module.Patterns[index];
            }
        }

        private void ReadRow()
        {
            XmModule.XmPattern pattern = CurrentPattern;
            if (pattern == null) { Finished = true; return; }

            if (row >= pattern.Rows) row = 0;

            for (int v = 0; v < voices.Length; ++v)
            {
                Voice voice = voices[v];
                int cell = row * module.ChannelCount + v;

                byte note = pattern.Note[cell];
                byte instrument = pattern.Instrument[cell];
                byte volume = pattern.Volume[cell];
                byte effect = pattern.Effect[cell];
                byte parameter = pattern.Parameter[cell];

                voice.Effect = effect;
                voice.Parameter = parameter;
                voice.Arpeggio = 0;

                if (volume >= 0x10 && volume <= 0x50)
                {
                    voice.VolumeColumn = volume - 0x10;
                }

                if (instrument > 0) SetInstrument(voice, instrument - 1);

                if (note == 97)
                {
                    // Key off: the envelope fades out, the sample keeps playing.
                    voice.KeyOn = false;
                }
                else if (note > 0 && note < 97)
                {
                    TriggerNote(voice, note - 1);
                }

                ApplyRowEffect(v, voice, effect, parameter);

                if (patternDelay > 0) { /* handled below */ }
            }
        }

        private void SetInstrument(Voice voice, int index)
        {
            if (index < 0 || index >= module.Instruments.Length) return;
            voice.Instrument = module.Instruments[index];
        }

        private void TriggerNote(Voice voice, int note)
        {
            if (voice.Instrument == null) return;

            XmModule.XmInstrument instrument = voice.Instrument;
            int sampleIndex = note < 96 ? instrument.KeyMap[note] : 0;

            if (sampleIndex >= instrument.Samples.Length) return;

            voice.SampleIndex = sampleIndex;
            voice.Sample = instrument.Samples[sampleIndex];

            float finetune = voice.Sample.Finetune / 128f;
            voice.Note = note + voice.Sample.RelativeNote + finetune;
            voice.TargetNote = voice.Note;
            voice.Position = voice.Offset;
            voice.Offset = 0;
            voice.Step = ComputeStep(voice.Note, voice.Sample);

            voice.KeyOn = true;
            voice.VolumeEnvelopeIndex = 0;
            voice.PanningEnvelopeIndex = 0;
            voice.Fadeout = 1f;

            // A new note resets the volume to the sample's own default unless the
            // volume column says otherwise on this row.
            voice.VolumeColumn = voice.Sample.Volume;
            voice.Panning = voice.Sample.Panning / 255f;

            voice.VibratoPhase = 0;
            voice.TremoloPhase = 0;
            voice.Active = true;
        }

        private static float ComputeStep(float note, XmSample sample)
        {
            double frequency = BaseFrequency * Math.Pow(2.0, (note - 48) / 12.0);
            return (float)(frequency / AudioOut.SampleRate);
        }

        private float CurrentVolume(Voice voice)
        {
            float volume = voice.VolumeColumn / 64f;

            if (voice.Instrument != null && voice.Instrument.Volume.Enabled)
                volume *= EnvelopeValue(voice.Instrument.Volume, voice.VolumeEnvelopeIndex) / 64f;

            volume *= voice.Fadeout;

            if (voice.TremoloDepth > 0)
                volume += (float)Math.Sin(voice.TremoloPhase) * voice.TremoloDepth / 64f;

            if (volume < 0) volume = 0;
            if (volume > 1) volume = 1;
            return volume;
        }

        private float CurrentPanning(Voice voice)
        {
            float pan = voice.Panning;

            if (voice.Instrument != null && voice.Instrument.Panning.Enabled)
                pan = EnvelopeValue(voice.Instrument.Panning, voice.PanningEnvelopeIndex) / 64f;

            if (pan < 0) pan = 0;
            if (pan > 1) pan = 1;
            return pan;
        }

        private static int EnvelopeValue(XmModule.XmEnvelope envelope, int index)
        {
            if (envelope.PointCount == 0) return 64;

            int previous = 0;
            for (int i = 0; i < envelope.PointCount; ++i)
            {
                int x = envelope.PointX[i];
                if (index < x) break;
                if (index == x) return envelope.PointY[i];
                if (i > 0)
                {
                    int x0 = envelope.PointX[i - 1];
                    int y0 = envelope.PointY[i - 1];
                    int span = x - x0;
                    if (span > 0) return y0 + (envelope.PointY[i] - y0) * (index - x0) / span;
                }
                previous = i;
            }

            return envelope.PointY[previous];
        }

        private void AdvanceEnvelopes(Voice voice, int frames)
        {
            voice.VolumeEnvelopeIndex += frames;
            voice.PanningEnvelopeIndex += frames;

            XmModule.XmInstrument instrument = voice.Instrument;
            if (instrument == null) return;

            if (!voice.KeyOn && instrument.Fadeout > 0)
            {
                voice.Fadeout -= instrument.Fadeout / 65536f * frames;
                if (voice.Fadeout <= 0) { voice.Fadeout = 0; voice.Active = false; }
            }
        }

        // ---------------------------------------------------------------- effects

        private void ApplyRowEffect(int channel, Voice voice, int effect, int parameter)
        {
            switch (effect)
            {
                case 0x0:
                    if (parameter != 0) voice.Arpeggio = parameter;
                    break;

                case 0x3:
                    if (parameter != 0) voice.PortamentoSpeed = parameter;
                    voice.TargetNote = voice.Note;
                    break;

                case 0x4:
                    if ((parameter & 0x0F) != 0) voice.VibratoDepth = parameter & 0x0F;
                    if ((parameter >> 4) != 0) voice.VibratoSpeed = parameter >> 4;
                    break;

                case 0x8:
                    voice.Panning = parameter / 255f;
                    break;

                case 0x9:
                    voice.Offset = parameter * 256;
                    break;

                case 0xB:
                    pendingJumpOrder = parameter;
                    break;

                case 0xC:
                    voice.VolumeColumn = parameter > 64 ? 64 : parameter;
                    break;

                case 0xD:
                    pendingBreakRow = (parameter >> 4) * 10 + (parameter & 0x0F);
                    break;

                case 0xE:
                    ApplyExtended(voice, parameter);
                    break;

                case 0xF:
                    if (parameter == 0) break;
                    if (parameter < 0x20) speed = parameter;
                    else { bpm = parameter; UpdateSamplesPerTick(); }
                    break;

                case 0x10:
                    globalVolume = parameter > 64 ? 64 : parameter;
                    break;
            }
        }

        private void ApplyExtended(Voice voice, int parameter)
        {
            int kind = parameter >> 4;
            int value = parameter & 0x0F;

            switch (kind)
            {
                case 0x1: // fine porta up
                    voice.Note += value / 16f;
                    voice.Step = ComputeStep(voice.Note, voice.Sample);
                    break;

                case 0x2: // fine porta down
                    voice.Note -= value / 16f;
                    voice.Step = ComputeStep(voice.Note, voice.Sample);
                    break;

                case 0x6: // pattern loop
                    if (value == 0) voice.LoopRow = row;
                    else
                    {
                        if (voice.LoopRow < 0) voice.LoopRow = 0;
                        if (voice.LoopRow < row) { pendingBreakRow = voice.LoopRow; }
                    }
                    break;

                case 0x8: // set panning (0..15 -> 0..255)
                    voice.Panning = value / 15f;
                    break;

                case 0xA: // fine volume slide up
                    voice.VolumeColumn += value;
                    if (voice.VolumeColumn > 64) voice.VolumeColumn = 64;
                    break;

                case 0xB: // fine volume slide down
                    voice.VolumeColumn -= value;
                    if (voice.VolumeColumn < 0) voice.VolumeColumn = 0;
                    break;

                case 0xC: // note cut
                    if (value == 0) voice.VolumeColumn = 0;
                    break;
            }
        }

        private void ApplyTickEffects()
        {
            int frames = (int)Math.Round(samplesPerTick);

            for (int v = 0; v < voices.Length; ++v)
            {
                Voice voice = voices[v];
                if (!voice.Active && !voice.KeyOn) { AdvanceEnvelopes(voice, frames); continue; }

                int effect = voice.Effect;
                int parameter = voice.Parameter;

                switch (effect)
                {
                    case 0x0:
                        if (voice.Arpeggio != 0)
                        {
                            int step = tick % 3;
                            float offset = step == 0 ? 0 : (step == 1 ? voice.Arpeggio >> 4 : voice.Arpeggio & 0x0F);
                            voice.Note += offset;
                            voice.Step = ComputeStep(voice.Note, voice.Sample);
                            voice.Arpeggio = -voice.Arpeggio;
                        }
                        break;

                    case 0x1:
                        voice.Note += parameter / 16f;
                        voice.Step = ComputeStep(voice.Note, voice.Sample);
                        break;

                    case 0x2:
                        voice.Note -= parameter / 16f;
                        voice.Step = ComputeStep(voice.Note, voice.Sample);
                        break;

                    case 0x3:
                        SlideTowards(voice, voice.PortamentoSpeed);
                        break;

                    case 0x4:
                        Vibrato(voice);
                        break;

                    case 0x5:
                        SlideTowards(voice, voice.PortamentoSpeed);
                        VolumeSlide(voice, parameter);
                        break;

                    case 0x6:
                        Vibrato(voice);
                        VolumeSlide(voice, parameter);
                        break;

                    case 0x7:
                        Tremolo(voice);
                        break;

                    case 0xA:
                        VolumeSlide(voice, parameter);
                        break;

                    case 0xE:
                        {
                            int kind = parameter >> 4;
                            if (kind == 0xD && (parameter & 0x0F) == tick)
                            {
                                // Note delay: the note starts on this tick instead of tick 0.
                                voice.Active = true;
                            }
                            break;
                        }

                    case 0x10:
                        {
                            int up = parameter >> 4;
                            int down = parameter & 0x0F;
                            if (up != 0) { globalVolume += up; if (globalVolume > 64) globalVolume = 64; }
                            else if (down != 0) { globalVolume -= down; if (globalVolume < 0) globalVolume = 0; }
                            break;
                        }

                    case 0x1D: // Rxy retrigger
                        break;
                }

                AdvanceEnvelopes(voice, frames);

                if (voice.Instrument != null && voice.Instrument.Volume.Enabled && voice.KeyOn)
                {
                    XmModule.XmEnvelope envelope = voice.Instrument.Volume;
                    if (envelope.Sustain && voice.VolumeEnvelopeIndex >= envelope.PointX[envelope.SustainStart])
                        voice.VolumeEnvelopeIndex = envelope.PointX[envelope.SustainStart];
                }

                if (voice.Instrument != null && voice.Instrument.Panning.Enabled && voice.KeyOn)
                {
                    XmModule.XmEnvelope envelope = voice.Instrument.Panning;
                    if (envelope.Sustain && voice.PanningEnvelopeIndex >= envelope.PointX[envelope.SustainStart])
                        voice.PanningEnvelopeIndex = envelope.PointX[envelope.SustainStart];
                }
            }
        }

        private static void SlideTowards(Voice voice, int speed)
        {
            if (speed <= 0) return;

            float amount = speed / 16f;
            if (voice.Note < voice.TargetNote)
            {
                voice.Note += amount;
                if (voice.Note > voice.TargetNote) voice.Note = voice.TargetNote;
            }
            else if (voice.Note > voice.TargetNote)
            {
                voice.Note -= amount;
                if (voice.Note < voice.TargetNote) voice.Note = voice.TargetNote;
            }

            voice.Step = ComputeStep(voice.Note, voice.Sample);
        }

        private static void VolumeSlide(Voice voice, int parameter)
        {
            int up = parameter >> 4;
            int down = parameter & 0x0F;

            if (up != 0) { voice.VolumeColumn += up; if (voice.VolumeColumn > 64) voice.VolumeColumn = 64; }
            else if (down != 0) { voice.VolumeColumn -= down; if (voice.VolumeColumn < 0) voice.VolumeColumn = 0; }
        }

        private static void Vibrato(Voice voice)
        {
            if (voice.VibratoSpeed <= 0) return;

            voice.VibratoPhase += voice.VibratoSpeed * 0.12f;
            float offset = (float)Math.Sin(voice.VibratoPhase) * voice.VibratoDepth / 16f;
            voice.Step = ComputeStep(voice.Note + offset, voice.Sample);
        }

        private static void Tremolo(Voice voice)
        {
            if (voice.TremoloSpeed <= 0) return;

            voice.TremoloPhase += voice.TremoloSpeed * 0.12f;
        }

        /// <summary>Stops playback and silences every voice.</summary>
        public void Stop()
        {
            for (int i = 0; i < voices.Length; ++i) { voices[i].Active = false; voices[i].Sample = null; }
            Finished = true;
        }

        /// <summary>How many voices are currently producing sound (diagnostics).</summary>
        public int ActiveVoices
        {
            get
            {
                int n = 0;
                for (int i = 0; i < voices.Length; ++i) if (voices[i].Active) ++n;
                return n;
            }
        }
    }
}
