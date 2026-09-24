using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // The resource features that replaced the FPS cap.
    //
    // Capping FPS per client is not possible from outside the process - the only
    // lever is a Roblox config file, it is global to every client, and editing
    // it would break this project's "nothing on disk is touched" position. What
    // IS possible from outside is throttling the clients you are not looking at,
    // and handing their idle memory back.
    static class PerformanceModesTests
    {
        static ClientProfile P(int priority, bool eco)
        {
            ClientProfile p = new ClientProfile();
            p.Priority = priority; p.Eco = eco;
            return p;
        }

        // ---------- background throttling ----------

        public static void TestThrottlingDropsPriorityAndTurnsEcoOn()
        {
            ClientProfile t = PerformanceManager.Throttled(P(PerformanceManager.PRIORITY_NORMAL, false));

            Assert.Equal(PerformanceManager.PRIORITY_BELOW, t.Priority, "one step down");
            Assert.True(t.Eco, "efficiency mode on");
        }

        public static void TestThrottlingNeverGoesBelowTheLowestPriority()
        {
            ClientProfile t = PerformanceManager.Throttled(P(PerformanceManager.PRIORITY_LOW, true));
            Assert.Equal(PerformanceManager.PRIORITY_LOW, t.Priority, "already at the floor");
        }

        public static void TestThrottlingDoesNotMutateTheOriginal()
        {
            ClientProfile original = P(PerformanceManager.PRIORITY_NORMAL, false);
            PerformanceManager.Throttled(original);

            Assert.Equal(PerformanceManager.PRIORITY_NORMAL, original.Priority,
                "the stored profile must survive, or restoring would be impossible");
            Assert.False(original.Eco, "and its eco flag too");
        }

        public static void TestTheClientYouAreLookingAtIsNeverThrottled()
        {
            PerformanceManager perf = new PerformanceManager();
            perf.Log = delegate { };
            perf.ThrottleBackground = true;

            ClientProfile normal = P(PerformanceManager.PRIORITY_NORMAL, false);
            perf.Defaults = normal;

            Assert.False(perf.EffectiveFor(100, 100).Eco, "foreground client left alone");
            Assert.True(perf.EffectiveFor(200, 100).Eco, "background client throttled");
        }

        public static void TestNothingIsThrottledWhenTheFeatureIsOff()
        {
            PerformanceManager perf = new PerformanceManager();
            perf.Log = delegate { };
            perf.ThrottleBackground = false;
            perf.Defaults = P(PerformanceManager.PRIORITY_NORMAL, false);

            Assert.False(perf.EffectiveFor(200, 100).Eco, "background client untouched");
        }

        // ---------- memory ceiling ----------

        public static void TestNoCeilingMeansNoTrimming()
        {
            Assert.False(PerformanceManager.OverCeiling(4L * 1024 * 1024 * 1024, 0),
                "zero disables the ceiling entirely");
        }

        public static void TestAClientUnderTheCeilingIsLeftAlone()
        {
            Assert.False(PerformanceManager.OverCeiling(1500L * 1024 * 1024, 2000), "1.5 GB under a 2 GB ceiling");
        }

        public static void TestAClientOverTheCeilingIsTrimmed()
        {
            Assert.True(PerformanceManager.OverCeiling(2500L * 1024 * 1024, 2000), "2.5 GB over a 2 GB ceiling");
        }

        public static void TestTheCeilingItselfIsNotOver()
        {
            Assert.False(PerformanceManager.OverCeiling(2000L * 1024 * 1024, 2000), "exactly at the ceiling");
        }

        // ---------- AFK mode ----------

        public static void TestAfkModeThrottlesEverythingButTheOneYouArePlaying()
        {
            PerformanceManager perf = new PerformanceManager();
            perf.Log = delegate { };

            List<ClientInfo> clients = new List<ClientInfo>();
            foreach (int pid in new int[] { 100, 200, 300 })
            {
                ClientInfo ci = new ClientInfo(); ci.Pid = pid; clients.Add(ci);
            }

            perf.EnterAfkMode(clients, 200);

            Assert.Equal(PerformanceManager.PRIORITY_NORMAL, perf.ProfileFor(200).Priority,
                "the client you're playing gets full speed");
            Assert.False(perf.ProfileFor(200).Eco, "and no throttling");
            Assert.True(perf.ProfileFor(100).Eco, "the others are parked");
            Assert.True(perf.ProfileFor(300).Eco, "all of them");
        }

        public static void TestLeavingAfkModeGivesEveryClientTheDefaultBack()
        {
            PerformanceManager perf = new PerformanceManager();
            perf.Log = delegate { };
            perf.Defaults = P(PerformanceManager.PRIORITY_NORMAL, false);

            List<ClientInfo> clients = new List<ClientInfo>();
            ClientInfo a = new ClientInfo(); a.Pid = 100; clients.Add(a);
            ClientInfo b = new ClientInfo(); b.Pid = 200; clients.Add(b);

            perf.EnterAfkMode(clients, 200);
            perf.LeaveAfkMode(clients);

            Assert.False(perf.ProfileFor(100).Eco, "back to the default");
            Assert.False(perf.HasOverride(100), "and no override left behind");
        }
    }
}
