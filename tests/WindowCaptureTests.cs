using System;
using System.Diagnostics;

namespace RobloxKeeper.Tests
{
    // Whether a client can be watched, and what to say when it cannot.
    //
    // The capture itself is four lines of Win32 with no decisions in it. The
    // decisions are all about which clients are readable, and those get tested
    // here - because a client that silently reports nothing forever looks
    // exactly like a watcher that does not work.
    static class WindowCaptureTests
    {
        public static void TestAClientInAGameIsWatchable()
        {
            Assert.Equal(WatchState.Watchable,
                WindowCapture.StateOf(true, false, 1936, 1048), "a normal game window");
        }

        // Behind other windows is fine - the compositor still has the frame.
        // This is the whole reason watching costs a client nothing.
        public static void TestBeingHiddenBehindOtherWindowsIsStillWatchable()
        {
            Assert.Equal(WatchState.Watchable,
                WindowCapture.StateOf(true, false, 800, 600), "hidden is not minimized");
        }

        // Windows stops updating the surface of a minimized window, so there
        // is genuinely nothing to read.
        public static void TestAMinimizedClientCannotBeWatched()
        {
            Assert.Equal(WatchState.Minimized,
                WindowCapture.StateOf(true, true, 1936, 1048), "nothing is being drawn");
        }

        // What Windows actually reports for a minimized window, measured: a
        // 160x28 rectangle parked at -32000,-32000. Checking the size first
        // would call that "not in a game" and send the user off to wait for a
        // client that is already in one.
        public static void TestAMinimizedClientIsReportedAsMinimizedAtTheSizeWindowsGivesIt()
        {
            Assert.Equal(WatchState.Minimized,
                WindowCapture.StateOf(true, true, 160, 28), "minimized, not still launching");
        }

        // Finding the window has the same trap: a minimized game window is
        // small, and skipping small windows would lose it entirely.
        public static void TestAMinimizedGameWindowIsStillFound()
        {
            Assert.True(WindowCapture.IsGameWindow("WINDOWSCLIENT", true, 160),
                "the game, minimized");
        }

        public static void TestTheGameWindowIsFoundAtItsNormalSize()
        {
            Assert.True(WindowCapture.IsGameWindow("WINDOWSCLIENT", false, 1936), "the game");
        }

        // The tray host shares the process and the class is not the only
        // thing that tells them apart.
        public static void TestASmallWindowThatIsNotMinimizedIsNotTheGame()
        {
            Assert.False(WindowCapture.IsGameWindow("WINDOWSCLIENT", false, 16),
                "a 16-pixel helper window");
        }

        public static void TestAnotherKindOfWindowIsNeverTheGame()
        {
            Assert.False(WindowCapture.IsGameWindow("SomethingElse", false, 1936),
                "right size, wrong window");
            Assert.False(WindowCapture.IsGameWindow(null, false, 1936), "no class at all");
        }

        // A tray client has no game window at all.
        public static void TestAClientWithNoWindowCannotBeWatched()
        {
            Assert.Equal(WatchState.NotInAGame,
                WindowCapture.StateOf(false, false, 0, 0), "in the tray, not in a game");
        }

        // A window that exists but has no area is still launching.
        public static void TestAWindowWithNoAreaCountsAsNotInAGameYet()
        {
            Assert.Equal(WatchState.NotInAGame,
                WindowCapture.StateOf(true, false, 0, 0), "nothing to read yet");
            Assert.Equal(WatchState.NotInAGame,
                WindowCapture.StateOf(true, false, 120, 40), "too small to be a game");
        }

        // Every state has to say something a person can act on, or the list
        // just shows a blank next to a client and tells them nothing.
        public static void TestEveryStateExplainsItself()
        {
            foreach (WatchState s in (WatchState[])Enum.GetValues(typeof(WatchState)))
            {
                string why = WindowCapture.Explain(s);
                Assert.True(!string.IsNullOrEmpty(why), "something is said for " + s);
                Assert.True(why.Length > 8, "and it is a sentence, not a word: " + s);
            }
        }

        public static void TestTheMinimizedExplanationSaysWhatToDoAboutIt()
        {
            Assert.Contains("minimi", WindowCapture.Explain(WatchState.Minimized),
                "names the actual problem");
        }

        // Asking about a process that is not there is a normal thing to do on
        // a tick where a client just closed.
        public static void TestLookingForAWindowOfAProcessThatIsGoneIsNotAnError()
        {
            Assert.Equal(IntPtr.Zero, WindowCapture.GameWindow(0), "no such process");
            Assert.Equal(IntPtr.Zero, WindowCapture.GameWindow(-1), "not even a valid id");
        }

        public static void TestGrabbingNothingReturnsNothing()
        {
            Pixels p = WindowCapture.Grab(IntPtr.Zero);
            Assert.True(p == null || p.IsEmpty, "no window, no pixels, no exception");
        }

        // The real thing, when there is a client in a game to try it on.
        // Skips cleanly when there is not, because most machines running the
        // tests will not have one.
        public static void TestItCapturesALiveClientWithoutTouchingIt()
        {
            Process[] all = Process.GetProcessesByName(ClientTracker.ROBLOX_PROCESS);
            try
            {
                foreach (Process p in all)
                {
                    IntPtr h = WindowCapture.GameWindow(p.Id);
                    if (h == IntPtr.Zero) continue;

                    Pixels shot = WindowCapture.Grab(h);
                    Assert.True(shot != null && !shot.IsEmpty, "got pixels back");
                    Assert.True(shot.Width > 300 && shot.Height > 200, "and a real window's worth");

                    // A Direct3D window captured with the wrong flag comes
                    // back uniformly black. Anything else proves the frame is
                    // genuinely there.
                    Assert.True(NotAllOneColour(shot), "a real frame, not an empty rectangle");
                    return;
                }
            }
            finally { foreach (Process p in all) p.Dispose(); }
        }

        static bool NotAllOneColour(Pixels p)
        {
            int first = p.At(0, 0);
            for (int y = 0; y < p.Height; y += 13)
                for (int x = 0; x < p.Width; x += 13)
                    if (p.At(x, y) != first) return true;
            return false;
        }
    }
}
