using System;
using System.Collections.Generic;

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

        // The same answer as Find, far sooner on a big area: shrink both
        // pictures, score every place in the small versions, and look closely
        // only around the few best.
        //
        // A whole-window picture watcher is where this matters - Find scores
        // two million places on a 1936x1048 window. A box barely bigger than
        // its picture has few places in it, and those are all scored as
        // before.
        const int QUICK_BELOW = 4096;      // places; fewer than this are all scored
        const int CANDIDATES = 6;          // best small-picture places looked at closely

        public static ImageHit FindQuick(Pixels haystack, Pixels needle, bool[] mask, double tolerance)
        {
            int scored;
            return FindQuick(haystack, needle, mask, tolerance, out scored);
        }

        public static ImageHit FindQuick(Pixels haystack, Pixels needle, bool[] mask, double tolerance,
                                         out int scored)
        {
            scored = 0;
            if (haystack == null || needle == null || haystack.IsEmpty || needle.IsEmpty ||
                needle.Width > haystack.Width || needle.Height > haystack.Height)
                return Find(haystack, needle, mask, tolerance);

            int places = (haystack.Width - needle.Width + 1) * (haystack.Height - needle.Height + 1);

            // Shrunk by up to four, but never so far that the picture is fewer
            // than eight pixels across - below that the small version is
            // mush and the best places in it mean nothing.
            int f = Math.Min(4, Math.Min(needle.Width, needle.Height) / 8);
            if (places < QUICK_BELOW || f < 2)
            {
                scored = places;
                return Find(haystack, needle, mask, tolerance);
            }

            Pixels smallHay = Shrink(haystack, f);
            Pixels smallNeedle = Shrink(needle, f);
            bool[] smallMask = ShrinkMask(mask, needle.Width, f, smallNeedle.Width, smallNeedle.Height);
            if (smallMask != null && !AnyKept(smallMask))
            {
                scored = places;
                return Find(haystack, needle, mask, tolerance);
            }

            // The best few places in the small pictures, kept apart from one
            // another so they are not all the same place a pixel over.
            List<int[]> best = new List<int[]>();       // x, y
            List<double> bestScore = new List<double>();
            int lastSX = smallHay.Width - smallNeedle.Width, lastSY = smallHay.Height - smallNeedle.Height;
            double floor = 0;
            for (int y = 0; y <= lastSY; y++)
                for (int x = 0; x <= lastSX; x++)
                {
                    // Anything below the worst of a full shortlist cannot join
                    // it, so it may stop being scored early.
                    double s = ScoreAt(smallHay, smallNeedle, smallMask, x, y, bestScore.Count < CANDIDATES * 4 ? 0 : floor);
                    scored++;
                    Keep(best, bestScore, x, y, s, CANDIDATES * 4);
                    if (bestScore.Count >= CANDIDATES * 4) floor = bestScore[bestScore.Count - 1];
                }

            List<int[]> picked = new List<int[]>();
            for (int i = 0; i < best.Count && picked.Count < CANDIDATES; i++)
            {
                bool near = false;
                foreach (int[] p in picked)
                    if (Math.Abs(p[0] - best[i][0]) <= 2 && Math.Abs(p[1] - best[i][1]) <= 2) { near = true; break; }
                if (!near) picked.Add(best[i]);
            }

            // Close up, around each: every full-size place the small one could
            // have come from, and a margin of one small pixel either side.
            ImageHit hit = new ImageHit();
            double top = -1;
            int lastX = haystack.Width - needle.Width, lastY = haystack.Height - needle.Height;
            foreach (int[] p in picked)
            {
                int x0 = Math.Max(0, p[0] * f - f), x1 = Math.Min(lastX, p[0] * f + 2 * f);
                int y0 = Math.Max(0, p[1] * f - f), y1 = Math.Min(lastY, p[1] * f + 2 * f);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        double s = ScoreAt(haystack, needle, mask, x, y, top);
                        scored++;
                        if (s > top) { top = s; hit.X = x; hit.Y = y; }
                    }
            }

            hit.Score = top < 0 ? 0 : top;
            hit.Found = hit.Score >= tolerance;
            return hit;
        }

        // Keeps the list sorted best first and no longer than max.
        static void Keep(List<int[]> at, List<double> scores, int x, int y, double s, int max)
        {
            if (scores.Count >= max && s <= scores[scores.Count - 1]) return;
            int i = scores.Count;
            while (i > 0 && scores[i - 1] < s) i--;
            at.Insert(i, new int[] { x, y });
            scores.Insert(i, s);
            if (scores.Count > max) { at.RemoveAt(max); scores.RemoveAt(max); }
        }

        // Each f x f block becomes its average colour.
        static Pixels Shrink(Pixels p, int f)
        {
            Pixels s = new Pixels(p.Width / f, p.Height / f);
            int n = f * f;
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int dy = 0; dy < f; dy++)
                        for (int dx = 0; dx < f; dx++)
                        {
                            int c = p.At(x * f + dx, y * f + dy);
                            r += (c >> 16) & 0xFF; g += (c >> 8) & 0xFF; b += c & 0xFF;
                        }
                    s.Set(x, y, unchecked((int)0xFF000000) | ((r / n) << 16) | ((g / n) << 8) | (b / n));
                }
            return s;
        }

        // A small pixel counts only if every pixel it was made from counts,
        // so painted-out background never leaks into the small picture.
        static bool[] ShrinkMask(bool[] mask, int width, int f, int sw, int sh)
        {
            if (mask == null) return null;
            bool[] s = new bool[sw * sh];
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    bool all = true;
                    for (int dy = 0; dy < f && all; dy++)
                        for (int dx = 0; dx < f && all; dx++)
                            all = mask[(y * f + dy) * width + x * f + dx];
                    s[y * sw + x] = all;
                }
            return s;
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
