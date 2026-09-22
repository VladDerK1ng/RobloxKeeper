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

        // A safety net over the rule above: however they arrive, no more than
        // this many wait. More are turned away, and the caller says so.
        public static void TestTheQueueNeverBacksUpIntoMinutes()
        {
            MacroQueue q = new MacroQueue();
            for (int i = 0; i < MacroQueue.MAX_WAITING; i++) Assert.True(q.Offer(Job("m" + i)), "room for " + i);
            Assert.False(q.Offer(Job("too many")), "turned away");
            Assert.Equal(MacroQueue.MAX_WAITING, q.Waiting, "still five");
        }

        // A rule on every client queues one each. With more clients than the
        // queue had room for, the rest were turned away every time - a
        // collect-every-five-minutes rule that never reached clients 6 to 8.
        public static void TestARuleOnEveryClientReachesEveryClient()
        {
            MacroQueue q = new MacroQueue();
            for (int pid = 1; pid <= 8; pid++)
            {
                MacroJob j = Job("Collect");
                j.Pid = pid;
                Assert.True(q.Offer(j), "client " + pid);
            }
            Assert.Equal(8, q.Waiting, "all eight waiting their turn");
        }

        // What actually keeps the queue from backing up: the same macro for
        // the same client is never waiting twice.
        public static void TestTheSameMacroForTheSameClientIsNeverWaitingTwice()
        {
            MacroQueue q = new MacroQueue();
            Assert.True(q.Offer(Job("Buy egg")), "first");
            Assert.False(q.Offer(Job("Buy egg")), "already waiting for that client");
            MacroJob other = Job("Buy egg");
            other.Pid = 200;
            Assert.True(q.Offer(other), "another client's turn is its own");
            Assert.True(q.Offer(Job("Sell egg")), "and another macro for the first client");
            q.Take();
            Assert.True(q.Offer(Job("Buy egg")), "once it has been taken to play, it may wait again");
        }

        // ---------- held keys ----------

        // A key rule on Ctrl+F1 fires the moment the keys go down, with
        // Ctrl still held - so the macro's E arrived in the game as Ctrl+E.
        // It waits for your hands to come off the keys first.
        public static void TestPlayingWaitsForHeldModifiersToBeLetGo()
        {
            int polls = 0, slept = 0;
            bool ok = MacroPlayer.WaitForKeysUp(
                delegate(int vk) { return polls++ < 3 * MacroPlayer.Modifiers.Length; },   // held for three looks
                delegate(int ms) { slept += ms; }, 2000);
            Assert.True(ok, "let go, so it plays");
            Assert.True(slept > 0 && slept < 2000, "after waiting a moment, not the whole time: " + slept + "ms");
        }

        public static void TestHeldModifiersThatStayHeldStopTheMacro()
        {
            int slept = 0;
            bool ok = MacroPlayer.WaitForKeysUp(delegate(int vk) { return vk == 0x11; },   // Ctrl, for ever
                                                delegate(int ms) { slept += ms; }, 2000);
            Assert.False(ok, "not played with Ctrl down");
            Assert.True(slept >= 2000, "after giving it two seconds");
        }

        public static void TestNothingHeldPlaysAtOnce()
        {
            int slept = 0;
            Assert.True(MacroPlayer.WaitForKeysUp(delegate(int vk) { return false; }, delegate(int ms) { slept += ms; }, 2000), "plays");
            Assert.Equal(0, slept, "without waiting");
        }

        // ---------- typing with real keys ----------

        // What VkKeyScan says for a character on a US keyboard: the key in the
        // low byte, Shift/Ctrl/Alt in the high byte, -1 for no key.
        public static void TestACharacterWithAKeyIsTypedWithThatKey()
        {
            Assert.Equal("down A|up A", Keys(MacroPlayer.KeysFor(0x0041)), "a is the A key");
            Assert.Equal("down Shift|down G|up G|up Shift", Keys(MacroPlayer.KeysFor(0x0147)), "G is Shift and the G key");
            Assert.Equal("down /|up /", Keys(MacroPlayer.KeysFor(0x00BF)), "/ is its own key");
        }

        // No key for it, or one that needs Ctrl or Alt (AltGr on many
        // layouts, where Ctrl or Alt would mean something else to the game):
        // sent as the character itself.
        public static void TestACharacterWithNoPlainKeyIsSentAsItself()
        {
            Assert.Equal(null, MacroPlayer.KeysFor(-1), "no key on this keyboard");
            Assert.Equal(null, MacroPlayer.KeysFor(0x0651), "AltGr+Q, @ on a German keyboard");
        }

        static string Keys(List<KeyValuePair<byte, bool>> keys)
        {
            if (keys == null) return null;
            List<string> s = new List<string>();
            foreach (KeyValuePair<byte, bool> k in keys) s.Add((k.Value ? "down " : "up ") + MacroKeys.Name(k.Key));
            return string.Join("|", s.ToArray());
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
