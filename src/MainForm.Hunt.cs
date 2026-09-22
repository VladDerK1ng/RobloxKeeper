using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Hunt mode, wired into the main window: the one-second tick drives each
    // account's Hunt, hops run on a worker, and joins and finds are fed in
    // from the log watch and the watchers.
    partial class MainForm : IHuntControl
    {
        readonly Dictionary<string, Hunt> hunts = new Dictionary<string, Hunt>(StringComparer.OrdinalIgnoreCase);
        // Each hunting account's client. Kept here rather than read back from
        // the client labels, because a client just started has no window yet.
        readonly Dictionary<string, int> huntPids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly HopHistory hopHistory = new HopHistory();
        readonly Random hopRandom = new Random();
        string huntPlace;

        bool IHuntControl.Running
        {
            get
            {
                foreach (Hunt h in hunts.Values) if (h.Active) return true;
                return false;
            }
        }

        string IHuntControl.Start(HuntSettings s)
        {
            if (watchStore == null) return "Watching hasn't started yet.";
            EnsureAccounts();
            string place = RobloxAuth.PlaceIdFromUrl(s.GameLink);
            string problem = Hunting.StartProblem(place, s.Accounts,
                delegate(string a) { RobloxAccount acc = accounts.Find(a); return acc != null && acc.IsUsable; },
                delegate(string a)
                {
                    foreach (Watcher w in watchStore.Watchers) if (w.Enabled && w.RunsOn(a)) return true;
                    return false;
                });
            if (problem != null) return problem;

            StopHunts(false);
            huntPlace = place;
            foreach (string a in s.Accounts)
            {
                Hunt h = new Hunt(a);
                h.LookSeconds = Math.Max(10, s.LookSeconds);
                h.StayWhenFound = s.StayWhenFound;
                hunts[a] = h;
                huntPids[a] = PidOfAccount(a);
            }
            Log("Hunting with " + string.Join(", ", s.Accounts) + " - " + s.LookSeconds
                + " seconds a server" + (s.StayWhenFound ? ", staying wherever a watcher finds something." : "."));
            UpdateWatchStatus();
            return null;
        }

        void IHuntControl.Stop() { StopHunts(true); }

        void StopHunts(bool say)
        {
            bool any = hunts.Count > 0;
            foreach (Hunt h in hunts.Values) h.Stop();
            hunts.Clear();
            huntPids.Clear();
            if (say && any) Log("Hunt stopped. Every client stays where it is.");
            UpdateWatchStatus();
        }

        IList<string> IHuntControl.Lines(DateTime now)
        {
            List<string> lines = new List<string>();
            foreach (Hunt h in hunts.Values) lines.Add(h.Account + " - " + h.Describe(now));
            return lines;
        }

        int PidOfAccount(string account)
        {
            foreach (ClientInfo c in lastClients)
                if (string.Equals(clientLabels.NameFor(c.Pid), account, StringComparison.OrdinalIgnoreCase)) return c.Pid;
            return 0;
        }

        // Once a second, with every Roblox process alive now.
        void HuntTick(List<int> alivePids)
        {
            if (hunts.Count == 0) return;
            DateTime now = DateTime.Now;
            foreach (Hunt h in new List<Hunt>(hunts.Values))
            {
                int pid;
                if (huntPids.TryGetValue(h.Account, out pid) && pid > 0)
                {
                    if (!alivePids.Contains(pid)) huntPids[h.Account] = 0;              // closed by someone
                    else if (clientLabels.NameFor(pid) == null) clientLabels.Assign(pid, h.Account);
                }
                if (h.Tick(now) == HuntOrder.Hop) StartHop(h, now);
            }
        }

        void StartHop(Hunt h, DateTime now)
        {
            RobloxAccount acc = accounts == null ? null : accounts.Find(h.Account);
            if (acc == null || !acc.IsUsable) { h.HopFailed("no saved sign-in for this account", now); return; }

            // Not where it is, not where it has been lately, not where another
            // of our clients is. Worked out here, on the UI thread, and handed
            // over as a plain set.
            Dictionary<string, bool> avoid = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(h.JobId)) avoid[h.JobId] = true;
            foreach (RobloxLogEvent e in clientWhere.Values) if (!string.IsNullOrEmpty(e.JobId)) avoid[e.JobId] = true;
            foreach (string id in hopHistory.RecentIds(now)) avoid[id] = true;

            HopRequest r = new HopRequest();
            r.AccountName = acc.Name;
            r.Cookie = acc.Cookie;
            r.TrackerId = acc.BrowserTrackerId;
            r.PlaceId = huntPlace;
            r.CurrentPid = huntPids.ContainsKey(h.Account) ? huntPids[h.Account] : 0;
            r.Avoid = delegate(string id) { return avoid.ContainsKey(id); };
            Random rng = new Random(hopRandom.Next());

            ThreadPool.QueueUserWorkItem(delegate
            {
                HopResult res;
                try { res = Hopper.Hop(r, new LiveHopWorld(), rng); }
                catch (Exception ex) { res = new HopResult(); res.Problem = ex.Message; }
                OnUi(delegate { HopDone(h, res); });
            });
        }

        void HopDone(Hunt h, HopResult res)
        {
            Hunt current;
            if (!hunts.TryGetValue(h.Account, out current) || !ReferenceEquals(current, h)) return;   // stopped meanwhile
            DateTime now = DateTime.Now;
            if (res.Problem != null)
            {
                h.HopFailed(res.Problem, now);
                Log(h.Account + " couldn't move to another server: " + res.Problem + ".");
                if (h.Stage == HuntStage.Stopped) Log("Hunt with " + h.Account + " stopped: " + h.Describe(now) + ".");
                return;
            }
            huntPids[h.Account] = res.Pid;
            clientLabels.Assign(res.Pid, h.Account);
            hopHistory.Visited(res.JobId, now);
            h.HopStarted(now);
        }

        // From the log watch: some client joined a server.
        void HuntJoined(int pid, RobloxLogEvent e)
        {
            foreach (Hunt h in hunts.Values)
            {
                int mine;
                if (!huntPids.TryGetValue(h.Account, out mine) || mine != pid) continue;
                hopHistory.Visited(e.JobId, DateTime.Now);
                h.Joined(e.JobId, DateTime.Now);
                return;
            }
        }

        // From the watchers: something was found on some client.
        void HuntFound(DetectionEvent d)
        {
            foreach (Hunt h in hunts.Values)
            {
                int mine;
                if (!huntPids.TryGetValue(h.Account, out mine) || mine != d.Pid) continue;
                if (!h.Found(d.WatcherName, d.When)) return;

                string said = d.WatcherName + " found something on " + h.Account + " - the hunt stays in this server.";
                Log(said);
                try
                {
                    tray.BalloonTipTitle = "Hunt found it";
                    tray.BalloonTipText = said;
                    tray.BalloonTipIcon = ToolTipIcon.Info;
                    tray.ShowBalloonTip(8000);
                }
                catch { }
                return;
            }
        }

        int HuntServers()
        {
            int n = 0;
            foreach (Hunt h in hunts.Values) n += h.Servers;
            return n;
        }

        int HuntsActive()
        {
            int n = 0;
            foreach (Hunt h in hunts.Values) if (h.Active) n++;
            return n;
        }
    }
}
