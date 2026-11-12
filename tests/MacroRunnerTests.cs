using System;
using System.Collections.Generic;
using System.Drawing;

namespace RobloxKeeper.Tests
{
    // A macro played a step at a time: repeats, steps that go by a picture,
    // stopping, and stepping aside - all against a pretend client, so none of
    // it needs a game.
    static class MacroRunnerTests
    {
        // A client that does what the test says and writes down what it was
        // sent. Time only moves when something sleeps.
        class World : IMacroWorld
        {
            public readonly List<InputAction> Sent = new List<InputAction>();
            public long Now;
            public int Looks;
            public Func<int, bool> Seen = delegate { return false; };      // by how many looks so far
            public Point SeenAt = new Point(640, 360);
            public string LookProblem;
            public Func<bool> Front = delegate { return true; };
            public Func<bool> Stop = delegate { return false; };
            public Func<bool> Gate = delegate { return false; };
            public Func<bool> Others = delegate { return false; };

            public Rectangle Client { get { return new Rectangle(100, 50, 800, 600); } }
            public bool InFront() { return Front(); }
            public void Send(InputAction a) { Sent.Add(a); }
            public void Sleep(int ms) { Now += ms; }
            public long NowMs { get { return Now; } }
            public bool StopAsked { get { return Stop(); } }
            public bool GateWanted { get { return Gate(); } }
            public bool OthersWaiting { get { return Others(); } }

            public PictureLook Look(MacroStep s)
            {
                PictureLook l = new PictureLook();
                l.Problem = LookProblem;
                l.Found = LookProblem == null && Seen(Looks);
                l.At = SeenAt;
                Looks++;
                return l;
            }

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
            m.Name = "test";
            m.Steps.AddRange(steps);
            return m;
        }

        static MacroStep Until(RepeatMode mode)
        {
            MacroStep s = MacroStep.Repeat(mode, 0);
            s.Picture = "egg.png";
            return s;
        }

        // ---------- repeats ----------

        public static void TestARepeatPlaysItsStepsThatManyTimes()
        {
            World w = new World();
            MacroRunner r = new MacroRunner(Of(MacroStep.Repeat(RepeatMode.Times, 3), MacroStep.Key(0x45, 50),
                                               MacroStep.EndRepeat(), MacroStep.Key(0x46, 50)));
            Assert.Equal(RunEnd.Finished, r.Run(w), "plays to the end");
            Assert.Equal(4, w.Count(InputActionKind.KeyDown), "E three times, then F once");
            Assert.Equal((byte)0x46, w.Sent[w.Sent.Count - 1].Vk, "what follows the repeat comes last");
            Assert.Equal(3, r.Rounds, "and it says how many rounds it did");
        }

        public static void TestARepeatInsideARepeatMultiplies()
        {
            World w = new World();
            MacroRunner r = new MacroRunner(Of(
                MacroStep.Repeat(RepeatMode.Times, 2),
                    MacroStep.Repeat(RepeatMode.Times, 3), MacroStep.Key(0x45, 50), MacroStep.EndRepeat(),
                MacroStep.EndRepeat()));
            Assert.Equal(RunEnd.Finished, r.Run(w), "plays to the end");
            Assert.Equal(6, w.Count(InputActionKind.KeyDown), "two rounds of three");
        }

        public static void TestRepeatUntilAPictureShowsStopsWhenItDoes()
        {
            World w = new World();
            w.Seen = delegate(int looks) { return looks >= 2; };       // there on the third look
            MacroRunner r = new MacroRunner(Of(Until(RepeatMode.UntilSeen), MacroStep.Key(0x45, 50), MacroStep.EndRepeat()));
            Assert.Equal(RunEnd.Finished, r.Run(w), "ends by itself");
            Assert.Equal(2, w.Count(InputActionKind.KeyDown), "two rounds before it showed");
        }

        // Already there before the first round: there is nothing to wait for.
        public static void TestRepeatUntilAPictureShowsPlaysNothingIfItIsAlreadyThere()
        {
            World w = new World();
            w.Seen = delegate { return true; };
            MacroRunner r = new MacroRunner(Of(Until(RepeatMode.UntilSeen), MacroStep.Key(0x45, 50), MacroStep.EndRepeat()));
            Assert.Equal(RunEnd.Finished, r.Run(w), "ends at once");
            Assert.Equal(0, w.Sent.Count, "nothing pressed");
        }

        public static void TestRepeatUntilAPictureHasGoneKeepsOnWhileItIsThere()
        {
            World w = new World();
            w.Seen = delegate(int looks) { return looks < 3; };
            MacroRunner r = new MacroRunner(Of(Until(RepeatMode.UntilGone), MacroStep.Key(0x45, 50), MacroStep.EndRepeat()));
            Assert.Equal(RunEnd.Finished, r.Run(w), "ends once it has gone");
            Assert.Equal(3, w.Count(InputActionKind.KeyDown), "a round for each time it was still there");
        }

        // A round with nothing slow in it would otherwise spin as fast as the
        // processor allows and flood the game.
        public static void TestAnEmptyHandedRoundStillTakesAMoment()
        {
            World w = new World();
            MacroRunner r = new MacroRunner(Of(MacroStep.Repeat(RepeatMode.Forever, 0), MacroStep.KeyDown(0x45),
                                               MacroStep.KeyUp(0x45), MacroStep.EndRepeat()));
            w.Stop = delegate { return r.Rounds >= 100; };
            Assert.Equal(RunEnd.Cancelled, r.Run(w), "runs until stopped");
            // The pretend clock only moves when something sleeps.
            Assert.True(w.Now >= 100 * MacroRunner.MIN_ROUND_MS, "a hundred rounds took a hundred pauses");
        }

        // A list can be dragged into any order, so the runner never trusts it.
        public static void TestAnEndWithNoRepeatAboveItIsSteppedOver()
        {
            World w = new World();
            MacroRunner r = new MacroRunner(Of(MacroStep.EndRepeat(), MacroStep.Key(0x45, 50)));
            Assert.Equal(RunEnd.Finished, r.Run(w), "still finishes");
            Assert.Equal(1, w.Count(InputActionKind.KeyDown), "and plays the rest");
        }

        public static void TestARepeatWithNoEndRunsToTheEndOfTheMacro()
        {
            World w = new World();
            MacroRunner r = new MacroRunner(Of(MacroStep.Repeat(RepeatMode.Times, 3), MacroStep.Key(0x45, 50)));
            Assert.Equal(RunEnd.Finished, r.Run(w), "finishes rather than hanging");
            Assert.Equal(1, w.Count(InputActionKind.KeyDown), "its steps once");
        }

        // ---------- pictures ----------

        public static void TestClickAPictureClicksWhereItWasFound()
        {
            World w = new World();
            w.Seen = delegate { return true; };
            w.SeenAt = new Point(321, 234);
            MacroRunner r = new MacroRunner(Of(MacroStep.ClickPicture("egg.png", null, 0)));
            Assert.Equal(RunEnd.Finished, r.Run(w), "plays");
            Assert.Equal(InputActionKind.MoveTo, w.Sent[0].Kind, "moves there first");
            Assert.Equal(321, w.Sent[0].X, "across");
            Assert.Equal(234, w.Sent[0].Y, "down");
            Assert.Equal(1, w.Count(InputActionKind.ButtonDown), "one press");
            Assert.Equal(1, w.Count(InputActionKind.ButtonUp), "one release");
        }

        public static void TestClickAPictureWaitsForItToTurnUp()
        {
            World w = new World();
            w.Seen = delegate(int looks) { return looks >= 4; };
            MacroRunner r = new MacroRunner(Of(MacroStep.ClickPicture("egg.png", null, 5000)));
            Assert.Equal(RunEnd.Finished, r.Run(w), "plays");
            Assert.Equal(1, w.Count(InputActionKind.ButtonDown), "clicked once it showed");
            Assert.True(w.Now >= 4 * MacroStep.LOOK_EVERY_MS, "after looking a few times");
        }

        // The default: a picture that isn't there is no reason to stop.
        public static void TestAPictureThatNeverShowsIsSkipped()
        {
            World w = new World();
            MacroRunner r = new MacroRunner(Of(MacroStep.ClickPicture("egg.png", null, 1000), MacroStep.Key(0x45, 50)));
            Assert.Equal(RunEnd.Finished, r.Run(w), "carries on");
            Assert.Equal(0, w.Count(InputActionKind.ButtonDown), "no click");
            Assert.Equal(1, w.Count(InputActionKind.KeyDown), "but the next step played");
            Assert.True(w.Now >= 1000, "after waiting as long as it was told to");
        }

        public static void TestAPictureThatNeverShowsCanStopTheMacro()
        {
            World w = new World();
            MacroStep click = MacroStep.ClickPicture("egg-20261110-213000.png", null, 300);
            click.StopIfMissing = true;
            MacroRunner r = new MacroRunner(Of(click, MacroStep.Key(0x45, 50)));
            Assert.Equal(RunEnd.Failed, r.Run(w), "stops");
            Assert.Contains("egg", r.Problem, "and says which picture");
            Assert.Equal(0, w.Count(InputActionKind.KeyDown), "nothing after it played");
        }

        public static void TestAMissingPictureFileIsSaidPlainly()
        {
            World w = new World();
            w.LookProblem = "the picture egg is missing";
            MacroRunner r = new MacroRunner(Of(MacroStep.ClickPicture("egg.png", null, 300)));
            Assert.Equal(RunEnd.Failed, r.Run(w), "can't play");
            Assert.Contains("missing", r.Problem, "says why");
        }

        public static void TestWaitUntilAPictureHasGone()
        {
            World w = new World();
            w.Seen = delegate(int looks) { return looks < 3; };
            MacroRunner r = new MacroRunner(Of(MacroStep.WaitPicture("loading.png", null, 5000, true), MacroStep.Key(0x45, 50)));
            Assert.Equal(RunEnd.Finished, r.Run(w), "plays");
            Assert.Equal(3 * MacroStep.LOOK_EVERY_MS, (int)w.Now - 50 - MacroPlan.SETTLE_MS, "waited three looks, then pressed");
        }

        // ---------- stopping ----------

        public static void TestTheStopKeyLetsGoOfEveryKey()
        {
            World w = new World();
            w.Stop = delegate { return w.Now >= 500; };
            MacroRunner r = new MacroRunner(Of(MacroStep.KeyDown(0x57), MacroStep.Repeat(RepeatMode.Forever, 0),
                                               MacroStep.Wait(100), MacroStep.EndRepeat()));
            Assert.Equal(RunEnd.Cancelled, r.Run(w), "stops");
            InputAction last = w.Sent[w.Sent.Count - 1];
            Assert.Equal(InputActionKind.KeyUp, last.Kind, "W is let go");
            Assert.Equal((byte)0x57, last.Vk, "the key it was holding");
            Assert.Equal(0, r.Held.Count, "nothing held any more");
        }

        // You clicked another window: nothing more is sent into it.
        public static void TestAnotherWindowInFrontStopsItAtOnce()
        {
            World w = new World();
            w.Front = delegate { return w.Count(InputActionKind.KeyDown) < 2; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Repeat(RepeatMode.Forever, 0), MacroStep.Key(0x45, 50), MacroStep.EndRepeat()));
            Assert.Equal(RunEnd.TakenOver, r.Run(w), "stops");
            Assert.Equal(2, w.Count(InputActionKind.KeyDown), "nothing pressed after that");
        }

        // A drag stopped half-way would leave the mouse button down.
        public static void TestStoppingMidDragLetsGoOfTheButton()
        {
            World w = new World();
            w.Stop = delegate { return w.Count(InputActionKind.DragTo) >= 2; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Drag(0.2, 0.2, 0.8, 0.8, 600, true)));
            Assert.Equal(RunEnd.Cancelled, r.Run(w), "stops");
            InputAction last = w.Sent[w.Sent.Count - 1];
            Assert.Equal(InputActionKind.ButtonUp, last.Kind, "the button comes up");
            Assert.True(last.Right, "the one that was down");
        }

        // ---------- stepping aside ----------

        // The nudge gives up after a minute of waiting; a session that never
        // stepped aside would let every other client be kicked for idling.
        public static void TestASessionStepsAsideWhenTheNudgeWantsTheKeyboard()
        {
            World w = new World();
            w.Gate = delegate { return w.Count(InputActionKind.KeyDown) >= 3; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Repeat(RepeatMode.Forever, 0), MacroStep.Key(0x45, 50), MacroStep.EndRepeat()));
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside");

            w.Gate = delegate { return false; };
            w.Stop = delegate { return w.Count(InputActionKind.KeyDown) >= 6; };
            Assert.Equal(RunEnd.Cancelled, r.Run(w), "and carries on when it gets the keyboard back");
            Assert.Equal(6, w.Count(InputActionKind.KeyDown), "from where it left off");
        }

        public static void TestSteppingAsideLetsGoOfHeldKeysAndPressesThemAgain()
        {
            World w = new World();
            bool wanted = false;
            w.Gate = delegate { return wanted; };
            MacroRunner r = new MacroRunner(Of(MacroStep.KeyDown(0x57), MacroStep.Repeat(RepeatMode.Forever, 0),
                                               MacroStep.Wait(100), MacroStep.EndRepeat()));
            w.Stop = delegate { return false; };
            // Wanted once it has been going a while.
            w.Gate = delegate { return w.Now >= 300; };
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside");
            Assert.Equal(InputActionKind.KeyUp, w.Sent[w.Sent.Count - 1].Kind, "W let go while something else has the keyboard");

            int before = w.Sent.Count;
            w.Gate = delegate { return wanted; };
            w.Stop = delegate { return w.Now >= 1000; };
            r.Run(w);
            Assert.Equal(InputActionKind.KeyDown, w.Sent[before].Kind, "W goes down again first thing");
            Assert.Equal((byte)0x57, w.Sent[before].Vk, "the same key");
        }

        // The nudge gives up after a minute. A session sitting in a long wait
        // lets it in part-way, and waits out the rest when it is back.
        public static void TestALongWaitInASessionStepsAsidePartWay()
        {
            World w = new World();
            w.Gate = delegate { return w.Now >= 2000; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Repeat(RepeatMode.Forever, 0), MacroStep.Wait(30000),
                                               MacroStep.Key(0x45, 50), MacroStep.EndRepeat()));
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside");
            Assert.True(w.Now < 3000, "after a couple of seconds, not thirty");

            w.Gate = delegate { return false; };
            w.Stop = delegate { return w.Count(InputActionKind.KeyDown) >= 1; };
            r.Run(w);
            Assert.True(w.Now >= 30000, "the rest of the wait is still waited before the key");
            Assert.True(w.Now < 31000, "and no more than the rest");
        }

        // Two sessions taking turns: each gets its slice, not one step each.
        public static void TestASessionKeepsItsTurnBeforeLettingTheNextOneIn()
        {
            World w = new World();
            w.Others = delegate { return true; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Repeat(RepeatMode.Forever, 0), MacroStep.Wait(100), MacroStep.EndRepeat()));
            r.TurnMs = 2000;
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside in the end");
            Assert.True(w.Now >= 2000, "but only after its turn");
            Assert.True(w.Now < 2400, "and not long after");
        }

        // Something that is both a nudge away from stepping aside and wanted
        // at once still gets a step in every turn, so nothing stalls.
        public static void TestEveryTurnGetsAtLeastOneStepIn()
        {
            World w = new World();
            w.Gate = delegate { return true; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Repeat(RepeatMode.Forever, 0), MacroStep.Key(0x45, 50), MacroStep.EndRepeat()));
            for (int turn = 0; turn < 6; turn++) Assert.Equal(RunEnd.Yielded, r.Run(w), "turn " + turn);
            Assert.True(w.Count(InputActionKind.KeyDown) >= 2, "it got somewhere all the same");
        }

        // One that only presses and waits is never interrupted: it is short,
        // and half a macro is worse than a late nudge.
        public static void TestAPlainMacroNeverStepsAside()
        {
            World w = new World();
            w.Gate = delegate { return true; };
            w.Others = delegate { return true; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Key(0x45, 50), MacroStep.Wait(8000), MacroStep.Key(0x46, 50)));
            Assert.Equal(RunEnd.Finished, r.Run(w), "plays straight through");
        }

        // A play ten times over is a counted repeat, not a session - but it
        // can hold the keyboard for an hour. Told to step aside, it lets the
        // nudge in and carries on to the end.
        public static void TestAPlayToldToStepAsideMakesWayForTheNudge()
        {
            Macro ten = MacroPlay.Prepare(Of(MacroStep.Key(0x45, 50), MacroStep.Wait(100)), 10, 100);
            Assert.False(ten.IsSession, "it ends by itself");

            World plain = new World();
            plain.Gate = delegate { return true; };
            Assert.Equal(RunEnd.Finished, new MacroRunner(ten).Run(plain), "left as it is, it never makes way");

            World w = new World();
            w.Gate = delegate { return w.Count(InputActionKind.KeyDown) >= 3; };
            MacroRunner r = new MacroRunner(ten);
            r.StepsAside = true;
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside for the nudge");
            Assert.True(w.Count(InputActionKind.KeyDown) < 10, "part-way");

            w.Gate = delegate { return false; };
            Assert.Equal(RunEnd.Finished, r.Run(w), "and finishes when it is back");
            Assert.Equal(10, w.Count(InputActionKind.KeyDown), "ten times in all, none twice");
        }

        // With other macros waiting it has its turn, then lets them have theirs.
        public static void TestAPlayToldToStepAsideTakesTurnsWithOthers()
        {
            World w = new World();
            w.Others = delegate { return true; };
            MacroRunner r = new MacroRunner(MacroPlay.Prepare(Of(MacroStep.Wait(100)), 1000, 100));
            r.StepsAside = true;
            r.TurnMs = 2000;
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside in the end");
            Assert.True(w.Now >= 2000, "but only after its turn");
            Assert.True(w.Now < 2400, "and not long after");
        }

        // A slowed-down play can sit in a wait of minutes; the nudge still
        // gets in part-way, and the rest of the wait is waited after.
        public static void TestALongWaitInAPlayToldToStepAsideLetsTheNudgeIn()
        {
            World w = new World();
            w.Gate = delegate { return w.Now >= 2000; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Wait(30000), MacroStep.Key(0x45, 50)));
            r.StepsAside = true;
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside");
            Assert.True(w.Now < 3000, "after a couple of seconds, not thirty");

            w.Gate = delegate { return false; };
            Assert.Equal(RunEnd.Finished, r.Run(w), "then plays the rest");
            Assert.True(w.Now >= 30000, "the whole wait was waited before the key");
        }

        // Stopped while it waited for its turn back: the key it let go of to
        // step aside is not pressed again only to be let go once more.
        public static void TestAStopWhileWaitingForItsTurnPressesNothingAgain()
        {
            World w = new World();
            w.Gate = delegate { return w.Now >= 300; };
            MacroRunner r = new MacroRunner(Of(MacroStep.KeyDown(0x57), MacroStep.Repeat(RepeatMode.Forever, 0),
                                               MacroStep.Wait(100), MacroStep.EndRepeat()));
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside holding W");
            Assert.Equal(InputActionKind.KeyUp, w.Sent[w.Sent.Count - 1].Kind, "W let go meanwhile");

            int before = w.Sent.Count;
            w.Gate = delegate { return false; };
            w.Stop = delegate { return true; };
            Assert.Equal(RunEnd.Cancelled, r.Run(w), "stopped");
            Assert.Equal(before, w.Sent.Count, "nothing sent at all - W stays let go");
            Assert.Equal(0, r.Held.Count, "and nothing held");
        }

        // ---------- long holds and drags ----------

        static int CountIn(List<InputAction> plan, InputActionKind kind)
        {
            int n = 0;
            foreach (InputAction a in plan) if (a.Kind == kind) n++;
            return n;
        }

        // A recording holding W for twenty seconds, played at a quarter
        // speed, is one hold of eighty - past the minute the nudge waits.
        public static void TestALongHoldInAPlayThatTakesTurnsLetsTheNudgeIn()
        {
            World w = new World();
            w.Gate = delegate { return w.Now >= 2000; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Key(0x57, 80000), MacroStep.Key(0x45, 50)));
            r.StepsAside = true;
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside part-way through the hold");
            Assert.True(w.Now < 3000, "after a couple of seconds, not eighty");
            InputAction last = w.Sent[w.Sent.Count - 1];
            Assert.Equal(InputActionKind.KeyUp, last.Kind, "W let go while the nudge has the keyboard");
            Assert.Equal((byte)0x57, last.Vk, "W");

            int before = w.Sent.Count;
            w.Gate = delegate { return false; };
            Assert.Equal(RunEnd.Finished, r.Run(w), "then plays the rest");
            Assert.Equal(InputActionKind.KeyDown, w.Sent[before].Kind, "W goes down again first thing");
            Assert.Equal((byte)0x57, w.Sent[before].Vk, "the same key");
            Assert.True(w.Now >= 80000, "the whole hold was held before E");
            Assert.True(w.Now < 81000, "and no more than the rest");
            Assert.Equal(0, r.Held.Count, "nothing left held");
        }

        // A second's hold is played whole, even with the nudge waiting: it
        // is over before the nudge would notice, and letting go and pressing
        // again would move the character for nothing.
        public static void TestAShortHoldIsPlayedWhole()
        {
            World w = new World();
            w.Gate = delegate { return true; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Key(0x57, MacroRunner.LONG_STEP_MS)));
            r.StepsAside = true;
            Assert.Equal(RunEnd.Finished, r.Run(w), "played to the end");
            Assert.Equal(1, w.Count(InputActionKind.KeyDown), "pressed once");
        }

        // One that does not take turns plays even a long hold whole, as before.
        public static void TestALongHoldInAPlainPlayIsPlayedWhole()
        {
            World w = new World();
            w.Gate = delegate { return true; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Key(0x57, 80000)));
            Assert.Equal(RunEnd.Finished, r.Run(w), "played to the end");
            Assert.Equal(1, w.Count(InputActionKind.KeyDown), "pressed once");
        }

        // Turning the camera for eighty seconds: the nudge gets in part-way,
        // and the drag carries on from where it was with the button down
        // again - every move sent once, none twice.
        public static void TestALongDragStepsAsideAndCarriesOnWithTheButtonDownAgain()
        {
            MacroStep drag = MacroStep.Drag(0.2, 0.2, 0.8, 0.8, 80000, true);
            World w = new World();
            List<InputAction> whole = new List<InputAction>();
            MacroPlan.Add(whole, new List<byte>(), drag, w.Client);

            w.Gate = delegate { return w.Now >= 2000; };
            MacroRunner r = new MacroRunner(Of(drag));
            r.StepsAside = true;
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside part-way through the drag");
            Assert.True(w.Now < 3000, "after a couple of seconds, not eighty");
            InputAction last = w.Sent[w.Sent.Count - 1];
            Assert.Equal(InputActionKind.ButtonUp, last.Kind, "the button comes up while the nudge has the keyboard");
            Assert.True(last.Right, "the right one");
            InputAction lastDrag = new InputAction();
            foreach (InputAction a in w.Sent) if (a.Kind == InputActionKind.DragTo) lastDrag = a;

            int before = w.Sent.Count;
            w.Gate = delegate { return false; };
            Assert.Equal(RunEnd.Finished, r.Run(w), "then drags the rest");
            Assert.Equal(InputActionKind.MoveTo, w.Sent[before].Kind, "back to where the drag had got to");
            Assert.Equal(lastDrag.X, w.Sent[before].X, "across");
            Assert.Equal(lastDrag.Y, w.Sent[before].Y, "down");
            Assert.Equal(InputActionKind.ButtonDown, w.Sent[before + 1].Kind, "then the button goes down again");
            Assert.True(w.Sent[before + 1].Right, "the same one");
            Assert.Equal(CountIn(whole, InputActionKind.DragTo), w.Count(InputActionKind.DragTo), "every move once");
            Assert.Equal(2, w.Count(InputActionKind.ButtonDown), "down twice");
            Assert.Equal(2, w.Count(InputActionKind.ButtonUp), "and up twice");
            Assert.True(w.Now >= 80000, "the whole drag was dragged");
        }

        // Stopped while it stood aside mid-drag: the button it let go of is
        // not pressed again.
        public static void TestAStopWhileALongDragStoodAsideSendsNothing()
        {
            World w = new World();
            w.Gate = delegate { return w.Now >= 2000; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Drag(0.2, 0.2, 0.8, 0.8, 80000, false)));
            r.StepsAside = true;
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside mid-drag");

            int before = w.Sent.Count;
            w.Gate = delegate { return false; };
            w.Stop = delegate { return true; };
            Assert.Equal(RunEnd.Cancelled, r.Run(w), "stopped");
            Assert.Equal(before, w.Sent.Count, "nothing sent - the button stays up");
        }

        // With the nudge wanted every turn, a long drag still gets somewhere
        // each time, and to the end, rather than pressing and letting go of
        // the button for ever.
        public static void TestEveryTurnOfALongDragGetsSomewhere()
        {
            MacroStep drag = MacroStep.Drag(0.2, 0.2, 0.8, 0.8, 3000, true);
            World w = new World();
            List<InputAction> whole = new List<InputAction>();
            MacroPlan.Add(whole, new List<byte>(), drag, w.Client);

            w.Gate = delegate { return true; };
            MacroRunner r = new MacroRunner(Of(drag));
            r.StepsAside = true;
            RunEnd end = RunEnd.Yielded;
            for (int turn = 0; turn < 5000 && end == RunEnd.Yielded; turn++) end = r.Run(w);
            Assert.Equal(RunEnd.Finished, end, "it gets to the end");
            Assert.Equal(CountIn(whole, InputActionKind.DragTo), w.Count(InputActionKind.DragTo), "every move once");
            Assert.Equal(w.Count(InputActionKind.ButtonDown), w.Count(InputActionKind.ButtonUp), "never left down");
        }

        // A session's long hold steps aside too, not only a play told to.
        public static void TestALongHoldInASessionStepsAside()
        {
            World w = new World();
            w.Gate = delegate { return w.Now >= 2000; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Repeat(RepeatMode.Forever, 0), MacroStep.Key(0x57, 60000),
                                               MacroStep.EndRepeat()));
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside");
            Assert.True(w.Now < 3000, "part-way through the hold");
        }

        // ---------- what is put back at the end ----------

        // After stepping aside, the client is in front and the pointer where
        // the play left it. What is put back at the end is how things were
        // before the first turn.
        public static void TestWhatWasThereBeforeIsKeptFromTheFirstTurnOnly()
        {
            MacroRunner r = new MacroRunner(Of(MacroStep.Key(0x45, 50)));
            Guid home = Guid.NewGuid();
            r.Before.Keep(new IntPtr(111), home, new Point(10, 20), true, null);
            r.Before.Keep(new IntPtr(222), Guid.NewGuid(), new Point(500, 600), false, null);
            Assert.Equal(new IntPtr(111), r.Before.Window, "the window you were using");
            Assert.Equal(home, r.Before.Desktop, "the desktop you were on");
            Assert.Equal(new Point(10, 20), r.Before.Pointer, "where your pointer was");
            Assert.True(r.Before.Minimized, "minimised as it was");
        }

        // Two plays taking turns: the second's first turn finds the first's
        // client in front, left there when it stood aside. It puts back what
        // the first will - your window - not that client.
        public static void TestAPlayStartingWhileAnotherStandsAsideKeepsWhatThatOneKept()
        {
            Guid home = Guid.NewGuid();
            FrontBefore first = new FrontBefore();
            first.Keep(new IntPtr(111), home, new Point(10, 20), false, null);

            FrontBefore second = new FrontBefore();
            second.Keep(new IntPtr(500), Guid.NewGuid(), new Point(700, 300), true, first);
            Assert.Equal(new IntPtr(111), second.Window, "your window, not the other client");
            Assert.Equal(home, second.Desktop, "your desktop");
            Assert.Equal(new Point(10, 20), second.Pointer, "your pointer");
            Assert.True(second.Minimized, "its own client's state is its own");
        }

        // Stopped in the moment between going back to where a drag had got
        // and pressing the button again: no stray click.
        public static void TestAStopJustBeforeADragPressesAgainPressesNothing()
        {
            World w = new World();
            w.Gate = delegate { return w.Now >= 2000; };
            MacroRunner r = new MacroRunner(Of(MacroStep.Drag(0.2, 0.2, 0.8, 0.8, 80000, true)));
            r.StepsAside = true;
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside mid-drag");

            int before = w.Sent.Count;
            w.Gate = delegate { return false; };
            w.Stop = delegate { return w.Sent.Count > before; };
            Assert.Equal(RunEnd.Cancelled, r.Run(w), "stopped after going back");
            Assert.Equal(0, w.Count(InputActionKind.ButtonDown) - w.Count(InputActionKind.ButtonUp), "nothing left down");
            Assert.Equal(1, w.Count(InputActionKind.ButtonDown), "and the button never went down again");
        }

        // The finding itself: W held for twenty seconds in a Quick Record,
        // played at a quarter speed, is a hold of eighty. Prepared that way
        // it takes turns, and lets the nudge in part-way through the hold.
        public static void TestAQuarterSpeedHoldLetsTheNudgeIn()
        {
            Macro m = Of(MacroStep.Key(0x57, 20000));
            Macro slow = MacroPlay.Prepare(m, 1, 25);
            Assert.True(MacroPlay.StepsAside(m, 1, 25), "a slowed-down play takes turns");

            World w = new World();
            w.Gate = delegate { return w.Now >= 2000; };
            MacroRunner r = new MacroRunner(slow);
            r.StepsAside = true;
            Assert.Equal(RunEnd.Yielded, r.Run(w), "steps aside");
            Assert.True(w.Now < 3000, "well inside the minute the nudge waits");
            w.Gate = delegate { return false; };
            Assert.Equal(RunEnd.Finished, r.Run(w), "then finishes");
            Assert.True(w.Now >= 80000, "the hold was the full eighty seconds");
        }

        // A rule can play a macro twenty times over: one long play that has
        // to take turns, or the nudge is kept out.
        public static void TestAJobPlayedSeveralTimesOverTakesTurns()
        {
            MacroJob j = new MacroJob();
            j.Times = 1;
            Assert.False(MainForm.StepsAsideFor(j), "once, at its own speed: played straight through");
            j.Times = 3;
            Assert.True(MainForm.StepsAsideFor(j), "three times over");
            j.Times = 1;
            j.StepsAside = true;
            Assert.True(MainForm.StepsAsideFor(j), "told to");
        }
    }
}
