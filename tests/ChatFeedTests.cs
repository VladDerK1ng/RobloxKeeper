using System;

namespace RobloxKeeper.Tests
{
    // Chat scrolls, fades, and OCR reads the same line slightly differently
    // from one scan to the next. A plain "is this line in the previous scan"
    // check reports the same message five times, so lines are compared
    // loosely rather than exactly.
    //
    // The threshold has two jobs pulling against each other: a misread must
    // not become a second message, and two genuinely different messages must
    // not collapse into one. Both directions are tested.
    static class ChatFeedTests
    {
        public static void TestEveryLineIsNewOnTheFirstScan()
        {
            ChatFeed f = new ChatFeed();
            Assert.Equal(2, f.NewLines("hello there\nsecond line").Count,
                "both lines are news the first time");
        }

        public static void TestTheSameScanTwiceProducesNothingNew()
        {
            ChatFeed f = new ChatFeed();
            f.NewLines("hello there\nsecond line");
            Assert.Equal(0, f.NewLines("hello there\nsecond line").Count, "nothing has changed");
        }

        public static void TestAGenuinelyNewLineIsReported()
        {
            ChatFeed f = new ChatFeed();
            f.NewLines("hello there");
            System.Collections.Generic.IList<string> n =
                f.NewLines("hello there\nRestock: Rainbow Egg x3");
            Assert.Equal(1, n.Count, "one new message");
            Assert.Equal("Restock: Rainbow Egg x3", n[0], "and it is the whole line");
        }

        // The reason this class exists.
        public static void TestOcrJitterIsNotANewMessage()
        {
            ChatFeed f = new ChatFeed();
            f.NewLines("Restock: Rainbow Egg x3");
            Assert.Equal(0, f.NewLines("Restock: Ra1nbow Egg x3").Count, "one character misread");
        }

        public static void TestCaseAndSpacingDoNotMakeALineNew()
        {
            ChatFeed f = new ChatFeed();
            f.NewLines("Restock: Rainbow Egg x3");
            Assert.Equal(0, f.NewLines("restock:   rainbow  egg x3").Count, "same words");
        }

        // Chat scrolls. A line that leaves the top of the box and comes back
        // is not a new message.
        public static void TestALineScrollingBackIntoViewIsNotNew()
        {
            ChatFeed f = new ChatFeed();
            f.NewLines("one two three\nfour five six\nseven eight nine");
            f.NewLines("four five six\nseven eight nine\nten eleven twelve");
            Assert.Equal(0, f.NewLines("one two three\nfour five six").Count, "all seen before");
        }

        // The other side of the jitter test, and it matters just as much: the
        // fuzzy comparison must not swallow a genuinely different message.
        public static void TestADifferentMessageIsStillNew()
        {
            ChatFeed f = new ChatFeed();
            f.NewLines("Restock: Rainbow Egg x3");
            Assert.Equal(1, f.NewLines("Restock: Common Egg x9").Count, "a different restock");
        }

        public static void TestBlankLinesAreIgnored()
        {
            ChatFeed f = new ChatFeed();
            Assert.Equal(1, f.NewLines("\n\n  \nhello\n\n").Count, "only the real line");
        }

        // Single characters and other OCR debris are not messages.
        public static void TestVeryShortLinesAreIgnored()
        {
            ChatFeed f = new ChatFeed();
            Assert.Equal(0, f.NewLines("x\n.\n[]").Count, "debris, not chat");
        }

        // Memory must not grow for the whole session.
        public static void TestHistoryIsBounded()
        {
            ChatFeed f = new ChatFeed(3, 0.85);
            f.NewLines("alpha one");
            f.NewLines("bravo two");
            f.NewLines("charlie three");
            f.NewLines("delta four");
            Assert.Equal(1, f.NewLines("alpha one").Count, "pushed out of a three-line history");
        }

        public static void TestClearForgetsEverything()
        {
            ChatFeed f = new ChatFeed();
            f.NewLines("hello there");
            f.Clear();
            Assert.Equal(1, f.NewLines("hello there").Count, "a different game, a fresh start");
        }

        public static void TestSimilarityIsOneForIdenticalAndLowForUnrelated()
        {
            Assert.Equal(1.0, ChatFeed.Similarity("abcdef", "abcdef"), "identical");
            Assert.True(ChatFeed.Similarity("abcdef", "zyxwvu") < 0.2, "nothing in common");
        }

        public static void TestNormaliseFlattensCaseAndSpacing()
        {
            Assert.Equal("restock: rainbow egg", ChatFeed.Normalise("  Restock:   RAINBOW  Egg  "),
                "one form for what is the same line");
        }

        // A capture that read nothing is not an error and is not a message.
        public static void TestNothingReadIsNotAMessage()
        {
            ChatFeed f = new ChatFeed();
            Assert.Equal(0, f.NewLines(null).Count, "nothing at all");
            Assert.Equal(0, f.NewLines("").Count, "an empty read");
        }

        // ---------- what happened live ----------

        // A line of junk that looks like nothing else - the money pop-ups and
        // counters a whole-window read picks up change every scan like this.
        static string Junk(int i)
        {
            char[] c = new char[9];
            uint x = (uint)(i * 2654435761u + 12345u);
            for (int k = 0; k < c.Length; k++) { x = x * 1103515245u + 12345u; c[k] = (char)('a' + (x >> 16) % 26); }
            return "+" + i + "K " + new string(c);
        }

        const string OldSecret = "A Secret Kraken Egg spawned in Angels!";

        // Measured on the owner's client: a whole-window chat watcher also reads
        // the counters and money pop-ups, 500 "new" lines in 90 seconds, and the
        // Secret lines still on screen were pushed out of a memory kept in the
        // order lines were first seen - then sent to Discord again, ten times.
        public static void TestALineStillOnScreenIsNeverForgottenHoweverMuchElseTurnsUp()
        {
            ChatFeed f = new ChatFeed(5, 0.85);
            f.NewLines(OldSecret);
            int again = 0;
            for (int i = 0; i < 20; i++)
                foreach (string line in f.NewLines(OldSecret + "\n" + Junk(i)))
                    if (line == OldSecret) again++;
            Assert.Equal(0, again, "on screen the whole time, so never news again");
        }

        // The chat fades out when nobody is talking and comes back with the
        // same lines in it, while the rest of the screen keeps changing.
        public static void TestAChatThatFadesOutAndComesBackIsNotNews()
        {
            ChatFeed f = new ChatFeed();
            DateTime t = new DateTime(2026, 9, 22, 13, 0, 0);
            f.NewLines(OldSecret, t);
            for (int i = 0; i < 360; i++)                       // three minutes of it, two a second
                f.NewLines(Junk(i), t.AddMilliseconds(500 * (i + 1)));
            Assert.Equal(0, f.NewLines(OldSecret, t.AddMinutes(3)).Count, "the same line, back again");
        }

        // Remembered for a while, not for ever: a line gone for longer than
        // that is news if it turns up again.
        public static void TestALineGoneLongerThanItIsRememberedIsNewAgain()
        {
            ChatFeed f = new ChatFeed(1000, 0.85, TimeSpan.FromMinutes(10));
            DateTime t = new DateTime(2026, 9, 22, 13, 0, 0);
            f.NewLines("hello there everyone", t);
            Assert.Equal(0, f.NewLines("hello there everyone", t.AddMinutes(9)).Count, "still remembered");
            Assert.Equal(1, f.NewLines("hello there everyone", t.AddMinutes(20)).Count, "eleven minutes away, and new");
        }
    }
}
