using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Playing a macro from the Macros window: now, or by itself. The owner's
    // report was that a macro could not be made to play on every client at
    // the press of a key - it could, from Rules, which nothing in the Macros
    // window led to. These are the rule a macro's Play menu makes.
    static class MacroPlayTests
    {
        class NoCapture : ICapture
        {
            public Pixels Grab(int pid, out WatchState state) { state = WatchState.NotInAGame; return null; }
        }

        class NoReader : IReadText
        {
            public string Read(Pixels image) { return ""; }
        }

        class Host : IMacroHost
        {
            readonly AutoClick clicker = new AutoClick();
            public byte Stop = 0x79;
            public AutoClick Clicker { get { return clicker; } }
            public void SaveClicker() { }
            public bool ClickerOn { get { return false; } }
            public string ToggleClicker() { return null; }
            public byte StopKey { get { return Stop; } set { Stop = value; } }
            public string StopKeyProblem(byte vk) { return null; }
            public void StopEverything() { }
            public int SessionsRunning { get { return 0; } }
            public string KeyUseProblem(byte vk, string usedFor, string editing) { return null; }
            public void MacrosSaved() { }
            public string PlayAnywhere(Macro m) { return null; }
            public bool IsPlaying(string tag) { return false; }
            public bool StopTag(string tag) { return false; }
            public Macro QuickRecording { get; set; }
        }

        static WatchKit Kit(Host host)
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(Path.Combine(Path.GetTempPath(), "rk-play-" + Guid.NewGuid().ToString("N") + ".dat"));
            k.Capture = new NoCapture();
            k.Reader = new NoReader();
            k.Engine = new WatchEngine(k.Capture, k.Reader);
            k.Clients = delegate { return new WatchedClient[0]; };
            k.Accounts = delegate { return (IList<string>)new List<string>(new string[] { "main", "alt1" }); };
            k.Log = delegate(string s) { };
            k.Macros = host;
            Macro m = MacroForm.Example(0);
            m.Name = "Jump";
            k.Store.Macros.Add(m);
            return k;
        }

        // What was asked for: a key, and every client.
        public static void TestAKeyRuleIsReadyToSaveAsItOpens()
        {
            WatchKit k = Kit(new Host());
            Rule r = MacroForm.StartRule(k.Store, "Jump", RuleWhen.Hotkey, null);
            Assert.Equal(null, r.Problem(), "nothing left to fill in");
            Assert.Equal(RuleWhen.Hotkey, r.When, "a key");
            Assert.Equal(RuleOn.EveryClient, r.On, "on every client");
            Assert.Equal("Jump", r.Macro, "this macro");
            Assert.Equal("Jump - key", r.Name, "named after it");
            Assert.Equal((byte)0x70, r.HotkeyVk, "the first key there is");
            Assert.Contains("When you press F1, play Jump on every Roblox window", r.Describe(), "and it reads as what was asked");
        }

        public static void TestTheKeyOfferedIsOneNothingElseIsOn()
        {
            Host host = new Host();
            host.Stop = 0x70;                       // F1 stops macros
            WatchKit k = Kit(host);
            Rule other = MacroForm.StartRule(k.Store, "Jump", RuleWhen.Hotkey, null);
            other.HotkeyVk = 0x71;                  // and a rule is on F2
            k.Store.Rules.Add(other);

            using (MacrosDialog d = new MacrosDialog(k))
            {
                Rule r = d.StartRuleFor(0, RuleWhen.Hotkey);
                Assert.Equal((byte)0x72, r.HotkeyVk, "F3: not the stop key, not another rule's key");
                Assert.Equal("Jump - key 2", r.Name, "and not another rule's name");
            }

            // A rule on Ctrl+F2 does not have F2.
            other.Ctrl = true;
            Assert.Equal((byte)0x70, MacroForm.StartRule(k.Store, "Jump", RuleWhen.Hotkey, null).HotkeyVk, "with Ctrl it is a different key");
        }

        // Every way of starting one, each ready as it opens.
        public static void TestEveryWayOfStartingAMacroIsOffered()
        {
            WatchKit k = Kit(new Host());
            Assert.Equal(MacroForm.StartWhens.Length, MacroForm.StartNames.Length, "a name for each");
            Assert.Equal(4, MacroForm.StartWhens.Length, "a key, a timer, a watcher, a join");
            foreach (RuleWhen when in MacroForm.StartWhens)
            {
                Rule r = MacroForm.StartRule(k.Store, "Jump", when, null);
                Assert.Equal(null, r.Problem(), when + " is ready to save");
                Assert.Equal(when == RuleWhen.Hotkey || when == RuleWhen.Every ? RuleOn.EveryClient : RuleOn.WhereItHappened, r.On,
                             when + " plays where it makes sense to");
            }
        }

        // The rule editor opens on it and shows it as it is: saving without
        // touching anything saves what was offered.
        public static void TestTheRuleEditorOpensOnItUnchanged()
        {
            WatchKit k = Kit(new Host());
            Rule offered = MacroForm.StartRule(k.Store, "Jump", RuleWhen.Hotkey, null);
            using (RuleEditDialog d = new RuleEditDialog(offered, k, "Play Jump by itself"))
            {
                Assert.True(d.TrySave(), "saves as it stands: " + d.ProblemText);
                Assert.Equal(WatchStore.SerializeRule(offered), WatchStore.SerializeRule(d.Result), "what was offered");
            }
        }

        // Added from the Macros window, it is a rule like any other - and
        // the macro's row says what starts it.
        public static void TestTheMacrosRowSaysWhatStartsIt()
        {
            WatchKit k = Kit(new Host());
            Assert.Equal("", MacroForm.Starts(k.Store, k.Store.Macros[0]), "nothing starts it yet");
            using (MacrosDialog d = new MacrosDialog(k))
            {
                d.AddStartRule(d.StartRuleFor(0, RuleWhen.Hotkey));
                d.AddStartRule(d.StartRuleFor(0, RuleWhen.Every));
            }
            Assert.Equal(2, k.Store.Rules.Count, "two rules");
            Assert.Equal("F1, every 5 minutes", MacroForm.Starts(k.Store, k.Store.Macros[0]), "a key and a timer");

            k.Store.Rules[0].Enabled = false;
            Assert.Equal("every 5 minutes", MacroForm.Starts(k.Store, k.Store.Macros[0]), "one switched off does not start anything");

            Macro other = MacroForm.Example(1);
            other.Name = "Click";
            k.Store.Macros.Add(other);
            Assert.Equal("", MacroForm.Starts(k.Store, other), "and another macro's row is its own");
        }

        // The main window plays it; with nowhere to play it, nothing is sent.
        public static void TestPlayingNowWithNoClientsSendsNothing()
        {
            using (MainForm f = new MainForm(false))
            {
                IMacroPlay play = (IMacroPlay)f;
                Macro m = MacroForm.Example(0);
                Assert.Equal(0, play.PlayNow(m, new WatchedClient[0]), "no clients");
                Assert.Equal(0, play.PlayNow(m, null), "no list");
                Assert.Equal(0, play.PlayNow(null, new WatchedClient[0]), "no macro");
            }
        }
    }
}
