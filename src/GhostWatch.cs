using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RobloxKeeper
{
    // Decides which window-less Roblox processes are actually leaked.
    //
    // The obvious test - "no window and the process is older than the grace
    // period" - is wrong, and wrong in the worst possible direction. A client
    // that has been playing for four hours has no grace left at all, so a single
    // tick where Process.MainWindowHandle returns zero is enough to condemn it.
    // And it returns zero for plenty of healthy reasons: the window is not
    // WS_VISIBLE during a place teleport, across a fullscreen switch, or while
    // the client is shutting down. That shipped in v1.2 and killed live sessions.
    //
    // What matters is how long the window has been gone, not how old the process
    // is. Showing a window at any point resets the clock, so a momentary blip can
    // never accumulate. A genuinely leaked process never recovers and crosses the
    // threshold on its own.
    class GhostWatch
    {
        class Sighting
        {
            public DateTime Since;
            public int Ticks;
        }

        readonly Dictionary<int, Sighting> windowless = new Dictionary<int, Sighting>();
        readonly List<int> stuck = new List<int>();

        // Swapped out by the tests so grace periods don't take real minutes.
        public Func<DateTime> Clock = delegate { return DateTime.Now; };

        public IList<int> Stuck { get { return stuck; } }
        public int Starting { get; private set; }
        public int Total { get { return stuck.Count + Starting; } }

        // Called once per tick with the process snapshot the rest of the loop
        // already took.
        public void Observe(IList<Process> procs)
        {
            List<int> withWindow = new List<int>();
            List<int> withoutWindow = new List<int>();

            foreach (Process p in procs)
            {
                try
                {
                    if (!ClientTracker.IsClient(p)) continue;
                    if (p.MainWindowHandle != IntPtr.Zero) withWindow.Add(p.Id);
                    else withoutWindow.Add(p.Id);
                }
                catch { }
            }

            Update(withWindow, withoutWindow);
        }

        // The decision itself, free of Process so it can be driven directly.
        public void Update(IList<int> withWindow, IList<int> withoutWindow)
        {
            stuck.Clear();
            Starting = 0;

            // A window right now voids whatever we believed a moment ago.
            foreach (int pid in withWindow) windowless.Remove(pid);

            foreach (int pid in withoutWindow)
            {
                Sighting s;
                if (!windowless.TryGetValue(pid, out s))
                {
                    s = new Sighting();
                    s.Since = Clock();
                    windowless[pid] = s;
                }
                s.Ticks++;

                if (IsLeaked(s)) stuck.Add(pid);
                else Starting++;
            }

            Prune(withWindow, withoutWindow);
        }

        // Both conditions, deliberately. Elapsed time alone breaks across a
        // laptop sleep: the machine wakes, the first tick catches a client whose
        // window has not been restored yet, and the wall clock claims it has been
        // gone for eight hours. A tick count cannot jump like that, because no
        // ticks happen while the machine is suspended.
        bool IsLeaked(Sighting s)
        {
            return s.Ticks >= ClientTracker.GHOST_GRACE_SECONDS
                && (Clock() - s.Since).TotalSeconds >= ClientTracker.GHOST_GRACE_SECONDS;
        }

        void Prune(IList<int> withWindow, IList<int> withoutWindow)
        {
            if (windowless.Count == 0) return;
            List<int> gone = new List<int>();
            foreach (int pid in windowless.Keys)
                if (!withoutWindow.Contains(pid) && !withWindow.Contains(pid)) gone.Add(pid);
            foreach (int pid in gone) windowless.Remove(pid);
        }

        // How long a given process has gone without a window. Only used for the
        // log line, so it is clear why something was ended.
        public int SecondsWindowless(int pid)
        {
            Sighting s;
            if (!windowless.TryGetValue(pid, out s)) return 0;
            return (int)(Clock() - s.Since).TotalSeconds;
        }

        public void Forget(int pid) { windowless.Remove(pid); }
    }
}
