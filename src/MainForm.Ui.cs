using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RobloxKeeper
{
    // Layout and the widgets that hang off it. Every card is a fixed-position
    // panel; the window does not resize, so the coordinates are the layout.
    partial class MainForm
    {
        void BuildUi()
        {
            // --- Header ---
            Label lblTitle = new Label();
            lblTitle.Text = "RobloxKeeper";
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(18, 12);
            lblTitle.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            lblTitle.ForeColor = Theme.Text;
            lblTitle.BackColor = Theme.Bg;
            Controls.Add(lblTitle);

            Label lblVer = new Label();
            lblVer.Text = "v" + AppInfo.APP_VERSION;
            lblVer.AutoSize = true;
            lblVer.Location = new Point(152, 19);
            lblVer.Font = new Font("Segoe UI", 8.25f);
            lblVer.ForeColor = Theme.Muted;
            lblVer.BackColor = Theme.Bg;
            Controls.Add(lblVer);

            chkAutostart = Ui.DarkCheck("Start with Windows", 296, 16, 9f);
            chkAutostart.BackColor = Theme.Bg;
            bool autostartOn = false;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    object val = k != null ? k.GetValue(AUTOSTART_VALUE) : null;
                    autostartOn = val != null;
                    // Self-heal entries from older versions (no --minimized flag)
                    // or after the exe was moved.
                    string want = "\"" + Application.ExecutablePath + "\" --minimized";
                    if (autostartOn && (val as string) != want)
                        k.SetValue(AUTOSTART_VALUE, want);
                }
            }
            catch { }
            chkAutostart.Checked = autostartOn;
            chkAutostart.CheckedChanged += OnAutostartToggled;
            Controls.Add(chkAutostart);

            // --- Anti-AFK card ---
            Card cardAfk = new Card();
            cardAfk.Location = new Point(16, 48);
            cardAfk.Size = new Size(428, 156);
            Controls.Add(cardAfk);

            cardAfk.Controls.Add(Ui.SectionTitle("ANTI-AFK"));

            chkAfk = MakeToggle();
            chkAfk.CheckedChanged += OnAfkToggled;
            cardAfk.Controls.Add(chkAfk);

            cardAfk.Controls.Add(Ui.MutedLabel("Nudge every", 20, 53, 9.75f));

            numInterval = Ui.DarkNumeric(102, 50, 46, 1, 19, 15);
            numInterval.ValueChanged += OnIntervalChanged;
            cardAfk.Controls.Add(numInterval);

            cardAfk.Controls.Add(Ui.MutedLabel("min", 154, 53, 9.75f));

            cmbKeys = Ui.DarkCombo(206, 49, 202);
            cmbKeys.Items.Add("Zoom out + in  (O, I)");
            cmbKeys.Items.Add("Turn camera  (← →)");
            cmbKeys.Items.Add("Jump  (Space)");
            cmbKeys.SelectedIndex = 1;   // default: turn camera (arrow keys)
            cmbKeys.SelectedIndexChanged += delegate
            {
                if (!initializing) Log("Nudge keys set: " + cmbKeys.Text);
                SaveSettings();
            };
            cardAfk.Controls.Add(cmbKeys);

            cardAfk.Controls.Add(Ui.CaptionLabel("NEXT NUDGE IN", 20, 92));

            countdownClock = new Font("Segoe UI", 19f, FontStyle.Bold);
            countdownWord = new Font("Segoe UI", 12f, FontStyle.Bold);

            lblCountdown = new Label();
            lblCountdown.AutoSize = true;
            lblCountdown.Location = new Point(17, 106);
            lblCountdown.Font = countdownClock;
            lblCountdown.ForeColor = Theme.Text;
            lblCountdown.BackColor = Theme.Card;
            cardAfk.Controls.Add(lblCountdown);

            btnNudge = Ui.AccentButton("Nudge now", 292, 104, 116, 36);
            btnNudge.Click += delegate { NudgeAll("manual"); };
            cardAfk.Controls.Add(btnNudge);

            // --- Clients card ---
            Card cardClients = new Card();
            cardClients.Location = new Point(16, 218);
            cardClients.Size = new Size(428, 184);
            Controls.Add(cardClients);

            lblClientsTitle = Ui.SectionTitle("CLIENTS");
            cardClients.Controls.Add(lblClientsTitle);

            cardClients.Controls.Add(Ui.MutedLabel("untick to skip its nudge · Tune for CPU + memory", 110, 18, 8.25f));

            clientsPanel = new ScrollPanel();
            clientsPanel.Location = new Point(20, 44);
            clientsPanel.Size = new Size(388, 104);
            clientsPanel.BackColor = Theme.Card;
            clientsPanel.AutoScroll = true;
            cardClients.Controls.Add(clientsPanel);

            chkAutoGhost = Ui.DarkCheck("Auto-clear ghosts", 18, 153, 8.25f);
            chkAutoGhost.Checked = true;
            chkAutoGhost.CheckedChanged += delegate
            {
                if (!initializing)
                    Log("Auto-clear ghosts " + (chkAutoGhost.Checked
                        ? "on - stuck clients are ended after " + ClientTracker.GHOST_GRACE_SECONDS + "s."
                        : "off."));
                SaveSettings();
            };
            cardClients.Controls.Add(chkAutoGhost);

            lblGhosts = Ui.MutedLabel("", 152, 157, 8.25f);
            lblGhosts.MaximumSize = new Size(132, 0);   // stop short of the button at x=292
            cardClients.Controls.Add(lblGhosts);

            btnZombie = Ui.AccentButton("End background", 292, 151, 116, 26);
            btnZombie.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnZombie.Visible = false;
            btnZombie.Click += delegate { ghostCleaner.KillAll(); };
            cardClients.Controls.Add(btnZombie);

            // --- Performance card ---
            Card cardPerf = new Card();
            cardPerf.Location = new Point(16, 416);
            cardPerf.Size = new Size(428, 100);
            Controls.Add(cardPerf);

            cardPerf.Controls.Add(Ui.SectionTitle("PERFORMANCE"));
            cardPerf.Controls.Add(Ui.MutedLabel("what each Roblox client is allowed to use", 130, 18, 8.25f));

            cardPerf.Controls.Add(Ui.MutedLabel("New clients:", 20, 46, 9f));

            cmbPerfPriority = Ui.DarkCombo(104, 43, 108);
            Ui.FillPriorityCombo(cmbPerfPriority);
            cmbPerfPriority.SelectedIndexChanged += OnPerfDefaultsChanged;
            cardPerf.Controls.Add(cmbPerfPriority);

            cmbPerfCores = Ui.DarkCombo(218, 43, 94);
            Ui.FillCoreCombo(cmbPerfCores);
            cmbPerfCores.SelectedIndexChanged += OnPerfDefaultsChanged;
            cardPerf.Controls.Add(cmbPerfCores);

            chkPerfEco = Ui.DarkCheck("Eco", 320, 45, 9f);
            chkPerfEco.CheckedChanged += OnPerfDefaultsChanged;
            cardPerf.Controls.Add(chkPerfEco);

            chkAutoTrim = Ui.DarkCheck("Auto-trim idle clients every", 20, 74, 8.25f);
            chkAutoTrim.CheckedChanged += delegate
            {
                // Starting the clock now stops a freshly ticked box from trimming
                // on the very next tick.
                perf.ResetAutoTrimClock();
                if (!initializing)
                    Log(chkAutoTrim.Checked
                        ? "Auto-trim on - background clients release idle memory every " + numTrimEvery.Value + " min."
                        : "Auto-trim off.");
                SaveSettings();
            };
            cardPerf.Controls.Add(chkAutoTrim);

            numTrimEvery = Ui.DarkNumeric(188, 70, 44, 1, 120, 10);
            numTrimEvery.ValueChanged += delegate
            {
                if (!initializing && chkAutoTrim.Checked)
                    Log("Auto-trim interval set to " + numTrimEvery.Value + " min.");
                SaveSettings();
            };
            cardPerf.Controls.Add(numTrimEvery);

            cardPerf.Controls.Add(Ui.MutedLabel("min", 236, 74, 8.25f));

            btnTrimAll = Ui.AccentButton("Trim all now", 292, 68, 116, 26);
            btnTrimAll.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnTrimAll.Click += delegate { OnTrimAllClicked(); };
            cardPerf.Controls.Add(btnTrimAll);

            // --- Multi-instance card ---
            Card cardMulti = new Card();
            cardMulti.Location = new Point(16, 530);
            cardMulti.Size = new Size(428, 126);
            Controls.Add(cardMulti);

            cardMulti.Controls.Add(Ui.SectionTitle("MULTI-INSTANCE"));

            chkMulti = MakeToggle();
            chkMulti.CheckedChanged += OnMultiToggled;
            cardMulti.Controls.Add(chkMulti);

            lblDot = new Label();
            lblDot.Text = "●";
            lblDot.AutoSize = true;
            lblDot.Location = new Point(20, 45);
            lblDot.Font = new Font("Segoe UI", 11f);
            lblDot.ForeColor = Theme.Muted;
            lblDot.BackColor = Theme.Card;
            cardMulti.Controls.Add(lblDot);

            lblMultiStatus = new Label();
            lblMultiStatus.AutoSize = true;
            lblMultiStatus.MaximumSize = new Size(362, 0);   // narrowed by UpdateMultiStatus when the button shows
            lblMultiStatus.Location = new Point(42, 48);
            lblMultiStatus.ForeColor = Theme.Text;
            lblMultiStatus.BackColor = Theme.Card;
            cardMulti.Controls.Add(lblMultiStatus);

            btnCloseRbx = Ui.AccentButton("Close all Roblox", 292, 44, 116, 28);
            btnCloseRbx.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnCloseRbx.Visible = false;
            btnCloseRbx.Click += delegate { CloseAllRoblox(); };
            cardMulti.Controls.Add(btnCloseRbx);

            lblUpdating = Ui.MutedLabel("Accounts needing different Roblox versions are handled automatically.",
                20, 92, 8.25f);
            cardMulti.Controls.Add(lblUpdating);

            // --- Activity card ---
            Card cardLog = new Card();
            cardLog.BackColor = Theme.Inset;
            cardLog.Location = new Point(16, 670);
            cardLog.Size = new Size(428, 184);
            Controls.Add(cardLog);

            Label lblAct = new Label();
            lblAct.Text = "ACTIVITY";
            lblAct.AutoSize = true;
            lblAct.Location = new Point(20, 14);
            lblAct.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            lblAct.ForeColor = Theme.Muted;
            lblAct.BackColor = Theme.Inset;
            cardLog.Controls.Add(lblAct);

            LinkLabel lnkCopy = Ui.RowLink("Copy log", 355, 13);
            lnkCopy.Font = new Font("Segoe UI", 8.25f);
            lnkCopy.BackColor = Theme.Inset;
            lnkCopy.Click += delegate { CopyLog(); };
            cardLog.Controls.Add(lnkCopy);

            rtbLog = new RichTextBox();
            rtbLog.Location = new Point(18, 36);
            rtbLog.Size = new Size(392, 134);
            rtbLog.ReadOnly = true;
            rtbLog.BorderStyle = BorderStyle.None;
            rtbLog.BackColor = Theme.Inset;
            rtbLog.ForeColor = Theme.LogFg;
            rtbLog.Font = new Font("Consolas", 8.75f);
            rtbLog.WordWrap = true;
            rtbLog.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtbLog.TabStop = false;
            cardLog.Controls.Add(rtbLog);

            // --- Tray ---
            tray = new NotifyIcon();
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { tray.Icon = SystemIcons.Application; }
            tray.Text = "RobloxKeeper";
            tray.Visible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, delegate { RestoreFromTray(); });
            menu.Items.Add("Nudge now", null, delegate { NudgeAll("tray"); });
            menu.Items.Add("Trim client memory", null, delegate { OnTrimAllClicked(); });
            menu.Items.Add("Exit", null, delegate { Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { RestoreFromTray(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
        }

        // Right-aligned to 408 so it lines up with the accent buttons below it.
        ThemedToggle MakeToggle()
        {
            ThemedToggle c = new ThemedToggle();
            c.Font = new Font("Segoe UI", 9.75f);
            c.Text = "Enabled";
            c.ForeColor = Theme.Text;
            c.BackColor = Theme.Card;
            c.Location = new Point(408 - c.PreferredSize.Width, 13);
            return c;
        }

        // ---------- Client rows ----------

        void RebuildClientRows(List<ClientInfo> clients)
        {
            clientsPanel.SuspendLayout();
            while (clientsPanel.Controls.Count > 0)
            {
                Control c = clientsPanel.Controls[0];
                clientsPanel.Controls.Remove(c);
                c.Dispose();
            }
            shownPids.Clear();
            ramLabels.Clear();

            List<int> stale = new List<int>();
            foreach (int k in nudgePrefs.Keys)
            {
                bool alive = false;
                foreach (ClientInfo c in clients) if (c.Pid == k) { alive = true; break; }
                if (!alive) stale.Add(k);
            }
            foreach (int k in stale) nudgePrefs.Remove(k);

            if (clients.Count == 0)
            {
                Label empty = new Label();
                empty.Text = "No Roblox clients running.";
                empty.AutoSize = true;
                empty.Location = new Point(2, 4);
                empty.ForeColor = Theme.Muted;
                empty.BackColor = Theme.Card;
                clientsPanel.Controls.Add(empty);
            }

            int y = 2;
            int idx = 1;
            foreach (ClientInfo ci in clients)
            {
                if (!nudgePrefs.ContainsKey(ci.Pid)) nudgePrefs[ci.Pid] = true;

                ThemedCheckBox chk = new ThemedCheckBox();
                chk.Location = new Point(2, y);
                chk.Text = "Client " + idx + " · PID " + ci.Pid;
                chk.Checked = nudgePrefs[ci.Pid];
                chk.ForeColor = Theme.Text;
                chk.BackColor = Theme.Card;
                int pid = ci.Pid;
                ThemedCheckBox chkRef = chk;
                chk.CheckedChanged += delegate
                {
                    nudgePrefs[pid] = chkRef.Checked;
                    Log("Client PID " + pid + (chkRef.Checked ? " will be nudged." : " will be left alone."));
                    UpdateAfkTimer(NudgeableClientCount());
                    UpdateCountdown();
                };
                clientsPanel.Controls.Add(chk);

                // Fixed width and right-aligned so the number does not shuffle the
                // links around every time it changes.
                Label ram = new Label();
                ram.AutoSize = false;
                ram.Size = new Size(58, 17);
                ram.Location = new Point(192, y + 3);
                ram.TextAlign = ContentAlignment.MiddleRight;
                ram.Font = new Font("Segoe UI", 8.25f);
                ram.ForeColor = Theme.Muted;
                ram.BackColor = Theme.Card;
                ram.Text = ClientTracker.FormatBytes(ci.WorkingSet);
                clientsPanel.Controls.Add(ram);
                ramLabels[ci.Pid] = ram;

                string label = "Client " + idx;
                int index = idx - 1;
                LinkLabel tune = Ui.RowLink("Tune", 258, y + 3);
                tune.Font = new Font("Segoe UI", 8.25f);
                tune.Click += delegate { OpenTune(pid, index, label); };
                clientsPanel.Controls.Add(tune);

                LinkLabel show = Ui.RowLink("Show", 312, y + 3);
                IntPtr hwnd = ci.Hwnd;
                show.Click += delegate { InputSender.ShowClient(hwnd); };
                clientsPanel.Controls.Add(show);

                shownPids.Add(ci.Pid);
                y += 26;
                idx++;
            }
            clientsPanel.ResumeLayout();
        }

        // The row set only gets rebuilt when clients come and go; memory numbers
        // change constantly, so they are refreshed in place.
        void UpdateRamLabels(List<ClientInfo> clients)
        {
            foreach (ClientInfo ci in clients)
            {
                Label l;
                if (ramLabels.TryGetValue(ci.Pid, out l))
                {
                    string text = ClientTracker.FormatBytes(ci.WorkingSet);
                    if (l.Text != text) l.Text = text;
                }
            }
        }

        // ---------- Performance handlers ----------

        void OnPerfDefaultsChanged(object sender, EventArgs e)
        {
            ClientProfile p = new ClientProfile();
            p.Priority = cmbPerfPriority.SelectedIndex < 0
                ? PerformanceManager.PRIORITY_NORMAL : cmbPerfPriority.SelectedIndex;
            p.Cores = Ui.SelectedCoreCount(cmbPerfCores);
            p.Eco = chkPerfEco.Checked;
            perf.Defaults = p;
            if (!initializing) Log("Clients without their own settings will run at " + p + ".");
            SaveSettings();
        }

        void OpenTune(int pid, int clientIndex, string label)
        {
            using (ClientTuneDialog d = new ClientTuneDialog(pid, clientIndex, label, perf))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (d.ResetToDefault)
                {
                    perf.Forget(pid);
                    Log(label + " (PID " + pid + ") now uses the default profile.");
                }
                else
                {
                    // The next tick notices the mismatch, applies it, and logs the
                    // result - including any part Windows refused.
                    perf.SetOverride(pid, d.Result);
                }
            }
        }

        void OnTrimAllClicked()
        {
            if (lastClients.Count == 0)
            {
                Log("No Roblox clients to trim.");
                return;
            }
            long freed = perf.TrimAll(lastClients, 0);
            perf.ResetAutoTrimClock();
            Log(freed > 0
                ? "Trimmed " + lastClients.Count + " client(s) - released " + ClientTracker.FormatBytes(freed) + "."
                : "Trimmed " + lastClients.Count + " client(s) - nothing idle left to release.");
        }
    }
}
