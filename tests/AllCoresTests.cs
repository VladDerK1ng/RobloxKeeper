using System;
using System.Diagnostics;

namespace RobloxKeeper.Tests
{
    // Every Roblox client runs on every core.
    //
    // Pinning a client to cores was the lag. A Roblox client runs about ninety
    // threads; locked to one core they queued behind each other until Windows'
    // starvation boost let one through, so the game froze for five seconds at
    // a time while fifteen other cores sat idle. The hidden copy Roblox starts
    // when a game closes inherited the same single core and starved too. No
    // core count helps a game more than Windows' own scheduler does, so the
    // setting is gone and every client is given every core.
    static class AllCoresTests
    {
        static long ExpectedAll()
        {
            int n = Environment.ProcessorCount;
            return n >= 64 ? -1L : (1L << n) - 1;
        }

        public static void TestEveryClientIsGivenEveryCore()
        {
            Assert.Equal(ExpectedAll(), (long)PerformanceManager.AllCoresMask(), "every logical processor");
        }

        // What an older RobloxKeeper left behind: clients locked to one core.
        // Reading one back as drift is what puts it back on every core.
        public static void TestAClientLockedToOneCoreIsPutBackOnEveryCore()
        {
            string problem = PerformanceManager.ReadBackProblem(
                ProcessPriorityClass.Normal, (long)PerformanceManager.AllCoresMask(), false,
                ProcessPriorityClass.Normal, 0x1, false);

            Assert.True(problem != null, "a client on one core does not match");
            Assert.Contains("core", problem, "and the core lock is what is named");
        }

        public static void TestAClientAlreadyOnEveryCoreIsLeftAlone()
        {
            long all = (long)PerformanceManager.AllCoresMask();
            string problem = PerformanceManager.ReadBackProblem(
                ProcessPriorityClass.Normal, all, false,
                ProcessPriorityClass.Normal, all, false);

            Assert.True(problem == null, "nothing to put back");
        }

        // The setting that caused it, as the real settings file had it.
        public static void TestACoreLimitSavedByAnOlderVersionIsIgnored()
        {
            AppSettings s = AppSettings.Parse(new string[] { "perfpriority=2", "perfcores=1", "perfeco=0" });

            Assert.Equal("Normal", s.ToProfile().ToString(), "no core limit survives loading");
        }

        public static void TestTheOtherPerformanceSettingsStillLoad()
        {
            AppSettings s = AppSettings.Parse(new string[] { "perfpriority=1", "perfcores=4", "perfeco=1" });
            ClientProfile p = s.ToProfile();

            Assert.Equal(PerformanceManager.PRIORITY_BELOW, p.Priority, "priority kept");
            Assert.True(p.Eco, "low power kept");
        }

        public static void TestACoreLimitIsNeverWrittenBack()
        {
            foreach (string line in AppSettings.Parse(new string[] { "perfcores=1" }).ToLines())
                Assert.False(line.StartsWith("perfcores", StringComparison.Ordinal), "perfcores is no longer saved");
        }

        // Parse and ToLines are the file's two halves; everything else that
        // was in it must come back out unchanged.
        public static void TestSettingsSurviveARoundTrip()
        {
            AppSettings s = AppSettings.Parse(new string[] { "keys=0", "interval=12", "perfpriority=1", "acc_where=1" });
            AppSettings back = AppSettings.Parse(s.ToLines());

            Assert.Equal(0, back.KeysIndex, "nudge method");
            Assert.Equal(12, back.IntervalMinutes, "interval");
            Assert.Equal(PerformanceManager.PRIORITY_BELOW, back.PerfPriority, "priority");
            Assert.Equal(JoinWhere.Emptiest, back.Accounts.Where, "accounts window choice");
        }
    }
}
