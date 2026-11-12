using System;
using System.Collections.Generic;
using System.Drawing;

namespace RobloxKeeper.Tests
{
    // Scrolling a page of cards has to cost a frame a few milliseconds, not
    // thirty. The pictures are scaled once and copied after that; what
    // decides when a copy is kept, made again or let go is here.
    static class ScrollTests
    {
        static int made;

        static Bitmap Source(int w, int h)
        {
            Bitmap b = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(200, 30, 30));
            return b;
        }

        static PictureCache Cache(int limit)
        {
            made = 0;
            return new PictureCache(limit, delegate(Bitmap src, Size size) { made++; return new Bitmap(size.Width, size.Height); });
        }

        static bool IsDisposed(Bitmap b)
        {
            try { int w = b.Width; return false; }
            catch (ArgumentException) { return true; }
        }

        public static void TestAPictureIsScaledOnceAndThenKept()
        {
            PictureCache c = Cache(10);
            Bitmap src = Source(150, 150);
            c.BeginPaint();
            Bitmap first = c.Get(src, new Size(158, 158));
            c.BeginPaint();
            Bitmap second = c.Get(src, new Size(158, 158));
            Assert.True(ReferenceEquals(first, second), "the same copy comes back");
            Assert.Equal(1, made, "made once, not on every frame");
        }

        public static void TestADifferentSizeMakesItAgainAndLetsTheOldOneGo()
        {
            PictureCache c = Cache(10);
            Bitmap src = Source(150, 150);
            c.BeginPaint();
            Bitmap small = c.Get(src, new Size(36, 36));
            c.BeginPaint();
            Bitmap big = c.Get(src, new Size(158, 158));
            Assert.Equal(2, made, "a new size is a new copy");
            Assert.Equal(158, big.Width, "at the size asked for");
            Assert.True(IsDisposed(small), "and the old one is not left behind");
            Assert.Equal(1, c.Count, "still one picture held for it");
        }

        public static void TestADifferentPictureIsAnotherEntry()
        {
            PictureCache c = Cache(10);
            c.BeginPaint();
            Bitmap a = c.Get(Source(150, 150), new Size(158, 158));
            Bitmap b = c.Get(Source(150, 150), new Size(158, 158));
            Assert.False(ReferenceEquals(a, b), "two pictures, two copies");
            Assert.Equal(2, c.Count, "both kept");
        }

        public static void TestWhatIsNotDrawnAnyMoreIsLetGoOnceThereAreTooMany()
        {
            PictureCache c = Cache(3);
            List<Bitmap> sources = new List<Bitmap>();
            for (int i = 0; i < 5; i++) sources.Add(Source(10, 10));
            Size size = new Size(20, 20);

            c.BeginPaint();
            Bitmap[] copies = new Bitmap[5];
            for (int i = 0; i < 3; i++) copies[i] = c.Get(sources[i], size);
            c.Trim();
            Assert.Equal(3, c.Count, "at the limit, nothing goes");

            // Scrolled on: two others come into view.
            c.BeginPaint();
            copies[3] = c.Get(sources[3], size);
            copies[4] = c.Get(sources[4], size);
            c.Get(sources[2], size);
            c.Trim();
            Assert.Equal(3, c.Count, "back down to the limit");
            Assert.True(IsDisposed(copies[0]), "the one idle longest went first");
            Assert.True(IsDisposed(copies[1]), "then the next");
            Assert.False(IsDisposed(copies[2]), "the one still in view stayed");
            Assert.False(IsDisposed(copies[3]), "so did the new ones");
            Assert.False(IsDisposed(copies[4]), "both");
        }

        // A tall window can show more than the limit at once. Letting one of
        // those go would only have it made again by the very next frame.
        public static void TestNothingDrawnThisFrameIsEverLetGo()
        {
            PictureCache c = Cache(2);
            Size size = new Size(20, 20);
            c.BeginPaint();
            Bitmap[] copies = new Bitmap[5];
            for (int i = 0; i < 5; i++) copies[i] = c.Get(Source(10, 10), size);
            c.Trim();
            Assert.Equal(5, c.Count, "all five were in view");
            foreach (Bitmap b in copies) Assert.False(IsDisposed(b), "none was thrown away mid-use");
        }

        public static void TestClearingLetsEverythingGo()
        {
            PictureCache c = Cache(10);
            c.BeginPaint();
            Bitmap copy = c.Get(Source(10, 10), new Size(20, 20));
            c.Clear();
            Assert.Equal(0, c.Count, "nothing held");
            Assert.True(IsDisposed(copy), "and its memory returned");
            c.BeginPaint();
            c.Get(Source(10, 10), new Size(20, 20));
            Assert.Equal(1, c.Count, "usable again afterwards");
        }

        // ---------- what comes out on the page ----------

        static CardCanvas Canvas(bool rows, Bitmap picture)
        {
            CardCanvas c = new CardCanvas();
            c.Size = new Size(402, 300);
            CardSection s = new CardSection();
            s.Title = "Games";
            s.Rows = rows;
            CardItem item = new CardItem();
            item.Title = "One";
            item.Image = picture;
            s.Items.Add(item);
            c.Show(new CardSection[] { s });
            return c;
        }

        static Bitmap Paint(CardCanvas c)
        {
            Bitmap b = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
            return b;
        }

        public static void TestACardShowsItsPictureWithRoundedCornersAndPaintsTheSameAgain()
        {
            using (Bitmap src = Source(150, 150))
            using (CardCanvas c = Canvas(false, src))
            using (Bitmap one = Paint(c))
            using (Bitmap two = Paint(c))
            {
                int x = CardLayout.PAD, y = CardLayout.PAD + CardLayout.TITLE_H;
                Color middle = one.GetPixel(x + 79, y + 79);
                Assert.True(middle.R > 150 && middle.G < 80, "the picture is in the middle of the card");
                Color corner = one.GetPixel(x, y);
                Assert.True(corner.R < 100, "but not out in the rounded corner");
                for (int py = 0; py < one.Height; py += 7)
                    for (int px = 0; px < one.Width; px += 5)
                        Assert.Equal(one.GetPixel(px, py).ToArgb(), two.GetPixel(px, py).ToArgb(), "a kept picture paints as it did first");
            }
        }

        public static void TestAFriendsFaceIsCutRound()
        {
            using (Bitmap src = Source(48, 48))
            using (CardCanvas c = Canvas(true, src))
            using (Bitmap one = Paint(c))
            {
                int x = CardLayout.PAD, y = CardLayout.PAD + CardLayout.TITLE_H;
                Color middle = one.GetPixel(x + 10 + 18, y + (CardLayout.ROW_H - 36) / 2 + 18);
                Assert.True(middle.R > 150 && middle.G < 80, "the face is there");
                Color corner = one.GetPixel(x + 10 + 1, y + (CardLayout.ROW_H - 36) / 2 + 1);
                Assert.True(corner.R < 100, "round, so its corner is not");
            }
        }
    }
}
