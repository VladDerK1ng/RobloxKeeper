using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

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

        [STAThread]
        static void Main()
        {
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

        const string APP_VERSION = "1.4.2";

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
        ComboBox cmbKeys;
        Button btnNudge, btnZombie, btnCloseRbx;
        Label lblCountdown, lblDot, lblMultiStatus, lblClientsTitle, lblGhosts;
        ScrollPanel clientsPanel;
        RichTextBox rtbLog;
        System.Windows.Forms.Timer nudgeTimer, uiTimer;
        NotifyIcon tray;
        DateTime nextNudge;
        readonly Dictionary<int, bool> nudgePrefs = new Dictionary<int, bool>();
        readonly List<int> shownPids = new List<int>();

        public MainForm()
        {
            Text = "RobloxKeeper";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(460, 700);
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
            chkAfk.Checked = true;
            chkMulti.Checked = true;
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
            cmbKeys.SelectedIndexChanged += delegate { Log("Nudge keys set: " + cmbKeys.Text); };
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

            lblGhosts = MutedLabel("", 20, 157, 9f);
            cardClients.Controls.Add(lblGhosts);

            btnZombie = AccentButton("End background", 292, 151, 116, 26);
            btnZombie.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnZombie.Visible = false;
            btnZombie.Click += delegate { KillZombies(); };
            cardClients.Controls.Add(btnZombie);

            // --- Multi-instance card ---
            Card cardMulti = new Card();
            cardMulti.Location = new Point(16, 416);
            cardMulti.Size = new Size(428, 132);
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

            Label hint = MutedLabel("One account can't join two games at once \u2014 use separate accounts.", 20, 104, 8.25f);
            cardMulti.Controls.Add(hint);

            // --- Activity card ---
            Card cardLog = new Card();
            cardLog.BackColor = Theme.Inset;
            cardLog.Location = new Point(16, 562);
            cardLog.Size = new Size(428, 122);
            Controls.Add(cardLog);

            Label lblAct = new Label();
            lblAct.Text = "ACTIVITY";
            lblAct.AutoSize = true;
            lblAct.Location = new Point(20, 14);
            lblAct.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            lblAct.ForeColor = Theme.Muted;
            lblAct.BackColor = Theme.Inset;
            cardLog.Controls.Add(lblAct);

            rtbLog = new RichTextBox();
            rtbLog.Location = new Point(18, 36);
            rtbLog.Size = new Size(392, 72);
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
                Log("Anti-AFK enabled \u2014 interval " + numInterval.Value + " min.");
            }
            else
            {
                nudgeTimer.Stop();
                Log("Anti-AFK disabled.");
            }
            UpdateCountdown();
        }

        void OnIntervalChanged(object sender, EventArgs e)
        {
            if (!chkAfk.Checked) return;
            nudgeTimer.Stop();
            nudgeTimer.Interval = (int)numInterval.Value * 60000;
            nextNudge = DateTime.Now.AddMilliseconds(nudgeTimer.Interval);
            nudgeTimer.Start();
            Log("Interval set to " + numInterval.Value + " min.");
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
                Log("Multi-instance enabled \u2014 queued for the singleton mutex.");
            }
            else
            {
                StopMulti();
                Log("Multi-instance disabled \u2014 mutex released.");
            }
            UpdateMultiStatus();
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

            int ghosts;
            List<ClientInfo> clients = GetClients(out ghosts);
            lblClientsTitle.Text = "CLIENTS \u00B7 " + clients.Count;
            lblGhosts.Text = ghosts > 0 ? "+" + ghosts + " background process(es)" : "";
            btnZombie.Visible = ghosts > 0;

            bool changed = clients.Count != shownPids.Count;
            if (!changed)
            {
                for (int i = 0; i < clients.Count; i++)
                    if (clients[i].Pid != shownPids[i]) { changed = true; break; }
            }
            if (changed) RebuildClientRows(clients);
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
            uiTimer.Stop();
            nudgeTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            StopMulti();
            base.OnFormClosing(e);
        }
    }
}






