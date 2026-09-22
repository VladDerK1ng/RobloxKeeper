using System;
using System.Collections.Generic;
using System.Drawing;

namespace RobloxKeeper.Tests
{
    // Turning what was pressed and clicked while recording into steps.
    //
    // The hooks that see the input are a thin shell; everything about what
    // the input MEANS - a tap, a hold, a pause, a click on the game, a click
    // somewhere else - is decided here.
    static class MacroRecordingTests
    {
        static readonly Rectangle Window = new Rectangle(100, 50, 800, 600);

        static RawInput Down(byte vk, long ms) { return RawInput.Key(true, vk, ms); }
        static RawInput Up(byte vk, long ms) { return RawInput.Key(false, vk, ms); }

        static List<MacroStep> Steps(long endMs, params RawInput[] events)
        {
            return MacroRecording.ToSteps(events, Window, endMs);
        }

        static string Said(List<MacroStep> steps)
        {
            List<string> s = new List<string>();
            foreach (MacroStep m in steps) s.Add(m.Describe());
            return string.Join(" | ", s.ToArray());
        }

        // Recording starts before anything is pressed; that first pause is
        // not part of the macro.
        public static void TestATapIsAPress()
        {
            Assert.Equal("Press W", Said(Steps(2000, Down(0x57, 1000), Up(0x57, 1060))), "one tap");
        }

        public static void TestThePausesBetweenAreWaits()
        {
            Assert.Equal("Press E | Wait 1.5s | Press E",
                Said(Steps(3000, Down(0x45, 0), Up(0x45, 50), Down(0x45, 1550), Up(0x45, 1600))), "two taps");
        }

        public static void TestAPauseIsRoundedToAHundredthOfASecond()
        {
            Assert.Equal("Press E | Wait 1.23s | Press E",
                Said(Steps(3000, Down(0x45, 0), Up(0x45, 50), Down(0x45, 1284), Up(0x45, 1330))), "1234ms");
        }

        // Holding one key while pressing another - walking and jumping - has
        // to stay overlapped, or it is a different move.
        public static void TestKeysHeldTogetherStayTogether()
        {
            Assert.Equal("Hold down W | Wait 1s | Press Space | Wait 0.95s | Let go of W",
                Said(Steps(3000, Down(0x57, 0), Down(0x20, 1000), Up(0x20, 1050), Up(0x57, 2000))), "walk and jump");
        }

        // Windows repeats a held key's down event; it is still one hold.
        public static void TestAHeldKeysRepeatsAreOneHold()
        {
            Assert.Equal("Hold W for 0.5s",
                Said(Steps(1000, Down(0x57, 0), Down(0x57, 30), Down(0x57, 60), Up(0x57, 500))), "one hold");
        }

        public static void TestAClickOnTheGameIsWhereItWasOnTheWindow()
        {
            List<MacroStep> s = Steps(1000, RawInput.Button(true, false, 500, 200, 0), RawInput.Button(false, false, 500, 200, 40));
            Assert.Equal(1, s.Count, "one click");
            Assert.Equal(MacroStepKind.Click, s[0].Kind, "a click");
            Assert.Equal(new Point(500, 200), MacroPlan.PointIn(Window, s[0].X, s[0].Y), "played back on the same spot");
        }

        public static void TestARightClickStaysARightClick()
        {
            List<MacroStep> s = Steps(1000, RawInput.Button(true, true, 300, 300, 0), RawInput.Button(false, true, 300, 300, 40));
            Assert.True(s[0].RightButton, "right");
        }

        // A click outside the client went to something else - this app, the
        // taskbar - and is no part of the macro.
        public static void TestAClickOffTheGameIsLeftOut()
        {
            Assert.Equal("Press E | Wait 1s | Press E",
                Said(Steps(2000, Down(0x45, 0), Up(0x45, 50),
                              RawInput.Button(true, false, 20, 20, 500), RawInput.Button(false, false, 20, 20, 540),
                              Down(0x45, 1050), Up(0x45, 1100))), "the outside click is gone");
        }

        public static void TestTheStopKeyIsNotPartOfTheMacro()
        {
            Assert.Equal("Press E",
                Said(Steps(2000, Down(0x45, 0), Up(0x45, 50), Down(MacroRecording.STOP_KEY, 900))), "F8 left out");
        }

        // Stopping while a key is down must not leave it stuck down in the
        // game every time the macro plays.
        public static void TestAKeyStillDownWhenRecordingStopsIsLetGo()
        {
            Assert.Equal("Hold W for 2s", Said(Steps(2000, Down(0x57, 0))), "let go when it stopped");
        }

        // The same protection when playing: a macro edited down to a hold
        // with no let-go still never leaves the key down.
        public static void TestPlayingNeverLeavesAKeyDown()
        {
            Macro m = new Macro();
            m.Steps.Add(MacroStep.KeyDown(0x57));
            m.Steps.Add(MacroStep.Wait(500));
            List<InputAction> plan = MacroPlan.For(m, Window);
            InputAction last = plan[plan.Count - 1];
            Assert.Equal(InputActionKind.KeyUp, last.Kind, "ends by letting go");
            Assert.Equal((byte)0x57, last.Vk, "of W");
        }
    }
}
