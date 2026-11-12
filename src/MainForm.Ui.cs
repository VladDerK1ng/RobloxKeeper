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
        // Card geometry. Cards are a fixed width and lay out their own contents
        // in card-relative coordinates, so which column a card sits in is purely
        // its Location.X - nothing inside a card knows or cares.
        const int CARD_X = 16;
        const int CARD_X2 = 458;   // second column: CARD_X + CARD_W + GUTTER
        const int CARD_W = 428;
        const int GUTTER = 14;     // the gap between cards, horizontally and vertically
        const int RIGHT = 408;    // right edge of every button and toggle
        const int BTN_X = 292;
        const int BTN_W = 116;

        // Two columns.
        //
        // The single stack ran out of screen: at 992px it left 32px of headroom
        // on a 1080p display, and the remaining features needed five more rows.
        // The window was 460px wide on a 1920px screen, so the space was there
        // horizontally the whole time. Splitting the stack roughly halves the
        // height and gives the client list room to show more than three rows.
        //
        // Left column:  what you watch     - anti-AFK, clients
        // Right column: what you configure - performance, multi-instance, log
        const int TITLEBAR_H = 44;
        const int AFK_Y = 54, AFK_H = 224;
        const int CLIENTS_Y = 292, CLIENTS_H = 428;
        const int PERF_Y = 54, PERF_H = 232;
        const int MULTI_Y = 300, MULTI_H = 188;
        // The log takes the slack so both columns end level at COLUMN_BOTTOM;
        // otherwise the right column stopped 34px short and left a gap.
        const int LOG_Y = 502, LOG_H = COLUMN_BOTTOM - LOG_Y;

        // Both columns end here; FULL_HEIGHT adds the bottom margin.
        const int COLUMN_BOTTOM = 720;

        const int ROW_H = 26;
        // Roblox serves different accounts different client versions, so more
        // than one recent version is worth keeping to avoid a re-download.
        const int KEEP_NEWEST_VERSIONS = 2;
        // The multi-instance status row. Its height is whatever the status
        // label wraps to, so the dot and button are centred against that rather
        // than against a fixed number - the text is one line or two depending
        // on state, and a fixed offset is wrong in one of them.
        const int MULTI_ROW = 44;
        const int WELL_W = 248;   // the countdown well, left of the Nudge now button

        // Title bar columns. TITLE_X + TITLE_W must not reach VER_X.
        internal const int TITLE_X = 18, TITLE_W = 138;
        internal const int VER_X = 156, VER_W = 60;
        // Clear of the minimise/close buttons, which sit at BASE_WIDTH - 88.
        internal const int AUTOSTART_RIGHT = BASE_WIDTH - 104;

        Panel titleBar;

        // Hover explanations. The labels stay short enough to fit their rows,
        // and the "what does this actually do" lives here instead of being
        // squeezed into the label or left out entirely.
        readonly ToolTip tips = new ToolTip();

        void Explain(Control c, string text)
        {
            tips.SetToolTip(c, text);
        }

        void BuildUi()
        {
            tips.AutoPopDelay = 20000;
            tips.InitialDelay = 400;
            tips.ReshowDelay = 100;

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
            lblTitle.Font = Ui.F("Segoe UI", 13f, FontStyle.Bold);
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
            chkAutostart.CheckedChanged += OnAutostartToggled;
            Explain(chkAutostart,
                "Opens RobloxKeeper in the tray when Windows starts, so it's ready before you open Roblox. "
                + "No admin needed.");
            titleBar.Controls.Add(chkAutostart);
            // Checked once the window exists (OnHandleCreated): the answer is
            // handed back to it, and before then there is nothing to hand it to.

            // 32px tall in a 44px bar: without centring they sat 6px above the
            // title text beside them.
            const int winBtnY = (TITLEBAR_H - 32) / 2;
            WindowButton btnMin = new WindowButton(false);
            btnMin.Location = new Point(BASE_WIDTH - 88, winBtnY);
            btnMin.Size = new Size(44, 32);
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            titleBar.Controls.Add(btnMin);

            WindowButton btnClose = new WindowButton(true);
            btnClose.Location = new Point(BASE_WIDTH - 44, winBtnY);
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
            cmbKeys.Items.AddRange(NudgeMethod.AllNames());
            cmbKeys.SelectedIndex = NudgeMethod.CAMERA;
            cmbKeys.SelectedIndexChanged += delegate
            {
                if (!initializing) Log("Nudge method: " + cmbKeys.Text);
                UpdateCustomKeyRow();
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
            caption.Font = Ui.F("Segoe UI", 7.5f, FontStyle.Bold);
            caption.ForeColor = Theme.Muted;
            caption.BackColor = Theme.Inset;
            well.Controls.Add(caption);

            countdownClock = Ui.F("Segoe UI", 19f, FontStyle.Bold);
            countdownWord = Ui.F("Segoe UI", 12f, FontStyle.Bold);

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

            // A nudge takes the foreground for about a second per client, which
            // in a competitive game is a lost fight. With this on it waits for a
            // lull instead, and only overrides that when a client is close
            // enough to the idle kick that waiting would cost the account.
            const int idleRow = 148;
            chkIdleOnly = Ui.DarkCheck("Only nudge while I'm away from the keyboard", Ui.PAD, idleRow, 8.25f);
            Ui.CenterIn(chkIdleOnly, idleRow, ROW_H);
            chkIdleOnly.CheckedChanged += delegate
            {
                if (!initializing)
                    Log(chkIdleOnly.Checked
                        ? "Nudges will wait until you're away, unless a Roblox window is about to be kicked."
                        : "Nudges will run on the timer, whatever you're doing.");
                SaveSettings();
            };
            Explain(chkIdleOnly,
                "A nudge briefly puts a Roblox window in front, which can cost you a fight. "
                + "This waits until you pause, and never interrupts a fullscreen game.\r\n\r\n"
                + "If a window is about to be kicked for idling (20 minutes), it nudges anyway and warns you first.");
            card.Controls.Add(chkIdleOnly);

            // Used by the "Custom key" method. Offered as a list of keys known
            // to be safe, plus a capture box for anything else - which still
            // refuses chat, menu, focus and modifier keys, because this fires
            // unattended and a bad key would be sending it into a live game.
            const int keyRow = 182;
            lblCustomKey = Ui.RowLabel("Custom key", Ui.PAD, keyRow, ROW_H, 80, 8.25f, Theme.Muted);
            card.Controls.Add(lblCustomKey);

            cmbCustomKey = Ui.DarkCombo(104, keyRow, 176);
            cmbCustomKey.Items.AddRange(NudgeKeys.SafeKeyNames());
            cmbCustomKey.SelectedIndex = 0;
            cmbCustomKey.SelectedIndexChanged += delegate
            {
                capturedVk = 0;   // the list overrides an earlier capture
                if (!initializing) Log("Custom nudge key set to " + cmbCustomKey.Text + ".");
                SaveSettings();
            };
            card.Controls.Add(cmbCustomKey);

            btnCaptureKey = Ui.AccentButton("Choose a key", BTN_X, keyRow, BTN_W, ROW_H);
            btnCaptureKey.Font = Ui.F("Segoe UI", 8.25f, FontStyle.Bold);
            btnCaptureKey.Click += delegate { CaptureCustomKey(); };
            card.Controls.Add(btnCaptureKey);
        }

        // The custom key only matters for the Custom method; the row stays put
        // either way so the card does not change shape as you switch methods.
        void UpdateCustomKeyRow()
        {
            bool custom = cmbKeys.SelectedIndex == NudgeMethod.CUSTOM;
            lblCustomKey.ForeColor = custom ? Theme.Text : Theme.Muted;
        }

        void CaptureCustomKey()
        {
            using (KeyCaptureDialog d = new KeyCaptureDialog())
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Captured == 0) return;

                string name = NudgeKeys.NameFor(d.Captured);
                if (name != null)
                {
                    // A key the list already offers - select it there, so the
                    // two controls never disagree about what is set.
                    int i = cmbCustomKey.Items.IndexOf(name);
                    if (i >= 0) cmbCustomKey.SelectedIndex = i;
                }
                else
                {
                    capturedVk = d.Captured;
                    Log("Custom nudge key is now " + (char)d.Captured + ".");
                    SaveSettings();
                }

                if (cmbKeys.SelectedIndex != NudgeMethod.CUSTOM)
                    Log("Choose the Custom key method above to use it.");
            }
        }

        // What the Custom method sends: a captured key if there is one, else
        // whatever the list is showing.
        byte CustomNudgeVk()
        {
            if (capturedVk != 0) return capturedVk;
            return NudgeKeys.VkFor(cmbCustomKey.Text);
        }

        // ---------- Clients ----------

        void BuildClientsCard()
        {
            Card card = new Card();
            card.Location = new Point(CARD_X, CLIENTS_Y);
            card.Size = new Size(CARD_W, CLIENTS_H);
            Controls.Add(card);

            lblClientsTitle = Ui.SectionTitle("ROBLOX WINDOWS");
            card.Controls.Add(lblClientsTitle);
            card.Controls.Add(Ui.Subtitle("Untick a window to skip its nudge · Tune sets its limits"));

            clientsPanel = new ScrollPanel();
            clientsPanel.Location = new Point(Ui.PAD, 54);
            clientsPanel.Size = new Size(388, CLIENTS_H - 172);
            clientsPanel.BackColor = Theme.Card;
            clientsPanel.AutoScroll = true;
            card.Controls.Add(clientsPanel);

            const int row = CLIENTS_H - 110;
            chkAutoGhost = Ui.DarkCheck("Auto-close leftovers", 18, row, 8.25f);
            chkAutoGhost.Checked = true;
            Ui.CenterIn(chkAutoGhost, row, ROW_H);
            chkAutoGhost.CheckedChanged += delegate
            {
                if (!initializing)
                    Log("Auto-close leftovers " + (chkAutoGhost.Checked
                        ? "on - stuck Roblox windows close after " + ClientTracker.GHOST_GRACE_SECONDS + "s."
                        : "off."));
                SaveSettings();
            };
            Explain(chkAutoGhost,
                "Sometimes Roblox keeps running, hidden, after you close a window. It uses memory and can "
                + "stop new windows opening, so it's closed for you.\r\n\r\n"
                + "Copies Roblox leaves in the tray are closed too.");
            card.Controls.Add(chkAutoGhost);

            // Amber, not muted grey: "stuck" is a state that wants attention, and
            // at muted grey this line was almost invisible against the card.
            lblGhosts = Ui.RowLabel("", 166, row, ROW_H, 120, 8.25f, Theme.Amber);
            card.Controls.Add(lblGhosts);

            btnZombie = Ui.AccentButton("Close leftovers", BTN_X, row, BTN_W, ROW_H);
            btnZombie.Font = Ui.F("Segoe UI", 8.25f, FontStyle.Bold);
            btnZombie.Visible = false;
            // Ends exactly what the counter beside it says is leaked, nothing
            // more. "Close all Roblox" is the button for ending everything.
            btnZombie.Click += delegate { ghostCleaner.Clear(ghostWatch.Leftovers, ghostWatch); };
            card.Controls.Add(btnZombie);

            // Roblox stores five accounts and makes you sign out to switch.
            // This is the way past that, and it lives beside the client list
            // because launching an account is how a client gets here.
            const int accRow = CLIENTS_H - 76;
            lblAccounts = Ui.RowLabel("", Ui.PAD, accRow, ROW_H, 250, 8.25f, Theme.Muted);
            card.Controls.Add(lblAccounts);

            btnAccounts = Ui.AccentButton("Accounts", BTN_X, accRow, BTN_W, ROW_H);
            btnAccounts.Font = Ui.F("Segoe UI", 8.25f, FontStyle.Bold);
            btnAccounts.Click += delegate { OpenAccounts(); };
            Explain(btnAccounts,
                "Save as many Roblox accounts as you like and open them without signing out. "
                + "Roblox itself only keeps five.");
            card.Controls.Add(btnAccounts);

            // Watching lives here too: what it watches is these clients.
            const int watchRow = CLIENTS_H - 42;
            lblWatchers = Ui.RowLabel("", Ui.PAD, watchRow, ROW_H, 262, 8.25f, Theme.Muted);
            card.Controls.Add(lblWatchers);

            // Shown once there are two setups or more, and then the line
            // above moves over to make room for it.
            cmbSetup = Ui.DarkCombo(Ui.PAD, watchRow, Watching.SETUP_PICKER_W);
            cmbSetup.Visible = false;
            cmbSetup.SelectedIndexChanged += delegate { ChooseSetup(cmbSetup.Text); };
            Explain(cmbSetup,
                "Pick which set of watchers is on, for example one per game. Make sets in Watchers.");
            card.Controls.Add(cmbSetup);

            btnWatchers = Ui.AccentButton("Watchers", BTN_X, watchRow, BTN_W, ROW_H);
            btnWatchers.Font = Ui.F("Segoe UI", 8.25f, FontStyle.Bold);
            btnWatchers.Click += delegate { OpenWatchers(); };
            Explain(btnWatchers,
                "Get a Discord message, pop-up or sound when a word, chat line or picture shows up in a Roblox window. "
                + "It only looks. It never clicks or types.");
            card.Controls.Add(btnWatchers);
        }

        // Opens the account manager. The store is loaded lazily, so a user
        // who never touches accounts never pays for reading or decrypting it.
        void OpenAccounts()
        {
            if (!WebView2Runtime.RuntimeAvailable())
            {
                MessageBox.Show(this,
                    "Accounts needs Microsoft Edge WebView2, and it isn't installed.\r\n\r\n"
                    + "It comes with Windows 11 and with up-to-date Edge. Install either one to use Accounts.",
                    "WebView2 missing", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            EnsureAccounts();
            using (AccountsDialog d = new AccountsDialog(accounts, this))
                d.ShowDialog(this);
            UpdateAccountsLabel();
        }

        void EnsureAccounts()
        {
            if (accounts != null) return;
            accounts = new AccountStore(AccountStore.DefaultPath);
            accounts.Load();
        }

        void UpdateAccountsLabel()
        {
            int n = accounts == null ? -1 : accounts.Accounts.Count;
            string text;
            if (n < 0) text = "Open accounts without signing out";
            else if (n == 0) text = "No accounts saved yet";
            else text = n + " account" + (n == 1 ? "" : "s") + " saved";
            if (lblAccounts.Text != text) lblAccounts.Text = text;
        }

        // ---------- Performance ----------

        void BuildPerformanceCard()
        {
            Card card = new Card();
            card.Location = new Point(CARD_X2, PERF_Y);
            card.Size = new Size(CARD_W, PERF_H);
            Controls.Add(card);

            card.Controls.Add(Ui.SectionTitle("PERFORMANCE"));
            card.Controls.Add(Ui.Subtitle("How much each Roblox window may use"));

            const int row1 = 54;
            card.Controls.Add(Ui.RowLabel("New windows:", Ui.PAD, row1, ROW_H, 84, 9f, Theme.Muted));

            cmbPerfPriority = Ui.DarkCombo(104, row1, 122);       // "Below normal" fits closed
            Ui.FillPriorityCombo(cmbPerfPriority);
            cmbPerfPriority.SelectedIndexChanged += OnPerfDefaultsChanged;
            card.Controls.Add(cmbPerfPriority);

            // No core count beside it: every client gets every core. Locking a
            // client to cores is what froze it - see ClientProfile.
            chkPerfEco = Ui.DarkCheck("Low power", 240, row1, 9f);
            Ui.CenterIn(chkPerfEco, row1, ROW_H);
            chkPerfEco.CheckedChanged += OnPerfDefaultsChanged;
            Explain(chkPerfEco,
                "Uses less CPU and battery. Good for windows left AFK, not the one you're playing.");
            card.Controls.Add(chkPerfEco);

            const int row2 = 88;
            chkAutoTrim = Ui.DarkCheck("Free up memory every", Ui.PAD, row2, 8.25f);
            Ui.CenterIn(chkAutoTrim, row2, ROW_H);
            chkAutoTrim.CheckedChanged += delegate
            {
                // Starting the clock now stops a freshly ticked box from trimming
                // on the very next tick.
                perf.ResetAutoTrimClock();
                if (!initializing)
                    Log(chkAutoTrim.Checked
                        ? "Free up memory is on - idle windows give memory back every " + numTrimEvery.Value + " min."
                        : "Free up memory is off.");
                SaveSettings();
            };
            Explain(chkAutoTrim,
                "Gives back memory a window isn't using, on a timer. You may notice a tiny hitch. "
                + "The window you're looking at is skipped.");
            card.Controls.Add(chkAutoTrim);

            numTrimEvery = Ui.DarkNumeric(188, row2, 46, 1, 120, 10);
            numTrimEvery.ValueChanged += delegate
            {
                if (!initializing && chkAutoTrim.Checked)
                    Log("Free up memory every " + numTrimEvery.Value + " min.");
                SaveSettings();
            };
            card.Controls.Add(numTrimEvery);

            card.Controls.Add(Ui.RowLabel("min", 240, row2, ROW_H, 32, 8.25f, Theme.Muted));

            btnTrimAll = Ui.AccentButton("Free memory now", BTN_X, row2, BTN_W, ROW_H);
            btnTrimAll.Font = Ui.F("Segoe UI", 8.25f, FontStyle.Bold);
            btnTrimAll.Click += delegate { OnTrimAllClicked(); };
            card.Controls.Add(btnTrimAll);

            // "New clients:" above means exactly that - a client that is already
            // playing keeps what it started with. Retuning everything is a
            // deliberate action, not a side effect of changing the default,
            // because the usual reason to change it is to set up the NEXT AFK
            // client without disturbing the one being played.
            const int row3 = 122;
            card.Controls.Add(Ui.RowLabel("Open windows keep their settings",
                Ui.PAD, row3, ROW_H, 250, 8.25f, Theme.Muted));

            btnApplyAll = Ui.AccentButton("Apply to all", BTN_X, row3, BTN_W, ROW_H);
            btnApplyAll.Font = Ui.F("Segoe UI", 8.25f, FontStyle.Bold);
            btnApplyAll.Click += delegate { OnApplyToAllClicked(); };
            Explain(btnApplyAll,
                "The settings above only affect new windows. This applies them to the open ones too.");
            card.Controls.Add(btnApplyAll);

            // Capping a client's FPS is not reachable from outside the process -
            // the only lever is a Roblox config file, and it is global to every
            // client. Dropping priority and switching on EcoQoS for the clients
            // you are not looking at is, and it touches nothing on disk.
            const int row4 = 156;
            chkThrottleBg = Ui.DarkCheck("Slow down windows I'm not using", Ui.PAD, row4, 8.25f);
            Ui.CenterIn(chkThrottleBg, row4, ROW_H);
            chkThrottleBg.CheckedChanged += delegate
            {
                perf.ThrottleBackground = chkThrottleBg.Checked;
                if (!initializing)
                    Log(chkThrottleBg.Checked
                        ? "Windows you're not using will run slower. The one you're using stays at full speed."
                        : "Slowing down other windows is off.");
                SaveSettings();
            };
            Explain(chkThrottleBg,
                "Roblox windows you're not looking at run slower. The one in front stays at full speed, "
                + "and the others speed up when you switch to them.");
            card.Controls.Add(chkThrottleBg);

            btnAfkMode = Ui.AccentButton("AFK mode", BTN_X, row4, BTN_W, ROW_H);
            btnAfkMode.Font = Ui.F("Segoe UI", 8.25f, FontStyle.Bold);
            btnAfkMode.Click += delegate { OnAfkModeClicked(); };
            Explain(btnAfkMode,
                "Slows every Roblox window except the one you're playing. Press it again to undo.");
            card.Controls.Add(btnAfkMode);

            // A real ceiling: Windows holds each background client at or under
            // it, a little at a time, for the client that would otherwise
            // quietly grow to three gigabytes.
            const int row5 = 190;
            chkCeiling = Ui.DarkCheck("Keep memory under", Ui.PAD, row5, 8.25f);
            Ui.CenterIn(chkCeiling, row5, ROW_H);
            chkCeiling.CheckedChanged += delegate
            {
                if (!initializing)
                    Log(chkCeiling.Checked
                        ? "Memory limit on - windows you're not using stay under " + numCeiling.Value + " MB."
                        : "Memory limit off - windows can use what they need again.");
                SaveSettings();
            };
            Explain(chkCeiling,
                "Keeps each window you're not using under this much memory. "
                + "The one you're using is never limited.");
            card.Controls.Add(chkCeiling);

            numCeiling = Ui.DarkNumeric(150, row5, 62, 256, 16384, 2000);
            numCeiling.ValueChanged += delegate
            {
                if (!initializing && chkCeiling.Checked)
                    Log("Memory limit set to " + numCeiling.Value + " MB.");
                SaveSettings();
            };
            card.Controls.Add(numCeiling);

            card.Controls.Add(Ui.RowLabel("MB", 218, row5, ROW_H, 30, 8.25f, Theme.Muted));
        }

        // ---------- Multi-instance ----------

        void BuildMultiCard()
        {
            Card card = new Card();
            card.Location = new Point(CARD_X2, MULTI_Y);
            card.Size = new Size(CARD_W, MULTI_H);
            Controls.Add(card);

            card.Controls.Add(Ui.SectionTitle("MULTI-INSTANCE"));

            chkMulti = MakeToggle();
            chkMulti.CheckedChanged += OnMultiToggled;
            card.Controls.Add(chkMulti);

            statusDot = new Dot();
            statusDot.Location = new Point(Ui.PAD, MULTI_ROW);
            card.Controls.Add(statusDot);

            lblMultiStatus = new Label();
            lblMultiStatus.AutoSize = true;
            lblMultiStatus.MaximumSize = new Size(MultiStatus.WIDTH_ALONE, 0);
            lblMultiStatus.Location = new Point(40, MULTI_ROW);
            lblMultiStatus.ForeColor = Theme.Text;
            lblMultiStatus.BackColor = Theme.Card;
            card.Controls.Add(lblMultiStatus);

            btnCloseRbx = Ui.AccentButton("Close all Roblox", BTN_X, MULTI_ROW, BTN_W, 28);
            btnCloseRbx.Font = Ui.F("Segoe UI", 8.25f, FontStyle.Bold);
            btnCloseRbx.Visible = false;
            btnCloseRbx.Click += delegate { CloseAllRoblox(); };
            card.Controls.Add(btnCloseRbx);

            lblUpdating = Ui.MutedLabel(MultiStatus.HINT_NORMAL, Ui.PAD, 86, 8.25f);
            card.Controls.Add(lblUpdating);

            // Only ever visible when the registration is actually dangling.
            // Amber, because this one silently closes every open client every
            // time Play is pressed, and it will keep doing so until it is fixed.
            const int fixRow = 116;
            lblHandler = Ui.RowLabel("", Ui.PAD, fixRow, ROW_H, BTN_X - Ui.PAD - 8, 8.25f, Theme.Amber);
            lblHandler.Visible = false;
            card.Controls.Add(lblHandler);

            btnFixHandler = Ui.AccentButton("Repair", BTN_X, fixRow, BTN_W, ROW_H);
            btnFixHandler.Font = Ui.F("Segoe UI", 8.25f, FontStyle.Bold);
            btnFixHandler.Visible = false;
            btnFixHandler.Click += delegate { RepairLaunchHandler(); };
            card.Controls.Add(btnFixHandler);
        }

        // ---------- Activity ----------

        void BuildLogCard()
        {
            Card card = new Card();
            card.BackColor = Theme.Inset;
            card.Location = new Point(CARD_X2, LOG_Y);
            card.Size = new Size(CARD_W, LOG_H);
            Controls.Add(card);

            Label lblAct = Ui.SectionTitle("ACTIVITY");
            lblAct.BackColor = Theme.Inset;
            card.Controls.Add(lblAct);

            // Housekeeping lives beside the log rather than taking a row of
            // its own - there is no space left for one, and this is something
            // you do once in a while, not every session.
            LinkLabel lnkClean = Ui.RowLink("Free up disk", 246, Ui.TITLE_Y, Ui.TITLE_H, 8.25f);
            lnkClean.BackColor = Theme.Inset;
            lnkClean.Click += delegate { CleanOldVersions(); };
            card.Controls.Add(lnkClean);
            Explain(lnkClean,
                "Roblox keeps every version it has ever installed. This deletes old ones nothing is using. "
                + "The current version, any in use and the newest two always stay.");

            LinkLabel lnkCopy = Ui.RowLink("Copy log", 352, Ui.TITLE_Y);
            lnkCopy.Font = Ui.F("Segoe UI", 8.25f);
            Ui.CenterIn(lnkCopy, Ui.TITLE_Y, Ui.TITLE_H);
            lnkCopy.BackColor = Theme.Inset;
            lnkCopy.Click += delegate { CopyLog(); };
            card.Controls.Add(lnkCopy);

            rtbLog = new RichTextBox();
            rtbLog.Location = new Point(18, 36);
            rtbLog.Size = new Size(392, LOG_H - 50);
            rtbLog.ReadOnly = true;
            rtbLog.BorderStyle = BorderStyle.None;
            rtbLog.BackColor = Theme.Inset;
            rtbLog.ForeColor = Theme.LogFg;
            rtbLog.Font = Ui.F("Consolas", 8.75f);
            rtbLog.WordWrap = true;
            // Vertical, not ForcedVertical: the bar only appears once the log
            // actually overflows the box.
            rtbLog.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtbLog.TabStop = false;
            card.Controls.Add(rtbLog);
        }

        // Removes installed Roblox versions nothing is using.
        //
        // Deleting the wrong one makes Roblox reinstall, and its installer
        // closes every open client - so the rules in DeletableVersions are
        // deliberately cautious, and the size is shown before anything goes.
        void CleanOldVersions()
        {
            IList<string> spare = RobloxInstall.DeletableVersions(KEEP_NEWEST_VERSIONS, lastClients);
            if (spare.Count == 0)
            {
                Log("Nothing to clean up - every installed Roblox version is in use or recent.");
                return;
            }

            long bytes = 0;
            foreach (string v in spare) bytes += RobloxInstall.VersionSize(v);

            string size = ClientTracker.FormatBytes(bytes);
            if (MessageBox.Show(this,
                    "Delete " + spare.Count + " old Roblox version(s) and free about " + size + "?\r\n\r\n"
                    + "The current version, any in use and the newest two stay. "
                    + "If Roblox needs one again, it downloads it.",
                    "Free up disk", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            int removed = 0;
            long freed = 0;
            foreach (string v in spare)
            {
                long was = RobloxInstall.VersionSize(v);
                if (!RobloxInstall.DeleteVersion(v)) continue;
                removed++;
                freed += was;
            }

            Log(removed > 0
                ? "Removed " + removed + " old Roblox version(s) and freed " + ClientTracker.FormatBytes(freed) + "."
                : "Couldn't remove those versions - Roblox may still be using them.");
        }

        // ---------- Tray ----------

        void BuildTray()
        {
            tray = new NotifyIcon();
            try { tray.Icon = Ui.AppIcon ?? SystemIcons.Application; }
            catch { tray.Icon = SystemIcons.Application; }
            tray.Text = "RobloxKeeper";
            tray.Visible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, delegate { RestoreFromTray(); });
            menu.Items.Add("Nudge now", null, delegate { NudgeAll("tray"); });
            menu.Items.Add("Free up memory", null, delegate { OnTrimAllClicked(); });
            menu.Items.Add("Macros...", null, delegate { OpenMacros(); });
            menu.Items.Add("Autoclicker...", null, delegate { OpenClicker(); });
            // A hunt closes and reopens clients every minute or so, and the
            // app usually sits in the tray while it does - so it can be
            // stopped from here. Only offered while one is running.
            ToolStripItem stopHunt = menu.Items.Add("Stop hunting", null, delegate { StopHunts(true); });
            // The same for following a player, which also moves clients.
            ToolStripItem stopFollow = menu.Items.Add("Stop following", null, delegate { StopFollowing(true); });
            // And for a macro that goes on until stopped, or the autoclicker.
            ToolStripItem stopMacros = menu.Items.Add("Stop macros", null, delegate { StopEverything(); });
            menu.Opening += delegate
            {
                stopMacros.Visible = SessionsRunning > 0;
                stopHunt.Visible = HuntsActive() > 0;
                stopFollow.Visible = follow != null;
                if (follow != null) stopFollow.Text = "Stop following " + follow.Request.Player;
            };
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
            c.Font = Ui.F("Segoe UI", 9.75f);
            c.Text = "Enabled";
            c.ForeColor = Theme.Text;
            c.BackColor = Theme.Card;
            // Centred on the section title beside it rather than nudged to a
            // number that looked about right.
            c.Location = new Point(RIGHT - c.PreferredSize.Width, Ui.TITLE_Y);
            Ui.CenterIn(c, Ui.TITLE_Y, Ui.TITLE_H);
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
                    Ui.RowLabel("No Roblox windows open.", 2, 2, ROW_INNER, 300, 9.75f, Theme.Muted));
            }

            int y = 2;
            int idx = 1;
            foreach (ClientInfo ci in clients)
            {
                if (!nudgePrefs.ContainsKey(ci.Pid)) nudgePrefs[ci.Pid] = true;

                ThemedCheckBox chk = new ThemedCheckBox();
                // Named by account when this app launched it; a client
                // started from the website is still just a number,
                // because there is no honest way to know whose it is.
                string account = clientLabels.NameFor(ci.Pid);
                string label = ClientLabels.RowTitle(account, idx);
                chk.Text = ClientLabels.RowTitle(account, idx) + " · PID " + ci.Pid;
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
                    Log(label + (chkRef.Checked ? " will be nudged." : " will be left alone."));
                    UpdateAfkTimer(NudgeableClientCount());
                    UpdateCountdown();
                };
                clientsPanel.Controls.Add(chk);

                Label ram = Ui.RowValue(ClientTracker.FormatBytes(ci.WorkingSet),
                    COL_RAM_X, y, ROW_INNER, COL_RAM_W, 8.25f);
                clientsPanel.Controls.Add(ram);
                ramLabels[ci.Pid] = ram;

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
            p.Eco = chkPerfEco.Checked;
            perf.Defaults = p;
            if (!initializing) Log("New windows will run at " + p + ".");
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
                    Log(label + " now uses the default settings.");
                }
                else
                {
                    // The next tick notices the mismatch, applies it, and logs the
                    // result - including any part Windows refused.
                    perf.SetOverride(pid, d.Result);
                }
            }
        }

        // The opt-in that changing the default deliberately is not.
        void OnApplyToAllClicked()
        {
            if (lastClients.Count == 0)
            {
                Log("No Roblox windows to apply settings to.");
                return;
            }
            perf.ApplyToAllRunning(lastClients);
            Log("Applied " + perf.Defaults + " to " + lastClients.Count +
                " open window(s). Windows you tuned yourself were left alone.");
        }

        // One switch instead of tuning each client by hand: everything except
        // what you are actually playing gets parked.
        void OnAfkModeClicked()
        {
            if (lastClients.Count == 0) { Log("No Roblox windows open."); return; }

            if (afkModeOn)
            {
                perf.LeaveAfkMode(lastClients);
                afkModeOn = false;
                btnAfkMode.Text = "AFK mode";
                Log("AFK mode off - all windows are back to normal.");
                return;
            }

            int fg = PerformanceManager.ForegroundPid();
            perf.EnterAfkMode(lastClients, fg);
            afkModeOn = true;
            btnAfkMode.Text = "Exit AFK";
            bool anyForeground = false;
            foreach (ClientInfo ci in lastClients) if (ci.Pid == fg) anyForeground = true;
            Log(anyForeground
                ? "AFK mode on - every window slowed except the one in front."
                : "AFK mode on - all " + lastClients.Count + " window(s) slowed; none is in front.");
        }

        void OnTrimAllClicked()
        {
            if (lastClients.Count == 0)
            {
                Log("No Roblox windows open.");
                return;
            }
            long freed = perf.TrimAll(lastClients, 0);
            perf.ResetAutoTrimClock();
            Log(freed > 0
                ? "Freed " + ClientTracker.FormatBytes(freed) + " of memory from " + lastClients.Count + " window(s)."
                : "Checked " + lastClients.Count + " window(s) - no idle memory to free.");
        }
    }
}
