using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Lua scripts, wired into the main window: starting and stopping them,
    // their keys, where each plays, and handing what they say to the Scripts
    // window and the activity list. A running script counts as something
    // playing, so the stop key is held while it runs and stops it.
    partial class MainForm : IScriptHost
    {
        ScriptStore scriptStore;
        readonly ScriptPill scriptPill = new ScriptPill();

        // Running now. Guarded by its own lock.
        readonly List<ScriptJob> scriptJobs = new List<ScriptJob>();

        // The Roblox window that was last in front, for a script set to play
        // on "the Roblox window in front" started from one of the app's own
        // windows - which are in front then.
        int lastRobloxFront;

        // One run of one script, and what it has said that the window has not
        // been told yet.
        class ScriptJob
        {
            public LuaRun Run;
            public string Name;
            public string Tag;
            public ScriptTarget Target;
            public DateTime Started = DateTime.Now;
            public bool DiscordSummary;
            public volatile int Line;
            public volatile string Status;
            public readonly List<string> Output = new List<string>();     // under its own lock
            public int Dropped;                                           // printed while the window was behind
            public readonly List<string> Said = new List<string>();       // for the activity list; under Output's lock
            public int SaidDropped;
            public bool LineSent = true, StatusSent = true;
            public bool NoWebhookSaid;
        }

        public ScriptStore Store
        {
            get
            {
                if (scriptStore == null) scriptStore = new ScriptStore(ScriptStore.DefaultDir);
                return scriptStore;
            }
        }

        public event Action<string, string> ScriptOutput;
        public event Action<string, int> ScriptLine;
        public event Action<string, string> ScriptStatus;
        public event Action<string, ScriptResult> ScriptEnded;

        public string Notices { get { return RobloxKeeper.Notices.Text(); } }

        int ScriptsCount { get { lock (scriptJobs) return scriptJobs.Count; } }

        // ---------- starting and stopping ----------

        public const string UNTRUSTED_START = "This script came from someone else. Open it in Scripts, read it, and press 'I trust this script' first.";

        public string StartScript(string name) { return StartScript(name, 0, null); }

        public string StartScript(string name, int pid, string tag)
        {
            if (!started) return "Scripts don't run in this window.";
            string real = Store.Find(name);
            if (real == null) return "There's no script called \"" + name + "\".";
            string code = Store.Load(real);
            if (code == null) return "Couldn't read " + real + ".";
            ScriptSettings s = Store.Settings(real);
            // A shared script could do anything keys and clicks can: it is
            // read before it runs - also from a rule or its key.
            if (!s.Trusted) return UNTRUSTED_START;

            ScriptTarget target;
            string why = TargetFor(s, pid, out target);
            if (why != null) return why;
            lock (scriptJobs)
                foreach (ScriptJob j in scriptJobs)
                    if (string.Equals(j.Name, real, StringComparison.OrdinalIgnoreCase) && j.Target.Same(target))
                        return real + " is already running on " + target.Name + ".";
            if (!LuaRuntime.Ensure() || !LuaRuntime.Packed) return "This build of RobloxKeeper has no Lua in it, so scripts can't run.";

            ScriptJob job = new ScriptJob();
            job.Name = real;
            job.Tag = tag;
            job.Target = target;
            job.DiscordSummary = s.SummaryToDiscord;
            LiveDesk desk = new LiveDesk(delegate { return watchWork.Clients; },
                                         PictureShelf.For(watchStore, null, WatchStore.TemplatesDir));
            job.Run = new LuaRun(real, code, desk, target, new JobOutlet(this, job), Store);
            job.Run.Trusted = s.Trusted;
            lock (scriptJobs) scriptJobs.Add(job);
            SessionsChanged();
            Log(real + " is running on " + target.Name + " - press " + MacroKeys.Name(settings.StopKey) + " to stop it.");
            job.Run.Start(delegate(ScriptResult r) { ScriptOver(job, r); });
            return null;
        }

        // Where a script plays: the window a rule names, or what its own
        // setting says. Null and the target, or why it can't.
        string TargetFor(ScriptSettings s, int pid, out ScriptTarget target)
        {
            target = null;
            WatchedClient[] clients = watchWork.Clients ?? new WatchedClient[0];
            if (pid > 0)
            {
                foreach (WatchedClient c in clients) if (c != null && c.Pid == pid) { target = ScriptTarget.Of(c); return null; }
                return "that Roblox window has closed.";
            }
            if (s.OnScreen) { target = ScriptTarget.OnScreen(); return null; }
            string account = s.Account;
            if (account != null)
            {
                foreach (WatchedClient c in clients)
                    if (c != null && string.Equals(c.AccountName, account, StringComparison.OrdinalIgnoreCase)) { target = ScriptTarget.Of(c); return null; }
                return account + "'s Roblox window isn't open.";
            }
            if (clients.Length == 0) return "Open Roblox first - it plays on the Roblox window in front.";
            int front = PerformanceManager.ForegroundPid();
            foreach (int want in new int[] { front, lastRobloxFront })
                foreach (WatchedClient c in clients)
                    if (c != null && want > 0 && c.Pid == want) { target = ScriptTarget.Of(c); return null; }
            if (clients.Length == 1 && clients[0] != null) { target = ScriptTarget.Of(clients[0]); return null; }
            return "click into the Roblox window you want it on, then start it again.";
        }

        public bool StopScript(string name)
        {
            bool any = false;
            lock (scriptJobs)
                foreach (ScriptJob j in scriptJobs)
                    if (string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase)) { j.Run.Stop(); any = true; }
            return any;
        }

        public bool IsScriptRunning(string name)
        {
            lock (scriptJobs)
                foreach (ScriptJob j in scriptJobs)
                    if (string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public List<ScriptRunInfo> ScriptsRunning()
        {
            List<ScriptRunInfo> list = new List<ScriptRunInfo>();
            lock (scriptJobs)
                foreach (ScriptJob j in scriptJobs)
                {
                    ScriptRunInfo i = new ScriptRunInfo();
                    i.Name = j.Name;
                    i.Where = j.Target.Name;
                    i.Pid = j.Target.Screen ? 0 : j.Target.Pid;
                    i.Tag = j.Tag;
                    i.Line = j.Line;
                    i.Status = j.Status;
                    i.Started = j.Started;
                    list.Add(i);
                }
            return list;
        }

        // Running under a tag - see IsPlaying - and stopping just those,
        // for StopTag: how many were stopped.
        bool ScriptsTagged(string tag)
        {
            if (tag == null) return false;
            lock (scriptJobs)
                foreach (ScriptJob j in scriptJobs) if (j.Tag == tag && !j.Run.StopAsked) return true;
            return false;
        }

        int StopScriptsTagged(string tag)
        {
            int n = 0;
            if (tag == null) return 0;
            lock (scriptJobs)
                foreach (ScriptJob j in scriptJobs) if (j.Tag == tag && !j.Run.StopAsked) { j.Run.Stop(); n++; }
            return n;
        }

        // Stop everything: how many scripts it stopped.
        int StopAllScripts()
        {
            int n = 0;
            lock (scriptJobs)
                foreach (ScriptJob j in scriptJobs) { j.Run.Stop(); n++; }
            return n;
        }

        // On the script's thread, last.
        void ScriptOver(ScriptJob job, ScriptResult r)
        {
            lock (scriptJobs) scriptJobs.Remove(job);
            string summary = r.Summary;
            if (job.DiscordSummary) SendScriptDiscord(job, summary);
            OnUi(delegate
            {
                FlushScripts();
                // On the window's thread, after the last batch: the label is
                // only ever set from here, so none comes back after this.
                scriptPill.Set(job, null, IntPtr.Zero);
                Log(summary);
                Action<string, ScriptResult> ended = ScriptEnded;
                if (ended != null) try { ended(job.Name, r); } catch { }
                SessionsChanged();
            });
        }

        // ---------- what scripts say ----------

        // Called on the script's thread; everything for the window is
        // gathered on the job and handed over on the window's thread.
        class JobOutlet : IScriptOutlet
        {
            readonly MainForm f;
            readonly ScriptJob j;
            public JobOutlet(MainForm f, ScriptJob j) { this.f = f; this.j = j; }

            public void Print(string text)
            {
                lock (j.Output)
                {
                    // A loop printing as fast as it can: the newest are
                    // kept, and how many were left out is said.
                    if (j.Output.Count >= MAX_OUTPUT_WAITING) { j.Output.RemoveAt(0); j.Dropped++; }
                    j.Output.Add(text ?? "");
                }
                f.ScriptsChanged(j);
            }

            public void Line(int line)
            {
                j.Line = line;
                j.LineSent = false;
                f.ScriptsChanged(j);
            }

            // The label over the game is set with the rest, at most every
            // ScriptFlushGate.EVERY_MS: a loop changing it every round would
            // otherwise draw a new one for each.
            public void Status(string text)
            {
                if (text == j.Status) return;
                j.Status = text;
                j.StatusSent = false;
                f.ScriptsChanged(j);
            }

            // Kept with the rest and written to the activity list a batch at
            // a time - never one hand-over to the window per line, which a
            // loop of say() would keep the window busy with.
            public void Say(string text)
            {
                lock (j.Output)
                {
                    if (j.Said.Count >= MAX_SAID_WAITING) { j.Said.RemoveAt(0); j.SaidDropped++; }
                    j.Said.Add(text ?? "");
                }
                f.ScriptsChanged(j);
            }

            public void Notify(string text)
            {
                f.OnUi(delegate
                {
                    try
                    {
                        f.tray.BalloonTipTitle = j.Name;
                        f.tray.BalloonTipText = string.IsNullOrEmpty(text) ? " " : (text.Length > 200 ? text.Substring(0, 197) + "..." : text);
                        f.tray.BalloonTipIcon = ToolTipIcon.Info;
                        f.tray.ShowBalloonTip(5000);
                    }
                    catch { }
                });
            }

            public void Discord(string text) { f.SendScriptDiscord(j, j.Name + ": " + text); }
        }

        // Lines waiting for the window, the newest kept. Each is a line added
        // to a text box on the window's thread, so this many at most every
        // ScriptFlushGate.EVERY_MS keeps the window free in between.
        const int MAX_OUTPUT_WAITING = 100;
        const int MAX_SAID_WAITING = 20;

        readonly List<ScriptJob> scriptsDirty = new List<ScriptJob>();
        ScriptFlushGate scriptsGate;

        ScriptFlushGate ScriptsGate
        {
            get
            {
                lock (scriptsDirty)
                {
                    if (scriptsGate == null) scriptsGate = ScriptFlushGate.Live(delegate { OnUi(FlushScripts); });
                    return scriptsGate;
                }
            }
        }

        // Something for the window: asked for once, however much comes in
        // before the window gets to it, and never sooner than EVERY_MS after
        // the last batch was handed over.
        void ScriptsChanged(ScriptJob j)
        {
            lock (scriptsDirty)
                if (!scriptsDirty.Contains(j)) scriptsDirty.Add(j);
            ScriptsGate.Changed();
        }

        void FlushScripts()
        {
            ScriptFlushGate gate = ScriptsGate;
            gate.Begin();
            try { FlushScriptsNow(); }
            finally { gate.End(); }
        }

        void FlushScriptsNow()
        {
            List<ScriptJob> dirty;
            lock (scriptsDirty)
            {
                dirty = new List<ScriptJob>(scriptsDirty);
                scriptsDirty.Clear();
            }
            foreach (ScriptJob j in dirty)
            {
                List<string> lines, said;
                int dropped, saidDropped;
                lock (j.Output)
                {
                    lines = new List<string>(j.Output);
                    j.Output.Clear();
                    dropped = j.Dropped;
                    j.Dropped = 0;
                    said = new List<string>(j.Said);
                    j.Said.Clear();
                    saidDropped = j.SaidDropped;
                    j.SaidDropped = 0;
                }
                Action<string, string> output = ScriptOutput;
                if (output != null)
                {
                    if (dropped > 0) try { output(j.Name, "(" + dropped + " more lines not shown)"); } catch { }
                    foreach (string l in lines) try { output(j.Name, l); } catch { }
                }
                if (saidDropped > 0) Log("(" + saidDropped + " more lines from " + j.Name + " left out)");
                foreach (string l in said) Log(j.Name + ": " + l);
                if (!j.StatusSent)
                {
                    // Still running: its label shows what it said. Ended:
                    // the label went with it, and is not brought back.
                    bool running;
                    lock (scriptJobs) running = scriptJobs.Contains(j);
                    if (running) scriptPill.Set(j, j.Status, j.Target.Screen ? IntPtr.Zero : WindowCapture.GameWindow(j.Target.Pid));
                }
                if (!j.LineSent)
                {
                    j.LineSent = true;
                    Action<string, int> line = ScriptLine;
                    if (line != null) try { line(j.Name, j.Line); } catch { }
                }
                if (!j.StatusSent)
                {
                    j.StatusSent = true;
                    Action<string, string> status = ScriptStatus;
                    if (status != null) try { status(j.Name, j.Status); } catch { }
                }
            }
        }

        // To the watchers' webhook, off this thread, as plain text. Mentions
        // are switched off: a script can't ping @everyone.
        void SendScriptDiscord(ScriptJob job, string text)
        {
            string url = watchStore == null ? null : watchStore.WebhookUrl;
            if (string.IsNullOrEmpty(url))
            {
                if (!job.NoWebhookSaid)
                {
                    job.NoWebhookSaid = true;
                    string name = job.Name;
                    OnUi(delegate { Log(name + " wanted to send to Discord, but no Discord webhook is set - add one in Watchers."); });
                }
                return;
            }
            WebhookRequest r = new WebhookRequest();
            r.Url = url;
            r.ContentType = "application/json";
            r.Body = Encoding.UTF8.GetBytes("{\"content\":\"" + WebhookPost.Escape(text) + "\",\"allowed_mentions\":{\"parse\":[]}}");
            string who = job.Name;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string failed;
                try
                {
                    failed = WebhookPost.SendWithRetry(delegate { return WebhookPost.Send(r); }, WebhookPost.RetryDelaysMs,
                                                       delegate(int ms) { Thread.Sleep(ms); });
                }
                catch { failed = "couldn't send it"; }
                if (failed != null) OnUi(delegate { Log("Couldn't send " + who + "'s message to Discord: " + failed + "."); });
            });
        }

        // ---------- keys ----------

        public string ScriptKeyProblem(byte vk, string name)
        {
            string why = KeyUseProblem(vk, KeyUse.SCRIPT, name);
            if (why != null || !Extras.Loaded) return why;
            // The extras' own keys are not in the one check - they refuse
            // everyone else's through it - so a script looks at them here.
            ExtrasSettings x = Extras.Current;
            string key = MacroKeys.Name(vk);
            if (x.CrossKey != 0 && vk == x.CrossKey) return key + " switches the crosshair. Choose another.";
            if (x.ZoomOn && x.ZoomKey != 0 && vk == x.ZoomKey) return key + " is the zoom key. Choose another.";
            if (x.HoldOn && vk == (x.HoldWith != 0 ? x.HoldWith : x.HoldKey)) return key + " holds a key for you. Choose another.";
            return null;
        }

        public void ScriptsSaved()
        {
            RefreshGlobalKeys();
        }

        // ---------- trust ----------

        public bool IsScriptTrusted(string name)
        {
            string real = Store.Find(name);
            return real == null || Store.Settings(real).Trusted;
        }

        public void TrustScript(string name)
        {
            string real = Store.Find(name);
            if (real == null) return;
            ScriptSettings s = Store.Settings(real);
            if (s.Trusted) return;
            s.Trusted = true;
            Store.SaveSettings(real, s);
        }

        // A key went down. True when it is a script's own key, whatever that
        // then did: pressed again, it stops it.
        bool ScriptKeyHeard(int key)
        {
            if ((key >> 8) != 0 || scriptStore == null) return false;
            string name = scriptStore.OnKey((byte)(key & 0xFF));
            if (name == null) return false;
            if (StopScript(name)) return true;
            // While recording, what is pressed is being recorded, not meant.
            if (MacroRecorder.Recording) return true;
            string problem = StartScript(name, 0, "key:" + name);
            if (problem != null) WarnOverGame(name, problem);
            return true;
        }

        // ---------- the scripts themselves ----------

        public string RenameScript(string from, string to)
        {
            if (IsScriptRunning(from)) return "Stop " + from + " first.";
            string why = Store.Rename(from, to);
            if (why == null) RefreshGlobalKeys();
            // Rules name the script they run; a rename carries them along, as it
            // does for macros.
            if (why == null && watchStore != null && watchStore.RenameScriptUses(from, to) > 0) watchStore.Save();
            return why;
        }

        public string RemoveScript(string name)
        {
            if (IsScriptRunning(name)) return "Stop " + name + " first.";
            if (!Store.Remove(name)) return "Couldn't remove " + name + ".";
            RefreshGlobalKeys();
            // Its rules stay, marked "Needs attention", rather than silently running nothing.
            if (watchStore != null && watchStore.RenameScriptUses(name, null) > 0) watchStore.Save();
            return null;
        }

        public string MacroAsLua(Macro m)
        {
            PictureShelf shelf = PictureShelf.For(watchStore, m == null ? null : m.Steps, WatchStore.TemplatesDir);
            return ScriptConvert.ToLua(m, shelf);
        }

        public string ExportScript(string name, string zipPath)
        {
            return ScriptPack.Export(zipPath, Store, name, WatchStore.TemplatesDir,
                                     PictureShelf.For(watchStore, null, WatchStore.TemplatesDir));
        }

        public ScriptImport ImportScript(string zipPath)
        {
            ScriptImport r = ScriptPack.Import(zipPath, Store, WatchStore.TemplatesDir,
                                               PictureShelf.For(watchStore, null, WatchStore.TemplatesDir),
                                               delegate(byte vk) { return ScriptKeyProblem(vk, null); });
            if (r.Name != null)
            {
                if (watchEngine != null) watchEngine.ForgetImages();
                RefreshGlobalKeys();
            }
            return r;
        }

        // Once a second, from the watch tick: which Roblox window is in
        // front, and the status labels kept over their windows.
        void ScriptsTick()
        {
            int front = PerformanceManager.ForegroundPid();
            foreach (WatchedClient c in watchWork.Clients ?? new WatchedClient[0])
                if (c != null && c.Pid == front) { lastRobloxFront = front; break; }
            if (ScriptsCount > 0) scriptPill.Follow();
        }
    }

    // When what scripts said is handed to the window: once for however much
    // comes in, and never sooner than EVERY_MS after the last hand-over
    // ended. The window's thread must be left free in between: Windows only
    // delivers the nudge's timer and the stop key when nothing else is
    // waiting for it, so a hand-over asked for straight after every one
    // would keep both out for as long as a script prints in a loop.
    class ScriptFlushGate
    {
        public const int EVERY_MS = 100;

        readonly object gate = new object();
        readonly Action post;           // puts one hand-over on the window's thread
        readonly Func<long> now;
        readonly Action<int> later;     // calls post after this many ms, from any thread
        bool asked, flushing, again;
        long lastEnd = long.MinValue / 2;

        public ScriptFlushGate(Action post, Func<long> now, Action<int> later)
        {
            this.post = post;
            this.now = now;
            this.later = later;
        }

        // The real one: the real clock, and a timer for "later".
        public static ScriptFlushGate Live(Action post)
        {
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            System.Threading.Timer timer = null;
            object timerGate = new object();
            return new ScriptFlushGate(post, delegate { return clock.ElapsedMilliseconds; }, delegate(int ms)
            {
                lock (timerGate)
                {
                    if (timer == null) timer = new System.Threading.Timer(delegate { try { post(); } catch { } });
                    timer.Change(ms, Timeout.Infinite);
                }
            });
        }

        // From any thread: something new is waiting.
        public void Changed()
        {
            long wait;
            lock (gate)
            {
                // Being handed over now: one more is asked for when it ends.
                if (flushing) { again = true; return; }
                if (asked) return;
                asked = true;
                wait = EVERY_MS - (now() - lastEnd);
            }
            if (wait <= 0) post();
            else later((int)Math.Min(wait, EVERY_MS));
        }

        // On the window's thread, around a hand-over.
        public void Begin()
        {
            lock (gate) { flushing = true; asked = false; }
        }

        public void End()
        {
            bool more;
            lock (gate)
            {
                flushing = false;
                lastEnd = now();
                more = again;
                again = false;
                if (more) asked = true;
            }
            if (more) later(EVERY_MS);
        }
    }
}
