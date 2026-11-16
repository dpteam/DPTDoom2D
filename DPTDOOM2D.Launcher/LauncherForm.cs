//
// DPT.DOOM 2D — launcher window.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DPTDOOM2D.Shared;

namespace DPTDOOM2D.Launcher
{
    /// <summary>
    /// The launcher: pick a mode, start the game, browse the master's session list.
    ///
    /// Deliberately a plain WinForms window rather than an embedded browser. The
    /// launcher's whole job is a dozen options and one button; a browser control
    /// would add an engine, a COM interop layer and a class of layout bugs for
    /// nothing.
    /// </summary>
    internal sealed class LauncherForm : Form
    {
        private LauncherSettings settings;

        private ComboBox mapBox;
        private RadioButton onePlayer;
        private RadioButton twoPlayers;
        private NumericUpDown portBox;
        private TextBox passwordBox;
        private ComboBox scaleBox;
        private CheckBox fullscreen;
        private TextBox masterBox;
        private CheckBox useRelay;
        private Button playButton;
        private Button refreshButton;
        private Button joinButton;
        private ListView sessions;
        private TextBox logBox;
        private TextBox commandBox;

        private Process running;

        public LauncherForm()
        {
            settings = LauncherSettings.Load();
            BuildUi();
            LoadSettings();
            AppendLog("Launcher ready. Game: " + (settings.GamePath.Length > 0 ? settings.GamePath : "(not found)"));
        }

        private void BuildUi()
        {
            Text = Branding.LauncherName;
            ClientSize = new Size(760, 620);
            MinimumSize = new Size(700, 580);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(18, 16, 20);
            ForeColor = Color.FromArgb(230, 230, 230);
            Font = new Font("Segoe UI", 9f);

            var title = new Label();
            title.Text = Branding.Name;
            title.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(255, 77, 90);
            title.AutoSize = true;
            title.Location = new Point(16, 10);
            Controls.Add(title);

            var subtitle = new Label();
            subtitle.Text = Branding.Tagline;
            subtitle.ForeColor = Color.FromArgb(150, 150, 160);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(20, 48);
            Controls.Add(subtitle);

            // ---- single player ------------------------------------------------
            var singleGroup = MakeGroup("Single player", 16, 80, 360, 92);
            Controls.Add(singleGroup);

            mapBox = new ComboBox();
            mapBox.DropDownStyle = ComboBoxStyle.DropDownList;
            for (int i = 1; i <= 19; ++i) mapBox.Items.Add("MAP" + i.ToString("00"));
            mapBox.SelectedIndex = 0;
            mapBox.Location = new Point(90, 24);
            mapBox.Width = 120;
            singleGroup.Controls.Add(MakeLabel("Level", 12, 27));
            singleGroup.Controls.Add(mapBox);

            scaleBox = new ComboBox();
            scaleBox.DropDownStyle = ComboBoxStyle.DropDownList;
            scaleBox.Items.AddRange(new object[] { "1x", "2x", "3x", "4x", "5x", "6x", "fit" });
            scaleBox.SelectedIndex = 1;
            scaleBox.Location = new Point(90, 54);
            scaleBox.Width = 120;
            singleGroup.Controls.Add(MakeLabel("Window", 12, 57));
            singleGroup.Controls.Add(scaleBox);

            fullscreen = new CheckBox();
            fullscreen.Text = "Fullscreen";
            fullscreen.Location = new Point(230, 54);
            fullscreen.AutoSize = true;
            singleGroup.Controls.Add(fullscreen);

            // ---- network ------------------------------------------------------
            var netGroup = MakeGroup("Two players (lockstep over TCP)", 16, 182, 360, 190);
            Controls.Add(netGroup);

            onePlayer = new RadioButton();
            onePlayer.Text = "Cooperative";
            onePlayer.Location = new Point(12, 24);
            onePlayer.AutoSize = true;
            onePlayer.Checked = true;
            netGroup.Controls.Add(onePlayer);

            twoPlayers = new RadioButton();
            twoPlayers.Text = "Deathmatch";
            twoPlayers.Location = new Point(140, 24);
            twoPlayers.AutoSize = true;
            netGroup.Controls.Add(twoPlayers);

            netGroup.Controls.Add(MakeLabel("Port", 12, 60));
            portBox = new NumericUpDown();
            portBox.Minimum = 1;
            portBox.Maximum = 65535;
            portBox.Value = 5030;
            portBox.Location = new Point(90, 57);
            portBox.Width = 100;
            netGroup.Controls.Add(portBox);

            netGroup.Controls.Add(MakeLabel("Password", 12, 90));
            passwordBox = new TextBox();
            passwordBox.Location = new Point(90, 87);
            passwordBox.Width = 250;
            passwordBox.UseSystemPasswordChar = true;
            netGroup.Controls.Add(passwordBox);

            useRelay = new CheckBox();
            useRelay.Text = "Register on the master (works through NAT)";
            useRelay.Location = new Point(12, 118);
            useRelay.AutoSize = true;
            netGroup.Controls.Add(useRelay);

            netGroup.Controls.Add(MakeLabel("Master", 12, 146));
            masterBox = new TextBox();
            masterBox.Location = new Point(90, 143);
            masterBox.Width = 250;
            netGroup.Controls.Add(masterBox);

            // ---- sessions -----------------------------------------------------
            var sessionGroup = MakeGroup("Public sessions", 16, 384, 360, 150);
            Controls.Add(sessionGroup);

            sessions = new ListView();
            sessions.View = View.Details;
            sessions.FullRowSelect = true;
            sessions.MultiSelect = false;
            sessions.Location = new Point(12, 22);
            sessions.Size = new Size(336, 88);
            sessions.Columns.Add("Name", 120);
            sessions.Columns.Add("Map", 50);
            sessions.Columns.Add("Players", 55);
            sessions.Columns.Add("Relay", 50);
            sessionGroup.Controls.Add(sessions);

            refreshButton = new Button();
            refreshButton.Text = "Refresh";
            refreshButton.Location = new Point(12, 114);
            refreshButton.Width = 90;
            refreshButton.Click += (s, e) => RefreshSessions();
            sessionGroup.Controls.Add(refreshButton);

            joinButton = new Button();
            joinButton.Text = "Join by id";
            joinButton.Location = new Point(110, 114);
            joinButton.Width = 90;
            joinButton.Click += (s, e) => JoinById();
            sessionGroup.Controls.Add(joinButton);

            // ---- right column -------------------------------------------------
            playButton = new Button();
            playButton.Text = "PLAY";
            playButton.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            playButton.BackColor = Color.FromArgb(200, 48, 60);
            playButton.ForeColor = Color.White;
            playButton.FlatStyle = FlatStyle.Flat;
            playButton.Location = new Point(396, 84);
            playButton.Size = new Size(348, 52);
            playButton.Click += (s, e) => StartGame();
            Controls.Add(playButton);

            var commandLabel = MakeLabel("Command line", 396, 148);
            Controls.Add(commandLabel);

            commandBox = new TextBox();
            commandBox.ReadOnly = true;
            commandBox.Location = new Point(396, 168);
            commandBox.Size = new Size(348, 22);
            commandBox.BackColor = Color.FromArgb(30, 28, 34);
            commandBox.ForeColor = Color.FromArgb(180, 180, 190);
            Controls.Add(commandBox);

            var logLabel = MakeLabel("Log", 396, 202);
            Controls.Add(logLabel);

            logBox = new TextBox();
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Location = new Point(396, 222);
            logBox.Size = new Size(348, 312);
            logBox.BackColor = Color.FromArgb(12, 10, 14);
            logBox.ForeColor = Color.FromArgb(200, 200, 210);
            logBox.Font = new Font("Consolas", 9f);
            Controls.Add(logBox);

            // Keep the preview in step with the controls it describes.
            mapBox.SelectedIndexChanged += (s, e) => UpdatePreview();
            scaleBox.SelectedIndexChanged += (s, e) => UpdatePreview();
            portBox.ValueChanged += (s, e) => UpdatePreview();
            useRelay.CheckedChanged += (s, e) => UpdatePreview();
            fullscreen.CheckedChanged += (s, e) => UpdatePreview();
            onePlayer.CheckedChanged += (s, e) => UpdatePreview();
            twoPlayers.CheckedChanged += (s, e) => UpdatePreview();
            passwordBox.TextChanged += (s, e) => UpdatePreview();
        }

        private static GroupBox MakeGroup(string text, int x, int y, int w, int h)
        {
            var group = new GroupBox();
            group.Text = text;
            group.ForeColor = Color.FromArgb(200, 200, 210);
            group.Location = new Point(x, y);
            group.Size = new Size(w, h);
            return group;
        }
        private static Label MakeLabel(string text, int x, int y)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            return label;
        }

        private void LoadSettings()
        {
            mapBox.SelectedIndex = Math.Max(0, Math.Min(18, settings.Map - 1));
            scaleBox.SelectedIndex = settings.Scale <= 0 ? 6 : Math.Max(0, Math.Min(5, settings.Scale - 1));
            fullscreen.Checked = settings.Fullscreen;
            portBox.Value = Math.Max(1, Math.Min(65535, settings.Port));
            passwordBox.Text = settings.Password;
            masterBox.Text = settings.Master;
            useRelay.Checked = settings.UseRelay;

            if (settings.TwoPlayers && settings.Deathmatch) twoPlayers.Checked = true;
            else onePlayer.Checked = true;

            if (settings.GamePath.Length == 0) settings.GamePath = LauncherSettings.FindGame() ?? "";

            UpdatePreview();
        }

        private void Persist()
        {
            settings.Map = mapBox.SelectedIndex + 1;
            settings.Scale = scaleBox.SelectedIndex == 6 ? 0 : scaleBox.SelectedIndex + 1;
            settings.Fullscreen = fullscreen.Checked;
            settings.Port = (int)portBox.Value;
            settings.Password = passwordBox.Text;
            settings.Master = masterBox.Text;
            settings.UseRelay = useRelay.Checked;
            settings.TwoPlayers = twoPlayers.Checked;
            settings.Deathmatch = twoPlayers.Checked;
            settings.Save();
        }

        private void UpdatePreview()
        {
            Persist();

            string args = settings.BuildArguments(true);
            commandBox.Text = string.IsNullOrEmpty(args) ? "(single player, defaults)" : args;
        }

        private void AppendLog(string line)
        {
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
        }

        private void StartGame()
        {
            if (running != null && !running.HasExited)
            {
                AppendLog("the game is already running");
                return;
            }

            string exe = settings.GamePath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                exe = LauncherSettings.FindGame();
                if (exe == null)
                {
                    MessageBox.Show(this, "dptdoom2d.exe не найден рядом с лаунчером.",
                        Branding.LauncherName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                settings.GamePath = exe;
            }

            Persist();

            var info = new ProcessStartInfo();
            info.FileName = exe;
            info.Arguments = settings.BuildArguments(false);
            info.WorkingDirectory = Path.GetDirectoryName(exe);
            info.UseShellExecute = false;

            try
            {
                running = Process.Start(info);
                AppendLog("started: " + Path.GetFileName(exe) + " " + settings.BuildArguments(true));
            }
            catch (Exception e)
            {
                AppendLog("cannot start: " + e.Message);
                MessageBox.Show(this, e.Message, Branding.LauncherName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshSessions()
        {
            sessions.Items.Clear();

            string host = masterBox.Text.Trim();
            if (host.Length == 0)
            {
                AppendLog("set a master address first");
                return;
            }

            string address = host;
            int port = MasterProtocol.DefaultPort;

            int colon = address.IndexOf(':');
            if (colon > 0)
            {
                port = int.Parse(address.Substring(colon + 1));
                address = address.Substring(0, colon);
            }

            AppendLog("querying " + address + ":" + port + " ...");

            try
            {
                List<MasterServerEntry> entries = MasterClient.List(address, port, 4000);

                foreach (MasterServerEntry entry in entries)
                {
                    var item = new ListViewItem(entry.Info.Name);
                    item.SubItems.Add(entry.Info.MapText);
                    item.SubItems.Add(entry.Info.Players.ToString());
                    item.SubItems.Add(entry.RelayAvailable ? "yes" : "no");
                    item.Tag = entry;
                    sessions.Items.Add(item);
                }

                AppendLog(entries.Count + " session(s)");
            }
            catch (Exception e)
            {
                AppendLog("master error: " + e.Message);
            }
        }

        private void JoinById()
        {
            string exe = settings.GamePath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) exe = LauncherSettings.FindGame();
            if (exe == null) { AppendLog("game not found"); return; }

            string sessionId = Prompt.Show(this, "Join by session id", "Paste the 32-character session id:");
            if (string.IsNullOrEmpty(sessionId)) return;

            settings.RelaySession = sessionId.Trim();
            Persist();

            var info = new ProcessStartInfo();
            info.FileName = exe;
            info.Arguments = "-net join -master " + settings.Master
                + " -relay-session " + settings.RelaySession
                + " -scale " + settings.Scale;
            info.WorkingDirectory = Path.GetDirectoryName(exe);
            info.UseShellExecute = false;

            try
            {
                running = Process.Start(info);
                AppendLog("joining " + settings.RelaySession);
            }
            catch (Exception e)
            {
                AppendLog("cannot start: " + e.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Persist();
            base.OnFormClosing(e);
        }
    }

    /// <summary>A one-field dialog: WinForms has no InputBox and this is all it needs.</summary>
    internal static class Prompt
    {
        public static string Show(IWin32Window owner, string title, string label)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.ClientSize = new Size(420, 120);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var text = new Label();
                text.Text = label;
                text.Location = new Point(12, 12);
                text.AutoSize = true;
                form.Controls.Add(text);

                var input = new TextBox();
                input.Location = new Point(12, 36);
                input.Width = 396;
                form.Controls.Add(input);

                var ok = new Button();
                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(252, 76);
                form.Controls.Add(ok);

                var cancel = new Button();
                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(333, 76);
                form.Controls.Add(cancel);

                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
            }
        }
    }
}
