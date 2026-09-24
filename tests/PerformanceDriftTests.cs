using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RobloxKeeper.Tests
{
    // Settings that stay put.
    //
    // What was applied to a client was remembered and never looked at again,
    // so anything that changed it afterwards - the game, another tool, Task
    // Manager - went unnoticed for as long as the client ran. And Low power
    // was never read back at all, only priority and cores.
    static class PerformanceDriftTests
    {
        class Rig
        {
            public readonly PerformanceManager Perf = new PerformanceManager();
            public readonly List<string> Applied = new List<string>();
            public readonly List<string> Logged = new List<string>();
            public int Checks;
            public string Drift;
            public DateTime Now = new DateTime(2026, 9, 23, 10, 0, 0);

            public Rig()
            {
                Perf.Log = delegate(string s) { Logged.Add(s); };
                Perf.Clock = delegate { return Now; };
                Perf.Applier = delegate(int pid, ClientProfile p, out string error)
                {
                    Applied.Add(pid + "=" + p);
                    error = null;
                    return true;
                };
                Perf.Checker = delegate(int pid, ClientProfile want) { Checks++; return Drift; };
            }
        }

        static List<ClientInfo> Clients(params int[] pids)
        {
            List<ClientInfo> list = new List<ClientInfo>();
            foreach (int pid in pids) { ClientInfo c = new ClientInfo(); c.Pid = pid; list.Add(c); }
            return list;
        }

        public static void TestSettingsAreLookedAtAgainEveryHalfMinute()
        {
            Rig r = new Rig();
            r.Perf.ApplyPending(Clients(7));
            r.Perf.ApplyPending(Clients(7));
            Assert.Equal(0, r.Checks, "not straight after being set");

            r.Now = r.Now.AddSeconds(31);
            r.Perf.ApplyPending(Clients(7));
            r.Perf.ApplyPending(Clients(7));
            Assert.Equal(1, r.Checks, "once, half a minute on");
            Assert.Equal(1, r.Applied.Count, "and left alone when nothing had changed");
        }

        public static void TestAChangedSettingIsPutBack()
        {
            Rig r = new Rig();
            r.Perf.Defaults.Priority = PerformanceManager.PRIORITY_BELOW;
            r.Perf.ApplyPending(Clients(7));
            r.Drift = "priority did not stick (Windows left it at Normal)";
            r.Now = r.Now.AddSeconds(31);
            r.Perf.ApplyPending(Clients(7));
            Assert.Equal(2, r.Applied.Count, "set again");
            Assert.Contains("had changed", r.Logged[r.Logged.Count - 1], "and said so");
            Assert.Equal(2, r.Logged.Count, "once - not a second line saying it was set");
        }

        // Something that keeps changing it back is put right every time, but
        // doesn't fill the activity list saying so.
        public static void TestAClientThatKeepsChangingIsNotReportedEveryTime()
        {
            Rig r = new Rig();
            r.Perf.ApplyPending(Clients(7));
            r.Drift = "priority did not stick (Windows left it at Idle)";
            int before = r.Logged.Count;
            for (int i = 0; i < 5; i++)
            {
                r.Now = r.Now.AddSeconds(31);
                r.Perf.ApplyPending(Clients(7));
            }
            Assert.Equal(6, r.Applied.Count, "put right every time");
            Assert.Equal(1, r.Logged.Count - before, "said the first time");
        }

        public static void TestLowPowerThatDidntStickIsAProblem()
        {
            string p = PerformanceManager.ReadBackProblem(ProcessPriorityClass.Normal, 0xF, true,
                ProcessPriorityClass.Normal, 0xF, false);
            Assert.Contains("low power", p, "said in the card's own words");
        }

        // Some Windows builds can't be asked; not knowing isn't a problem.
        public static void TestLowPowerThatCantBeReadIsNotAProblem()
        {
            Assert.Equal(null, PerformanceManager.ReadBackProblem(ProcessPriorityClass.Normal, 0xF, true,
                ProcessPriorityClass.Normal, 0xF, null), "nothing to report");
        }
    }
}
