using System;
using DPTDOOM2D.Game;
using DPTDOOM2D.Win32;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// keyb.c — the key table.
    ///
    /// The original code indexes keys[] with SDL 1.2 keysyms (SDLK_ESCAPE,
    /// SDLK_KP8, ...). Keeping that indexing means ported gameplay code reads
    /// exactly like the C source, so Win32 virtual keys are translated into
    /// keysyms here once per tick instead of being spread over the whole game.
    ///
    /// Presses are edge-triggered: holding a key does not repeat it, which is
    /// what SDL 1.2 did by default and what the menus expect.
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

        /// <summary>Every press this tick, in order (for the overlay and the chat box).</summary>
        public static readonly System.Collections.Generic.List<int> PressedThisTick =
            new System.Collections.Generic.List<int>();

        // vkToSym must be built first: BuildSymToVk() inverts it, and static
        // field initialisers run in declaration order.
        static readonly int[] vkToSym = BuildVkToSym();
        static readonly int[] symToVk = BuildSymToVk();

        /// <summary>Reads the window's key state; call once per game tick.</summary>
        public static void Update(Win32Window window)
        {
            PressedThisTick.Clear();

            for (int i = 0; i < KeyCount; ++i) Keys[i] = false;

            for (int vk = 0; vk < 256; ++vk)
            {
                int sym = vkToSym[vk];
                if (sym >= 0 && window.IsKeyDown(vk)) Keys[sym] = true;
            }

            int[] pressed = window.TakePressed();
            for (int i = 0; i < pressed.Length; ++i)
            {
                int sym = vkToSym[pressed[i]];
                if (sym < 0) continue;
                PressedThisTick.Add(sym);
                if (LastKey == 0) LastKey = sym;
            }

            window.TakeReleased();
        }

        // ------------------------------------------------------- network input

        /// <summary>The local key state, kept aside while a remote player is simulated.</summary>
        private static readonly bool[] local = new bool[KeyCount];

        /// <summary>True while <see cref="Keys"/> holds a remote player's input.</summary>
        private static bool remoteActive;

        /// <summary>Saves the local state. Called once per tick, before anything runs.</summary>
        public static void SaveLocal()
        {
            if (remoteActive) return;
            Array.Copy(Keys, local, KeyCount);
        }

        /// <summary>Restores the local state after a remote player has been simulated.</summary>
        public static void RestoreLocal()
        {
            if (!remoteActive) return;
            Array.Copy(local, Keys, KeyCount);
            remoteActive = false;
        }

        /// <summary>
        /// Replaces <see cref="Keys"/> with the input a remote player applied this
        /// tick. The gameplay code reads Keys as usual, so nothing in player.c has
        /// to know whether the human is at this keyboard or across the network.
        /// </summary>
        public static void ApplyRemote(int playerId, ushort bits)
        {
            if (!remoteActive) { Array.Copy(Keys, local, KeyCount); remoteActive = true; }

            for (int i = 0; i < KeyCount; ++i) Keys[i] = false;

            PlayerData p = playerId == 2 ? Game.Game.Pl2 : Game.Game.Pl1;

            Set(bits, Net.NetProtocol.InputBits.Left, p.Kl);
            Set(bits, Net.NetProtocol.InputBits.Right, p.Kr);
            Set(bits, Net.NetProtocol.InputBits.Up, p.Ku);
            Set(bits, Net.NetProtocol.InputBits.Down, p.Kd);
            Set(bits, Net.NetProtocol.InputBits.Fire, p.Kf);
            Set(bits, Net.NetProtocol.InputBits.Jump, p.Kj);
            Set(bits, Net.NetProtocol.InputBits.Use, p.Kp);
            Set(bits, Net.NetProtocol.InputBits.NextWeapon, p.Kwr);
            Set(bits, Net.NetProtocol.InputBits.PrevWeapon, p.Kwl);
        }

        private static void Set(ushort bits, Net.NetProtocol.InputBits flag, int sym)
        {
            if ((bits & (ushort)flag) != 0 && sym >= 0 && sym < KeyCount) Keys[sym] = true;
        }

        /// <summary>Packs the local key state into the wire format.</summary>
        public static ushort PackLocal(PlayerData p)
        {
            ushort bits = 0;

            if (Down(p.Kl)) bits |= (ushort)Net.NetProtocol.InputBits.Left;
            if (Down(p.Kr)) bits |= (ushort)Net.NetProtocol.InputBits.Right;
            if (Down(p.Ku)) bits |= (ushort)Net.NetProtocol.InputBits.Up;
            if (Down(p.Kd)) bits |= (ushort)Net.NetProtocol.InputBits.Down;
            if (Down(p.Kf)) bits |= (ushort)Net.NetProtocol.InputBits.Fire;
            if (Down(p.Kj)) bits |= (ushort)Net.NetProtocol.InputBits.Jump;
            if (Down(p.Kp)) bits |= (ushort)Net.NetProtocol.InputBits.Use;
            if (Down(p.Kwr)) bits |= (ushort)Net.NetProtocol.InputBits.NextWeapon;
            if (Down(p.Kwl)) bits |= (ushort)Net.NetProtocol.InputBits.PrevWeapon;

            return bits;
        }

        public static bool Down(int sym)
        {
            return sym >= 0 && sym < KeyCount && Keys[sym];
        }

        /// <summary>True if any of the given keysym was pressed this tick.</summary>
        public static bool WasPressed(int sym)
        {
            return PressedThisTick.Contains(sym);
        }

        // -------------------------------------------------------- scripted input

        static readonly System.Collections.Generic.Dictionary<int, int> scripted =
            new System.Collections.Generic.Dictionary<int, int>();
        static readonly System.Collections.Generic.Dictionary<int, int> releases =
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

        public static void ClearScript()
        {
            scripted.Clear();
            releases.Clear();
        }

        /// <summary>Replaces Update() when there is no window: scripted keys stay down until released.</summary>
        public static void UpdateScripted(int tick)
        {
            PressedThisTick.Clear();

            int sym;
            if (releases.TryGetValue(tick, out sym)) Keys[sym] = false;
            if (scripted.TryGetValue(tick, out sym))
            {
                if (!Keys[sym]) { LastKey = sym; PressedThisTick.Add(sym); }
                Keys[sym] = true;
            }
        }

        // ------------------------------------------------------------- translation

        static int[] BuildVkToSym()
        {
            int[] map = new int[256];
            for (int i = 0; i < map.Length; ++i) map[i] = -1;

            for (int c = 'A'; c <= 'Z'; ++c) map[c] = c + 32;
            for (int c = '0'; c <= '9'; ++c) map[c] = c;

            map[NativeMethods.VK_SPACE] = SDLK_SPACE;
            map[NativeMethods.VK_ESCAPE] = SDLK_ESCAPE;
            map[NativeMethods.VK_RETURN] = SDLK_RETURN;
            map[NativeMethods.VK_TAB] = SDLK_TAB;
            map[NativeMethods.VK_BACK] = SDLK_BACKSPACE;

            map[NativeMethods.VK_OEM_MINUS] = '-';
            map[NativeMethods.VK_OEM_PLUS] = '=';
            map[NativeMethods.VK_OEM_4] = '[';
            map[NativeMethods.VK_OEM_6] = ']';
            map[NativeMethods.VK_OEM_5] = '\\';
            map[NativeMethods.VK_OEM_1] = ';';
            map[NativeMethods.VK_OEM_7] = '\'';
            map[NativeMethods.VK_OEM_COMMA] = ',';
            map[NativeMethods.VK_OEM_PERIOD] = '.';
            map[NativeMethods.VK_OEM_2] = '/';
            map[NativeMethods.VK_OEM_3] = '`';

            for (int i = 0; i < 10; ++i) map[NativeMethods.VK_NUMPAD0 + i] = SDLK_KP0 + i;
            map[NativeMethods.VK_DECIMAL] = SDLK_KP_PERIOD;
            map[NativeMethods.VK_DIVIDE] = SDLK_KP_DIVIDE;
            map[NativeMethods.VK_MULTIPLY] = SDLK_KP_MULTIPLY;
            map[NativeMethods.VK_SUBTRACT] = SDLK_KP_MINUS;
            map[NativeMethods.VK_ADD] = SDLK_KP_PLUS;

            map[NativeMethods.VK_UP] = SDLK_UP;
            map[NativeMethods.VK_DOWN] = SDLK_DOWN;
            map[NativeMethods.VK_LEFT] = SDLK_LEFT;
            map[NativeMethods.VK_RIGHT] = SDLK_RIGHT;
            map[NativeMethods.VK_INSERT] = SDLK_INSERT;
            map[NativeMethods.VK_HOME] = SDLK_HOME;
            map[NativeMethods.VK_PRIOR] = SDLK_PAGEUP;
            map[NativeMethods.VK_END] = SDLK_END;
            map[NativeMethods.VK_NEXT] = SDLK_PAGEDOWN;
            map[NativeMethods.VK_DELETE] = SDLK_DELETE;

            for (int i = 0; i < 15; ++i) map[NativeMethods.VK_F1 + i] = SDLK_F1 + i;

            map[NativeMethods.VK_NUMLOCK] = SDLK_NUMLOCK;
            map[NativeMethods.VK_CAPITAL] = SDLK_CAPSLOCK;
            map[NativeMethods.VK_SCROLL] = SDLK_SCROLLOCK;
            map[NativeMethods.VK_RSHIFT] = SDLK_RSHIFT;
            map[NativeMethods.VK_LSHIFT] = SDLK_LSHIFT;
            map[NativeMethods.VK_RCONTROL] = SDLK_RCTRL;
            map[NativeMethods.VK_LCONTROL] = SDLK_LCTRL;
            map[NativeMethods.VK_RMENU] = SDLK_RALT;
            map[NativeMethods.VK_LMENU] = SDLK_LALT;

            return map;
        }

        static int[] BuildSymToVk()
        {
            int[] map = new int[KeyCount];
            for (int i = 0; i < map.Length; ++i) map[i] = -1;

            for (int vk = 0; vk < 256; ++vk)
            {
                int sym = vkToSym[vk];
                if (sym >= 0 && sym < KeyCount && map[sym] < 0) map[sym] = vk;
            }

            return map;
        }

        /// <summary>Win32 virtual key for a keysym, or 0 (used by the overlay and netplay).</summary>
        public static int VkForSym(int sym)
        {
            return sym >= 0 && sym < KeyCount ? symToVk[sym] : -1;
        }
    }
}
