using System;
using System.Drawing;

namespace RobloxKeeper.Tests
{
    // How much damage a nudge can do when it has to happen.
    //
    // Waiting for a lull covers the common case, but a client near the idle kick
    // overrides that, and then focus moves whatever the user is doing. Two things
    // decide how bad that is: how long focus is held, and whether the thing it
    // interrupts was a fullscreen game.
    static class NudgeDisruptionTests
    {
        // ---------- how long focus is held ----------

        public static void TestEveryMethodFitsInAShortBlip()
        {
            // A second and a half per client was long enough to lose a fight.
            // Anything here should read as a flicker, not an alt-tab.
            for (int i = 0; i < NudgeMethod.Count; i++)
            {
                int ms = NudgeMethod.TotalMs(NudgeMethod.StepsFor(i, 0x57));
                Assert.True(ms <= 500,
                    NudgeMethod.Name(i) + " takes " + ms + "ms of held focus, budget is 500");
            }
        }

        public static void TestKeysAreStillHeldLongEnoughToRegister()
        {
            // Too short and the client misses it between frames. At 60fps a
            // frame is ~17ms, so a hold needs to span several.
            for (int i = 0; i < NudgeMethod.Count; i++)
                foreach (NudgeStep s in NudgeMethod.StepsFor(i, 0x57))
                    if (!s.IsMouse)
                        Assert.True(s.HoldMs >= 50,
                            NudgeMethod.Name(i) + " holds a key for only " + s.HoldMs + "ms");
        }

        public static void TestTotalOfAnEmptySequenceIsZero()
        {
            Assert.Equal(0, NudgeMethod.TotalMs(new NudgeStep[0]), "nothing takes no time");
        }

        // ---------- what it would interrupt ----------

        static Rectangle Screen() { return new Rectangle(0, 0, 1920, 1080); }

        public static void TestAWindowCoveringTheWholeScreenIsAFullscreenGame()
        {
            Assert.True(NudgePolicy.LooksFullscreen(new Rectangle(0, 0, 1920, 1080), Screen()),
                "exactly the screen");
        }

        public static void TestAWindowSpillingPastTheScreenEdgesCountsToo()
        {
            // Borderless fullscreen often overhangs by a pixel or two.
            Assert.True(NudgePolicy.LooksFullscreen(new Rectangle(-1, -1, 1922, 1082), Screen()),
                "borderless fullscreen");
        }

        public static void TestAMaximisedWindowIsNotTreatedAsFullscreen()
        {
            // A maximised window leaves the taskbar visible, so it is not the
            // exclusive-fullscreen case worth protecting.
            Assert.False(NudgePolicy.LooksFullscreen(new Rectangle(0, 0, 1920, 1032), Screen()),
                "maximised, taskbar still showing");
        }

        public static void TestAnOrdinaryWindowIsNotFullscreen()
        {
            Assert.False(NudgePolicy.LooksFullscreen(new Rectangle(100, 100, 900, 600), Screen()),
                "a normal window");
        }

        public static void TestAnEmptyRectIsNotFullscreen()
        {
            Assert.False(NudgePolicy.LooksFullscreen(Rectangle.Empty, Screen()),
                "nothing in the foreground");
        }

        // ---------- the two combined ----------

        public static void TestAFullscreenGameIsProtectedEvenWhenTheUserSeemsIdle()
        {
            // Watching a cutscene or holding a position counts as playing, and
            // GetLastInputInfo cannot tell that from being away.
            NudgeDecision d = NudgePolicy.Decide(
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
                TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(19), true);

            Assert.Equal(NudgeDecision.Defer, d, "a fullscreen game is not interrupted on a hunch");
        }

        public static void TestTheDeadlineStillOverridesAFullscreenGame()
        {
            NudgeDecision d = NudgePolicy.Decide(
                TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(19),
                TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(19), true);

            Assert.Equal(NudgeDecision.Urgent, d, "losing the account still beats losing the round");
        }

        public static void TestNothingChangesWhenNoFullscreenGameIsInFront()
        {
            NudgeDecision d = NudgePolicy.Decide(
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
                TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(19), false);

            Assert.Equal(NudgeDecision.Go, d, "idle at the desktop, nudge away");
        }
    }
}
