using System;
using System.Collections.Generic;
using System.Drawing;

namespace RobloxKeeper.Tests
{
    // The steps added in 1.4, as plain data: what they are called, how long
    // they take, what they send, how they are saved, and where a picture
    // found in a capture is on the screen.
    static class MacroBlocksTests
    {
        static readonly Rectangle Window = new Rectangle(100, 50, 800, 600);

        static Macro Of(params MacroStep[] steps)
        {
            Macro m = new Macro();
            m.Name = "test";
            m.Steps.AddRange(steps);
            return m;
        }

        static List<InputAction> Plan(params MacroStep[] steps)
        {
            return MacroPlan.For(Of(steps), Window);
        }

        // ---------- the model ----------

        public static void TestARepeatCountsItsStepsEachTimeRound()
        {
            Macro m = Of(MacroStep.Wait(1000), MacroStep.Repeat(RepeatMode.Times, 5), MacroStep.Wait(2000), MacroStep.EndRepeat());
            Assert.Equal(11000, m.DurationMs, "one second, then two seconds five times");
        }

        public static void TestARepeatedMacroOverAMinuteIsRefused()
        {
            Macro m = Of(MacroStep.Repeat(RepeatMode.Times, 40), MacroStep.Wait(2000), MacroStep.EndRepeat());
            Assert.Contains("under a minute", m.Problem(), "eighty seconds in front is too long");
        }

        // One that repeats until stopped is a session: it steps aside for the
        // nudge, so the minute's limit is not what protects you from it.
        public static void TestARepeatUntilStoppedHasNoMinuteLimit()
        {
            Macro m = Of(MacroStep.Repeat(RepeatMode.Forever, 0), MacroStep.Wait(50000), MacroStep.Wait(50000), MacroStep.EndRepeat());
            Assert.True(m.IsSession, "it goes on until stopped");
            Assert.Equal(null, m.Problem(), "and may be saved");
        }

        public static void TestAMacroThatOnlyRepeatsANumberOfTimesIsNotASession()
        {
            Assert.False(Of(MacroStep.Repeat(RepeatMode.Times, 3), MacroStep.Wait(10), MacroStep.EndRepeat()).IsSession, "it ends by itself");
        }

        public static void TestARepeatNeedsItsEnd()
        {
            Assert.Contains("no end", Of(MacroStep.Repeat(RepeatMode.Times, 3), MacroStep.Wait(10)).Problem(), "left open");
            Assert.Contains("no repeat above", Of(MacroStep.Wait(10), MacroStep.EndRepeat()).Problem(), "an end on its own");
            Assert.Contains("repeats nothing", Of(MacroStep.Repeat(RepeatMode.Times, 3), MacroStep.EndRepeat()).Problem(), "nothing inside");
        }

        public static void TestRepeatsOnlyNestSoDeep()
        {
            List<MacroStep> steps = new List<MacroStep>();
            for (int i = 0; i <= Macro.MAX_DEPTH; i++) steps.Add(MacroStep.Repeat(RepeatMode.Times, 2));
            steps.Add(MacroStep.Wait(10));
            for (int i = 0; i <= Macro.MAX_DEPTH; i++) steps.Add(MacroStep.EndRepeat());
            Assert.Contains("too many repeats", Of(steps.ToArray()).Problem(), "one deeper than allowed");
        }

        public static void TestAPictureStepNeedsAPicture()
        {
            Assert.Contains("no picture", Of(MacroStep.ClickPicture(null, null, 0)).Problem(), "nothing chosen yet");
        }

        public static void TestStepsSayWhatTheyDo()
        {
            Assert.Equal("Click egg where it is, waiting up to 3s",
                MacroStep.ClickPicture("egg-20261110-213000.png", null, 3000).Describe(), "a picture click, by the picture's name");
            Assert.Equal("Wait until loading disappears, up to 10s",
                MacroStep.WaitPicture("loading-20261110-213000.png", null, 10000, true).Describe(), "a wait for it to go");
            Assert.Equal("Repeat 5 times:", MacroStep.Repeat(RepeatMode.Times, 5).Describe(), "a counted repeat");
            Assert.Equal("Repeat until stopped:", MacroStep.Repeat(RepeatMode.Forever, 0).Describe(), "one that goes on");
            Assert.Equal("Click where the pointer is", MacroStep.ClickHere(false).Describe(), "an autoclicker's click");
            Assert.Equal("Right-drag 20%, 30% to 80%, 30% over 0.5s", MacroStep.Drag(0.2, 0.3, 0.8, 0.3, 500, true).Describe(), "turning the camera");
            Assert.Equal("Scroll down 3", MacroStep.Scroll(0.5, 0.5, -3).Describe(), "the wheel");
        }

        public static void TestAPictureIsCalledByItsNameNotItsFile()
        {
            Assert.Equal("Buy-button", MacroStep.PictureLabel("Buy-button-20261110-213000.png"), "the stamp is dropped");
            Assert.Equal("egg", MacroStep.PictureLabel("egg.png"), "one without a stamp is left alone");
            Assert.Equal("a picture", MacroStep.PictureLabel(null), "none chosen");
        }

        // ---------- what they send ----------

        public static void TestAClickWhereThePointerIsNeverMovesIt()
        {
            List<InputAction> plan = Plan(MacroStep.ClickHere(false));
            foreach (InputAction a in plan) Assert.NotEqual(InputActionKind.MoveTo, a.Kind, "the pointer is the user's");
            Assert.Equal(InputActionKind.ButtonDown, plan[0].Kind, "it presses straight away");
        }

        // Turning the camera goes by how far the mouse travelled, so a drag is
        // many small moves, each saying how far it came.
        public static void TestADragTravelsInSmallMoves()
        {
            List<InputAction> plan = Plan(MacroStep.Drag(0, 0, 1, 0, 300, true));
            int moves = 0, travelled = 0, lastX = 0;
            bool pressed = false, released = false;
            foreach (InputAction a in plan)
            {
                if (a.Kind == InputActionKind.ButtonDown) { pressed = true; Assert.Equal(0, moves, "pressed before it moves"); }
                if (a.Kind == InputActionKind.ButtonUp) released = true;
                if (a.Kind != InputActionKind.DragTo) continue;
                Assert.False(released, "let go only at the end");
                moves++;
                travelled += a.Dx;
                lastX = a.X;
            }
            Assert.True(pressed && released, "pressed and let go");
            Assert.True(moves >= 10, "many moves, not one jump");
            Assert.Equal(Window.Width - 1, travelled, "which add up to the whole way");
            Assert.Equal(Window.Right - 1, lastX, "and end on the far edge");
        }

        public static void TestScrollingTurnsTheWheelAClickAtATime()
        {
            int up = 0, down = 0;
            foreach (InputAction a in Plan(MacroStep.Scroll(0.5, 0.5, 3), MacroStep.Scroll(0.5, 0.5, -2)))
            {
                if (a.Kind != InputActionKind.Wheel) continue;
                if (a.Delta == MacroPlan.WHEEL_CLICK) up++;
                else if (a.Delta == -MacroPlan.WHEEL_CLICK) down++;
            }
            Assert.Equal(3, up, "three up");
            Assert.Equal(2, down, "two down");
        }

        // The whole-macro plan is for macros that only press and click; steps
        // decided as they run add nothing to it rather than something wrong.
        public static void TestStepsDecidedAsTheyRunAddNothingToAPlan()
        {
            Assert.Equal(0, Plan(MacroStep.ClickPicture("egg.png", null, 0), MacroStep.Repeat(RepeatMode.Times, 2), MacroStep.EndRepeat()).Count,
                "nothing sent blind");
        }

        // A build older than 1.4 plays a kind it doesn't know as typing. The
        // new kinds keep that text empty, so it types nothing into the game.
        public static void TestNewStepsSaveNoTextForAnOlderBuildToType()
        {
            Macro m = Of(MacroStep.ClickPicture("egg.png", "egg-mask.png", 3000), MacroStep.Repeat(RepeatMode.Forever, 0),
                         MacroStep.Drag(0, 0, 1, 1, 100, false), MacroStep.EndRepeat());
            foreach (MacroStep s in WatchStore.DeserializeMacro(WatchStore.SerializeMacro(m)).Steps)
                Assert.True(string.IsNullOrEmpty(s.Text), s.Kind + " carries no text");
        }

        // ---------- saving ----------

        public static void TestEveryNewStepComesBackAsItWasSaved()
        {
            MacroStep click = MacroStep.ClickPicture("egg.png", "egg-mask.png", 3000);
            click.Tolerance = 0.9; click.Box = "Shop"; click.OffX = 12; click.OffY = -4;
            click.StopIfMissing = true; click.RightButton = true;
            MacroStep until = MacroStep.Repeat(RepeatMode.UntilGone, 7);
            until.Picture = "loading.png";
            Macro m = Of(click, MacroStep.WaitPicture("loading.png", null, 10000, true), until,
                         MacroStep.Drag(0.1, 0.2, 0.3, 0.4, 500, true), MacroStep.Scroll(0.5, 0.5, -3),
                         MacroStep.Move(0.25, 0.75), MacroStep.ClickHere(true), MacroStep.EndRepeat());

            Macro back = WatchStore.DeserializeMacro(WatchStore.SerializeMacro(m));
            Assert.Equal(m.Steps.Count, back.Steps.Count, "every step");
            for (int i = 0; i < m.Steps.Count; i++)
                Assert.Equal(m.Steps[i].Describe(), back.Steps[i].Describe(), "step " + (i + 1));

            MacroStep c = back.Steps[0];
            Assert.Equal("egg-mask.png", c.Mask, "the painted-out copy");
            Assert.Equal(0.9, c.Tolerance, "how alike");
            Assert.Equal("Shop", c.Box, "the box to look in");
            Assert.Equal(12, c.OffX, "right of its middle");
            Assert.Equal(-4, c.OffY, "above its middle");
            Assert.True(c.StopIfMissing, "stop if it isn't there");
            Assert.Equal(RepeatMode.UntilGone, back.Steps[2].Mode, "how the repeat ends");
            Assert.Equal("loading.png", back.Steps[2].Picture, "and by which picture");
            Assert.True(back.Steps[6].AtPointer, "where the pointer is");
        }

        // A macro saved by 1.3 has none of the new fields.
        public static void TestAMacroSavedBeforeTheseStepsStillLoads()
        {
            char f = (char)0x1E;
            string step = string.Join(f.ToString(), new string[] { "1", "0", "50", "0.5", "0.25", "1", "0", "", "0" });
            Macro m = WatchStore.DeserializeMacro("old\t" + step);
            Assert.Equal(1, m.Steps.Count, "its one step");
            Assert.Equal("Right-click 50% across, 25% down", m.Steps[0].Describe(), "the click it always was");
            Assert.False(m.Steps[0].AtPointer, "at a place, not the pointer");
        }

        // ---------- where a found picture is ----------

        static Pixels Flat(int w, int h, int colour)
        {
            Pixels p = new Pixels(w, h);
            p.Fill(colour);
            return p;
        }

        static Pixels Needle()
        {
            Pixels n = new Pixels(20, 10);
            for (int y = 0; y < n.Height; y++)
                for (int x = 0; x < n.Width; x++)
                    n.Set(x, y, unchecked((int)0xFF000000) | ((x * 12) << 16) | ((y * 25) << 8) | 0x40);
            return n;
        }

        static void Paste(Pixels into, Pixels what, int atX, int atY)
        {
            for (int y = 0; y < what.Height; y++)
                for (int x = 0; x < what.Width; x++)
                    into.Set(atX + x, atY + y, what.At(x, y));
        }

        public static void TestAFoundPictureIsClickedInItsMiddle()
        {
            Pixels shot = Flat(400, 300, unchecked((int)0xFF202020));
            Paste(shot, Needle(), 100, 60);
            double fx, fy, score;
            Assert.True(PictureSearch.Find(shot, Needle(), null, 0.95, Rectangle.Empty, 0, 0, out fx, out fy, out score), "found");
            Assert.Equal(110.0 / 400, fx, "its middle, across");
            Assert.Equal(65.0 / 300, fy, "its middle, down");
        }

        // The capture is of the whole window, frame and title bar too, and on
        // a scaled display is not the size the window is on the screen. So a
        // find goes to the screen as a fraction of the window.
        public static void TestAFindLandsOnTheSameSpotOfAWindowOfAnySize()
        {
            Pixels shot = Flat(400, 300, unchecked((int)0xFF202020));
            Paste(shot, Needle(), 190, 145);         // dead centre
            double fx, fy, score;
            PictureSearch.Find(shot, Needle(), null, 0.95, Rectangle.Empty, 0, 0, out fx, out fy, out score);

            Assert.Equal(new Point(200, 150), PictureSearch.OnScreen(new Rectangle(0, 0, 401, 301), fx, fy), "a window the capture's size");
            Assert.Equal(new Point(1100, 550), PictureSearch.OnScreen(new Rectangle(600, 250, 1001, 601), fx, fy),
                "one two and a half times bigger, somewhere else on the screen");
        }

        public static void TestAClickCanBeSetOffFromThePicturesMiddle()
        {
            Pixels shot = Flat(400, 300, unchecked((int)0xFF202020));
            Paste(shot, Needle(), 100, 60);
            double fx, fy, score;
            PictureSearch.Find(shot, Needle(), null, 0.95, Rectangle.Empty, 30, -10, out fx, out fy, out score);
            Assert.Equal(140.0 / 400, fx, "thirty to the right");
            Assert.Equal(55.0 / 300, fy, "ten up");
        }

        public static void TestLookingInABoxStillSaysWhereInTheWholeWindow()
        {
            Pixels shot = Flat(400, 300, unchecked((int)0xFF202020));
            Paste(shot, Needle(), 300, 200);
            double fx, fy, score;
            Assert.True(PictureSearch.Find(shot, Needle(), null, 0.95, new Rectangle(250, 150, 150, 150), 0, 0, out fx, out fy, out score), "found in the box");
            Assert.Equal(310.0 / 400, fx, "across the whole window, not the box");
            Assert.Equal(205.0 / 300, fy, "and down it");
        }

        public static void TestAPictureOutsideTheBoxIsNotFound()
        {
            Pixels shot = Flat(400, 300, unchecked((int)0xFF202020));
            Paste(shot, Needle(), 20, 20);
            double fx, fy, score;
            Assert.False(PictureSearch.Find(shot, Needle(), null, 0.95, new Rectangle(250, 150, 150, 150), 0, 0, out fx, out fy, out score),
                "it is there, but not where it was told to look");
        }

        public static void TestNothingToLookAtIsNotFoundRatherThanAFault()
        {
            double fx, fy, score;
            Assert.False(PictureSearch.Find(null, Needle(), null, 0.9, Rectangle.Empty, 0, 0, out fx, out fy, out score), "no capture");
            Assert.False(PictureSearch.Find(Flat(10, 5, 0), Needle(), null, 0.9, Rectangle.Empty, 0, 0, out fx, out fy, out score), "a capture smaller than the picture");
        }
    }
}
