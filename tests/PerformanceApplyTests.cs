using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // P3: the card is labelled "New clients:", so the default must reach clients
    //     that appear after it is set - and leave running ones alone.
    // P2: a tune Windows refused must be retried, not recorded as done.
    static class PerformanceApplyTests
    {
        static List<ClientInfo> Clients(params int[] pids)
        {
            List<ClientInfo> list = new List<ClientInfo>();
            foreach (int pid in pids)
            {
                ClientInfo ci = new ClientInfo();
                ci.Pid = pid;
                ci.Hwnd = (IntPtr)pid;
                ci.Start = new DateTime(2026, 9, 20, 12, 0, 0).AddSeconds(pid);
                list.Add(ci);
            }
            return list;
        }

        static ClientProfile Profile(int priority, int cores, bool eco)
        {
            ClientProfile p = new ClientProfile();
            p.Priority = priority;
            p.Cores = cores;
            p.Eco = eco;
            return p;
        }

        // Records what was applied to whom, and can be told to fail.
        class FakeWindows
        {
            public readonly List<string> Applied = new List<string>();
            public int FailTimes;
            public string Error = "Access is denied";

            public bool Apply(int pid, ClientProfile profile, int block, out string error)
            {
                if (FailTimes > 0)
                {
                    FailTimes--;
                    error = Error;
                    return false;
                }
                error = null;
                Applied.Add(pid + "=" + profile);
                return true;
            }
        }

        static PerformanceManager Manager(FakeWindows windows, DateTime[] now)
        {
            PerformanceManager perf = new PerformanceManager();
            perf.Log = delegate { };
            perf.Applier = windows.Apply;
            perf.Clock = delegate { return now[0]; };
            return perf;
        }

        // ---------- P3: defaults ----------

        public static void TestAppliesTheDefaultToAClientItHasNotSeenBefore()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            FakeWindows windows = new FakeWindows();
            PerformanceManager perf = Manager(windows, now);
            perf.Defaults = Profile(PerformanceManager.PRIORITY_BELOW, 4, true);

            perf.ApplyPending(Clients(100));

            Assert.Equal(1, windows.Applied.Count, "one apply for one new client");
            Assert.Contains("Below normal", windows.Applied[0], "the default reached the client");
        }

        // The regression: "New clients:" must not mean "every client, right now".
        public static void TestChangingTheDefaultLeavesRunningClientsAlone()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            FakeWindows windows = new FakeWindows();
            PerformanceManager perf = Manager(windows, now);
            List<ClientInfo> running = Clients(100);

            perf.ApplyPending(running);
            windows.Applied.Clear();

            perf.Defaults = Profile(PerformanceManager.PRIORITY_LOW, 2, true);
            perf.ApplyPending(running);

            Assert.Equal(0, windows.Applied.Count, "running client must not be retuned by a default change");
        }

        public static void TestAClientThatAppearsAfterTheChangeGetsTheNewDefault()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            FakeWindows windows = new FakeWindows();
            PerformanceManager perf = Manager(windows, now);

            perf.ApplyPending(Clients(100));
            perf.Defaults = Profile(PerformanceManager.PRIORITY_HIGH, 0, false);
            windows.Applied.Clear();

            perf.ApplyPending(Clients(100, 200));

            Assert.Equal(1, windows.Applied.Count, "only the new client is tuned");
            Assert.Contains("200=High", windows.Applied[0], "new client got the new default");
        }

        public static void TestApplyToAllRunningIsTheExplicitWayToRetuneEveryone()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            FakeWindows windows = new FakeWindows();
            PerformanceManager perf = Manager(windows, now);
            List<ClientInfo> running = Clients(100, 200);

            perf.ApplyPending(running);
            perf.Defaults = Profile(PerformanceManager.PRIORITY_LOW, 1, true);
            windows.Applied.Clear();

            perf.ApplyToAllRunning(running);

            Assert.Equal(2, windows.Applied.Count, "both running clients retuned on request");
        }

        public static void TestAPerClientOverrideStillAppliesWithoutWaiting()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            FakeWindows windows = new FakeWindows();
            PerformanceManager perf = Manager(windows, now);
            List<ClientInfo> running = Clients(100);

            perf.ApplyPending(running);
            windows.Applied.Clear();

            perf.SetOverride(100, Profile(PerformanceManager.PRIORITY_HIGH, 0, false));
            perf.ApplyPending(running);

            Assert.Equal(1, windows.Applied.Count, "Tune takes effect on the next tick");
            Assert.Contains("High", windows.Applied[0], "the override, not the default");
        }

        // ---------- P2: retry ----------

        public static void TestRetriesATuneThatWindowsRefused()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            FakeWindows windows = new FakeWindows();
            windows.FailTimes = 1;
            PerformanceManager perf = Manager(windows, now);
            List<ClientInfo> running = Clients(100);

            perf.ApplyPending(running);
            Assert.Equal(0, windows.Applied.Count, "first attempt failed");

            now[0] = now[0].AddSeconds(2);
            perf.ApplyPending(running);

            Assert.Equal(1, windows.Applied.Count, "the refused tune was retried and stuck");
        }

        public static void TestDoesNotHammerWindowsBetweenRetries()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            FakeWindows windows = new FakeWindows();
            windows.FailTimes = 1;
            PerformanceManager perf = Manager(windows, now);
            List<ClientInfo> running = Clients(100);

            perf.ApplyPending(running);          // fails, backoff starts

            now[0] = now[0].AddMilliseconds(200);
            perf.ApplyPending(running);
            perf.ApplyPending(running);

            Assert.Equal(0, windows.Applied.Count, "no retry before the backoff elapses");
        }

        public static void TestBackoffGrowsWhileFailuresContinue()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            FakeWindows windows = new FakeWindows();
            windows.FailTimes = 2;
            PerformanceManager perf = Manager(windows, now);
            List<ClientInfo> running = Clients(100);

            perf.ApplyPending(running);                 // attempt 1 fails, wait 1s
            now[0] = now[0].AddSeconds(1);
            perf.ApplyPending(running);                 // attempt 2 fails, wait 2s

            now[0] = now[0].AddSeconds(1);              // only 1s of the 2s gone
            perf.ApplyPending(running);
            Assert.Equal(0, windows.Applied.Count, "second backoff is longer than the first");

            now[0] = now[0].AddSeconds(1);              // now 2s
            perf.ApplyPending(running);
            Assert.Equal(1, windows.Applied.Count, "retried once the longer backoff elapsed");
        }

        public static void TestStopsRetryingOnceTheTuneSticks()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            FakeWindows windows = new FakeWindows();
            windows.FailTimes = 1;
            PerformanceManager perf = Manager(windows, now);
            List<ClientInfo> running = Clients(100);

            perf.ApplyPending(running);
            now[0] = now[0].AddSeconds(2);
            perf.ApplyPending(running);      // succeeds
            windows.Applied.Clear();

            now[0] = now[0].AddSeconds(60);
            perf.ApplyPending(running);

            Assert.Equal(0, windows.Applied.Count, "a settled client is left alone");
        }

        public static void TestForgettingAClientClearsItsRetryState()
        {
            DateTime[] now = { new DateTime(2026, 9, 20, 12, 0, 0) };
            FakeWindows windows = new FakeWindows();
            windows.FailTimes = 1;
            PerformanceManager perf = Manager(windows, now);
            List<ClientInfo> running = Clients(100);

            perf.ApplyPending(running);      // fails, backoff pending
            perf.Prune(new List<ClientInfo>());   // client closed
            windows.FailTimes = 0;

            perf.ApplyPending(running);      // a recycled PID starts clean

            Assert.Equal(1, windows.Applied.Count, "no stale backoff held over from the closed client");
        }
    }
}
