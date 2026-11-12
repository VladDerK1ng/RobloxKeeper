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

        // The few things a rule asks of the scripts: their names.
        class FakeHost : IScriptHost
        {
            readonly ScriptStore store;
            public FakeHost(ScriptStore store) { this.store = store; }
            public ScriptStore Store { get { return store; } }
            public string StartScript(string name) { return null; }
            public string StartScript(string name, int pid, string tag) { return null; }
            public bool StopScript(string name) { return false; }
            public bool IsScriptRunning(string name) { return false; }
            public List<ScriptRunInfo> ScriptsRunning() { return new List<ScriptRunInfo>(); }
            public string ScriptKeyProblem(byte vk, string name) { return null; }
            public void ScriptsSaved() { }
            public bool IsScriptTrusted(string name) { return true; }
            public void TrustScript(string name) { }
            public string RenameScript(string from, string to) { return null; }
            public string RemoveScript(string name) { return null; }
            public string MacroAsLua(Macro m) { return ""; }
            public string ExportScript(string name, string zipPath) { return null; }
            public ScriptImport ImportScript(string zipPath) { return null; }
            public string Notices { get { return ""; } }
            public event Action<string, string> ScriptOutput { add { } remove { } }
            public event Action<string, int> ScriptLine { add { } remove { } }
            public event Action<string, string> ScriptStatus { add { } remove { } }
            public event Action<string, ScriptResult> ScriptEnded { add { } remove { } }
        }

        // A kit whose scripts are the ones named; they are files in a folder
        // of their own.
        static WatchKit KitWithScripts(string path, params string[] scripts)
        {
            WatchKit k = Kit(path);
            string dir = Path.Combine(Path.GetTempPath(), "rk-ruleui-scripts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            ScriptStore store = new ScriptStore(dir);
            foreach (string s in scripts) store.Create(s, "print(1)");
            k.Scripts = new FakeHost(store);
            return k;
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

        // Macros first, then scripts, each labelled once there are scripts.
        public static void TestTheEditorListsMacrosThenScripts()
        {
            using (RuleEditDialog d = new RuleEditDialog(new Rule(), KitWithScripts(TempStore(), "Farm loop", "Chat")))
            {
                List<string> items = d.PlayItems();
                Assert.Equal(4, items.Count, "the prompt, a macro and two scripts");
                Assert.Equal("Choose a macro or a script", items[0], "the prompt");
                Assert.Equal("Macro: Buy egg", items[1], "the macro");
                Assert.Equal("Script: Chat", items[2], "a script");
                Assert.Equal("Script: Farm loop", items[3], "another");
            }
            // With no scripts the list reads as it always did.
            using (RuleEditDialog d = new RuleEditDialog(new Rule(), Kit(TempStore())))
            {
                List<string> items = d.PlayItems();
                Assert.Equal("Choose a macro", items[0], "the prompt");
                Assert.Equal("Buy egg", items[1], "no label needed");
                Assert.Equal(2, items.Count, "nothing else");
            }
        }

        public static void TestChoosingAScriptHidesWhatOnlyMacrosUse()
        {
            using (RuleEditDialog d = new RuleEditDialog(new Rule(), KitWithScripts(TempStore(), "Farm loop")))
            {
                d.SetName("Farm");
                d.SetWhen(RuleWhen.Hotkey);
                d.SetHotkey(0x71, false, false, false);
                d.SetOn(RuleOn.InFront);
                d.SetMacro("Buy egg");
                Assert.True(d.TimesShown, "a macro can be played several times");
                Assert.Equal(null, d.ScriptNote, "and has no note");

                d.SetScript("Farm loop");
                Assert.False(d.TimesShown, "a script repeats by itself");
                Assert.Contains("Press the key again", d.ScriptNote, "a key rule says how to stop it");
                d.SetWhen(RuleWhen.Every);
                Assert.False(d.ScriptNote.Contains("Press the key"), "a timer doesn't");

                d.SetWhen(RuleWhen.Hotkey);
                d.SetOn(RuleOn.InFront);
                Assert.True(d.TrySave(), "saved");
                Assert.Equal("Farm loop", d.Result.Script, "the script");
                Assert.Equal(null, d.Result.Macro, "and no macro");
                Assert.Equal("When you press F2, run the script Farm loop on the Roblox window in front", d.Result.Describe(), "as chosen");

                d.SetMacro("Buy egg");
                Assert.True(d.TimesShown, "back to a macro: Times is back");
                Assert.True(d.TrySave(), "saved again");
                Assert.Equal("Buy egg", d.Result.Macro, "the macro");
                Assert.Equal(null, d.Result.Script, "and no script");
            }
        }

        public static void TestAScriptRuleSurvivesTheEditorUnchanged()
        {
            Rule r = Timer();
            r.Macro = null;
            r.Script = "Farm loop";
            using (RuleEditDialog d = new RuleEditDialog(r, KitWithScripts(TempStore(), "Farm loop")))
                Assert.Equal(WatchStore.SerializeRule(r), WatchStore.SerializeRule(d.Collect()), "unchanged");

            // A script that has since been removed stays chosen, so opening
            // the rule doesn't quietly drop it.
            using (RuleEditDialog d = new RuleEditDialog(r, KitWithScripts(TempStore())))
                Assert.Equal("Farm loop", d.Collect().Script, "kept");
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
                Assert.Equal("When you press Ctrl+Shift+F2, play Buy egg on the Roblox window in front", d.Result.Describe(), "as chosen");
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
