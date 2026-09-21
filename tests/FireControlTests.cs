using System;

namespace RobloxKeeper.Tests
{
    // A leaderboard sits on screen permanently. Matching it every scan and
    // reporting every match would be forty webhooks in ten seconds, so a
    // watcher fires on the moment something ARRIVES, not while it is present.
    //
    // Confirming over consecutive scans is the other half: OCR flickers, and
    // one bad frame should not be news.
    static class FireControlTests
    {
        static readonly DateTime T0 = new DateTime(2026, 9, 21, 12, 0, 0);
        static DateTime At(double seconds) { return T0.AddSeconds(seconds); }

        static FireControl Fresh()
        {
            return new FireControl(2, TimeSpan.FromSeconds(30));
        }

        // Two scans to confirm, so the first sighting alone is not enough.
        public static void TestOneSightingIsNotEnough()
        {
            FireControl f = Fresh();
            Assert.False(f.Update(true, At(0)), "seen once - wait for confirmation");
        }

        public static void TestTwoInARowFires()
        {
            FireControl f = Fresh();
            f.Update(true, At(0));
            Assert.True(f.Update(true, At(0.25)), "confirmed, fire");
        }

        // The whole point: still on screen is not news.
        public static void TestStayingOnScreenDoesNotFireAgain()
        {
            FireControl f = Fresh();
            f.Update(true, At(0));
            f.Update(true, At(0.25));
            for (int i = 2; i < 40; i++)
                Assert.False(f.Update(true, At(0.25 * i)), "still there, scan " + i);
        }

        // One dropped frame in the middle must not count as a confirmation.
        public static void TestFlickerDoesNotConfirm()
        {
            FireControl f = Fresh();
            f.Update(true, At(0));
            f.Update(false, At(0.25));
            Assert.False(f.Update(true, At(0.5)), "the run was broken, start again");
            Assert.True(f.Update(true, At(0.75)), "two clean scans, now fire");
        }

        // Gone and genuinely back is news again - but only after it has been
        // absent long enough to be sure it really went.
        public static void TestItRearmsAfterTwoAbsentScans()
        {
            FireControl f = Fresh();
            f.Update(true, At(0));
            f.Update(true, At(0.25));          // fired
            f.Update(false, At(31));
            f.Update(false, At(31.25));        // absent twice - rearmed
            f.Update(true, At(31.5));
            Assert.True(f.Update(true, At(31.75)), "came back, and the cooldown has passed");
        }

        public static void TestOneAbsentScanIsNotEnoughToRearm()
        {
            FireControl f = Fresh();
            f.Update(true, At(0));
            f.Update(true, At(0.25));
            f.Update(false, At(31));
            f.Update(true, At(31.25));
            Assert.False(f.Update(true, At(31.5)), "it never really left");
        }

        // The cooldown stops a thing that flickers in and out every second
        // from being reported every second.
        public static void TestTheCooldownBlocksAnEarlySecondFiring()
        {
            FireControl f = Fresh();
            f.Update(true, At(0));
            f.Update(true, At(0.25));
            f.Update(false, At(1));
            f.Update(false, At(1.25));
            f.Update(true, At(2));
            Assert.False(f.Update(true, At(2.25)), "inside the 30 second cooldown");
        }

        public static void TestTheCooldownBoundaryCountsAsExpired()
        {
            FireControl f = Fresh();
            f.Update(true, At(0));
            f.Update(true, At(0.25));          // fired at 0.25
            f.Update(false, At(1));
            f.Update(false, At(1.25));
            f.Update(true, At(30));
            Assert.True(f.Update(true, At(30.25)), "exactly 30s later is allowed");
        }

        // Confirm of 1 means fire on sight, which is what someone who wants
        // speed over certainty will set.
        public static void TestConfirmOfOneFiresImmediately()
        {
            FireControl f = new FireControl(1, TimeSpan.FromSeconds(30));
            Assert.True(f.Update(true, At(0)), "no confirmation wanted");
        }

        // A nonsense setting must not produce a watcher that never fires.
        public static void TestConfirmOfZeroIsTreatedAsOne()
        {
            FireControl f = new FireControl(0, TimeSpan.FromSeconds(30));
            Assert.True(f.Update(true, At(0)), "zero would otherwise never fire");
        }

        // Before anything has fired there is no cooldown to serve, so a
        // watcher must not be silent on its very first sighting.
        public static void TestTheFirstFiringIsNotBlockedByACooldown()
        {
            FireControl f = new FireControl(1, TimeSpan.FromHours(1));
            Assert.True(f.Update(true, At(0)), "nothing has fired yet");
        }
    }
}
