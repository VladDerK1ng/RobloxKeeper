using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RobloxKeeper
{
    // Terminates leaked window-less Roblox clients.
    //
    // This never decides what is leaked. GhostWatch does that, from how long a
    // window has been gone rather than how old the process is, and hands over a
    // list of PIDs. Everything here does is double-check that list and kill it.
    class GhostCleaner
    {
        public Action<string> Log;

        // A Kill() is asynchronous, so without this the same doomed process gets
        // killed again on every one-second tick until Windows finishes tearing it
        // down - and any failure would be logged just as often.
        readonly Dictionary<int, DateTime> recentlyKilled = new Dictionary<int, DateTime>();
        DateTime lastFailureLogged = DateTime.MinValue;

        const int RETRY_COOLDOWN_SECONDS = 10;
        const int FAILURE_LOG_COOLDOWN_SECONDS = 120;

        public void Clear(IList<int> pids, GhostWatch watch)
        {
            if (pids == null || pids.Count == 0) return;
            Prune();

            List<string> killed = new List<string>();
            int failed = 0;
            string lastError = null;

            foreach (int pid in pids)
            {
                if (recentlyKilled.ContainsKey(pid)) continue;

                // Re-checked against a fresh handle immediately before the kill.
                // The decision was made from a snapshot taken earlier in the
                // tick, and a client that drew its window in between must not be
                // ended on the strength of a stale reading.
                if (!StillWindowless(pid)) { if (watch != null) watch.Forget(pid); continue; }

                try
                {
                    using (Process p = Process.GetProcessById(pid))
                    {
                        int seconds = watch != null ? watch.SecondsWindowless(pid) : 0;
                        p.Kill();
                        recentlyKilled[pid] = DateTime.Now;
                        killed.Add("PID " + pid + " (no window for " + seconds + "s)");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    lastError = "PID " + pid + " - " + ex.Message;
                }
            }

            if (killed.Count > 0)
                Log("Ended " + killed.Count + " leaked Roblox process(es): " +
                    string.Join(", ", killed.ToArray()) + ".");

            // Retrying every second and complaining every second are different
            // problems; the second one makes the log unreadable.
            if (failed > 0 && (DateTime.Now - lastFailureLogged).TotalSeconds > FAILURE_LOG_COOLDOWN_SECONDS)
            {
                lastFailureLogged = DateTime.Now;
                Log("Could not end " + failed + " leaked Roblox process(es): " + lastError +
                    ". Try running RobloxKeeper as administrator if this keeps happening.");
            }
        }

        static bool StillWindowless(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return ClientTracker.IsClient(p) && p.MainWindowHandle == IntPtr.Zero;
            }
            catch { return false; }   // already gone, or not ours to judge
        }

        // Only for "Close all Roblox", where the user has asked for everything to
        // go and a client that is still drawing its window is meant to go too.
        public void KillEveryWindowless()
        {
            int killed = 0;
            foreach (Process p in Process.GetProcessesByName(ClientTracker.ROBLOX_PROCESS))
            {
                try
                {
                    if (ClientTracker.IsWindowless(p)) { p.Kill(); killed++; }
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            if (killed > 0) Log("Ended " + killed + " background Roblox process(es).");
        }

        void Prune()
        {
            List<int> expired = new List<int>();
            foreach (KeyValuePair<int, DateTime> kv in recentlyKilled)
                if ((DateTime.Now - kv.Value).TotalSeconds > RETRY_COOLDOWN_SECONDS) expired.Add(kv.Key);
            foreach (int pid in expired) recentlyKilled.Remove(pid);
        }
    }
}
