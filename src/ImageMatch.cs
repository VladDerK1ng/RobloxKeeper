using System;

namespace RobloxKeeper
{
    struct ImageHit
    {
        public bool Found;
        public double Score;
        public int X, Y;
    }

    // Finding a cropped picture again inside a later capture.
    //
    // The hard part is not finding it - it is not claiming to have found it.
    // The background behind an icon changes as the character moves, so a plain
    // pixel comparison either misses the icon or, with the tolerance opened up
    // far enough to cope, starts matching scenery instead.
    //
    // The mask is the real answer: the user paints out the background after
    // cropping and only the pixels they kept are ever compared. Tolerance is
    // the coarse control on top of that.
    static class ImageMatch
    {
        public static ImageHit Find(Pixels haystack, Pixels needle, bool[] mask, double tolerance)
        {
            ImageHit hit = new ImageHit();
            if (haystack == null || needle == null) return hit;
            if (haystack.IsEmpty || needle.IsEmpty) return hit;
            if (needle.Width > haystack.Width || needle.Height > haystack.Height) return hit;
            if (mask != null && !AnyKept(mask)) return hit;

            double best = -1;
            int bestX = 0, bestY = 0;

            int lastX = haystack.Width - needle.Width;
            int lastY = haystack.Height - needle.Height;

            for (int y = 0; y <= lastY; y++)
                for (int x = 0; x <= lastX; x++)
                {
                    // Nothing scoring below the best so far can win, so a
                    // position stops being scored the moment it cannot beat
                    // it. Across a region of flat background that ends most
                    // positions after a row or two.
                    double score = ScoreAt(haystack, needle, mask, x, y, best);
                    if (score > best) { best = score; bestX = x; bestY = y; }
                }

            hit.Score = best < 0 ? 0 : best;
            hit.X = bestX;
            hit.Y = bestY;
            hit.Found = hit.Score >= tolerance;
            return hit;
        }

        // How well the needle sits at one position, 0 to 1.
        //
        // abandonBelow is an optimisation only. A position that cannot reach
        // it returns early with its best-possible score, which is a correct
        // upper bound and is never the winner - so the answer the caller ends
        // up using is the same either way. Pass 0 to score without abandoning.
        public static double ScoreAt(Pixels haystack, Pixels needle, bool[] mask,
                                     int atX, int atY, double abandonBelow)
        {
            int total = CountKept(needle, mask);
            if (total == 0) return 0;

            // The worst a single pixel can score across three channels.
            long worstTotal = (long)total * 255 * 3;
            long diff = 0;

            for (int y = 0; y < needle.Height; y++)
            {
                for (int x = 0; x < needle.Width; x++)
                {
                    if (mask != null && !mask[y * needle.Width + x]) continue;

                    int a = needle.At(x, y);
                    int b = haystack.At(atX + x, atY + y);
                    diff += Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF))
                          + Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF))
                          + Math.Abs((a & 0xFF) - (b & 0xFF));
                }

                // Once a row, not once a pixel: per-pixel the test costs more
                // than it saves.
                if (abandonBelow > 0)
                {
                    // The best this position could still reach if every
                    // remaining pixel were perfect. Divided by the FULL worst
                    // case, not the part scored so far - otherwise this stops
                    // being an upper bound and starts rejecting winners.
                    double ceiling = 1.0 - diff / (double)worstTotal;
                    if (ceiling <= abandonBelow) return ceiling < 0 ? 0 : ceiling;
                }
            }

            return 1.0 - diff / (double)worstTotal;
        }

        static int CountKept(Pixels needle, bool[] mask)
        {
            if (mask == null) return needle.Width * needle.Height;
            int n = 0;
            foreach (bool keep in mask) if (keep) n++;
            return n;
        }

        static bool AnyKept(bool[] mask)
        {
            foreach (bool keep in mask) if (keep) return true;
            return false;
        }
    }
}
