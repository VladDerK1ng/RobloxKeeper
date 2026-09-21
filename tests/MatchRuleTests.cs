namespace RobloxKeeper.Tests
{
    // What counts as a hit in a screenful of OCR.
    //
    // OCR gets small dim text wrong - a real capture read "5B" where the
    // screen said "58". So matching is deliberately forgiving by default and
    // the user tightens it when a false hit shows up, rather than the other
    // way round. A rule too strict to ever fire looks exactly like nothing
    // having happened, which is the worse failure of the two.
    static class MatchRuleTests
    {
        static MatchRule Rule(string[] words, MatchMode mode)
        {
            MatchRule r = new MatchRule();
            r.Words = words;
            r.Mode = mode;
            return r;
        }

        public static void TestAnyMatchesWhenOneWordIsPresent()
        {
            MatchRule r = Rule(new string[] { "secret", "rainbow" }, MatchMode.Any);
            Assert.True(r.Matches("a Rainbow Egg appeared"), "one of the two is enough");
        }

        public static void TestAnyDoesNotMatchWhenNoneArePresent()
        {
            MatchRule r = Rule(new string[] { "secret", "rainbow" }, MatchMode.Any);
            Assert.False(r.Matches("a Common Egg appeared"), "neither word is there");
        }

        public static void TestAllNeedsEveryWord()
        {
            MatchRule r = Rule(new string[] { "secret", "egg" }, MatchMode.All);
            Assert.False(r.Matches("secret pet"), "only one of the two");
            Assert.True(r.Matches("secret egg hatched"), "both present");
        }

        public static void TestCaseIsIgnoredByDefault()
        {
            MatchRule r = Rule(new string[] { "SECRET" }, MatchMode.Any);
            Assert.True(r.IgnoreCase, "forgiving by default");
            Assert.True(r.Matches("a secret egg"), "case does not matter");
        }

        public static void TestCaseCanBeMadeToMatter()
        {
            MatchRule r = Rule(new string[] { "SECRET" }, MatchMode.Any);
            r.IgnoreCase = false;
            Assert.False(r.Matches("a secret egg"), "case now matters");
        }

        // Without this, "rat" matches "grateful" - the usual reason a webhook
        // fires all day for no reason.
        public static void TestWholeWordsOnlyRejectsAWordInsideAnother()
        {
            MatchRule r = Rule(new string[] { "rat" }, MatchMode.Any);
            r.WholeWordsOnly = true;
            Assert.False(r.Matches("grateful"), "not a word on its own");
            Assert.True(r.Matches("a rat appeared"), "a word on its own");
        }

        public static void TestWholeWordsHandlesPunctuationAndEdges()
        {
            MatchRule r = Rule(new string[] { "egg" }, MatchMode.Any);
            r.WholeWordsOnly = true;
            Assert.True(r.Matches("egg"), "the whole string");
            Assert.True(r.Matches("[SERVER] Restock: egg, x3"), "between punctuation");
            Assert.False(r.Matches("eggplant"), "start of a longer word");
        }

        public static void TestAnEmptyRuleMatchesNothing()
        {
            Assert.False(Rule(new string[0], MatchMode.Any).Matches("anything"), "no words, no hits");
            Assert.False(Rule(null, MatchMode.Any).Matches("anything"), "no list at all");
            Assert.False(Rule(new string[] { "egg" }, MatchMode.Any).Matches(null), "nothing read");
        }

        // Blank lines in the words box are the user pressing Enter, not a rule
        // that matches everything.
        public static void TestBlankWordsAreIgnored()
        {
            MatchRule r = Rule(new string[] { "", "  ", "egg" }, MatchMode.All);
            Assert.True(r.Matches("one egg"), "the blanks are not requirements");
        }

        // An All rule where none of the words are present must not match, even
        // though the blank-word handling makes that easy to get wrong.
        public static void TestAllWithNothingPresentDoesNotMatch()
        {
            MatchRule r = Rule(new string[] { "secret", "egg" }, MatchMode.All);
            Assert.False(r.Matches("nothing of interest here"), "neither word");
        }

        // The webhook says what was found, so the rule has to report which
        // word it was.
        public static void TestFirstHitNamesTheWordThatMatched()
        {
            MatchRule r = Rule(new string[] { "secret", "rainbow" }, MatchMode.Any);
            Assert.Equal("rainbow", r.FirstHit("a Rainbow Egg"), "the word that hit");
            Assert.Equal(null, r.FirstHit("a Common Egg"), "nothing hit");
        }
    }
}
