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
            return Count(enabled, "watcher") + " · " + Count(clientsWatched, "window") + " · "
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
            return Count(clientsWatched, "window") + " · " + Count(hitsToday, "hit") + (withToday ? " today" : "");
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
                case WatchState.Minimized: return "minimized, so it can't be watched - restore it (it can stay behind other windows)";
                case WatchState.NotInAGame: return "not in a game yet - nothing to watch";
                default: return "being watched";
            }
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
        readonly ClientNamer namer = new ClientNamer();
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
            watchKit.Macros = this;
            watchKit.Scripts = this;

            logWatch.Joined = OnClientJoined;
            logWatch.Identified = OnClientIdentified;
            PublishWatchWork();
        }

        void StopWatching()
        {
            if (watchEngine != null) watchEngine.Stop();
            StopEverything();
            ReleaseHotkeys();
            ReleaseMacroKeys();
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
            int pid = PidForLog(logFileName);
            if (pid <= 0) return;
            // Named first, so a hunt already knows this is its account's client.
            NameClient(pid, e.UserId);
            clientWhere[pid] = e;
            // For Home's "Continue": the games played, as Roblox's log names them.
            RememberPlace(e.PlaceId);
            PublishWatchWork();
            HuntJoined(pid, e);
            // Not for a join from before the app started: a join rule would
            // otherwise play on every client already in a game, at once.
            if (!e.Earlier) RulesJoined(pid);
        }

        // Roblox said who a client is, a read after its join.
        void OnClientIdentified(string logFileName, RobloxLogEvent e)
        {
            NameClient(PidForLog(logFileName), e.UserId);
        }

        // Names a client after the account its log says it is.
        //
        // The process the app started is not always the one that plays: Roblox
        // can hand a launch to one of its own tray copies and exit, and the
        // game then runs in a process nobody named. The log is right however
        // the client was started, so it wins over a name given at launch.
        void NameClient(int pid, string userId)
        {
            if (pid <= 0 || string.IsNullOrEmpty(userId)) return;
            EnsureAccounts();
            RobloxAccount recorded;
            string name = namer.NameFor(userId, accounts,
                delegate { OnUi(delegate { NameClient(pid, userId); }); }, out recorded);
            if (recorded != null) accounts.Save();
            if (name == null || name == clientLabels.NameFor(pid)) return;

            clientLabels.Assign(pid, name);
            HuntAdopt(pid, name);
            PublishWatchWork();
        }

        // Once a second, from the main loop, with every Roblox process alive
        // now - window or not, as the labels are pruned: a client can join
        // before its window shows.
        void WatchTick(List<int> alivePids)
        {
            if (watchStore == null) return;
            List<int> gone = new List<int>();
            foreach (int pid in clientWhere.Keys)
                if (!alivePids.Contains(pid)) gone.Add(pid);
            foreach (int pid in gone) clientWhere.Remove(pid);
            PublishWatchWork();
            RulesTick();
            ScriptsTick();
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

            RefreshGlobalKeys();
            RefreshStopKey();
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
            Macro m = w.ThenClicks ? (w.Kind == WatchKind.ImageFound ? w.ClickMacro() : null)
                                   : watchStore.FindMacro(w.ThenMacro);
            if (m == null)
            {
                if (!w.ThenClicks)
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
            QueueMacro(m, pid, label, times, null, false, MacroRunner.DEFAULT_TURN_MS);
        }

        // Null when it was not taken: the same macro is already waiting or
        // playing on that client, or the queue is full.
        MacroJob QueueMacro(Macro m, int pid, string label, int times, string tag, bool userIsPlaying, int turnMs)
        {
            return QueueMacro(m, pid, label, times, tag, userIsPlaying, turnMs, false);
        }

        // stepsAside: it takes turns with the keyboard like a session though
        // it ends by itself - see MacroRunner.StepsAside.
        MacroJob QueueMacro(Macro m, int pid, string label, int times, string tag, bool userIsPlaying, int turnMs,
                            bool stepsAside)
        {
            // One made for the whole screen never goes to a client: it plays
            // on the screen, the way its Play button plays it, under the
            // caller's tag so that stopping that stops it.
            Macro onScreen = MacroPlay.OnScreenInstead(m, times);
            if (onScreen != null) return StartOnScreen(onScreen, MacroPlay.ScreenTag(tag, m.Name));

            MacroJob job = new MacroJob();
            job.Macro = m.Copy();
            job.Pid = pid;
            job.Label = label;
            job.Times = Math.Max(1, times);
            job.Tag = tag;
            job.UserIsPlaying = userIsPlaying;
            job.TurnMs = turnMs;
            // Before it is offered: the queue's thread may build its runner
            // the moment it is.
            job.StepsAside = stepsAside;

            // A session already going on that client is the same one: it is
            // between turns, so the queue alone would not see it.
            lock (sessions)
                foreach (MacroJob s in sessions)
                    if (s.Pid == pid && !s.Cancelled && string.Equals(s.Macro.Name, m.Name, StringComparison.OrdinalIgnoreCase))
                        return null;

            // One that plays to its end, started by someone who may want to
            // know it is still going, or to stop just it - a macro's key, a
            // Play button. Listed before it is offered: the queue may play it
            // and be done with it before this line would come round again.
            bool listed = tag != null && !job.Macro.IsSession;
            if (listed) lock (sessions) tagged.Add(job);

            if (!macroQueue.Offer(job))
            {
                if (listed) lock (sessions) tagged.Remove(job);
                // Already waiting for that client is the queue working, not
                // worth a line. Only a full queue is.
                if (macroQueue.Waiting >= MacroQueue.MAX_WAITING)
                    Log("Didn't play " + m.Name + " on " + job.Label + ": " + MacroQueue.MAX_WAITING
                        + " macros are already waiting their turn.");
                return null;
            }

            if (job.Macro.IsSession)
            {
                lock (sessions) sessions.Add(job);
                SessionsChanged();
                Log(m.Name + " started on " + job.Label + " - it goes on until you press "
                    + MacroKeys.Name(settings.StopKey) + " or bring another window to the front.");
            }

            lock (pumpGate)
            {
                if (pumping) return job;
                pumping = true;
            }
            Thread t = new Thread(PumpMacros);
            t.IsBackground = true;
            t.Name = "Macros";
            t.Start();
            return job;
        }

        // One at a time, off the UI thread: a macro sleeps for as long as it
        // plays. A session plays a turn and goes to the back of the queue, so
        // several take turns and anything else waiting gets in between.
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
                if (j.Cancelled) { SessionOver(j, null, true); continue; }

                if (j.Run == null)
                {
                    // Several times is the steps over again, in one turn at the
                    // front rather than handing focus back and forth between.
                    // Not for a session: it never reaches the end of the first.
                    // One play, though it still steps aside for the nudge, below.
                    Macro play = j.Macro;
                    if (j.Times > 1 && !j.Macro.IsSession)
                    {
                        play = j.Macro.Copy();
                        for (int i = 1; i < j.Times; i++)
                            foreach (MacroStep s in j.Macro.Steps) play.Steps.Add(s.Copy());
                    }
                    j.Run = new MacroRunner(play);
                    j.Run.TurnMs = j.TurnMs;
                    // Still one play, but it takes turns with the keyboard: a
                    // rule can play a macro twenty times over, and that one
                    // long play would keep the nudge out until it gave up.
                    j.Run.StepsAside = StepsAsideFor(j);
                }

                MacroJob job = j;
                PlayOptions o = new PlayOptions();
                o.Stop = delegate { return job.Cancelled; };
                o.OthersWaiting = delegate { return macroQueue.Waiting > 0; };
                o.PointerIsTheUsers = j.UserIsPlaying;
                WatchWork work = watchWork;
                o.Regions = work.Regions;
                foreach (WatchedClient c in work.Clients)
                    if (c != null && c.Pid == j.Pid) o.PlaceId = c.PlaceId;

                string problem;
                RunEnd end;
                try { end = MacroPlayer.PlayTurn(j.Run, WindowCapture.GameWindow(j.Pid), o, out problem); }
                catch (Exception ex) { end = RunEnd.Failed; problem = ex.Message; }

                if (end == RunEnd.Yielded && !j.Cancelled)
                {
                    macroQueue.Requeue(j);
                    MacroPlayer.LetWaitersIn();
                    continue;
                }
                SessionOver(j, problem, end == RunEnd.Yielded || end == RunEnd.Cancelled);
            }
        }

        // Whether a job's play takes turns with the keyboard: when it was told
        // to, or when it is the macro several times over.
        internal static bool StepsAsideFor(MacroJob j)
        {
            return j.StepsAside || j.Times > 1;
        }

        // A job has played its last: said in the activity list, and a
        // session taken off the list of what is running.
        void SessionOver(MacroJob j, string problem, bool stoppedOnPurpose)
        {
            bool wasSession;
            lock (sessions)
            {
                wasSession = sessions.Remove(j);
                tagged.Remove(j);
            }
            // Dropped between turns, it never gets the turn that would say
            // it no longer stands aside.
            if (j.Run != null) MacroPlayer.BackFromAside(j.Run.Before);

            string said;
            if (wasSession || j.Macro.IsSession)
            {
                int rounds = j.Run == null ? 0 : j.Run.Rounds;
                string after = rounds == 1 ? " after 1 round" : " after " + rounds + " rounds";
                said = stoppedOnPurpose || problem == null
                    ? j.Macro.Name + " stopped on " + j.Label + after + "."
                    : j.Macro.Name + " on " + j.Label + " ended" + after + ": " + problem + ".";
            }
            else
            {
                // Stopped between turns, or before its first, comes here with
                // nothing to say - and was not played.
                if (problem == null && stoppedOnPurpose) problem = MacroPlayer.CANCELLED;
                said = problem == null
                    ? "Played " + j.Macro.Name + (j.Times > 1 ? " " + j.Times + " times" : "") + " on " + j.Label + "."
                    : j.Macro.Name + " on " + j.Label + " didn't finish: " + problem + ".";
            }

            OnUi(delegate
            {
                Log(said);
                if (wasSession) SessionsChanged();
            });
        }

        // ---------- sessions: macros that go on until stopped ----------

        // Playing, or waiting their turn. Macros playing anywhere on the
        // screen are here too, with no client, however long they last.
        readonly List<MacroJob> sessions = new List<MacroJob>();

        // Macros that play to their end on a client, waiting or playing,
        // started under a tag - see IsPlaying. Guarded by `sessions`' lock.
        readonly List<MacroJob> tagged = new List<MacroJob>();

        // Lua scripts running count too: the stop key is held while they
        // run, and the tray's "Stop macros" stops them.
        public int SessionsRunning
        {
            get
            {
                int n;
                lock (sessions) n = sessions.Count;
                return n + ScriptsCount;
            }
        }

        bool AnySession(string tag)
        {
            lock (sessions)
                foreach (MacroJob j in sessions) if (!j.Cancelled && j.Tag == tag) return true;
            return false;
        }

        // Switches off every session started under one tag. How many.
        int StopSessions(string tag)
        {
            int n = 0;
            lock (sessions)
                foreach (MacroJob j in sessions)
                    if (!j.Cancelled && j.Tag == tag) { j.Cancelled = true; n++; }
            return n;
        }

        // The stop key, the tray, the Macros window: everything that is
        // playing stops now, and nothing that was waiting plays.
        public void StopEverything()
        {
            int n;
            lock (sessions)
            {
                n = sessions.Count;
                foreach (MacroJob j in sessions) j.Cancelled = true;
                // Clearing the queue below never tells these they are over.
                foreach (MacroJob j in tagged) j.Cancelled = true;
                tagged.Clear();
            }
            bool playing = MacroPlayer.Playing;
            int waiting = macroQueue.Waiting;
            // Each script says how its run went as it ends.
            n += StopAllScripts();
            MacroStop.All();
            macroQueue.Clear();
            // Sessions that were between turns never reach the player again.
            lock (sessions) sessions.Clear();
            SessionsChanged();
            if (n > 0 || playing || waiting > 0) Log("Stopped everything that was playing.");
        }

        // Called on any thread whenever a session starts or ends.
        void SessionsChanged()
        {
            OnUi(delegate
            {
                int n = SessionsRunning;
                // Your own idle time, not the session's key presses, decides
                // whether you are away - see RealInput.
                if (n > 0 && !RealInput.Active) RealInput.Start(NudgePolicy.UserIdleFor());
                else if (n == 0 && RealInput.Active) RealInput.Stop();
                RefreshStopKey();
            });
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
