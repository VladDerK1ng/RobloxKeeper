using System;

namespace RobloxKeeper.Tests
{
    // How long a lull has to be before a nudge may take the foreground.
    //
    // 45 seconds was too cautious. Most of the time the window in front is a
    // browser or a chat app, where a 300ms flicker costs nothing, and waiting
    // three quarters of a minute for a gap that long just meant nudges kept
    // being deferred. AntiAFK-RBX uses three seconds for the same check.
    //
    // What makes a short threshold safe here is the thing that project has no
    // equivalent of: a fullscreen game in front is protected outright, no
    // matter how idle the keyboard looks. The two settings only make sense
    // together, which is why they are tested together.
    static class NudgeThresholdTests
    {
        public static void TestTheShippedThresholdIsShort()
        {
            Assert.True(NudgePolicy.DefaultQuiet <= TimeSpan.FromSeconds(10),
                "a lull should be seconds, not the better part of a minute - was "
                + NudgePolicy.DefaultQuiet.TotalSeconds + "s");
        }

        public static void TestTheShippedThresholdIsNotZero()
        {
            // Zero would mean nudging the instant a keystroke lands, which is
            // worse than the old behaviour, not better.
            Assert.True(NudgePolicy.DefaultQuiet >= TimeSpan.FromSeconds(2),
                "still long enough to be a genuine pause");
        }

        public static void TestTheDeadlineStaysUnderRobloxsIdleKick()
        {
            // Roblox kicks at 20 minutes - measured, reason 278. The deadline
            // has to leave room to actually perform the nudge before then.
            Assert.True(NudgePolicy.DefaultDeadline < TimeSpan.FromMinutes(20),
                "must fire before the kick, not with it");
            Assert.True(NudgePolicy.DefaultDeadline >= TimeSpan.FromMinutes(15),
                "but not so early that it defeats the point of deferring");
        }

        public static void TestAShortLullIsEnoughOutsideAGame()
        {
            NudgeDecision d = NudgePolicy.Decide(
                NudgePolicy.DefaultQuiet, TimeSpan.FromMinutes(15),
                NudgePolicy.DefaultQuiet, NudgePolicy.DefaultDeadline, false);

            Assert.Equal(NudgeDecision.Go, d, "alt-tabbed to a browser is a fine moment to nudge");
        }

        public static void TestStillPlayingMeansStillDeferred()
        {
            NudgeDecision d = NudgePolicy.Decide(
                TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15),
                NudgePolicy.DefaultQuiet, NudgePolicy.DefaultDeadline, false);

            Assert.Equal(NudgeDecision.Defer, d, "actively typing is not a lull");
        }

        // The pairing that makes the short threshold defensible.
        public static void TestAFullscreenGameOutranksTheShortThreshold()
        {
            NudgeDecision d = NudgePolicy.Decide(
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15),
                NudgePolicy.DefaultQuiet, NudgePolicy.DefaultDeadline, true);

            Assert.Equal(NudgeDecision.Defer, d,
                "ten minutes of no keypresses in a fullscreen game is still playing");
        }
    }
}
