using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Roblox relaunches itself window-less when you close a client:
    //
    //   PID 33116  parent 19380 (exited)
    //   "...\RobloxPlayerBeta.exe" --launch-to-tray
    //
    // That is a legitimate Roblox process, not a leak - but it has no window, so
    // the ghost cleaner condemned it after 150 seconds and, with auto-clear on
    // by default, killed it. It is also why the mutex looks stuck after closing
    // every client: the tray process is still holding it.
    static class TrayProcessTests
    {
        static GhostWatch Watch(DateTime[] now)
        {
            GhostWatch w = new GhostWatch();
            w.Clock = delegate { return now[0]; };
            return w;
        }

        static List<int> L(params int[] pids) { return new List<int>(pids); }

        static void RunPastGrace(GhostWatch w, DateTime[] now, List<int> windowed, List<int> windowless)
        {
            for (int i = 0; i <= ClientTracker.GHOST_GRACE_SECONDS; i++)
            {
                w.Update(windowed, windowless);
                now[0] = now[0].AddSeconds(1);
            }
            w.Update(windowed, windowless);
        }

        public static void TestAWindowlessClientIsStillCondemnedAfterTheGracePeriod()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            GhostWatch w = Watch(now);

            RunPastGrace(w, now, L(), L(999));

            Assert.Equal(1, w.Stuck.Count, "a genuine leak is still caught");
        }

        // The regression.
        public static void TestTheTrayProcessIsNeverCondemned()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            GhostWatch w = Watch(now);
            w.IsTray = delegate(int pid) { return pid == 33116; };

            RunPastGrace(w, now, L(), L(33116));

            Assert.Equal(0, w.Stuck.Count, "Roblox's own tray process is not a leak");
            Assert.Equal(1, w.Tray, "but it is counted, because it holds the mutex");
        }

        public static void TestTrayAndLeakAreCountedSeparately()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            GhostWatch w = Watch(now);
            w.IsTray = delegate(int pid) { return pid == 33116; };

            RunPastGrace(w, now, L(), L(33116, 999));

            Assert.Equal(1, w.Stuck.Count, "one leak");
            Assert.Equal(999, w.Stuck[0], "and it is the right one");
            Assert.Equal(1, w.Tray, "one tray process");
        }

        public static void TestATrayProcessIsNotCountedAsStarting()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            GhostWatch w = Watch(now);
            w.IsTray = delegate(int pid) { return pid == 33116; };

            w.Update(L(), L(33116));

            Assert.Equal(0, w.Starting, "it is not launching, it is resident");
            Assert.Equal(1, w.Tray, "it is in the tray");
        }

        public static void TestWithoutTheTrayTestNothingChanges()
        {
            // IsTray unset must behave exactly as before, so the classification
            // is additive rather than a rewrite of the leak rule.
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            GhostWatch w = Watch(now);

            RunPastGrace(w, now, L(), L(33116));

            Assert.Equal(1, w.Stuck.Count, "no tray predicate means the old behaviour");
        }
    }
}
