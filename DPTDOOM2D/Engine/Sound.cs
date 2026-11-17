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
            public string Name;
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
            CachedSound sound = Parse(id, raw);
            cache[id] = sound;
            return sound;
        }

        /// <summary>
        /// Reads a DMI lump.
        ///
        /// Two layouts exist and both are in the wild. The DOS build wrote an
        /// 8-byte header (len u16, rate u16, loop start u16, loop length u16) and
        /// that is what doom2d.wad actually contains; rembo's sound.h describes a
        /// 16-byte header with 32-bit fields. Guessing wrong makes every sample
        /// silently unplayable, which sounds exactly like "there are no sounds".
        ///
        /// The lump length is the tie-breaker: the 8-byte header adds up exactly.
        /// </summary>
        static CachedSound Parse(int id, byte[] raw)
        {
            if (raw == null || raw.Length < 8) return null;

            int length, rate, loopStart, loopLength, dataAt;

            int len16 = BitConverter.ToUInt16(raw, 0);
            int rate16 = BitConverter.ToUInt16(raw, 2);

            bool header8 = len16 > 0 && len16 + 8 <= raw.Length
                && rate16 >= 4000 && rate16 <= 48000;

            if (header8)
            {
                length = len16;
                rate = rate16;
                loopStart = BitConverter.ToUInt16(raw, 4);
                loopLength = BitConverter.ToUInt16(raw, 6);
                dataAt = 8;
            }
            else
            {
                if (raw.Length < 16) return null;

                length = BitConverter.ToInt32(raw, 0);
                rate = BitConverter.ToInt32(raw, 4);
                loopStart = BitConverter.ToInt32(raw, 8);
                loopLength = BitConverter.ToInt32(raw, 12);
                dataAt = 16;
            }

            if (length <= 0 || dataAt + length > raw.Length) return null;
            if (rate <= 1000 || rate > 48000) rate = 11025;

            var data = new sbyte[length];
            for (int i = 0; i < length; ++i) data[i] = (sbyte)raw[dataAt + i];

            var sound = new CachedSound();
            sound.Data = data;
            sound.Rate = rate;
            sound.LoopStart = loopStart >= 0 && loopStart < length ? loopStart : 0;
            sound.LoopLength = loopLength > 0 && loopStart + loopLength <= length ? loopLength : 0;
            sound.Name = Wad.GetResName(id);
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

        /// <summary>
        /// Walks D_START..D_END and parses every lump, for the headless
        /// <c>--sounds</c> check. Returns the number of sounds that produced data.
        /// </summary>
        public static int Diagnose(out int total, out int failed, out int looped, out int peak)
        {
            total = 0;
            failed = 0;
            looped = 0;
            peak = 0;

            for (int id = Wad.DStart + 1; id < Wad.DEnd; ++id)
            {
                string name = Wad.GetResName(id);
                if (!name.StartsWith("DS", StringComparison.OrdinalIgnoreCase)) continue;

                ++total;

                CachedSound sound;
                try
                {
                    sound = Parse(id, Wad.LoadRes(id));
                }
                catch (Exception e)
                {
                    Log.Warn("sound " + name + ": " + Log.Describe(e));
                    sound = null;
                }

                if (sound == null || sound.Data.Length == 0)
                {
                    ++failed;
                    continue;
                }

                if (sound.LoopLength > 0) ++looped;

                for (int i = 0; i < sound.Data.Length; ++i)
                {
                    int v = sound.Data[i];
                    if (v < 0) v = -v;
                    if (v > peak) peak = v;
                }
            }

            return total - failed;
        }
    }
}
