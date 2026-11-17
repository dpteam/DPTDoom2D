//
// DPT.DOOM 2D — launcher window.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DPTDOOM2D.Shared;

namespace DPTDOOM2D.Launcher
{
    /// <summary>
    /// The launcher: pick a mode, start the game, browse the master's session list.
    ///
    /// The interface is real HTML/CSS/JS rendered by the WebBrowser control, the
    /// same approach DPT.DOOM uses. MSHTML ships with every supported Windows, so
    /// this costs no extra dependency — and it keeps the launcher's layout in a
    /// file a designer can edit instead of in a hundred lines of control
    /// positioning.
    ///
    /// The bridge is navigation-based rather than COM: the page navigates to
    /// dptdoom2d://&lt;verb&gt;, the host cancels that navigation and reads the payload
    /// back out of the document. That works on Windows XP, where the managed
    /// ObjectForScripting route needs a newer MSHTML than the OS has.
    /// </summary>
    internal sealed class LauncherForm : Form
    {
        /// <summary>Private URL scheme used by the page to call into the host.</summary>
        private const string BridgeScheme = "dptdoom2d://";

        private readonly LauncherSettings settings;

        private WebBrowser browser;
        private Process gameProcess;
        private string lastLogDirectory;

        public LauncherForm()
        {
            settings = LauncherSettings.Load();

            Text = Branding.LauncherName;
            ClientSize = new Size(1000, 720);
            MinimumSize = new Size(860, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(0x13, 0x15, 0x1D);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            browser = new WebBrowser();
            browser.Dock = DockStyle.Fill;
            browser.IsWebBrowserContextMenuEnabled = false;
            browser.AllowWebBrowserDrop = false;
            browser.WebBrowserShortcutsEnabled = true;
            browser.ScriptErrorsSuppressed = true;
            browser.Navigating += OnNavigating;
            Controls.Add(browser);

            LoadPage();
        }

        // ---------------------------------------------------------------- page

        /// <summary>
        /// Merges the three embedded assets into one document. Keeping CSS and
        /// script in separate files during development keeps them readable; at
        /// runtime the page is written to disk as one file.
        /// </summary>
        public static string AssemblePage()
        {
            string html = ReadAsset("ui.html");
            html = html.Replace("/*__STYLE__*/", Brand(ReadAsset("theme.css")));
            html = html.Replace("/*__SCRIPT__*/", ReadAsset("app.js"));
            return Brand(html);
        }

        /// <summary>
        /// Substitutes the project's own name and colours into a page asset, so a
        /// fork that renames itself does not have to hand-edit an embedded file.
        /// </summary>
        private static string Brand(string asset)
        {
            int dot = Branding.Name.IndexOf('.');

            string marked = dot > 0 && dot < Branding.Name.Length - 1
                ? Branding.Name.Substring(0, dot) + "<em>" +
                  Branding.Name.Substring(dot, 1) + "</em>" +
                  Branding.Name.Substring(dot + 1)
                : Branding.Name;

            return asset
                .Replace("__BRAND_NAME__", Branding.Name)
                .Replace("__BRAND_MARKED__", marked)
                .Replace("__BRAND_TITLE__", Branding.LauncherName)
                .Replace("__BRAND_TAGLINE__", Branding.Tagline)
                .Replace("__BRAND_VERSION__", Branding.Version)
                .Replace("__BRAND_PRIMARY__", Branding.PrimaryColor)
                .Replace("__BRAND_SECONDARY__", Branding.SecondaryColor)
                .Replace("__BRAND_DARK__", Branding.DarkColor)
                .Replace("__BRAND_ONDARK__", Branding.OnDarkColor);
        }

        private static string ReadAsset(string name)
        {
            var assembly = typeof(LauncherForm).Assembly;

            using (Stream stream = assembly.GetManifestResourceStream("DPTDOOM2D.Launcher.Assets." + name))
            {
                if (stream == null) throw new Exception("Missing embedded asset: " + name);

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Writes the page into Storage/webview and navigates to it as a file.
        ///
        /// Loading a real file matters for the bridge: a document assigned through
        /// DocumentText lives at about:blank, and MSHTML does not hand the host to
        /// that origin on every Windows version. A file in the Local Machine zone
        /// always gets it.
        /// </summary>
        private void LoadPage()
        {
            try
            {
                string directory = Path.Combine(LauncherSettings.DataDirectory, "webview");
                Directory.CreateDirectory(directory);

                string path = Path.Combine(directory, "launcher.html");
                File.WriteAllText(path, AssemblePage(), new UTF8Encoding(false));

                // DPTDOOM2D_LAUNCHER_TAB=servers opens the server browser; the
                // page reads it from the hash, which is also what a deep link uses.
                string tab = Environment.GetEnvironmentVariable("DPTDOOM2D_LAUNCHER_TAB");

                browser.Navigate(path + (string.IsNullOrEmpty(tab) ? "" : "#" + tab));
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "Не удалось открыть интерфейс лаунчера: " + e.Message,
                    Branding.LauncherName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------------------------------------------------------- bridge

        /// <summary>
        /// The page calls the host by navigating to dptdoom2d://&lt;verb&gt;. The
        /// navigation is cancelled and the verb dispatched; the payload is read
        /// back from the page because it can be arbitrarily long.
        /// </summary>
        private void OnNavigating(object sender, WebBrowserNavigatingEventArgs e)
        {
            string url = e.Url == null ? "" : e.Url.AbsoluteUri;

            if (!url.StartsWith(BridgeScheme, StringComparison.OrdinalIgnoreCase)) return;

            e.Cancel = true;

            string verb = url.Substring(BridgeScheme.Length).Trim('/');

            int query = verb.IndexOf('?');
            if (query >= 0) verb = verb.Substring(0, query);

            string payload = TakePayload();

            try
            {
                HandleVerb(verb, payload);
            }
            catch (Exception ex)
            {
                Send("fatal", "Ошибка лаунчера: " + ex.Message);
            }
        }

        private string TakePayload()
        {
            if (browser.Document == null) return "";

            try
            {
                object result = browser.Document.InvokeScript("dptdoom2dTakePayload");
                return Convert.ToString(result) ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>Host → page.</summary>
        private void Send(string verb, string payload)
        {
            if (browser.Document == null) return;

            try
            {
                browser.Document.InvokeScript("dptReceive", new object[] { verb, payload ?? "" });
            }
            catch
            {
                // The document is navigating or has been replaced.
            }
        }

        /// <summary>
        /// Single entry point for everything the page asks for.
        ///
        /// The comparison is **case-insensitive**, and that is not defensive
        /// coding: MSHTML lower-cases the authority part of a URI, so
        /// <c>dptdoom2d://masterSave</c> arrives here as <c>mastersave</c>. A
        /// case-sensitive switch silently ignores every camel-case verb.
        /// </summary>
        public void HandleVerb(string verb, string payload)
        {
            string name = (verb ?? "").Trim();

            switch (name.ToLowerInvariant())
            {
                case "ready":
                    Send("theme", settings.Theme);
                    Send("state", settings.Encode());
                    Send("game", DescribeGame());
                    Send("storage", DescribeStorage());
                    Send("hasLog", lastLogDirectory != null ? "1" : "0");
                    Send("running", IsGameRunning() ? "1" : "0");
                    Send("status", "Готов. Выбери режим и нажми «Запустить игру».\n0");
                    break;

                case "preview":
                    settings.Decode(payload);
                    settings.Save();
                    Send("preview", settings.BuildArguments(true));
                    break;

                case "pack":
                    settings.Decode(payload);
                    settings.MapPack = payload ?? "";
                    settings.Save();
                    Send("game", DescribeGame());
                    break;

                case "browse":
                    OnBrowse();
                    break;

                case "launch":
                    OnLaunch(payload);
                    break;

                case "mastersave":
                    settings.Master = (payload ?? "").Trim();
                    settings.Save();
                    Send("status", "Мастер сохранён: " + settings.Master + "\n0");
                    break;

                case "masterrefresh":
                    OnMasterRefresh(payload);
                    break;

                case "themesave":
                    settings.Theme = payload == "light" ? "light" : "dark";
                    settings.Save();
                    break;

                case "openlog":
                    OnOpenLog();
                    break;
            }
        }

        // ---------------------------------------------------------------- verbs

        /// <summary>
        /// Describes the game to the page: exe name, data folder, the map packs it
        /// can see and how many levels the chosen pack defines.
        /// </summary>
        private string DescribeGame()
        {
            if (string.IsNullOrEmpty(settings.GamePath) || !File.Exists(settings.GamePath))
            {
                string found = LauncherSettings.FindGame();
                if (found != null) settings.GamePath = found;
            }

            if (string.IsNullOrEmpty(settings.GamePath) || !File.Exists(settings.GamePath))
                return "dptdoom2d.exe\nИгра не найдена рядом с лаунчером — выбери файл вручную.\n\n19";

            string directory = Path.GetDirectoryName(settings.GamePath);
            string data = Path.Combine(directory, "Data");

            string hint = Directory.Exists(data)
                ? "Данные: " + data
                : "Папка Data не найдена рядом с игрой: " + directory;

            string[] packs = LauncherSettings.FindMapPacks(settings.GamePath);
            int levels = LauncherSettings.CountLevels(settings.GamePath, settings.MapPack);

            string[] mods = LauncherSettings.FindMods(settings.GamePath);
            var modRows = new StringBuilder();

            for (int i = 0; i < mods.Length; ++i)
            {
                string full = Path.Combine(ModsDirectoryFor(settings.GamePath), mods[i]);

                if (modRows.Length > 0) modRows.Append('\t');
                modRows.Append(Safe(mods[i])).Append('=').Append(DescribeSize(full));
            }

            return Path.GetFileName(settings.GamePath) + "\n" + hint + "\n" +
                string.Join("\t", packs) + "\n" + levels + "\n" + modRows;
        }

        private static string ModsDirectoryFor(string gamePath)
        {
            return string.IsNullOrEmpty(gamePath)
                ? LauncherSettings.ModsDirectory
                : Path.Combine(Path.GetDirectoryName(gamePath), "Data", "Mods");
        }

        private static string DescribeSize(string path)
        {
            try
            {
                long size = new FileInfo(path).Length;
                if (size >= 1024 * 1024) return (size / (1024.0 * 1024.0)).ToString("0.0") + " МБ";
                return Math.Max(1, size / 1024) + " КБ";
            }
            catch
            {
                return "?";
            }
        }

        private string DescribeStorage()
        {
            return "Лаунчер: " + LauncherSettings.DataDirectory + " · игра пишет логи в Storage\\Logs";
        }

        private void OnBrowse()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Выбери dptdoom2d.exe";
                dialog.Filter = "DPT.DOOM 2D (dptdoom2d.exe)|dptdoom2d.exe|Программы (*.exe)|*.exe";

                if (Directory.Exists(Path.GetDirectoryName(settings.GamePath ?? "")))
                    dialog.InitialDirectory = Path.GetDirectoryName(settings.GamePath);

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                settings.GamePath = dialog.FileName;
                settings.Save();

                Send("state", settings.Encode());
                Send("game", DescribeGame());
            }
        }

        private void OnLaunch(string payload)
        {
            settings.Decode(payload);
            settings.Save();

            if (IsGameRunning())
            {
                Send("status", "Игра уже запущена.\n1");
                return;
            }

            string exe = settings.GamePath;

            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                exe = LauncherSettings.FindGame();

                if (exe == null)
                {
                    Send("status", "dptdoom2d.exe не найден рядом с лаунчером.\n1");
                    return;
                }

                settings.GamePath = exe;
            }

            var info = new ProcessStartInfo();
            info.FileName = exe;
            info.Arguments = settings.BuildArguments(false);
            info.WorkingDirectory = Path.GetDirectoryName(exe);
            info.UseShellExecute = false;

            try
            {
                gameProcess = Process.Start(info);
                lastLogDirectory = Path.Combine(Path.GetDirectoryName(exe), "Storage", "Logs");

                Send("running", "1");
                Send("hasLog", "1");
                Send("status", "Запущено: " + Path.GetFileName(exe) + " " + settings.BuildArguments(true) + "\n0");

                gameProcess.EnableRaisingEvents = true;
                gameProcess.Exited += OnGameExited;
            }
            catch (Exception e)
            {
                Send("status", "Не удалось запустить: " + e.Message + "\n1");
            }
        }

        private void OnGameExited(object sender, EventArgs e)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    Send("running", "0");
                    Send("status", "Игра завершена.\n0");
                });
            }
            catch
            {
                // The window is closing.
            }
        }

        private bool IsGameRunning()
        {
            if (gameProcess == null) return false;

            try { return !gameProcess.HasExited; }
            catch { return false; }
        }

        /// <summary>
        /// Queries the master and pushes one tab-separated row per session:
        /// name, map, map number, players, address, port, relay, id.
        /// </summary>
        private void OnMasterRefresh(string endpoint)
        {
            string host;
            int port;

            if (!SplitEndpoint(string.IsNullOrEmpty(endpoint) ? settings.Master : endpoint, out host, out port))
            {
                Send("status", "Адрес мастера должен быть вида 127.0.0.1:25135\n1");
                return;
            }

            settings.Master = host + ":" + port;
            settings.Save();

            try
            {
                List<MasterServerEntry> entries = MasterClient.List(host, port, 4000);

                var rows = new StringBuilder();
                for (int i = 0; i < entries.Count; ++i)
                {
                    MasterServerEntry entry = entries[i];
                    if (entry.Info == null) continue;

                    string map = entry.Info.MapText ?? "MAP01";
                    int number = ParseMapNumber(map);

                    if (rows.Length > 0) rows.Append('\n');

                    rows.Append(Safe(entry.Info.Name)).Append('\t');
                    rows.Append(map).Append('\t');
                    rows.Append(number).Append('\t');
                    rows.Append(entry.Info.Players).Append('\t');
                    rows.Append(entry.AddressText).Append('\t');
                    rows.Append(entry.Info.Port).Append('\t');
                    rows.Append(entry.RelayAvailable ? "1" : "0").Append('\t');
                    rows.Append(MasterProtocol.IdToHex(entry.Id));
                }

                Send("servers", rows.ToString());
            }
            catch (Exception e)
            {
                Send("servers", "");
                Send("status", "Мастер недоступен: " + e.Message + "\n1");
            }
        }

        private static string Safe(string text)
        {
            return (text ?? "").Replace('\t', ' ').Replace('\n', ' ');
        }

        private static int ParseMapNumber(string map)
        {
            int number;
            return map != null && map.Length == 5 && int.TryParse(map.Substring(3), out number) ? number : 1;
        }

        private static bool SplitEndpoint(string endpoint, out string host, out int port)
        {
            host = "";
            port = MasterProtocol.DefaultPort;

            if (string.IsNullOrEmpty(endpoint)) return false;

            string value = endpoint.Trim();
            int colon = value.LastIndexOf(':');

            if (colon < 0)
            {
                host = value;
                return host.Length > 0;
            }

            host = value.Substring(0, colon).Trim();
            return host.Length > 0 && int.TryParse(value.Substring(colon + 1), out port);
        }

        private void OnOpenLog()
        {
            try
            {
                if (lastLogDirectory != null && Directory.Exists(lastLogDirectory))
                    Process.Start("explorer.exe", "\"" + lastLogDirectory + "\"");
                else
                    Send("status", "Логов пока нет: игра ещё не запускалась.\n1");
            }
            catch (Exception e)
            {
                Send("status", "Не удалось открыть папку: " + e.Message + "\n1");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            settings.Save();
            base.OnFormClosing(e);
        }
    }
}
