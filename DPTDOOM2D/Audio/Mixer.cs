//
// DPT.DOOM 2D — software mixer.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// A small software mixer: 16 effect channels plus one music stream, summed
    /// into the interleaved stereo buffer that <see cref="AudioOut"/> hands to
    /// waveOut.
    ///
    /// Doom2D sounds are 8-bit signed mono at whatever rate the lump declares
    /// (typically 11 kHz), so each channel resamples with linear interpolation.
    /// Nothing here depends on a library: this is the whole audio stack.
    /// </summary>
    internal sealed class Mixer
    {
        public const int MaxChannels = 16;

        internal sealed class Channel
        {
            public sbyte[] Data;
            public int Length;
            public int Rate = 11025;
            public double Position;
            public float Volume;
            public int LoopStart;
            public int LoopLength;
            public bool Loop;
            public bool Active;

            /// <summary>
            /// Bumped every time the channel is restarted.
            ///
            /// The mixer renders on the audio thread while the game calls Play from
            /// the game thread. A restart that lands while the mixer is inside this
            /// channel would be undone: the mixer writes back the position it read at
            /// the top of the buffer, so the new sound starts from wherever the old one
            /// had got to. The audible result is the same sound heard twice — a
            /// fragment of it, then the whole thing. Comparing the generation before
            /// writing the position back makes a restart win, without a lock in the
            /// mixer's inner loop.
            /// </summary>
            public int Generation;
        }

        private readonly Channel[] channels = new Channel[MaxChannels];
        private float[] accumulator = new float[0];

        /// <summary>0..1, applied to effects and music alike.</summary>
        public float MasterVolume = 0.8f;

        public bool Muted;

        /// <summary>Set by Music: writes into the accumulator, returns false when idle.</summary>
        public Func<float[], int, bool> MusicRenderer;

        public Mixer()
        {
            for (int i = 0; i < MaxChannels; ++i) channels[i] = new Channel();
        }

        public Channel GetChannel(int index)
        {
            return index >= 0 && index < MaxChannels ? channels[index] : null;
        }

        /// <summary>
        /// How many effect channels are playing, for the F2 stats tab.
        ///
        /// Worth watching while testing a sound: sixteen is the whole mixer, and a
        /// scene that keeps every channel busy is one where a new gunshot cuts an
        /// older sound instead of mixing with it.
        /// </summary>
        public int ActiveVoices
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxChannels; ++i) if (channels[i].Active) ++n;
                return n;
            }
        }

        public int FindFreeChannel()
        {
            for (int i = 0; i < MaxChannels; ++i) if (!channels[i].Active) return i;

            // Everything is busy: take the next one in rotation rather than always
            // channel 0. Always stealing 0 meant the same channel was cut every time a
            // seventeenth sound arrived, which is heard as a sound stuttering or
            // starting twice.
            int chosen = stealAt;
            stealAt = (stealAt + 1) % MaxChannels;
            return chosen;
        }

        private int stealAt;

        public void StopAll()
        {
            for (int i = 0; i < MaxChannels; ++i)
            {
                channels[i].Active = false;
                channels[i].Data = null;
            }
        }

        public void Render(short[] destination, int frames)
        {
            int samples = frames * AudioOut.Channels;

            if (accumulator.Length < samples) accumulator = new float[samples];
            Array.Clear(accumulator, 0, samples);

            for (int i = 0; i < MaxChannels; ++i)
            {
                Channel channel = channels[i];
                if (channel.Active && channel.Data != null) MixChannel(channel, accumulator, frames);
            }

            if (!Muted && MusicRenderer != null) MusicRenderer(accumulator, frames);

            float master = Muted ? 0f : MasterVolume;

            for (int i = 0; i < samples; ++i)
            {
                float value = accumulator[i] * master * 32767f;
                if (value > 32767f) value = 32767f;
                else if (value < -32768f) value = -32768f;
                destination[i] = (short)value;
            }
        }

        private static void MixChannel(Channel channel, float[] destination, int frames)
        {
            // Everything is copied out once: the game thread can restart this channel
            // at any moment, and reading Length after Data was swapped would index off
            // the end. A restart during this buffer invalidates the position written
            // back at the end, which is what the generation check is for.
            int generation = channel.Generation;

            sbyte[] data = channel.Data;
            int length = channel.Length;
            int loopStart = channel.LoopStart;
            int loopLength = channel.LoopLength;
            bool loop = channel.Loop;

            if (data == null || length <= 0 || length > data.Length) return;

            double step = (double)channel.Rate / AudioOut.SampleRate;
            double position = channel.Position;
            float volume = channel.Volume;

            for (int frame = 0; frame < frames; ++frame)
            {
                if (position >= length)
                {
                    if (loop && loopLength > 0)
                    {
                        // Wrap inside the loop region the lump declared.
                        double over = position - loopStart;
                        position = loopStart + (over % loopLength);
                    }
                    else
                    {
                        channel.Active = false;
                        channel.Position = 0;
                        return;
                    }
                }

                int index = (int)position;
                int next = index + 1;
                if (next >= length) next = loop && loopLength > 0 ? loopStart : index;

                float fraction = (float)(position - index);
                float sample = data[index] / 128f;
                if (next != index) sample += (data[next] / 128f - sample) * fraction;

                int at = frame * AudioOut.Channels;
                destination[at] += sample * volume;
                destination[at + 1] += sample * volume;

                position += step;
            }

            if (channel.Generation == generation) channel.Position = position;
        }
    }
}
