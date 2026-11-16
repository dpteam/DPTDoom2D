//
// DPT.DOOM 2D — audio system.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using DPTDOOM2D.Audio;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Owns the one output device and the one mixer. Sound effects and music both
    /// render into the same stream, which is why this lives apart from either of
    /// them.
    /// </summary>
    public static class AudioSystem
    {
        internal static AudioOut Output;
        internal static Mixer Mixer = new Mixer();

        public static bool Available { get { return Output != null && Output.IsOpen; } }

        public static void Start()
        {
            if (Output != null) return;

            Output = new AudioOut();
            Output.Renderer = Mixer.Render;

            if (!Output.Open())
            {
                // No sound card, or the device is busy: the game keeps working,
                // it just stays silent. That is what the original did too.
                Output.Dispose();
                Output = null;
            }
        }

        public static void Shutdown()
        {
            if (Output == null) return;
            Output.Dispose();
            Output = null;
        }

        /// <summary>0..128, the scale the config file and the menu use.</summary>
        public static void SetVolume(int volume)
        {
            if (volume < 0) volume = 0;
            if (volume > 128) volume = 128;
            Mixer.MasterVolume = volume / 128f;
            Mixer.Muted = volume == 0;
        }
    }
}
