using System;
using System.Collections.Generic;
using System.Drawing;

namespace RobloxKeeper.Tests
{
    // Making every client the size of the first, so boxes and macro clicks
    // land exactly the same on all of them.
    static class WindowSizerTests
    {
        static SizedWindow W(int id, int x, int y, int w, int h)
        {
            SizedWindow s = new SizedWindow();
            s.Hwnd = new IntPtr(id);
            s.Bounds = new Rectangle(x, y, w, h);
            return s;
        }

        public static void TestEveryOtherClientGetsTheFirstOnesSize()
        {
            string said;
            List<KeyValuePair<IntPtr, Size>> plan = WindowSizer.Plan(new SizedWindow[] {
                W(1, 0, 0, 1280, 720), W(2, 300, 200, 800, 600), W(3, 50, 50, 1936, 1048) }, out said);
            Assert.Equal(2, plan.Count, "the two others");
            Assert.Equal(new IntPtr(2), plan[0].Key, "the second");
            Assert.Equal(new Size(1280, 720), plan[0].Value, "to the first one's size");
            Assert.Equal(new Size(1280, 720), plan[1].Value, "and the third");
            Assert.Contains("2 clients", said, "says how many");
            Assert.Contains("1280 x 720", said, "and what size");
        }

        public static void TestClientsAlreadyTheSameSizeAreLeftAlone()
        {
            string said;
            List<KeyValuePair<IntPtr, Size>> plan = WindowSizer.Plan(new SizedWindow[] {
                W(1, 0, 0, 1280, 720), W(2, 300, 200, 1280, 720) }, out said);
            Assert.Equal(0, plan.Count, "nothing to do");
            Assert.Contains("already", said, "and says so");
        }

        // A minimized or full-screen first client has no ordinary size to
        // copy - copying full screen would cover the whole screen with every
        // client.
        public static void TestTheFirstClientMustBeAnOrdinaryWindow()
        {
            string said;
            SizedWindow first = W(1, 0, 0, 1936, 1048);
            first.Maximized = true;
            Assert.Equal(0, WindowSizer.Plan(new SizedWindow[] { first, W(2, 0, 0, 800, 600) }, out said).Count, "full screen");
            Assert.Contains("ordinary window", said, "says what to do");

            first.Maximized = false;
            first.Minimized = true;
            Assert.Equal(0, WindowSizer.Plan(new SizedWindow[] { first, W(2, 0, 0, 800, 600) }, out said).Count, "minimized");
        }

        public static void TestMinimizedOrFullScreenOthersAreLeftAsTheyAre()
        {
            string said;
            SizedWindow min = W(2, 0, 0, 800, 600);
            min.Minimized = true;
            SizedWindow max = W(3, 0, 0, 1936, 1048);
            max.Maximized = true;
            List<KeyValuePair<IntPtr, Size>> plan = WindowSizer.Plan(new SizedWindow[] {
                W(1, 0, 0, 1280, 720), min, max, W(4, 0, 0, 800, 600) }, out said);
            Assert.Equal(1, plan.Count, "only the ordinary one");
            Assert.Contains("2 left as they are", said, "and the others are mentioned");
        }

        public static void TestOneClientHasNothingToMatch()
        {
            string said;
            Assert.Equal(0, WindowSizer.Plan(new SizedWindow[] { W(1, 0, 0, 1280, 720) }, out said).Count, "nothing");
            Assert.Contains("one client", said, "says why");
        }

        // Measured live: two Roblox processes running in the tray, neither
        // with a window. That is not "one client".
        public static void TestNoWindowsAtAllIsSaidAsSuch()
        {
            string said;
            WindowSizer.Plan(new SizedWindow[0], out said);
            Assert.Contains("No client window", said, "none open");
        }
    }
}
