using System;
using System.Collections.Generic;
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
            Assert.Equal("When Secret chat finds something with \"Kraken\" in it, play Buy egg 2 times after 3s on the Roblox window that saw it - only while you're away",
                Sample().Describe(), "in one sentence");

            Rule every = new Rule();
            every.When = RuleWhen.Every; every.EverySeconds = 300; every.Macro = "Collect"; every.On = RuleOn.EveryClient;
            Assert.Equal("Every 5 minutes, play Collect on every Roblox window", every.Describe(), "a timer");

            Rule key = new Rule();
            key.When = RuleWhen.Hotkey; key.HotkeyVk = 0x70; key.Ctrl = true; key.Macro = "Dance"; key.On = RuleOn.InFront;
            Assert.Equal("When you press Ctrl+F1, play Dance on the Roblox window in front", key.Describe(), "a hotkey");

            Rule join = new Rule();
            join.When = RuleWhen.Joins; join.Macro = "Claim"; join.DelaySeconds = 10;
            Assert.Equal("When a Roblox window joins a server, play Claim after 10s on that Roblox window", join.Describe(), "a join");
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
            Assert.Contains("which Roblox windows", r.Problem(), "a timer has nowhere it happened");
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

        static Rule ScriptRule()
        {
            Rule r = Sample();
            r.Macro = null;
            r.Script = "Farm loop";
            return r;
        }

        public static void TestARuleCanRunAScriptInsteadOfAMacro()
        {
            Rule r = ScriptRule();
            Assert.True(r.PlaysScript, "it runs a script");
            Assert.Equal(null, r.Problem(), "fine");
            // Times is a macro's: a script repeats by itself.
            Assert.Equal("When Secret chat finds something with \"Kraken\" in it, run the script Farm loop after 3s on the Roblox window that saw it - only while you're away",
                r.Describe(), "in one sentence");

            r.Script = null;
            Assert.Contains("macro or a script", r.Problem(), "neither chosen");
            r.Macro = "Buy egg";
            r.Script = "Farm loop";
            Assert.Contains("not both", r.Problem(), "both chosen");

            Rule timer = ScriptRule();
            timer.When = RuleWhen.Every; timer.EverySeconds = 2; timer.On = RuleOn.EveryClient;
            Assert.Contains("script holds the keyboard", timer.Problem(), "a too-fast timer says script");
        }

        public static void TestAScriptRuleSurvivesTheRoundTrip()
        {
            Rule back = WatchStore.DeserializeRule(WatchStore.SerializeRule(ScriptRule()));
            Assert.Equal("Farm loop", back.Script, "the script");
            Assert.Equal(null, back.Macro, "and no macro");
            Assert.Equal(40, back.GapSeconds, "the rest is kept");
            Assert.True(back.OnlyWhenAway, "the rest is kept");
        }

        // A line an older build wrote has no script field: it is a macro rule,
        // exactly as it was.
        public static void TestAnOldRuleLineLoadsAsAMacroRule()
        {
            string line = WatchStore.SerializeRule(Sample());
            string old = line.Substring(0, line.LastIndexOf('\t'));
            Assert.True(old.Split('\t').Length == 15, "fifteen fields, as before");
            Rule back = WatchStore.DeserializeRule(old);
            Assert.Equal("Buy egg", back.Macro, "still plays the macro");
            Assert.Equal(null, back.Script, "no script");
            Assert.False(back.PlaysScript, "a macro rule");
            Assert.Equal(null, back.Problem(), "still fine");
            Assert.Equal(line, WatchStore.SerializeRule(back), "writes back the same");
        }

        public static void TestRenamingAScriptCarriesItsRulesAlong()
        {
            WatchStore s = new WatchStore(Path.Combine(Path.GetTempPath(), "rk-unsaved.dat"));
            s.Rules.Add(ScriptRule());
            s.Rules.Add(Sample());
            s.AddSetup("Other game", false);
            Rule other = ScriptRule();
            other.Name = "Other";
            s.Rules.Add(other);
            Assert.Equal(2, s.RenameScriptUses("farm LOOP", "Farm better"), "both rules that used it, in both setups");
            Assert.Equal("Farm better", s.Setups[1].Rules[0].Script, "renamed along");
            Assert.Equal("Buy egg", s.Setups[0].Rules[1].Macro, "a macro rule is left alone");

            Assert.Equal(2, s.RenameScriptUses("Farm better", null), "removed");
            Assert.Equal(null, s.Setups[0].Rules[0].Script, "it points at nothing");
            Assert.Equal(null, s.Setups[1].Rules[0].Script, "in every setup");
            Assert.Contains("macro or a script", s.Setups[1].Rules[0].Problem(), "and says to choose again");
        }

        // The scripts as a rule sees them.
        class FakeScripts : IRuleScripts
        {
            public List<string> Have = new List<string> { "Farm loop" };
            public List<string> Started = new List<string>();     // "name|pid|tag"
            public List<string> Running = new List<string>();      // "name|pid|tag"
            public string Refuse;
            public List<string> Stopped = new List<string>();

            public bool Exists(string name) { return Have.Contains(name); }

            public string Start(string name, int pid, string tag)
            {
                if (Refuse != null) return Refuse;
                string run = name + "|" + pid + "|" + tag;
                Started.Add(run);
                Running.Add(run);
                return null;
            }

            public bool IsRunning(string name, int pid, string tag)
            {
                foreach (string run in Running)
                {
                    string[] p = run.Split('|');
                    if (p[0] == name && p[2] == tag && (pid <= 0 || p[1] == pid.ToString())) return true;
                }
                return false;
            }

            public int StopTagged(string tag)
            {
                int n = 0;
                for (int i = Running.Count - 1; i >= 0; i--)
                    if (Running[i].EndsWith("|" + tag)) { Stopped.Add(Running[i]); Running.RemoveAt(i); n++; }
                return n;
            }
        }

        // The window the rule fired on, and the rule's tag, go to the script.
        public static void TestARuleStartsItsScriptOnTheWindowItFiredOn()
        {
            FakeScripts host = new FakeScripts();
            List<string> said = new List<string>();
            RuleScripts scripts = new RuleScripts(host, delegate(string s) { said.Add(s); });
            Rule r = ScriptRule();
            scripts.Play(r, 4242, "rule:" + r.Name);
            Assert.Equal(1, host.Started.Count, "started once");
            Assert.Equal("Farm loop|4242|rule:Buy krakens", host.Started[0], "on that window, under the rule's tag");

            // Its timer comes round while it still runs: not started twice,
            // and nothing said about it.
            scripts.Play(r, 4242, "rule:" + r.Name);
            Assert.Equal(1, host.Started.Count, "not twice on one window");
            Assert.Equal(0, said.Count, "and not reported");

            // Another window is another run.
            scripts.Play(r, 777, "rule:" + r.Name);
            Assert.Equal(2, host.Started.Count, "once on each window");
        }

        public static void TestAMissingScriptIsSaidOncePerRule()
        {
            FakeScripts host = new FakeScripts();
            List<string> said = new List<string>();
            RuleScripts scripts = new RuleScripts(host, delegate(string s) { said.Add(s); });
            Rule r = ScriptRule();
            r.Script = "Gone";
            for (int i = 0; i < 3; i++) scripts.Play(r, 1, "rule:" + r.Name);
            Assert.Equal(0, host.Started.Count, "nothing started");
            Assert.Equal(1, said.Count, "said once");
            Assert.Contains("Gone", said[0], "names the script");
            Assert.Contains("Buy krakens", said[0], "and the rule");

            // Another rule has its own line.
            Rule other = ScriptRule();
            other.Name = "Other";
            other.Script = "Gone";
            scripts.Play(other, 1, "rule:Other");
            Assert.Equal(2, said.Count, "once for each rule");
        }

        public static void TestAScriptThatCannotStartIsSaidOncePerRule()
        {
            FakeScripts host = new FakeScripts();
            host.Refuse = "that Roblox window has closed.";
            List<string> said = new List<string>();
            RuleScripts scripts = new RuleScripts(host, delegate(string s) { said.Add(s); });
            Rule r = ScriptRule();
            scripts.Play(r, 5, "rule:" + r.Name);
            scripts.Play(r, 5, "rule:" + r.Name);
            Assert.Equal(1, said.Count, "said once");
            Assert.Contains("has closed", said[0], "with the reason");
        }

        // A key rule's key, pressed again, stops the script it started - and
        // only that rule's.
        public static void TestPressingTheKeyAgainStopsTheScript()
        {
            FakeScripts host = new FakeScripts();
            RuleScripts scripts = new RuleScripts(host, delegate(string s) { });
            Rule r = ScriptRule();
            Rule other = ScriptRule();
            other.Name = "Other";
            scripts.Play(r, 10, "rule:" + r.Name);
            scripts.Play(other, 10, "rule:" + other.Name);
            Assert.Equal(1, scripts.StopFor("rule:" + r.Name), "stopped one");
            Assert.Equal(1, host.Stopped.Count, "only the rule's own");
            Assert.True(host.Stopped[0].EndsWith("rule:Buy krakens"), "the right one");
            Assert.Equal(0, scripts.StopFor("rule:" + r.Name), "nothing left to stop, so the press starts it again");
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
