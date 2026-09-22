using System;
using System.IO;

namespace RobloxKeeper.Tests
{
    // A rule: when something happens, play a macro, on some clients, within
    // some limits. What it is, what it says about itself, and that it lives
    // in its setup like the watchers do.
    static class RuleModelTests
    {
        static Rule Sample()
        {
            Rule r = new Rule();
            r.Name = "Buy krakens";
            r.When = RuleWhen.WatcherFinds;
            r.Watcher = "Secret chat";
            r.FoundContains = "Kraken";
            r.Macro = "Buy egg";
            r.Times = 2;
            r.DelaySeconds = 3;
            r.On = RuleOn.WhereItHappened;
            r.GapSeconds = 40;
            r.OnlyWhenAway = true;
            return r;
        }

        public static void TestARuleSurvivesTheRoundTrip()
        {
            Rule back = WatchStore.DeserializeRule(WatchStore.SerializeRule(Sample()));
            Assert.Equal("Buy krakens", back.Name, "name");
            Assert.Equal(RuleWhen.WatcherFinds, back.When, "when");
            Assert.Equal("Secret chat", back.Watcher, "which watcher");
            Assert.Equal("Kraken", back.FoundContains, "what it must contain");
            Assert.Equal("Buy egg", back.Macro, "the macro");
            Assert.Equal(2, back.Times, "how many times");
            Assert.Equal(3, back.DelaySeconds, "the wait first");
            Assert.Equal(40, back.GapSeconds, "the gap");
            Assert.True(back.OnlyWhenAway, "only while away");

            Rule key = new Rule();
            key.Name = "k"; key.When = RuleWhen.Hotkey; key.HotkeyVk = 0x72; key.Ctrl = true; key.Shift = true;
            key.On = RuleOn.TheseAccounts; key.Accounts = new string[] { "VladDerKing", "alt1" };
            Rule k2 = WatchStore.DeserializeRule(WatchStore.SerializeRule(key));
            Assert.Equal((byte)0x72, k2.HotkeyVk, "the key");
            Assert.True(k2.Ctrl && k2.Shift && !k2.Alt, "the modifiers");
            Assert.Equal(2, k2.Accounts.Length, "the accounts");
        }

        public static void TestARuleSaysWhatItDoes()
        {
            Assert.Equal("When Secret chat finds something with \"Kraken\" in it, play Buy egg 2 times after 3s on the client that saw it - only while you're away",
                Sample().Describe(), "in one sentence");

            Rule every = new Rule();
            every.When = RuleWhen.Every; every.EverySeconds = 300; every.Macro = "Collect"; every.On = RuleOn.EveryClient;
            Assert.Equal("Every 5 minutes, play Collect on every client", every.Describe(), "a timer");

            Rule key = new Rule();
            key.When = RuleWhen.Hotkey; key.HotkeyVk = 0x70; key.Ctrl = true; key.Macro = "Dance"; key.On = RuleOn.InFront;
            Assert.Equal("When you press Ctrl+F1, play Dance on the client in front", key.Describe(), "a hotkey");

            Rule join = new Rule();
            join.When = RuleWhen.Joins; join.Macro = "Claim"; join.DelaySeconds = 10;
            Assert.Equal("When a client joins a server, play Claim after 10s on that client", join.Describe(), "a join");
        }

        public static void TestARuleSaysWhatIsMissing()
        {
            Rule r = new Rule();
            Assert.Contains("name", r.Problem(), "a name");
            r.Name = "x";
            Assert.Contains("macro", r.Problem(), "a macro");
            r.Macro = "m";
            r.When = RuleWhen.Every;
            r.EverySeconds = 2;
            Assert.Contains("5 seconds", r.Problem(), "not a timer that never lets go of the keyboard");
            r.EverySeconds = 60;
            r.On = RuleOn.WhereItHappened;
            Assert.Contains("which clients", r.Problem(), "a timer has nowhere it happened");
            r.On = RuleOn.TheseAccounts;
            Assert.Contains("account", r.Problem(), "these accounts, but none ticked");
            r.Accounts = new string[] { "a" };
            Assert.Equal(null, r.Problem(), "fine");
        }

        // Pressing the key is being at the keyboard, so a key rule that waits
        // for you to be away could never fire. Said, rather than saved.
        public static void TestAKeyRuleCannotAlsoWaitForYouToBeAway()
        {
            Rule r = new Rule();
            r.Name = "k"; r.Macro = "m"; r.When = RuleWhen.Hotkey; r.On = RuleOn.InFront;
            r.OnlyWhenAway = true;
            Assert.Contains("away", r.Problem(), "it could never fire");
            r.OnlyWhenAway = false;
            Assert.Equal(null, r.Problem(), "fine without it");
        }

        // A rule belongs to a game's setup, so a timer for one game never
        // fires in another.
        public static void TestRulesLiveInTheirSetup()
        {
            string path = Path.Combine(Path.GetTempPath(), "rk-rules-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                WatchStore s = new WatchStore(path);
                s.Rules.Add(Sample());
                s.AddSetup("Other game", false);
                Assert.Equal(0, s.Rules.Count, "a new setup has none");
                s.Choose(WatchStore.FirstSetupName);
                WatchSetup copy = s.AddSetup("Copy", true);
                Assert.Equal(1, copy.Rules.Count, "a copy has its own");
                Assert.False(ReferenceEquals(copy.Rules[0], s.Setups[0].Rules[0]), "copies, not the same rule");
                s.Save();

                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.Equal(1, back.Setups[0].Rules.Count, "saved with the first setup");
                Assert.Equal(0, back.Setups[1].Rules.Count, "and not the second");
                Assert.Equal("Buy krakens", back.Setups[2].Rules[0].Name, "and the copy's");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        public static void TestRenamingAMacroCarriesItsRulesAlong()
        {
            WatchStore s = new WatchStore(Path.Combine(Path.GetTempPath(), "rk-unsaved.dat"));
            s.Rules.Add(Sample());
            Assert.Equal(1, s.RenameMacroUses("Buy egg", "Buy the egg"), "the rule used it");
            Assert.Equal("Buy the egg", s.Rules[0].Macro, "renamed along");
        }
    }
}
