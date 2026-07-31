using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RobloxKeeper
{
    // The multi-instance status line shares a fixed-height card with the hint
    // below it, so how these wrap is a layout constraint, not just wording.
    // Kept together here so the length of each is obvious side by side.
    static class MultiStatus
    {
        public const int WIDTH_WITH_BUTTON = 238;
        public const int WIDTH_ALONE = 362;

        public const string DISABLED = "Disabled - a new client replaces the running one.";
        public const string ACTIVE = "Active - singleton mutex held. New clients stay open.";
        public const string WAITING = "Waiting - a Roblox client owns the mutex. Close them all and I take over.";
    }

    // Window state, the one-second housekeeping loop, and the glue between the
    // UI and the workers. The pieces that do real work live in their own files:
    // MainForm.Ui.cs builds the layout, MainForm.Afk.cs runs the nudges, and
    // MainForm.Install.cs deals with Roblox reinstalling itself.
    partial class MainForm : Form
    {
        const string RUN_KEY = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string AUTOSTART_VALUE = "RobloxKeeper";
        const string ROBLOX_EVENT = "ROBLOX_singletonEvent";

        readonly MutexKeeper keeper = new MutexKeeper();
        readonly GhostCleaner ghostCleaner = new GhostCleaner();
        readonly PerformanceManager perf = new PerformanceManager();
        Updater updater;
        AppSettings settings = new AppSettings();

        EventWaitHandle singletonEvent;
        bool heldLogged;
        bool updateHeldLogged;
        string lastRedirectArgs;
        DateTime lastRedirectAt = DateTime.MinValue;

        ThemedToggle chkAfk, chkMulti;
        ThemedCheckBox chkAutostart, chkAutoGhost, chkAutoTrim, chkPerfEco;
        ThemedNumeric numInterval, numTrimEvery;
        ThemedPicker cmbKeys, cmbPerfPriority, cmbPerfCores;
        Button btnNudge, btnZombie, btnCloseRbx, btnTrimAll;
        Label lblCountdown, lblDot, lblMultiStatus, lblClientsTitle, lblGhosts, lblUpdating;
        ScrollPanel clientsPanel;
        RichTextBox rtbLog;
        System.Windows.Forms.Timer nudgeTimer, uiTimer;
        NotifyIcon tray;

        DateTime nextNudge;
        Font countdownClock, countdownWord;
        readonly Dictionary<int, bool> nudgePrefs = new Dictionary<int, bool>();
        readonly List<int> shownPids = new List<int>();
        readonly Dictionary<int, Label> ramLabels = new Dictionary<int, Label>();
        List<ClientInfo> lastClients = new List<ClientInfo>();

        bool initializing;
        bool rowsBuilt;
        bool startHidden;
        bool installerSeen;
        DateTime lastCloseRequest = DateTime.MinValue;
        DateTime updaterSeenAt = DateTime.MinValue;
        bool updatingShown;
        bool versionConflictLogged;
        string lastRegisteredVersion;
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
            SetHeightToFitScreen();
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();

            ghostCleaner.Log = Log;
            perf.Log = Log;
            updater = new Updater(this, Log);

            nudgeTimer = new System.Windows.Forms.Timer();
            nudgeTimer.Tick += delegate { NudgeAll("timer"); };

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 1000;
            uiTimer.Tick += delegate { OnUiTick(); };
            uiTimer.Start();

            Log("RobloxKeeper v" + AppInfo.APP_VERSION + " started.");

            initializing = true;
            settings = AppSettings.Load();
            numInterval.Value = settings.IntervalMinutes;
            if (settings.KeysIndex >= 0 && settings.KeysIndex < cmbKeys.Items.Count)
                cmbKeys.SelectedIndex = settings.KeysIndex;
            chkAutoGhost.Checked = settings.AutoGhost;
            cmbPerfPriority.SelectedIndex = settings.PerfPriority;
            Ui.SelectCoreCount(cmbPerfCores, settings.PerfCores);
            chkPerfEco.Checked = settings.PerfEco;
            chkAutoTrim.Checked = settings.AutoTrim;
            numTrimEvery.Value = settings.AutoTrimMinutes;
            perf.Defaults = settings.ToProfile();
            chkAfk.Checked = settings.Afk;
            chkMulti.Checked = settings.Multi;
            initializing = false;

            UpdateCountdown();
            Log("Settings: Anti-AFK " + (settings.Afk ? "on, " + settings.IntervalMinutes + " min, " + cmbKeys.Text : "off") +
                " · multi-instance " + (settings.Multi ? "on" : "off") + ".");
            if (!perf.Defaults.SameAs(new ClientProfile()))
                Log("New clients will run at " + perf.Defaults + ".");

            CheckLaunchPath();
            FixStaleShortcuts();
            EnsureStartMenuShortcut();
            updater.CheckInBackground();
            OnUiTick();
        }

        // The cards are laid out at fixed positions and add up to FULL_HEIGHT. On
        // a screen too short for that - a 768p or 900p laptop - the bottom cards
        // would simply be off-screen and unreachable, so the window is capped to
        // the working area and scrolls instead. The extra width covers the
        // scrollbar so it never sits on top of a card.
        const int FULL_HEIGHT = 870;
        const int BASE_WIDTH = 460;

        void SetHeightToFitScreen()
        {
            int available = FULL_HEIGHT;
            try
            {
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                // Leave room for the title bar and border, which sit outside ClientSize.
                available = work.Height - (SystemInformation.CaptionHeight +
                                           SystemInformation.FixedFrameBorderSize.Height * 2 + 8);
            }
            catch { }

            if (available >= FULL_HEIGHT)
            {
                ClientSize = new Size(BASE_WIDTH, FULL_HEIGHT);
                return;
            }

            int height = available > 400 ? available : 400;
            ClientSize = new Size(BASE_WIDTH + SystemInformation.VerticalScrollBarWidth, height);
            AutoScroll = true;
            AutoScrollMinSize = new Size(BASE_WIDTH, FULL_HEIGHT);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1;
            try { Native.DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { }

            // Scrollbars are child windows with their own theme, so they stay
            // bright white next to the dark log unless asked otherwise.
            DarkScrollbars.Apply(rtbLog);
            DarkScrollbars.Apply(clientsPanel);
            DarkScrollbars.Apply(this);
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

        // ---------- Housekeeping ----------

        void OnUiTick()
        {
            UpdateCountdown();
            if (chkMulti.Checked && keeper.Held && !heldLogged)
            {
                heldLogged = true;
                Log("Multi-instance active - singleton mutex acquired.");
            }
            UpdateMultiStatus();
            btnCloseRbx.Visible = chkMulti.Checked && !keeper.Held;

            Process[] snapshot = Process.GetProcesses();
            try { TickWithSnapshot(snapshot); }
            finally { foreach (Process p in snapshot) { try { p.Dispose(); } catch { } } }
        }

        void TickWithSnapshot(Process[] snapshot)
        {
            GhostCount ghosts;
            List<ClientInfo> clients = ClientTracker.ClientsFrom(snapshot, out ghosts);
            lastClients = clients;

            lblClientsTitle.Text = "CLIENTS · " + clients.Count;
            lblGhosts.Text = GhostLabel(ghosts);
            // Only offered for genuinely stuck processes. Showing it while a
            // client is still launching invites the user to kill the client they
            // just opened.
            btnZombie.Visible = ghosts.Stuck > 0;

            // A window-less client that has outlived the launch grace period is
            // leaked memory whether or not multi-instance is on and whether or not
            // we hold the mutex, so nothing else gates this. Younger processes are
            // still drawing their first frame and are never touched.
            if (chkAutoGhost.Checked && ghosts.Stuck > 0)
                ghostCleaner.ClearStuck();

            perf.Prune(clients);
            perf.ApplyPending(clients);
            if (chkAutoTrim.Checked)
                perf.AutoTrimTick(clients, numTrimEvery.Value, PerformanceManager.ForegroundPid());

            // When Roblox installs an update, ITS OWN installer terminates every
            // running client (old version) - no tool can prevent that. Surface it
            // so a mass client close is explained instead of looking like a bug.
            // The install rewriting its own registration is the ping-pong itself.
            // Catch the flip as it happens - comparing versions only at client
            // open misses it, because the flip lands moments later.
            string regNow = RobloxInstall.LaunchPathVersion();
            if (lastRegisteredVersion == null) lastRegisteredVersion = regNow;
            else if (regNow != lastRegisteredVersion)
            {
                Log("ROBLOX RE-REGISTERED ITSELF: " + lastRegisteredVersion + " -> " + regNow +
                    ". Two installs are taking turns claiming Roblox; each hand-over runs an " +
                    "installer that closes every open client. This repeats forever until one is removed. " +
                    "Launchers present: " + RobloxInstall.ThirdPartyLaunchers());
                lastRegisteredVersion = regNow;
            }

            // Roblox serves different client versions to different accounts, so
            // switching between accounts makes it reinstall - and its installer
            // closes every running client to replace files. Stopping the installer
            // WHILE clients are open keeps the session alive; when nothing is
            // running it is left alone, so Roblox still updates normally.
            List<Process> installers = ClientTracker.ByName(snapshot, "RobloxPlayerInstaller");
            List<Process> launchers = ClientTracker.ByName(snapshot, "RobloxPlayerLauncher");
            HandleVersionSwitch(clients.Count, installers, snapshot);

            bool installerRunning = installers.Count > 0 || launchers.Count > 0;
            string helpers = installerRunning ? RobloxInstall.DescribeHelpers() : "(none)";
            if (installerRunning && !installerSeen && helpers != "(none)")
            {
                updaterSeenAt = DateTime.Now;
                Log("Roblox helper running: " + helpers +
                    " - it can close open clients regardless of the mutex." +
                    (RobloxInstall.UsesLegacyBootstrapper()
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
                    : "One account can't join two games at once - use separate accounts.";
                lblUpdating.ForeColor = updating ? Theme.Amber : Theme.Muted;
                lblUpdating.Font = new Font("Segoe UI", 8.25f, updating ? FontStyle.Bold : FontStyle.Regular);
            }

            TrackClientLifecycle(clients, installerRunning);

            int nudgeable = 0;
            foreach (ClientInfo ci in clients)
            {
                bool wanted;
                if (!nudgePrefs.TryGetValue(ci.Pid, out wanted)) wanted = true;
                if (wanted) nudgeable++;
            }
            UpdateAfkTimer(nudgeable);

            // rowsBuilt forces the first pass through. Without it, starting with
            // no clients means 0 == 0, the rows are never built, and the panel
            // sits empty instead of saying so.
            bool changed = !rowsBuilt || clients.Count != shownPids.Count;
            if (!changed)
            {
                for (int i = 0; i < clients.Count; i++)
                    if (clients[i].Pid != shownPids[i]) { changed = true; break; }
            }
            if (changed) { RebuildClientRows(clients); rowsBuilt = true; }
            else UpdateRamLabels(clients);
        }

        static string GhostLabel(GhostCount g)
        {
            if (g.Stuck > 0) return "+" + g.Stuck + " stuck";
            if (g.Starting > 0) return "+" + g.Starting + " starting";
            return "";
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
                string clientVer = RobloxInstall.VersionOfPid(ci.Pid);
                Log("Client PID " + ci.Pid + " [" + clientVer + "] opened, launched by " + RobloxInstall.ParentOf(ci.Pid) +
                    " - mutex " +
                    (mutexHeld ? "HELD by RobloxKeeper, other clients are safe." :
                                 "NOT held (a Roblox process owns it) - THIS CAN CLOSE YOUR OTHER CLIENTS."));
                if (clientVer != "?" && !seenClientVersions.Contains(clientVer))
                    seenClientVersions.Add(clientVer);
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
                    why = "SINGLETON KILL - another client launched " + ((int)sinceOther) +
                          "s ago while a Roblox process (not RobloxKeeper) owned the mutex. Fix: close all clients, wait for the green light, then reopen.";
                else if ((DateTime.Now - lastCloseRequest).TotalSeconds < 20)
                    why = "closed by your \"Close all Roblox\" request.";
                else if (!mutexHeld)
                    why = "closed while the mutex was NOT held by RobloxKeeper - check the multi-instance light.";
                else
                    why = "closed normally - RobloxKeeper held the mutex, so this was NOT a singleton kill (you or the game closed it).";
                Log("Client PID " + pid + " ended after " + ((int)lived) + "s - " + why);
            }

            clientTrackingReady = true;
        }

        // ---------- Multi-instance ----------

        void OnMultiToggled(object sender, EventArgs e)
        {
            if (chkMulti.Checked)
            {
                StartMulti();
                if (!initializing) Log("Multi-instance enabled - queued for the singleton mutex.");
            }
            else
            {
                StopMulti();
                if (!initializing) Log("Multi-instance disabled - mutex released.");
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

        void UpdateMultiStatus()
        {
            // The status shares its row with "Close all Roblox", which only
            // appears while waiting. Widening the text when the button is gone
            // keeps the common "Active" case on a single line.
            bool buttonShowing = chkMulti.Checked && !keeper.Held;
            lblMultiStatus.MaximumSize = new Size(
                buttonShowing ? MultiStatus.WIDTH_WITH_BUTTON : MultiStatus.WIDTH_ALONE, 0);

            if (!chkMulti.Checked)
            {
                lblDot.ForeColor = Theme.Muted;
                lblMultiStatus.Text = MultiStatus.DISABLED;
            }
            else if (keeper.Held)
            {
                lblDot.ForeColor = Theme.Green;
                lblMultiStatus.Text = MultiStatus.ACTIVE;
            }
            else
            {
                lblDot.ForeColor = Theme.Amber;
                lblMultiStatus.Text = MultiStatus.WAITING;
            }
        }

        // ---------- Autostart / Start menu ----------

        void OnAutostartToggled(object sender, EventArgs e)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (chkAutostart.Checked)
                    {
                        k.SetValue(AUTOSTART_VALUE, "\"" + Application.ExecutablePath + "\" --minimized");
                        Log("Autostart enabled - starts minimized to the tray with Windows.");
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

        // Puts RobloxKeeper in the Start menu so it can be found by typing its
        // name in Windows search. Rewrites the shortcut if the exe has moved,
        // so searching never launches a stale path.
        void EnsureStartMenuShortcut()
        {
            try
            {
                string exe = Application.ExecutablePath;
                string lnk = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "RobloxKeeper.lnk");

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);

                if (System.IO.File.Exists(lnk))
                {
                    object existing = shellType.InvokeMember("CreateShortcut",
                        System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                    string target = existing.GetType().InvokeMember("TargetPath",
                        System.Reflection.BindingFlags.GetProperty, null, existing, null) as string;
                    if (string.Equals(target, exe, StringComparison.OrdinalIgnoreCase)) return;
                }

                object sc = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type t = sc.GetType();
                t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc,
                    new object[] { exe });
                t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc,
                    new object[] { System.IO.Path.GetDirectoryName(exe) });
                t.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc,
                    new object[] { exe + ",0" });
                t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc,
                    new object[] { "Anti-AFK and multi-instance manager for Roblox" });
                t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);

                Log("Added to the Start menu - search \"RobloxKeeper\" to open it.");
            }
            catch (Exception ex)
            {
                Log("Could not add a Start menu entry: " + ex.Message);
            }
        }

        // ---------- Settings ----------

        void SaveSettings()
        {
            if (initializing) return;
            settings.Afk = chkAfk.Checked;
            settings.KeysIndex = cmbKeys.SelectedIndex;
            settings.IntervalMinutes = numInterval.Value;
            settings.Multi = chkMulti.Checked;
            settings.AutoGhost = chkAutoGhost.Checked;
            settings.PerfPriority = cmbPerfPriority.SelectedIndex;
            settings.PerfCores = Ui.SelectedCoreCount(cmbPerfCores);
            settings.PerfEco = chkPerfEco.Checked;
            settings.AutoTrim = chkAutoTrim.Checked;
            settings.AutoTrimMinutes = numTrimEvery.Value;
            settings.Save();
        }

        // ---------- Tray / logging ----------

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
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != Handle)
            {
                uint pid;
                uint fgThread = Native.GetWindowThreadProcessId(fg, out pid);
                uint mine = Native.GetCurrentThreadId();
                if (fgThread != 0 && fgThread != mine)
                {
                    Native.AttachThreadInput(mine, fgThread, true);
                    Native.SetForegroundWindow(Handle);
                    Native.AttachThreadInput(mine, fgThread, false);
                }
                else Native.SetForegroundWindow(Handle);
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

        void CopyLog()
        {
            try
            {
                string header = "RobloxKeeper v" + AppInfo.APP_VERSION + " log\r\n" +
                    "Windows: " + RobloxInstall.OsDescription() + "\r\n" +
                    "CPU: " + Environment.ProcessorCount + " logical processor(s)\r\n" +
                    "Multi-instance: " + (chkMulti.Checked ? "on" : "off") +
                    ", mutex held: " + keeper.Held + "\r\n" +
                    "Anti-AFK: " + (chkAfk.Checked ? "on, " + numInterval.Value + " min, " + cmbKeys.Text : "off") + "\r\n" +
                    "Autostart: " + chkAutostart.Checked + ", auto-clear ghosts: " + chkAutoGhost.Checked + "\r\n" +
                    "Client defaults: " + perf.Defaults + "\r\n" +
                    "Auto-trim: " + (chkAutoTrim.Checked ? "every " + numTrimEvery.Value + " min" : "off") + "\r\n" +
                    "Launch path: " + RobloxInstall.RobloxLaunchCommand() + "\r\n" +
                    "Legacy bootstrapper: " + RobloxInstall.UsesLegacyBootstrapper() + "\r\n" +
                    "Installed: " + RobloxInstall.InstalledVersions() + "\r\n" +
                    "Registered version: " + RobloxInstall.LaunchPathVersion() + "\r\n" +
                    "Version folders: " + RobloxInstall.AllVersionFolders() + "\r\n" +
                    "Third-party launchers: " + RobloxInstall.ThirdPartyLaunchers() + "\r\n" +
                    "Roblox shortcuts: " + RobloxInstall.DescribeShortcuts() + "\r\n" +
                    "----------------------------------------\r\n";
                Clipboard.SetText(header + rtbLog.Text);
                Log("Log copied to clipboard - paste it wherever you need.");
            }
            catch (Exception ex) { Log("Copy failed: " + ex.Message); }
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
