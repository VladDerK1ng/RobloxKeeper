using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // The memory ceiling, as a ceiling.
    //
    // It used to empty a background client's whole working set every time it
    // crossed the line, once a second. A game that really uses more than the
    // line climbs straight back, so it was emptied again seconds later -
    // steady stutter, and never actually under the line. Now each background
    // client gets a hard working-set maximum, which Windows keeps by moving
    // out only its least-used pages. Set once, lifted while the client is the
    // one in front, and lifted from everything when the ceiling is switched off.
    static class MemoryCeilingTests
    {
        class Rig
        {
            public readonly PerformanceManager Perf = new PerformanceManager();
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> Logged = new List<string>();
            public bool Refuse;
            public DateTime Now = new DateTime(2026, 9, 23, 10, 0, 0);

            public Rig()
            {
                Perf.Log = delegate(string s) { Logged.Add(s); };
                Perf.Clock = delegate { return Now; };
                Perf.Capper = delegate(int pid, int mb, out string error)
                {
                    Calls.Add(pid + ":" + mb);
                    error = Refuse ? "access to the process was denied" : null;
                    return !Refuse;
                };
            }
        }

        static List<ClientInfo> Clients(params int[] pids)
        {
            List<ClientInfo> list = new List<ClientInfo>();
            foreach (int pid in pids) { ClientInfo c = new ClientInfo(); c.Pid = pid; list.Add(c); }
            return list;
        }

        public static void TestEveryClientButTheOneInFrontIsCapped()
        {
            Rig r = new Rig();
            r.Perf.CeilingTick(Clients(1, 2, 3), 2000, 1);
            Assert.Equal("2:2000|3:2000", string.Join("|", r.Calls.ToArray()), "the two behind, at the line");
        }

        public static void TestACapIsSetOnceNotEveryTick()
        {
            Rig r = new Rig();
            r.Perf.CeilingTick(Clients(1, 2), 2000, 1);
            r.Perf.CeilingTick(Clients(1, 2), 2000, 1);
            r.Perf.CeilingTick(Clients(1, 2), 2000, 1);
            Assert.Equal(1, r.Calls.Count, "Windows keeps it from then on");
        }

        // The game being played is never held back; the one left behind is.
        public static void TestSwitchingClientsMovesTheCap()
        {
            Rig r = new Rig();
            r.Perf.CeilingTick(Clients(1, 2), 2000, 1);
            r.Calls.Clear();
            r.Perf.CeilingTick(Clients(1, 2), 2000, 2);
            Assert.Contains("2:0", string.Join("|", r.Calls.ToArray()), "the one now in front is let go");
            Assert.Contains("1:2000", string.Join("|", r.Calls.ToArray()), "and the one left behind is capped");
        }

        public static void TestANewNumberReachesEveryCap()
        {
            Rig r = new Rig();
            r.Perf.CeilingTick(Clients(1, 2, 3), 2000, 0);
            r.Calls.Clear();
            r.Perf.CeilingTick(Clients(1, 2, 3), 1500, 0);
            Assert.Equal("1:1500|2:1500|3:1500", string.Join("|", r.Calls.ToArray()), "all moved to the new line");
        }

        public static void TestSwitchingTheCeilingOffLetsEveryClientGo()
        {
            Rig r = new Rig();
            r.Perf.CeilingTick(Clients(1, 2), 2000, 0);
            r.Calls.Clear();
            r.Perf.CeilingTick(Clients(1, 2), 0, 0);
            Assert.Equal("1:0|2:0", string.Join("|", r.Calls.ToArray()), "both let go");
            r.Calls.Clear();
            r.Perf.CeilingTick(Clients(1, 2), 0, 0);
            Assert.Equal(0, r.Calls.Count, "and never touched again while it is off");
        }

        // Exiting the app lets go too, rather than leaving clients held to a
        // line nothing is managing any more.
        public static void TestLettingGoOfEverythingOnExit()
        {
            Rig r = new Rig();
            r.Perf.CeilingTick(Clients(1, 2), 2000, 0);
            r.Calls.Clear();
            r.Perf.LiftCeiling();
            Assert.Equal("1:0|2:0", string.Join("|", r.Calls.ToArray()), "every cap lifted");
        }

        // Refused - it said why, once, and asks again in a minute rather than
        // every second.
        public static void TestARefusedCapIsTriedAgainLater()
        {
            Rig r = new Rig();
            r.Refuse = true;
            r.Perf.CeilingTick(Clients(2), 2000, 0);
            r.Perf.CeilingTick(Clients(2), 2000, 0);
            Assert.Equal(1, r.Calls.Count, "not again straight away");
            Assert.Equal(1, r.Logged.Count, "said once");
            Assert.Contains("access to the process was denied", r.Logged[0], "with the reason");

            r.Refuse = false;
            r.Now = r.Now.AddSeconds(61);
            r.Perf.CeilingTick(Clients(2), 2000, 0);
            Assert.Equal(2, r.Calls.Count, "tried again a minute later");
        }

        // A closed client's cap is forgotten, so a new client that happens to
        // get the same process id is capped afresh.
        public static void TestAClosedClientIsForgotten()
        {
            Rig r = new Rig();
            r.Perf.CeilingTick(Clients(2), 2000, 0);
            r.Perf.Prune(Clients());
            r.Calls.Clear();
            r.Perf.CeilingTick(Clients(2), 2000, 0);
            Assert.Equal("2:2000", string.Join("|", r.Calls.ToArray()), "capped again");
        }

        // The numbers Windows is given: the ceiling as the maximum, and a
        // minimum that can never be above it.
        public static void TestTheLimitsWindowsIsGiven()
        {
            long min, max;
            PerformanceManager.CapLimits(2000, 1024 * 1024, out min, out max);
            Assert.Equal(2000L * 1024 * 1024, max, "the ceiling, in bytes");
            Assert.Equal(1024L * 1024, min, "the client's own minimum kept");

            PerformanceManager.CapLimits(256, 400L * 1024 * 1024, out min, out max);
            Assert.True(min <= max, "a minimum above the ceiling is brought under it");
        }
    }
}
