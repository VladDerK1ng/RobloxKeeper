using System;

namespace RobloxKeeper.Tests
{
    // Deciding WHEN to nudge, so it stops happening in the middle of a match.
    //
    // A nudge takes the foreground for about a second per client. That is a lost
    // fight in anything competitive. The rule: if the user is at the keyboard,
    // wait for a lull - unless a client is close enough to Roblox's 20-minute
    // idle kick that waiting would cost the account instead.
    static class NudgePolicyTests
    {
        static readonly TimeSpan Quiet = TimeSpan.FromSeconds(45);
        static readonly TimeSpan Deadline = TimeSpan.FromMinutes(19);

        static NudgeDecision Decide(double userIdleSec, double sinceNudgeMin)
        {
            return NudgePolicy.Decide(
                TimeSpan.FromSeconds(userIdleSec),
                TimeSpan.FromMinutes(sinceNudgeMin),
                Quiet, Deadline);
        }

        public static void TestNudgesWhenNobodyIsAtTheKeyboard()
        {
            Assert.Equal(NudgeDecision.Go, Decide(120, 15), "user idle two minutes");
        }

        public static void TestWaitsWhileTheUserIsPlaying()
        {
            Assert.Equal(NudgeDecision.Defer, Decide(2, 15), "input two seconds ago");
        }

        public static void TestTreatsAShortPauseAsStillPlaying()
        {
            // Looking at the map or reading chat is not a lull worth stealing
            // focus in.
            Assert.Equal(NudgeDecision.Defer, Decide(44, 15), "just under the quiet threshold");
        }

        public static void TestTheThresholdItselfCounsAsQuiet()
        {
            Assert.Equal(NudgeDecision.Go, Decide(45, 15), "exactly at the threshold");
        }

        // Protecting the AFK account beats protecting the match, but only once
        // there is no time left.
        public static void TestNudgesAnywayWhenTheKickIsImminent()
        {
            Assert.Equal(NudgeDecision.Urgent, Decide(1, 19), "deadline reached while the user is active");
        }

        public static void TestUrgentBeatsDeferEvenWithConstantInput()
        {
            Assert.Equal(NudgeDecision.Urgent, Decide(0, 25), "long overdue");
        }

        // If the user is idle anyway there is nothing to interrupt, so a
        // deadline nudge needs no warning - it is just a nudge.
        public static void TestNoWarningNeededWhenTheUserIsAlreadyIdle()
        {
            Assert.Equal(NudgeDecision.Go, Decide(300, 19),
                "at the deadline but nobody is playing");
        }

        public static void TestDoesNotNudgeEarlyJustBecauseTheUserIsIdle()
        {
            // The interval still governs. Being idle is permission to nudge on
            // time, not a reason to nudge constantly.
            Assert.Equal(NudgeDecision.Go, Decide(300, 0),
                "the caller only asks at interval, so idle always means go");
        }
    }
}
