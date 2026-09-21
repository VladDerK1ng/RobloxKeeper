using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;

namespace RobloxKeeper
{
    // Taking a picture of one client. One method, so the engine can be run
    // against a script instead of a game.
    interface ICapture
    {
        // Null when there is nothing to take a picture of; state says why.
        Pixels Grab(int pid, out WatchState state);
    }

    // Reading the writing in a picture: one line per line of text, "" for none.
    interface IReadText
    {
        string Read(Pixels image);
    }

    // The real ones: a Roblox window, and the recogniser built into Windows.
    class LiveCapture : ICapture
    {
        public Pixels Grab(int pid, out WatchState state)
        {
            IntPtr hwnd = WindowCapture.GameWindow(pid);
            state = WindowCapture.StateOf(hwnd);
            return state == WatchState.Watchable ? WindowCapture.Grab(hwnd) : null;
        }
    }

    class LiveReader : IReadText
    {
        public string Read(Pixels image) { return ScreenText.Read(image); }
    }

    // A client as the engine sees it: which window, whose account, which server.
    class WatchedClient
    {
        public int Pid;
        public string Label;          // "Client 3"
        public string AccountName;    // null for a client we did not launch
        public string PlaceId;        // null until its log says which game
        public string JobId;
    }

    // Everything one pass works from. Handed over as a snapshot so the UI can
    // change its own lists while a pass is running without either side
    // waiting on the other.
    class WatchWork
    {
        public WatchedClient[] Clients = new WatchedClient[0];
        public Watcher[] Watchers = new Watcher[0];
        public WatchRegion[] Regions = new WatchRegion[0];
    }

    // What one watcher made of one picture, before anything decides whether to
    // tell anyone. The editor's test button shows this as it is.
    class WatchCheck
    {
        public WatchState State = WatchState.Watchable;
        public bool Seen;
        public string Text;        // what was read, for text and chat watchers
        public string Matched;     // the word, or the picture's name
        public double Score;       // for a picture watcher
        public Pixels Crop;        // what was looked at
        public string Problem;     // why it could not look, or null
    }

    // How often a pass runs.
    //
    // Four a second is the target: that is what makes a banner shown for three
    // seconds a certainty rather than a coin flip. But a pass costs what it
    // costs - five clients with three watchers each is about 260ms - and a
    // fixed timer would queue passes up behind each other and pin a core. So
    // the pace follows the passes. A pass may use half its interval; when one
    // takes longer the interval grows until it fits, and when they get cheap
    // again it shrinks back. Either way the watch thread is idle at least half
    // the time.
    class PassPacer
    {
        public const int FastestMs = 250;
        public const int SlowestMs = 4000;

        int intervalMs = FastestMs;

        public int IntervalMs { get { return intervalMs; } }

        // Feed in how long the pass that just finished took; get back how long
        // to wait before starting the next one.
        public int After(int passMs)
        {
            if (passMs < 0) passMs = 0;

            if (passMs * 2 > intervalMs)
                intervalMs = Math.Max(intervalMs * 3 / 2, passMs * 2);
            else if (passMs * 4 < intervalMs)
                intervalMs = intervalMs * 4 / 5;

            if (intervalMs < FastestMs) intervalMs = FastestMs;
            if (intervalMs > SlowestMs) intervalMs = SlowestMs;

            return Math.Max(0, intervalMs - passMs);
        }

        // The pace in words, for the watchers list.
        public static string Describe(int intervalMs)
        {
            if (intervalMs <= 0) intervalMs = FastestMs;
            if (intervalMs <= 1000)
            {
                double perSecond = Math.Round(1000.0 / intervalMs, 1);
                return perSecond == 1 ? "1 scan a second" : perSecond.ToString("0.#") + " scans a second";
            }
            double seconds = Math.Round(intervalMs / 1000.0, 1);
            return "1 scan every " + seconds.ToString("0.#") + " seconds";
        }
    }

    // The pass: take one picture of each watched client, run every watcher
    // assigned to it off that one picture, and raise what turns up.
    //
    // A capture costs about 22ms and reading a region about 10ms, so the
    // picture is taken once per client and shared - five clients with three
    // watchers each is five captures, not fifteen - and watchers on the same
    // box share one read of it. A client no enabled watcher is assigned to is
    // never captured at all.
    //
    // Pass is synchronous and takes everything it needs as arguments, which is
    // what lets it be tested against fakes. Start runs it on one background
    // thread at the pace PassPacer sets. Found and Problem are raised on that
    // thread, so whatever handles them must hand off to the UI with BeginInvoke
    // - Invoke would deadlock against Stop.
    class WatchEngine
    {
        // How much a projected region grows before it is read, to absorb the
        // drift no formula predicts. About a millisecond of extra reading.
        public const double WIDEN = 0.25;

        readonly ICapture capture;
        readonly IReadText reader;

        readonly object gate = new object();          // one pass or check at a time
        readonly object shownGate = new object();     // what the UI reads mid-pass

        // Per client, per watcher: its own sighting count and chat history.
        readonly Dictionary<int, Dictionary<Watcher, Slot>> slots =
            new Dictionary<int, Dictionary<Watcher, Slot>>();
        // Which server each client was in last pass; moving starts it over.
        readonly Dictionary<int, string> whereWas = new Dictionary<int, string>();
        readonly Dictionary<string, Pixels> images =
            new Dictionary<string, Pixels>(StringComparer.OrdinalIgnoreCase);

        readonly Dictionary<int, WatchState> states = new Dictionary<int, WatchState>();
        readonly Dictionary<Watcher, Dictionary<int, string>> problems =
            new Dictionary<Watcher, Dictionary<int, string>>();

        readonly PassPacer pacer = new PassPacer();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Thread thread;
        volatile bool running;

        public Action<DetectionEvent> Found;
        public Action<Watcher, WatchedClient, string> Problem;

        // Turns a stored picture name into pixels. Replaced in tests.
        public Func<string, Pixels> LoadImage = LoadFromTemplates;

        class Slot
        {
            public FireControl Fire;
            public ChatFeed Chat;
            public bool ChatPrimed;
            public string Problem;
        }

        public WatchEngine(ICapture capture, IReadText reader)
        {
            this.capture = capture;
            this.reader = reader;
        }

        public bool Running { get { return running; } }

        // The current pace, for the watchers list.
        public int IntervalMs { get { return pacer.IntervalMs; } }

        // ---------- the pass ----------

        public void Pass(IList<WatchedClient> clients, IList<Watcher> watchers,
                         IList<WatchRegion> regions, DateTime now)
        {
            lock (gate)
            {
                Prune(clients, watchers);

                foreach (WatchedClient c in clients)
                {
                    if (c == null) continue;

                    List<Watcher> mine = AssignedTo(c, watchers);
                    if (mine.Count == 0)
                    {
                        lock (shownGate) states.Remove(c.Pid);
                        continue;
                    }

                    StartOverIfMoved(c);

                    WatchState state;
                    Pixels shot = capture.Grab(c.Pid, out state);
                    lock (shownGate) states[c.Pid] = state;
                    if (shot == null || shot.IsEmpty) continue;

                    Dictionary<Rectangle, string> readThisPass = new Dictionary<Rectangle, string>();
                    foreach (Watcher w in mine) Run(w, c, shot, regions, readThisPass, now);
                }
            }
        }

        static List<Watcher> AssignedTo(WatchedClient c, IList<Watcher> watchers)
        {
            List<Watcher> mine = new List<Watcher>();
            foreach (Watcher w in watchers)
                if (w != null && w.Enabled && w.RunsOn(c.AccountName)) mine.Add(w);
            return mine;
        }

        void Run(Watcher w, WatchedClient c, Pixels shot, IList<WatchRegion> regions,
                 Dictionary<Rectangle, string> readThisPass, DateTime now)
        {
            Slot slot = SlotFor(c.Pid, w);

            WatchCheck k;
            try { k = Look(w, c, shot, regions, readThisPass); }
            catch (Exception ex)
            {
                // One watcher failing must not take the others on this client
                // down with it.
                k = new WatchCheck();
                k.Problem = "Couldn't check this just now: " + ex.Message;
            }

            SetProblem(slot, w, c, k.Problem);
            if (k.Problem != null) return;

            if (w.Kind == WatchKind.ChatLine) { RunChat(w, c, slot, k, now); return; }
            if (slot.Fire.Update(k.Seen, now)) Raise(w, c, k, null, now);
        }

        void RunChat(Watcher w, WatchedClient c, Slot slot, WatchCheck k, DateTime now)
        {
            IList<string> fresh = slot.Chat.NewLines(k.Text);

            // Whatever is in the box the first time it is read was said before
            // anyone was looking. Reporting it would send a burst of old
            // messages every time the app starts or a client changes server.
            if (!slot.ChatPrimed) { slot.ChatPrimed = true; return; }

            // Every new line is its own piece of news, so chat is not held back
            // by the confirm count or the cooldown.
            foreach (string line in fresh)
                if (ChatWanted(w, line)) Raise(w, c, k, line, now);
        }

        static bool ChatWanted(Watcher w, string line)
        {
            if (string.IsNullOrEmpty(w.ChatContains) || w.ChatContains.Trim().Length == 0) return true;
            return line.IndexOf(w.ChatContains.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Raise(Watcher w, WatchedClient c, WatchCheck k, string line, DateTime now)
        {
            DetectionEvent d = new DetectionEvent();
            d.WatcherName = w.Name;
            d.AccountName = c.AccountName;
            d.ClientLabel = c.Label;
            d.PlaceId = c.PlaceId;
            d.JobId = c.JobId;
            d.Matched = line == null ? k.Matched : BlankToNull(w.ChatContains);
            d.Line = line;
            d.Score = k.Score;
            d.When = now;
            d.Crop = k.Crop;

            Action<DetectionEvent> found = Found;
            if (found != null) found(d);
        }

        // ---------- looking ----------

        // What one watcher sees in one picture. Shared by the pass and by the
        // editor's test button, so the button tests exactly what runs.
        WatchCheck Look(Watcher w, WatchedClient c, Pixels shot, IList<WatchRegion> regions,
                        Dictionary<Rectangle, string> readThisPass)
        {
            WatchCheck k = new WatchCheck();
            Rectangle whole = new Rectangle(0, 0, shot.Width, shot.Height);
            Rectangle box = whole;

            if (!w.WholeWindow)
            {
                WatchRegion r = WatchStore.FindIn(regions, c.PlaceId, w.RegionName);
                if (r == null)
                {
                    // Reading the whole window instead would cost nine times as
                    // much and match things outside the box that was drawn.
                    k.Problem = string.IsNullOrEmpty(c.PlaceId)
                        ? "Waiting to see which game this client is in."
                        : "No box called \"" + w.RegionName + "\" has been drawn for this game yet.";
                    return k;
                }
                box = WatchRegion.Widen(r.Project(shot.Width, shot.Height), WIDEN, shot.Width, shot.Height);
            }

            k.Crop = box == whole ? shot : shot.Crop(box);

            if (w.Kind == WatchKind.ImageFound)
            {
                LookForPicture(w, k);
                return k;
            }

            string text;
            if (!readThisPass.TryGetValue(box, out text))
            {
                text = reader.Read(k.Crop) ?? "";
                readThisPass[box] = text;
            }
            k.Text = text;

            if (w.Kind == WatchKind.TextAppears)
            {
                k.Matched = w.Rule == null ? null : w.Rule.FirstHit(text);
                k.Seen = k.Matched != null;
            }
            else k.Seen = text.Length > 0;
            return k;
        }

        void LookForPicture(Watcher w, WatchCheck k)
        {
            if (string.IsNullOrEmpty(w.TemplateFile))
            {
                k.Problem = "No picture has been chosen for this to look for.";
                return;
            }

            Pixels template = Image(w.TemplateFile);
            if (template == null || template.IsEmpty)
            {
                k.Problem = "Couldn't open the picture it looks for (" + Path.GetFileName(w.TemplateFile) + ").";
                return;
            }
            k.Matched = Path.GetFileNameWithoutExtension(w.TemplateFile);

            // Otherwise it could never match, which would look exactly like the
            // picture simply not being there.
            if (template.Width > k.Crop.Width || template.Height > k.Crop.Height)
            {
                k.Problem = "The box is smaller than the picture it looks for - draw it bigger.";
                return;
            }

            bool[] mask = null;
            if (!string.IsNullOrEmpty(w.MaskFile))
            {
                Pixels m = Image(w.MaskFile);
                if (m == null || m.Width != template.Width || m.Height != template.Height)
                {
                    k.Problem = "The painted-out copy of the picture no longer fits it - paint it again.";
                    return;
                }
                mask = MaskFrom(m);
            }

            ImageHit hit = ImageMatch.Find(k.Crop, template, mask, w.Tolerance);
            k.Score = hit.Score;
            k.Seen = hit.Found;
        }

        // A mask is the picture with its background painted out: a pixel is
        // compared only where the mask is still visible.
        public static bool[] MaskFrom(Pixels mask)
        {
            bool[] keep = new bool[mask.Argb.Length];
            for (int i = 0; i < keep.Length; i++) keep[i] = ((mask.Argb[i] >> 24) & 0xFF) >= 128;
            return keep;
        }

        Pixels Image(string file)
        {
            Pixels p;
            if (images.TryGetValue(file, out p)) return p;

            // A missing picture is remembered too, so it is not looked for on
            // disk four times a second. ForgetImages clears it once it is fixed.
            p = LoadImage(file);
            images[file] = p;
            return p;
        }

        // Called after a watcher's pictures change.
        public void ForgetImages()
        {
            lock (gate) images.Clear();
        }

        static Pixels LoadFromTemplates(string file)
        {
            try
            {
                string path = WatchStore.TemplatePath(file);
                if (path == null || !File.Exists(path)) return null;

                // Read into memory first: a Bitmap opened on a file keeps it
                // locked, and the editor needs to be able to replace it.
                using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path)))
                using (Bitmap bmp = new Bitmap(ms))
                    return Pixels.FromBitmap(bmp);
            }
            catch { return null; }
        }

        // ---------- the test button ----------

        // One look at one client, right now. Nothing is counted and nothing is
        // sent. Pictures are re-read from disk, because the usual reason to
        // press the button is having just changed them.
        public WatchCheck Check(Watcher w, WatchedClient c, IList<WatchRegion> regions)
        {
            lock (gate)
            {
                if (w.TemplateFile != null) images.Remove(w.TemplateFile);
                if (w.MaskFile != null) images.Remove(w.MaskFile);

                WatchState state;
                Pixels shot = capture.Grab(c.Pid, out state);

                WatchCheck k;
                if (shot == null || shot.IsEmpty)
                {
                    k = new WatchCheck();
                    k.Problem = state == WatchState.Watchable
                        ? "Couldn't take a picture of that client just now."
                        : WindowCapture.Explain(state);
                }
                else
                {
                    try { k = Look(w, c, shot, regions, new Dictionary<Rectangle, string>()); }
                    catch (Exception ex)
                    {
                        k = new WatchCheck();
                        k.Problem = "Couldn't check this just now: " + ex.Message;
                    }
                }
                k.State = state;
                return k;
            }
        }

        // ---------- what the list shows ----------

        public bool TryStateOf(int pid, out WatchState state)
        {
            lock (shownGate) return states.TryGetValue(pid, out state);
        }

        // Why a watcher cannot run, on any client, or null when it can.
        public string ProblemFor(Watcher w)
        {
            lock (shownGate)
            {
                Dictionary<int, string> byPid;
                if (!problems.TryGetValue(w, out byPid)) return null;
                foreach (string p in byPid.Values) return p;
                return null;
            }
        }

        // ---------- bookkeeping ----------

        Slot SlotFor(int pid, Watcher w)
        {
            Dictionary<Watcher, Slot> mine;
            if (!slots.TryGetValue(pid, out mine))
            {
                mine = new Dictionary<Watcher, Slot>();
                slots[pid] = mine;
            }

            Slot s;
            if (!mine.TryGetValue(w, out s))
            {
                s = new Slot();
                s.Fire = w.NewFireControl();
                s.Chat = new ChatFeed();
                mine[w] = s;
            }
            return s;
        }

        // A different game or server is a different chat and a different
        // screen, so what was counted in the last one does not carry over.
        void StartOverIfMoved(WatchedClient c)
        {
            string where = (c.PlaceId ?? "") + "/" + (c.JobId ?? "");
            string was;
            if (whereWas.TryGetValue(c.Pid, out was) && was != where) slots.Remove(c.Pid);
            whereWas[c.Pid] = where;
        }

        // Said once when it starts, not four times a second while it lasts.
        void SetProblem(Slot slot, Watcher w, WatchedClient c, string problem)
        {
            if (problem == slot.Problem) return;
            slot.Problem = problem;

            lock (shownGate)
            {
                Dictionary<int, string> byPid;
                if (!problems.TryGetValue(w, out byPid))
                {
                    byPid = new Dictionary<int, string>();
                    problems[w] = byPid;
                }
                if (problem == null) byPid.Remove(c.Pid);
                else byPid[c.Pid] = problem;
                if (byPid.Count == 0) problems.Remove(w);
            }

            Action<Watcher, WatchedClient, string> said = Problem;
            if (problem != null && said != null) said(w, c, problem);
        }

        // PIDs are reused by Windows, so nothing may outlive its client; and a
        // deleted watcher's counts go with it.
        void Prune(IList<WatchedClient> clients, IList<Watcher> watchers)
        {
            List<int> alive = new List<int>();
            foreach (WatchedClient c in clients) if (c != null) alive.Add(c.Pid);

            foreach (int pid in Gone(slots.Keys, alive)) slots.Remove(pid);
            foreach (int pid in Gone(whereWas.Keys, alive)) whereWas.Remove(pid);
            foreach (Dictionary<Watcher, Slot> mine in slots.Values)
            {
                List<Watcher> dropped = new List<Watcher>();
                foreach (Watcher w in mine.Keys) if (!watchers.Contains(w)) dropped.Add(w);
                foreach (Watcher w in dropped) mine.Remove(w);
            }

            lock (shownGate)
            {
                foreach (int pid in Gone(states.Keys, alive)) states.Remove(pid);

                List<Watcher> empty = new List<Watcher>();
                foreach (KeyValuePair<Watcher, Dictionary<int, string>> p in problems)
                {
                    if (!watchers.Contains(p.Key)) { empty.Add(p.Key); continue; }
                    foreach (int pid in Gone(p.Value.Keys, alive)) p.Value.Remove(pid);
                    if (p.Value.Count == 0) empty.Add(p.Key);
                }
                foreach (Watcher w in empty) problems.Remove(w);
            }
        }

        static List<int> Gone(IEnumerable<int> keys, List<int> alive)
        {
            List<int> gone = new List<int>();
            foreach (int pid in keys) if (!alive.Contains(pid)) gone.Add(pid);
            return gone;
        }

        static string BlankToNull(string s)
        {
            return string.IsNullOrEmpty(s) || s.Trim().Length == 0 ? null : s.Trim();
        }

        // ---------- the thread ----------

        public void Start(Func<WatchWork> work)
        {
            if (running) return;
            running = true;
            thread = new Thread(delegate() { Loop(work); });
            thread.IsBackground = true;
            thread.Name = "RobloxKeeper watchers";
            // The games come first. If this falls behind, the pace backs off.
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            wake.Set();
            Thread t = thread;
            thread = null;
            if (t != null) t.Join(2000);
        }

        void Loop(Func<WatchWork> work)
        {
            Stopwatch took = new Stopwatch();
            while (running)
            {
                took.Restart();
                try
                {
                    WatchWork now = work();
                    if (now != null) Pass(now.Clients, now.Watchers, now.Regions, DateTime.Now);
                }
                catch
                {
                    // One bad pass must not end watching for the whole session.
                }
                wake.WaitOne(pacer.After((int)took.ElapsedMilliseconds));
            }
        }
    }
}
