using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // The windows for macros that play anywhere on the screen and have a key:
    // the editor's new settings, the list, the Quick Record card and the
    // rule's key picker. The playing and recording underneath have tests of
    // their own; here a stand-in for the main window says what was asked of it.
    static class StandaloneMacroUiTests
    {
        class NoCapture : ICapture
        {
            public Pixels Grab(int pid, out WatchState state) { state = WatchState.NotInAGame; return null; }
        }

        class NoReader : IReadText
        {
            public string Read(Pixels image) { return ""; }
        }

        // The main window, as far as these windows can tell. It answers key
        // questions with the real check, over keys the test fills in.
        class Host : IMacroHost
        {
            readonly AutoClick clicker = new AutoClick();
            public byte Stop = 0x79;
            public readonly KeyOwners Owners = new KeyOwners();
            public Macro Quick;
            public bool Playing;
            public string PlayRefusal;
            public int Saved;
            public readonly List<Macro> Played = new List<Macro>();
            public readonly List<string> Stopped = new List<string>();

            public Host() { Owners.StopKey = Stop; }

            public AutoClick Clicker { get { return clicker; } }
            public void SaveClicker() { }
            public bool ClickerOn { get { return false; } }
            public string ToggleClicker() { return null; }
            public byte StopKey { get { return Stop; } set { Stop = value; Owners.StopKey = value; } }
            public string StopKeyProblem(byte vk) { return null; }
            public void StopEverything() { }
            public int SessionsRunning { get { return 0; } }
            public string KeyUseProblem(byte vk, string usedFor, string editing) { return KeyUse.Problem(vk, usedFor, editing, Owners); }
            public void MacrosSaved() { Saved++; }
            public string PlayAnywhere(Macro m) { if (PlayRefusal != null) return PlayRefusal; Played.Add(m.Copy()); return null; }
            public bool IsPlaying(string tag) { return Playing; }
            public bool StopTag(string tag) { Stopped.Add(tag); return true; }

            public Macro QuickRecording
            {
                get { return Quick == null ? null : Quick.Copy(); }
                set
                {
                    Quick = value == null ? null : value.Copy();
                    if (Quick != null) { Quick.Anywhere = true; if (string.IsNullOrEmpty(Quick.Name)) Quick.Name = MacroPlay.QUICK_NAME; }
                }
            }
        }

        static string TempStore()
        {
            return Path.Combine(Path.GetTempPath(), "rk-standalone-ui-" + Guid.NewGuid().ToString("N") + ".dat");
        }

        static WatchKit Kit(string path, Host host)
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(path);
            k.Capture = new NoCapture();
            k.Reader = new NoReader();
            k.Engine = new WatchEngine(k.Capture, k.Reader);
            k.Clients = delegate { return new WatchedClient[0]; };
            k.Accounts = delegate { return (IList<string>)new List<string>(); };
            k.Log = delegate(string s) { };
            k.Macros = host;
            return k;
        }

        static Macro Named(string name)
        {
            Macro m = new Macro();
            m.Name = name;
            m.Steps.Add(MacroStep.Key(0x45, 50));
            return m;
        }

        static List<MacroStep> Recording()
        {
            List<MacroStep> got = new List<MacroStep>();
            got.Add(MacroStep.Click(0.25, 0.5, false));
            got.Add(MacroStep.Wait(1200));
            got.Add(MacroStep.Key(0x45, 50));
            return got;
        }

        // ---------- the editor ----------

        // The settings used to be lost on every save: the editor built a
        // fresh macro from the name and the steps alone.
        public static void TestTheEditorKeepsWhereAKeyAndHowOftenItPlays()
        {
            Host host = new Host();
            WatchKit k = Kit(TempStore(), host);
            Macro m = Named("Shop");
            m.Anywhere = true; m.Key = 0x76; m.KeyMode = MacroKeyMode.Once; m.Times = 5; m.SpeedPct = 200;
            using (MacroEditDialog d = new MacroEditDialog(m, k))
            {
                Assert.True(d.TrySave("Shop"), "saves as it was");
                Assert.True(d.Result.Anywhere, "still plays anywhere");
                Assert.Equal((byte)0x76, d.Result.Key, "key");
                Assert.Equal(MacroKeyMode.Once, d.Result.KeyMode, "what the key does");
                Assert.Equal(5, d.Result.Times, "how many times");
                Assert.Equal(200, d.Result.SpeedPct, "how fast");
            }

            Macro odd = Named("Odd");
            odd.Times = 7; odd.SpeedPct = 75;
            using (MacroEditDialog d = new MacroEditDialog(odd, k))
            {
                Assert.True(d.TrySave("Odd"), "an odd count and speed");
                Assert.Equal(7, d.Result.Times, "a count that isn't in the list is kept, not rounded");
                Assert.Equal(75, d.Result.SpeedPct, "so is a speed");
            }
        }

        public static void TestTheEditorSavesWhatWasChosen()
        {
            Host host = new Host();
            WatchKit k = Kit(TempStore(), host);
            using (MacroEditDialog d = new MacroEditDialog(Named("Walk"), k))
            {
                d.SetWhere(true);
                Assert.Equal(null, d.SetKey(0x77 + 1), "a free key");     // F9
                d.SetKeyMode(MacroKeyMode.Hold);
                d.SetSpeed(400);
                Assert.True(d.TrySave("Walk"), "saves");
                Assert.True(d.Result.Anywhere, "anywhere");
                Assert.Equal((byte)0x78, d.Result.Key, "the key");
                Assert.Equal(MacroKeyMode.Hold, d.Result.KeyMode, "held");
                Assert.Equal(400, d.Result.SpeedPct, "four times faster");
                Assert.Equal(1, d.Result.Times, "and a count the hidden Repeat row left alone");
            }
            using (MacroEditDialog d = new MacroEditDialog(Named("Spin"), k))
            {
                d.SetRepeat(0);
                Assert.True(d.TrySave("Spin"), "saves");
                Assert.Equal(0, d.Result.Times, "until I stop it");
                Assert.False(d.Result.Anywhere, "a Roblox window unless said otherwise");
                Assert.Equal((byte)0, d.Result.Key, "no key unless one is chosen");
            }
        }

        // Only the rows that apply are there: a key that starts and stops it
        // goes round until stopped, so there is no count to give.
        public static void TestTheEditorShowsOnlyWhatAppliesToTheKey()
        {
            WatchKit k = Kit(TempStore(), new Host());
            using (MacroEditDialog d = new MacroEditDialog(Named("Shop"), k))
            {
                Assert.False(d.KeyModeShown, "no key, nothing to say about pressing it");
                Assert.True(d.RepeatShown, "but a Play button repeats it");
                d.SetKey(0x76);
                Assert.True(d.KeyModeShown, "with a key, what pressing it does");
                Assert.True(d.RepeatShown, "pressed it plays it once, as many times as it says");
                d.SetKeyMode(MacroKeyMode.Toggle);
                Assert.False(d.RepeatShown, "start and stop goes round until stopped");
                d.SetKeyMode(MacroKeyMode.Hold);
                Assert.False(d.RepeatShown, "so does holding");
                d.SetKey(0);
                Assert.False(d.KeyModeShown, "key taken away");
                Assert.True(d.RepeatShown, "the count is back");
            }
        }

        // The Roblox window picker is for Roblox windows. On the screen
        // there is nothing to wait for.
        public static void TestAnywhereNeedsNoRobloxWindowToRecordOrTry()
        {
            WatchKit k = Kit(TempStore(), new Host());
            using (MacroEditDialog d = new MacroEditDialog(Named("Shop"), k))
            {
                Assert.False(d.CanRecord, "no Roblox window, so no recording on one");
                Assert.False(d.CanTry, "and no trying");
                Assert.Contains("Anywhere on my screen", d.StatusText, "and it says what to do instead");
                Assert.True(d.ClientRowShown, "the window picker is shown");
                d.SetWhere(true);
                Assert.True(d.CanRecord, "on the screen it can record");
                Assert.True(d.CanTry, "and try");
                Assert.False(d.ClientRowShown, "with no window to pick");
                Assert.Contains("F8", d.StatusText, "and F8 ends it");
                d.SetWhere(false);
                Assert.False(d.CanRecord, "back to a Roblox window");
            }
        }

        // A recording on the screen lands in the list like any other.
        public static void TestARecordingOnTheScreenLandsInTheStepList()
        {
            WatchKit k = Kit(TempStore(), new Host());
            using (MacroEditDialog d = new MacroEditDialog(new Macro(), k))
            {
                d.SetWhere(true);
                d.Recorded(Recording());
                Assert.Equal(3, d.StepCount, "three steps");
                Assert.Contains("Recorded 3 steps", d.StatusText, "and it says so");
                d.Recorded(new List<MacroStep>());
                Assert.Equal(3, d.StepCount, "nothing recorded adds nothing");
                Assert.Contains("Nothing was recorded", d.StatusText, "and says that");
            }
        }

        // Short screens (768 high) must still show the whole window.
        public static void TestTheEditorStaysShortEnoughForASmallScreen()
        {
            WatchKit k = Kit(TempStore(), new Host());
            Assert.True(MacroEditDialog.HEIGHT <= 690, "at most 690 high");
            using (MacroEditDialog d = new MacroEditDialog(new Macro(), k))
            {
                Assert.True(d.ClientSize.Height <= 690, "new macro: " + d.ClientSize.Height);
                d.SetWhere(true);
                d.SetKey(0x76);
                Assert.True(d.ClientSize.Height <= 690, "every row showing: " + d.ClientSize.Height);
                foreach (Control c in d.Controls)
                    Assert.True(c.Bottom <= d.ClientSize.Height, "nothing below the window: " + c.GetType().Name + " " + c.Bottom);
            }
        }

        // One check for every key: a key another job has is said so, and
        // the macro keeps the one it had.
        public static void TestAKeyAnotherJobHasIsRefusedInTheEditor()
        {
            Host host = new Host();
            host.Owners.ShotKey = 0x7B;                                        // F12 takes screenshots
            host.Owners.Macros.Add(new KeyValuePair<string, byte>("Jump", 0x75));  // F6 plays Jump
            WatchKit k = Kit(TempStore(), host);
            using (MacroEditDialog d = new MacroEditDialog(Named("Shop"), k))
            {
                Assert.Contains("screenshots", d.SetKey(0x7B), "a screenshot key");
                Assert.Contains("screenshots", d.StatusText, "shown in the status line");
                Assert.Contains("Jump", d.SetKey(0x75), "another macro's key");
                Assert.Contains("F10", d.SetKey(0x79), "the stop key");
                Assert.Contains("ends a recording", d.SetKey(MacroRecording.STOP_KEY), "F8");
                Assert.Equal((byte)0, d.Draft.Key, "none of them was taken");
                Assert.Equal(null, d.SetKey(0x76), "a free one");
                Assert.Equal((byte)0x76, d.Draft.Key, "is");
            }
            // Its own key is not a clash, and renaming it doesn't make it one.
            Macro had = Named("Jump");
            had.Key = 0x75;
            using (MacroEditDialog d = new MacroEditDialog(had, k))
            {
                Assert.Equal(null, d.SetKey(0x75), "its own key");
                Assert.True(d.TrySave("Jump again"), "saved under a new name");
                Assert.Equal((byte)0x75, d.Result.Key, "with the same key");
            }
        }

        // ---------- the list ----------

        public static void TestCopyingAMacroLeavesItsKeyBehind()
        {
            string path = TempStore();
            try
            {
                Host host = new Host();
                WatchKit k = Kit(path, host);
                Macro m = Named("Shop");
                m.Key = 0x76; m.Anywhere = true; m.Times = 3;
                k.Store.Macros.Add(m);
                using (MacrosDialog d = new MacrosDialog(k))
                {
                    d.CopyAt(0);
                    Assert.Equal(2, d.RowCount, "a copy");
                }
                Assert.Equal((byte)0x76, k.Store.Macros[0].Key, "the first keeps its key");
                Assert.Equal((byte)0, k.Store.Macros[1].Key, "the copy has none: two macros would play together");
                Assert.True(k.Store.Macros[1].Anywhere, "but still plays anywhere");
                Assert.Equal(3, k.Store.Macros[1].Times, "and as often");
                Assert.True(host.Saved > 0, "and the main window was told");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        public static void TestARowSaysWhereItPlaysAndItsKey()
        {
            Macro plain = Named("a");
            Assert.Equal("", MacroForm.RowTag(plain), "a Roblox macro with no key has nothing to add");
            plain.Key = 0x76;
            Assert.Equal("F7", MacroForm.RowTag(plain), "its key");
            plain.Anywhere = true;
            Assert.Equal("anywhere · F7", MacroForm.RowTag(plain), "and where");
            plain.Key = 0;
            Assert.Equal("anywhere", MacroForm.RowTag(plain), "where, with no key");
        }

        public static void TestPlayingAnAnywhereMacroFromTheListHandsItToTheMainWindow()
        {
            string path = TempStore();
            try
            {
                Host host = new Host();
                WatchKit k = Kit(path, host);
                Macro m = Named("Shop");
                m.Anywhere = true; m.Times = 3;
                k.Store.Macros.Add(m);
                using (MacrosDialog d = new MacrosDialog(k))
                {
                    d.PlayAnywhereNow(m);
                    Assert.Equal(1, host.Played.Count, "played");
                    Assert.Equal("Shop", host.Played[0].Name, "that one");
                    d.StopAnywhere(m);
                    Assert.Contains("Shop", string.Join(",", host.Stopped.ToArray()), "and stopped by the tag it was played under");
                    Assert.True(host.Stopped.Contains(MacroPlay.PlayTag("Shop")), "from its Play button");
                    Assert.True(host.Stopped.Contains(MacroPlay.KeyTag("Shop")), "or its key");
                }
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // ---------- Quick Record ----------

        static QuickRecordCard Card(Host host, WatchKit k)
        {
            return new QuickRecordCard(host, delegate { return k; }, delegate { return null; }, delegate(string s) { }, 862);
        }

        public static void TestQuickRecordKeepsARecordingAndSaysSo()
        {
            Host host = new Host();
            WatchKit k = Kit(TempStore(), host);
            using (QuickRecordCard c = Card(host, k))
            {
                Assert.Equal("Nothing recorded yet.", c.StatusText, "nothing yet");
                Assert.False(c.CanPlay, "so nothing to play");
                Assert.False(c.CanSave, "or to save");
                c.TakeRecording(Recording());
                Assert.True(host.Quick != null, "kept by the main window");
                Assert.True(host.Quick.Anywhere, "for the screen");
                Assert.Equal(3, host.Quick.Steps.Count, "its steps");
                Assert.Equal(MacroPlay.QUICK_NAME, host.Quick.Name, "under its own name");
                Assert.Equal("Recorded 3 steps - 1 second.", c.StatusText, "and the line says what it has");
                Assert.True(c.CanPlay, "now it can be played");
                Assert.True(c.CanSave, "and saved");
                c.TakeRecording(new List<MacroStep>());
                Assert.Equal(3, host.Quick.Steps.Count, "an empty one doesn't replace it");
                Assert.Contains("Nothing was recorded", c.StatusText, "and says why");
            }
        }

        // The ask: Save as macro keeps it as a named macro that plays
        // anywhere, on disk, with the main window told its keys may have changed.
        public static void TestQuickRecordSavesANamedMacroThatPlaysAnywhere()
        {
            string path = TempStore();
            try
            {
                Host host = new Host();
                WatchKit k = Kit(path, host);
                k.Store.Macros.Add(Named("Taken"));
                using (QuickRecordCard c = Card(host, k))
                {
                    Assert.Contains("Record something", c.SaveAs("Shop"), "nothing to save yet");
                    c.TakeRecording(Recording());
                    c.ChooseRepeat(3);
                    c.ChooseSpeed(200);
                    Assert.Contains("already", c.SaveAs("taken"), "a name in use");
                    Assert.Contains("name", c.SaveAs("  "), "no name");
                    Assert.Contains("Quick Record uses that name", c.SaveAs(MacroPlay.QUICK_NAME), "the recording's own name");
                    Assert.Equal(null, c.SaveAs(" Open the shop "), "saved");
                    Assert.Contains("Open the shop", c.StatusText, "and said");
                    Assert.Contains("key", c.StatusText, "with where to give it a key");
                }
                Assert.True(host.Saved > 0, "the main window was told");
                WatchStore back = new WatchStore(path);
                back.Load();
                Macro m = back.FindMacro("Open the shop");
                Assert.True(m != null, "on disk");
                Assert.True(m.Anywhere, "plays anywhere");
                Assert.Equal((byte)0, m.Key, "no key yet");
                Assert.Equal(3, m.Times, "three times");
                Assert.Equal(200, m.SpeedPct, "twice as fast");
                Assert.Equal(3, m.Steps.Count, "its steps");
                Assert.Equal(2, back.Macros.Count, "and nothing else added");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // The choices live on the recording, so they are what they were
        // when the app is opened again.
        public static void TestQuickRecordKeepsRepeatAndSpeedOnTheRecording()
        {
            Host host = new Host();
            WatchKit k = Kit(TempStore(), host);
            host.Quick = Named("Quick recording");
            host.Quick.Anywhere = true;
            host.Quick.Times = 10;
            host.Quick.SpeedPct = 50;
            using (QuickRecordCard c = Card(host, k))
            {
                // Opening it shows what is kept - and doesn't write anything.
                host.Quick.Times = 10;
                c.ChooseRepeat(0);
                Assert.Equal(0, host.Quick.Times, "until I stop it");
                c.ChooseSpeed(400);
                Assert.Equal(400, host.Quick.SpeedPct, "four times faster");
            }
            using (QuickRecordCard c = Card(host, k))
            {
                c.ChooseRepeat(5);
                Assert.Equal(5, host.Quick.Times, "five times");
                Assert.Equal(400, host.Quick.SpeedPct, "the speed is as it was");
            }
        }

        public static void TestQuickRecordPlaysWhatWasChosenAndFlipsToStop()
        {
            Host host = new Host();
            WatchKit k = Kit(TempStore(), host);
            using (QuickRecordCard c = Card(host, k))
            {
                Assert.Contains("Record something", c.StartPlay(), "nothing to play yet");
                c.TakeRecording(Recording());
                c.ChooseRepeat(5);
                c.ChooseSpeed(200);
                Assert.Equal(null, c.StartPlay(), "started");
                Assert.Equal(1, host.Played.Count, "handed to the main window");
                Assert.Equal(5, host.Played[0].Times, "five times");
                Assert.Equal(200, host.Played[0].SpeedPct, "twice as fast");
                Assert.True(host.Played[0].Anywhere, "anywhere");
                Assert.Equal("Stop", c.PlayText, "Play is Stop now");
                Assert.Contains("F10", c.StatusText, "and the line says how to stop it");
                Assert.False(c.CanSave, "nothing is saved mid-play");

                host.Playing = true;        // it goes on after the page is left and come back to
                c.Reload();
                Assert.Equal("Stop", c.PlayText, "still");
                host.Playing = false;
                c.Reload();
                Assert.Equal("Play", c.PlayText, "it ended");
                Assert.Contains("Recorded 3 steps", c.StatusText, "and the line is the recording again");
            }
            host.PlayRefusal = "Quick recording is already playing.";
            using (QuickRecordCard c = Card(host, k))
            {
                host.Quick = Named("Quick recording");
                Assert.Contains("already playing", c.StartPlay(), "a refusal is passed on");
            }
        }

        // ---------- the rule's key ----------

        static Rule KeyRule(byte vk)
        {
            Rule r = new Rule();
            r.Name = "Press it"; r.When = RuleWhen.Hotkey; r.HotkeyVk = vk; r.Macro = "Buy egg";
            return r;
        }

        public static void TestARuleCannotBeOnAKeyAnotherJobHas()
        {
            Host host = new Host();
            host.Owners.Macros.Add(new KeyValuePair<string, byte>("Shop", 0x76));
            WatchKit k = Kit(TempStore(), host);
            k.Store.Macros.Add(Named("Buy egg"));
            using (RuleEditDialog d = new RuleEditDialog(KeyRule(0x76), k))
            {
                Assert.False(d.TrySave(), "F7 plays a macro");
                Assert.Contains("Shop", d.ProblemText, "and it says which");
                d.SetHotkey(0x76, true, false, false);
                Assert.True(d.TrySave(), "with Ctrl it is another key");
                d.SetHotkey(0x7A, false, false, false);
                Assert.True(d.TrySave(), "a free key");
            }
            using (RuleEditDialog d = new RuleEditDialog(KeyRule(0x79), k))
                Assert.Contains("stops macros", d.TrySave() ? "" : d.ProblemText, "the stop key");
        }

        // A rule plays a macro on a Roblox window, so a macro made for the
        // screen would click the wrong places there.
        public static void TestARuleDoesNotOfferMacrosMadeForTheScreen()
        {
            Host host = new Host();
            WatchKit k = Kit(TempStore(), host);
            k.Store.Macros.Add(Named("On a window"));
            Macro screen = Named("On the screen");
            screen.Anywhere = true;
            k.Store.Macros.Add(screen);
            using (RuleEditDialog d = new RuleEditDialog(new Rule(), k))
            {
                d.SetMacro("On the screen");
                d.SetName("x");
                d.SetWhen(RuleWhen.Every);
                Assert.False(d.TrySave(), "not in the list, so none is chosen");
                d.SetMacro("On a window");
                Assert.True(d.TrySave(), "the other one is");
            }
        }

        // ---------- the step windows ----------

        static bool HasButton(Control c, string text)
        {
            foreach (Control child in c.Controls)
            {
                if (child is Button && child.Text == text) return true;
                if (HasButton(child, text)) return true;
            }
            return false;
        }

        // Picking a spot is done on a picture of a Roblox window.
        public static void TestAnywhereStepsHaveNoPickOnTheGame()
        {
            Host host = new Host();
            WatchKit k = Kit(TempStore(), host);
            using (MacroStepDialog d = new MacroStepDialog(MacroEditDialog.NewStep(MacroStepKind.Click), k, false))
                Assert.True(HasButton(d, "Pick it on the game"), "on a Roblox window it is there");
            using (MacroStepDialog d = new MacroStepDialog(MacroEditDialog.NewStep(MacroStepKind.Click), k, true))
                Assert.False(HasButton(d, "Pick it on the game"), "on the screen there is no window to pick on");
            using (MacroStepDialog d = new MacroStepDialog(MacroEditDialog.NewStep(MacroStepKind.Drag), k, true))
                Assert.False(HasButton(d, "Pick both on the game"), "nor for a drag");
        }
    }
}
