using System;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // Drawing a box on a still picture of a client.
    //
    // The arithmetic - which way a drag went, where the box is pinned, how a
    // shrunk picture maps back to the real one - is where a box quietly ends up
    // a few pixels off, so it is tested on its own. The form is then checked
    // for turning what was drawn into exactly the region or picture wanted.
    static class RegionPickerTests
    {
        const int Grey = unchecked((int)0xFF202020);
        const int Red = unchecked((int)0xFFFF0000);

        static Pixels Still(int w, int h)
        {
            Pixels p = new Pixels(w, h);
            p.Fill(Grey);
            return p;
        }

        // ---------- dragging ----------

        public static void TestADragInEitherDirectionGivesTheSameBox()
        {
            Rectangle want = new Rectangle(10, 20, 100, 50);
            Assert.Equal(want, RegionPick.FromDrag(new Point(10, 20), new Point(110, 70), 400, 300), "down and right");
            Assert.Equal(want, RegionPick.FromDrag(new Point(110, 70), new Point(10, 20), 400, 300), "up and left");
        }

        public static void TestADragPastTheEdgeStopsAtTheEdge()
        {
            Assert.Equal(new Rectangle(0, 0, 400, 300),
                RegionPick.FromDrag(new Point(-5, -5), new Point(500, 500), 400, 300), "kept inside the picture");
        }

        // ---------- the anchor ----------

        public static void TestTheAnchorIsGuessedFromWhereTheBoxLanded()
        {
            Assert.Equal(RegionAnchor.TopCentre,
                RegionPick.GuessAnchor(new Rectangle(484, 0, 968, 230), 1936, 1048), "a banner across the top");
            Assert.Equal(RegionAnchor.MiddleLeft,
                RegionPick.GuessAnchor(new Rectangle(0, 300, 600, 400), 1936, 1048), "chat down the left");
            Assert.Equal(RegionAnchor.TopRight,
                RegionPick.GuessAnchor(new Rectangle(1500, 50, 400, 300), 1936, 1048), "a leaderboard");
            Assert.Equal(RegionAnchor.BottomCentre,
                RegionPick.GuessAnchor(new Rectangle(768, 900, 400, 100), 1936, 1048), "a hotbar");
        }

        public static void TestEveryAnchorCanBeGuessed()
        {
            foreach (RegionAnchor a in (RegionAnchor[])Enum.GetValues(typeof(RegionAnchor)))
            {
                int col = (int)a % 3, row = (int)a / 3;
                Rectangle box = new Rectangle(col * 300 + 100, row * 200 + 50, 100, 100);
                Assert.Equal(a, RegionPick.GuessAnchor(box, 900, 600), "the box in cell " + a);
            }
        }

        // ---------- nudging ----------

        public static void TestArrowKeysMoveTheBottomAndRightEdges()
        {
            Rectangle box = new Rectangle(10, 10, 20, 20);
            Assert.Equal(new Rectangle(10, 10, 21, 20), RegionPick.Nudge(box, Keys.Right, false, 100, 100), "right edge out");
            Assert.Equal(new Rectangle(10, 10, 19, 20), RegionPick.Nudge(box, Keys.Left, false, 100, 100), "right edge in");
            Assert.Equal(new Rectangle(10, 10, 20, 21), RegionPick.Nudge(box, Keys.Down, false, 100, 100), "bottom edge down");
            Assert.Equal(new Rectangle(10, 10, 20, 19), RegionPick.Nudge(box, Keys.Up, false, 100, 100), "bottom edge up");
        }

        public static void TestShiftArrowsMoveTheTopAndLeftEdges()
        {
            Rectangle box = new Rectangle(10, 10, 20, 20);
            Assert.Equal(new Rectangle(9, 10, 21, 20), RegionPick.Nudge(box, Keys.Left, true, 100, 100), "left edge out");
            Assert.Equal(new Rectangle(10, 9, 20, 21), RegionPick.Nudge(box, Keys.Up, true, 100, 100), "top edge up");
        }

        public static void TestNudgingNeverLeavesThePictureOrCollapsesTheBox()
        {
            Rectangle corner = new Rectangle(0, 0, 1, 1);
            Assert.Equal(corner, RegionPick.Nudge(corner, Keys.Left, true, 100, 100), "no further left");
            Assert.Equal(corner, RegionPick.Nudge(corner, Keys.Left, false, 100, 100), "never narrower than a pixel");
            Rectangle far = new Rectangle(99, 99, 1, 1);
            Assert.Equal(far, RegionPick.Nudge(far, Keys.Right, false, 100, 100), "no further right");
        }

        // ---------- what is shown ----------

        public static void TestTheSizeIsShownInPixelsAndPercent()
        {
            Assert.Equal("968 x 230 pixels - 50% x 22% of the window",
                RegionPick.Describe(new Rectangle(484, 0, 968, 230), 1936, 1048), "both ways of saying it");
        }

        public static void TestNoBoxYetSaysWhatToDo()
        {
            Assert.Contains("Drag", RegionPick.Describe(Rectangle.Empty, 1936, 1048), "an instruction, not 0 x 0");
        }

        public static void TestWhatWasReadIsShownOrItSaysNothingWasRead()
        {
            Assert.Contains("5B", RegionPick.ReadBack("Money 5B\nRestock"), "the text itself");
            Assert.Contains("didn't read anything", RegionPick.ReadBack(""), "said plainly");
            Assert.Contains("didn't read anything", RegionPick.ReadBack(null), "and for nothing at all");
        }

        public static void TestABoxNeedsAName()
        {
            Assert.True(RegionPick.NameProblem("") != null, "empty");
            Assert.True(RegionPick.NameProblem("   ") != null, "only spaces");
            Assert.Equal(null, RegionPick.NameProblem("banner"), "a name");
        }

        // ---------- fitting on screen ----------

        public static void TestABigCaptureIsShrunkToFitButNeverEnlarged()
        {
            Assert.Equal(0.5, RegionPick.FitScale(1936, 1048, 968, 1048), "halved to fit the width");
            Assert.Equal(1.0, RegionPick.FitScale(400, 300, 1200, 700), "small ones stay actual size");
        }

        public static void TestPointsOnTheShrunkPictureMapBackToTheCapture()
        {
            Assert.Equal(new Point(968, 230), RegionPick.ToImage(new Point(484, 115), 0.5), "doubled back");
            Assert.Equal(new Rectangle(242, 0, 484, 115),
                RegionPick.ToScreen(new Rectangle(484, 0, 968, 230), 0.5), "and a box the other way");
        }

        // ---------- the form ----------

        public static void TestThePickerTurnsADrawnBoxIntoARegionForThisGame()
        {
            using (RegionPickerForm f = new RegionPickerForm(Still(1936, 1048), "Client 1",
                       RegionPickMode.Region, "142823291", RegionScaling.Uniform, null))
            {
                Rectangle box = new Rectangle(484, 0, 968, 230);
                f.SetBox(box);
                WatchRegion r = f.MakeRegion("banner");

                Assert.Equal("banner", r.Name, "named");
                Assert.Equal("142823291", r.PlaceId, "belongs to this game");
                Assert.Equal(RegionAnchor.TopCentre, r.Anchor, "pinned where it was drawn");
                Assert.Equal(RegionScaling.Uniform, r.Scaling, "scaled the way asked");
                Assert.Equal(1936, r.DrawnWidth, "remembers the window it was drawn on");
                Assert.Equal(box, r.Project(1936, 1048), "and comes back exactly on it");
            }
        }

        // A deliberate choice on the grid must survive the next adjustment.
        public static void TestAChosenAnchorIsNotOverwrittenByTheNextDrag()
        {
            using (RegionPickerForm f = new RegionPickerForm(Still(900, 600), "Client 1",
                       RegionPickMode.Region, "1", RegionScaling.Stretch, null))
            {
                f.ChooseAnchor(RegionAnchor.BottomRight);
                f.SetBox(new Rectangle(10, 10, 50, 50));
                Assert.Equal(RegionAnchor.BottomRight, f.BoxAnchor, "still the one chosen");
            }
        }

        public static void TestPickingAPictureGivesExactlyThePixelsInTheBox()
        {
            Pixels still = Still(40, 30);
            still.Set(12, 8, Red);
            using (RegionPickerForm f = new RegionPickerForm(still, "Client 1",
                       RegionPickMode.Picture, "1", RegionScaling.Uniform, null))
            {
                f.SetBox(new Rectangle(10, 5, 4, 4));
                Pixels p = f.MakePicture();
                Assert.Equal(4, p.Width, "as wide as the box");
                Assert.Equal(4, p.Height, "as tall as the box");
                Assert.Equal(Red, p.At(2, 3), "and the same pixels");
            }
        }

        // What the test button reads must be what the watcher will read - the
        // box grown by the same quarter - or it proves nothing.
        public static void TestTheTestButtonReadsTheSameBoxTheWatcherWill()
        {
            Size seen = Size.Empty;
            Func<Pixels, string> read = delegate(Pixels p) { seen = new Size(p.Width, p.Height); return "Money 5B"; };
            using (RegionPickerForm f = new RegionPickerForm(Still(400, 300), "Client 1",
                       RegionPickMode.Region, "1", RegionScaling.Stretch, read))
            {
                f.SetBox(new Rectangle(0, 0, 100, 60));
                string shown = f.TestRead();
                Assert.Equal(new Size(125, 75), seen, "the widened box");
                Assert.Contains("5B", shown, "and what it read is shown");
            }
        }

        // A chat box reads many lines, and every one has to be reachable - a
        // box that scrolls, not a label that stops at its bottom edge.
        public static void TestALongReadBackScrollsRatherThanCutsOff()
        {
            Func<Pixels, string> read = delegate(Pixels p)
            {
                return "Roblox\nHere\nGlobal\nFriends\nSay hi to everyone playing now!\n"
                     + "A Secret Pure Jellyfish Egg spawned in Angels!\n"
                     + "A Eternal Mosasaurus Egg spawned in Prehistoric!";
            };
            using (RegionPickerForm f = new RegionPickerForm(Still(400, 300), "Client 1",
                       RegionPickMode.Region, "1", RegionScaling.Stretch, read))
            {
                f.SetBox(new Rectangle(0, 0, 100, 60));
                f.TestRead();
                Assert.True(f.ReadScrolls, "the read-back can be scrolled");
                Assert.Contains("\r\nA Eternal Mosasaurus Egg spawned in Prehistoric!", f.ReadShown,
                    "and the last line is in it, on a line of its own");
            }
        }
    }
}
