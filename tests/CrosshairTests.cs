using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // The crosshair is only worth having if it is exactly in the middle,
    // and only acceptable if it shows where it was asked for and nowhere
    // else. Those are decisions, and they are checked here without a window.
    static class CrosshairTests
    {
        // A shape whose middle is the line before pixel c (2c half pixels)
        // or pixel c itself (2c + 1).
        static Point OnLine(int c) { return new Point(2 * c, 2 * c); }
        static Point OnPixel(int c) { return new Point(2 * c + 1, 2 * c + 1); }

        public static void TestTheCrossSitsOnTheMiddleOfTheGamesPicture()
        {
            // A maximised client as measured live: its picture starts under a
            // 29 pixel title bar. 1920 across: its middle is the line between
            // 959 and 960. 1111 down: pixel 29 + 555 = 584.
            Point at = CrossPlace.TopLeft(new Rectangle(0, 29, 1920, 1111), new Point(50, 51), 0, 0);
            Assert.Equal(960 - 25, at.X, "left edge: the line at 25 lands on the line at 960");
            Assert.Equal(584 - 25, at.Y, "top edge: pixel 25 lands on pixel 584");
        }

        public static void TestItFollowsAWindowThatIsNotAtTheCorner()
        {
            Point at = CrossPlace.TopLeft(new Rectangle(300, 200, 800, 600), OnLine(10), 0, 0);
            Assert.Equal(300 + 400 - 10, at.X, "left edge");
            Assert.Equal(200 + 300 - 10, at.Y, "top edge");
        }

        public static void TestNudgingMovesItByThatManyPixels()
        {
            foreach (Point m in new Point[] { OnLine(10), OnPixel(10), new Point(20, 21) })
                foreach (Rectangle game in new Rectangle[] { new Rectangle(0, 0, 1000, 1000), new Rectangle(-1921, -7, 1111, 999) })
                {
                    Point mid = CrossPlace.TopLeft(game, m, 0, 0);
                    Point moved = CrossPlace.TopLeft(game, m, 3, -18);
                    Assert.Equal(mid.X + 3, moved.X, "across, whole pixels");
                    Assert.Equal(mid.Y - 18, moved.Y, "up, whole pixels");
                }
        }

        // Where the shape's middle ends up, minus the game's true middle, in
        // half pixels, across. 0 is exact.
        static int Off(Rectangle game, Point middle2)
        {
            Point at = CrossPlace.TopLeft(game, middle2, 0, 0);
            return 2 * at.X + middle2.X - (2 * game.Left + game.Width);
        }

        static int OffDown(Rectangle game, Point middle2)
        {
            Point at = CrossPlace.TopLeft(game, middle2, 0, 0);
            return 2 * at.Y + middle2.Y - (2 * game.Top + game.Height);
        }

        // Even or odd game, even or odd thickness: exact when they match,
        // otherwise half a pixel - and always to the same side.
        public static void TestTheMiddleIsExactWhenEvenMatchesEvenAndHalfAPixelOffOtherwise()
        {
            foreach (int wide in new int[] { 1920, 1111 })
                for (int thick = 1; thick <= 4; thick++)
                {
                    CrossStyle s = Plain(CrossShape.Cross);
                    s.Thick = thick;
                    Point m = CrossArt.Middle2(s, wide % 2 == 0, wide % 2 == 0);
                    Rectangle game = new Rectangle(0, 0, wide, wide);
                    bool match = (wide % 2 == 0) == (thick % 2 == 0);
                    Assert.Equal(match ? 0 : -1, Off(game, m), wide + " wide, " + thick + " thick: across");
                    Assert.Equal(match ? 0 : -1, OffDown(game, m), wide + " high, " + thick + " thick: down");
                }
        }

        // The middle of what is drawn really is where Middle2 says: the
        // picture is a mirror image about it.
        public static void TestACrossIsDrawnAboutTheMiddleItSays()
        {
            for (int thick = 1; thick <= 4; thick++)
            {
                CrossStyle s = Plain(CrossShape.Cross);
                s.Thick = thick; s.Outline = true;
                AssertMirrored(s, true, true, "cross " + thick + " thick");
            }
        }

        // Pixel x and its mirror about a middle of m2 half pixels.
        static void AssertMirrored(CrossStyle s, bool evenWide, bool evenHigh, string what)
        {
            Point m = CrossArt.Middle2(s, evenWide, evenHigh);
            string why;
            using (Bitmap b = CrossArt.Draw(s, evenWide, evenHigh, out why))
            {
                int inked = 0;
                for (int y = 0; y < b.Height; y++)
                    for (int x = 0; x < b.Width; x++)
                    {
                        if (b.GetPixel(x, y).A > 0) inked++;
                        int mx = m.X - 1 - x, my = m.Y - 1 - y;     // 2x+1 and 2mx+1 are the same distance from m.X
                        if (mx >= 0 && mx < b.Width)
                            Assert.True(Close(b.GetPixel(x, y), b.GetPixel(mx, y)), what + ": left mirrors right at " + x + "," + y);
                        else
                            Assert.True(b.GetPixel(x, y).A <= 8, what + ": nothing without a mirror at " + x + "," + y);
                        if (my >= 0 && my < b.Height)
                            Assert.True(Close(b.GetPixel(x, y), b.GetPixel(x, my)), what + ": top mirrors bottom at " + x + "," + y);
                        else
                            Assert.True(b.GetPixel(x, y).A <= 8, what + ": nothing without a mirror at " + x + "," + y);
                    }
                Assert.True(inked > 0, what + ": something is drawn");
            }
        }

        // Smoothing may round a mirrored edge pixel a step differently; a
        // shape half a pixel off differs by far more than this.
        static bool Close(Color a, Color b)
        {
            return Math.Abs(a.A - b.A) <= 8 && Math.Abs(a.R - b.R) <= 8 && Math.Abs(a.G - b.G) <= 8 && Math.Abs(a.B - b.B) <= 8;
        }

        // The smoothed shapes are drawn for the game they go on, so they are
        // exact on both: centred on a line for an even game, a pixel for an odd one.
        public static void TestTheDotAndTheCircleAreCentredExactlyOnEvenAndOddGames()
        {
            foreach (CrossShape shape in new CrossShape[] { CrossShape.Dot, CrossShape.Circle })
                foreach (bool evenWide in new bool[] { true, false })
                    foreach (bool evenHigh in new bool[] { true, false })
                    {
                        CrossStyle s = Plain(shape);
                        s.Size = 5; s.Thick = 2; s.Outline = true; s.Dot = shape == CrossShape.Circle;
                        string what = shape + (evenWide ? " even" : " odd") + " across," + (evenHigh ? " even" : " odd") + " down";
                        AssertMirrored(s, evenWide, evenHigh, what);
                        Rectangle game = new Rectangle(-1920, 29, evenWide ? 1920 : 1111, evenHigh ? 1080 : 1111);
                        Point m = CrossArt.Middle2(s, evenWide, evenHigh);
                        Assert.Equal(0, Off(game, m), what + ": exactly on the middle across");
                        Assert.Equal(0, OffDown(game, m), what + ": exactly on the middle down");
                    }
        }

        // A monitor to the left of the main one has negative coordinates.
        // Half a pixel off must go the same way there as anywhere: a
        // division that rounds toward zero would flip it.
        public static void TestAMonitorToTheLeftRoundsTheSameWay()
        {
            Point oddMiddle = OnPixel(25);      // an odd-thick cross
            foreach (int left in new int[] { -3841, -1920, -1919, -1, 0, 1, 1919 })
            {
                Rectangle game = new Rectangle(left, -1081, 1920, 1080);
                Assert.Equal(-1, Off(game, oddMiddle), "left edge at " + left + ": half a pixel left");
                Assert.Equal(-1, OffDown(game, oddMiddle), "top at -1081: half a pixel up");
                Assert.Equal(0, Off(game, OnLine(25)), "left edge at " + left + ": an even-thick one is exact");
            }
        }

        public static void TestTheUsersPictureIsCentredForTheGameItGoesOn()
        {
            string path = Path.Combine(Path.GetTempPath(), "rk_pic_" + Guid.NewGuid().ToString("n") + ".png");
            using (Bitmap src = new Bitmap(100, 50))
            {
                using (Graphics g = Graphics.FromImage(src)) g.Clear(Color.Red);
                src.Save(path, ImageFormat.Png);
            }
            try
            {
                foreach (bool even in new bool[] { true, false })
                {
                    CrossStyle s = new CrossStyle();
                    s.Shape = CrossShape.Picture; s.Picture = path; s.PictureSize = 40;
                    AssertMirrored(s, even, even, "picture on an " + (even ? "even" : "odd") + " game");
                }
            }
            finally { File.Delete(path); }
        }

        // ---------- while the screen is magnified ----------

        public static void TestWhileZoomedItIsDrawnThatManyTimesSmaller()
        {
            CrossStyle s = new CrossStyle();
            s.Size = 12; s.Gap = 6; s.Thick = 4; s.PictureSize = 64; s.NudgeX = 9; s.NudgeY = -4;
            CrossStyle z = s.ScaledFor(3);
            Assert.Equal(4, z.Size, "size");
            Assert.Equal(2, z.Gap, "gap");
            Assert.Equal(1, z.Thick, "thickness");
            Assert.Equal(21, z.PictureSize, "picture");
            Assert.Equal(12, s.Size, "the style itself is left as it was");
            CrossStyle same = s.ScaledFor(1);
            Assert.Equal(12, same.Size, "not zoomed: the same");
            Assert.Equal(4, same.Thick, "not zoomed: the same thickness");
        }

        public static void TestWhileZoomedNothingShrinksBelowAPixelAndAGapStays()
        {
            CrossStyle s = new CrossStyle();
            s.Size = 2; s.Gap = 1; s.Thick = 1; s.PictureSize = 16;
            CrossStyle z = s.ScaledFor(6);
            Assert.Equal(1, z.Size, "size never under a pixel");
            Assert.Equal(1, z.Thick, "thickness never under a pixel");
            Assert.Equal(1, z.Gap, "a gap that was there stays: none at all fills the middle");
            Assert.True(z.PictureSize >= 1, "picture never under a pixel");
            s.Gap = 0;
            Assert.Equal(0, s.ScaledFor(6).Gap, "no gap stays no gap");
            string why;
            using (Bitmap b = CrossArt.Draw(z, out why)) Assert.True(b != null && b.Width % 2 == 1, "the smallest still draws, with a middle pixel");
        }

        // The magnifier grows the crosshair's window about the game's middle
        // with everything else, so a nudge n shows as n * zoom - exactly
        // where the game's own point under it goes. Divided, it would leave it.
        public static void TestWhileZoomedTheNudgeIsKeptSoItStaysOnTheSameSpot()
        {
            CrossStyle s = new CrossStyle();
            s.NudgeX = 9; s.NudgeY = -4;
            CrossStyle z = s.ScaledFor(4);
            Assert.Equal(9, z.NudgeX, "across");
            Assert.Equal(-4, z.NudgeY, "down");
            Point middle = new Point(960, 584);
            Point off = GlassMath.ZoomOffset(middle, 4);
            int shownAt = (middle.X + z.NudgeX - off.X) * 4, gamesPointAt = (middle.X + s.NudgeX - off.X) * 4;
            Assert.Equal(gamesPointAt, shownAt, "magnified, it is on the game's point it was nudged to");
        }

        // ---------- only while aiming ----------

        public static void TestForAimingItShowsOnlyWhileTheMouseIsHeldInTheMiddle()
        {
            Rectangle game = new Rectangle(0, 29, 1920, 1111);       // middle: the line at x 960, pixel 584
            Assert.True(CrossPlace.AimShows(true, new Point(960, 584), game), "held in the middle");
            Assert.True(CrossPlace.AimShows(true, new Point(959, 584), game), "the other pixel beside the middle line");
            Assert.True(CrossPlace.AimShows(true, new Point(963, 587), game), "three pixels off");
            Assert.True(CrossPlace.AimShows(true, new Point(956, 581), game), "three pixels off the other way");
            Assert.False(CrossPlace.AimShows(true, new Point(964, 584), game), "four pixels off across: a free mouse");
            Assert.False(CrossPlace.AimShows(true, new Point(960, 589), game), "five pixels off down");
            Assert.False(CrossPlace.AimShows(true, new Point(200, 900), game), "out in a menu");
            Assert.True(CrossPlace.AimShows(false, new Point(200, 900), game), "not for aiming only: always");
        }

        public static void TestForAimingAWindowOnAMonitorToTheLeftWorksToo()
        {
            Rectangle game = new Rectangle(-1280, 100, 1280, 720);  // middle: the line at x -640, y 460
            Assert.True(CrossPlace.AimShows(true, new Point(-640, 460), game), "in the middle");
            Assert.True(CrossPlace.AimShows(true, new Point(-644, 456), game), "three pixels up and left of it");
            Assert.False(CrossPlace.AimShows(true, new Point(-645, 460), game), "four pixels left");
        }

        public static void TestAimingOnlyIsOffUntilChosenAndIsSaved()
        {
            Assert.False(new CrossStyle().AimOnly, "off for a new crosshair");
            Assert.False(ExtrasSettings.Parse(null).Cross.AimOnly, "off when the file never said");
            ExtrasSettings s = new ExtrasSettings();
            s.Cross.AimOnly = true;
            Assert.True(ExtrasSettings.Parse(s.ToLines()).Cross.AimOnly, "read back on");
            Assert.True(s.ToLines().Contains("cross.aimonly=1"), "written as cross.aimonly");
        }

        // ---------- the editor's picture ----------

        public static void TestThePreviewShowsItFourTimesBiggerWhenItFits()
        {
            Size box = new Size(220, 196);
            Assert.Equal(4, CrossPreview.EnlargeFor(new CrossStyle(), box), "the default crosshair");
            CrossStyle big = new CrossStyle();
            big.Size = 60; big.Gap = 30; big.Thick = 10;
            Assert.Equal(1, CrossPreview.EnlargeFor(big, box), "the biggest is shown as it is");
            CrossStyle moved = new CrossStyle();
            moved.NudgeX = 300;
            Assert.Equal(1, CrossPreview.EnlargeFor(moved, box), "moved far off the middle: shown as it is");
            foreach (CrossPreset p in CrossPresets.All)
                Assert.True(CrossPreview.EnlargeFor(p.Style, box) >= 2, p.Name + " is shown bigger");
        }

        // Every pixel a block of z, and the middle stays the middle - an
        // enlargement half a block off would make an exact cross look lopsided.
        public static void TestTheEnlargedPictureIsBlocksAboutTheMiddle()
        {
            using (Bitmap b = new Bitmap(20, 10, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b)) g.Clear(Color.Black);
                b.SetPixel(10, 5, Color.White);     // just right of and below the middle lines
                using (Bitmap big = CrossPreview.Enlarge(b, 4))
                {
                    for (int y = 0; y < 10; y++)
                        for (int x = 0; x < 20; x++)
                        {
                            bool lit = x >= 10 && x < 14 && y >= 5 && y < 9;
                            Assert.Equal(lit ? Color.White.ToArgb() : Color.Black.ToArgb(), big.GetPixel(x, y).ToArgb(), "pixel " + x + "," + y);
                        }
                }
            }
        }

        public static void TestEveryReadyMadeOneHasASingleMiddlePixel()
        {
            foreach (CrossPreset p in CrossPresets.All)
            {
                string why;
                using (Bitmap b = CrossArt.Draw(p.Style, out why))
                {
                    Assert.True(b != null, p.Name + " draws");
                    Assert.Equal(1, b.Width % 2, p.Name + " is an odd number of pixels across");
                    Assert.Equal(b.Width, b.Height, p.Name + " is square");
                    Assert.Equal(CrossArt.SideOf(p.Style), b.Width, p.Name + " is the size it said it would be");
                }
            }
        }

        static CrossStyle Plain(CrossShape shape)
        {
            CrossStyle s = new CrossStyle();
            s.Shape = shape; s.Size = 10; s.Gap = 4; s.Thick = 2; s.Outline = false; s.Dot = false;
            s.Colour = Color.FromArgb(255, 0, 0);
            return s;
        }

        static int Inked(Bitmap b, int x0, int y0, int x1, int y1)
        {
            int n = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (b.GetPixel(x, y).A > 0) n++;
            return n;
        }

        public static void TestACrossHasFourEqualArmsAndAClearMiddle()
        {
            string why;
            using (Bitmap b = CrossArt.Draw(Plain(CrossShape.Cross), out why))
            {
                int c = b.Width / 2, end = b.Width - 1;
                Assert.Equal(0, (int)b.GetPixel(c, c).A, "the middle pixel is clear");
                int left = Inked(b, 0, 0, c - 1, end), right = Inked(b, c + 1, 0, end, end);
                int up = Inked(b, 0, 0, end, c - 4), down = Inked(b, 0, c + 4, end, end);
                Assert.True(left > 0, "there is a left arm");
                Assert.Equal(10 * 2, Inked(b, 0, c - 1, c - 4, c), "the left arm is 10 long and 2 thick");
                Assert.Equal(10 * 2, Inked(b, c + 4, c - 1, end, c), "the right arm is 10 long and 2 thick");
                Assert.Equal(up, down, "up and down arms are the same size");
                Assert.Equal(Color.FromArgb(255, 255, 0, 0).ToArgb(), b.GetPixel(c - 6, c).ToArgb(), "arms are the chosen colour, unsmoothed");
            }
        }

        // The fault a user saw: the arms sat a pixel to one side of the bar
        // they belong to. Whatever the thickness, the left half must be the
        // mirror of the right, and the top of the bottom.
        public static void TestACrossIsTheSameOnBothSidesAtEveryThickness()
        {
            for (int thick = 1; thick <= 5; thick++)
                foreach (bool edge in new bool[] { false, true })
                {
                    CrossStyle s = Plain(CrossShape.Cross);
                    s.Thick = thick; s.Outline = edge; s.Gap = thick == 5 ? 0 : 4;
                    string why;
                    using (Bitmap b = CrossArt.Draw(s, out why))
                    {
                        int c = b.Width / 2, far = thick % 2 == 0 ? c - 1 : c;      // the pixel that mirrors pixel c
                        for (int y = 0; y < b.Height; y++)
                            for (int k = 0; c + k < b.Width && far - k >= 0; k++)
                            {
                                Assert.Equal(b.GetPixel(c + k, y).ToArgb(), b.GetPixel(far - k, y).ToArgb(), "thickness " + thick + ": left mirrors right at row " + y);
                                Assert.Equal(b.GetPixel(y, c + k).ToArgb(), b.GetPixel(y, far - k).ToArgb(), "thickness " + thick + ": top mirrors bottom at column " + y);
                            }
                    }
                }
        }

        public static void TestATShapeHasNoArmGoingUp()
        {
            string why;
            using (Bitmap b = CrossArt.Draw(Plain(CrossShape.Tee), out why))
            {
                int c = b.Width / 2, end = b.Width - 1;
                Assert.Equal(0, Inked(b, c - 1, 0, c, c - 4), "nothing above the gap");
                Assert.True(Inked(b, c - 1, c + 4, c, end) > 0, "an arm below it");
            }
        }

        public static void TestADotInTheMiddleFillsTheMiddle()
        {
            CrossStyle s = Plain(CrossShape.Cross);
            s.Dot = true;
            string why;
            using (Bitmap b = CrossArt.Draw(s, out why))
                Assert.True(b.GetPixel(b.Width / 2, b.Width / 2).A > 0, "the middle pixel is inked");
        }

        public static void TestADarkEdgeGoesRoundTheColour()
        {
            CrossStyle s = Plain(CrossShape.Cross);
            s.Outline = true;
            string why;
            using (Bitmap b = CrossArt.Draw(s, out why))
            {
                int c = b.Width / 2;
                Color edge = b.GetPixel(c - 6, c - 2);      // one above the left arm
                Assert.True(edge.A > 0 && edge.R == 0, "a dark pixel beside the arm");
                Assert.Equal(255, (int)b.GetPixel(c - 6, c).R, "the arm itself keeps its colour");
            }
        }

        public static void TestItShowsOnlyOverAGameThatIsInFront()
        {
            Size big = new Size(1280, 720);
            Assert.True(CrossPlace.Shows(true, true, true, false, true, big), "on, Roblox, in front");
            Assert.False(CrossPlace.Shows(false, true, true, false, true, big), "switched off");
            Assert.False(CrossPlace.Shows(true, false, true, false, true, big), "another program's window");
            Assert.False(CrossPlace.Shows(true, true, false, false, true, big), "behind another window");
            Assert.False(CrossPlace.Shows(true, true, true, true, true, big), "minimised");
            Assert.False(CrossPlace.Shows(true, true, true, false, false, big), "a client it was not asked for on");
            Assert.False(CrossPlace.Shows(true, true, true, false, true, new Size(180, 120)), "a window too small to be a game");
        }

        public static void TestWithNobodyNamedItIsOnEveryClient()
        {
            Assert.True(CrossPlace.Chosen(new List<string>(), "Alice", "Client 1"), "nobody named");
            Assert.True(CrossPlace.Chosen(null, null, null), "no list at all");
        }

        public static void TestNamedClientsAreMatchedByAccountOrByLabel()
        {
            List<string> only = new List<string> { "alice", "Client 3" };
            Assert.True(CrossPlace.Chosen(only, "Alice", "Client 1"), "by account, whatever the capitals");
            Assert.True(CrossPlace.Chosen(only, null, "Client 3"), "by label when it has no account");
            Assert.False(CrossPlace.Chosen(only, "Bob", "Client 2"), "someone else");
        }

        public static void TestAStyleSurvivesBeingSavedAndReadBack()
        {
            ExtrasSettings s = new ExtrasSettings();
            s.CrossOn = true; s.CrossKey = 0x78;
            s.Cross.Shape = CrossShape.Circle; s.Cross.Size = 17; s.Cross.Gap = 9; s.Cross.Thick = 3;
            s.Cross.Colour = Color.FromArgb(12, 200, 255); s.Cross.Outline = false; s.Cross.Dot = true;
            s.Cross.Opacity = 70; s.Cross.NudgeX = -4; s.Cross.NudgeY = 18;
            s.Only.Add("Alice"); s.Only.Add("Client 2");
            s.KeysOn = true; s.KeysCorner = 3; s.KeysSize = 2;
            s.ZoomOn = true; s.ZoomKey = 0x43; s.ZoomLevel = 2;
            s.DimOn = true; s.DimDark = 2; s.DimHole = 0;
            s.PingOn = true; s.MemoryOn = true; s.StatsCorner = 0;
            s.HoldOn = true; s.HoldKey = 0x11; s.HoldWith = 0x14;
            s.FontName = "Comic Sans MS";

            ExtrasSettings r = ExtrasSettings.Parse(s.ToLines());
            Assert.Equal(string.Join("\n", s.ToLines().ToArray()), string.Join("\n", r.ToLines().ToArray()), "every line comes back the same");
            Assert.Equal(CrossShape.Circle, r.Cross.Shape, "shape");
            Assert.Equal(Color.FromArgb(12, 200, 255).ToArgb(), r.Cross.Colour.ToArgb(), "colour");
            Assert.Equal(2, r.Only.Count, "named clients");
            Assert.Equal((byte)0x14, r.HoldWith, "the key that switches the held key");
        }

        public static void TestNumbersFromAFileAreBroughtBackInsideTheLimits()
        {
            ExtrasSettings r = ExtrasSettings.Parse(new string[]
            {
                "cross.size=99999", "cross.gap=-5", "cross.thick=0", "cross.opacity=0", "cross.nudgex=100000",
                "cross.shape=Banana", "cross.colour=nothex", "keys.corner=9", "zoom.level=77", "dim.dark=-3", "cross.key=4000"
            });
            Assert.Equal(CrossStyle.MAX_SIZE, r.Cross.Size, "size");
            Assert.Equal(0, r.Cross.Gap, "gap");
            Assert.Equal(CrossStyle.MIN_THICK, r.Cross.Thick, "thickness");
            Assert.Equal(CrossStyle.MIN_OPACITY, r.Cross.Opacity, "never fully invisible");
            Assert.Equal(CrossStyle.MAX_NUDGE, r.Cross.NudgeX, "never off the screen");
            Assert.Equal(CrossShape.Cross, r.Cross.Shape, "an unknown shape is the default");
            Assert.Equal(ExtrasSettings.CORNERS - 1, r.KeysCorner, "corner");
            Assert.Equal(ExtrasSettings.ZoomLevels.Length - 1, r.ZoomLevel, "zoom");
            Assert.Equal(0, r.DimDark, "dim");
            Assert.Equal((byte)0, r.CrossKey, "a key number that is not a key");
        }

        public static void TestNothingIsOnUntilItIsSwitchedOn()
        {
            ExtrasSettings fresh = ExtrasSettings.Parse(null);
            Assert.False(fresh.AnyOn, "a PC that never opened these windows has nothing running");
            Assert.Equal((byte)0, fresh.CrossKey, "and no key taken");
        }

        public static void TestZoomWithNoKeyIsOff()
        {
            ExtrasSettings r = ExtrasSettings.Parse(new string[] { "zoom.on=1", "zoom.key=0" });
            Assert.False(r.ZoomOn, "zoom cannot be on without a key to hold");
        }

        public static void TestAMissingPictureSaysWhyInsteadOfThrowing()
        {
            CrossStyle s = new CrossStyle();
            s.Shape = CrossShape.Picture;
            string why;
            Assert.True(CrossArt.Draw(s, out why) == null, "nothing drawn with no picture");
            Assert.Contains("no picture", why, "says none was chosen");
            s.Picture = Path.Combine(Path.GetTempPath(), "rk_no_such_" + Guid.NewGuid().ToString("n") + ".png");
            Assert.True(CrossArt.Draw(s, out why) == null, "nothing drawn for a file that is gone");
            Assert.Contains("no longer", why, "says it has gone");
        }

        public static void TestAFileThatIsNotAPictureIsRefused()
        {
            string path = Path.Combine(Path.GetTempPath(), "rk_notpic_" + Guid.NewGuid().ToString("n") + ".png");
            File.WriteAllText(path, "this is not a picture");
            try
            {
                CrossStyle s = new CrossStyle();
                s.Shape = CrossShape.Picture; s.Picture = path;
                string why;
                Assert.True(CrossArt.Draw(s, out why) == null, "nothing drawn");
                Assert.Contains("isn't a picture", why, "says so");
            }
            finally { File.Delete(path); }
        }

        public static void TestMyOwnPictureIsFittedAndCentred()
        {
            string path = Path.Combine(Path.GetTempPath(), "rk_pic_" + Guid.NewGuid().ToString("n") + ".png");
            using (Bitmap src = new Bitmap(100, 50))
            {
                using (Graphics g = Graphics.FromImage(src)) g.Clear(Color.Red);
                src.Save(path, ImageFormat.Png);
            }
            try
            {
                CrossStyle s = new CrossStyle();
                s.Shape = CrossShape.Picture; s.Picture = path; s.PictureSize = 64;
                string why;
                using (Bitmap b = CrossArt.Draw(s, out why))
                {
                    Assert.Equal(65, b.Width, "an odd number of pixels across");
                    Assert.True(b.GetPixel(32, 32).A > 0, "the picture covers the middle");
                    Assert.Equal(0, (int)b.GetPixel(32, 2).A, "a wide picture leaves the top clear rather than being stretched");
                }
            }
            finally { File.Delete(path); }
        }

        public static void TestTheEditorShowsOnlyTheRowsTheShapeHas()
        {
            using (CrosshairDialog d = new CrosshairDialog(null))
            {
                d.ChooseShape(CrossShape.Cross);
                Assert.True(d.ShowsGap && d.ShowsThick && d.ShowsColour && !d.ShowsPicture, "a cross has a gap, a thickness and a colour");
                d.ChooseShape(CrossShape.Dot);
                Assert.True(!d.ShowsGap && !d.ShowsThick && d.ShowsColour, "a dot has neither gap nor thickness");
                d.ChooseShape(CrossShape.Circle);
                Assert.True(!d.ShowsGap && d.ShowsThick, "a circle has a thickness and no gap");
                d.ChooseShape(CrossShape.Picture);
                Assert.True(d.ShowsPicture && !d.ShowsColour && !d.ShowsGap, "a picture is chosen, not coloured");
                d.ChooseOn(true);
                Assert.True(d.ShowsClients, "the list of clients appears when asked for");
                d.ChooseOn(false);
                Assert.False(d.ShowsClients, "and goes again");
            }
        }

        public static void TestAReadyMadeOneKeepsWhereItWasMovedTo()
        {
            using (CrosshairDialog d = new CrosshairDialog(null))
            {
                d.Working.Cross.NudgeY = 18;
                d.Working.Cross.AimOnly = true;
                d.ChoosePreset(2);
                Assert.Equal(CrossPresets.All[2].Style.Shape, d.Working.Cross.Shape, "the ready-made shape is taken");
                Assert.Equal(18, d.Working.Cross.NudgeY, "the move off the middle is kept");
                Assert.True(d.Working.Cross.AimOnly, "showing only while aiming is kept");
                Assert.Equal(CrossPresets.All.Length + 1, d.PresetChoices, "every ready-made one is offered");
            }
        }

        public static void TestTheSliderReadsAPlaceAlongItAsAValue()
        {
            using (GlassSlider s = new GlassSlider())
            {
                s.Width = 270; s.Minimum = 2; s.Maximum = 60;
                Assert.Equal(2, s.ValueAt(-50), "left of the track is the least");
                Assert.Equal(60, s.ValueAt(5000), "right of it is the most");
                int mid = s.ValueAt(7 + (270 - 70 - 14) / 2);
                Assert.True(mid >= 30 && mid <= 32, "the middle of the track is the middle value");
                int fired = 0;
                s.ValueChanged += delegate { fired++; };
                s.Value = 500;
                Assert.Equal(60, s.Value, "a value past the end is the end");
                s.Value = 60;
                Assert.Equal(1, fired, "said once, not again for the same value");
            }
        }

        public static void TestTheExtrasDoNothingInTheTestRunner()
        {
            using (Control anchor = new Control())
            {
                Extras.Boot(anchor);
                Assert.False(Extras.Running, "no overlay thread");
                Assert.False(Extras.Loaded, "and the real settings file was not read");
            }
        }
    }
}
