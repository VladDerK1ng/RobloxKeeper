using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Macros that stand on their own: played anywhere on the screen, set off
    // by their own key, a number of times at a speed - and Quick Record's
    // recording kept between runs. Nothing here touches the real screen,
    // mouse or keyboard.
    static class StandaloneMacroTests
    {
        // A pretend world that writes down what it was sent. Time only moves
        // when something sleeps; it can be told to ask for a stop after so
        // many sends.
        class World : IMacroWorld
        {
            public readonly List<InputAction> Sent = new List<InputAction>();
            public long Now;
            public int StopAfter = -1;
            public Rectangle Screen = new Rectangle(-1920, 0, 3840, 1080);

            public Rectangle Client { get { return Screen; } }
            public bool InFront() { return true; }
            public void Send(InputAction a) { Sent.Add(a); }
            public void Sleep(int ms) { Now += ms; }
            public long NowMs { get { return Now; } }
            public bool StopAsked { get { return StopAfter >= 0 && Sent.Count >= StopAfter; } }
            public bool GateWanted { get { return false; } }
            public bool OthersWaiting { get { return false; } }
            public PictureLook Look(MacroStep s) { return new PictureLook(); }

            public int Count(InputActionKind kind)
            {
                int n = 0;
                foreach (InputAction a in Sent) if (a.Kind == kind) n++;
                return n;
            }
        }

        static Macro Of(params MacroStep[] steps)
        {
            Macro m = new Macro();
            m.Name = "Farm";
            m.Steps.AddRange(steps);
            return m;
        }

        // ---------- the macro and its file ----------

        public static void TestANewMacroPlaysAsMacrosAlwaysHave()
        {
            Macro m = new Macro();
            Assert.False(m.Anywhere, "on a Roblox window");
            Assert.Equal((byte)0, m.Key, "no key");
            Assert.Equal(MacroKeyMode.Once, m.KeyMode, "a key would play it once");
            Assert.Equal(1, m.Times, "once");
            Assert.Equal(100, m.SpeedPct, "at its own speed");
        }

        public static void TestACopyKeepsWhereAndHowItPlays()
        {
            Macro m = Of(MacroStep.Key(0x45, 50));
            m.Anywhere = true; m.Key = 0x75; m.KeyMode = MacroKeyMode.Hold; m.Times = 0; m.SpeedPct = 200;
            Macro c = m.Copy();
            Assert.True(c.Anywhere, "anywhere");
            Assert.Equal((byte)0x75, c.Key, "the key");
            Assert.Equal(MacroKeyMode.Hold, c.KeyMode, "what the key does");
            Assert.Equal(0, c.Times, "until stopped");
            Assert.Equal(200, c.SpeedPct, "the speed");
        }

        public static void TestEveryNewSettingIsSavedAndReadBack()
        {
            Macro m = Of(MacroStep.Key(0x45, 50), MacroStep.Wait(250));
            m.Anywhere = true; m.Key = 0x75; m.KeyMode = MacroKeyMode.Toggle; m.Times = 5; m.SpeedPct = 400;
            Macro back = WatchStore.DeserializeMacro(WatchStore.SerializeMacro(m));
            Assert.Equal("Farm", back.Name, "the name");
            Assert.Equal(2, back.Steps.Count, "the steps");
            Assert.True(back.Anywhere, "anywhere");
            Assert.Equal((byte)0x75, back.Key, "its key");
            Assert.Equal(MacroKeyMode.Toggle, back.KeyMode, "toggle");
            Assert.Equal(5, back.Times, "five times");
            Assert.Equal(400, back.SpeedPct, "four times as fast");

            m.Anywhere = false; m.Key = 0; m.KeyMode = MacroKeyMode.Hold; m.Times = 0; m.SpeedPct = 25;
            back = WatchStore.DeserializeMacro(WatchStore.SerializeMacro(m));
            Assert.False(back.Anywhere, "on a Roblox window");
            Assert.Equal((byte)0, back.Key, "no key");
            Assert.Equal(MacroKeyMode.Hold, back.KeyMode, "hold");
            Assert.Equal(0, back.Times, "until stopped");
            Assert.Equal(25, back.SpeedPct, "a quarter speed");
        }

        // A line from before this build is only a name and the steps.
        public static void TestALineFromAnOlderBuildPlaysAsItDid()
        {
            Macro m = Of(MacroStep.Key(0x45, 50));
            string full = WatchStore.SerializeMacro(m);
            string[] f = full.Split('\t');
            string old = f[0] + "\t" + f[1];
            Macro back = WatchStore.DeserializeMacro(old);
            Assert.Equal("Farm", back.Name, "the name");
            Assert.Equal(1, back.Steps.Count, "the step");
            Assert.False(back.Anywhere, "on a Roblox window");
            Assert.Equal((byte)0, back.Key, "no key");
            Assert.Equal(MacroKeyMode.Once, back.KeyMode, "once");
            Assert.Equal(1, back.Times, "once");
            Assert.Equal(100, back.SpeedPct, "its own speed");

            // The new settings come after the steps, where an older build
            // never looks: the steps field is unchanged.
            Assert.Equal(f[1], full.Split('\t')[1], "the steps are written as before");
        }

        public static void TestNonsenseInTheNewFieldsIsReadAsTheDefaults()
        {
            Macro back = WatchStore.DeserializeMacro("Farm\t\tx\t999\t7\t-3\t5000");
            Assert.False(back.Anywhere, "not 1");
            Assert.Equal((byte)0, back.Key, "a key that can't be one is no key");
            Assert.Equal(MacroKeyMode.Once, back.KeyMode, "a mode that doesn't exist");
            Assert.Equal(0, back.Times, "below 0 is until stopped");
            Assert.Equal(MacroPlay.MAX_SPEED_PCT, back.SpeedPct, "held to the fastest");
        }

        // The minute is for a client held in front; anywhere has half an hour.
        public static void TestTheMinuteIsOnlyForRobloxWindowMacros()
        {
            Macro m = Of(MacroStep.Wait(5 * 60 * 1000));
            Assert.Contains("minute", m.Problem(), "five minutes on a Roblox window");
            m.Anywhere = true;
            Assert.True(m.Problem() == null, "five minutes anywhere is fine");
            m.Steps[0].Ms = Macro.MAX_ANYWHERE_MS + 1;
            Assert.Contains("half an hour", m.Problem(), "past half an hour is not");
            m.Steps.Insert(0, MacroStep.Repeat(RepeatMode.Forever, 0));
            m.Steps.Add(MacroStep.EndRepeat());
            Assert.True(m.Problem() == null, "one that goes on until stopped has no limit");
        }

        // ---------- getting a play ready ----------

        public static void TestOnceAtItsOwnSpeedIsTheMacroAsItIs()
        {
            Macro m = Of(MacroStep.Key(0x45, 50), MacroStep.Wait(300));
            Macro p = MacroPlay.Prepare(m, 1, 100);
            Assert.False(ReferenceEquals(m, p), "a copy");
            Assert.Equal(2, p.Steps.Count, "nothing added");
            Assert.Equal(300, p.Steps[1].Ms, "the wait as it was");
            Assert.Equal(50, p.Steps[0].HoldMs, "the key as it was");
            p.Steps[1].Ms = 1;
            Assert.Equal(300, m.Steps[1].Ms, "and the macro itself is left alone");
        }

        public static void TestTimesIsARepeatAroundTheWholeMacro()
        {
            Macro m = Of(MacroStep.Key(0x45, 50), MacroStep.Wait(300));
            Macro three = MacroPlay.Prepare(m, 3, 100);
            Assert.Equal(4, three.Steps.Count, "a repeat, the steps, its end");
            Assert.Equal(MacroStepKind.Repeat, three.Steps[0].Kind, "starts with a repeat");
            Assert.Equal(RepeatMode.Times, three.Steps[0].Mode, "of a number of times");
            Assert.Equal(3, three.Steps[0].Count, "three");
            Assert.Equal(MacroStepKind.EndRepeat, three.Steps[3].Kind, "and ends it");
            Assert.False(three.IsSession, "it ends by itself");

            World w = new World();
            MacroRunner r = new MacroRunner(three);
            Assert.Equal(RunEnd.Finished, r.Run(w), "plays to the end");
            Assert.Equal(3, w.Count(InputActionKind.KeyDown), "E three times");
            Assert.Equal(3, r.Rounds, "three rounds");

            Macro forever = MacroPlay.Prepare(m, 0, 100);
            Assert.Equal(RepeatMode.Forever, forever.Steps[0].Mode, "0 is until stopped");
            Assert.True(forever.IsSession, "so it steps aside for the nudge between rounds");
        }

        // The repeat around it can take it past the deepest the editor
        // allows. The player has no such limit, and it is never refused.
        public static void TestAPreparedPlayIsNeverRefusedForDepth()
        {
            Macro m = Of(MacroStep.Repeat(RepeatMode.Times, 2), MacroStep.Repeat(RepeatMode.Times, 2),
                         MacroStep.Repeat(RepeatMode.Times, 2), MacroStep.Key(0x45, 50),
                         MacroStep.EndRepeat(), MacroStep.EndRepeat(), MacroStep.EndRepeat());
            Assert.True(m.Problem() == null, "three deep is allowed");
            Macro p = MacroPlay.Prepare(m, 2, 100);
            Assert.Contains("too many", p.BlockProblem(), "four deep is more than the editor takes");

            World w = new World();
            Assert.Equal(RunEnd.Finished, new MacroRunner(p).Run(w), "and it plays all the same");
            Assert.Equal(16, w.Count(InputActionKind.KeyDown), "2 x 2 x 2, twice");
        }

        public static void TestSpeedChangesTheWaitsAndDrags()
        {
            Macro m = Of(MacroStep.Wait(1000), MacroStep.Drag(0.1, 0.1, 0.9, 0.9, 400, false),
                         MacroStep.Click(0.5, 0.5, false), MacroStep.Wait(1));
            Macro fast = MacroPlay.Prepare(m, 1, 200);
            Assert.Equal(500, fast.Steps[0].Ms, "twice as fast: half the wait");
            Assert.Equal(200, fast.Steps[1].Ms, "and half the drag");
            Assert.Equal(m.Steps[2].HoldMs, fast.Steps[2].HoldMs, "a click is held as long as ever");
            Assert.Equal(1, fast.Steps[3].Ms, "a wait never goes down to nothing");

            Macro slow = MacroPlay.Prepare(m, 1, 50);
            Assert.Equal(2000, slow.Steps[0].Ms, "half speed: twice the wait");
            Assert.Equal(800, slow.Steps[1].Ms, "and twice the drag");

            Assert.Equal(250, MacroPlay.Prepare(m, 1, 4000).Steps[0].Ms, "held to four times as fast");
            Assert.Equal(4000, MacroPlay.Prepare(m, 1, 1).Steps[0].Ms, "held to a quarter speed");
        }

        // Roblox misses a key held for less than 30ms.
        public static void TestAFasterKeyIsNeverHeldTooShortToNotice()
        {
            Macro m = Of(MacroStep.Key(0x45, 400), MacroStep.Key(0x46, 40), MacroStep.Key(0x47, 20));
            Macro fast = MacroPlay.Prepare(m, 1, 400);
            Assert.Equal(100, fast.Steps[0].HoldMs, "a long hold is shorter");
            Assert.Equal(MacroPlay.SHORTEST_HOLD_MS, fast.Steps[1].HoldMs, "but never under 30ms");
            Assert.Equal(20, fast.Steps[2].HoldMs, "one already shorter is left as it was made");
            Assert.Equal(1600, MacroPlay.Prepare(m, 1, 25).Steps[0].HoldMs, "a quarter speed holds it four times as long");
        }

        // Kept to a minute on a Roblox window is one play at its own speed.
        // Several times over, slowed down, or on the whole screen, it can go
        // on far longer - and takes turns with the keyboard so the nudge,
        // which gives up after a minute, still gets in.
        public static void TestALongerPlayTakesTurnsWithTheKeyboard()
        {
            Macro m = Of(MacroStep.Key(0x45, 50));
            Assert.False(MacroPlay.StepsAside(m, 1, 100), "once at its own speed: as it is");
            Assert.False(MacroPlay.StepsAside(m, 1, 200), "faster is shorter");
            Assert.True(MacroPlay.StepsAside(m, 10, 100), "ten times over");
            Assert.True(MacroPlay.StepsAside(m, 0, 100), "until stopped");
            Assert.True(MacroPlay.StepsAside(m, 1, 25), "a quarter speed");
            Assert.True(MacroPlay.StepsAside(m, 1, 1), "slower than allowed is a quarter speed");
            m.Anywhere = true;
            Assert.True(MacroPlay.StepsAside(m, 1, 100), "on the whole screen it can last half an hour");
            Assert.False(MacroPlay.StepsAside(null, 10, 25), "no macro");
        }

        // A watcher, an older rule or a Play button can set off a macro made
        // for the whole screen. On a client its places would land on the
        // wrong spots, so it is played on the screen, under the caller's tag.
        public static void TestAnAnywhereMacroSetOffForAClientPlaysOnTheScreen()
        {
            Macro m = Of(MacroStep.Key(0x45, 50), MacroStep.Wait(400));
            Assert.True(MacroPlay.OnScreenInstead(m, 3) == null, "one for a Roblox window goes to the client");

            m.Anywhere = true;
            m.SpeedPct = 200;
            Macro three = MacroPlay.OnScreenInstead(m, 3);
            Assert.True(three != null, "one for the screen is played there");
            Assert.Equal(RepeatMode.Times, three.Steps[0].Mode, "as many times as asked");
            Assert.Equal(3, three.Steps[0].Count, "three");
            Assert.Equal(200, three.Steps[2].Ms, "at its own speed");
            Assert.Equal(2, MacroPlay.OnScreenInstead(m, 0).Steps.Count, "asked for none, it plays once");
            Assert.Equal(400, m.Steps[1].Ms, "the macro itself is left alone");

            World w = new World();
            Assert.Equal(RunEnd.Finished, new MacroRunner(three).Run(w), "and plays to its end");
            Assert.Equal(3, w.Count(InputActionKind.KeyDown), "E three times");

            Assert.Equal("rule:Every minute", MacroPlay.ScreenTag("rule:Every minute", "Farm"), "a rule's own tag, so stopping the rule stops it");
            Assert.Equal(MacroPlay.PlayTag("Farm"), MacroPlay.ScreenTag(MacroPlay.PlayTag("Farm"), "Farm"), "a Play button's");
            Assert.Equal("screen:Farm", MacroPlay.ScreenTag(null, "Farm"), "a watcher's, by the macro's name");
        }

        // Through the main window: a Play on a client sends it to the screen,
        // once, under that Play's tag, which stops it. Its one step is a wait,
        // so nothing is pressed for real.
        public static void TestTheMainWindowPlaysAnAnywhereMacroOnTheScreen()
        {
            using (MainForm f = new MainForm(false))
            {
                IMacroPlay play = (IMacroPlay)f;
                IMacroHost host = (IMacroHost)f;
                Macro m = Of(MacroStep.Wait(5000));
                m.Name = "Screen only";
                m.Anywhere = true;
                WatchedClient c = new WatchedClient();
                c.Pid = 4242;
                c.Label = "Client 1";
                string tag = MacroPlay.PlayTag(m.Name);

                Assert.Equal(1, play.PlayNow(m, new WatchedClient[] { c, c }), "started once, not once per client");
                Assert.True(host.IsPlaying(tag), "playing under its Play's tag");
                Assert.Equal(0, play.PlayNow(m, new WatchedClient[] { c }), "not started twice");
                Assert.True(host.StopTag(tag), "and that tag stops it");

                System.Diagnostics.Stopwatch t = System.Diagnostics.Stopwatch.StartNew();
                while (host.IsPlaying(tag) && t.ElapsedMilliseconds < 3000) System.Threading.Thread.Sleep(10);
                Assert.False(host.IsPlaying(tag), "at once, not at the end of its wait");
            }
        }

        // ---------- its key ----------

        public static void TestWhatAMacrosKeyDoes()
        {
            // Plays it once: pressing again while it plays does nothing.
            Assert.Equal(MacroKeyAction.Start, MacroPlay.Decide(MacroKeyMode.Once, true, false), "once: starts");
            Assert.Equal(MacroKeyAction.Nothing, MacroPlay.Decide(MacroKeyMode.Once, true, true), "once: already playing");
            Assert.Equal(MacroKeyAction.Nothing, MacroPlay.Decide(MacroKeyMode.Once, false, true), "once: letting go");

            // The same key starts it and stops it.
            Assert.Equal(MacroKeyAction.Start, MacroPlay.Decide(MacroKeyMode.Toggle, true, false), "toggle: starts");
            Assert.Equal(MacroKeyAction.Stop, MacroPlay.Decide(MacroKeyMode.Toggle, true, true), "toggle: stops");
            Assert.Equal(MacroKeyAction.Nothing, MacroPlay.Decide(MacroKeyMode.Toggle, false, true), "toggle: letting go");

            // While it is held.
            Assert.Equal(MacroKeyAction.Start, MacroPlay.Decide(MacroKeyMode.Hold, true, false), "hold: starts");
            Assert.Equal(MacroKeyAction.Stop, MacroPlay.Decide(MacroKeyMode.Hold, false, true), "hold: let go stops it");
            Assert.Equal(MacroKeyAction.Nothing, MacroPlay.Decide(MacroKeyMode.Hold, false, false), "hold: let go with nothing playing");
            // Its up was lost, so it is still held: nothing new starts.
            Assert.Equal(MacroKeyAction.Nothing, MacroPlay.Decide(MacroKeyMode.Hold, true, true), "hold: a second down");
        }

        public static void TestOnlyOnceKeysPlayItTheTimesItIsSetTo()
        {
            Macro m = Of(MacroStep.Key(0x45, 50));
            m.Times = 3;
            Assert.Equal(3, MacroPlay.TimesFor(m), "once: three times");
            m.KeyMode = MacroKeyMode.Toggle;
            Assert.Equal(0, MacroPlay.TimesFor(m), "toggle: until the key again");
            m.KeyMode = MacroKeyMode.Hold;
            Assert.Equal(0, MacroPlay.TimesFor(m), "hold: until it is let go");
        }

        public static void TestEachPlayHasATagOfItsOwn()
        {
            Assert.Equal("key:Farm", MacroPlay.KeyTag("Farm"), "started by its key");
            Assert.Equal("play:Farm", MacroPlay.PlayTag("Farm"), "started by a Play button");
        }

        // ---------- playing anywhere ----------

        // Nothing but stopping ends one that goes on until stopped.
        public static void TestStoppingEndsAPlayThatGoesOnForever()
        {
            Macro p = MacroPlay.Prepare(Of(MacroStep.Key(0x45, 50), MacroStep.Wait(100)), 0, 100);
            World w = new World();
            w.StopAfter = 40;
            Assert.Equal(RunEnd.Cancelled, new MacroRunner(p).Run(w), "stopped");
            Assert.True(w.Count(InputActionKind.KeyDown) >= 10, "after going round many times");
            Assert.Equal(w.Count(InputActionKind.KeyDown), w.Count(InputActionKind.KeyUp), "nothing left held down");
        }

        public static void TestTheScreenIsAlwaysInFront()
        {
            Rectangle all = new Rectangle(-1920, -200, 4480, 1440);
            ScreenWorld w = new ScreenWorld(new PlayOptions(), MacroStop.Epoch, all);
            Assert.True(w.InFront(), "no window coming to the front stops it");
            Assert.Equal(all, w.Client, "its client is every monitor together");
            Assert.False(w.StopAsked, "nothing asked it to stop");
        }

        // Its own stop and the stop key both end it - the stop key even when
        // pressed while it had stepped aside for the nudge, since one world
        // is used for the whole play.
        public static void TestTheStopKeyAndItsOwnStopBothEndAScreenPlay()
        {
            bool off = false;
            PlayOptions o = new PlayOptions();
            o.Stop = delegate { return off; };
            ScreenWorld w = new ScreenWorld(o, MacroStop.Epoch, new Rectangle(0, 0, 1920, 1080));
            Assert.False(w.StopAsked, "playing");
            off = true;
            Assert.True(w.StopAsked, "switched off");

            off = false;
            ScreenWorld before = new ScreenWorld(o, MacroStop.Epoch, new Rectangle(0, 0, 1920, 1080));
            MacroStop.All();
            Assert.True(before.StopAsked, "the stop key, pressed after it began");

            // A stop that has come in already ends it before anything is sent.
            o.Stop = delegate { return true; };
            Assert.Equal(MacroPlayer.CANCELLED, MacroPlayer.PlayOnScreen(Of(MacroStep.Key(0x45, 50)), o), "stopped at once");
        }

        public static void TestAStoppedScreenPlayWakesFromALongWait()
        {
            PlayOptions o = new PlayOptions();
            o.Stop = delegate { return true; };
            ScreenWorld w = new ScreenWorld(o, MacroStop.Epoch, new Rectangle(0, 0, 1920, 1080));
            System.Diagnostics.Stopwatch t = System.Diagnostics.Stopwatch.StartNew();
            w.Sleep(10000);
            Assert.True(t.ElapsedMilliseconds < 2000, "a ten-second wait ends at once");
        }

        // A macro made for the whole screen clicks where it was recorded, a
        // monitor to the left of the main one included.
        public static void TestAPlaceOnTheScreenLandsLeftOfTheMainMonitor()
        {
            World w = new World();
            Macro m = Of(MacroStep.Click(0.25, 0.5, false));
            new MacroRunner(m).Run(w);
            InputAction move = w.Sent[0];
            Assert.Equal(InputActionKind.MoveTo, move.Kind, "goes there");
            Assert.True(move.X < 0, "on the left monitor, below zero");
            Assert.Equal(-1920 + (int)Math.Round(0.25 * 3839), move.X, "a quarter across both");
        }

        // ---------- recording anywhere ----------

        public static void TestRecordingTheScreenLeavesOutThisAppsOwnWindows()
        {
            IntPtr client = new IntPtr(42), other = new IntPtr(7);
            const int me = 100, them = 200;
            Assert.True(MacroRecorder.Keeps(false, client, client, 0, 0, me), "one client: it is in front");
            Assert.False(MacroRecorder.Keeps(false, client, other, them, them, me), "one client: something else is");

            Assert.True(MacroRecorder.Keeps(true, IntPtr.Zero, other, them, them, me), "the screen: any other program");
            Assert.True(MacroRecorder.Keeps(true, IntPtr.Zero, other, them, 0, me), "with nothing known under the pointer");
            Assert.False(MacroRecorder.Keeps(true, IntPtr.Zero, other, me, them, me), "not while this app is in front");
            // The click on Stop arrives while the window before is still in
            // front: it is told by the window under the pointer.
            Assert.False(MacroRecorder.Keeps(true, IntPtr.Zero, other, them, me, me), "not a click on this app's window");
        }

        public static void TestAScreenRecordingIsFractionsOfEveryMonitor()
        {
            Rectangle all = new Rectangle(-1920, 0, 3840, 1080);
            List<RawInput> events = new List<RawInput>();
            events.Add(RawInput.Button(true, false, -1920, 0, 0));
            events.Add(RawInput.Button(false, false, -1920, 0, 50));
            events.Add(RawInput.Button(true, false, 1919, 1079, 1000));
            events.Add(RawInput.Button(false, false, 1919, 1079, 1050));
            events.Add(RawInput.Button(true, true, -961, 540, 2000));
            events.Add(RawInput.Button(false, true, -961, 540, 2050));
            List<MacroStep> steps = MacroRecording.ToSteps(events, all, 3000);

            List<MacroStep> clicks = new List<MacroStep>();
            foreach (MacroStep s in steps) if (s.Kind == MacroStepKind.Click) clicks.Add(s);
            Assert.Equal(3, clicks.Count, "three clicks");
            Assert.Equal(0.0, clicks[0].X, "the far left edge");
            Assert.Equal(0.0, clicks[0].Y, "the top");
            Assert.Equal(1.0, clicks[1].X, "the far right edge");
            Assert.Equal(1.0, clicks[1].Y, "the bottom");
            Assert.True(Math.Abs(clicks[2].X - 0.25) < 0.001, "a quarter across, on the left monitor");
            Assert.True(clicks[2].RightButton, "the right button");

            // And played back, they land where they were made.
            World w = new World();
            w.Screen = all;
            new MacroRunner(Of(clicks[2])).Run(w);
            Assert.Equal(-961, w.Sent[0].X, "back on the left monitor");
            Assert.Equal(540, w.Sent[0].Y, "at the same height");
        }

        // ---------- one check for every key ----------

        static KeyOwners Owners()
        {
            KeyOwners k = new KeyOwners();
            k.StopKey = 0x79;           // F10
            k.ClickerKey = 0x75;        // F6
            k.ShotKey = 0x7A;           // F11
            k.RuleKeys.Add(new KeyValuePair<string, byte>("Rebirth", 0x71));         // F2
            k.Macros.Add(new KeyValuePair<string, byte>("Farm", 0x72));              // F3
            return k;
        }

        public static void TestNoTwoFeaturesShareAKey()
        {
            KeyOwners k = Owners();
            Assert.Contains("Choose a key", KeyUse.Problem(0, KeyUse.MACRO, null, k), "no key");
            Assert.Contains("recording", KeyUse.Problem(MacroRecording.STOP_KEY, KeyUse.MACRO, null, k), "F8");
            Assert.Contains("stops macros", KeyUse.Problem(0x79, KeyUse.MACRO, null, k), "the stop key");
            Assert.Contains("autoclicker", KeyUse.Problem(0x75, KeyUse.MACRO, null, k), "the autoclicker's");
            Assert.Contains("screenshots", KeyUse.Problem(0x7A, KeyUse.MACRO, null, k), "the screenshot key");
            Assert.Contains("Rebirth", KeyUse.Problem(0x71, KeyUse.MACRO, null, k), "a rule's");
            Assert.Contains("Farm", KeyUse.Problem(0x72, KeyUse.MACRO, null, k), "another macro's");
            Assert.True(KeyUse.Problem(0x78, KeyUse.MACRO, null, k) == null, "F9 is free");
        }

        public static void TestAKeyIsNoClashWithItsOwnJob()
        {
            KeyOwners k = Owners();
            Assert.True(KeyUse.Problem(0x79, KeyUse.STOP, null, k) == null, "the stop key chosen again");
            Assert.True(KeyUse.Problem(0x75, KeyUse.CLICKER, null, k) == null, "the autoclicker's chosen again");
            Assert.True(KeyUse.Problem(0x7A, KeyUse.SHOT, null, k) == null, "the screenshot key chosen again");
            Assert.True(KeyUse.Problem(0x71, KeyUse.RULE, null, k) == null, "two rules may share a key");
            Assert.True(KeyUse.Problem(0x72, KeyUse.MACRO, "farm", k) == null, "the macro being edited keeps its own key");
            Assert.Contains("Farm", KeyUse.Problem(0x72, KeyUse.MACRO, "Other", k), "but another macro may not take it");
            Assert.Contains("Farm", KeyUse.Problem(0x72, KeyUse.STOP, "Farm", k), "and nothing else may either");
        }

        // The hole there was: the screenshot key or the autoclicker's could
        // be put on a rule's key without a word.
        public static void TestTheScreenshotAndAutoclickerKeysCantTakeARulesKey()
        {
            KeyOwners k = Owners();
            Assert.Contains("Rebirth", KeyUse.Problem(0x71, KeyUse.SHOT, null, k), "the screenshot key");
            Assert.Contains("Rebirth", KeyUse.Problem(0x71, KeyUse.CLICKER, null, k), "the autoclicker's");
            Assert.Contains("Rebirth", KeyUse.Problem(0x71, KeyUse.STOP, null, k), "the stop key");
            Assert.Contains("Farm", KeyUse.Problem(0x72, KeyUse.RULE, null, k), "and a rule can't take a macro's");
        }

        public static void TestAnAutoclickerNotSetUpHasNoKey()
        {
            KeyOwners k = Owners();
            k.ClickerKey = 0;
            k.ShotKey = 0;
            Assert.True(KeyUse.Problem(0x75, KeyUse.MACRO, null, k) == null, "F6 is free while it is not set up");
            Assert.True(KeyUse.Problem(0x7A, KeyUse.MACRO, null, k) == null, "and F11 with no screenshot key");
        }

        // ---------- Quick Record's recording ----------

        static string TempQuick()
        {
            return Path.Combine(Path.GetTempPath(), "rk-quick-" + Guid.NewGuid().ToString("N"), "quick-record.dat");
        }

        public static void TestQuickRecordsRecordingIsKeptBetweenRuns()
        {
            string path = TempQuick();
            try
            {
                Assert.True(MacroPlay.LoadQuick(path) == null, "none yet");
                Macro m = Of(MacroStep.Click(0.25, 0.5, false), MacroStep.Wait(400));
                m.Name = MacroPlay.QUICK_NAME;
                m.Anywhere = true; m.Times = 3; m.SpeedPct = 200;
                Assert.True(MacroPlay.SaveQuick(path, m), "kept");
                Macro back = MacroPlay.LoadQuick(path);
                Assert.Equal(MacroPlay.QUICK_NAME, back.Name, "its name");
                Assert.Equal(2, back.Steps.Count, "its steps");
                Assert.True(back.Anywhere, "anywhere");
                Assert.Equal(3, back.Times, "three times");
                Assert.Equal(200, back.SpeedPct, "twice as fast");

                Assert.True(MacroPlay.SaveQuick(path, null), "forgotten");
                Assert.True(MacroPlay.LoadQuick(path) == null, "none again");
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(path), true); } catch { } }
        }

        public static void TestABadQuickRecordFileIsNoRecording()
        {
            string path = TempQuick();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "");
                Assert.True(MacroPlay.LoadQuick(path) == null, "empty");
                File.WriteAllText(path, "garbage with no steps");
                Assert.True(MacroPlay.LoadQuick(path) == null, "nonsense");

                // Whatever the file says, a recording of the screen plays on it.
                Macro m = Of(MacroStep.Wait(400));
                File.WriteAllText(path, WatchStore.SerializeMacro(m));
                Assert.True(MacroPlay.LoadQuick(path).Anywhere, "anywhere");
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(path), true); } catch { } }
        }

        public static void TestQuickRecordIsKeptBesideTheWatchers()
        {
            Assert.Equal(Path.GetDirectoryName(WatchStore.DefaultPath), Path.GetDirectoryName(MacroPlay.QuickPath), "the same folder");
            Assert.Equal("quick-record.dat", Path.GetFileName(MacroPlay.QuickPath), "its own file");
        }
    }
}
