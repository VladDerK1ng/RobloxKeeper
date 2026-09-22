using System;
using System.Collections.Generic;
using System.Drawing;

namespace RobloxKeeper.Tests
{
    // What playing a macro sends, worked out as plain data before anything is
    // sent - so it can be checked exactly, without a game or a keyboard.
    static class MacroPlanTests
    {
        static readonly Rectangle Window = new Rectangle(100, 50, 800, 600);

        public static void TestAClickLandsOnTheSameFractionOfAnyWindow()
        {
            Assert.Equal(new Point(500, 200), MacroPlan.PointIn(Window, 0.5, 0.25), "halfway across, a quarter down");
            Assert.Equal(new Point(960, 270), MacroPlan.PointIn(new Rectangle(0, 0, 1920, 1080), 0.5, 0.25),
                "the same place on a bigger window");
        }

        // A click outside the client would land on whatever is next to it.
        public static void TestAClickNeverLandsOutsideTheWindow()
        {
            Assert.Equal(new Point(100, 649), MacroPlan.PointIn(Window, -0.2, 1.5), "held to the edges");
        }

        public static void TestAKeyIsPressedHeldAndLetGo()
        {
            List<InputAction> plan = Plan(MacroStep.Key(0x57, 2000));
            Assert.Equal(InputActionKind.KeyDown, plan[0].Kind, "down");
            Assert.Equal((byte)0x57, plan[0].Vk, "the W key");
            Assert.Equal(InputActionKind.Sleep, plan[1].Kind, "held");
            Assert.Equal(2000, plan[1].Ms, "for two seconds");
            Assert.Equal(InputActionKind.KeyUp, plan[2].Kind, "then up");
            Assert.Equal((byte)0x57, plan[2].Vk, "the same key");
        }

        public static void TestAClickMovesThereThenPressesAndLetsGo()
        {
            List<InputAction> plan = Plan(MacroStep.Click(0.5, 0.25, true));
            Assert.Equal(InputActionKind.MoveTo, plan[0].Kind, "moves first");
            Assert.Equal(500, plan[0].X, "across");
            Assert.Equal(200, plan[0].Y, "down");

            List<InputActionKind> presses = new List<InputActionKind>();
            foreach (InputAction a in plan)
                if (a.Kind == InputActionKind.ButtonDown || a.Kind == InputActionKind.ButtonUp)
                {
                    presses.Add(a.Kind);
                    Assert.True(a.Right, "the right button");
                }
            Assert.Equal(2, presses.Count, "one press and one release");
            Assert.Equal(InputActionKind.ButtonDown, presses[0], "press first");
        }

        public static void TestTypingSendsEachCharacterInTurn()
        {
            List<char> typed = new List<char>();
            foreach (InputAction a in Plan(MacroStep.Type("/e hi")))
                if (a.Kind == InputActionKind.Char) typed.Add(a.Char);
            Assert.Equal("/e hi", new string(typed.ToArray()), "every character, in order");
        }

        // The owner asked for it: typing that opens the chat first and sends
        // what it typed, rather than three steps to get right by hand.
        public static void TestSayingSomethingInChatOpensItTypesAndSends()
        {
            MacroStep say = MacroStep.Type("gg");
            say.InChat = true;
            List<InputAction> plan = Plan(say);

            List<string> did = new List<string>();
            foreach (InputAction a in plan)
            {
                if (a.Kind == InputActionKind.KeyDown) did.Add("down " + MacroKeys.Name(a.Vk));
                else if (a.Kind == InputActionKind.Char) did.Add("type " + a.Char);
                else if (a.Kind == InputActionKind.Sleep && a.Ms >= MacroPlan.CHAT_OPEN_MS) did.Add("wait for the chat");
            }
            Assert.Equal("down /|wait for the chat|type g|type g|down Enter", string.Join("|", did.ToArray()),
                "open, wait, type, send");
            Assert.Equal("Say \"gg\" in chat", say.Describe(), "and it says so");
        }

        public static void TestSayingInChatIsSaved()
        {
            MacroStep say = MacroStep.Type("gg");
            say.InChat = true;
            Macro m = new Macro();
            m.Name = "x";
            m.Steps.Add(say);
            m.Steps.Add(MacroStep.Type("plain"));
            Macro back = WatchStore.DeserializeMacro(WatchStore.SerializeMacro(m));
            Assert.True(back.Steps[0].InChat, "in chat");
            Assert.False(back.Steps[1].InChat, "and plain typing stays plain");
        }

        public static void TestAWaitIsJustAWait()
        {
            List<InputAction> plan = Plan(MacroStep.Wait(1500));
            Assert.Equal(1, plan.Count, "nothing sent");
            Assert.Equal(1500, plan[0].Ms, "only time passing");
        }

        // Played close to as long as the macro says it takes: the only extra
        // is the short settling gap after each key or click.
        public static void TestPlayingTakesAboutAsLongAsTheMacroSays()
        {
            Macro m = new Macro();
            m.Steps.Add(MacroStep.Key(0x45, 50));
            m.Steps.Add(MacroStep.Wait(1500));
            m.Steps.Add(MacroStep.Click(0.5, 0.5, false));
            int slept = 0;
            foreach (InputAction a in MacroPlan.For(m, Window))
                if (a.Kind == InputActionKind.Sleep) slept += a.Ms;
            Assert.True(slept >= m.DurationMs, "at least as long");
            Assert.True(slept <= m.DurationMs + 3 * MacroPlan.SETTLE_MS, "and not much longer");
        }

        static List<InputAction> Plan(MacroStep s)
        {
            Macro m = new Macro();
            m.Steps.Add(s);
            return MacroPlan.For(m, Window);
        }
    }
}
