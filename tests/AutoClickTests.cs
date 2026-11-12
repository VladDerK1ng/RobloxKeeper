using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RobloxKeeper.Tests
{
    // The autoclicker, and the rest of what 1.4 added around macros: drags
    // and the wheel kept by the recorder, a watcher that clicks what it
    // found, your own idle time while a session presses keys, and the new
    // windows.
    static class AutoClickTests
    {
        static readonly Rectangle Window = new Rectangle(100, 50, 800, 600);

        static WatchedClient Client(int pid, string account)
        {
            WatchedClient c = new WatchedClient();
            c.Pid = pid;
            c.Label = "Client " + pid;
            c.AccountName = account;
            return c;
        }

        static readonly WatchedClient[] Three = { Client(1, "main"), Client(2, "alt1"), Client(3, "alt2") };

        // ---------- the autoclicker ----------

        public static void TestAnAutoclickerIsAClickAndAWaitUntilStopped()
        {
            AutoClick c = new AutoClick();
            c.EveryMs = 100;
            Macro m = c.ToMacro();
            Assert.True(m.IsSession, "it goes on until stopped");
            Assert.Equal(null, m.Problem(), "and is a macro that may play");
            Assert.Equal("Repeat until stopped: | Click where the pointer is | Wait 0.05s | End of repeat", Said(m), "its steps");
        }

        // The gap is from click to click, so the time the button is down
        // comes out of the wait.
        public static void TestTheGapIsFromOneClickToTheNext()
        {
            AutoClick c = new AutoClick();
            c.EveryMs = 1000;
            int ms = 0;
            List<InputAction> round = MacroPlan.For(Strip(c.ToMacro()), Window);
            foreach (InputAction a in round) if (a.Kind == InputActionKind.Sleep) ms += a.Ms;
            Assert.Equal(1000, ms, "one round is the gap asked for");
        }

        public static void TestAFastClickIsHeldForHalfItsGap()
        {
            Assert.Equal(10, AutoClick.HoldFor(20), "as fast as it goes: down 10ms, up 10ms");
            Assert.Equal(25, AutoClick.HoldFor(50), "half");
            Assert.Equal(MacroStep.CLICK_MS, AutoClick.HoldFor(5000), "never longer than a macro's own click");
        }

        public static void TestAnAutoclickerAtThePointerNeverMovesIt()
        {
            foreach (InputAction a in MacroPlan.For(Strip(new AutoClick().ToMacro()), Window))
                Assert.NotEqual(InputActionKind.MoveTo, a.Kind, "you aim, it clicks");
        }

        public static void TestAnAutoclickerAtAPlaceGoesThere()
        {
            AutoClick c = new AutoClick();
            c.Where = ClickWhere.Place; c.X = 0.5; c.Y = 0.25;
            List<InputAction> plan = MacroPlan.For(Strip(c.ToMacro()), Window);
            Assert.Equal(InputActionKind.MoveTo, plan[0].Kind, "moves there");
            Assert.Equal(500, plan[0].X, "across");
            Assert.Equal(200, plan[0].Y, "down");
        }

        public static void TestAPictureAutoclickerLooksAsCloselyAsItIsTold()
        {
            AutoClick c = new AutoClick();
            c.Where = ClickWhere.Picture; c.Picture = "egg.png"; c.Tolerance = 0.93;
            Assert.Equal(0.93, c.ToMacro().Steps[1].Tolerance, "how alike, from its own window");
        }

        // F6 belongs to every other program until the autoclicker has been
        // set up: nobody who never opened it should lose a key to it.
        public static void TestTheAutoclickerIsOffUntilItIsSetUp()
        {
            Assert.False(new AutoClick().Enabled, "off to start with");
            Assert.False(AppSettings.Parse(new string[] { "afk=1" }).Clicker.Enabled, "and for settings saved before it existed");

            Host host = new Host();
            using (AutoClickDialog d = new AutoClickDialog(Kit(host)))
                Assert.True(d.TrySave(), "saved from its window");
            Assert.True(host.Clicker.Enabled, "on from then");
            Assert.True(AppSettings.Parse(Lines(host.Clicker)).Clicker.Enabled, "and it stays on");
        }

        static string[] Lines(AutoClick c)
        {
            List<string> lines = new List<string>();
            c.Write(lines);
            return lines.ToArray();
        }

        // A quarter of the window is searched in a quarter of the time.
        public static void TestAPictureStepCanLookInABoxDrawnForTheGame()
        {
            WatchKit k = Kit(new Host());
            WatchRegion shop = new WatchRegion();
            shop.Name = "Shop";
            shop.PlaceId = "1";
            k.Store.Regions.Add(shop);

            MacroStep step = MacroStep.ClickPicture("buy.png", null, 0);
            using (MacroStepDialog d = new MacroStepDialog(step, k))
            {
                Assert.True(d.TryAccept(), "saves as shown");
                Assert.Equal(null, d.Result.Box, "the whole window unless a box is chosen");
            }
            step.Box = "Shop";
            using (MacroStepDialog d = new MacroStepDialog(step, k))
            {
                Assert.True(d.TryAccept(), "saves");
                Assert.Equal("Shop", d.Result.Box, "the box it was set to is kept");
            }
            step.Box = "Gone";
            using (MacroStepDialog d = new MacroStepDialog(step, k))
            {
                Assert.True(d.TryAccept(), "saves");
                Assert.Equal("Gone", d.Result.Box, "even one that is not in the list any more - opening a step must not change it");
            }
        }

        public static void TestTheAutoclickersBoxIsSavedAndUsed()
        {
            AutoClick c = new AutoClick();
            c.Where = ClickWhere.Picture; c.Picture = "egg.png"; c.Box = "Shop";
            Assert.Equal("Shop", c.ToMacro().Steps[1].Box, "its click looks in the box");
            Assert.Equal("Shop", AppSettings.Parse(Lines(c)).Clicker.Box, "and it comes back with the settings");
        }

        public static void TestANewPictureNeedsAName()
        {
            using (PictureNameDialog d = new PictureNameDialog())
            {
                Assert.False(d.Accept("  "), "nothing typed");
                Assert.False(d.Accept("!!!"), "nothing a file could be called");
                Assert.True(d.Accept(" Buy button "), "a name");
                Assert.Equal("Buy button", d.Chosen, "tidied");
            }
        }

        public static void TestTooFastIsRefused()
        {
            AutoClick c = new AutoClick();
            c.EveryMs = 5;
            Assert.Contains("as fast as it can click", c.Problem(), "faster than it can go");
        }

        // The pointer is in one window; clients taking turns can't share it.
        public static void TestTakingTurnsNeedsAPlaceOrAPicture()
        {
            AutoClick c = new AutoClick();
            c.On = ClickOn.TakeTurns;
            Assert.Contains("place or a picture", c.Problem(), "at the pointer makes no sense in turns");
            c.Where = ClickWhere.Place;
            Assert.Equal(null, c.Problem(), "a place does");
        }

        public static void TestInFrontMeansTheClientInFrontAndNoOther()
        {
            AutoClick c = new AutoClick();
            Assert.Equal(1, c.Targets(Three, 2).Count, "one client");
            Assert.Equal(2, c.Targets(Three, 2)[0].Pid, "the one in front");
            Assert.Equal(0, c.Targets(Three, 99).Count, "none when you are somewhere else - never just the first");
        }

        public static void TestTakingTurnsIsEveryClientOrTheOnesTicked()
        {
            AutoClick c = new AutoClick();
            c.On = ClickOn.TakeTurns;
            Assert.Equal(3, c.Targets(Three, 0).Count, "nobody ticked: every client");
            c.Accounts = new string[] { "ALT1", "alt2" };
            Assert.Equal(2, c.Targets(Three, 0).Count, "the two ticked, however the name is cased");
        }

        public static void TestTheAutoclickerIsSavedWithTheSettings()
        {
            AppSettings s = new AppSettings();
            s.StopKey = 0x7A;
            s.Clicker.Right = true; s.Clicker.EveryMs = 250; s.Clicker.Where = ClickWhere.Picture;
            s.Clicker.Picture = "egg.png"; s.Clicker.Mask = "egg-mask.png";
            s.Clicker.On = ClickOn.TakeTurns; s.Clicker.Accounts = new string[] { "alt1", "alt2" };
            s.Clicker.TurnSeconds = 9; s.Clicker.ToggleVk = 0x76; s.Clicker.X = 0.25; s.Clicker.Y = 0.75;

            AppSettings back = AppSettings.Parse(s.ToLines());
            Assert.Equal((byte)0x7A, back.StopKey, "the stop key");
            Assert.Equal(s.Clicker.Describe(), back.Clicker.Describe(), "what it does");
            Assert.Equal("egg-mask.png", back.Clicker.Mask, "the painted-out copy");
            Assert.Equal(9, back.Clicker.TurnSeconds, "the turn");
            Assert.Equal((byte)0x76, back.Clicker.ToggleVk, "its key");
            Assert.Equal(0.25, back.Clicker.X, "across");
        }

        public static void TestSettingsFromBeforeTheAutoclickerLoad()
        {
            AppSettings s = AppSettings.Parse(new string[] { "afk=1", "interval=12" });
            Assert.Equal((byte)0x79, s.StopKey, "F10 stops macros");
            Assert.Equal((byte)0x75, s.Clicker.ToggleVk, "F6 switches the autoclicker");
            Assert.Equal(12, s.IntervalMinutes, "and the rest is read as before");
        }

        public static void TestAHandEditedAutoclickerIsHeldToWhatTheWindowCanShow()
        {
            AppSettings s = AppSettings.Parse(new string[] { "click_every=1", "click_turn=0", "click_x=7" });
            Assert.Equal(AutoClick.MIN_EVERY_MS, s.Clicker.EveryMs, "no faster than allowed");
            Assert.Equal(1, s.Clicker.TurnSeconds, "a turn of at least a second");
            Assert.Equal(1.0, s.Clicker.X, "inside the window");
        }

        // ---------- recording ----------

        static string Said(Macro m) { return Said(m.Steps); }

        static string Said(IList<MacroStep> steps)
        {
            List<string> s = new List<string>();
            foreach (MacroStep m in steps) s.Add(m.Describe());
            return string.Join(" | ", s.ToArray());
        }

        // Without the repeat around it: one round, to add up.
        static Macro Strip(Macro m)
        {
            Macro one = new Macro();
            foreach (MacroStep s in m.Steps)
                if (s.Kind != MacroStepKind.Repeat && s.Kind != MacroStepKind.EndRepeat) one.Steps.Add(s);
            return one;
        }

        static List<MacroStep> Steps(long endMs, params RawInput[] events)
        {
            return MacroRecording.ToSteps(events, Window, endMs);
        }

        public static void TestAButtonLetGoWhereItWentDownIsAClick()
        {
            Assert.Equal("Click 50% across, 50% down",
                Said(Steps(1000, RawInput.Button(true, false, 500, 350, 0), RawInput.Button(false, false, 502, 351, 80))),
                "an unsteady hand is still a click");
        }

        public static void TestAButtonLetGoSomewhereElseIsADrag()
        {
            List<MacroStep> steps = Steps(2000, RawInput.Button(true, true, 300, 350, 0), RawInput.Button(false, true, 700, 350, 600));
            Assert.Equal(1, steps.Count, "one step");
            Assert.Equal(MacroStepKind.Drag, steps[0].Kind, "a drag");
            Assert.True(steps[0].RightButton, "with the button that was held");
            Assert.Equal(600, steps[0].Ms, "as long as it took");
            Assert.Equal("Right-drag 25%, 50% to 75%, 50% over 0.6s", steps[0].Describe(), "from where to where");
        }

        // The wait after a drag is counted from when the button came up.
        public static void TestTheWaitAfterADragStartsWhenItEnds()
        {
            Assert.Equal("Drag 25%, 50% to 75%, 50% over 0.6s | Wait 1s | Press E",
                Said(Steps(3000, RawInput.Button(true, false, 300, 350, 0), RawInput.Button(false, false, 700, 350, 600),
                           RawInput.Key(true, 0x45, 1600), RawInput.Key(false, 0x45, 1650))), "a drag, a second, a key");
        }

        public static void TestWheelClicksCloseTogetherAreOneScroll()
        {
            Assert.Equal("Scroll up 3",
                Said(Steps(1000, RawInput.Scroll(120, 500, 350, 0), RawInput.Scroll(120, 500, 350, 60), RawInput.Scroll(120, 500, 350, 130))),
                "three clicks of the wheel");
            Assert.Equal("Scroll up 1 | Wait 0.06s | Scroll down 2",
                Said(Steps(1000, RawInput.Scroll(120, 500, 350, 0), RawInput.Scroll(-240, 500, 350, 60))), "the other way is its own");
        }

        // A mouse reports hundreds of moves a second. Kept as a place now and
        // then, a minute of it is still a list someone can read.
        public static void TestPointerMovementIsThinnedToAFewPlaces()
        {
            List<RawInput> events = new List<RawInput>();
            for (int i = 0; i < 1000; i++) events.Add(RawInput.Move(200 + i / 2, 300, i));     // a second, 500px
            List<MacroStep> steps = MacroRecording.ToSteps(events, Window, 1000);
            int moves = 0;
            foreach (MacroStep s in steps) if (s.Kind == MacroStepKind.Move) moves++;
            Assert.True(moves >= 5 && moves <= 12, "about ten places for a second of movement, not a thousand: " + moves);
        }

        public static void TestMovementWhileAButtonIsDownBelongsToTheDrag()
        {
            List<MacroStep> steps = Steps(2000, RawInput.Button(true, false, 300, 350, 0), RawInput.Move(400, 350, 200),
                                          RawInput.Move(600, 350, 400), RawInput.Button(false, false, 700, 350, 600));
            Assert.Equal(1, steps.Count, "the drag alone");
        }

        // ---------- a watcher that clicks what it found ----------

        public static void TestAWatcherCanClickThePictureItFound()
        {
            Watcher w = Watcher.Default("Buy button", WatchKind.ImageFound);
            w.TemplateFile = "Buy-button-20261110-213000.png";
            w.MaskFile = "Buy-button-mask.png";
            w.Tolerance = 0.9;
            w.RegionName = "Shop";
            w.ThenMacro = Watcher.CLICK_IT;

            Assert.True(w.ThenClicks, "set to click");
            Macro m = w.ClickMacro();
            Assert.Equal(1, m.Steps.Count, "one step");
            MacroStep s = m.Steps[0];
            Assert.Equal(MacroStepKind.ClickPicture, s.Kind, "a picture click");
            Assert.Equal(w.TemplateFile, s.Picture, "of its own picture");
            Assert.Equal(w.MaskFile, s.Mask, "with its background painted out");
            Assert.Equal(0.9, s.Tolerance, "as alike as the watcher asks");
            Assert.Equal("Shop", s.Box, "in the box it watches");
            Assert.Equal(null, m.Problem(), "and it may play");
        }

        public static void TestClickingWhatItFoundSurvivesSaving()
        {
            Watcher w = Watcher.Default("Buy button", WatchKind.ImageFound);
            w.TemplateFile = "buy.png";
            w.ThenMacro = Watcher.CLICK_IT;
            Watcher back = WatchStore.DeserializeWatcher(WatchStore.SerializeWatcher(w));
            Assert.True(back.ThenClicks, "still set to click");
        }

        public static void TestOnlyAPictureWatcherCanClickWhatItFound()
        {
            Watcher w = Watcher.Default("Spawned", WatchKind.TextAppears);
            w.Rule.Words = new string[] { "spawned" };
            w.ThenMacro = Watcher.CLICK_IT;
            Assert.Contains("picture", WatcherForm.Problem(w), "a word has no place to click");
        }

        // ---------- your own idle time ----------

        public static void TestIdleTimeIsRightAcrossTheTickCountsWrap()
        {
            Assert.Equal(5000.0, RealInput.IdleFor(1000, 6000).TotalMilliseconds, "five seconds");
            Assert.Equal(200.0, RealInput.IdleFor(int.MaxValue - 99, int.MinValue + 100).TotalMilliseconds, "over the wrap");
        }

        // ---------- the windows ----------

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
            byte stopKey = 0x79;
            public int Saved;
            public AutoClick Clicker { get { return clicker; } }
            public void SaveClicker() { Saved++; }
            public bool ClickerOn { get { return false; } }
            public string ToggleClicker() { return null; }
            public byte StopKey { get { return stopKey; } set { stopKey = value; } }
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
            k.Store = new WatchStore(Path.Combine(Path.GetTempPath(), "rk-click-" + Guid.NewGuid().ToString("N") + ".dat"));
            k.Capture = new NoCapture();
            k.Reader = new NoReader();
            k.Engine = new WatchEngine(k.Capture, k.Reader);
            k.Clients = delegate { return new WatchedClient[0]; };
            k.Accounts = delegate { return (IList<string>)new List<string>(new string[] { "main", "alt1" }); };
            k.Log = delegate(string s) { };
            k.Macros = host;
            return k;
        }

        public static void TestEveryKindOfStepHasAWindow()
        {
            MacroStepKind[] kinds = { MacroStepKind.Key, MacroStepKind.Click, MacroStepKind.Wait, MacroStepKind.Type,
                                      MacroStepKind.ClickPicture, MacroStepKind.WaitPicture, MacroStepKind.Move,
                                      MacroStepKind.Drag, MacroStepKind.Scroll, MacroStepKind.Repeat };
            WatchKit k = Kit(new Host());
            foreach (MacroStepKind kind in kinds)
                using (MacroStepDialog d = new MacroStepDialog(MacroEditDialog.NewStep(kind), k))
                    Assert.True(d.ClientSize.Height > 200, kind + " builds");
        }

        public static void TestAPictureStepWontSaveWithoutAPicture()
        {
            using (MacroStepDialog d = new MacroStepDialog(MacroEditDialog.NewStep(MacroStepKind.ClickPicture), Kit(new Host())))
            {
                Assert.False(d.TryAccept(), "nothing chosen");
                Assert.Contains("picture", d.ProblemText, "and it says so");
            }
        }

        public static void TestTheStepWindowsDefaultsSave()
        {
            WatchKit k = Kit(new Host());
            using (MacroStepDialog d = new MacroStepDialog(MacroEditDialog.NewStep(MacroStepKind.Drag), k))
            {
                Assert.True(d.TryAccept(), "a drag as first shown");
                Assert.Equal("Right-drag 40%, 50% to 60%, 50% over 0.5s", d.Result.Describe(), "a small turn of the camera");
            }
            using (MacroStepDialog d = new MacroStepDialog(MacroEditDialog.NewStep(MacroStepKind.Repeat), k))
            {
                Assert.True(d.TryAccept(), "a repeat as first shown");
                Assert.Equal("Repeat 5 times:", d.Result.Describe(), "five times");
            }
            using (MacroStepDialog d = new MacroStepDialog(MacroEditDialog.NewStep(MacroStepKind.Scroll), k))
            {
                Assert.True(d.TryAccept(), "a scroll as first shown");
                Assert.Equal("Scroll up 3", d.Result.Describe(), "three clicks up");
            }
        }

        // "All of this, over again" is what a repeat is nearly always for.
        public static void TestAddingARepeatGoesRoundTheStepsAlreadyThere()
        {
            using (MacroEditDialog d = new MacroEditDialog(new Macro(), Kit(new Host())))
            {
                d.AddStep(MacroStep.Key(0x45, 50));
                d.AddStep(MacroStep.Wait(1000));
                d.AddRepeat(MacroStep.Repeat(RepeatMode.Forever, 0));
                Assert.Equal("Repeat until stopped: | Press E | Wait 1s | End of repeat", d.StepsSaid, "wrapped");
                Assert.True(d.TrySave("Farm"), "and it saves");
                Assert.True(d.Result.IsSession, "as one that goes on until stopped");
            }
        }

        // Something to start from, for someone who has never made a macro.
        public static void TestEveryExampleIsAMacroThatCanBeOpened()
        {
            for (int i = 0; i < MacroForm.ExampleNames.Length; i++)
            {
                Macro m = MacroForm.Example(i);
                Assert.Equal(MacroForm.ExampleNames[i], m.Name, "called what the menu calls it");
                Assert.True(m.IsSession, m.Name + " goes on until stopped");
                Assert.Equal(null, m.BlockProblem(), m.Name + " has its repeat closed");
                using (MacroEditDialog d = new MacroEditDialog(m, Kit(new Host())))
                    Assert.Equal(m.Steps.Count, d.StepCount, m.Name + " opens in the editor");
            }
            Assert.Equal(null, MacroForm.Example(0).Problem(), "jumping can be saved as it is");
            Assert.Contains("no picture yet", MacroForm.Example(2).Problem(), "the picture one says what it still needs");
        }

        public static void TestTheAutoclickerWindowSavesWhatItShows()
        {
            Host host = new Host();
            host.Clicker.EveryMs = 250;
            using (AutoClickDialog d = new AutoClickDialog(Kit(host)))
            {
                AutoClick c = d.Collect();
                Assert.True(c != null, "as first shown it is fine: " + d.ProblemText);
                Assert.Equal(250, c.EveryMs, "the gap it was opened with");
                Assert.True(d.TrySave(), "saves");
                Assert.Equal(1, host.Saved, "through the main window");
            }
        }

        // The key that stops everything can't also be the one that starts it.
        public static void TestTheAutoclickersKeyCantBeTheStopKey()
        {
            Host host = new Host();
            host.Clicker.ToggleVk = 0x79;
            using (AutoClickDialog d = new AutoClickDialog(Kit(host)))
            {
                Assert.True(d.Collect() == null, "refused");
                Assert.Contains("stops macros", d.ProblemText, "and it says why");
            }
        }

        public static void TestTheMacrosWindowOffersTheAutoclickerOnlyWithAMainWindow()
        {
            WatchKit with = Kit(new Host());
            using (MacrosDialog d = new MacrosDialog(with))
                Assert.True(HasButton(d, "Autoclicker"), "there with a main window behind it");
            with.Macros = null;
            using (MacrosDialog d = new MacrosDialog(with))
                Assert.False(HasButton(d, "Autoclicker"), "and not without");
        }

        static bool HasButton(System.Windows.Forms.Control parent, string text)
        {
            foreach (System.Windows.Forms.Control c in parent.Controls)
                if ((c is System.Windows.Forms.Button && c.Text == text) || HasButton(c, text)) return true;
            return false;
        }
    }
}
