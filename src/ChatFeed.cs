using System;
using System.Collections.Generic;
using System.Text;

namespace RobloxKeeper
{
    // Which chat lines are new since the last look.
    //
    // Harder than it sounds. Chat scrolls, so the same lines are read over and
    // over; it fades, so lines appear and vanish on their own; and OCR reads
    // the same pixels slightly differently from one scan to the next, so an
    // exact comparison reports one message five times.
    //
    // Lines are therefore compared loosely: normalised, then scored for
    // similarity. Close enough is the same message. The threshold has two jobs
    // pulling against each other - a misread must not become a second message,
    // and two genuinely different messages must not collapse into one.
    class ChatFeed
    {
        // Long enough to cover a chat box that has scrolled a screenful
        // between scans, short enough that comparing against all of it is free.
        const int DefaultHistory = 40;

        // Chosen so a one-character misreading is still the same message, but
        // a different word is a different message.
        const double DefaultSimilarity = 0.85;

        // Below this a "line" is OCR debris - a stray bracket, a character
        // clipped off the edge of a speech bubble - not something anyone said.
        const int MinLineLength = 3;

        readonly int historyLines;
        readonly double similarity;
        readonly List<string> seen = new List<string>();

        public ChatFeed() : this(DefaultHistory, DefaultSimilarity) { }

        public ChatFeed(int historyLines, double similarity)
        {
            this.historyLines = historyLines < 1 ? 1 : historyLines;
            this.similarity = similarity;
        }

        public void Clear() { seen.Clear(); }

        public IList<string> NewLines(string ocrText)
        {
            List<string> fresh = new List<string>();
            if (string.IsNullOrEmpty(ocrText)) return fresh;

            foreach (string raw in ocrText.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length < MinLineLength) continue;

                string key = Normalise(line);
                if (key.Length == 0) continue;
                if (KnownAlready(key)) continue;

                fresh.Add(line);
                Remember(key);
            }
            return fresh;
        }

        bool KnownAlready(string key)
        {
            foreach (string old in seen)
                if (Similarity(old, key) >= similarity) return true;
            return false;
        }

        void Remember(string key)
        {
            seen.Add(key);
            while (seen.Count > historyLines) seen.RemoveAt(0);
        }

        // Case and spacing are not part of what was said.
        public static string Normalise(string line)
        {
            if (line == null) return "";
            StringBuilder sb = new StringBuilder();
            bool lastWasSpace = true;
            foreach (char c in line.ToLowerInvariant())
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace) sb.Append(' ');
                    lastWasSpace = true;
                }
                else { sb.Append(c); lastWasSpace = false; }
            }
            return sb.ToString().Trim();
        }

        // How alike two lines are, 0 to 1, by counting matching character
        // pairs.
        //
        // Pairs rather than comparing characters position by position: a
        // misread that changes a character's WIDTH shifts everything after it,
        // and a positional comparison then scores two near-identical sentences
        // as completely different - which is the exact case this has to catch.
        public static double Similarity(string a, string b)
        {
            if (a == null || b == null) return 0;
            if (a == b) return 1.0;
            if (a.Length < 2 || b.Length < 2) return 0.0;

            List<string> pairsB = Pairs(b);
            int totalB = pairsB.Count;
            int hits = 0;

            foreach (string p in Pairs(a))
            {
                int at = pairsB.IndexOf(p);
                if (at >= 0) { pairsB.RemoveAt(at); hits++; }
            }

            // Against the longer of the two, so a short line hiding inside a
            // long one does not score as a match.
            int biggest = Math.Max(totalB, a.Length - 1);
            return biggest == 0 ? 0 : hits / (double)biggest;
        }

        static List<string> Pairs(string s)
        {
            List<string> p = new List<string>();
            for (int i = 0; i < s.Length - 1; i++) p.Add(s.Substring(i, 2));
            return p;
        }
    }
}
