using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Audio;

namespace Doom2D.Engine
{
    /// <summary>
    /// sound.c — the 16-channel sound effects mixer.
    ///
    /// Doom2D sound lumps are DMI: { uint len, rate, lstart, llen } then 8-bit
    /// signed mono samples. rembo converted them to 16-bit at 22050 Hz and handed
    /// them to SDL_mixer; here the same conversion produces a SoundEffect, and the
    /// channels map onto SoundEffectInstances.
    /// </summary>
    public static class Sound
    {
        public const int NumChannels = 16;

        public static int SndVol = 50;
        public static int MusVol = 50;

        static bool disabled;
        static readonly SoundEffect[] cache = new SoundEffect[Wad.MaxEntries];
        static readonly SoundEffectInstance[] channels = new SoundEffectInstance[NumChannels];

        public static void Init()
        {
            disabled = SndVol == 0;
            Volume(SndVol);
        }

        public static void Done()
        {
            StopAll();
        }

        static void StopAll()
        {
            for (int i = 0; i < NumChannels; ++i)
            {
                if (channels[i] != null) { channels[i].Stop(true); channels[i].Dispose(); channels[i] = null; }
            }
        }

        public static void Volume(int v)
        {
            SndVol = v < 0 ? 0 : (v > 128 ? 128 : v);
        }

        /// <summary>Converts a DMI lump into a SoundEffect, once per resource.</summary>
        static SoundEffect Get(int id)
        {
            if (cache[id] != null) return cache[id];

            byte[] raw = Wad.LoadRes(id);
            if (raw.Length < 16) return null;

            int len = BitConverter.ToInt32(raw, 0);
            int rate = BitConverter.ToInt32(raw, 4);

            if (len <= 0 || rate <= 0 || 16 + len > raw.Length) return null;

            // 8-bit signed mono -> 16-bit signed mono.
            byte[] pcm = new byte[len * 2];
            for (int i = 0; i < len; ++i)
            {
                short s = (short)((sbyte)raw[16 + i] << 8);
                pcm[i * 2] = (byte)(s & 0xFF);
                pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }

            try
            {
                cache[id] = new SoundEffect(pcm, rate, AudioChannels.Mono);
            }
            catch
            {
                cache[id] = null;
            }
            return cache[id];
        }

        /// <summary>S_play: channel -1 means "any free one".</summary>
        public static int Play(Res.Locked s, int c, int rate, int v)
        {
            if (disabled || s == null) return -1;

            SoundEffect fx = Get(s.Id);
            if (fx == null) return -1;

            int ch = c;
            if (ch < 0)
            {
                for (int i = 0; i < NumChannels; ++i)
                    if (channels[i] == null || channels[i].State != SoundState.Playing) { ch = i; break; }
                if (ch < 0) ch = 0;
            }
            if (ch >= NumChannels) return -1;

            if (channels[ch] != null)
            {
                channels[ch].Stop(true);
                channels[ch].Dispose();
            }

            SoundEffectInstance inst = fx.CreateInstance();
            inst.IsLooped = false;
            inst.Volume = Math.Min(1f, v / 255f * (SndVol / 128f) * 2f);
            if (inst.Volume <= 0f) inst.Volume = 0f;
            inst.Play();
            channels[ch] = inst;
            return ch;
        }

        public static void Stop(int c)
        {
            if (c < 0 || c >= NumChannels) return;
            if (channels[c] == null) return;
            channels[c].Stop(true);
            channels[c].Dispose();
            channels[c] = null;
        }

        /// <summary>free_chunks: drops every cached sound and stops the channels.</summary>
        public static void FreeChunks()
        {
            StopAll();
            for (int i = 0; i < cache.Length; ++i)
            {
                if (cache[i] != null) { cache[i].Dispose(); cache[i] = null; }
            }
        }
    }
}
