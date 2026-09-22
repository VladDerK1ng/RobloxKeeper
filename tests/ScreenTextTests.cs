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

        // Chat is read line by line, so the line breaks are the point. Windows'
        // own result text runs every line together with spaces - measured:
        // three lines drawn, one line back - which would hand ChatFeed a whole
        // chat box as a single message.
        public static void TestEachLineOnScreenComesBackAsItsOwnLine()
        {
            if (!ScreenText.Available) return;

            using (Bitmap bmp = new Bitmap(700, 200))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using (Font f = new Font("Segoe UI", 24f))
                {
                    g.DrawString("hello there friend", f, Brushes.Black, 8, 10);
                    g.DrawString("Restock: Rainbow Egg x3", f, Brushes.Black, 8, 70);
                    g.DrawString("third line here", f, Brushes.Black, 8, 130);
                }

                string[] lines = ScreenText.Read(Pixels.FromBitmap(bmp)).Split('\n');
                Assert.Equal(3, lines.Length, "three lines drawn, three lines read");
                Assert.Contains("hello", lines[0], "the top line first");
                Assert.Contains("Rainbow", lines[1], "the middle line on its own");
                Assert.Contains("third", lines[2], "the bottom line last");
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

        // ---------- colour ----------

        static readonly Color ChatPanel = Color.FromArgb(38, 42, 36);
        static readonly Color ChatWhite = Color.FromArgb(235, 235, 235);
        static readonly Color EggRed = Color.FromArgb(120, 25, 35);

        // Parts of a line in different colours, one after another, as Roblox
        // chat draws an egg name inside a white sentence.
        static void Parts(Graphics g, Font f, int x, int y, string[] parts, Color[] colours)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                using (SolidBrush b = new SolidBrush(colours[i])) g.DrawString(parts[i], f, b, x, y);
                x += System.Windows.Forms.TextRenderer.MeasureText(g, parts[i], f, new Size(9999, 99),
                    System.Windows.Forms.TextFormatFlags.NoPadding).Width;
            }
        }

        // The owner's report, measured: the recogniser sees brightness, not
        // colour, and a dark red egg name (54) on the chat panel (40) is
        // nearly the panel's brightness. It read "spawned in Angels!" and
        // nothing of the egg.
        public static void TestColouredTextOnADarkPanelIsRead()
        {
            if (!ScreenText.Available) return;

            using (Bitmap bmp = new Bitmap(480, 140))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(ChatPanel);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (Font f = new Font("Segoe UI", 10.5f))
                {
                    Parts(g, f, 10, 12, new string[] { "A ", "Secret Pure Jellyfish Egg", " spawned in Angels!" },
                          new Color[] { ChatWhite, EggRed, ChatWhite });
                    Parts(g, f, 10, 92, new string[] { "bob: anyone trading?" }, new Color[] { ChatWhite });
                }

                string text = ScreenText.Read(Pixels.FromBitmap(bmp));
                Assert.Contains("Jellyfish", text, "the egg's name, in dark red");
                Assert.Contains("spawned in Angels", text, "and the white words around it");
                Assert.Contains("anyone trading", text, "and the plain line below");
            }
        }

        // Turning colour into brightness makes red or blue text on white
        // vanish - measured: both lines read in full as they are, and not at
        // all turned. Whatever reads the colour must not lose this.
        public static void TestColouredTextOnWhiteIsStillRead()
        {
            if (!ScreenText.Available) return;

            using (Bitmap bmp = new Bitmap(480, 100))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (Font f = new Font("Segoe UI", 12f, FontStyle.Bold))
                {
                    using (SolidBrush r = new SolidBrush(Color.FromArgb(230, 20, 20))) g.DrawString("SELL YOUR PETS HERE", f, r, 10, 10);
                    using (SolidBrush u = new SolidBrush(Color.FromArgb(20, 60, 230))) g.DrawString("Daily reward ready", f, u, 10, 55);
                }

                string text = ScreenText.Read(Pixels.FromBitmap(bmp));
                Assert.Contains("SELL YOUR PETS", text, "red on white");
                Assert.Contains("Daily reward", text, "blue on white");
            }
        }

        // A whole window has both at once: a dark chat panel with a coloured
        // egg name on one side, dark-red text on a light sign on the other.
        // Each way of reading gets one of them right, so the choice has to be
        // made line by line, not for the picture as a whole.
        public static void TestColouredChatAndTextOnWhiteAreBothReadInOnePicture()
        {
            if (!ScreenText.Available) return;

            using (Bitmap bmp = new Bitmap(960, 140))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(ChatPanel);
                g.FillRectangle(Brushes.White, 500, 0, 460, 140);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (Font f = new Font("Segoe UI", 10.5f))
                {
                    Parts(g, f, 10, 12, new string[] { "A ", "Secret Pure Jellyfish Egg", " spawned in Angels!" },
                          new Color[] { ChatWhite, EggRed, ChatWhite });
                    Parts(g, f, 10, 92, new string[] { "bob: anyone trading?" }, new Color[] { ChatWhite });
                }
                using (Font f = new Font("Segoe UI", 12f, FontStyle.Bold))
                using (SolidBrush r = new SolidBrush(Color.FromArgb(230, 20, 20)))
                    g.DrawString("SELL YOUR PETS HERE", f, r, 520, 60);

                string text = ScreenText.Read(Pixels.FromBitmap(bmp));
                Assert.Contains("Jellyfish", text, "the coloured egg name on the dark panel");
                Assert.Contains("SELL YOUR PETS", text, "and the red sign on white, in the same picture");
            }
        }

        public static void TestColourBecomesTheBrightnessOfItsBrightestPart()
        {
            Pixels p = new Pixels(2, 1);
            p.Set(0, 0, EggRed.ToArgb());
            p.Set(1, 0, ChatPanel.ToArgb());
            Pixels grey = ScreenText.ColourAsBrightness(p);
            Assert.Equal(Color.FromArgb(120, 120, 120).ToArgb(), grey.At(0, 0), "dark red is as bright as its red");
            Assert.Equal(Color.FromArgb(42, 42, 42).ToArgb(), grey.At(1, 0), "the panel stays dark");
        }

        // ---------- putting two readings together, line by line ----------

        static TextLine Line(int x, int y, int w, string text)
        {
            return new TextLine(new Rectangle(x, y, w, 20), text);
        }

        static string Merged(TextLine[] asItIs, TextLine[] inColour)
        {
            return ScreenText.Join(ScreenText.Merge(asItIs, inColour));
        }

        // The same line read better replaces the poorer reading of it, once.
        public static void TestAFullerReadingOfALineReplacesThePoorerOne()
        {
            Assert.Equal("A Secret Pure Jellyfish Egg spawned in Angels!",
                Merged(new TextLine[] { Line(60, 10, 120, "spawned in Angels!") },
                       new TextLine[] { Line(10, 10, 300, "A Secret Pure Jellyfish Egg spawned in Angels!") }),
                "the reading that found the egg, and only it");
        }

        // Measured on the owner's live spawn banner: as it is, the red word
        // came back garbled and in a piece of its own - "spawned in" and
        // "venuons", a letter MORE than the colour reading's correct "spawned
        // in Demons". Counting letters alone keeps the wrong one.
        public static void TestAColouredWordReadRightIsNotLostToAGarbledOne()
        {
            Assert.Equal("spawned in Demons",
                Merged(new TextLine[] { Line(40, 10, 150, "spawned in"), Line(200, 10, 110, "venuons") },
                       new TextLine[] { Line(40, 10, 270, "spawned in Demons") }),
                "the reading that got the coloured word right");
        }

        public static void TestAPoorerReadingOfALineDoesNotReplaceABetterOne()
        {
            Assert.Equal("SELL YOUR PETS HERE",
                Merged(new TextLine[] { Line(10, 10, 200, "SELL YOUR PETS HERE") },
                       new TextLine[] { Line(10, 10, 200, "SEL") }),
                "kept as it was");
        }

        // Each reading finds things the other misses; both are kept.
        public static void TestALineOnlyOneReadingFoundIsKept()
        {
            string merged = Merged(new TextLine[] { Line(520, 60, 200, "SELL YOUR PETS HERE") },
                                   new TextLine[] { Line(10, 10, 300, "A Secret Pure Jellyfish Egg spawned in Angels!") });
            Assert.Contains("SELL YOUR PETS HERE", merged, "found only as it is");
            Assert.Contains("Jellyfish", merged, "found only in colour");
        }

        public static void TestLinesComeBackTopToBottomThenLeftToRight()
        {
            Assert.Equal("A Secret Pure Jellyfish Egg spawned in Angels!\nVladDerKing\n152B\nbob: anyone trading?",
                Merged(new TextLine[] { Line(10, 90, 200, "bob: anyone trading?"), Line(300, 51, 60, "152B") },
                       new TextLine[] { Line(10, 10, 280, "A Secret Pure Jellyfish Egg spawned in Angels!"),
                                        Line(10, 50, 120, "VladDerKing") }),
                "in the order they are on screen");
        }
    }
}
