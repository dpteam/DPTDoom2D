//
// DPT.DOOM 2D — control layouts.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using DPTDOOM2D.Game;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Keyboard layouts as named presets, the way an emulator presents its pads.
    ///
    /// Preset 1 (the default) is rebuilt for a modern keyboard: arrows to move,
    /// Z/X/C under the left hand — what a NES emulator feels like. Preset 2 is
    /// main.c's table without a single change (numpad 8/5/4/6, PageDown to fire,
    /// Delete to jump), so a player who learned Doom2D in 1996 does not have to
    /// relearn anything.
    ///
    /// A preset only supplies defaults. The F2 overlay can rebind any key; those
    /// keys are written to Storage/default.cfg and applied on top of the preset.
    /// An explicit -layout resets to the named preset instead, because asking for
    /// a preset and getting yesterday's custom keys back would make the option
    /// look broken.
    /// </summary>
    public static class Controls
    {
        public const int DefaultPreset = 1;
        public const int PresetCount = 2;

        /// <summary>Order shared by the overlay, the config keys and the saved file.</summary>
        public static readonly string[] Actions =
        {
            "left", "right", "up", "down", "fire", "jump", "use", "prev", "next"
        };

        // player.c reads ku/kd to lean the aim, kj to jump, kf to fire and kp to
        // press switches — which is why up/down and jump are separate keys here.
        //
        // Template 1 keeps the two players apart: P1 owns the arrows and Z/X/C/Q/E,
        // P2 the IJKL cluster with F/G/H and R/T. The numpad is deliberately left
        // out — Kbd.Down aliases arrows to numpad, so a numpad P2 would fight P1.
        static readonly int[] Preset1P1 =
        {
            Kbd.SDLK_LEFT, Kbd.SDLK_RIGHT, Kbd.SDLK_UP, Kbd.SDLK_DOWN,
            'z', 'x', 'c', 'q', 'e'
        };

        static readonly int[] Preset1P2 =
        {
            'j', 'l', 'i', 'k',
            'f', 'g', 'h', 'r', 't'
        };

        // main.c: pl1.ku/kd/kl/kr, kf, kj, kwl, kwr, kp; then pl2, in that order.
        static readonly int[] Preset2P1 =
        {
            Kbd.SDLK_KP4, Kbd.SDLK_KP6, Kbd.SDLK_KP8, Kbd.SDLK_KP5,
            Kbd.SDLK_PAGEDOWN, Kbd.SDLK_DELETE, Kbd.SDLK_KP8, Kbd.SDLK_HOME, Kbd.SDLK_END
        };

        static readonly int[] Preset2P2 =
        {
            's', 'f', 'e', 'd',
            'a', 'q', 'e', '1', '2'
        };

        /// <summary>
        /// The preset the current keys started from; 0 is never used. The overlay shows
        /// <see cref="MatchPreset"/> instead, but the saved file has to remember which
        /// preset the customisations belong to, or picking that preset again in the
        /// launcher would throw them away.
        /// </summary>
        public static int BasePreset = DefaultPreset;

        /// <summary>A preset asked for by -layout or layout=; 0 when nobody asked.</summary>
        public static int RequestedPreset;

        /// <summary>The preset the keys in Storage/default.cfg were recorded on; 0 = none.</summary>
        public static int SavedPreset;

        public static int ClampPreset(int preset)
        {
            if (preset == 1) return 1;
            if (preset == 2) return 2;
            return 0;
        }

        public static string PresetName(int preset)
        {
            switch (preset)
            {
                case 1: return "1 - arrows + Z/X";
                case 2: return "2 - numpad (1996)";
                default: return "custom";
            }
        }

        /// <summary>Applies a preset to both players. Choosing one is a reset.</summary>
        public static void Apply(int preset)
        {
            preset = ClampPreset(preset);
            if (preset == 0) preset = DefaultPreset;

            int[] p1 = preset == 2 ? Preset2P1 : Preset1P1;
            int[] p2 = preset == 2 ? Preset2P2 : Preset1P2;

            for (int i = 0; i < Actions.Length; ++i)
            {
                Set(Game.Game.Pl1, Actions[i], p1[i]);
                Set(Game.Game.Pl2, Actions[i], p2[i]);
            }

            BasePreset = preset;
        }

        /// <summary>
        /// Which preset the live keys match, or 0 when they were edited. The overlay
        /// shows the answer instead of remembering it: the keys are the truth, and a
        /// stored number can only drift away from them.
        /// </summary>
        public static int MatchPreset()
        {
            if (Matches(Preset1P1, Preset1P2)) return 1;
            if (Matches(Preset2P1, Preset2P2)) return 2;
            return 0;
        }

        static bool Matches(int[] p1, int[] p2)
        {
            for (int i = 0; i < Actions.Length; ++i)
            {
                if (Get(Game.Game.Pl1, Actions[i]) != p1[i]) return false;
                if (Get(Game.Game.Pl2, Actions[i]) != p2[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Whether keys saved by the overlay belong to the preset in force. A saved
        /// set is a snapshot of one preset plus edits: it survives choosing the same
        /// preset again, and is dropped when another one is asked for.
        /// </summary>
        public static bool SavedKeysApply(int requested, int saved)
        {
            return requested == 0 || saved == requested;
        }

        public static int Get(PlayerData p, string action)
        {
            switch (action)
            {
                case "left": return p.Kl;
                case "right": return p.Kr;
                case "up": return p.Ku;
                case "down": return p.Kd;
                case "fire": return p.Kf;
                case "jump": return p.Kj;
                case "use": return p.Kp;
                case "prev": return p.Kwl;
                case "next": return p.Kwr;
                default: return 0;
            }
        }

        public static void Set(PlayerData p, string action, int key)
        {
            switch (action)
            {
                case "left": p.Kl = key; break;
                case "right": p.Kr = key; break;
                case "up": p.Ku = key; break;
                case "down": p.Kd = key; break;
                case "fire": p.Kf = key; break;
                case "jump": p.Kj = key; break;
                case "use": p.Kp = key; break;
                case "prev": p.Kwl = key; break;
                case "next": p.Kwr = key; break;
            }
        }

        /// <summary>Human label for the overlay: "Left", "Prev weapon", ...</summary>
        public static string ActionName(string action)
        {
            switch (action)
            {
                case "left": return "Left";
                case "right": return "Right";
                case "up": return "Up (aim)";
                case "down": return "Down (aim)";
                case "fire": return "Fire";
                case "jump": return "Jump";
                case "use": return "Use / switch";
                case "prev": return "Prev weapon";
                case "next": return "Next weapon";
                default: return action;
            }
        }

        /// <summary>
        /// The name default.cfg understands, so a saved binding can be read back by
        /// Config.KeyByName: single characters as they are, the rest as SDL named
        /// them ("kp 4", "page down").
        /// </summary>
        public static string KeyName(int sym)
        {
            if (sym > 32 && sym < 127) return ((char)sym).ToString();

            switch (sym)
            {
                case Kbd.SDLK_SPACE: return "space";
                case Kbd.SDLK_RETURN: return "return";
                case Kbd.SDLK_TAB: return "tab";
                case Kbd.SDLK_ESCAPE: return "escape";
                case Kbd.SDLK_BACKSPACE: return "backspace";
                case Kbd.SDLK_DELETE: return "delete";
                case Kbd.SDLK_INSERT: return "insert";
                case Kbd.SDLK_HOME: return "home";
                case Kbd.SDLK_END: return "end";
                case Kbd.SDLK_PAGEUP: return "page up";
                case Kbd.SDLK_PAGEDOWN: return "page down";
                case Kbd.SDLK_UP: return "up";
                case Kbd.SDLK_DOWN: return "down";
                case Kbd.SDLK_LEFT: return "left";
                case Kbd.SDLK_RIGHT: return "right";
                case Kbd.SDLK_NUMLOCK: return "numlock";
                case Kbd.SDLK_CAPSLOCK: return "caps lock";
                case Kbd.SDLK_SCROLLOCK: return "scroll lock";
                case Kbd.SDLK_LSHIFT: return "left shift";
                case Kbd.SDLK_RSHIFT: return "right shift";
                case Kbd.SDLK_LCTRL: return "left ctrl";
                case Kbd.SDLK_RCTRL: return "right ctrl";
                case Kbd.SDLK_LALT: return "left alt";
                case Kbd.SDLK_RALT: return "right alt";
                case Kbd.SDLK_KP_PERIOD: return "kp .";
                case Kbd.SDLK_KP_DIVIDE: return "kp /";
                case Kbd.SDLK_KP_MULTIPLY: return "kp *";
                case Kbd.SDLK_KP_MINUS: return "kp -";
                case Kbd.SDLK_KP_PLUS: return "kp +";
                case Kbd.SDLK_KP_ENTER: return "kp enter";
                case Kbd.SDLK_KP_EQUALS: return "kp equals";
            }

            if (sym >= Kbd.SDLK_KP0 && sym <= Kbd.SDLK_KP9) return "kp " + (sym - Kbd.SDLK_KP0);
            if (sym >= Kbd.SDLK_F1 && sym <= Kbd.SDLK_F15) return "f" + (sym - Kbd.SDLK_F1 + 1);

            return sym.ToString();
        }
    }
}
