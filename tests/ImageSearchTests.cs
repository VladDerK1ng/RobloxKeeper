using System;

namespace RobloxKeeper.Tests
{
    // The quick search: shrink both pictures, find the few places worth a
    // closer look, and look closely only there.
    //
    // Its whole promise is "the same answer, far sooner", so both halves are
    // tested: it lands where the full search lands, and it scores a small
    // fraction of the places the full search scores.
    static class ImageSearchTests
    {
        // A blocky, noisy scene, so every place looks different from its
        // neighbours and a wrong place genuinely scores lower.
        static Pixels Scene(int w, int h, int seed)
        {
            Random r = new Random(seed);
            int[,] cell = new int[w / 8 + 2, h / 8 + 2];
            for (int i = 0; i < cell.GetLength(0); i++)
                for (int j = 0; j < cell.GetLength(1); j++)
                    cell[i, j] = r.Next(0x1000000);

            Pixels p = new Pixels(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int c = cell[x / 8, y / 8];
                    int n = r.Next(21) - 10;
                    p.Set(x, y, unchecked((int)0xFF000000)
                              | (Clamp(((c >> 16) & 0xFF) + n) << 16)
                              | (Clamp(((c >> 8) & 0xFF) + n) << 8)
                              | Clamp((c & 0xFF) + n));
                }
            return p;
        }

        static int Clamp(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        static void Paste(Pixels into, Pixels what, int atX, int atY)
        {
            for (int y = 0; y < what.Height; y++)
                for (int x = 0; x < what.Width; x++)
                    into.Set(atX + x, atY + y, what.At(x, y));
        }

        public static void TestTheQuickSearchFindsAPictureWhereverItIs()
        {
            Pixels scene = Scene(600, 400, 1);
            Pixels icon = Scene(48, 40, 99);
            Paste(scene, icon, 237, 141);              // not on a multiple of anything

            ImageHit hit = ImageMatch.FindQuick(scene, icon, null, 0.9);
            Assert.True(hit.Found, "found");
            Assert.Equal(237, hit.X, "exactly across");
            Assert.Equal(141, hit.Y, "exactly down");
            Assert.Equal(1.0, hit.Score, "a perfect match");
        }

        public static void TestTheQuickSearchLandsWhereTheFullSearchLands()
        {
            for (int seed = 2; seed < 8; seed++)
            {
                Random r = new Random(seed * 31);
                Pixels scene = Scene(500, 300, seed);
                Pixels icon = Scene(40, 40, seed + 100);
                int x = r.Next(500 - 40), y = r.Next(300 - 40);
                Paste(scene, icon, x, y);

                ImageHit full = ImageMatch.Find(scene, icon, null, 0.9);
                ImageHit quick = ImageMatch.FindQuick(scene, icon, null, 0.9);
                Assert.Equal(full.X, quick.X, "across, seed " + seed);
                Assert.Equal(full.Y, quick.Y, "down, seed " + seed);
                Assert.Equal(full.Score, quick.Score, "score, seed " + seed);
            }
        }

        public static void TestTheQuickSearchScoresAFractionOfThePlaces()
        {
            Pixels scene = Scene(800, 500, 3);
            Pixels icon = Scene(40, 40, 77);
            Paste(scene, icon, 401, 233);

            int scored;
            ImageMatch.FindQuick(scene, icon, null, 0.9, out scored);
            int everywhere = (800 - 40 + 1) * (500 - 40 + 1);
            Assert.True(scored * 10 < everywhere,
                "scored " + scored + " places of " + everywhere + " - under a tenth");
        }

        // The mask still decides what counts: the icon's corners are painted
        // out and the scene behind them is different, and it is still found.
        public static void TestTheQuickSearchStillIgnoresPaintedOutPixels()
        {
            Pixels scene = Scene(600, 400, 4);
            Pixels icon = Scene(40, 40, 55);
            bool[] mask = new bool[40 * 40];
            for (int y = 0; y < 40; y++)
                for (int x = 0; x < 40; x++)
                {
                    int dx = x - 20, dy = y - 20;
                    mask[y * 40 + x] = dx * dx + dy * dy <= 18 * 18;
                }

            // Only the kept circle goes into the scene; the corners around it
            // stay as the scene's own pixels.
            for (int y = 0; y < 40; y++)
                for (int x = 0; x < 40; x++)
                    if (mask[y * 40 + x]) scene.Set(310 + x, 95 + y, icon.At(x, y));

            ImageHit hit = ImageMatch.FindQuick(scene, icon, mask, 0.9);
            Assert.True(hit.Found, "found despite the corners");
            Assert.Equal(310, hit.X, "across");
            Assert.Equal(95, hit.Y, "down");
        }

        public static void TestTheQuickSearchSaysSoWhenItIsNotThere()
        {
            Pixels scene = Scene(600, 400, 5);
            Pixels icon = Scene(40, 40, 66);
            Assert.False(ImageMatch.FindQuick(scene, icon, null, 0.95).Found, "not in the scene");
        }

        // A box barely bigger than the picture has few places in it; those
        // are all scored, exactly as before.
        public static void TestASmallBoxIsSearchedInFull()
        {
            Pixels scene = Scene(70, 60, 6);
            Pixels icon = Scene(40, 40, 88);
            Paste(scene, icon, 13, 9);

            int scored;
            ImageHit hit = ImageMatch.FindQuick(scene, icon, null, 0.9, out scored);
            Assert.Equal((70 - 40 + 1) * (60 - 40 + 1), scored, "every place");
            Assert.Equal(13, hit.X, "and it is found");
        }
    }
}
