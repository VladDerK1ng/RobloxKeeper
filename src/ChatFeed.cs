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
    //
    // And lines are remembered by when they were last SEEN, not when they
    // first turned up. Measured on a live client, a whole-window chat watcher
    // also reads counters and money pop-ups that change every scan - 500 "new"
    // lines in 90 seconds. A memory kept in arrival order pushed out the chat
    // lines still sitting on screen, and sent them to Discord again and again.
    // Now a line on screen is refreshed every scan and never forgotten while it
    // is there, and one that has gone - the chat fades out between messages -
    // is kept for ten minutes, so the chat coming back is not news.
    class ChatFeed
    {
        // A safety limit only. Lines are forgotten by time; this caps memory
        // if a screen produces junk faster than the time limit clears it.
        const int DefaultHistory = 5000;

        // Long enough for the chat to fade out and come back between spawns.
        static readonly TimeSpan DefaultRemember = TimeSpan.FromMinutes(10);

        // Chosen so a one-character misreading is still the same message, but
        // a different word is a different message.
        const double DefaultSimilarity = 0.85;

        // Below this a "line" is OCR debris - a stray bracket, a character
        // clipped off the edge of a speech bubble - not something anyone said.
        const int MinLineLength = 3;

        struct Seen
        {
            public DateTime At;     // when it was last on screen
            public long Look;       // which look that was, to order ties
        }

        readonly int historyLines;
        readonly double similarity;
        readonly TimeSpan remember;
        readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>();
        long look;

        public ChatFeed() : this(DefaultHistory, DefaultSimilarity) { }

        public ChatFeed(int historyLines, double similarity) : this(historyLines, similarity, DefaultRemember) { }

        public ChatFeed(int historyLines, double similarity, TimeSpan remember)
        {
            this.historyLines = historyLines < 1 ? 1 : historyLines;
            this.similarity = similarity;
            this.remember = remember;
        }

        // The word a watcher is looking for, or null for every line. With one,
        // only lines containing it are considered at all, and two readings are
        // compared by what sits next to it - see SameMessage.
        public string Anchor;

        public void Clear() { seen.Clear(); }

        // Are two readings the same message? Judged by the few characters on
        // either side of the watched-for word, with spacing and punctuation set
        // aside.
        //
        // Measured on the owner's client: one chat line came back as "A Secret
        // Pure Jellyfish Egg spawned In Angels--!", "o: Secret Pure Jellyfish
        // Egg", and more, as the chat changed look - words dropped, junk on the
        // ends, "RazorFang" read as "Razor Fang". Whole lines that different
        // cannot be matched, and loosening the match would merge different
        // eggs, which share most of their line. What does hold still is what
        // sits right next to the word - "krakenegg", "purejellyfish" - and that
        // is also exactly what tells one egg from another.
        //
        // Eight characters each side are compared, one misread allowed. Fewer
        // than three on a side is no evidence either way, which is what lets a
        // stray "o:" or a missing "A" through.
        const int CONTEXT = 8;

        public static bool SameMessage(string a, string b, string anchor)
        {
            string ca = Core(a), cb = Core(b), cw = Core(anchor);
            if (cw.Length == 0) return false;
            int ia = ca.IndexOf(cw, StringComparison.Ordinal);
            int ib = cb.IndexOf(cw, StringComparison.Ordinal);
            if (ia < 0 || ib < 0) return false;
            return Agree(ca, ia - 1, cb, ib - 1, -1)
                && Agree(ca, ia + cw.Length, cb, ib + cw.Length, 1);
        }

        static bool Agree(string a, int i, string b, int j, int step)
        {
            int roomA = step > 0 ? a.Length - i : i + 1;
            int roomB = step > 0 ? b.Length - j : j + 1;
            int n = Math.Min(CONTEXT, Math.Min(roomA, roomB));
            if (n < 3) return true;

            int wrong = 0;
            for (int k = 0; k < n; k++)
                if (a[i + k * step] != b[j + k * step]) wrong++;
            return wrong <= 1;
        }

        // Letters and digits only, lower case: what readings of one line agree
        // on once spacing and stray punctuation are set aside.
        static string Core(string s)
        {
            StringBuilder sb = new StringBuilder();
            if (s != null)
                foreach (char c in s.ToLowerInvariant())
                    if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        public IList<string> NewLines(string ocrText)
        {
            return NewLines(ocrText, DateTime.Now);
        }

        public IList<string> NewLines(string ocrText, DateTime now)
        {
            look++;
            ForgetOlderThan(now - remember);

            List<string> fresh = new List<string>();
            if (string.IsNullOrEmpty(ocrText)) return fresh;

            foreach (string raw in ocrText.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length < MinLineLength) continue;
                if (!string.IsNullOrEmpty(Anchor) && Anchor.Trim().Length > 0 &&
                    line.IndexOf(Anchor.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;

                string key = Normalise(line);
                if (key.Length == 0) continue;

                // Seen before: still here, so remembered from now.
                string known = Known(key);
                if (known != null) { Touch(known, now); continue; }

                fresh.Add(line);
                Touch(key, now);
            }

            KeepAtMost(historyLines);
            return fresh;
        }

        string Known(string key)
        {
            if (seen.ContainsKey(key)) return key;
            bool anchored = !string.IsNullOrEmpty(Anchor) && Anchor.Trim().Length > 0;
            foreach (string old in seen.Keys)
            {
                if (anchored && SameMessage(old, key, Anchor)) return old;

                // Lines too different in length cannot reach the threshold,
                // and skipping them keeps a long memory cheap to search.
                int shorter = Math.Min(old.Length, key.Length), longer = Math.Max(old.Length, key.Length);
                if (shorter < similarity * longer - 1) continue;
                if (Similarity(old, key) >= similarity) return old;
            }
            return null;
        }

        void Touch(string key, DateTime now)
        {
            Seen s;
            s.At = now;
            s.Look = look;
            seen[key] = s;
        }

        void ForgetOlderThan(DateTime cutoff)
        {
            List<string> gone = new List<string>();
            foreach (KeyValuePair<string, Seen> p in seen)
                if (p.Value.At < cutoff) gone.Add(p.Key);
            foreach (string k in gone) seen.Remove(k);
        }

        // Over the limit, the lines seen longest ago go first - never one still
        // on screen, which was seen on this look.
        void KeepAtMost(int count)
        {
            if (seen.Count <= count) return;
            List<KeyValuePair<string, Seen>> oldest = new List<KeyValuePair<string, Seen>>(seen);
            oldest.Sort(delegate(KeyValuePair<string, Seen> a, KeyValuePair<string, Seen> b)
            {
                return a.Value.Look.CompareTo(b.Value.Look);
            });
            for (int i = 0; i < oldest.Count - count; i++) seen.Remove(oldest[i].Key);
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
