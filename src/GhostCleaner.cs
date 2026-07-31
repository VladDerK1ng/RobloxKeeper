using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RobloxKeeper
{
    // Terminates leaked window-less Roblox clients.
    //
    // Auto-clearing used to be gated on multi-instance being enabled AND the
    // singleton mutex NOT being held by us - which is exactly the state the app
    // spends almost none of its time in, so the setting effectively did nothing.
    // A leaked client wastes a gigabyte of RAM no matter who owns the mutex, so
    // the age check is now the only thing standing between a process and a kill.
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

        public void ClearStuck()
        {
            Sweep(false);
        }

        // The manual "End background" button. No age check: the user is looking
        // at the count and asking for all of them, including one that started
        // ten seconds ago.
        public void KillAll()
        {
            Sweep(true);
        }

        void Sweep(bool includeStarting)
        {
            Prune();

            int killed = 0, failed = 0;
            string lastError = null;
            Process[] procs = Process.GetProcessesByName(ClientTracker.ROBLOX_PROCESS);

            foreach (Process p in procs)
            {
                try
                {
                    bool target = includeStarting
                        ? ClientTracker.IsWindowless(p)
                        : ClientTracker.IsStuckGhost(p);
                    if (!target) continue;

                    int pid = p.Id;
                    if (!includeStarting && recentlyKilled.ContainsKey(pid)) continue;

                    try
                    {
                        p.Kill();
                        recentlyKilled[pid] = DateTime.Now;
                        killed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        lastError = "PID " + pid + " - " + ex.Message;
                    }
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            if (killed > 0)
                Log("Cleared " + killed + " stuck background Roblox process(es) - their memory is back.");

            // Retrying every second and complaining every second are different
            // problems; the second one makes the log unreadable.
            if (failed > 0 && (DateTime.Now - lastFailureLogged).TotalSeconds > FAILURE_LOG_COOLDOWN_SECONDS)
            {
                lastFailureLogged = DateTime.Now;
                Log("Could not end " + failed + " stuck Roblox process(es): " + lastError +
                    ". Try running RobloxKeeper as administrator if this keeps happening.");
            }

            if (includeStarting && killed == 0 && failed == 0)
                Log("No background Roblox processes to end.");
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
