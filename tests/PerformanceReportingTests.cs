using System;
using System.Diagnostics;

namespace RobloxKeeper.Tests
{
    // P5: the card must report what Windows actually did.
    //
    // Every efficiency-mode failure used to be reported as "needs Windows 10
    // 2004 or newer" regardless of what went wrong, which sent anyone hitting a
    // permissions problem looking at their Windows version. And nothing ever
    // checked that priority or affinity had stuck, so a silently ignored call
    // was indistinguishable from a successful one.
    static class PerformanceReportingTests
    {
        public static void TestUnsupportedWindowsIsNamedAsSuch()
        {
            string reason = PerformanceManager.EcoFailureReason(87);   // ERROR_INVALID_PARAMETER
            Assert.Contains("Windows 10", reason, "old-Windows case still explained");
        }

        public static void TestAccessDeniedIsNotBlamedOnTheWindowsVersion()
        {
            string reason = PerformanceManager.EcoFailureReason(5);    // ERROR_ACCESS_DENIED
            Assert.Contains("access", reason, "names the real problem");
            Assert.False(reason.Contains("Windows 10"), "must not blame the Windows version");
        }

        public static void TestAnUnknownFailureReportsItsCode()
        {
            string reason = PerformanceManager.EcoFailureReason(1234);
            Assert.Contains("1234", reason, "unknown failures carry their code for diagnosis");
            Assert.False(reason.Contains("Windows 10"), "must not guess at the cause");
        }

        // ---------- read-back ----------

        public static void TestMatchingStateReportsNoProblem()
        {
            string problem = PerformanceManager.ReadBackProblem(
                ProcessPriorityClass.BelowNormal, 0xF,
                ProcessPriorityClass.BelowNormal, 0xF);

            Assert.Equal(null, problem, "nothing to report when the state stuck");
        }

        public static void TestPriorityThatDidNotStickIsReported()
        {
            string problem = PerformanceManager.ReadBackProblem(
                ProcessPriorityClass.High, 0xF,
                ProcessPriorityClass.Normal, 0xF);

            Assert.Contains("priority", problem, "names what drifted");
            Assert.Contains("Normal", problem, "reports what Windows actually left it at");
        }

        public static void TestAffinityThatDidNotStickIsReported()
        {
            string problem = PerformanceManager.ReadBackProblem(
                ProcessPriorityClass.Normal, 0xF,
                ProcessPriorityClass.Normal, 0xFF);

            Assert.Contains("core", problem, "names what drifted");
        }

        public static void TestBothDriftingAreReportedTogether()
        {
            string problem = PerformanceManager.ReadBackProblem(
                ProcessPriorityClass.High, 0xF,
                ProcessPriorityClass.Idle, 0xFF);

            Assert.Contains("priority", problem, "priority drift reported");
            Assert.Contains("core", problem, "affinity drift reported");
        }

        // ---------- the mask itself ----------

        public static void TestAskingForEveryCoreGivesEveryCore()
        {
            long all = (long)PerformanceManager.AffinityMask(0, 0);
            long expected = Environment.ProcessorCount >= 64
                ? -1L : (1L << Environment.ProcessorCount) - 1;

            Assert.Equal(expected, all, "zero cores means no pinning at all");
        }

        public static void TestSuccessiveBlocksDoNotOverlap()
        {
            long first = (long)PerformanceManager.AffinityMask(4, 0);
            long second = (long)PerformanceManager.AffinityMask(4, 1);

            Assert.Equal(0L, first & second, "blocks 0 and 1 share no core");
        }
    }
}
