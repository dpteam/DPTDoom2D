//
// DPT.DOOM 2D — launcher settings.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DPTDOOM2D.Launcher
{
    /// <summary>
    /// Everything the launcher remembers between runs, plus the wire format it
    /// shares with the page.
    ///
    /// Stored as a plain key=value file next to the executable, not in the
    /// registry: the launcher is portable, and a folder that can be copied to a
    /// USB stick should carry its settings with it.
    /// </summary>
    internal sealed class LauncherSettings
    {
        // ---- session ---------------------------------------------------------

        /// <summary>single | hostcoop | hostdm | join</summary>
        public string Mode = "single";

        public string GamePath = "";
        public int Map = 1;
        public string MapPack = "";

        public int Scale = 2;
        public string Stretch = "near";
        public bool Fullscreen;

        public int Port = 5030;
        public string Password = "";
        public string Master = "127.0.0.1:25135";
        public string RelaySession = "";
        public string Address = "127.0.0.1";
        public string PlayerName = "Player";

        /// <summary>Enabled mods from Data/Mods, in load order.</summary>
        public readonly List<string> Mods = new List<string>();

        /// <summary>Theme: "dark" or "light".</summary>
        public string Theme = "dark";

        private const string FileName = "launcher.cfg";

        public static string SettingsPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName); }
        }

        /// <summary>
        /// Where the launcher writes what it generates. The page is written to
        /// disk and navigated to as a file, so it needs a folder of its own.
        /// </summary>
        public static string DataDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage"); }
        }

        public static LauncherSettings Load()
        {
            var settings = new LauncherSettings();
            string path = SettingsPath;
            if (!File.Exists(path)) return settings;

            try
            {
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "mode": settings.Mode = value; break;
                        case "game": settings.GamePath = value; break;
                        case "map": settings.Map = ParseInt(value, 1); break;
                        case "map_pack": settings.MapPack = value; break;
                        case "scale": settings.Scale = ParseInt(value, 2); break;
                        case "stretch": settings.Stretch = value; break;
                        case "fullscreen": settings.Fullscreen = ParseBool(value); break;
                        case "port": settings.Port = ParseInt(value, 5030); break;
                        case "password": settings.Password = value; break;
                        case "master": settings.Master = value; break;
                        case "relay_session": settings.RelaySession = value; break;
                        case "address": settings.Address = value; break;
                        case "name": settings.PlayerName = value; break;
                        case "theme": settings.Theme = value; break;
                        case "mods":
                            settings.Mods.Clear();
                            foreach (string mod in value.Split('|'))
                                if (mod.Length > 0) settings.Mods.Add(mod);
                            break;
                    }
                }
            }
            catch
            {
                // A damaged config must not stop the launcher from opening.
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                var text = new StringBuilder();
                text.AppendLine("; DPT.DOOM 2D launcher settings");
                text.AppendLine("mode=" + Mode);
                text.AppendLine("game=" + GamePath);
                text.AppendLine("map=" + Map);
                text.AppendLine("map_pack=" + MapPack);
                text.AppendLine("scale=" + Scale);
                text.AppendLine("stretch=" + Stretch);
                text.AppendLine("fullscreen=" + (Fullscreen ? "on" : "off"));
                text.AppendLine("port=" + Port);
                text.AppendLine("password=" + Password);
                text.AppendLine("master=" + Master);
                text.AppendLine("relay_session=" + RelaySession);
                text.AppendLine("address=" + Address);
                text.AppendLine("name=" + PlayerName);
                text.AppendLine("theme=" + Theme);
                text.AppendLine("mods=" + string.Join("|", Mods.ToArray()));

                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, text.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Read-only folder: the launcher still works, it just forgets.
            }
        }

        // ---- wire format shared with the page --------------------------------

        /// <summary>
        /// Encodes the state as newline-separated fields. The page splits on '\n'
        /// and maps the fields by position, so both ends must agree on the order;
        /// trailing fields are optional, which lets an old page work with a new
        /// host.
        /// </summary>
        public string Encode()
        {
            return string.Join("\n", new[]
            {
                Mode,
                GamePath,
                Map.ToString(),
                MapPack,
                Scale.ToString(),
                Stretch,
                Fullscreen ? "1" : "0",
                Port.ToString(),
                Password,
                Master,
                RelaySession,
                Address,
                PlayerName,
                string.Join("|", Mods.ToArray())
            });
        }

        /// <summary>Applies a payload from the page. Unknown or short input is ignored.</summary>
        public void Decode(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;

            string[] parts = payload.Split('\n');

            if (parts.Length > 0 && parts[0].Length > 0) Mode = parts[0];
            if (parts.Length > 1) GamePath = parts[1];
            if (parts.Length > 2) Map = ParseInt(parts[2], Map);
            if (parts.Length > 3) MapPack = parts[3];
            if (parts.Length > 4) Scale = ParseInt(parts[4], Scale);
            if (parts.Length > 5 && parts[5].Length > 0) Stretch = parts[5];
            if (parts.Length > 6) Fullscreen = parts[6] == "1";
            if (parts.Length > 7) Port = ParseInt(parts[7], Port);
            if (parts.Length > 8) Password = parts[8];
            if (parts.Length > 9) Master = parts[9];
            if (parts.Length > 10) RelaySession = parts[10];
            if (parts.Length > 11) Address = parts[11];
            if (parts.Length > 12) PlayerName = parts[12];

            if (parts.Length > 13)
            {
                Mods.Clear();
                foreach (string mod in parts[13].Split('|'))
                    if (mod.Length > 0) Mods.Add(mod);
            }
        }

        // ---- command line ----------------------------------------------------

        /// <summary>The command line for the chosen mode, so the launcher can show it.</summary>
        public string BuildArguments(bool maskPassword)
        {
            var builder = new StringBuilder();

            builder.Append("-scale ").Append(Scale).Append(' ');

            if (!string.IsNullOrEmpty(Stretch)) builder.Append("-stretch ").Append(Stretch).Append(' ');
            if (Fullscreen) builder.Append("-fullscr ");

            if (!string.IsNullOrEmpty(MapPack))
                builder.Append("-map-pack ").Append(Quote(MapPack)).Append(' ');

            // Mods load in the order they are listed, and later ones win, so a
            // patch that overrides a resource goes below the mod it patches.
            for (int i = 0; i < Mods.Count; ++i)
                builder.Append("-file ").Append(Quote(Path.Combine(ModsDirectory, Mods[i]))).Append(' ');

            if (Mode == "join")
            {
                builder.Append("-net join ");

                if (!string.IsNullOrEmpty(RelaySession) && !string.IsNullOrEmpty(Master))
                {
                    // Joining by session id goes through the master's relay, so the
                    // address of the host is not needed and no port is forwarded.
                    builder.Append("-master ").Append(Quote(Master)).Append(' ');
                    builder.Append("-relay-session ").Append(RelaySession).Append(' ');
                }
                else
                {
                    builder.Append("-address ").Append(Quote(Address)).Append(' ');
                    builder.Append("-port ").Append(Port).Append(' ');
                }

                if (!string.IsNullOrEmpty(Password))
                    builder.Append("-password ").Append(maskPassword ? "****" : Quote(Password)).Append(' ');
            }
            else
            {
                builder.Append("-warp ").Append(Map).Append(' ');

                if (Mode == "hostcoop" || Mode == "hostdm")
                {
                    builder.Append("-net host ");
                    builder.Append("-port ").Append(Port).Append(' ');
                    builder.Append("-map ").Append(Map).Append(' ');

                    if (Mode == "hostdm") builder.Append("-deathmatch ");

                    if (!string.IsNullOrEmpty(Password))
                        builder.Append("-password ").Append(maskPassword ? "****" : Quote(Password)).Append(' ');

                    if (!string.IsNullOrEmpty(Master))
                        builder.Append("-master ").Append(Quote(Master)).Append(' ').Append("-relay ");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.IndexOf(' ') < 0 && value.IndexOf('"') < 0) return value;
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        // ---- helpers ---------------------------------------------------------

        private static int ParseInt(string value, int fallback)
        {
            int result;
            return int.TryParse(value, out result) ? result : fallback;
        }

        private static bool ParseBool(string value)
        {
            return string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || value == "1";
        }

        /// <summary>Finds the game next to the launcher, or one level up in a dev tree.</summary>
        public static string FindGame()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates = {
                Path.Combine(directory, "dptdoom2d.exe"),
                Path.Combine(directory, "..", "DPTDOOM2D", "bin", "Release", "net40", "dptdoom2d.exe"),
                Path.Combine(directory, "..", "..", "..", "DPTDOOM2D", "bin", "Release", "net40", "dptdoom2d.exe"),
                Path.Combine(directory, "..", "..", "..", "..", "DPTDOOM2D", "bin", "Release", "net40", "dptdoom2d.exe")
            };

            foreach (string candidate in candidates)
            {
                string full = Path.GetFullPath(candidate);
                if (File.Exists(full)) return full;
            }

            return null;
        }

        /// <summary>WADs available in the game's Data/Maps folder, as plain names.</summary>
        public static string[] FindMapPacks(string gamePath)
        {
            try
            {
                if (string.IsNullOrEmpty(gamePath)) return new string[0];

                string directory = Path.Combine(Path.GetDirectoryName(gamePath), "Data", "Maps");
                if (!Directory.Exists(directory)) return new string[0];

                string[] files = Directory.GetFiles(directory, "*.wad");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                string[] names = new string[files.Length];
                for (int i = 0; i < files.Length; ++i) names[i] = Path.GetFileName(files[i]);

                return names;
            }
            catch
            {
                return new string[0];
            }
        }

        /// <summary>Where the mods live: &lt;game&gt;/Data/Mods.</summary>
        public static string ModsDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Mods"); }
        }

        /// <summary>Files a mod list can offer: WAD and PK3 from Data/Mods.</summary>
        public static string[] FindMods(string gamePath)
        {
            try
            {
                if (string.IsNullOrEmpty(gamePath)) return new string[0];

                string directory = Path.Combine(Path.GetDirectoryName(gamePath), "Data", "Mods");
                if (!Directory.Exists(directory)) return new string[0];

                var found = new List<string>();

                foreach (string file in Directory.GetFiles(directory))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".wad" || ext == ".pk3" || ext == ".zip") found.Add(file);
                }

                found.Sort(StringComparer.OrdinalIgnoreCase);

                string[] names = new string[found.Count];
                for (int i = 0; i < found.Count; ++i) names[i] = Path.GetFileName(found[i]);

                return names;
            }
            catch
            {
                return new string[0];
            }
        }

        /// <summary>Levels a pack defines, by looking at its WAD directory.</summary>
        public static int CountLevels(string gamePath, string mapPack)
        {
            try
            {
                if (string.IsNullOrEmpty(gamePath) || string.IsNullOrEmpty(mapPack)) return 19;

                string directory = Path.Combine(Path.GetDirectoryName(gamePath), "Data", "Maps");
                string path = Path.Combine(directory, mapPack);
                if (!File.Exists(path)) return 19;

                byte[] data = File.ReadAllBytes(path);
                if (data.Length < 12) return 19;

                int count = BitConverter.ToInt32(data, 4);
                int dirOfs = BitConverter.ToInt32(data, 8);

                int levels = 0;
                for (int i = 0; i < count; ++i)
                {
                    int at = dirOfs + i * 16;
                    if (at + 16 > data.Length) break;

                    string name = Encoding.ASCII.GetString(data, at + 8, 8).TrimEnd('\0');
                    if (name.StartsWith("MAP", StringComparison.OrdinalIgnoreCase) && name.Length == 5) ++levels;
                }

                return levels > 0 ? levels : 19;
            }
            catch
            {
                return 19;
            }
        }
    }
}
