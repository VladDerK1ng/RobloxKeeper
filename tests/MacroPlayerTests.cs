using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace RobloxKeeper.Tests
{
    // The rules around playing: one thing at a time in front, never a key
    // left down, and a queue that cannot grow into minutes of held focus.
    static class MacroPlayerTests
    {
        // A nudge and a macro must never both be moving the foreground window
        // about - each would send its keys into the other's client.
        public static void TestOnlyOneThingUsesTheKeyboardAtATime()
        {
            Assert.True(FocusGate.TryEnter(0), "free to start with");
            try
            {
                bool other = true;
                Thread t = new Thread(delegate() { other = FocusGate.TryEnter(0); if (other) FocusGate.Exit(); });
                t.Start();
                t.Join();
                Assert.False(other, "a second one has to wait");
            }
            finally { FocusGate.Exit(); }

            bool after = false;
            Thread u = new Thread(delegate() { after = FocusGate.TryEnter(0); if (after) FocusGate.Exit(); });
            u.Start();
            u.Join();
            Assert.True(after, "and gets it once the first is done");
        }

        // Stopped part-way - someone clicked another window - whatever was
        // held down so far is let go, not left walking the character forward.
        public static void TestStoppingPartWayLetsGoOfWhatWasHeld()
        {
            Macro m = new Macro();
            m.Steps.Add(MacroStep.KeyDown(0x57));      // W
            m.Steps.Add(MacroStep.KeyDown(0x10));      // Shift
            m.Steps.Add(MacroStep.Wait(2000));
            m.Steps.Add(MacroStep.KeyUp(0x10));
            List<InputAction> plan = MacroPlan.For(m, new Rectangle(0, 0, 800, 600));

            Assert.Equal(2, MacroPlayer.StillHeld(plan, 3).Count, "W and Shift, stopped in the wait");
            List<byte> late = MacroPlayer.StillHeld(plan, 4);
            Assert.Equal(1, late.Count, "Shift already let go");
            Assert.Equal((byte)0x57, late[0], "only W left");
            Assert.Equal(0, MacroPlayer.StillHeld(plan, plan.Count).Count, "nothing at the end");
        }

        public static void TestMacrosFromWatchersWaitTheirTurn()
        {
            MacroQueue q = new MacroQueue();
            Assert.True(q.Offer(Job("one")), "first");
            Assert.True(q.Offer(Job("two")), "second");
            Assert.Equal("one", q.Take().Macro.Name, "in the order they came");
            Assert.Equal("two", q.Take().Macro.Name, "then the next");
            Assert.Equal(null, q.Take(), "then nothing");
        }

        // Five waiting is already most of a minute of held focus. More are
        // turned away, and the caller says so.
        public static void TestTheQueueNeverBacksUpIntoMinutes()
        {
            MacroQueue q = new MacroQueue();
            for (int i = 0; i < MacroQueue.MAX_WAITING; i++) Assert.True(q.Offer(Job("m" + i)), "room for " + i);
            Assert.False(q.Offer(Job("too many")), "turned away");
            Assert.Equal(MacroQueue.MAX_WAITING, q.Waiting, "still five");
        }

        static MacroJob Job(string name)
        {
            Macro m = new Macro();
            m.Name = name;
            m.Steps.Add(MacroStep.Wait(10));
            MacroJob j = new MacroJob();
            j.Macro = m;
            j.Pid = 100;
            j.Label = "Client 1";
            return j;
        }
    }
}
