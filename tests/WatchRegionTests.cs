using System;
using System.Drawing;

namespace RobloxKeeper.Tests
{
    // Projecting a drawn box onto a window of a different size.
    //
    // This is the piece most likely to break silently: get it wrong and a
    // watcher reads the wrong pixels forever without ever saying so. Every
    // anchor, both scaling modes and both directions of resize are covered.
    static class WatchRegionTests
    {
        static WatchRegion Drawn(Rectangle box, int w, int h, RegionAnchor a, RegionScaling s)
        {
            return WatchRegion.FromBox(box, w, h, a, s);
        }

        // The box must come back unchanged on the window it was drawn on.
        // If this fails nothing else here means anything.
        public static void TestIdentityOnTheWindowItWasDrawnOn()
        {
            Rectangle box = new Rectangle(484, 0, 968, 230);
            foreach (RegionScaling s in new RegionScaling[] { RegionScaling.Stretch, RegionScaling.Uniform })
            {
                WatchRegion r = Drawn(box, 1936, 1048, RegionAnchor.TopCentre, s);
                Assert.Equal(box, r.Project(1936, 1048), "identity, " + s);
            }
        }

        public static void TestEveryAnchorIsIdentityOnItsOwnWindow()
        {
            Rectangle box = new Rectangle(300, 200, 120, 80);
            foreach (RegionAnchor a in (RegionAnchor[])Enum.GetValues(typeof(RegionAnchor)))
            {
                WatchRegion r = Drawn(box, 1000, 600, a, RegionScaling.Stretch);
                Assert.Equal(box, r.Project(1000, 600), "identity for anchor " + a);
            }
        }

        // Stretch scales each axis on its own: half the width, half the height.
        public static void TestStretchHalvesBothAxes()
        {
            WatchRegion r = Drawn(new Rectangle(0, 0, 400, 200), 1000, 600,
                                  RegionAnchor.TopLeft, RegionScaling.Stretch);
            Assert.Equal(new Rectangle(0, 0, 200, 100), r.Project(500, 300), "halved");
        }

        // Uniform keeps the box's shape, so a template is never squashed.
        // 1936x1048 down to an 800x800 square scales by min(800/1936, 800/1048)
        // = 0.4132, so a 968x230 box becomes 400x95.
        public static void TestUniformKeepsTheBoxShapeWhenAspectChanges()
        {
            WatchRegion r = Drawn(new Rectangle(484, 0, 968, 230), 1936, 1048,
                                  RegionAnchor.TopCentre, RegionScaling.Uniform);
            Rectangle p = r.Project(800, 800);
            Assert.Equal(400, p.Width, "width scaled uniformly");
            Assert.Equal(95, p.Height, "height scaled by the SAME factor, not its own");
            Assert.Equal(200, p.X, "still centred: 400 - 400/2");
        }

        // The same box under stretch would be squashed to a different height -
        // which is exactly why an image watcher must not use stretch.
        public static void TestStretchAndUniformDisagreeWhenAspectChanges()
        {
            Rectangle box = new Rectangle(484, 0, 968, 230);
            Rectangle stretched = Drawn(box, 1936, 1048, RegionAnchor.TopCentre, RegionScaling.Stretch).Project(800, 800);
            Rectangle uniform = Drawn(box, 1936, 1048, RegionAnchor.TopCentre, RegionScaling.Uniform).Project(800, 800);
            Assert.NotEqual(stretched.Height, uniform.Height, "the two modes differ, and that is the point");
        }

        public static void TestABottomRightBoxStaysInTheCorner()
        {
            WatchRegion r = Drawn(new Rectangle(900, 550, 100, 50), 1000, 600,
                                  RegionAnchor.BottomRight, RegionScaling.Stretch);
            Rectangle p = r.Project(2000, 1200);
            Assert.Equal(2000, p.Right, "pinned to the right edge");
            Assert.Equal(1200, p.Bottom, "pinned to the bottom edge");
        }

        public static void TestGrowingTheWindowGrowsTheBox()
        {
            WatchRegion r = Drawn(new Rectangle(0, 0, 100, 100), 500, 500,
                                  RegionAnchor.TopLeft, RegionScaling.Stretch);
            Assert.Equal(new Rectangle(0, 0, 200, 200), r.Project(1000, 1000), "doubled");
        }

        // Widening is the insurance that absorbs drift no formula predicts.
        public static void TestWidenGrowsAroundTheCentre()
        {
            Rectangle w = WatchRegion.Widen(new Rectangle(100, 100, 100, 100), 0.25, 1000, 1000);
            Assert.Equal(new Rectangle(88, 88, 125, 125), w, "25% bigger, same centre");
        }

        public static void TestWidenNeverLeavesTheWindow()
        {
            Rectangle w = WatchRegion.Widen(new Rectangle(0, 0, 100, 100), 1.0, 120, 120);
            Assert.True(w.Left >= 0 && w.Top >= 0, "clamped at the top-left");
            Assert.True(w.Right <= 120 && w.Bottom <= 120, "clamped at the bottom-right");
        }

        // A box must never be projected to nothing, or the crop would throw.
        public static void TestAProjectedBoxIsNeverEmpty()
        {
            WatchRegion r = Drawn(new Rectangle(0, 0, 4, 4), 1936, 1048,
                                  RegionAnchor.TopLeft, RegionScaling.Stretch);
            Rectangle p = r.Project(100, 60);
            Assert.True(p.Width >= 1 && p.Height >= 1, "at least one pixel each way");
        }

        public static void TestAProjectedBoxIsClampedIntoTheWindow()
        {
            WatchRegion r = Drawn(new Rectangle(900, 500, 100, 100), 1000, 600,
                                  RegionAnchor.TopLeft, RegionScaling.Stretch);
            Rectangle p = r.Project(500, 300);
            Assert.True(p.Right <= 500 && p.Bottom <= 300, "inside the smaller window");
        }
    }
}
