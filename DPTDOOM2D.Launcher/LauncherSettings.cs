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
    /// Everything the launcher remembers between runs.
    ///
    /// Stored as a plain key=value file next to the executable, not in the
    /// registry: the launcher is portable, and a folder that can be copied to a USB
    /// stick should carry its settings with it.
    /// </summary>
    internal sealed class LauncherSettings
    {
        public string GamePath = "";
        public int Map = 1;
        public bool TwoPlayers;
        public bool Deathmatch;
        public int Scale = 2;
        public bool Fullscreen;
        public int Port = 5030;
        public string Password = "";
        public string Master = "127.0.0.1:25135";
        public bool UseRelay;
        public string PlayerName = "Player";
        public string RelaySession = "";

        private const string FileName = "launcher.cfg";

        public static string SettingsPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName); }
        }

        public static LauncherSettings Load()
        {
            var settings = new LauncherSettings();
            string path = SettingsPath;
            if (!File.Exists(path)) return settings;

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
                    case "game": settings.GamePath = value; break;
                    case "map": settings.Map = ParseInt(value, 1); break;
                    case "two_players": settings.TwoPlayers = ParseBool(value); break;
                    case "deathmatch": settings.Deathmatch = ParseBool(value); break;
                    case "scale": settings.Scale = ParseInt(value, 2); break;
                    case "fullscreen": settings.Fullscreen = ParseBool(value); break;
                    case "port": settings.Port = ParseInt(value, 5030); break;
                    case "password": settings.Password = value; break;
                    case "master": settings.Master = value; break;
                    case "use_relay": settings.UseRelay = ParseBool(value); break;
                    case "name": settings.PlayerName = value; break;
                    case "relay_session": settings.RelaySession = value; break;
                }
            }

            return settings;
        }

        public void Save()
        {
            var text = new StringBuilder();
            text.AppendLine("; DPT.DOOM 2D launcher settings");
            text.AppendLine("game=" + GamePath);
            text.AppendLine("map=" + Map);
            text.AppendLine("two_players=" + (TwoPlayers ? "on" : "off"));
            text.AppendLine("deathmatch=" + (Deathmatch ? "on" : "off"));
            text.AppendLine("scale=" + Scale);
            text.AppendLine("fullscreen=" + (Fullscreen ? "on" : "off"));
            text.AppendLine("port=" + Port);
            text.AppendLine("password=" + Password);
            text.AppendLine("master=" + Master);
            text.AppendLine("use_relay=" + (UseRelay ? "on" : "off"));
            text.AppendLine("name=" + PlayerName);
            text.AppendLine("relay_session=" + RelaySession);

            File.WriteAllText(SettingsPath, text.ToString(), Encoding.UTF8);
        }

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

        /// <summary>The command line for the chosen mode, so the launcher can show it.</summary>
        public string BuildArguments(bool maskPassword)
        {
            var builder = new StringBuilder();

            builder.Append("-scale ").Append(Scale).Append(' ');
            if (Fullscreen) builder.Append("-fullscr ");

            if (TwoPlayers)
            {
                builder.Append("-net host ");
                builder.Append("-port ").Append(Port).Append(' ');
                builder.Append("-map ").Append(Map).Append(' ');
                if (Deathmatch) builder.Append("-deathmatch ");

                if (!string.IsNullOrEmpty(Password))
                    builder.Append("-password ").Append(maskPassword ? "****" : Quote(Password)).Append(' ');

                if (UseRelay && !string.IsNullOrEmpty(Master))
                    builder.Append("-master ").Append(Master).Append(' ');
            }

            return builder.ToString().TrimEnd();
        }

        private static string Quote(string value)
        {
            if (value.IndexOf(' ') < 0 && value.IndexOf('"') < 0) return value;
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
