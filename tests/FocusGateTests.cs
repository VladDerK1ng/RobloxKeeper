using System;
using System.Diagnostics;
using System.Threading;

namespace RobloxKeeper.Tests
{
    // Waiting for the keyboard: the nudge told apart from a macro, a macro
    // about to give up let in, and a stop that ends the wait at once. Every
    // test holds the keyboard on a thread of its own, so nothing here ever
    // gets in and sends real keys.
    static class FocusGateTests
    {
        // Holds the gate on its own thread - a lock is its thread's - until
        // Dispose.
        class Holder : IDisposable
        {
            readonly ManualResetEvent held = new ManualResetEvent(false);
            readonly ManualResetEvent release = new ManualResetEvent(false);
            readonly Thread t;

            public Holder()
            {
                t = new Thread(delegate()
                {
                    FocusGate.TryEnter(0);
                    held.Set();
                    release.WaitOne();
                    FocusGate.Exit();
                });
                t.IsBackground = true;
                t.Start();
                held.WaitOne();
            }

            public void Dispose()
            {
                release.Set();
                t.Join();
            }
        }

        static Thread Waiter(int ms, bool nudge, bool[] got)
        {
            Thread t = new Thread(delegate()
            {
                got[0] = FocusGate.TryEnter(ms, nudge);
                if (got[0]) FocusGate.Exit();
            });
            t.IsBackground = true;
            t.Start();
            return t;
        }

        static bool Within(int ms, Func<bool> what)
        {
            Stopwatch c = Stopwatch.StartNew();
            while (c.ElapsedMilliseconds < ms) { if (what()) return true; Thread.Sleep(5); }
            return what();
        }

        // The nudge is let in at once; a macro waiting only once the one in
        // front has had its turn. Two macros that each made way at once
        // swapped after every step, letting go of every held key each time.
        public static void TestTheNudgeIsToldApartFromAMacroWaiting()
        {
            bool[] nudgeGot = new bool[1], macroGot = new bool[1];
            Thread nudge, macro;
            using (new Holder())
            {
                Assert.False(FocusGate.Wanted, "nobody waiting yet");
                nudge = Waiter(5000, true, nudgeGot);
                Assert.True(Within(2000, delegate { return FocusGate.NudgeWanted; }), "the nudge is waiting");
                Assert.False(FocusGate.MacroWanted, "and is no macro");

                macro = Waiter(5000, false, macroGot);
                Assert.True(Within(2000, delegate { return FocusGate.MacroWanted; }), "a macro is waiting too");
                Assert.True(FocusGate.NudgeWanted, "the nudge still is");
                Assert.False(FocusGate.MacroGivingUp, "and the macro is nowhere near giving up");
            }
            nudge.Join();
            macro.Join();
            Assert.True(nudgeGot[0] && macroGot[0], "both get in once it is free");
            Assert.False(FocusGate.Wanted || FocusGate.NudgeWanted || FocusGate.MacroWanted, "nobody waiting after");
        }

        // A macro gives up after fifteen seconds; the one in front may have a
        // turn of ten minutes. Two thirds of the way, it is let in as the
        // nudge would be.
        public static void TestAMacroAboutToGiveUpIsLetInLikeTheNudge()
        {
            bool[] got = new bool[1];
            Thread t;
            using (new Holder())
            {
                t = Waiter(900, false, got);
                Assert.True(Within(2000, delegate { return FocusGate.MacroWanted; }), "waiting");
                Assert.False(FocusGate.MacroGivingUp, "not giving up yet");
                Assert.True(Within(2000, delegate { return FocusGate.MacroGivingUp; }), "then close to it");
                t.Join();
            }
            Assert.False(got[0], "it gave up, held out the whole time");
            Assert.False(FocusGate.MacroGivingUp, "and is not waiting any more");
        }

        // A stop that comes while it waits ends the wait now, not up to
        // fifteen seconds later.
        public static void TestAStopEndsTheWaitForTheKeyboardAtOnce()
        {
            using (new Holder())
            {
                Stopwatch c = Stopwatch.StartNew();
                bool got = FocusGate.TryEnter(15000, false, delegate { return c.ElapsedMilliseconds >= 100; });
                Assert.False(got, "never got in");
                Assert.True(c.ElapsedMilliseconds < 2000, "back at once, not after fifteen seconds");
            }
        }

        // Said as stopped, not as "something else was using the keyboard" -
        // on the whole screen and on a client alike.
        public static void TestAStopWhileWaitingForTheKeyboardIsSaidAsStopped()
        {
            using (new Holder())
            {
                Macro m = new Macro();
                m.Name = "Farm";
                m.Steps.Add(MacroStep.Key(0x45, 50));

                Stopwatch c = Stopwatch.StartNew();
                PlayOptions o = new PlayOptions();
                o.Stop = delegate { return c.ElapsedMilliseconds >= 100; };
                ScreenWorld world = new ScreenWorld(o, MacroStop.Epoch, new System.Drawing.Rectangle(0, 0, 1920, 1080));
                string problem;
                RunEnd end = MacroPlayer.PlayOnScreenTurn(new MacroRunner(m), world, o, out problem);
                Assert.Equal(RunEnd.Cancelled, end, "on the screen: stopped");
                Assert.Equal(MacroPlayer.CANCELLED, problem, "and said so");
                Assert.True(c.ElapsedMilliseconds < 2000, "at once");

                c = Stopwatch.StartNew();
                end = MacroPlayer.PlayTurn(new MacroRunner(m), new IntPtr(1), o, out problem);
                Assert.Equal(RunEnd.Cancelled, end, "on a client: stopped");
                Assert.Equal(MacroPlayer.CANCELLED, problem, "and said so");
                Assert.True(c.ElapsedMilliseconds < 2000, "at once");

                // The stop key, pressed while a macro no one can mark stopped
                // - a watcher's - waited for its turn.
                c = Stopwatch.StartNew();
                PlayOptions plain = new PlayOptions();
                Thread press = new Thread(delegate() { Thread.Sleep(100); MacroStop.All(); });
                press.Start();
                end = MacroPlayer.PlayTurn(new MacroRunner(m), new IntPtr(1), plain, out problem);
                press.Join();
                Assert.Equal(RunEnd.Cancelled, end, "the stop key stops it too");
                Assert.True(c.ElapsedMilliseconds < 2000, "at once");
            }
        }
    }
}
