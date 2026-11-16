using System;
using System.Collections.Generic;
using DPTDOOM2D.Audio;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// sound.c — the 16-channel sound effect mixer.
    ///
    /// Doom2D sound lumps are DMI: { uint len, rate, lstart, llen } then 8-bit
    /// signed mono samples. rembo converted them to 16-bit for SDL_mixer; here the
    /// mixer resamples 8-bit data directly, which is both simpler and closer to
    /// what the DOS build did.
    /// </summary>
    public static class Sound
    {
        public const int NumChannels = 16;

        public static int SndVol = 50;

        static readonly Dictionary<int, CachedSound> cache = new Dictionary<int, CachedSound>();

        sealed class CachedSound
        {
            public sbyte[] Data;
            public int Rate;
            public int LoopStart;
            public int LoopLength;
        }

        public static void Init()
        {
            AudioSystem.Start();
            AudioSystem.SetVolume(SndVol);
        }

        public static void Done()
        {
            FreeChunks();
            AudioSystem.Shutdown();
        }

        public static void Volume(int v)
        {
            SndVol = v < 0 ? 0 : (v > 128 ? 128 : v);
            AudioSystem.SetVolume(SndVol);
        }

        static CachedSound Get(int id)
        {
            CachedSound cached;
            if (cache.TryGetValue(id, out cached)) return cached;

            byte[] raw = Wad.LoadRes(id);
            if (raw.Length < 16) { cache[id] = null; return null; }

            int length = BitConverter.ToInt32(raw, 0);
            int rate = BitConverter.ToInt32(raw, 4);
            int loopStart = BitConverter.ToInt32(raw, 8);
            int loopLength = BitConverter.ToInt32(raw, 12);

            if (length <= 0 || 16 + length > raw.Length) { cache[id] = null; return null; }
            if (rate <= 1000 || rate > 48000) rate = 11025;

            var data = new sbyte[length];
            for (int i = 0; i < length; ++i) data[i] = (sbyte)raw[16 + i];

            var sound = new CachedSound();
            sound.Data = data;
            sound.Rate = rate;
            sound.LoopStart = loopStart >= 0 && loopStart < length ? loopStart : 0;
            sound.LoopLength = loopLength > 0 && loopStart + loopLength <= length ? loopLength : 0;

            cache[id] = sound;
            return sound;
        }

        /// <summary>S_play: channel -1 means "any free one".</summary>
        public static int Play(Res.Locked s, int channel, int rate, int volume)
        {
            if (s == null) return -1;
            if (!AudioSystem.Available) return -1;

            CachedSound sound = Get(s.Id);
            if (sound == null) return -1;

            Mixer mixer = AudioSystem.Mixer;
            int index = channel < 0 ? mixer.FindFreeChannel() : channel;
            if (index < 0 || index >= Mixer.MaxChannels) return -1;

            Mixer.Channel target = mixer.GetChannel(index);
            if (target == null) return -1;

            target.Data = sound.Data;
            target.Length = sound.Data.Length;
            target.Rate = sound.Rate;
            target.Position = 0;
            target.Volume = Math.Min(1f, volume / 255f) * 0.9f;
            target.Loop = sound.LoopLength > 0;
            target.LoopStart = sound.LoopStart;
            target.LoopLength = sound.LoopLength;
            target.Active = true;

            return index;
        }

        public static void Stop(int channel)
        {
            if (!AudioSystem.Available) return;
            Mixer.Channel target = AudioSystem.Mixer.GetChannel(channel);
            if (target == null) return;
            target.Active = false;
            target.Data = null;
        }

        /// <summary>free_chunks: drops every cached sound and silences the channels.</summary>
        public static void FreeChunks()
        {
            cache.Clear();
            if (AudioSystem.Output == null) return;
            AudioSystem.Mixer.StopAll();
            AudioSystem.Output.Reset();
        }

        /// <summary>Number of sounds currently cached, for the F2 overlay.</summary>
        public static int CachedCount { get { return cache.Count; } }
    }
}
