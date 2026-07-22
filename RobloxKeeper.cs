using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.IO;
using System.Text;
using System.Management;
using Microsoft.Win32;

namespace RobloxKeeper
{
    static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern uint RegisterWindowMessage(string lpString);
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int dwProcessId);

        const string APP_MUTEX = "RobloxKeeper_SingleInstance_7C41A9E2";
        static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;
        const int ASFW_ANY = -1;

        public static readonly uint WM_SHOWME = RegisterWindowMessage("RobloxKeeper_ShowExistingWindow");

        static Mutex appMutex;
        public static bool StartMinimized;

        [STAThread]
        static void Main()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
                if (args[i] == "--minimized") StartMinimized = true;

            // Single instance: a second launch surfaces the running window and quits.
            // This runs before any Roblox mutex work, so the live instance is untouched.
            bool createdNew;
            appMutex = new Mutex(true, APP_MUTEX, out createdNew);
            if (!createdNew)
            {
                AllowSetForegroundWindow(ASFW_ANY);
                PostMessage(HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            GC.KeepAlive(appMutex);
        }
    }

    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(24, 24, 37);
        public static readonly Color Card = Color.FromArgb(35, 35, 53);
        public static readonly Color Inset = Color.FromArgb(17, 17, 27);
        public static readonly Color Text = Color.FromArgb(205, 214, 244);
        public static readonly Color Muted = Color.FromArgb(127, 132, 156);
        public static readonly Color Accent = Color.FromArgb(122, 111, 240);
        public static readonly Color AccentHover = Color.FromArgb(148, 137, 250);
        public static readonly Color Green = Color.FromArgb(129, 216, 143);
        public static readonly Color Amber = Color.FromArgb(249, 226, 175);
        public static readonly Color LogFg = Color.FromArgb(147, 154, 183);
    }

    class Card : Panel
    {
        public Card() { BackColor = Theme.Card; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            GraphicsPath p = new GraphicsPath();
            Rectangle r = ClientRectangle;
            int d = 20;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            Region = new Region(p);
        }
    }

    // Focusable panel so the mouse wheel scrolls the client list on hover.
    class ScrollPanel : Panel
    {
        public ScrollPanel()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (CanFocus && !ContainsFocus) Focus();
        }
    }

    // Queue-waits on the Roblox singleton mutex from a dedicated thread, the same
    // way Roblox clients do. The kernel hands over ownership the instant the
    // previous owner releases or dies, so a launching client can never win the
    // race against us. Polling can't guarantee that; a blocking wait can.
    class MutexKeeper
    {
        const string ROBLOX_MUTEX = "ROBLOX_singletonMutex";

        Thread worker;
        ManualResetEvent stop;
        public volatile bool Held;

        public bool Running { get { return worker != null && worker.IsAlive; } }

        public void Start()
        {
            if (Running) return;
            stop = new ManualResetEvent(false);
            Held = false;
            worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Name = "MutexKeeper";
            worker.Start();
        }

        public void Stop()
        {
            if (!Running) { Held = false; return; }
            stop.Set();
            worker.Join(3000);
            worker = null;
            Held = false;
        }

        void Run()
        {
            Mutex m = null;
            try
            {
                bool createdNew;
                m = new Mutex(false, ROBLOX_MUTEX, out createdNew);
                int signaled;
                try { signaled = WaitHandle.WaitAny(new WaitHandle[] { stop, m }); }
                catch (AbandonedMutexException) { signaled = 1; }
                if (signaled == 0) return;   // disabled before acquisition
                Held = true;
                stop.WaitOne();              // own it until disabled
                try { m.ReleaseMutex(); } catch { }
            }
            catch { }
            finally
            {
                Held = false;
                if (m != null) m.Close();
            }
        }
    }

    class MainForm : Form
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion U; }
        struct ClientInfo { public int Pid; public IntPtr Hwnd; public DateTime Start; }

        const string APP_VERSION = "2.5.0";

        const string RUN_KEY = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string AUTOSTART_VALUE = "RobloxKeeper";
        const int GHOST_MAX_AGE_SECONDS = 150;  // a client can sit window-less for a while on a slow launch
        const int PROTECT_PAUSE_SECONDS = 120;  // long enough for a launch that needs the installer

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_SCANCODE = 0x0008;
        const byte VK_LMENU = 0xA4;
        const byte VK_LEFT = 0x25;    // rotate camera left
        const byte VK_RIGHT = 0x27;   // rotate camera right
        const byte VK_I = 0x49;       // zoom in
        const byte VK_O = 0x4F;       // zoom out
        const byte VK_SPACE = 0x20;   // jump
        const int SW_RESTORE = 9;
        const int SW_MINIMIZE = 6;
        const uint WM_CLOSE = 0x0010;

        const string ROBLOX_EVENT = "ROBLOX_singletonEvent";
        const string ROBLOX_PROCESS = "RobloxPlayerBeta";

        readonly MutexKeeper keeper = new MutexKeeper();
        EventWaitHandle singletonEvent;
        bool heldLogged;
        CheckBox chkAfk, chkMulti;
        NumericUpDown numInterval;
        ComboBox cmbKeys, cmbVersion;
        Button btnNudge, btnZombie, btnCloseRbx, btnAllowUpdate;
        CheckBox chkAutoVersion;
        CheckBox chkAutostart, chkAutoGhost, chkProtect;
        Label lblCountdown, lblDot, lblMultiStatus, lblClientsTitle, lblGhosts, lblUpdating;
        ScrollPanel clientsPanel;
        RichTextBox rtbLog;
        System.Windows.Forms.Timer nudgeTimer, uiTimer;
        NotifyIcon tray;
        DateTime nextNudge;
        readonly Dictionary<int, bool> nudgePrefs = new Dictionary<int, bool>();
        readonly List<int> shownPids = new List<int>();
        bool initializing;
        bool startHidden;
        bool installerSeen;
        DateTime lastCloseRequest = DateTime.MinValue;
        DateTime updaterSeenAt = DateTime.MinValue;
        bool updatingShown;
        bool versionConflictLogged;
        string lastRegisteredVersion;
        DateTime protectPausedUntil = DateTime.MinValue;
        bool protectPauseShown;
        bool suppressVersionEvent;
        bool versionFlipSeen;
        readonly List<string> seenClientVersions = new List<string>();
        readonly Dictionary<int, DateTime> knownClients = new Dictionary<int, DateTime>();
        DateTime lastClientOpened = DateTime.MinValue;
        bool clientTrackingReady;

        public MainForm()
        {
            startHidden = Program.StartMinimized;
            Text = "RobloxKeeper";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(460, 826);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();

            nudgeTimer = new System.Windows.Forms.Timer();
            nudgeTimer.Tick += delegate { NudgeAll("timer"); };

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 1000;
            uiTimer.Tick += delegate { OnUiTick(); };
            uiTimer.Start();

            Log("RobloxKeeper v" + APP_VERSION + " started.");

            initializing = true;
            bool afk, multi, autoghost, protect, autoversion;
            int intervalMin, keysIdx;
            LoadSettings(out afk, out intervalMin, out keysIdx, out multi, out autoghost, out protect, out autoversion);
            chkProtect.Checked = protect;
            chkAutoVersion.Checked = autoversion;
            if (intervalMin < 1) intervalMin = 1;
            if (intervalMin > 19) intervalMin = 19;
            numInterval.Value = intervalMin;
            if (keysIdx >= 0 && keysIdx < cmbKeys.Items.Count) cmbKeys.SelectedIndex = keysIdx;
            chkAutoGhost.Checked = autoghost;
            chkAfk.Checked = afk;
            chkMulti.Checked = multi;
            initializing = false;
            UpdateCountdown();
            Log("Settings: Anti-AFK " + (afk ? "on, " + intervalMin + " min, " + cmbKeys.Text : "off") +
                " \u00B7 multi-instance " + (multi ? "on" : "off") + ".");
            CheckLaunchPath();
            FixStaleShortcuts();
            OnUiTick();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            try { DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null;
        }

        // With --minimized (used by autostart) the window starts hidden in the
        // tray instead of appearing on screen. The handle is still created so
        // timers, the tray icon, and the single-instance message all work.
        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                startHidden = false;
                CreateHandle();
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
        }

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
            lblVer.Text = "v" + APP_VERSION;
            lblVer.AutoSize = true;
            lblVer.Location = new Point(152, 19);
            lblVer.Font = new Font("Segoe UI", 8.25f);
            lblVer.ForeColor = Theme.Muted;
            lblVer.BackColor = Theme.Bg;
            Controls.Add(lblVer);

            chkAutostart = new CheckBox();
            chkAutostart.Text = "Start with Windows";
            chkAutostart.AutoSize = true;
            chkAutostart.Location = new Point(302, 17);
            chkAutostart.Font = new Font("Segoe UI", 9f);
            chkAutostart.ForeColor = Theme.Muted;
            chkAutostart.BackColor = Theme.Bg;
            chkAutostart.Cursor = Cursors.Hand;
            chkAutostart.TabStop = false;
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

            cardAfk.Controls.Add(SectionTitle("ANTI-AFK"));

            chkAfk = MakeToggle();
            chkAfk.CheckedChanged += OnAfkToggled;
            cardAfk.Controls.Add(chkAfk);

            cardAfk.Controls.Add(MutedLabel("Nudge every", 20, 53, 9.75f));

            numInterval = new NumericUpDown();
            numInterval.Minimum = 1;
            numInterval.Maximum = 19;
            numInterval.Value = 15;
            numInterval.Width = 46;
            numInterval.Location = new Point(102, 50);
            numInterval.BackColor = Theme.Inset;
            numInterval.ForeColor = Theme.Text;
            numInterval.BorderStyle = BorderStyle.FixedSingle;
            numInterval.TextAlign = HorizontalAlignment.Center;
            numInterval.TabStop = false;
            numInterval.ValueChanged += OnIntervalChanged;
            cardAfk.Controls.Add(numInterval);

            cardAfk.Controls.Add(MutedLabel("min", 154, 53, 9.75f));

            cmbKeys = new ComboBox();
            cmbKeys.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbKeys.FlatStyle = FlatStyle.Flat;
            cmbKeys.BackColor = Theme.Inset;
            cmbKeys.ForeColor = Theme.Text;
            cmbKeys.Location = new Point(206, 49);
            cmbKeys.Width = 202;
            cmbKeys.TabStop = false;
            cmbKeys.DrawMode = DrawMode.OwnerDrawFixed;
            cmbKeys.Items.Add("Zoom out + in  (O, I)");
            cmbKeys.Items.Add("Turn camera  (\u2190 \u2192)");
            cmbKeys.Items.Add("Jump  (Space)");
            cmbKeys.SelectedIndex = 1;   // default: turn camera (arrow keys)
            cmbKeys.DrawItem += DrawComboItem;
            cmbKeys.SelectedIndexChanged += delegate
            {
                if (!initializing) Log("Nudge keys set: " + cmbKeys.Text);
                SaveSettings();
            };
            cardAfk.Controls.Add(cmbKeys);

            cardAfk.Controls.Add(CaptionLabel("NEXT NUDGE IN", 20, 92));

            lblCountdown = new Label();
            lblCountdown.AutoSize = true;
            lblCountdown.Location = new Point(17, 108);
            lblCountdown.Font = new Font("Segoe UI", 19f, FontStyle.Bold);
            lblCountdown.ForeColor = Theme.Text;
            lblCountdown.BackColor = Theme.Card;
            cardAfk.Controls.Add(lblCountdown);

            btnNudge = AccentButton("Nudge now", 292, 104, 116, 36);
            btnNudge.Click += delegate { NudgeAll("manual"); };
            cardAfk.Controls.Add(btnNudge);

            // --- Clients card ---
            Card cardClients = new Card();
            cardClients.Location = new Point(16, 218);
            cardClients.Size = new Size(428, 184);
            Controls.Add(cardClients);

            lblClientsTitle = SectionTitle("CLIENTS");
            cardClients.Controls.Add(lblClientsTitle);

            Label sub = MutedLabel("untick a client to skip its anti-AFK nudge", 110, 18, 8.25f);
            cardClients.Controls.Add(sub);

            clientsPanel = new ScrollPanel();
            clientsPanel.Location = new Point(20, 44);
            clientsPanel.Size = new Size(388, 104);
            clientsPanel.BackColor = Theme.Card;
            clientsPanel.AutoScroll = true;
            cardClients.Controls.Add(clientsPanel);

            chkAutoGhost = new CheckBox();
            chkAutoGhost.Text = "Auto-clear ghosts";
            chkAutoGhost.AutoSize = true;
            chkAutoGhost.Location = new Point(18, 153);
            chkAutoGhost.Font = new Font("Segoe UI", 8.25f);
            chkAutoGhost.ForeColor = Theme.Muted;
            chkAutoGhost.BackColor = Theme.Card;
            chkAutoGhost.Cursor = Cursors.Hand;
            chkAutoGhost.TabStop = false;
            chkAutoGhost.Checked = true;
            chkAutoGhost.CheckedChanged += delegate
            {
                if (!initializing) Log("Auto-clear ghosts " + (chkAutoGhost.Checked ? "on." : "off."));
                SaveSettings();
            };
            cardClients.Controls.Add(chkAutoGhost);

            lblGhosts = MutedLabel("", 152, 157, 8.25f);
            lblGhosts.MaximumSize = new Size(132, 0);   // stop short of the button at x=292
            cardClients.Controls.Add(lblGhosts);

            btnZombie = AccentButton("End background", 292, 151, 116, 26);
            btnZombie.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnZombie.Visible = false;
            btnZombie.Click += delegate { KillZombies(); };
            cardClients.Controls.Add(btnZombie);

            // --- Multi-instance card ---
            Card cardMulti = new Card();
            cardMulti.Location = new Point(16, 416);
            cardMulti.Size = new Size(428, 196);
            Controls.Add(cardMulti);

            cardMulti.Controls.Add(SectionTitle("MULTI-INSTANCE"));

            chkMulti = MakeToggle();
            chkMulti.CheckedChanged += OnMultiToggled;
            cardMulti.Controls.Add(chkMulti);

            lblDot = new Label();
            lblDot.Text = "\u25CF";
            lblDot.AutoSize = true;
            lblDot.Location = new Point(20, 46);
            lblDot.Font = new Font("Segoe UI", 11f);
            lblDot.ForeColor = Theme.Muted;
            lblDot.BackColor = Theme.Card;
            cardMulti.Controls.Add(lblDot);

            lblMultiStatus = new Label();
            lblMultiStatus.AutoSize = true;
            lblMultiStatus.MaximumSize = new Size(240, 0);
            lblMultiStatus.Location = new Point(42, 49);
            lblMultiStatus.ForeColor = Theme.Text;
            lblMultiStatus.BackColor = Theme.Card;
            cardMulti.Controls.Add(lblMultiStatus);

            btnCloseRbx = AccentButton("Close all Roblox", 292, 46, 116, 28);
            btnCloseRbx.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnCloseRbx.Visible = false;
            btnCloseRbx.Click += delegate { CloseAllRoblox(); };
            cardMulti.Controls.Add(btnCloseRbx);

            chkProtect = new CheckBox();
            chkProtect.Text = "Block Roblox updater while clients are open";
            chkProtect.AutoSize = true;
            chkProtect.Location = new Point(18, 84);
            chkProtect.Font = new Font("Segoe UI", 8.75f);
            chkProtect.ForeColor = Theme.Text;
            chkProtect.BackColor = Theme.Card;
            chkProtect.Cursor = Cursors.Hand;
            chkProtect.TabStop = false;
            chkProtect.Checked = true;
            chkProtect.CheckedChanged += delegate
            {
                if (!initializing)
                    Log(chkProtect.Checked
                        ? "Updater blocking ON - Roblox cannot close your open clients to update."
                        : "Updater blocking OFF - Roblox may close all clients when it updates.");
                SaveSettings();
            };
            cardMulti.Controls.Add(chkProtect);

            btnAllowUpdate = AccentButton("Allow update", 292, 80, 116, 26);
            btnAllowUpdate.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnAllowUpdate.Click += delegate
            {
                protectPausedUntil = DateTime.Now.AddSeconds(PROTECT_PAUSE_SECONDS);
                protectPauseShown = true;
                Log("Updater blocking paused for " + (PROTECT_PAUSE_SECONDS / 60) + " minutes - Roblox may now " +
                    "install/update. Open the account that would not launch. Your other clients can close during " +
                    "this window; reopen them once it finishes.");
            };
            cardMulti.Controls.Add(btnAllowUpdate);

            chkAutoVersion = new CheckBox();
            chkAutoVersion.Text = "Auto-pick version for each account";
            chkAutoVersion.AutoSize = true;
            chkAutoVersion.Location = new Point(18, 110);
            chkAutoVersion.Font = new Font("Segoe UI", 8.75f);
            chkAutoVersion.ForeColor = Theme.Text;
            chkAutoVersion.BackColor = Theme.Card;
            chkAutoVersion.Cursor = Cursors.Hand;
            chkAutoVersion.TabStop = false;
            chkAutoVersion.Checked = true;
            chkAutoVersion.CheckedChanged += delegate
            {
                if (!initializing)
                    Log(chkAutoVersion.Checked
                        ? "Auto version ON - the next launch is pointed at the other installed version automatically."
                        : "Auto version OFF - choose the version yourself below.");
                SaveSettings();
            };
            cardMulti.Controls.Add(chkAutoVersion);

            Label lblVerPick = MutedLabel("Next launch", 20, 140, 8.25f);
            cardMulti.Controls.Add(lblVerPick);

            cmbVersion = new ComboBox();
            cmbVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbVersion.FlatStyle = FlatStyle.Flat;
            cmbVersion.BackColor = Theme.Inset;
            cmbVersion.ForeColor = Theme.Text;
            cmbVersion.Location = new Point(96, 136);
            cmbVersion.Width = 312;
            cmbVersion.TabStop = false;
            cmbVersion.SelectedIndexChanged += delegate { OnVersionPicked(); };
            cardMulti.Controls.Add(cmbVersion);

            lblUpdating = MutedLabel("One account can't join two games at once \u2014 use separate accounts.",
                20, 168, 8.25f);
            cardMulti.Controls.Add(lblUpdating);

            // --- Activity card ---
            Card cardLog = new Card();
            cardLog.BackColor = Theme.Inset;
            cardLog.Location = new Point(16, 626);
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

            LinkLabel lnkCopy = new LinkLabel();
            lnkCopy.Text = "Copy log";
            lnkCopy.AutoSize = true;
            lnkCopy.Location = new Point(355, 13);
            lnkCopy.Font = new Font("Segoe UI", 8.25f);
            lnkCopy.LinkColor = Theme.Accent;
            lnkCopy.ActiveLinkColor = Theme.AccentHover;
            lnkCopy.LinkBehavior = LinkBehavior.HoverUnderline;
            lnkCopy.BackColor = Theme.Inset;
            lnkCopy.TabStop = false;
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
            menu.Items.Add("Exit", null, delegate { Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { RestoreFromTray(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
        }

        void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool inEdit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color bg = (!inEdit && selected) ? Theme.Accent : Theme.Inset;
            Color fg = (!inEdit && selected) ? Color.White : Theme.Text;
            using (SolidBrush b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);
            Rectangle textRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, cmbKeys.Items[e.Index].ToString(), cmbKeys.Font, textRect, fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        Label SectionTitle(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(20, 16);
            l.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            l.ForeColor = Theme.Muted;
            l.BackColor = Theme.Card;
            return l;
        }

        Label CaptionLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            l.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            l.ForeColor = Theme.Muted;
            l.BackColor = Theme.Card;
            return l;
        }

        Label MutedLabel(string text, int x, int y, float size)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.MaximumSize = new Size(388, 0);
            l.Location = new Point(x, y);
            l.Font = new Font("Segoe UI", size);
            l.ForeColor = Theme.Muted;
            l.BackColor = Theme.Card;
            return l;
        }

        CheckBox MakeToggle()
        {
            CheckBox c = new CheckBox();
            c.Text = "Enabled";
            c.AutoSize = true;
            c.Location = new Point(334, 14);
            c.ForeColor = Theme.Text;
            c.BackColor = Theme.Card;
            c.Cursor = Cursors.Hand;
            c.TabStop = false;
            return c;
        }

        Button AccentButton(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Theme.Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            b.Cursor = Cursors.Hand;
            b.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold);
            b.TabStop = false;
            return b;
        }

        // ---------- Client tracking ----------

        List<ClientInfo> GetClients(out int ghosts)
        {
            List<ClientInfo> list = new List<ClientInfo>();
            ghosts = 0;
            Process[] procs = Process.GetProcessesByName(ROBLOX_PROCESS);
            foreach (Process p in procs)
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        ClientInfo ci = new ClientInfo();
                        ci.Pid = p.Id;
                        ci.Hwnd = p.MainWindowHandle;
                        try { ci.Start = p.StartTime; } catch { ci.Start = DateTime.MinValue; }
                        list.Add(ci);
                    }
                    else ghosts++;
                }
                finally { p.Dispose(); }
            }
            list.Sort(delegate(ClientInfo a, ClientInfo b) { return a.Start.CompareTo(b.Start); });
            return list;
        }

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

                CheckBox chk = new CheckBox();
                chk.AutoSize = true;
                chk.Location = new Point(2, y);
                chk.Text = "Client " + idx + "   \u00B7   PID " + ci.Pid;
                chk.Checked = nudgePrefs[ci.Pid];
                chk.ForeColor = Theme.Text;
                chk.BackColor = Theme.Card;
                chk.Cursor = Cursors.Hand;
                chk.TabStop = false;
                int pid = ci.Pid;
                CheckBox chkRef = chk;
                chk.CheckedChanged += delegate
                {
                    nudgePrefs[pid] = chkRef.Checked;
                    Log("Client PID " + pid + (chkRef.Checked ? " will be nudged." : " will be left alone."));
                };
                clientsPanel.Controls.Add(chk);

                LinkLabel lnk = new LinkLabel();
                lnk.Text = "Show";
                lnk.AutoSize = true;
                lnk.Location = new Point(310, y + 3);
                lnk.LinkColor = Theme.Accent;
                lnk.ActiveLinkColor = Theme.AccentHover;
                lnk.LinkBehavior = LinkBehavior.HoverUnderline;
                lnk.BackColor = Theme.Card;
                lnk.TabStop = false;
                IntPtr hwnd = ci.Hwnd;
                lnk.Click += delegate { ShowClient(hwnd); };
                clientsPanel.Controls.Add(lnk);

                shownPids.Add(ci.Pid);
                y += 26;
                idx++;
            }
            clientsPanel.ResumeLayout();
        }

        void ShowClient(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) { ShowWindow(hwnd, SW_RESTORE); Thread.Sleep(150); }
            FocusWindow(hwnd);
        }

        // ---------- Anti-AFK ----------

        void OnAfkToggled(object sender, EventArgs e)
        {
            if (chkAfk.Checked)
            {
                nudgeTimer.Interval = (int)numInterval.Value * 60000;
                nextNudge = DateTime.Now.AddMilliseconds(nudgeTimer.Interval);
                nudgeTimer.Start();
                if (!initializing) Log("Anti-AFK enabled \u2014 interval " + numInterval.Value + " min.");
            }
            else
            {
                nudgeTimer.Stop();
                if (!initializing) Log("Anti-AFK disabled.");
            }
            UpdateCountdown();
            SaveSettings();
        }

        void OnIntervalChanged(object sender, EventArgs e)
        {
            if (!chkAfk.Checked) return;
            nudgeTimer.Stop();
            nudgeTimer.Interval = (int)numInterval.Value * 60000;
            nextNudge = DateTime.Now.AddMilliseconds(nudgeTimer.Interval);
            nudgeTimer.Start();
            if (!initializing) Log("Interval set to " + numInterval.Value + " min.");
            SaveSettings();
        }

        void NudgeAll(string reason)
        {
            nextNudge = DateTime.Now.AddMinutes((double)numInterval.Value);
            int ghosts;
            List<ClientInfo> clients = GetClients(out ghosts);
            int count = 0, skipped = 0;
            IntPtr previous = GetForegroundWindow();

            foreach (ClientInfo ci in clients)
            {
                bool wanted;
                if (!nudgePrefs.TryGetValue(ci.Pid, out wanted)) wanted = true;
                if (!wanted) { skipped++; continue; }

                bool wasMinimized = IsIconic(ci.Hwnd);
                if (wasMinimized) { ShowWindow(ci.Hwnd, SW_RESTORE); Thread.Sleep(300); }

                FocusWindow(ci.Hwnd);
                Thread.Sleep(250);
                SendNudgeKeys();
                Thread.Sleep(150);

                if (wasMinimized) ShowWindow(ci.Hwnd, SW_MINIMIZE);
                count++;
            }

            if (count > 0 && previous != IntPtr.Zero && previous != Handle)
                FocusWindow(previous);

            if (clients.Count == 0)
                Log("No Roblox clients found (" + reason + ").");
            else
                Log("Nudged " + count + " client(s)" + (skipped > 0 ? ", skipped " + skipped : "") + " (" + reason + ").");
        }

        void SendNudgeKeys()
        {
            switch (cmbKeys.SelectedIndex)
            {
                case 1: // turn camera left, then back right
                    TapKey(VK_LEFT, 180);
                    Thread.Sleep(250);
                    TapKey(VK_RIGHT, 180);
                    break;
                case 2: // jump
                    TapKey(VK_SPACE, 90);
                    break;
                default: // zoom out one notch, zoom back in
                    TapKey(VK_O, 90);
                    Thread.Sleep(350);
                    TapKey(VK_I, 90);
                    break;
            }
        }

        void FocusWindow(IntPtr hwnd)
        {
            // A quick Alt tap makes Windows allow the foreground switch.
            SendVk(VK_LMENU, true);
            SendVk(VK_LMENU, false);
            SetForegroundWindow(hwnd);
            Thread.Sleep(120);
            if (GetForegroundWindow() != hwnd)
            {
                uint pid;
                uint target = GetWindowThreadProcessId(hwnd, out pid);
                uint mine = GetCurrentThreadId();
                AttachThreadInput(mine, target, true);
                SetForegroundWindow(hwnd);
                AttachThreadInput(mine, target, false);
                Thread.Sleep(120);
            }
        }

        void TapKey(byte vk, int holdMs)
        {
            SendScan(vk, true);
            Thread.Sleep(holdMs);
            SendScan(vk, false);
        }

        // Scan-code input: what games reading raw/hardware input actually listen for.
        // Arrow keys are extended keys and need the E0 flag, or they read as numpad.
        void SendScan(byte vk, bool down)
        {
            INPUT[] inp = new INPUT[1];
            inp[0].type = INPUT_KEYBOARD;
            inp[0].U.ki.wVk = 0;
            inp[0].U.ki.wScan = (ushort)MapVirtualKey(vk, 0);
            uint flags = KEYEVENTF_SCANCODE;
            if (vk >= 0x21 && vk <= 0x2E) flags |= KEYEVENTF_EXTENDEDKEY;
            if (!down) flags |= KEYEVENTF_KEYUP;
            inp[0].U.ki.dwFlags = flags;
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        void SendVk(byte vk, bool down)
        {
            INPUT[] inp = new INPUT[1];
            inp[0].type = INPUT_KEYBOARD;
            inp[0].U.ki.wVk = vk;
            inp[0].U.ki.dwFlags = down ? 0u : KEYEVENTF_KEYUP;
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        void UpdateCountdown()
        {
            if (!chkAfk.Checked)
            {
                lblCountdown.Text = "Disabled";
                return;
            }
            TimeSpan left = nextNudge - DateTime.Now;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            lblCountdown.Text = ((int)left.TotalMinutes).ToString() + ":" + left.Seconds.ToString("00");
        }

        // ---------- Multi-instance ----------

        void OnMultiToggled(object sender, EventArgs e)
        {
            if (chkMulti.Checked)
            {
                StartMulti();
                if (!initializing) Log("Multi-instance enabled \u2014 queued for the singleton mutex.");
            }
            else
            {
                StopMulti();
                if (!initializing) Log("Multi-instance disabled \u2014 mutex released.");
            }
            UpdateMultiStatus();
            SaveSettings();
        }

        void StartMulti()
        {
            keeper.Start();
            if (singletonEvent == null)
            {
                try
                {
                    bool createdNew;
                    singletonEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ROBLOX_EVENT, out createdNew);
                }
                catch { singletonEvent = null; }
            }
        }

        void StopMulti()
        {
            keeper.Stop();
            heldLogged = false;
            if (singletonEvent != null)
            {
                singletonEvent.Close();
                singletonEvent = null;
            }
        }

        // The duplicate-install ping-pong can only be broken by removing the
        // competing copies. Roblox re-downloads a clean version on next launch,
        // so this is recoverable - but it closes running games, and third-party
        // launchers must be uninstalled by the user, so both are spelled out first.
        void RepairInstall()
        {
            string versionsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions");

            string[] all;
            try { all = Directory.Exists(versionsRoot) ? Directory.GetDirectories(versionsRoot) : new string[0]; }
            catch (Exception ex) { Log("Repair aborted - can't read Versions folder: " + ex.Message); return; }

            // Accounts can legitimately sit on different Roblox release channels,
            // and then BOTH versions are needed - deleting one forces a reinstall
            // every time the user switches account. Only strip duplicates when the
            // user confirms they are leftovers.
            if (all.Length == 2)
            {
                DialogResult keepBoth = MessageBox.Show(this,
                    "Two Roblox versions are installed.\r\n\r\n" +
                    "That is normal if your accounts are on different Roblox release channels " +
                    "(a premium/main account often is) - each account needs its own version, and " +
                    "deleting one makes Roblox reinstall it every time you switch.\r\n\r\n" +
                    "Do your accounts need DIFFERENT versions?\r\n\r\n" +
                    "Yes  = keep both versions (recommended if two accounts fail to run together)\r\n" +
                    "No   = remove the extra copy",
                    "Keep both Roblox versions?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (keepBoth == DialogResult.Cancel) { Log("Repair cancelled."); return; }
                if (keepBoth == DialogResult.Yes)
                {
                    Log("Repair: keeping BOTH versions - accounts on different channels each need their own. " +
                        "Leave \"Block Roblox updater\" ticked so neither install can close your clients.");
                    RetargetShortcuts(LaunchPathVersion(), versionsRoot);
                    return;
                }
            }

            // Keep the version Roblox is currently registered to (fall back to the
            // newest client), and remove every competing copy. One version left
            // means there is nothing for the installer loop to fight over.
            string keep = LaunchPathVersion();
            if (keep == "?" || !Directory.Exists(Path.Combine(versionsRoot, keep)))
            {
                keep = null;
                DateTime newest = DateTime.MinValue;
                foreach (string d in all)
                {
                    string exe = Path.Combine(d, "RobloxPlayerBeta.exe");
                    if (!File.Exists(exe)) continue;
                    DateTime t = File.GetLastWriteTime(exe);
                    if (t > newest) { newest = t; keep = Path.GetFileName(d); }
                }
            }

            List<string> dirList = new List<string>();
            foreach (string d in all)
                if (keep == null || !string.Equals(Path.GetFileName(d), keep, StringComparison.OrdinalIgnoreCase))
                    dirList.Add(d);
            string[] dirs = dirList.ToArray();

            if (dirs.Length == 0)
            {
                MessageBox.Show(this,
                    "Only one Roblox version is installed (" + (keep ?? "none") + "), so there is nothing " +
                    "for duplicate installs to fight over.\n\nIf clients still close on their own, the cause is a " +
                    "third-party launcher reinstalling its own copy: " + ThirdPartyLaunchers() +
                    "\nUninstall the ones you don't use, then launch Roblox from one source only.",
                    "Nothing to repair", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log("Repair: only one version folder present (" + (keep ?? "none") + ") - nothing to remove.");
                return;
            }

            int ghosts;
            List<ClientInfo> clients = GetClients(out ghosts);
            string tp = ThirdPartyLaunchers();

            StringBuilder msg = new StringBuilder();
            msg.AppendLine("This repairs a Roblox install whose copies keep fighting and closing your clients.");
            msg.AppendLine();
            msg.AppendLine("It will:");
            msg.AppendLine("  - close " + clients.Count + " running client(s) and " + ghosts + " background process(es)");
            msg.AppendLine("  - KEEP the version Roblox currently uses:  " + (keep ?? "(none)"));
            msg.AppendLine("  - DELETE " + dirs.Length + " leftover version folder(s) from:");
            msg.AppendLine("    " + versionsRoot);
            msg.AppendLine();
            msg.AppendLine("Your working Roblox stays installed - only the duplicate copies go, so there is");
            msg.AppendLine("nothing left to fight over. You WILL have to rejoin your games.");
            msg.AppendLine();
            if (HasActiveThirdPartyLauncher())
            {
                msg.AppendLine("IMPORTANT - third-party launchers found: " + tp);
                msg.AppendLine("These install their OWN Roblox version and will recreate the conflict.");
                msg.AppendLine("Uninstall the ones you don't use (Windows Settings > Apps) BEFORE relaunching,");
                msg.AppendLine("then always launch Roblox from one source only.");
                msg.AppendLine();
            }
            msg.Append("Continue?");

            if (MessageBox.Show(this, msg.ToString(), "Repair Roblox install",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                Log("Repair cancelled.");
                return;
            }

            Log("Repair started - keeping " + (keep ?? "(none)") + ", removing " + dirs.Length + " duplicate version folder(s).");

            foreach (ClientInfo ci in clients)
                PostMessage(ci.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(2500);

            foreach (Process p in Process.GetProcessesByName(ROBLOX_PROCESS))
            {
                try { p.Kill(); } catch { }
                p.Dispose();
            }
            foreach (string helper in new string[] { "RobloxPlayerInstaller", "RobloxPlayerLauncher", "RobloxCrashHandler" })
            {
                foreach (Process p in Process.GetProcessesByName(helper))
                {
                    try { p.Kill(); } catch { }
                    p.Dispose();
                }
            }
            Thread.Sleep(1500);

            int removed = 0, failed = 0;
            foreach (string d in dirs)
            {
                try { Directory.Delete(d, true); removed++; }
                catch (Exception ex)
                {
                    failed++;
                    Log("Could not remove " + Path.GetFileName(d) + ": " + ex.Message);
                }
            }

            int retargeted = RetargetShortcuts(keep, versionsRoot);
            if (retargeted > 0)
                Log("Fixed " + retargeted + " stale Roblox shortcut(s) that still pointed at a removed version - " +
                    "those were launching the wrong client and triggering the repair loop.");

            Log("Repair finished - removed " + removed + " duplicate version folder(s)" +
                (failed > 0 ? ", " + failed + " could not be removed (try again once everything Roblox is closed)" : "") +
                ". Kept " + (keep ?? "(none)") + ".");
            if (HasActiveThirdPartyLauncher())
            {
                Log("IMPORTANT: a third-party launcher is still installed (" + tp +
                    ") - it will reinstall its own copy and the conflict returns unless you remove it.");
                OfferLauncherUninstall();
            }
            else
                Log("No active third-party launcher found. Launch Roblox from one source only from now on.");

            versionConflictLogged = false;
            lastRegisteredVersion = null;
        }

        void CloseAllRoblox()
        {
            int ghosts;
            List<ClientInfo> clients = GetClients(out ghosts);
            if (clients.Count == 0 && ghosts == 0)
            {
                Log("No Roblox processes to close.");
                return;
            }
            string msg = "Close " + clients.Count + " Roblox client(s)" +
                (ghosts > 0 ? " and end " + ghosts + " background process(es)" : "") +
                "?\n\nYou'll need to rejoin your games, but multi-instance activates the moment they're gone.";
            if (MessageBox.Show(this, msg, "RobloxKeeper", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            foreach (ClientInfo ci in clients)
                PostMessage(ci.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (ghosts > 0) KillZombies();
            lastCloseRequest = DateTime.Now;
            Log("Close request sent to " + clients.Count + " client(s) \u2014 taking the mutex as soon as they exit.");
        }

        void UpdateMultiStatus()
        {
            if (!chkMulti.Checked)
            {
                lblDot.ForeColor = Theme.Muted;
                lblMultiStatus.Text = "Disabled \u2014 a new Roblox client will replace the running one.";
            }
            else if (keeper.Held)
            {
                lblDot.ForeColor = Theme.Green;
                lblMultiStatus.Text = "Active \u2014 singleton mutex held. New clients stay open.";
            }
            else
            {
                lblDot.ForeColor = Theme.Amber;
                lblMultiStatus.Text = "Waiting \u2014 a Roblox client owns the mutex. Close every client and I take over instantly.";
            }
        }

        void OnAutostartToggled(object sender, EventArgs e)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (chkAutostart.Checked)
                    {
                        k.SetValue(AUTOSTART_VALUE, "\"" + Application.ExecutablePath + "\" --minimized");
                        Log("Autostart enabled \u2014 starts minimized to the tray with Windows.");
                    }
                    else
                    {
                        k.DeleteValue(AUTOSTART_VALUE, false);
                        Log("Autostart disabled.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Autostart change failed: " + ex.Message);
            }
        }

        void AutoClearGhosts()
        {
            Process[] procs = Process.GetProcessesByName(ROBLOX_PROCESS);
            int killed = 0;
            foreach (Process p in procs)
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero &&
                        (DateTime.Now - p.StartTime).TotalSeconds > GHOST_MAX_AGE_SECONDS)
                    {
                        p.Kill();
                        killed++;
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            if (killed > 0)
                Log("Auto-cleared " + killed + " stuck background Roblox process(es).");
        }

        void KillZombies()
        {
            Process[] procs = Process.GetProcessesByName(ROBLOX_PROCESS);
            int killed = 0;
            foreach (Process p in procs)
            {
                if (p.MainWindowHandle == IntPtr.Zero)
                {
                    try { p.Kill(); killed++; } catch { }
                }
                p.Dispose();
            }
            Log("Terminated " + killed + " background Roblox process(es).");
        }

        // ---------- Housekeeping ----------

        void OnUiTick()
        {
            UpdateCountdown();
            if (chkMulti.Checked && keeper.Held && !heldLogged)
            {
                heldLogged = true;
                Log("Multi-instance active \u2014 singleton mutex acquired.");
            }
            UpdateMultiStatus();
            btnCloseRbx.Visible = chkMulti.Checked && !keeper.Held;
            RefreshVersionList();

            int ghosts;
            List<ClientInfo> clients = GetClients(out ghosts);
            lblClientsTitle.Text = "CLIENTS \u00B7 " + clients.Count;
            lblGhosts.Text = ghosts > 0 ? "+" + ghosts + " stuck" : "";
            btnZombie.Visible = ghosts > 0;

            // Only clear ghosts when one is actually squatting on the mutex.
            // While we hold it, a window-less Roblox process is harmless - and is
            // usually a client still starting up, which must not be killed.
            if (chkAutoGhost.Checked && ghosts > 0 && chkMulti.Checked && !keeper.Held)
                AutoClearGhosts();

            // When Roblox installs an update, ITS OWN installer terminates every
            // running client (old version) - no tool can prevent that. Surface it
            // so a mass client close is explained instead of looking like a bug.
            // The install rewriting its own registration is the ping-pong itself.
            // Catch the flip as it happens - comparing versions only at client
            // open misses it, because the flip lands moments later.
            string regNow = LaunchPathVersion();
            if (lastRegisteredVersion == null) lastRegisteredVersion = regNow;
            else if (regNow != lastRegisteredVersion)
            {
                Log("ROBLOX RE-REGISTERED ITSELF: " + lastRegisteredVersion + " -> " + regNow +
                    ". Two installs are taking turns claiming Roblox; each hand-over runs an " +
                    "installer that closes every open client. This repeats forever until one is removed. " +
                    "Launchers present: " + ThirdPartyLaunchers());
                versionFlipSeen = true;
                lastRegisteredVersion = regNow;
            }

            // Roblox serves different client versions to different accounts, so
            // switching between accounts makes it reinstall - and its installer
            // closes every running client to replace files. Stopping the installer
            // WHILE clients are open keeps the session alive; when nothing is
            // running it is left alone, so Roblox still updates normally.
            bool paused = DateTime.Now < protectPausedUntil;
            if (paused != protectPauseShown)
            {
                protectPauseShown = paused;
                if (!paused) Log("Updater blocking resumed - your open clients are protected again.");
            }
            if (chkProtect != null && chkProtect.Checked && !paused && clients.Count > 0)
            {
                Process[] ups = Process.GetProcessesByName("RobloxPlayerInstaller");
                foreach (Process up in ups)
                {
                    string where = PathOf(up);
                    try
                    {
                        up.Kill();
                        Log("BLOCKED Roblox updater (" + VersionFolderOf(where) + ") - it was about to close your " +
                            clients.Count + " open client(s). Your session is safe. Roblox will update normally once " +
                            "you close all clients.");
                    }
                    catch (Exception ex) { Log("Could not block the Roblox updater: " + ex.Message); }
                    finally { up.Dispose(); }
                }
            }

            bool installerRunning = AnyProcess("RobloxPlayerInstaller") || AnyProcess("RobloxPlayerLauncher");
            if (installerRunning && !installerSeen)
            {
                updaterSeenAt = DateTime.Now;
                Log("Roblox helper running: " + DescribeHelpers() +
                    " - it can close open clients regardless of the mutex." +
                    (UsesLegacyBootstrapper()
                        ? " Your install uses it on EVERY launch - reinstall Roblox from roblox.com to stop this."
                        : " Reopen your clients when it finishes."));
                try
                {
                    tray.BalloonTipTitle = "Roblox is updating";
                    tray.BalloonTipText = "Roblox's own updater closes every open client once. " +
                        "Wait for it to finish, then reopen your clients - multi-instance keeps working.";
                    tray.BalloonTipIcon = ToolTipIcon.Warning;
                    tray.ShowBalloonTip(10000);
                }
                catch { }
            }
            installerSeen = installerRunning;

            // For a minute after the updater appears, this line turns into an
            // on-screen warning so the cause is visible, not only in the log.
            bool updating = installerRunning || (DateTime.Now - updaterSeenAt).TotalSeconds < 60;
            if (updating != updatingShown)
            {
                updatingShown = updating;
                lblUpdating.Text = updating
                    ? "Roblox is UPDATING - it closes every open client once. Reopen them after."
                    : "One account can't join two games at once \u2014 use separate accounts.";
                lblUpdating.ForeColor = updating ? Theme.Amber : Theme.Muted;
                lblUpdating.Font = new Font("Segoe UI", 8.25f, updating ? FontStyle.Bold : FontStyle.Regular);
            }

            TrackClientLifecycle(clients, installerRunning);

            bool changed = clients.Count != shownPids.Count;
            if (!changed)
            {
                for (int i = 0; i < clients.Count; i++)
                    if (clients[i].Pid != shownPids[i]) { changed = true; break; }
            }
            if (changed) RebuildClientRows(clients);
        }

        // Records why each client opened or vanished. When a client dies the log
        // states the probable cause, so the Activity text alone is enough to
        // diagnose a "my client keeps closing" report from another machine.
        void TrackClientLifecycle(List<ClientInfo> clients, bool installerRunning)
        {
            bool mutexHeld = keeper.Held;

            foreach (ClientInfo ci in clients)
            {
                if (knownClients.ContainsKey(ci.Pid)) continue;
                knownClients[ci.Pid] = DateTime.Now;
                if (!clientTrackingReady) continue;   // don't narrate clients already open at startup
                lastClientOpened = DateTime.Now;
                string clientVer = VersionOfPid(ci.Pid);
                Log("Client PID " + ci.Pid + " [" + clientVer + "] opened, launched by " + ParentOf(ci.Pid) +
                    " \u2014 mutex " +
                    (mutexHeld ? "HELD by RobloxKeeper, other clients are safe." :
                                 "NOT held (a Roblox process owns it) \u2014 THIS CAN CLOSE YOUR OTHER CLIENTS."));
                if (clientVer != "?" && !seenClientVersions.Contains(clientVer))
                    seenClientVersions.Add(clientVer);
                AutoPickNextVersion(clientVer);
                WarnOnVersionConflict(clientVer);
            }

            List<int> gone = new List<int>();
            foreach (int pid in knownClients.Keys)
            {
                bool alive = false;
                foreach (ClientInfo ci in clients) if (ci.Pid == pid) { alive = true; break; }
                if (!alive) gone.Add(pid);
            }

            foreach (int pid in gone)
            {
                DateTime opened = knownClients[pid];
                knownClients.Remove(pid);
                if (!clientTrackingReady) continue;

                double lived = (DateTime.Now - opened).TotalSeconds;
                double sinceOther = (DateTime.Now - lastClientOpened).TotalSeconds;
                string why;
                if (installerRunning)
                    why = "the Roblox launcher/bootstrapper ran and closed it. This is Roblox's own installer, " +
                          "not the mutex - it happens even while RobloxKeeper holds the mutex.";
                else if (!mutexHeld && sinceOther < 30 && lastClientOpened != DateTime.MinValue)
                    why = "SINGLETON KILL \u2014 another client launched " + ((int)sinceOther) +
                          "s ago while a Roblox process (not RobloxKeeper) owned the mutex. Fix: close all clients, wait for the green light, then reopen.";
                else if ((DateTime.Now - lastCloseRequest).TotalSeconds < 20)
                    why = "closed by your \"Close all Roblox\" request.";
                else if (!mutexHeld)
                    why = "closed while the mutex was NOT held by RobloxKeeper \u2014 check the multi-instance light.";
                else
                    why = "closed normally - RobloxKeeper held the mutex, so this was NOT a singleton kill (you or the game closed it).";
                Log("Client PID " + pid + " ended after " + ((int)lived) + "s \u2014 " + why);
            }

            clientTrackingReady = true;
        }

        // How Windows launches Roblox when you press Play on the website.
        // A handler pointing at RobloxPlayerLauncher/Installer means the legacy
        // bootstrapper runs on every launch, and that closes running clients
        // regardless of who holds the singleton mutex.
        static string RobloxLaunchCommand()
        {
            string[] roots = { "roblox-player", "roblox" };
            foreach (string proto in roots)
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                        "Software\\Classes\\" + proto + "\\shell\\open\\command"))
                    {
                        string v = k != null ? k.GetValue("") as string : null;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
                catch { }
                try
                {
                    using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(
                        proto + "\\shell\\open\\command"))
                    {
                        string v = k != null ? k.GetValue("") as string : null;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
                catch { }
            }
            return "(not registered)";
        }

        // The version folder the roblox-player protocol is currently registered to.
        static string LaunchPathVersion()
        {
            string cmd = RobloxLaunchCommand();
            int i = cmd.IndexOf("version-", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "?";
            int end = cmd.IndexOfAny(new char[] { '\\', '/', '"' }, i);
            return end > i ? cmd.Substring(i, end - i) : cmd.Substring(i);
        }

        // Point the roblox-player protocol at a specific installed version.
        // Roblox does exactly this itself; doing it up-front means the next
        // launch already matches what that account wants, so Roblox has no
        // reason to run its installer - and the installer is what kills clients.
        static bool SetRegisteredVersion(string versionFolder)
        {
            string exe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions", versionFolder, "RobloxPlayerBeta.exe");
            if (!File.Exists(exe)) return false;
            string value = "\"" + exe + "\" %1";
            bool ok = false;
            foreach (string proto in new string[] { "roblox-player", "roblox" })
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(
                        "Software\\Classes\\" + proto + "\\shell\\open\\command"))
                    {
                        if (k != null) { k.SetValue("", value); ok = true; }
                    }
                }
                catch { }
            }
            return ok;
        }

        static List<string> InstalledVersionList()
        {
            List<string> list = new List<string>();
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "Versions");
                if (!Directory.Exists(root)) return list;
                foreach (string d in Directory.GetDirectories(root))
                    if (File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")))
                        list.Add(Path.GetFileName(d));
            }
            catch { }
            return list;
        }

        static bool UsesLegacyBootstrapper()
        {
            string cmd = RobloxLaunchCommand().ToLowerInvariant();
            return cmd.Contains("robloxplayerlauncher") || cmd.Contains("robloxplayerinstaller");
        }

        // Two installed Roblox versions take turns re-registering themselves.
        // Each hand-over runs that version's installer, which closes every open
        // client - so multi-instance appears to "randomly" break every few minutes.
        void WarnOnVersionConflict(string clientVersion)
        {
            string launchVer = LaunchPathVersion();
            if (clientVersion == "?" || launchVer == "?") return;
            if (string.Equals(clientVersion, launchVer, StringComparison.OrdinalIgnoreCase)) return;
            if (versionConflictLogged) return;
            versionConflictLogged = true;
            Log("VERSION CONFLICT: this client runs " + clientVersion + " but Roblox is registered to launch " +
                launchVer + ". Two Roblox installs are competing - each launch re-runs the installer, " +
                "which closes your open clients. FIX: close Roblox, delete %LOCALAPPDATA%\\Roblox, " +
                "then reinstall once from roblox.com.");
        }

        static string DescribeShortcuts()
        {
            List<string[]> sc = FindRobloxShortcuts();
            if (sc.Count == 0) return "(none pointing at a version folder)";
            string reg = LaunchPathVersion();
            StringBuilder sb = new StringBuilder();
            foreach (string[] s in sc)
            {
                if (sb.Length > 0) sb.Append("\r\n                  ");
                sb.Append(Path.GetFileName(s[0])).Append(" -> ").Append(s[2]);
                if (!string.Equals(s[2], reg, StringComparison.OrdinalIgnoreCase))
                    sb.Append("  <-- STALE (registered is ").Append(reg).Append(")");
            }
            return sb.ToString();
        }

        void RefreshVersionList()
        {
            List<string> vers = InstalledVersionList();
            string current = LaunchPathVersion();

            bool same = cmbVersion.Items.Count == vers.Count;
            if (same)
                for (int i = 0; i < vers.Count; i++)
                    if ((string)cmbVersion.Items[i] != vers[i]) { same = false; break; }

            if (!same)
            {
                suppressVersionEvent = true;
                cmbVersion.Items.Clear();
                foreach (string v in vers) cmbVersion.Items.Add(v);
                suppressVersionEvent = false;
            }

            int idx = cmbVersion.Items.IndexOf(current);
            if (idx >= 0 && cmbVersion.SelectedIndex != idx)
            {
                suppressVersionEvent = true;
                cmbVersion.SelectedIndex = idx;
                suppressVersionEvent = false;
            }
        }

        // Once this machine is known to hand different accounts different client
        // versions, point the NEXT launch at the other installed version as soon
        // as a client opens. The following account then already matches, so Roblox
        // never runs the installer that would close everything.
        // Only engages after the problem is actually observed, so ordinary
        // single-version setups are never touched.
        void AutoPickNextVersion(string justOpenedVersion)
        {
            if (chkAutoVersion == null || !chkAutoVersion.Checked) return;
            if (!versionFlipSeen && seenClientVersions.Count < 2) return;
            if (justOpenedVersion == "?") return;

            List<string> installed = InstalledVersionList();
            if (installed.Count < 2) return;

            string other = null;
            foreach (string v in installed)
                if (!string.Equals(v, justOpenedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    if (seenClientVersions.Contains(v)) { other = v; break; }   // prefer a version an account really used
                    if (other == null) other = v;
                }
            if (other == null) return;
            if (string.Equals(other, LaunchPathVersion(), StringComparison.OrdinalIgnoreCase)) return;

            if (SetRegisteredVersion(other))
            {
                lastRegisteredVersion = other;
                Log("Auto version: next launch set to " + other + " (this client uses " + justOpenedVersion +
                    "). Open your other account now - it will start without Roblox reinstalling.");
            }
        }

        void OnVersionPicked()
        {
            if (suppressVersionEvent || cmbVersion.SelectedItem == null) return;
            string want = (string)cmbVersion.SelectedItem;
            if (string.Equals(want, LaunchPathVersion(), StringComparison.OrdinalIgnoreCase)) return;
            if (SetRegisteredVersion(want))
            {
                lastRegisteredVersion = want;   // our own change is not a Roblox flip
                Log("Next Roblox launch will use " + want + ". Open that account now - because Roblox is " +
                    "already pointed at the version it wants, it has no reason to reinstall, so your other " +
                    "clients stay open.");
            }
            else
                Log("Could not point Roblox at " + want + " - the version folder or exe is missing.");
        }

        // Desktop/Start-menu shortcuts hard-code a version path, so they bypass
        // the version this app points Roblox at. Repoint them silently; with
        // versions alternating per account, a mismatch here is expected rather
        // than something worth warning about.
        void FixStaleShortcuts()
        {
            string reg = LaunchPathVersion();
            if (reg == "?") return;
            int stale = 0;
            foreach (string[] s in FindRobloxShortcuts())
                if (!string.Equals(s[2], reg, StringComparison.OrdinalIgnoreCase)) stale++;
            if (stale == 0) return;

            string versionsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions");
            int fixedCount = RetargetShortcuts(reg, versionsRoot);
            if (fixedCount > 0)
                Log("Repointed " + fixedCount + " Roblox shortcut(s) at the current version (" + reg + ").");
        }

        void CheckLaunchPath()
        {
            if (HasActiveThirdPartyLauncher())
                Log("Third-party Roblox launcher detected: " + ThirdPartyLaunchers() +
                    ". These install and register their OWN Roblox version. If clients keep " +
                    "closing and Roblox keeps reinstalling, use only ONE launcher - remove the " +
                    "others, then reinstall Roblox once.");

            if (UsesLegacyBootstrapper())
                Log("WARNING: Roblox launches through the legacy bootstrapper " +
                    "(RobloxPlayerLauncher). It closes running clients on every launch, " +
                    "even while RobloxKeeper holds the mutex. Fix: reinstall Roblox from " +
                    "roblox.com so Play opens RobloxPlayerBeta.exe directly.");
        }

        // Environment.OSVersion is compatibility-shimmed for this framework target
        // and reports 6.2 on Windows 10/11, which is useless in a shared log.
        static string OsDescription()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion"))
                {
                    if (k != null)
                    {
                        string name = k.GetValue("ProductName") as string;
                        string disp = k.GetValue("DisplayVersion") as string;
                        string build = k.GetValue("CurrentBuild") as string;
                        int b;
                        if (name != null && build != null && int.TryParse(build, out b) && b >= 22000)
                            name = name.Replace("Windows 10", "Windows 11");
                        return (name ?? "Windows") + (disp != null ? " " + disp : "") +
                               " (build " + (build ?? "?") + ")";
                    }
                }
            }
            catch { }
            return Environment.OSVersion.Version.ToString();
        }

        void CopyLog()
        {
            try
            {
                string header = "RobloxKeeper v" + APP_VERSION + " log\r\n" +
                    "Windows: " + OsDescription() + "\r\n" +
                    "Multi-instance: " + (chkMulti.Checked ? "on" : "off") +
                    ", mutex held: " + keeper.Held + "\r\n" +
                    "Anti-AFK: " + (chkAfk.Checked ? "on, " + numInterval.Value + " min, " + cmbKeys.Text : "off") + "\r\n" +
                    "Autostart: " + chkAutostart.Checked + ", auto-clear ghosts: " + chkAutoGhost.Checked + "\r\n" +
                    "Launch path: " + RobloxLaunchCommand() + "\r\n" +
                    "Legacy bootstrapper: " + UsesLegacyBootstrapper() + "\r\n" +
                    "Installed: " + InstalledVersions() + "\r\n" +
                    "Registered version: " + LaunchPathVersion() + "\r\n" +
                    "Version folders: " + AllVersionFolders() + "\r\n" +
                    "Third-party launchers: " + ThirdPartyLaunchers() + "\r\n" +
                    "Roblox shortcuts: " + DescribeShortcuts() + "\r\n" +
                    "----------------------------------------\r\n";
                Clipboard.SetText(header + rtbLog.Text);
                Log("Log copied to clipboard \u2014 paste it wherever you need.");
            }
            catch (Exception ex) { Log("Copy failed: " + ex.Message); }
        }

        bool AnyProcess(string name)
        {
            Process[] procs = Process.GetProcessesByName(name);
            bool any = procs.Length > 0;
            foreach (Process p in procs) p.Dispose();
            return any;
        }

        static string PathOf(Process p)
        {
            try { return p.MainModule.FileName; }
            catch { return "(path unavailable)"; }
        }

        // Who started this client. This is what identifies a third-party
        // launcher (Bloxstrap and friends) or a stale shortcut launching the
        // wrong installed version - the thing that triggers the repair loop.
        static string ParentOf(int pid)
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = " + pid))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        object o = mo["ParentProcessId"];
                        if (o == null) continue;
                        int ppid = Convert.ToInt32(o);
                        try
                        {
                            using (Process pp = Process.GetProcessById(ppid))
                                return pp.ProcessName + " (PID " + ppid + ")";
                        }
                        catch { return "PID " + ppid + " (already exited)"; }
                    }
                }
            }
            catch { }
            return "(unknown)";
        }

        // Known third-party Roblox launchers/bootstrappers. These install and
        // manage their own Roblox version and re-register the protocol, which
        // is a common cause of two versions fighting.
        // A leftover settings folder is NOT an installed launcher - reporting one
        // as active sends people hunting for software they already removed. Only
        // a live process, an executable, or an uninstall entry counts as active.
        static string ThirdPartyLaunchers()
        {
            StringBuilder sb = new StringBuilder();
            string[] names = { "Bloxstrap", "Fishstrap", "Voidstrap", "Lunarstrap", "Roblox Account Manager" };
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            };

            List<string[]> uninstallers = FindLauncherUninstallers();

            foreach (string name in names)
            {
                bool running = false;
                try
                {
                    Process[] ps = Process.GetProcessesByName(name.Replace(" ", ""));
                    running = ps.Length > 0;
                    foreach (Process p in ps) p.Dispose();
                }
                catch { }

                bool hasExe = false, hasFolder = false;
                foreach (string root in roots)
                {
                    try
                    {
                        string dir = Path.Combine(root, name);
                        if (!Directory.Exists(dir)) continue;
                        hasFolder = true;
                        if (Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories).Length > 0)
                            hasExe = true;
                    }
                    catch { }
                }

                bool registered = false;
                foreach (string[] u in uninstallers)
                    if (u[0].ToLowerInvariant().Contains(name.ToLowerInvariant().Replace(" ", "")))
                        registered = true;

                if (!running && !hasExe && !hasFolder && !registered) continue;

                string state = running ? "RUNNING - this one is active"
                             : (hasExe || registered) ? "installed"
                             : "leftover settings only, not installed";
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(name).Append(" (").Append(state).Append(")");
            }
            return sb.Length > 0 ? sb.ToString() : "(none found)";
        }

        // Only launchers that are actually installed/running can cause the loop.
        static bool HasActiveThirdPartyLauncher()
        {
            string s = ThirdPartyLaunchers();
            return s.Contains("RUNNING") || s.Contains("(installed)");
        }

        // Windows uninstall entries for third-party Roblox launchers, so the
        // one step the app cannot do for the user is at least one click away.
        static List<string[]> FindLauncherUninstallers()
        {
            List<string[]> found = new List<string[]>();
            string[] needles = { "bloxstrap", "fishstrap", "voidstrap", "lunarstrap" };
            RegistryKey[] roots = { Registry.CurrentUser, Registry.LocalMachine };
            string[] paths =
            {
                "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
            };
            foreach (RegistryKey root in roots)
            {
                foreach (string p in paths)
                {
                    try
                    {
                        using (RegistryKey k = root.OpenSubKey(p))
                        {
                            if (k == null) continue;
                            foreach (string sub in k.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey s = k.OpenSubKey(sub))
                                    {
                                        if (s == null) continue;
                                        string name = s.GetValue("DisplayName") as string;
                                        string cmd = s.GetValue("UninstallString") as string;
                                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(cmd)) continue;
                                        string lower = name.ToLowerInvariant();
                                        foreach (string n in needles)
                                        {
                                            if (lower.Contains(n))
                                            {
                                                found.Add(new string[] { name, cmd });
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            return found;
        }

        void OfferLauncherUninstall()
        {
            List<string[]> unins = FindLauncherUninstallers();
            if (unins.Count == 0)
            {
                MessageBox.Show(this,
                    "No third-party launcher uninstaller was found in Windows' installed-programs list.\r\n\r\n" +
                    "If one is still installed, remove it from Windows Settings > Apps > Installed apps, " +
                    "then delete any leftover folder in %LOCALAPPDATA%.",
                    "Nothing to uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("These third-party Roblox launchers are installed:");
            sb.AppendLine();
            foreach (string[] u in unins) sb.AppendLine("  - " + u[0]);
            sb.AppendLine();
            sb.AppendLine("They install and register their OWN Roblox version, which is what makes");
            sb.AppendLine("clients close by themselves when a second install disagrees.");
            sb.AppendLine();
            sb.AppendLine("Run their uninstallers now? Each one opens its own uninstall window;");
            sb.AppendLine("follow the prompts. Keep a launcher only if it is the ONLY way you start Roblox.");

            if (MessageBox.Show(this, sb.ToString(), "Uninstall third-party launchers",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                Log("Launcher uninstall cancelled.");
                return;
            }

            foreach (string[] u in unins)
            {
                try
                {
                    string cmd = u[1].Trim();
                    string exe, args = "";
                    if (cmd.StartsWith("\""))
                    {
                        int close = cmd.IndexOf('"', 1);
                        exe = cmd.Substring(1, close - 1);
                        args = cmd.Substring(close + 1).Trim();
                    }
                    else
                    {
                        int sp = cmd.IndexOf(' ');
                        exe = sp > 0 ? cmd.Substring(0, sp) : cmd;
                        args = sp > 0 ? cmd.Substring(sp + 1) : "";
                    }
                    Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                    Log("Started uninstaller for " + u[0] + ".");
                }
                catch (Exception ex)
                {
                    Log("Could not start uninstaller for " + u[0] + ": " + ex.Message +
                        " - remove it from Windows Settings > Apps instead.");
                }
            }
        }

        // Roblox shortcuts point at a VERSIONED exe path. After an update the old
        // shortcut still launches the previous client, which then repairs itself
        // and closes every open client. Stale shortcuts are a prime trigger for
        // the ping-pong, and they survive uninstalling Roblox.
        static List<string[]> FindRobloxShortcuts()
        {
            List<string[]> hits = new List<string[]>();
            List<string> dirs = new List<string>();
            try
            {
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar"));
            }
            catch { }

            object shell = null;
            Type shellType = null;
            try
            {
                shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return hits;
                shell = Activator.CreateInstance(shellType);
            }
            catch { return hits; }

            foreach (string dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                string[] files;
                try { files = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (string f in files)
                {
                    try
                    {
                        object lnk = shellType.InvokeMember("CreateShortcut",
                            System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { f });
                        string target = lnk.GetType().InvokeMember("TargetPath",
                            System.Reflection.BindingFlags.GetProperty, null, lnk, null) as string;
                        if (string.IsNullOrEmpty(target)) continue;
                        if (target.IndexOf("\\Roblox\\Versions\\", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        hits.Add(new string[] { f, target, VersionFolderOf(target) });
                    }
                    catch { }
                }
            }
            return hits;
        }

        // Repoint shortcuts at the version that is actually installed/registered.
        int RetargetShortcuts(string keepVersion, string versionsRoot)
        {
            if (string.IsNullOrEmpty(keepVersion)) return 0;
            string goodExe = Path.Combine(versionsRoot, keepVersion, "RobloxPlayerBeta.exe");
            if (!File.Exists(goodExe)) return 0;

            int fixedCount = 0;
            object shell = null;
            Type shellType = null;
            try
            {
                shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return 0;
                shell = Activator.CreateInstance(shellType);
            }
            catch { return 0; }

            foreach (string[] sc in FindRobloxShortcuts())
            {
                if (string.Equals(sc[2], keepVersion, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    object lnk = shellType.InvokeMember("CreateShortcut",
                        System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { sc[0] });
                    lnk.GetType().InvokeMember("TargetPath",
                        System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { goodExe });
                    lnk.GetType().InvokeMember("Save",
                        System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
                    fixedCount++;
                }
                catch (Exception ex)
                {
                    Log("Could not repoint " + Path.GetFileName(sc[0]) + ": " + ex.Message);
                }
            }
            return fixedCount;
        }

        static string AllVersionFolders()
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "Versions");
                if (!Directory.Exists(root)) return "(no Versions folder)";
                StringBuilder sb = new StringBuilder();
                foreach (string d in Directory.GetDirectories(root))
                {
                    string exe = Path.Combine(d, "RobloxPlayerBeta.exe");
                    if (!File.Exists(exe)) continue;
                    if (sb.Length > 0) sb.Append("\r\n              ");
                    sb.Append(Path.GetFileName(d)).Append("  (")
                      .Append(File.GetLastWriteTime(exe).ToString("MM-dd HH:mm")).Append(")");
                }
                return sb.Length > 0 ? sb.ToString() : "(none with a client exe)";
            }
            catch { return "(unreadable)"; }
        }

        // "...\Versions\version-abc123\RobloxPlayerBeta.exe" -> "version-abc123"
        static string VersionFolderOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            try
            {
                string dir = Path.GetFileName(Path.GetDirectoryName(path));
                return string.IsNullOrEmpty(dir) ? "?" : dir;
            }
            catch { return "?"; }
        }

        string VersionOfPid(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return VersionFolderOf(PathOf(p));
            }
            catch { return "?"; }
        }

        // Names + paths of any Roblox helper processes, so a shared log shows
        // exactly which one interfered rather than just "something did".
        string DescribeHelpers()
        {
            StringBuilder sb = new StringBuilder();
            string[] names = { "RobloxPlayerInstaller", "RobloxPlayerLauncher", "RobloxPlayerBeta" };
            foreach (string n in names)
            {
                if (n == "RobloxPlayerBeta") continue;
                Process[] procs = Process.GetProcessesByName(n);
                foreach (Process p in procs)
                {
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(n).Append(" -> ").Append(PathOf(p));
                    p.Dispose();
                }
            }
            return sb.Length > 0 ? sb.ToString() : "(none)";
        }

        static string InstalledVersions()
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "Versions");
                if (!Directory.Exists(root)) return "(no Versions folder)";
                string[] dirs = Directory.GetDirectories(root);
                StringBuilder sb = new StringBuilder();
                sb.Append(dirs.Length).Append(" version folder(s)");
                DateTime newest = DateTime.MinValue;
                string newestName = "?";
                foreach (string d in dirs)
                {
                    string exe = Path.Combine(d, "RobloxPlayerBeta.exe");
                    if (!File.Exists(exe)) continue;
                    DateTime t = File.GetLastWriteTime(exe);
                    if (t > newest) { newest = t; newestName = Path.GetFileName(d); }
                }
                sb.Append(", newest: ").Append(newestName);
                if (newest != DateTime.MinValue) sb.Append(" (").Append(newest.ToString("yyyy-MM-dd HH:mm")).Append(")");
                return sb.ToString();
            }
            catch { return "(unreadable)"; }
        }

        static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RobloxKeeper", "settings.txt");
            }
        }

        void SaveSettings()
        {
            if (initializing) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllLines(SettingsPath, new string[]
                {
                    "protect=" + (chkProtect.Checked ? "1" : "0"),
                    "autoversion=" + (chkAutoVersion.Checked ? "1" : "0"),
                    "afk=" + (chkAfk.Checked ? "1" : "0"),
                    "interval=" + ((int)numInterval.Value).ToString(),
                    "keys=" + cmbKeys.SelectedIndex.ToString(),
                    "multi=" + (chkMulti.Checked ? "1" : "0"),
                    "autoghost=" + (chkAutoGhost.Checked ? "1" : "0")
                });
            }
            catch { }
        }

        void LoadSettings(out bool afk, out int intervalMin, out int keysIdx, out bool multi, out bool autoghost,
                          out bool protect, out bool autoversion)
        {
            afk = true; intervalMin = 15; keysIdx = 1; multi = true; autoghost = true; protect = true;
            autoversion = true;
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    int tmp;
                    if (key == "afk") afk = val == "1";
                    else if (key == "interval") { if (int.TryParse(val, out tmp)) intervalMin = tmp; }
                    else if (key == "keys") { if (int.TryParse(val, out tmp)) keysIdx = tmp; }
                    else if (key == "multi") multi = val == "1";
                    else if (key == "autoghost") autoghost = val == "1";
                    else if (key == "protect") protect = val == "1";
                    else if (key == "autoversion") autoversion = val == "1";
                }
            }
            catch { }
        }

        void RestoreFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            ForceForeground();
        }

        // Raising our OWN window needs an attach to the current foreground thread;
        // attaching to our own thread is invalid and silently does nothing.
        void ForceForeground()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != Handle)
            {
                uint pid;
                uint fgThread = GetWindowThreadProcessId(fg, out pid);
                uint mine = GetCurrentThreadId();
                if (fgThread != 0 && fgThread != mine)
                {
                    AttachThreadInput(mine, fgThread, true);
                    SetForegroundWindow(Handle);
                    AttachThreadInput(mine, fgThread, false);
                }
                else SetForegroundWindow(Handle);
            }
            // Guarantees the window surfaces even if Windows denies focus.
            bool wasTop = TopMost;
            TopMost = true;
            TopMost = wasTop;
        }

        // A second launch broadcasts this message instead of opening another window.
        protected override void WndProc(ref Message m)
        {
            if (Program.WM_SHOWME != 0 && m.Msg == (int)Program.WM_SHOWME)
            {
                RestoreFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        void Log(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message + "\n";
            rtbLog.ReadOnly = false;
            rtbLog.SelectionStart = 0;
            rtbLog.SelectionLength = 0;
            rtbLog.SelectedText = line;
            if (rtbLog.TextLength > 30000)
                rtbLog.Text = rtbLog.Text.Substring(0, 20000);
            rtbLog.ReadOnly = true;
            rtbLog.SelectionStart = 0;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            uiTimer.Stop();
            nudgeTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            StopMulti();
            base.OnFormClosing(e);
        }
    }
}



















