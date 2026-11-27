//
// DPT.DOOM 2D — shared branding.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

namespace DPTDOOM2D.Shared
{
    /// <summary>
    /// The project's name, version and colours, in one place.
    ///
    /// Shared rather than game-only because the name appears in four programs —
    /// the game, the launcher, the master and the relay — and in several kinds of
    /// file: C# source, the launcher's HTML, its CSS and the version resource. A
    /// fork that changes the name has to change all of them or ship a program that
    /// calls itself two things.
    ///
    /// What stays outside, deliberately: executable names (an assembly name is a
    /// build property, not a runtime value), icons (binary assets) and the licence
    /// text (legal text, not branding).
    /// </summary>
    public static class Branding
    {
        /// <summary>The public name, as it appears in window titles and on screen.</summary>
        public const string Name = "DPT.DOOM 2D";

        /// <summary>The version, without a leading 'v'.</summary>
        public const string Version = "2.2.0";

        /// <summary>Name and version together, for banners and About screens.</summary>
        public const string Title = Name + " v" + Version;

        /// <summary>What the project is, in one line.</summary>
        public const string Tagline = "Doom 2D on a native Win32 engine";

        public const string PrimaryColor = "#FF4D5A";
        public const string SecondaryColor = "#C8303C";
        public const string DarkColor = "#1A0A0E";
        public const string OnDarkColor = "#FFFFFF";

        /// <summary>Where the project lives. Empty when a fork has no home to point at.</summary>
        public const string HomeUrl = "https://github.com/dpteam/DPTDoom2D";

        public const string LauncherName = Name + " Launcher";
        public const string MasterName = Name + " master";
    }
}
