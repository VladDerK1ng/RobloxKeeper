using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The decisions behind watching as the main window shows it, kept apart
    // so they can be tested.
    static class Watching
    {
        // The one quiet line on the main window.
        public static string StatusLine(int enabled, int total, int clientsWatched, int hitsToday)
        {
            // Short enough for its row beside the button: 262px at 8.25pt.
            if (total == 0) return "Get told when something turns up on screen";
            if (enabled == 0)
                return total == 1 ? "1 watcher, switched off" : total + " watchers, all switched off";
            return Count(enabled, "watcher") + " · " + Count(clientsWatched, "client") + " · "
                 + Count(hitsToday, "hit") + " today";
        }

        static string Count(int n, string thing)
        {
            return n + " " + thing + (n == 1 ? "" : "s");
        }

        // While a hunt is on, the line says so instead - it is the thing most
        // worth knowing at a glance.
        public static string HuntStatusLine(int hunting, int servers, bool besidePicker)
        {
            if (besidePicker) return "hunting · " + Count(servers, "server");
            return "Hunting · " + Count(hunting, "account") + " · " + Count(servers, "server") + " so far";
        }

        // The setup picker on the main window, and the line beside it. They
        // share the status line's 262px: the picker, a gap, then the line.
        public const int SETUP_PICKER_W = 124;
        public const int SETUP_LINE_W = 130;

        // Only when there is something to switch between, so someone with one
        // setup sees the main window exactly as before.
        public static bool ShowsSetupPicker(int setups)
        {
            return setups > 1;
        }

        // The status line with half the room: the picker already says which
        // watchers, so this says how they are doing. "today" is left off when
        // the counts get too long for it.
        public static string SetupStatusLine(int enabled, int total, int clientsWatched, int hitsToday, bool withToday)
        {
            if (total == 0) return "no watchers yet";
            if (enabled == 0) return total == 1 ? "switched off" : "all switched off";
            return Count(clientsWatched, "client") + " · " + Count(hitsToday, "hit") + (withToday ? " today" : "");
        }

        // What each client's row in the watchers list says. Minimized and not
        // in a game are said plainly, because a client that silently reports
        // nothing forever looks exactly like a watcher that does not work.
        public static string StateText(bool assigned, bool known, WatchState state)
        {
            if (!assigned) return "no watcher is set to watch it";
            if (!known) return "about to be looked at";
            switch (state)
            {
                case WatchState.Minimized: return "minimized, so it can't be read - restore it and it can sit behind other windows";
                case WatchState.NotInAGame: return "not in a game yet, or in the tray - nothing to read";
                default: return "being watched";
            }
        }

        // Which client wrote a log, matched on when the log opened - the same
        // rule the activity list uses to name a client in a log line.
        public static int PidForLog(string logFileName, IList<ClientInfo> clients)
        {
            foreach (ClientInfo ci in clients)
            {
                if (ci.Start == DateTime.MinValue) continue;
                if (RobloxLog.LooksLikeSameSession(logFileName, ci.Start.ToUniversalTime())) return ci.Pid;
            }
            return 0;
        }

        // The clients as the watch thread sees them. "Client 2" is the second
        // row of the client list, as on the main window; the account and the
        // server it is in travel with it.
        public static WatchedClient[] ClientsFor(IList<ClientInfo> clients, Func<int, string> accountFor,
                                                 IDictionary<int, RobloxLogEvent> where)
        {
            WatchedClient[] list = new WatchedClient[clients.Count];
            for (int i = 0; i < clients.Count; i++)
            {
                WatchedClient c = new WatchedClient();
                c.Pid = clients[i].Pid;
                c.Label = "Client " + (i + 1);
                c.AccountName = accountFor == null ? null : accountFor(c.Pid);
                RobloxLogEvent e;
                if (where != null && where.TryGetValue(c.Pid, out e))
                {
                    c.PlaceId = e.PlaceId;
                    c.JobId = e.JobId;
                }
                list[i] = c;
            }
            return list;
        }

        public static int ClientsWatched(IList<WatchedClient> clients, IList<Watcher> watchers)
        {
            int n = 0;
            foreach (WatchedClient c in clients)
                foreach (Watcher w in watchers)
                    if (w.Enabled && w.RunsOn(c.AccountName)) { n++; break; }
            return n;
        }

        // Installing Windows' text recogniser needs administrator rights, so
        // Windows is asked to ask - never assumed.
        public static ProcessStartInfo InstallOcr()
        {
            string cmd = ScreenText.InstallCommand;
            int space = cmd.IndexOf(' ');
            ProcessStartInfo p = new ProcessStartInfo(cmd.Substring(0, space) + ".exe", cmd.Substring(space + 1));
            p.UseShellExecute = true;
            p.Verb = "runas";
            return p;
        }
    }

    // How many detections today, for the status line. Starts again at
    // midnight rather than growing for the life of the app.
    class HitCounter
    {
        DateTime day = DateTime.MinValue;
        int count;

        public void Add(DateTime when)
        {
            Roll(when);
            count++;
        }

        public int Today(DateTime now)
        {
            Roll(now);
            return count;
        }

        void Roll(DateTime now)
        {
            if (now.Date == day) return;
            day = now.Date;
            count = 0;
        }
    }

    // Watching, wired into the main window: the store, the engine and its
    // thread, which game each client is in, and what happens when something
    // is found.
    partial class MainForm
    {
        WatchStore watchStore;
        WatchEngine watchEngine;
        WatchKit watchKit;
        readonly HitCounter watchHits = new HitCounter();
        // Where each client is, from its log. Keyed by PID and pruned with it.
        readonly Dictionary<int, RobloxLogEvent> clientWhere = new Dictionary<int, RobloxLogEvent>();
        // What the watch thread reads. Replaced whole, never changed in place.
        volatile WatchWork watchWork = new WatchWork();
        bool noWebhookSaid;
        Button btnWatchers;
        Label lblWatchers;
        ThemedPicker cmbSetup;
        bool fillingSetup;

        void StartWatching()
        {
            watchStore = new WatchStore(WatchStore.DefaultPath);
            watchStore.Load();

            LiveCapture capture = new LiveCapture();
            LiveReader reader = new LiveReader();
            watchEngine = new WatchEngine(capture, reader);
            // Raised on the watch thread. BeginInvoke, never Invoke: Invoke
            // would deadlock the moment the window stops the thread.
            watchEngine.Found = delegate(DetectionEvent d) { OnUi(delegate { OnFound(d); }); };
            watchEngine.Problem = delegate(Watcher w, WatchedClient c, string why)
            {
                OnUi(delegate { Log(w.Name + " on " + c.Label + ": " + why); });
            };

            watchKit = new WatchKit();
            watchKit.Store = watchStore;
            watchKit.Engine = watchEngine;
            watchKit.Capture = capture;
            watchKit.Reader = reader;
            watchKit.Clients = delegate { return watchWork.Clients; };
            watchKit.Accounts = SavedAccountNames;
            watchKit.Log = Log;
            // Asked once and kept, so the answer cannot change from one look
            // at the watchers list to the next.
            watchKit.CanReadText = ScreenText.Available;
            watchKit.Hunts = this;

            logWatch.Joined = OnClientJoined;
            PublishWatchWork();
        }

        void StopWatching()
        {
            if (watchEngine != null) watchEngine.Stop();
            ReleaseHotkeys();
        }

        void OnUi(MethodInvoker a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); }
            catch { }   // closing
        }

        IList<string> SavedAccountNames()
        {
            EnsureAccounts();
            List<string> names = new List<string>();
            foreach (RobloxAccount a in accounts.Accounts) names.Add(a.Name);
            return names;
        }

        // A client joined a game, or was already in one when the app started.
        void OnClientJoined(string logFileName, RobloxLogEvent e)
        {
            int pid = Watching.PidForLog(logFileName, lastClients);
            if (pid <= 0) return;
            clientWhere[pid] = e;
            PublishWatchWork();
            HuntJoined(pid, e);
            RulesJoined(pid);
        }

        // Once a second, from the main loop.
        void WatchTick(List<ClientInfo> clients)
        {
            if (watchStore == null) return;
            List<int> gone = new List<int>();
            foreach (int pid in clientWhere.Keys)
            {
                bool alive = false;
                foreach (ClientInfo c in clients) if (c.Pid == pid) { alive = true; break; }
                if (!alive) gone.Add(pid);
            }
            foreach (int pid in gone) clientWhere.Remove(pid);
            PublishWatchWork();
            RulesTick();
        }

        // Hands the watch thread a fresh snapshot, and runs the thread only
        // while at least one watcher is switched on - someone who never uses
        // this pays nothing for it.
        void PublishWatchWork()
        {
            if (watchStore == null) return;

            WatchWork w = new WatchWork();
            w.Clients = Watching.ClientsFor(lastClients, clientLabels.NameFor, clientWhere);
            w.Watchers = new List<Watcher>(watchStore.Watchers).ToArray();
            w.Regions = new List<WatchRegion>(watchStore.Regions).ToArray();
            watchWork = w;

            bool any = false;
            foreach (Watcher x in w.Watchers) if (x.Enabled) { any = true; break; }
            if (any && !watchEngine.Running) watchEngine.Start(delegate { return watchWork; });
            else if (!any && watchEngine.Running) watchEngine.Stop();

            RefreshHotkeys();
            UpdateWatchStatus();
        }

        void UpdateWatchStatus()
        {
            if (lblWatchers == null || watchStore == null) return;
            bool picker = Watching.ShowsSetupPicker(watchStore.Setups.Count);
            ShowSetups(picker);

            WatchWork w = watchWork;
            int enabled = 0;
            foreach (Watcher x in w.Watchers) if (x.Enabled) enabled++;
            int clients = Watching.ClientsWatched(w.Clients, w.Watchers);
            int hits = watchHits.Today(DateTime.Now);
            string text;
            if (HuntsActive() > 0) text = Watching.HuntStatusLine(HuntsActive(), HuntServers(), picker);
            else if (!picker) text = Watching.StatusLine(enabled, w.Watchers.Length, clients, hits);
            else
            {
                text = Watching.SetupStatusLine(enabled, w.Watchers.Length, clients, hits, true);
                if (TextRenderer.MeasureText(text, lblWatchers.Font).Width > lblWatchers.Width)
                    text = Watching.SetupStatusLine(enabled, w.Watchers.Length, clients, hits, false);
            }
            if (lblWatchers.Text != text) lblWatchers.Text = text;
        }

        // The picker beside the Watchers button, filled only when the setups
        // have changed - refilling it every second would close it while open.
        void ShowSetups(bool picker)
        {
            if (cmbSetup == null) return;
            if (cmbSetup.Visible != picker)
            {
                cmbSetup.Visible = picker;
                int x = picker ? Ui.PAD + Watching.SETUP_PICKER_W + 8 : Ui.PAD;
                lblWatchers.Bounds = new Rectangle(x, lblWatchers.Top,
                    picker ? Watching.SETUP_LINE_W : 262, lblWatchers.Height);
            }
            if (!picker) return;

            List<string> names = new List<string>();
            foreach (WatchSetup s in watchStore.Setups) names.Add(s.Name);
            int chosen = watchStore.Setups.IndexOf(watchStore.Chosen);
            bool same = names.Count == cmbSetup.Items.Count && chosen == cmbSetup.SelectedIndex;
            for (int i = 0; same && i < names.Count; i++) same = names[i] == cmbSetup.Items[i];
            if (same) return;

            fillingSetup = true;
            try
            {
                cmbSetup.Items.Clear();
                cmbSetup.Items.AddRange(names);
                cmbSetup.SelectedIndex = chosen;
                cmbSetup.Invalidate();
            }
            finally { fillingSetup = false; }
        }

        // One click: every watcher swaps, and the watch thread with them.
        void ChooseSetup(string name)
        {
            if (fillingSetup || watchStore == null) return;
            WatchSetup s = watchStore.FindSetup(name);
            if (s == null || ReferenceEquals(s, watchStore.Chosen)) return;
            watchStore.Choose(s.Name);
            watchStore.Save();
            watchEngine.ForgetImages();
            Log("Now watching with the " + s.Name + " setup.");
            PublishWatchWork();
        }

        void OpenWatchers()
        {
            PublishWatchWork();
            using (WatchersDialog d = new WatchersDialog(watchKit, PublishWatchWork))
                d.ShowDialog(this);
            PublishWatchWork();
        }

        // ---------- when something is found ----------

        void OnFound(DetectionEvent d)
        {
            watchHits.Add(d.When);
            Watcher w = d.Watcher;
            string said = d.Line ?? d.Matched ?? "";

            if (w == null || w.WriteLog)
                Log(d.Headline() + (said.Length > 0 ? ": " + said : "") + ".");

            if (w != null && w.ShowTray)
            {
                try
                {
                    tray.BalloonTipTitle = Clip(d.Headline(), 60);
                    tray.BalloonTipText = said.Length > 0 ? Clip(said, 200) : "Seen just now.";
                    tray.BalloonTipIcon = ToolTipIcon.Info;
                    tray.ShowBalloonTip(8000);
                }
                catch { }
            }

            if (w != null && w.PlaySound)
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }

            if (w == null || w.SendDiscord) SendToDiscord(w, d);
            if (w != null && !string.IsNullOrEmpty(w.ThenMacro)) PlayThen(w, d);
            RulesFound(d);
            HuntFound(d);
            UpdateWatchStatus();
        }

        // ---------- then: a macro ----------

        readonly MacroQueue macroQueue = new MacroQueue();
        readonly RuleGate ruleGate = new RuleGate();
        readonly object pumpGate = new object();
        bool pumping;

        void PlayThen(Watcher w, DetectionEvent d)
        {
            Macro m = watchStore.FindMacro(w.ThenMacro);
            if (m == null)
            {
                Log(w.Name + " should play the macro " + w.ThenMacro + ", but there is no macro by that name any more.");
                return;
            }
            if (d.Pid <= 0 || !ruleGate.Allow(w, d.Pid, d.When)) return;
            QueueMacro(m, d.Pid, string.IsNullOrEmpty(d.AccountName) ? d.ClientLabel : d.ClientLabel + " - " + d.AccountName, 1);
        }

        // One queue for every macro a watcher or a rule sets off, played one
        // at a time off the UI thread.
        void QueueMacro(Macro m, int pid, string label, int times)
        {
            MacroJob job = new MacroJob();
            job.Macro = m.Copy();
            job.Pid = pid;
            job.Label = label;
            job.Times = Math.Max(1, times);
            if (!macroQueue.Offer(job))
            {
                Log("Didn't play " + m.Name + " on " + job.Label + ": " + MacroQueue.MAX_WAITING
                    + " macros are already waiting their turn.");
                return;
            }

            lock (pumpGate)
            {
                if (pumping) return;
                pumping = true;
            }
            Thread t = new Thread(PumpMacros);
            t.IsBackground = true;
            t.Name = "Macros";
            t.Start();
        }

        // One at a time, off the UI thread: a macro sleeps for as long as it
        // plays.
        void PumpMacros()
        {
            while (true)
            {
                MacroJob j;
                lock (pumpGate)
                {
                    j = macroQueue.Take();
                    if (j == null) { pumping = false; return; }
                }
                // Several times is the steps over again, in one turn at the
                // front rather than handing focus back and forth between.
                Macro play = j.Macro;
                if (j.Times > 1)
                {
                    play = j.Macro.Copy();
                    for (int i = 1; i < j.Times; i++)
                        foreach (MacroStep s in j.Macro.Steps) play.Steps.Add(s.Copy());
                }
                string problem;
                try { problem = MacroPlayer.Play(play, WindowCapture.GameWindow(j.Pid)); }
                catch (Exception ex) { problem = ex.Message; }
                string said = problem == null
                    ? "Played " + j.Macro.Name + (j.Times > 1 ? " " + j.Times + " times" : "") + " on " + j.Label + "."
                    : j.Macro.Name + " on " + j.Label + " didn't finish: " + problem + ".";
                OnUi(delegate { Log(said); });
            }
        }

        static string Clip(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max - 3) + "...";
        }

        // Off the UI thread, tried again while it is worth trying, and one line
        // in the activity list if it never gets through - never a dialog, and
        // never the webhook link itself.
        void SendToDiscord(Watcher w, DetectionEvent d)
        {
            string url = w != null && !string.IsNullOrEmpty(w.WebhookUrl) ? w.WebhookUrl : watchStore.WebhookUrl;
            if (string.IsNullOrEmpty(url))
            {
                if (!noWebhookSaid)
                {
                    noWebhookSaid = true;
                    Log("A watcher found something, but no Discord webhook is set - add one in Watchers.");
                }
                return;
            }

            string name = d.WatcherName;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string failed;
                try
                {
                    WebhookRequest r = WebhookPost.Build(url, d, WebhookPost.Png(d.Crop));
                    failed = WebhookPost.SendWithRetry(
                        delegate { return WebhookPost.Send(r); },
                        WebhookPost.RetryDelaysMs,
                        delegate(int ms) { Thread.Sleep(ms); });
                }
                catch { failed = "couldn't build the message"; }

                if (failed != null)
                    OnUi(delegate { Log("Couldn't send \"" + name + "\" to Discord: " + failed + "."); });
            });
        }
    }
}
