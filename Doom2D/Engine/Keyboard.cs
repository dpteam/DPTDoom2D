using System;
using Microsoft.Xna.Framework.Input;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace Doom2D.Engine
{
    /// <summary>
    /// keyb.c — the key table.
    ///
    /// The original code indexes keys[] with SDL 1.2 keysyms (SDLK_ESCAPE,
    /// SDLK_KP8, ...). Keeping that indexing means ported gameplay code reads
    /// exactly like the C source, so XNA keys are translated into keysyms here
    /// once per frame instead of being spread over the whole game.
    /// </summary>
    public static class Kbd
    {
        public const int SDLK_BACKSPACE = 8;
        public const int SDLK_TAB = 9;
        public const int SDLK_RETURN = 13;
        public const int SDLK_ESCAPE = 27;
        public const int SDLK_SPACE = 32;
        public const int SDLK_DELETE = 127;

        public const int SDLK_KP0 = 256;
        public const int SDLK_KP1 = 257;
        public const int SDLK_KP2 = 258;
        public const int SDLK_KP3 = 259;
        public const int SDLK_KP4 = 260;
        public const int SDLK_KP5 = 261;
        public const int SDLK_KP6 = 262;
        public const int SDLK_KP7 = 263;
        public const int SDLK_KP8 = 264;
        public const int SDLK_KP9 = 265;
        public const int SDLK_KP_PERIOD = 266;
        public const int SDLK_KP_DIVIDE = 267;
        public const int SDLK_KP_MULTIPLY = 268;
        public const int SDLK_KP_MINUS = 269;
        public const int SDLK_KP_PLUS = 270;
        public const int SDLK_KP_ENTER = 271;
        public const int SDLK_KP_EQUALS = 272;

        public const int SDLK_UP = 273;
        public const int SDLK_DOWN = 274;
        public const int SDLK_RIGHT = 275;
        public const int SDLK_LEFT = 276;
        public const int SDLK_INSERT = 277;
        public const int SDLK_HOME = 278;
        public const int SDLK_PAGEUP = 279;
        public const int SDLK_END = 280;
        public const int SDLK_PAGEDOWN = 281;

        public const int SDLK_F1 = 282;
        public const int SDLK_F2 = 283;
        public const int SDLK_F3 = 284;
        public const int SDLK_F4 = 285;
        public const int SDLK_F5 = 286;
        public const int SDLK_F6 = 287;
        public const int SDLK_F7 = 288;
        public const int SDLK_F8 = 289;
        public const int SDLK_F9 = 290;
        public const int SDLK_F10 = 291;
        public const int SDLK_F11 = 292;
        public const int SDLK_F12 = 293;
        public const int SDLK_F13 = 294;
        public const int SDLK_F14 = 295;
        public const int SDLK_F15 = 296;

        public const int SDLK_NUMLOCK = 300;
        public const int SDLK_CAPSLOCK = 301;
        public const int SDLK_SCROLLOCK = 302;
        public const int SDLK_RSHIFT = 303;
        public const int SDLK_LSHIFT = 304;
        public const int SDLK_RCTRL = 305;
        public const int SDLK_LCTRL = 306;
        public const int SDLK_RALT = 307;
        public const int SDLK_LALT = 308;

        public const int KeyCount = 512;

        /// <summary>keys[] — held state, indexed by SDL keysym.</summary>
        public static readonly bool[] Keys = new bool[KeyCount];

        /// <summary>lastkey: the last key that went down, 0 when consumed.</summary>
        public static int LastKey;

        static readonly int[] map = BuildMap();

        public static void Update()
        {
            KeyboardState ks = Keyboard.GetState();

            for (int i = 0; i < KeyCount; ++i) Keys[i] = false;

            foreach (XnaKeys k in ks.GetPressedKeys())
            {
                int sym = Translate(k);
                if (sym >= 0 && sym < KeyCount)
                {
                    if (!Keys[sym]) LastKey = sym;
                    Keys[sym] = true;
                }
            }
        }

        public static bool Down(int sym)
        {
            return sym >= 0 && sym < KeyCount && Keys[sym];
        }

        // -------------------------------------------------------- scripted input

        static readonly System.Collections.Generic.Dictionary<int, int> scripted =
            new System.Collections.Generic.Dictionary<int, int>();

        /// <summary>Holds a key down from this tick on (headless runs only).</summary>
        public static void Script(int tick, int sym)
        {
            if (sym != 0) scripted[tick] = sym;
        }

        /// <summary>Releases a key at this tick (headless runs only).</summary>
        public static void ScriptRelease(int tick, int sym)
        {
            if (sym != 0) releases[tick] = sym;
        }

        static readonly System.Collections.Generic.Dictionary<int, int> releases =
            new System.Collections.Generic.Dictionary<int, int>();

        public static void ClearScript()
        {
            scripted.Clear();
            releases.Clear();
        }

        /// <summary>Replaces Update() when there is no window: scripted keys stay down until released.</summary>
        public static void UpdateScripted(int tick)
        {
            int sym;
            if (releases.TryGetValue(tick, out sym)) Keys[sym] = false;
            if (scripted.TryGetValue(tick, out sym))
            {
                if (!Keys[sym]) LastKey = sym;
                Keys[sym] = true;
            }
        }

        static int Translate(XnaKeys k)
        {
            int i = (int)k;
            return i >= 0 && i < map.Length ? map[i] : -1;
        }

        static int[] BuildMap()
        {
            int[] m = new int[256];
            for (int i = 0; i < m.Length; ++i) m[i] = -1;

            // Letters: SDL 1.2 reports the lowercase keysym.
            for (int c = 'A'; c <= 'Z'; ++c) m[c] = c + 32;
            // Digits and punctuation match ASCII.
            for (int c = '0'; c <= '9'; ++c) m[c] = c;

            m[(int)XnaKeys.Space] = SDLK_SPACE;
            m[(int)XnaKeys.Escape] = SDLK_ESCAPE;
            m[(int)XnaKeys.Enter] = SDLK_RETURN;
            m[(int)XnaKeys.Tab] = SDLK_TAB;
            m[(int)XnaKeys.Back] = SDLK_BACKSPACE;

            m[(int)XnaKeys.OemMinus] = 45;
            m[(int)XnaKeys.OemPlus] = 61;
            m[(int)XnaKeys.OemOpenBrackets] = 91;
            m[(int)XnaKeys.OemCloseBrackets] = 93;
            m[(int)XnaKeys.OemPipe] = 92;
            m[(int)XnaKeys.OemSemicolon] = 59;
            m[(int)XnaKeys.OemQuotes] = 39;
            m[(int)XnaKeys.OemComma] = 44;
            m[(int)XnaKeys.OemPeriod] = 46;
            m[(int)XnaKeys.OemQuestion] = 47;
            m[(int)XnaKeys.OemTilde] = 96;

            for (int i = 0; i < 10; ++i) m[(int)XnaKeys.NumPad0 + i] = SDLK_KP0 + i;
            m[(int)XnaKeys.Decimal] = SDLK_KP_PERIOD;
            m[(int)XnaKeys.Divide] = SDLK_KP_DIVIDE;
            m[(int)XnaKeys.Multiply] = SDLK_KP_MULTIPLY;
            m[(int)XnaKeys.Subtract] = SDLK_KP_MINUS;
            m[(int)XnaKeys.Add] = SDLK_KP_PLUS;

            m[(int)XnaKeys.Up] = SDLK_UP;
            m[(int)XnaKeys.Down] = SDLK_DOWN;
            m[(int)XnaKeys.Left] = SDLK_LEFT;
            m[(int)XnaKeys.Right] = SDLK_RIGHT;
            m[(int)XnaKeys.Insert] = SDLK_INSERT;
            m[(int)XnaKeys.Home] = SDLK_HOME;
            m[(int)XnaKeys.PageUp] = SDLK_PAGEUP;
            m[(int)XnaKeys.End] = SDLK_END;
            m[(int)XnaKeys.PageDown] = SDLK_PAGEDOWN;
            m[(int)XnaKeys.Delete] = SDLK_DELETE;

            for (int i = 0; i < 15; ++i) m[(int)XnaKeys.F1 + i] = SDLK_F1 + i;

            m[(int)XnaKeys.NumLock] = SDLK_NUMLOCK;
            m[(int)XnaKeys.CapsLock] = SDLK_CAPSLOCK;
            m[(int)XnaKeys.Scroll] = SDLK_SCROLLOCK;
            m[(int)XnaKeys.RightShift] = SDLK_RSHIFT;
            m[(int)XnaKeys.LeftShift] = SDLK_LSHIFT;
            m[(int)XnaKeys.RightControl] = SDLK_RCTRL;
            m[(int)XnaKeys.LeftControl] = SDLK_LCTRL;
            m[(int)XnaKeys.RightAlt] = SDLK_RALT;
            m[(int)XnaKeys.LeftAlt] = SDLK_LALT;

            return m;
        }
    }
}
