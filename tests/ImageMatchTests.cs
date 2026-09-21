using System.Drawing;

namespace RobloxKeeper.Tests
{
    // Finding a cropped icon again in a later capture.
    //
    // The failure mode that matters is not missing it - it is finding it when
    // it is not there, because the background behind it changed. That is what
    // the mask is for, and most of these tests are about the mask.
    static class ImageMatchTests
    {
        const int Red = unchecked((int)0xFFFF0000);
        const int Green = unchecked((int)0xFF00FF00);
        const int Black = unchecked((int)0xFF000000);

        // A 20x20 field of black with a 4x4 red square at (8, 6).
        static Pixels Field()
        {
            Pixels p = new Pixels(20, 20);
            p.Fill(Black);
            for (int y = 6; y < 10; y++)
                for (int x = 8; x < 12; x++)
                    p.Set(x, y, Red);
            return p;
        }

        static Pixels Square(int argb, int size)
        {
            Pixels p = new Pixels(size, size);
            p.Fill(argb);
            return p;
        }

        public static void TestAnExactMatchScoresOne()
        {
            ImageHit hit = ImageMatch.Find(Field(), Square(Red, 4), null, 0.9);
            Assert.True(hit.Found, "the square is there");
            Assert.Equal(1.0, hit.Score, "identical pixels");
        }

        public static void TestItReportsWhereItFoundIt()
        {
            ImageHit hit = ImageMatch.Find(Field(), Square(Red, 4), null, 0.9);
            Assert.Equal(8, hit.X, "x");
            Assert.Equal(6, hit.Y, "y");
        }

        public static void TestSomethingThatIsNotThereIsNotFound()
        {
            Assert.False(ImageMatch.Find(Field(), Square(Green, 4), null, 0.9).Found,
                "no green anywhere");
        }

        // The whole point of the mask: the icon is the same, the scenery
        // behind it is not, and the match must survive that.
        public static void TestAMaskIgnoresTheBackgroundAroundAnIcon()
        {
            // A 4x4 tile whose middle 2x2 is the icon and whose edge is
            // whatever happened to be behind it when it was cropped.
            Pixels needle = Square(Green, 4);
            needle.Set(1, 1, Red); needle.Set(2, 1, Red);
            needle.Set(1, 2, Red); needle.Set(2, 2, Red);

            bool[] mask = new bool[16];
            mask[1 * 4 + 1] = true; mask[1 * 4 + 2] = true;
            mask[2 * 4 + 1] = true; mask[2 * 4 + 2] = true;

            Assert.False(ImageMatch.Find(Field(), needle, null, 0.9).Found,
                "without the mask the green edge sinks it");
            Assert.True(ImageMatch.Find(Field(), needle, mask, 0.9).Found,
                "with the mask only the icon is compared");
        }

        public static void TestAMaskThatHidesEverythingFindsNothing()
        {
            Assert.False(ImageMatch.Find(Field(), Square(Red, 4), new bool[16], 0.9).Found,
                "nothing to compare is not a match");
        }

        // Tolerance has to mean something a user can reason about, so the
        // boundary is checked from both sides with a known colour distance.
        public static void TestJustAboveTheToleranceMatchesAndJustBelowDoesNot()
        {
            Pixels field = new Pixels(8, 8);
            field.Fill(Black);
            int dimRed = unchecked((int)0xFFC00000);   // 25% darker
            for (int y = 2; y < 4; y++)
                for (int x = 2; x < 4; x++)
                    field.Set(x, y, dimRed);

            Assert.True(ImageMatch.Find(field, Square(Red, 2), null, 0.80).Found,
                "a loose tolerance accepts a dimmer red");
            Assert.False(ImageMatch.Find(field, Square(Red, 2), null, 0.99).Found,
                "a strict one does not");
        }

        public static void TestTheBestPositionWinsWhenThereAreTwo()
        {
            Pixels field = new Pixels(20, 20);
            field.Fill(Black);
            field.Set(2, 2, Red); field.Set(3, 2, Red);
            field.Set(2, 3, Red); field.Set(3, 3, Red);
            int dimRed = unchecked((int)0xFFE00000);
            field.Set(12, 12, dimRed); field.Set(13, 12, dimRed);
            field.Set(12, 13, dimRed); field.Set(13, 13, dimRed);

            ImageHit hit = ImageMatch.Find(field, Square(Red, 2), null, 0.5);
            Assert.Equal(2, hit.X, "the exact one wins, not merely the first");
            Assert.Equal(2, hit.Y, "the exact one wins, not merely the first");
        }

        // A template bigger than what it is searched in is a setup mistake,
        // not a crash.
        public static void TestANeedleBiggerThanTheHaystackIsNotFound()
        {
            Assert.False(ImageMatch.Find(Square(Red, 4), Square(Red, 8), null, 0.9).Found,
                "cannot fit");
        }

        public static void TestEmptyInputsAreNotAMatch()
        {
            Assert.False(ImageMatch.Find(null, Square(Red, 2), null, 0.9).Found, "no haystack");
            Assert.False(ImageMatch.Find(Field(), null, null, 0.9).Found, "no needle");
            Assert.False(ImageMatch.Find(Field(), new Pixels(0, 0), null, 0.9).Found, "empty needle");
        }

        // The early abandon is an optimisation. If it ever changes an answer
        // it is a bug, and this is how that shows up.
        public static void TestScoreAtIsTheSameWithAndWithoutAbandoning()
        {
            Pixels f = Field();
            Pixels n = Square(Red, 4);
            Assert.Equal(ImageMatch.ScoreAt(f, n, null, 8, 6, 0.0),
                         ImageMatch.ScoreAt(f, n, null, 8, 6, 0.9),
                         "a winning position scores the same either way");
        }

        public static void TestCropTakesTheRightPartOfTheImage()
        {
            Pixels c = Field().Crop(new Rectangle(8, 6, 4, 4));
            Assert.Equal(4, c.Width, "width");
            Assert.Equal(Red, c.At(0, 0), "top-left of the crop is the square");
        }

        // A projected region can land partly off a window that shrank between
        // the projection and the crop. That is a clamp, not a crash.
        public static void TestCroppingPastTheEdgeIsClampedNotThrown()
        {
            Pixels c = Field().Crop(new Rectangle(18, 18, 10, 10));
            Assert.Equal(2, c.Width, "clamped to what is actually there");
            Assert.Equal(2, c.Height, "clamped to what is actually there");
        }

        // GDI pads rows out to a four-byte boundary, so a width that is not a
        // multiple of four is where a stride mistake shows up as a skewed
        // picture. Round-tripping through a real Bitmap is the only way to
        // catch it.
        public static void TestAnOddlySizedImageSurvivesTheBitmapRoundTrip()
        {
            Pixels p = new Pixels(13, 7);
            p.Fill(Black);
            p.Set(11, 5, Red);

            using (Bitmap bmp = p.ToBitmap())
            {
                Pixels back = Pixels.FromBitmap(bmp);
                Assert.Equal(13, back.Width, "width");
                Assert.Equal(7, back.Height, "height");
                Assert.Equal(Red, back.At(11, 5), "the pixel is still where it was");
                Assert.Equal(Black, back.At(0, 0), "and nothing else moved into its place");
            }
        }
    }
}
