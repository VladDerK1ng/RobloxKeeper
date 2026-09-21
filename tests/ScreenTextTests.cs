using System.Drawing;

namespace RobloxKeeper.Tests
{
    // Reading text out of a picture.
    //
    // The recognition itself is Windows' and there is nothing here worth
    // testing about it. What IS worth testing is everything around it: that a
    // machine without the language pack says so instead of silently finding
    // nothing forever, that an empty or absurd image does not throw, and that
    // the install command we offer is the right one.
    //
    // These tests run against the real engine when the machine has it and
    // skip cleanly when it does not, because a build server generally will
    // not.
    static class ScreenTextTests
    {
        static Pixels Blank(int w, int h)
        {
            Pixels p = new Pixels(w, h);
            p.Fill(unchecked((int)0xFF000000));
            return p;
        }

        // Whatever the answer is, asking must not throw - this is called once
        // at startup before anything else exists.
        public static void TestAskingWhetherItIsAvailableNeverThrows()
        {
            bool available = ScreenText.Available;
            Assert.True(available || !available, "it answered one way or the other");
        }

        // The answer must not change from one call to the next, or the UI
        // would offer the install button intermittently.
        public static void TestAvailabilityIsStable()
        {
            Assert.Equal(ScreenText.Available, ScreenText.Available, "the same answer twice");
        }

        // A user who has not got it needs to be told what to do, and the
        // command has to be the real one.
        public static void TestTheInstallCommandIsTheRealOne()
        {
            string cmd = ScreenText.InstallCommand;
            Assert.Contains("DISM", cmd, "the tool that installs it");
            Assert.Contains("Language.OCR", cmd, "the capability");
            Assert.Contains("en-US", cmd, "the language");
        }

        // An empty region is a normal thing to hand it - a projected box can
        // clamp to nothing on a window that shrank - and it must come back as
        // no text rather than an exception.
        public static void TestAnEmptyImageReadsAsNothing()
        {
            Assert.Equal("", ScreenText.Read(new Pixels(0, 0)), "nothing in, nothing out");
            Assert.Equal("", ScreenText.Read(null), "and nothing at all is also fine");
        }

        // A picture with no writing in it is not a failure.
        public static void TestAPlainBlackImageReadsAsNothing()
        {
            if (!ScreenText.Available) return;   // no engine on this machine
            Assert.Equal("", ScreenText.Read(Blank(80, 40)).Trim(), "no text to find");
        }

        // The real thing, end to end: render known words and read them back.
        // This is what proves the WinRT plumbing works without the Task
        // bridge, which is the part most likely to break the build.
        public static void TestItReadsWordsItWasGiven()
        {
            if (!ScreenText.Available) return;

            using (Bitmap bmp = new Bitmap(420, 90))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using (Font f = new Font("Segoe UI", 28f))
                    g.DrawString("SECRET EGG", f, Brushes.Black, 8, 20);

                string text = ScreenText.Read(Pixels.FromBitmap(bmp));
                Assert.Contains("SECRET", text, "the first word");
                Assert.Contains("EGG", text, "the second word");
            }
        }

        // Reading is done several times a second, so the engine has to be
        // built once and kept, not rebuilt per call. Two reads in a row is
        // enough to catch it being disposed or recreated wrongly.
        public static void TestReadingTwiceInARowWorks()
        {
            if (!ScreenText.Available) return;
            ScreenText.Read(Blank(40, 20));
            Assert.Equal("", ScreenText.Read(Blank(40, 20)).Trim(), "still fine the second time");
        }
    }
}
