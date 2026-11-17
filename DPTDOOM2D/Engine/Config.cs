using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DPTDOOM2D.Game;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// config.c — command line and default.cfg.
    ///
    /// The original builds a table of {argument, cfg-key, target, type} and walks
    /// it twice: once for the command line, once for the file. Order matters — an
    /// option given on the command line must not be overwritten by the file — so
    /// each entry remembers whether it was already set (the `o` field in C).
    /// </summary>
    public static class Config
    {
        enum Kind { None, Byte, Word, DWord, String, SwitchOn, SwitchOff, Files, Key }

        sealed class Entry
        {
            public string Arg;      // command-line name, or null
            public string Cfg;      // default.cfg key, or null
            public Kind Kind;
            public Action<string> SetNumber;
            public Action<string> SetSwitch;
            public Action<string> SetString;
            public Action<string> SetKey;
            public Action<bool> SetBool;
            public string Binding;
            public bool Done;       // already set by an earlier pass
        }

        public static string CfgFile = "default.cfg";
        public static bool ScreenshotEnabled;
        public static bool Fullscreen;
        public static bool Sky = true;
        public static bool NoMonsters = true;
        public static bool Cheat;
        public static int Gamma;
        public static int Warp;
        public static int Width = 640;
        public static int Height = 400;

        /// <summary>
        /// Window scale: 1..6 for an integer multiple of the 320x200 framebuffer,
        /// 0 to fit the window. The game itself always renders at 320x200, exactly
        /// like the DOS original.
        /// </summary>
        public static int Scale = 2;

        /// <summary>
        /// How the frame fills the window: "pixel" (integer, letterboxed),
        /// "near" (stretch, nearest neighbour) or "smooth" (stretch, bilinear).
        /// The stretch modes are what make full screen look right: pixel perfect
        /// leaves black bars on a 16:9 display, the other two fill it.
        /// </summary>
        public static string Stretch = "near";

        /// <summary>Custom map pack from Data/Maps, or empty for the built-in levels.</summary>
        public static string MapPack = "";

        public static Win32.Win32Window.StretchMode StretchMode
        {
            get
            {
                switch (Stretch)
                {
                    case "pixel": return Win32.Win32Window.StretchMode.PixelPerfect;
                    case "smooth": return Win32.Win32Window.StretchMode.Smooth;
                    default: return Win32.Win32Window.StretchMode.Nearest;
                }
            }
        }

        public static string StretchName(int mode)
        {
            switch (mode)
            {
                case 0: return "pixel perfect";
                case 2: return "smooth";
                default: return "nearest";
            }
        }

        /// <summary>Accepts "pixel", "pixel perfect", "near", "nearest", "smooth", "linear".</summary>
        public static string NormalizeStretch(string value)
        {
            if (string.IsNullOrEmpty(value)) return "near";

            string v = value.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

            if (v.StartsWith("pixel", StringComparison.Ordinal) || v == "perfect" || v == "integer")
                return "pixel";
            if (v.StartsWith("smooth", StringComparison.Ordinal) || v == "linear" || v == "bilinear")
                return "smooth";

            return "near";
        }

        /// <summary>Network options, parsed from the same command line as everything else.</summary>
        public sealed class NetSettings
        {
            public bool Enabled;
            public bool Host;
            public string Address = "127.0.0.1";
            public int Port = 5030;
            public string Password = "";
            public bool Deathmatch;
            public int Map = 1;
            public string Master = "";
            public string RelaySession = "";
            public bool Relay;
        }

        public static NetSettings Net = new NetSettings();

        static readonly List<Entry> table = new List<Entry>();
        static readonly List<string> extraWads = new List<string>();

        public static IList<string> ExtraWads { get { return extraWads; } }

        static Config()
        {
            AddFiles("file");

            AddSwitch("cheat", null, v => Cheat = v);
            AddSwitch("vga", "screenshot", v => ScreenshotEnabled = v);
            AddNumber("sndvol", "sound_volume", v => Sound.SndVol = v);
            AddNumber("musvol", "music_volume", v => Music.MusVol = v);
            AddSwitch("fullscr", "fullscreen", v => Fullscreen = v);
            AddSwitch("window", null, v => Fullscreen = !v);
            AddSwitch(null, "sky", v => Sky = v);
            AddSwitch("mon", null, v => NoMonsters = !v);
            AddNumber("gamma", "gamma", v => Gamma = v);
            AddNumber("warp", null, v => Warp = v);
            AddNumber("width", "screen_width", v => Width = v);
            AddNumber("height", "screen_height", v => Height = v);
            AddNumber("scale", "window_scale", v => { Scale = v; ScaleExplicit = true; });
            AddString("stretch", "stretch", v => Stretch = NormalizeStretch(v));

            // -map-pack picks one WAD out of Data/Maps; -warp then picks the level
            // inside it. Both are plain options, not config-file settings: a map
            // pack belongs to a session, not to the installation.
            AddString("map-pack", "map_pack", v => MapPack = v);
            AddSwitch(null, "music_random", v => Game.Game.MusicRandom = v);
            AddNumber(null, "music_time", v => Game.Game.MusicTime = v);
            AddNumber(null, "music_fade", v => Game.Game.MusicFade = v);

            // Key bindings are collected, not applied: Game.Init() runs later and
            // would overwrite them with its defaults. Config.ApplyBindings() is
            // called after Init() so the file wins, which is what a player editing
            // default.cfg expects.
            AddKey(null, "pl1_left", "pl1_left");
            AddKey(null, "pl1_right", "pl1_right");
            AddKey(null, "pl1_up", "pl1_up");
            AddKey(null, "pl1_down", "pl1_down");
            AddKey(null, "pl1_jump", "pl1_jump");
            AddKey(null, "pl1_fire", "pl1_fire");
            AddKey(null, "pl1_next", "pl1_next");
            AddKey(null, "pl1_prev", "pl1_prev");
            AddKey(null, "pl1_use", "pl1_use");

            AddKey(null, "pl2_left", "pl2_left");
            AddKey(null, "pl2_right", "pl2_right");
            AddKey(null, "pl2_up", "pl2_up");
            AddKey(null, "pl2_down", "pl2_down");
            AddKey(null, "pl2_jump", "pl2_jump");
            AddKey(null, "pl2_fire", "pl2_fire");
            AddKey(null, "pl2_next", "pl2_next");
            AddKey(null, "pl2_prev", "pl2_prev");
            AddKey(null, "pl2_use", "pl2_use");

            AddString("config", null, v => CfgFile = v);

            // ---- network ----------------------------------------------------
            // -net host|join is the one switch that turns the session on; the rest
            // only make sense next to it, which is why they are plain options and
            // not part of the config file.
            AddString("net", null, v => { Net.Enabled = true; Net.Host = v == "host"; });
            AddString("address", null, v => Net.Address = v);
            AddNumber("port", null, v => Net.Port = v);
            AddString("password", null, v => Net.Password = v);
            AddNumber("map", null, v => Net.Map = v);
            AddString("master", null, v => Net.Master = v);
            AddString("relay-session", null, v => Net.RelaySession = v);
            AddSwitch("relay", null, v => Net.Relay = v);
            AddSwitch("deathmatch", null, v => { Net.Deathmatch = v; });
        }

        static void AddFiles(string arg)
        {
            Entry e = new Entry();
            e.Arg = arg;
            e.Kind = Kind.Files;
            table.Add(e);
        }

        static void AddSwitch(string arg, string cfg, Action<bool> apply)
        {
            // The pair shares one setter so that `fullscreen=off` in the file can
            // be applied as "false" instead of landing on the "on" entry and
            // switching full screen ON — which is exactly what a naive
            // on/off table does.
            Entry on = new Entry();
            on.Arg = arg; on.Cfg = cfg; on.Kind = Kind.SwitchOn;
            on.SetSwitch = v => apply(true);
            on.SetBool = apply;
            table.Add(on);

            Entry off = new Entry();
            off.Kind = Kind.SwitchOff;
            off.SetSwitch = v => apply(false);
            off.SetBool = apply;
            table.Add(off);
        }

        static void AddNumber(string arg, string cfg, Action<int> apply)
        {
            Entry e = new Entry();
            e.Arg = arg; e.Cfg = cfg;
            e.Kind = cfg != null ? Kind.Word : Kind.DWord;
            e.SetNumber = v => apply((int)ParseNumber(v));
            table.Add(e);
        }

        static void AddString(string arg, string cfg, Action<string> apply)
        {
            Entry e = new Entry();
            e.Arg = arg; e.Cfg = cfg; e.Kind = Kind.String;
            e.SetString = apply;
            table.Add(e);
        }

        static void AddKey(string arg, string cfg, string binding)
        {
            Entry e = new Entry();
            e.Arg = arg; e.Cfg = cfg; e.Kind = Kind.Key; e.Binding = binding;
            e.SetKey = v =>
            {
                int k = KeyByName(v);
                if (k != 0) PendingKeys[binding] = k;
            };
            table.Add(e);
        }

        /// <summary>Key bindings read from the config file, waiting for Game.Init().</summary>
        static readonly Dictionary<string, int> PendingKeys = new Dictionary<string, int>();

        /// <summary>
        /// Applies the bindings from default.cfg on top of the defaults from
        /// main.c. Called once, after Game.Init() has set those defaults.
        /// </summary>
        public static void ApplyBindings()
        {
            foreach (KeyValuePair<string, int> pair in PendingKeys)
                AssignBinding(pair.Key, pair.Value);

            if (PendingKeys.Count > 0)
                Log.Info("config: " + PendingKeys.Count + " key binding(s) from " + CfgFile);
        }

        private static void AssignBinding(string name, int k)
        {
            PlayerData p = name.StartsWith("pl2_", StringComparison.Ordinal) ? Game.Game.Pl2 : Game.Game.Pl1;
            string action = name.Substring(4);

            switch (action)
            {
                case "left": p.Kl = k; break;
                case "right": p.Kr = k; break;
                case "up": p.Ku = k; break;
                case "down": p.Kd = k; break;
                case "jump": p.Kj = k; break;
                case "fire": p.Kf = k; break;
                case "next": p.Kwr = k; break;
                case "prev": p.Kwl = k; break;
                case "use": p.Kp = k; break;
            }
        }
        static long ParseNumber(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return 0;
            try
            {
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToInt64(s.Substring(2), 16);
                return long.Parse(s);
            }
            catch { return 0; }
        }

        /// <summary>CFG_args: options win over the file, so they are applied first.</summary>
        public static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; ++i)
            {
                string s = args[i];
                if (s.Length == 0) continue;
                if (s[0] == '/' || s[0] == '-') s = s.Substring(1);

                Entry e = FindArg(s);
                if (e == null) continue;

                switch (e.Kind)
                {
                    case Kind.Files:
                        // Everything up to the next option is a file to load.
                        while (i + 1 < args.Length && !IsOption(args[i + 1]))
                            extraWads.Add(args[++i]);
                        break;

                    case Kind.SwitchOn:
                    case Kind.SwitchOff:
                        e.SetSwitch(null);
                        MarkPair(e);
                        break;

                    default:
                        if (i + 1 >= args.Length) break;
                        ApplyValue(e, args[++i]);
                        break;
                }
                e.Done = true;
            }
        }

        static bool IsOption(string s)
        {
            return s.Length > 0 && (s[0] == '/' || s[0] == '-');
        }

        static Entry FindArg(string name)
        {
            for (int i = 0; i < table.Count; ++i)
                if (table[i].Arg != null && string.Equals(table[i].Arg, name, StringComparison.OrdinalIgnoreCase))
                    return table[i];
            return null;
        }

        /// <summary>The on/off pair shares one target: setting one must not be undone by the other.</summary>
        static void MarkPair(Entry e)
        {
            int i = table.IndexOf(e);
            if (i > 0 && table[i - 1].Kind != e.Kind && table[i - 1].SetSwitch != null) table[i - 1].Done = true;
            if (i + 1 < table.Count && table[i + 1].Kind != e.Kind && table[i + 1].SetSwitch != null) table[i + 1].Done = true;
        }

        static void ApplyValue(Entry e, string value)
        {
            switch (e.Kind)
            {
                case Kind.String: e.SetString(value); break;
                case Kind.Key: e.SetKey(value); break;
                default: e.SetNumber(value); break;
            }
        }

        /// <summary>CFG_load: default.cfg next to the exe, or the copy in Data.</summary>
        public static void Load()
        {
            string path = FindCfg();
            if (path == null)
            {
                Console.Error.WriteLine("default.cfg не найден");
                return;
            }

            foreach (string raw in File.ReadAllLines(path, Encoding.GetEncoding(1251)))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == ';' || line.StartsWith(";;")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (value.Length == 0) continue;

                Entry e = FindCfg(key);
                if (e == null || e.Done) continue;

                if (e.Kind == Kind.SwitchOn || e.Kind == Kind.SwitchOff)
                {
                    // cfg.c accepted ON, OFF or a number for a switch; keep that,
                    // but apply the value through the shared setter so "off" means
                    // false no matter which half of the pair matched.
                    bool on = string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
                    bool off = string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);

                    if (on) e.SetBool(true);
                    else if (off) e.SetBool(false);
                    else e.SetBool(ParseNumber(value) != 0);
                }
                else ApplyValue(e, value);
            }
        }

        /// <summary>
        /// Reconciles the two ways of asking for a window size: `window_scale`
        /// (or `-scale`) is the modern one, while `screen_width`/`screen_height`
        /// come from the original default.cfg. The framebuffer itself is always
        /// 400x300, so a pixel size can only mean an integer multiple of it.
        /// </summary>
        public static void Normalize()
        {
            if (ScaleExplicit) return;

            if (Width > 0 && Height > 0)
            {
                int byWidth = Width / Vga.SCRW;
                int byHeight = Height / Vga.SCRH;
                int best = Math.Min(byWidth, byHeight);
                Scale = best < 1 ? 1 : (best > 6 ? 6 : best);
            }

            if (Scale < 0) Scale = 0;
            if (Scale > 6) Scale = 6;
        }

        /// <summary>True when the user named a scale explicitly, so width/height must not override it.</summary>
        public static bool ScaleExplicit;

        static Entry FindCfg(string name)
        {
            for (int i = 0; i < table.Count; ++i)
                if (table[i].Cfg != null && string.Equals(table[i].Cfg, name, StringComparison.OrdinalIgnoreCase))
                    return table[i];
            return null;
        }

        static string FindCfg()
        {
            string exe = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = {
                Path.Combine(exe, CfgFile),
                Path.Combine(exe, "Data", CfgFile),
                CfgFile
            };
            foreach (string c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        /// <summary>
        /// get_key: SDL named the keys and the cfg file uses those names ("caps lock",
        /// "right shift", "kp 8", ...). The table below covers what default.cfg and the
        /// README actually mention.
        /// </summary>
        public static int KeyByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string n = name.Trim().ToLowerInvariant();

            if (n.Length == 1) return n[0];

            // default.cfg writes numpad keys as [4], [/], [+], [.], which is the
            // notation the original keyboard handler used.
            if (n.Length == 3 && n[0] == '[' && n[2] == ']')
            {
                char inner = n[1];
                if (inner >= '0' && inner <= '9') return Kbd.SDLK_KP0 + (inner - '0');

                switch (inner)
                {
                    case '.': return Kbd.SDLK_KP_PERIOD;
                    case '/': return Kbd.SDLK_KP_DIVIDE;
                    case '*': return Kbd.SDLK_KP_MULTIPLY;
                    case '-': return Kbd.SDLK_KP_MINUS;
                    case '+': return Kbd.SDLK_KP_PLUS;
                }
            }

            switch (n)
            {
                case "space": return Kbd.SDLK_SPACE;
                case "return": case "enter": return Kbd.SDLK_RETURN;
                case "escape": return Kbd.SDLK_ESCAPE;
                case "tab": return Kbd.SDLK_TAB;
                case "backspace": return Kbd.SDLK_BACKSPACE;
                case "delete": return Kbd.SDLK_DELETE;
                case "insert": return Kbd.SDLK_INSERT;
                case "home": return Kbd.SDLK_HOME;
                case "end": return Kbd.SDLK_END;
                case "page up": return Kbd.SDLK_PAGEUP;
                case "page down": return Kbd.SDLK_PAGEDOWN;
                case "up": return Kbd.SDLK_UP;
                case "down": return Kbd.SDLK_DOWN;
                case "left": return Kbd.SDLK_LEFT;
                case "right": return Kbd.SDLK_RIGHT;
                case "numlock": return Kbd.SDLK_NUMLOCK;
                case "caps lock": return Kbd.SDLK_CAPSLOCK;
                case "scroll lock": return Kbd.SDLK_SCROLLOCK;
                case "left shift": return Kbd.SDLK_LSHIFT;
                case "right shift": return Kbd.SDLK_RSHIFT;
                case "left ctrl": return Kbd.SDLK_LCTRL;
                case "right ctrl": return Kbd.SDLK_RCTRL;
                case "left alt": return Kbd.SDLK_LALT;
                case "right alt": return Kbd.SDLK_RALT;
                case "kp enter": return Kbd.SDLK_KP_ENTER;
                case "kp equals": return Kbd.SDLK_KP_EQUALS;
                case "kp .": return Kbd.SDLK_KP_PERIOD;
                case "kp /": return Kbd.SDLK_KP_DIVIDE;
                case "kp *": return Kbd.SDLK_KP_MULTIPLY;
                case "kp -": return Kbd.SDLK_KP_MINUS;
                case "kp +": return Kbd.SDLK_KP_PLUS;
            }

            if (n.StartsWith("kp ") && n.Length == 4 && char.IsDigit(n[3]))
                return Kbd.SDLK_KP0 + (n[3] - '0');

            if (n.Length == 3 && n[0] == 'f' && char.IsDigit(n[1]))
            {
                int f = (n[1] - '0') * 10 + (n[2] - '0');
                if (f >= 1 && f <= 15) return Kbd.SDLK_F1 + f - 1;
            }
            if (n.Length == 2 && n[0] == 'f' && char.IsDigit(n[1]))
                return Kbd.SDLK_F1 + (n[1] - '1');

            return 0;
        }
    }
}
