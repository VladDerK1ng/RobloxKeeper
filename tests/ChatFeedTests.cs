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
    }
}
