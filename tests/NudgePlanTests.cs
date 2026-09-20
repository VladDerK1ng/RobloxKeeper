using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // P1: the nudge used to run on the UI thread, sleeping roughly 1.2-1.6s per
    // client. Two clients froze the window for about three seconds every nudge,
    // which is why buttons stopped responding as accounts were added.
    //
    // Moving it to a worker means everything it needs must be decided up front,
    // on the UI thread, because a worker may not touch controls. That decision
    // is this plan, and it is worth testing on its own: picking the wrong
    // targets silently leaves an account to be idle-kicked.
    static class NudgePlanTests
    {
        static List<ClientInfo> Clients(params int[] pids)
        {
            List<ClientInfo> list = new List<ClientInfo>();
            foreach (int pid in pids)
            {
                ClientInfo ci = new ClientInfo();
                ci.Pid = pid;
                ci.Hwnd = (IntPtr)(pid * 10);
                list.Add(ci);
            }
            return list;
        }

        public static void TestNudgesEveryClientByDefault()
        {
            NudgePlan plan = NudgePlan.From(Clients(100, 200), new Dictionary<int, bool>());

            Assert.Equal(2, plan.Targets.Count, "both clients targeted");
            Assert.Equal(0, plan.Skipped, "nothing skipped");
        }

        public static void TestLeavesUntickedClientsAlone()
        {
            Dictionary<int, bool> prefs = new Dictionary<int, bool>();
            prefs[200] = false;

            NudgePlan plan = NudgePlan.From(Clients(100, 200, 300), prefs);

            Assert.Equal(2, plan.Targets.Count, "only the ticked clients");
            Assert.Equal(1, plan.Skipped, "the unticked one counted as skipped");
            Assert.Equal((IntPtr)1000, plan.Targets[0], "first ticked client");
            Assert.Equal((IntPtr)3000, plan.Targets[1], "third ticked client");
        }

        public static void TestAClientWithNoPreferenceIsIncluded()
        {
            Dictionary<int, bool> prefs = new Dictionary<int, bool>();
            prefs[100] = true;

            NudgePlan plan = NudgePlan.From(Clients(100, 999), prefs);

            Assert.Equal(2, plan.Targets.Count, "a client never seen before defaults to enabled");
        }

        public static void TestEverythingUntickedProducesNoWork()
        {
            Dictionary<int, bool> prefs = new Dictionary<int, bool>();
            prefs[100] = false;
            prefs[200] = false;

            NudgePlan plan = NudgePlan.From(Clients(100, 200), prefs);

            Assert.Equal(0, plan.Targets.Count, "nothing to nudge");
            Assert.Equal(2, plan.Skipped, "both counted as skipped");
            Assert.False(plan.HasWork, "the worker should not even start");
        }

        public static void TestNoClientsAtAllProducesNoWork()
        {
            NudgePlan plan = NudgePlan.From(Clients(), new Dictionary<int, bool>());

            Assert.False(plan.HasWork, "nothing running");
            Assert.Equal(0, plan.ClientCount, "no clients seen");
        }

        public static void TestRemembersHowManyClientsWereRunning()
        {
            // The log line distinguishes "no clients" from "none ticked", so the
            // plan has to carry the total as well as the targets.
            Dictionary<int, bool> prefs = new Dictionary<int, bool>();
            prefs[100] = false;

            NudgePlan plan = NudgePlan.From(Clients(100, 200), prefs);

            Assert.Equal(2, plan.ClientCount, "two clients were running");
            Assert.Equal(1, plan.Targets.Count, "one of them was ticked");
        }
    }
}
