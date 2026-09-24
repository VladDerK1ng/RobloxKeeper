using System;

namespace RobloxKeeper.Tests
{
    // The main window made only to be drawn - for the picture at the top of
    // the README - on a machine where the real app is running. Starting it
    // would queue on Roblox's mutex, put a second icon in the tray, check
    // Start with Windows against the wrong exe and save its settings over
    // the real ones on close.
    static class PictureWindowTests
    {
        public static void TestAWindowMadeToBeDrawnStartsNothing()
        {
            using (MainForm f = new MainForm(false))
            {
                Assert.False(f.Started, "not started");
                Assert.False(f.TrayShowing, "no second tray icon");
                Assert.False(f.HoldsMutex, "not queued on Roblox's mutex");
                f.Close();
            }
        }
    }
}
