using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper.Tests
{
    // The Rules window and the rule editor.
    static class RuleUiTests
    {
        class NoCapture : ICapture
        {
            public Pixels Grab(int pid, out WatchState state) { state = WatchState.NotInAGame; return null; }
        }

        class NoReader : IReadText
        {
            public string Read(Pixels image) { return ""; }
        }

        static WatchKit Kit(string path)
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(path);
            k.Capture = new NoCapture();
            k.Reader = new NoReader();
            k.Engine = new WatchEngine(k.Capture, k.Reader);
            k.Clients = delegate { return new WatchedClient[0]; };
            k.Accounts = delegate { return (IList<string>)new List<string> { "VladDerKing", "alt1" }; };
            k.Log = delegate(string s) { };
            k.Store.Watchers.Add(Watcher.Default("Secret chat", WatchKind.ChatLine));
            Macro m = new Macro();
            m.Name = "Buy egg";
            m.Steps.Add(MacroStep.Key(0x45, 50));
            k.Store.Macros.Add(m);
            return k;
        }

        static string TempStore()
        {
            return Path.Combine(Path.GetTempPath(), "rk-ruleui-" + Guid.NewGuid().ToString("N") + ".dat");
        }

        static Rule Timer()
        {
            Rule r = new Rule();
            r.Name = "Collect"; r.When = RuleWhen.Every; r.EverySeconds = 120; r.Macro = "Buy egg";
            r.On = RuleOn.TheseAccounts; r.Accounts = new string[] { "alt1" }; r.Times = 2; r.DelaySeconds = 4;
            r.GapSeconds = 30; r.OnlyWhenAway = true;
            return r;
        }

        // Opening a rule and saving it untouched gives back exactly that rule,
        // for every kind of when.
        public static void TestEveryRuleSurvivesTheEditorUnchanged()
        {
            Rule timer = Timer();
            Rule found = Timer();
            found.When = RuleWhen.WatcherFinds; found.Watcher = "Secret chat"; found.FoundContains = "Kraken";
            found.On = RuleOn.WhereItHappened; found.Accounts = null;
            Rule key = Timer();
            key.When = RuleWhen.Hotkey; key.HotkeyVk = 0x72; key.Ctrl = true; key.Alt = true; key.On = RuleOn.InFront; key.Accounts = null;
            Rule join = Timer();
            join.When = RuleWhen.Joins; join.On = RuleOn.EveryClient; join.Accounts = null;

            foreach (Rule r in new Rule[] { timer, found, key, join })
                using (RuleEditDialog d = new RuleEditDialog(r, Kit(TempStore())))
                    Assert.Equal(WatchStore.SerializeRule(r), WatchStore.SerializeRule(d.Collect()), "unchanged: " + r.When);
        }

        public static void TestANewRuleIsBuiltFromWhatIsChosen()
        {
            using (RuleEditDialog d = new RuleEditDialog(new Rule(), Kit(TempStore())))
            {
                d.SetName("Dance");
                d.SetWhen(RuleWhen.Hotkey);
                d.SetHotkey(0x71, true, false, true);
                d.SetMacro("Buy egg");
                d.SetOn(RuleOn.InFront);
                Assert.True(d.TrySave(), "saved");
                Assert.Equal("When you press Ctrl+Shift+F2, play Buy egg on the client in front", d.Result.Describe(), "as chosen");
            }
        }

        public static void TestTheEditorSaysWhatIsMissing()
        {
            using (RuleEditDialog d = new RuleEditDialog(new Rule(), Kit(TempStore())))
            {
                d.SetName("x");
                Assert.False(d.TrySave(), "no macro chosen");
                Assert.Contains("macro", d.ProblemText, "says so");
            }
        }

        // "The client it happened on" means nothing to a timer or a key, so
        // it is only offered where something happens on a client.
        public static void TestWhereItHappenedIsOnlyOfferedWhenSomethingHappensOnAClient()
        {
            Assert.True(RuleForm.OnChoices(RuleWhen.WatcherFinds).Contains(RuleOn.WhereItHappened), "a watcher's find");
            Assert.True(RuleForm.OnChoices(RuleWhen.Joins).Contains(RuleOn.WhereItHappened), "a join");
            Assert.False(RuleForm.OnChoices(RuleWhen.Every).Contains(RuleOn.WhereItHappened), "not a timer");
            Assert.False(RuleForm.OnChoices(RuleWhen.Hotkey).Contains(RuleOn.WhereItHappened), "not a key");
        }

        // F8 ends a recording, so a rule can't take it.
        public static void TestTheKeysOfferedLeaveOutTheRecordingKey()
        {
            Assert.False(RuleForm.HotkeyChoices().Contains(MacroRecording.STOP_KEY), "no F8");
            Assert.True(RuleForm.HotkeyChoices().Contains((byte)0x70), "F1 is there");
        }

        public static void TestTheListAddsSwitchesAndRemovesRulesAndSaves()
        {
            string path = TempStore();
            try
            {
                WatchKit k = Kit(path);
                using (RulesDialog d = new RulesDialog(k))
                {
                    d.AddRule(Timer());
                    Rule second = Timer();
                    second.Name = "Other";
                    d.AddRule(second);
                    Assert.Equal(2, d.RowCount, "two rows");
                    d.SetEnabled(0, false);
                    d.RemoveAt(1);
                    Assert.Equal(1, d.RowCount, "one left");
                }
                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.Equal(1, back.Rules.Count, "saved");
                Assert.False(back.Rules[0].Enabled, "switched off");
            }
            finally { try { File.Delete(path); } catch { } }
        }
    }
}
