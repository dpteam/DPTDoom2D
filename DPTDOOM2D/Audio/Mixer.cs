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

        public int FindFreeChannel()
        {
            for (int i = 0; i < MaxChannels; ++i) if (!channels[i].Active) return i;
            return 0;
        }

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
            double step = (double)channel.Rate / AudioOut.SampleRate;
            double position = channel.Position;
            float volume = channel.Volume;

            for (int frame = 0; frame < frames; ++frame)
            {
                if (position >= channel.Length)
                {
                    if (channel.Loop && channel.LoopLength > 0)
                    {
                        // Wrap inside the loop region the lump declared.
                        double over = position - channel.LoopStart;
                        position = channel.LoopStart + (over % channel.LoopLength);
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
                if (next >= channel.Length) next = channel.Loop && channel.LoopLength > 0 ? channel.LoopStart : index;

                float fraction = (float)(position - index);
                float sample = channel.Data[index] / 128f;
                if (next != index) sample += (channel.Data[next] / 128f - sample) * fraction;

                int at = frame * AudioOut.Channels;
                destination[at] += sample * volume;
                destination[at + 1] += sample * volume;

                position += step;
            }

            channel.Position = position;
        }
    }
}
