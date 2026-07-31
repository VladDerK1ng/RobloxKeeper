using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RobloxKeeper
{
    // Layout and the widgets that hang off it. The window is borderless and
    // fixed size, so these coordinates are the layout - there is no layout
    // engine to fall back on. Cards keep a 20px gutter, put their heading close
    // to the top edge, and give each row a fixed height so labels and inputs
    // centre on the same line instead of drifting apart by a pixel or two.
    partial class MainForm
    {
        // Card geometry
        const int CARD_X = 16;
        const int CARD_W = 428;
        const int RIGHT = 408;    // right edge of every button and toggle
        const int BTN_X = 292;
        const int BTN_W = 116;

        // Vertical stack
        const int TITLEBAR_H = 44;
        const int AFK_Y = 54, AFK_H = 156;
        const int CLIENTS_Y = 224, CLIENTS_H = 208;
        const int PERF_Y = 446, PERF_H = 130;
        const int MULTI_Y = 590, MULTI_H = 120;
        const int LOG_Y = 724, LOG_H = 184;

        const int ROW_H = 26;
        const int WELL_W = 248;   // the countdown well, left of the Nudge now button

        // Title bar columns. TITLE_X + TITLE_W must not reach VER_X.
        internal const int TITLE_X = 18, TITLE_W = 138;
        internal const int VER_X = 156, VER_W = 60;
        internal const int AUTOSTART_RIGHT = 356;

        Panel titleBar;

        void BuildUi()
        {
            BuildTitleBar();
            BuildAfkCard();
            BuildClientsCard();
            BuildPerformanceCard();
            BuildMultiCard();
            BuildLogCard();
            BuildTray();
        }

        // ---------- Title bar ----------

        void BuildTitleBar()
        {
            titleBar = new Panel();
            titleBar.Location = new Point(0, 0);
            titleBar.Size = new Size(BASE_WIDTH, TITLEBAR_H);
            titleBar.BackColor = Theme.Bg;
            Controls.Add(titleBar);

            // Width stops at TITLE_W, not wherever looks roomy: these labels are
            // opaque, and one added earlier sits higher in the z-order, so an
            // oversized box here silently paints over the version beside it.
            Label lblTitle = Ui.RowLabel("RobloxKeeper", 18, 0, TITLEBAR_H, TITLE_W, 13f, Theme.Text);
            lblTitle.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            lblTitle.BackColor = Theme.Bg;
            titleBar.Controls.Add(lblTitle);

            // Smaller and dimmer than the surrounding UI text so it reads as a
            // footnote to the title rather than competing with it.
            Label lblVer = Ui.RowLabel("v" + AppInfo.APP_VERSION, VER_X, 0, TITLEBAR_H, VER_W, 8.25f,
                Color.FromArgb(96, 100, 122));
            lblVer.BackColor = Theme.Bg;
            titleBar.Controls.Add(lblVer);

            chkAutostart = Ui.DarkCheck("Start with Windows", 0, 0, 9f);
            chkAutostart.BackColor = Theme.Bg;
            chkAutostart.Location = new Point(AUTOSTART_RIGHT - chkAutostart.PreferredSize.Width,
                                              (TITLEBAR_H - chkAutostart.PreferredSize.Height) / 2);
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
            titleBar.Controls.Add(chkAutostart);

            WindowButton btnMin = new WindowButton(false);
            btnMin.Location = new Point(BASE_WIDTH - 88, 0);
            btnMin.Size = new Size(44, 32);
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            titleBar.Controls.Add(btnMin);

            WindowButton btnClose = new WindowButton(true);
            btnClose.Location = new Point(BASE_WIDTH - 44, 0);
            btnClose.Size = new Size(44, 32);
            btnClose.Click += delegate { Close(); };
            titleBar.Controls.Add(btnClose);

            // Dragging works from the bar itself and from the text on it; the
            // buttons keep their own clicks.
            titleBar.MouseDown += StartWindowDrag;
            lblTitle.MouseDown += StartWindowDrag;
            lblVer.MouseDown += StartWindowDrag;
        }

        void StartWindowDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        // ---------- Anti-AFK ----------

        void BuildAfkCard()
        {
            Card card = new Card();
            card.Location = new Point(CARD_X, AFK_Y);
            card.Size = new Size(CARD_W, AFK_H);
            Controls.Add(card);

            card.Controls.Add(Ui.SectionTitle("ANTI-AFK"));

            chkAfk = MakeToggle();
            chkAfk.CheckedChanged += OnAfkToggled;
            card.Controls.Add(chkAfk);

            const int row = 44;
            card.Controls.Add(Ui.RowLabel("Nudge every", Ui.PAD, row, ROW_H, 86, 9.75f, Theme.Muted));

            numInterval = Ui.DarkNumeric(110, row, 46, 1, 19, 15);
            numInterval.ValueChanged += OnIntervalChanged;
            card.Controls.Add(numInterval);

            card.Controls.Add(Ui.RowLabel("min", 162, row, ROW_H, 30, 9.75f, Theme.Muted));

            cmbKeys = Ui.DarkCombo(196, row, 212);
            cmbKeys.Items.Add("Zoom out + in  (O, I)");
            cmbKeys.Items.Add("Turn camera  (← →)");
            cmbKeys.Items.Add("Jump  (Space)");
            cmbKeys.SelectedIndex = 1;   // default: turn camera (arrow keys)
            cmbKeys.SelectedIndexChanged += delegate
            {
                if (!initializing) Log("Nudge keys set: " + cmbKeys.Text);
                SaveSettings();
            };
            card.Controls.Add(cmbKeys);

            // The countdown sits in its own well so the big numeral reads as a
            // contained readout rather than text floating in empty space.
            InsetPanel well = new InsetPanel();
            well.Location = new Point(Ui.PAD, 82);
            well.Size = new Size(WELL_W, 58);
            card.Controls.Add(well);

            // Both span the full width of the well and centre their own text, so
            // the caption and the readout stay centred no matter how the text
            // below changes - "12:47" and "Waiting for Roblox" are very
            // different widths.
            Label caption = new Label();
            caption.Text = "NEXT NUDGE IN";
            caption.AutoSize = false;
            caption.Location = new Point(0, 8);
            caption.Size = new Size(WELL_W, 14);
            caption.TextAlign = ContentAlignment.MiddleCenter;
            caption.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            caption.ForeColor = Theme.Muted;
            caption.BackColor = Theme.Inset;
            well.Controls.Add(caption);

            countdownClock = new Font("Segoe UI", 19f, FontStyle.Bold);
            countdownWord = new Font("Segoe UI", 12f, FontStyle.Bold);

            lblCountdown = new Label();
            lblCountdown.AutoSize = false;
            lblCountdown.Location = new Point(0, 22);
            lblCountdown.Size = new Size(WELL_W, 30);
            lblCountdown.TextAlign = ContentAlignment.MiddleCenter;
            lblCountdown.Font = countdownClock;
            lblCountdown.ForeColor = Theme.Text;
            lblCountdown.BackColor = Theme.Inset;
            well.Controls.Add(lblCountdown);

            // Centred against the well beside it: well is 82..140, button 87..135.
            btnNudge = Ui.AccentButton("Nudge now", BTN_X, 87, BTN_W, 48);
            btnNudge.Click += delegate { NudgeAll("manual"); };
            card.Controls.Add(btnNudge);
        }

        // ---------- Clients ----------

        void BuildClientsCard()
        {
            Card card = new Card();
            card.Location = new Point(CARD_X, CLIENTS_Y);
            card.Size = new Size(CARD_W, CLIENTS_H);
            Controls.Add(card);

            lblClientsTitle = Ui.SectionTitle("CLIENTS");
            card.Controls.Add(lblClientsTitle);
            card.Controls.Add(Ui.Subtitle("Untick a client to skip its nudge · Tune sets its CPU and memory"));

            clientsPanel = new ScrollPanel();
            clientsPanel.Location = new Point(Ui.PAD, 54);
            clientsPanel.Size = new Size(388, 104);
            clientsPanel.BackColor = Theme.Card;
            clientsPanel.AutoScroll = true;
            card.Controls.Add(clientsPanel);

            const int row = 166;
            chkAutoGhost = Ui.DarkCheck("Auto-clear ghosts", 18, row, 8.25f);
            chkAutoGhost.Checked = true;
            Ui.CenterIn(chkAutoGhost, row, ROW_H);
            chkAutoGhost.CheckedChanged += delegate
            {
                if (!initializing)
                    Log("Auto-clear ghosts " + (chkAutoGhost.Checked
                        ? "on - stuck clients are ended after " + ClientTracker.GHOST_GRACE_SECONDS + "s."
                        : "off."));
                SaveSettings();
            };
            card.Controls.Add(chkAutoGhost);

            // Amber, not muted grey: "stuck" is a state that wants attention, and
            // at muted grey this line was almost invisible against the card.
            lblGhosts = Ui.RowLabel("", 166, row, ROW_H, 120, 8.25f, Theme.Amber);
            card.Controls.Add(lblGhosts);

            btnZombie = Ui.AccentButton("End background", BTN_X, row, BTN_W, ROW_H);
            btnZombie.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnZombie.Visible = false;
            btnZombie.Click += delegate { ghostCleaner.KillAll(); };
            card.Controls.Add(btnZombie);
        }

        // ---------- Performance ----------

        void BuildPerformanceCard()
        {
            Card card = new Card();
            card.Location = new Point(CARD_X, PERF_Y);
            card.Size = new Size(CARD_W, PERF_H);
            Controls.Add(card);

            card.Controls.Add(Ui.SectionTitle("PERFORMANCE"));
            card.Controls.Add(Ui.Subtitle("What each Roblox client is allowed to use"));

            const int row1 = 54;
            card.Controls.Add(Ui.RowLabel("New clients:", Ui.PAD, row1, ROW_H, 80, 9f, Theme.Muted));

            cmbPerfPriority = Ui.DarkCombo(104, row1, 108);
            Ui.FillPriorityCombo(cmbPerfPriority);
            cmbPerfPriority.SelectedIndexChanged += OnPerfDefaultsChanged;
            card.Controls.Add(cmbPerfPriority);

            cmbPerfCores = Ui.DarkCombo(218, row1, 94);
            Ui.FillCoreCombo(cmbPerfCores);
            cmbPerfCores.SelectedIndexChanged += OnPerfDefaultsChanged;
            card.Controls.Add(cmbPerfCores);

            // A wider gap here than between the two dropdowns, so Eco reads as a
            // separate switch rather than a third field in the same group.
            chkPerfEco = Ui.DarkCheck("Eco", 336, row1, 9f);
            Ui.CenterIn(chkPerfEco, row1, ROW_H);
            chkPerfEco.CheckedChanged += OnPerfDefaultsChanged;
            card.Controls.Add(chkPerfEco);

            const int row2 = 88;
            chkAutoTrim = Ui.DarkCheck("Auto-trim idle clients every", Ui.PAD, row2, 8.25f);
            Ui.CenterIn(chkAutoTrim, row2, ROW_H);
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
            card.Controls.Add(chkAutoTrim);

            numTrimEvery = Ui.DarkNumeric(188, row2, 46, 1, 120, 10);
            numTrimEvery.ValueChanged += delegate
            {
                if (!initializing && chkAutoTrim.Checked)
                    Log("Auto-trim interval set to " + numTrimEvery.Value + " min.");
                SaveSettings();
            };
            card.Controls.Add(numTrimEvery);

            card.Controls.Add(Ui.RowLabel("min", 240, row2, ROW_H, 32, 8.25f, Theme.Muted));

            btnTrimAll = Ui.AccentButton("Trim all now", BTN_X, row2, BTN_W, ROW_H);
            btnTrimAll.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnTrimAll.Click += delegate { OnTrimAllClicked(); };
            card.Controls.Add(btnTrimAll);
        }

        // ---------- Multi-instance ----------

        void BuildMultiCard()
        {
            Card card = new Card();
            card.Location = new Point(CARD_X, MULTI_Y);
            card.Size = new Size(CARD_W, MULTI_H);
            Controls.Add(card);

            card.Controls.Add(Ui.SectionTitle("MULTI-INSTANCE"));

            chkMulti = MakeToggle();
            chkMulti.CheckedChanged += OnMultiToggled;
            card.Controls.Add(chkMulti);

            const int row = 44;
            statusDot = new Dot();
            statusDot.Location = new Point(Ui.PAD, row);
            card.Controls.Add(statusDot);

            lblMultiStatus = new Label();
            lblMultiStatus.AutoSize = true;
            lblMultiStatus.MaximumSize = new Size(MultiStatus.WIDTH_ALONE, 0);
            lblMultiStatus.Location = new Point(40, row);
            lblMultiStatus.ForeColor = Theme.Text;
            lblMultiStatus.BackColor = Theme.Card;
            card.Controls.Add(lblMultiStatus);

            btnCloseRbx = Ui.AccentButton("Close all Roblox", BTN_X, row - 1, BTN_W, 28);
            btnCloseRbx.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnCloseRbx.Visible = false;
            btnCloseRbx.Click += delegate { CloseAllRoblox(); };
            card.Controls.Add(btnCloseRbx);

            lblUpdating = Ui.MutedLabel(MultiStatus.HINT_NORMAL, Ui.PAD, 86, 8.25f);
            card.Controls.Add(lblUpdating);
        }

        // ---------- Activity ----------

        void BuildLogCard()
        {
            Card card = new Card();
            card.BackColor = Theme.Inset;
            card.Location = new Point(CARD_X, LOG_Y);
            card.Size = new Size(CARD_W, LOG_H);
            Controls.Add(card);

            Label lblAct = Ui.SectionTitle("ACTIVITY");
            lblAct.BackColor = Theme.Inset;
            card.Controls.Add(lblAct);

            LinkLabel lnkCopy = Ui.RowLink("Copy log", 352, 13);
            lnkCopy.Font = new Font("Segoe UI", 8.25f);
            lnkCopy.BackColor = Theme.Inset;
            lnkCopy.Click += delegate { CopyLog(); };
            card.Controls.Add(lnkCopy);

            rtbLog = new RichTextBox();
            rtbLog.Location = new Point(18, 36);
            rtbLog.Size = new Size(392, 134);
            rtbLog.ReadOnly = true;
            rtbLog.BorderStyle = BorderStyle.None;
            rtbLog.BackColor = Theme.Inset;
            rtbLog.ForeColor = Theme.LogFg;
            rtbLog.Font = new Font("Consolas", 8.75f);
            rtbLog.WordWrap = true;
            // Vertical, not ForcedVertical: the bar only appears once the log
            // actually overflows the box.
            rtbLog.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtbLog.TabStop = false;
            card.Controls.Add(rtbLog);
        }

        // ---------- Tray ----------

        void BuildTray()
        {
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

        // Right-aligned to the same edge as the accent buttons below it, and
        // centred against the section heading.
        ThemedToggle MakeToggle()
        {
            ThemedToggle c = new ThemedToggle();
            c.Font = new Font("Segoe UI", 9.75f);
            c.Text = "Enabled";
            c.ForeColor = Theme.Text;
            c.BackColor = Theme.Card;
            c.Location = new Point(RIGHT - c.PreferredSize.Width, 12);
            return c;
        }

        // ---------- Client rows ----------

        // The list is a table: every column starts at a fixed x so the memory
        // figures right-align under each other and the links sit in a column.
        const int ROW_PITCH = 26;
        const int ROW_INNER = 24;
        const int COL_RAM_X = 186, COL_RAM_W = 62;
        const int COL_TUNE_X = 256, COL_LINK_W = 46;
        const int COL_SHOW_X = 310;

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
                clientsPanel.Controls.Add(
                    Ui.RowLabel("No Roblox clients running.", 2, 2, ROW_INNER, 300, 9.75f, Theme.Muted));
            }

            int y = 2;
            int idx = 1;
            foreach (ClientInfo ci in clients)
            {
                if (!nudgePrefs.ContainsKey(ci.Pid)) nudgePrefs[ci.Pid] = true;

                ThemedCheckBox chk = new ThemedCheckBox();
                chk.Text = "Client " + idx + " · PID " + ci.Pid;
                chk.Checked = nudgePrefs[ci.Pid];
                chk.ForeColor = Theme.Text;
                chk.BackColor = Theme.Card;
                chk.Location = new Point(2, y);
                Ui.CenterIn(chk, y, ROW_INNER);
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

                Label ram = Ui.RowValue(ClientTracker.FormatBytes(ci.WorkingSet),
                    COL_RAM_X, y, ROW_INNER, COL_RAM_W, 8.25f);
                clientsPanel.Controls.Add(ram);
                ramLabels[ci.Pid] = ram;

                string label = "Client " + idx;
                int index = idx - 1;
                LinkLabel tune = Ui.ColumnLink("Tune", COL_TUNE_X, y, ROW_INNER, COL_LINK_W);
                tune.Click += delegate { OpenTune(pid, index, label); };
                clientsPanel.Controls.Add(tune);

                LinkLabel show = Ui.ColumnLink("Show", COL_SHOW_X, y, ROW_INNER, COL_LINK_W);
                IntPtr hwnd = ci.Hwnd;
                show.Click += delegate { InputSender.ShowClient(hwnd); };
                clientsPanel.Controls.Add(show);

                shownPids.Add(ci.Pid);
                y += ROW_PITCH;
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
