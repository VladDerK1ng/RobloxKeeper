using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace RobloxKeeper.Tests
{
    // A script's run as a whole: the sandbox, stopping it however it is
    // written, mistakes on their lines, sharing the keyboard with the nudge,
    // the summary, and what it remembers between runs.
    static class ScriptRunTests
    {
        // Runs on a thread of its own, as the app runs it, and is stopped by
        // `stop` after `afterMs`. How long the stop took to be felt.
        static long StopsWithin(string code, int afterMs, Action<LuaRun> stop, out ScriptResult result)
        {
            FakeDesk d = new FakeDesk();
            d.RealTime = true;
            d.Pictures["x"] = "x.png";
            LuaRun run = new LuaRun("Runaway", code, d, ScriptTarget.OnScreen(), new FakeOutlet(), null);
            ScriptResult got = null;
            ManualResetEvent ended = new ManualResetEvent(false);
            Thread t = run.Start(delegate(ScriptResult r) { got = r; ended.Set(); });
            Thread.Sleep(afterMs);
            Stopwatch felt = Stopwatch.StartNew();
            stop(run);
            bool done = ended.WaitOne(3000);
            long ms = felt.ElapsedMilliseconds;
            if (!done) { t.Abort(); throw new Exception("it never stopped"); }
            t.Join(2000);
            result = got;
            return ms;
        }

        // ---------- the sandbox ----------

        public static void TestTheSandboxHasNoFilesNoProgramsNoLoading()
        {
            FakeDesk d = new FakeDesk();
            FakeOutlet o = new FakeOutlet();
            ScriptRig.Run("print(io, os.execute, os.remove, os.exit, require, dofile, loadfile, load, coroutine, debug)", d, o);
            Assert.Equal("nil\tnil\tnil\tnil\tnil\tnil\tnil\tnil\tnil\tnil", o.All, "none of them is there");
        }

        public static void TestTheSandboxStillHasPlainLua()
        {
            FakeDesk d = new FakeDesk();
            FakeOutlet o = new FakeOutlet();
            ScriptResult r = ScriptRig.Run(
                "local t = {3, 1, 2} table.sort(t) print(table.concat(t, ','), string.upper('ok'), math.floor(2.7), typeof(t), os.time() > 0)",
                d, o);
            Assert.Equal(null, r.Problem, "ran");
            Assert.Equal("1,2,3\tOK\t2\ttable\ttrue", o.All, "tables, text, maths, Lua's own type() as typeof(), the clock");
        }

        // ---------- stopping ----------

        public static void TestARunawayLoopStops()
        {
            ScriptResult r;
            long ms = StopsWithin("while true do end", 100, delegate(LuaRun run) { run.Stop(); }, out r);
            Assert.True(ms < 1000, "felt at once (" + ms + "ms)");
            Assert.Equal(null, r.Problem, "stopped on purpose is no problem");
        }

        public static void TestTheStopKeyStopsARunawayLoop()
        {
            ScriptResult r;
            long ms = StopsWithin("local n = 0 while true do n = n + 1 end", 100, delegate { MacroStop.All(); }, out r);
            Assert.True(ms < 1000, "the stop key is felt at once (" + ms + "ms)");
        }

        // pcall catches Lua's own errors; a stop is not one, so it can't be
        // caught and ignored.
        public static void TestPcallCannotCatchAStop()
        {
            ScriptResult r;
            long ms = StopsWithin("while true do pcall(function() while true do end end) end", 100, delegate(LuaRun run) { run.Stop(); }, out r);
            Assert.True(ms < 1000, "inside pcall too (" + ms + "ms)");
            ms = StopsWithin("while true do pcall(wait, 1) end", 100, delegate(LuaRun run) { run.Stop(); }, out r);
            Assert.True(ms < 1000, "and a wait inside pcall (" + ms + "ms)");
        }

        // Lua called back from inside a library function - a sort's compare,
        // a gsub's replacement - is not suspended by MoonSharp. It still stops.
        public static void TestARunawayLoopInsideASortOrGsubStops()
        {
            ScriptResult r;
            long ms = StopsWithin("table.sort({3, 2, 1}, function(a, b) while true do end end)", 100, delegate(LuaRun run) { run.Stop(); }, out r);
            Assert.True(ms < 1000, "in a sort (" + ms + "ms)");
            ms = StopsWithin("string.gsub('abc', '%w', function(c) while true do end end)", 100, delegate(LuaRun run) { run.Stop(); }, out r);
            Assert.True(ms < 1000, "in a gsub (" + ms + "ms)");
        }

        public static void TestAStopEndsAWaitAtOnce()
        {
            ScriptResult r;
            long ms = StopsWithin("wait(60)", 100, delegate(LuaRun run) { run.Stop(); }, out r);
            Assert.True(ms < 500, "not at the end of the minute (" + ms + "ms)");
            ms = StopsWithin("use(1) waitfor(\"x\", 60)", 100, delegate(LuaRun run) { run.Stop(); }, out r);
            Assert.True(ms < 1000, "or a picture wait (" + ms + "ms)");
        }

        public static void TestTheStopKeyPressedBeforeItStartedDoesNotStopIt()
        {
            MacroStop.All();
            FakeDesk d = new FakeDesk();
            ScriptResult r = ScriptRig.Run("press(\"e\")", d, new FakeOutlet());
            Assert.Equal(null, r.Problem, "ran");
            Assert.Equal("down E | up E", d.SaidText(), "and pressed");
        }

        // ---------- mistakes ----------

        public static void TestMistakesSayTheirLine()
        {
            FakeDesk d = new FakeDesk();
            Assert.Equal("Line 3: that calls something that isn't a function - check how its name is spelled",
                         ScriptRig.Run("x = 1\n\nfoo()", d, new FakeOutlet()).Problem, "calling nothing");
            Assert.Contains("Line 1:", ScriptRig.Run("x = = 1", d, new FakeOutlet()).Problem, "a typing mistake");
            Assert.Equal("Line 2: my own", ScriptRig.Run("wait(0)\nerror('my own')", d, new FakeOutlet()).Problem, "error() itself");
            Assert.Equal("Line 2: that looks inside something that has no value (nil)",
                         ScriptRig.Run("local t\nprint(t.x)", d, new FakeOutlet()).Problem, "nil");
            Assert.Contains("Line 2:", ScriptRig.Run("local function f()\n  press('nokey')\nend\nf()", d, new FakeOutlet()).Problem, "on the line inside the function");
        }

        public static void TestAMistakeLetsGoOfWhatWasHeld()
        {
            FakeDesk d = new FakeDesk();
            ScriptResult r = ScriptRig.Run("keydown(\"w\")\nfoo()", d, new FakeOutlet());
            Assert.Contains("Line 2:", r.Problem, "the mistake");
            Assert.Equal("down W | up W", d.SaidText(), "W let go");
            Assert.False(d.Keyboard, "keyboard given back");
        }

        // ---------- sharing the keyboard ----------

        public static void TestALongWaitGivesTheKeyboardUpAndThePressAfterTakesItBack()
        {
            FakeDesk d = new FakeDesk();
            ScriptRig.Run("press(\"e\") press(\"e\") wait(1) press(\"e\") wait(0.1) press(\"e\")", d, new FakeOutlet());
            Assert.Equal("begin,take,front 11,give,take,front 11,give,end", string.Join(",", d.Log.ToArray()),
                         "kept across presses and a short wait, given up for the long one, taken back with the window in front");
        }

        public static void TestTheNudgeIsLetInDuringAWaitAndHeldKeysComeBack()
        {
            FakeDesk d = new FakeDesk();
            bool nudge = false;
            d.Nudge = delegate { return nudge; };
            d.Napped = delegate { nudge = d.Now >= 1000 && d.Now < 1100; };
            ScriptResult r = ScriptRig.Run("keydown(\"w\") wait(5) keyup(\"w\")", d, new FakeOutlet());
            Assert.Equal(null, r.Problem, "ran");
            Assert.Equal("down W | up W | down W | up W", d.SaidText(), "let go of for the nudge, pressed again after");
            Assert.Equal("begin,take,front 11,give,take,front 11,give,end", string.Join(",", d.Log.ToArray()), "given up and taken back");
            Assert.True(d.Now >= 5000, "the wait still lasted its time");
        }

        public static void TestALongWaitWithAKeyHeldKeepsTheKeyboard()
        {
            FakeDesk d = new FakeDesk();
            ScriptRig.Run("keydown(\"w\") wait(3) keyup(\"w\")", d, new FakeOutlet());
            Assert.Equal("down W | up W", d.SaidText(), "walked the whole time");
            Assert.Equal(1, d.Takes, "never let go of in between");
        }

        public static void TestTheNudgeIsLetInDuringALongPress()
        {
            FakeDesk d = new FakeDesk();
            bool nudge = false;
            d.Nudge = delegate { return nudge; };
            d.Napped = delegate { nudge = d.Now >= 2000 && d.Now < 2100; };
            ScriptRig.Run("press(\"w\", 10)", d, new FakeOutlet());
            Assert.Equal("down W | up W | down W | up W", d.SaidText(), "let go of part-way and held again");
            Assert.True(d.Takes >= 2, "the keyboard was handed over");
        }

        // A loop of nothing but looks, with no wait, still lets the nudge in:
        // every function makes way first.
        public static void TestLooksWithNoWaitStillLetTheNudgeIn()
        {
            FakeDesk d = new FakeDesk();
            d.Nudge = delegate { return d.Sent.Count >= 2; };
            ScriptRig.Run("press(\"e\") read() read() read()", d, new FakeOutlet());
            Assert.Equal("begin,take,front 11,give,read,read,read,end", string.Join(",", d.Log.ToArray()), "given up before the first look");
        }

        public static void TestLongTypingMakesWayForTheNudgePartWay()
        {
            FakeDesk d = new FakeDesk();
            // Wants in once 30 letters are typed, until it has been let in.
            d.Nudge = delegate { return d.Sent.Count >= 30 && d.Gives == 0; };
            ScriptResult r = ScriptRig.Run("type(string.rep(\"a\", 100))", d, new FakeOutlet());
            Assert.Equal(null, r.Problem, "typed");
            Assert.Equal(100, d.Sent.Count, "every letter");
            Assert.True(d.Takes >= 2, "and the nudge got in part-way");
            Assert.Contains("200 letters", ScriptRig.Run("chat(string.rep(\"a\", 201))", new FakeDesk(), new FakeOutlet()).Problem, "chat is kept to what Roblox takes");
        }

        public static void TestCodeThatCallsNothingGivesAnIdleKeyboardUp()
        {
            FakeDesk d = new FakeDesk();
            d.RealTime = true;
            ScriptResult r = ScriptRig.Run("press(\"e\") local t = os.clock() while os.clock() - t < 0.6 do end press(\"e\")", d, new FakeOutlet());
            Assert.Equal(null, r.Problem, "ran");
            Assert.Equal(2, d.Takes, "given up while it only worked things out, taken again to press");
        }

        // The real gate, with a real nudge waiting on another thread: the
        // script makes way, the nudge gets in, the script carries on.
        public static void TestARealNudgeGetsInWhileAScriptHoldsAKey()
        {
            GateDesk d = new GateDesk();
            FakeOutlet o = new FakeOutlet();
            LuaRun run = new LuaRun("Walk", "keydown(\"w\") wait(3) keyup(\"w\")", d, ScriptTarget.OnScreen(), o, null);
            ScriptResult result = null;
            ManualResetEvent ended = new ManualResetEvent(false);
            Thread t = run.Start(delegate(ScriptResult r) { result = r; ended.Set(); });
            try
            {
                Assert.True(Within(2000, delegate { return d.Keyboard; }), "the script has the keyboard");
                bool got = false;
                Stopwatch took = Stopwatch.StartNew();
                Thread nudge = new Thread(delegate()
                {
                    got = FocusGate.TryEnter(5000, true);
                    if (got) { Thread.Sleep(50); FocusGate.Exit(); }
                });
                nudge.IsBackground = true;
                nudge.Start();
                Assert.True(nudge.Join(4000), "the nudge finished");
                Assert.True(got, "the nudge got in");
                Assert.True(took.ElapsedMilliseconds < 1000, "at once (" + took.ElapsedMilliseconds + "ms)");
                Assert.True(ended.WaitOne(5000), "the script ended");
                Assert.Equal(null, result.Problem, "and finished");
                Assert.Equal("down W | up W | down W | up W", d.SaidText(), "W let go for the nudge and held again after");
            }
            finally
            {
                run.Stop();
                t.Join(3000);
            }
        }

        static bool Within(int ms, Func<bool> what)
        {
            Stopwatch c = Stopwatch.StartNew();
            while (c.ElapsedMilliseconds < ms) { if (what()) return true; Thread.Sleep(5); }
            return what();
        }

        // FakeDesk with the real FocusGate, real time and the real nudge flag.
        class GateDesk : FakeDesk, IScriptDesk
        {
            public GateDesk() { RealTime = true; Nudge = delegate { return FocusGate.NudgeWanted; }; Waiting = delegate { return FocusGate.Wanted; }; }

            bool IScriptDesk.TakeKeyboard(Func<bool> stop, out string problem)
            {
                problem = null;
                if (!FocusGate.TryEnter(5000, false, stop)) return false;
                Keyboard = true;
                Takes++;
                return true;
            }

            void IScriptDesk.GiveKeyboard()
            {
                Keyboard = false;
                Gives++;
                FocusGate.Exit();
            }
        }

        // ---------- counting, the summary, remembering ----------

        public static void TestCountersMakeTheSummary()
        {
            FakeDesk d = new FakeDesk();
            ScriptResult r = ScriptRig.Run("count(\"sold\") count(\"collected\", 200) count(\"sold\") count(\"collected\", 10) print(count(\"sold\"))", d, new FakeOutlet());
            Assert.Equal("Test ran 0s: sold 3, collected 210.", r.Summary, "in the order first counted");
        }

        public static void TestTheSummarySaysHowLongAndWhy()
        {
            List<KeyValuePair<string, double>> c = new List<KeyValuePair<string, double>>();
            Assert.Equal("Farm loop ran 12s.", ScriptSummary.Line("Farm loop", 12400, c, null), "no counters");
            c.Add(new KeyValuePair<string, double>("sold", 34));
            c.Add(new KeyValuePair<string, double>("collected", 210));
            Assert.Equal("Farm loop ran 1h 12m: sold 34, collected 210.", ScriptSummary.Line("Farm loop", (72 * 60 + 5) * 1000L, c, null), "the example");
            Assert.Equal("4m 5s", ScriptSummary.Duration(245000), "minutes");
            Assert.Equal("2h", ScriptSummary.Duration(7200000), "whole hours");
            Assert.Equal("X ran 3s. It stopped: Line 4: oops.", ScriptSummary.Line("X", 3000, null, "Line 4: oops"), "and why it stopped");
            Assert.Equal("X ran 3s. Stopped - another window came to the front.", ScriptSummary.Line("X", 3000, null, MacroPlayer.STOPPED), "said once");
        }

        public static void TestRememberAndRecallLastAcrossRuns()
        {
            string dir = ScriptRig.TempDir();
            try
            {
                ScriptStore store = new ScriptStore(dir);
                store.Create("Test", "");
                FakeDesk d = new FakeDesk();
                ScriptRig.Run("remember(\"runs\", (recall(\"runs\") or 0) + 1) remember(\"who\", \"me\") remember(\"on\", true) remember(\"gone\", 1) remember(\"gone\", nil)",
                              d, new FakeOutlet(), ScriptRig.Window(d), store);
                FakeOutlet o = new FakeOutlet();
                ScriptRig.Run("remember(\"runs\", recall(\"runs\") + 1) print(recall(\"runs\"), recall(\"who\"), recall(\"on\"), recall(\"gone\"))",
                              d, o, ScriptRig.Window(d), new ScriptStore(dir));
                Assert.Equal("2\tme\ttrue\tnil", o.All, "kept between runs, and after a restart");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestRememberRefusesWhatCantBeKept()
        {
            ScriptResult r = ScriptRig.Run("remember(\"t\", {})", new FakeDesk(), new FakeOutlet());
            Assert.Contains("numbers, text and true/false", r.Problem, "says what it keeps");
        }
    }
}
