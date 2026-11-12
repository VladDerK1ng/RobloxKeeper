using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace RobloxKeeper.Tests
{
    // What a script must never be able to do to the app or the PC: keep the
    // window too busy for the nudge, end the app, press the Windows key,
    // leave a key held after its window closed, or eat all the memory - and
    // what a script shared by someone else may do before it is trusted.
    static class ScriptSafetyTests
    {
        // On a thread of its own, as the app runs it - with the bigger stack
        // the app gives it, which the depth check counts on.
        static ScriptResult OnItsThread(string code, FakeDesk d, FakeOutlet o, long budget)
        {
            LuaRun run = new LuaRun("Crash", code, d, ScriptRig.Window(d), o, null);
            if (budget > 0) run.MemoryBudget = budget;
            ScriptResult got = null;
            ManualResetEvent ended = new ManualResetEvent(false);
            Thread t = run.Start(delegate(ScriptResult r) { got = r; ended.Set(); });
            if (!ended.WaitOne(60000)) { run.Stop(); ended.WaitOne(5000); throw new Exception("it never ended"); }
            t.Join(2000);
            return got;
        }

        static string Rep(string s, int n)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }

        static void StartsWith(string start, string actual, string what)
        {
            Assert.True(actual != null && actual.StartsWith(start, StringComparison.Ordinal), what + " (it said: " + (actual ?? "nothing") + ")");
        }

        // ---------- the window stays free ----------

        public static void TestASayLoopIsCappedAndSaysSoOnce()
        {
            FakeDesk d = new FakeDesk();
            FakeOutlet o = new FakeOutlet();
            ScriptResult r = ScriptRig.Run("for i = 1, 1000 do say('x' .. i) end wait(11) say('later')", d, o);
            Assert.Equal(null, r.Problem, "ran");
            Assert.Equal(LuaRun.SAY_MAX + 1, o.Said.Count, "the first ones, then one more once the time had passed");
            Assert.Equal("later", o.Said[o.Said.Count - 1], "said after the wait");
            int told = 0;
            foreach (string p in o.Printed) if (p.StartsWith("say: left out", StringComparison.Ordinal)) told++;
            Assert.Equal(1, told, "that lines were left out is said once");
        }

        public static void TestALongLineIsCut()
        {
            FakeDesk d = new FakeDesk();
            FakeOutlet o = new FakeOutlet();
            ScriptRig.Run("print(string.rep('x', 5000)) say(string.rep('y', 5000))", d, o);
            Assert.Equal(LuaRun.LINE_MAX + 3, o.Printed[0].Length, "a printed line is cut");
            Assert.Equal(LuaRun.LINE_MAX + 3, o.Said[0].Length, "and a said one");
        }

        // However much a script says, the window is asked once, and never
        // again sooner than EVERY_MS after it finished the last batch.
        public static void TestTheWindowIsAskedOnceForAFlood()
        {
            long now = 1000000;
            int posts = 0;
            List<int> laters = new List<int>();
            ScriptFlushGate g = new ScriptFlushGate(delegate { posts++; }, delegate { return now; }, delegate(int ms) { laters.Add(ms); });

            for (int i = 0; i < 10000; i++) g.Changed();
            Assert.Equal(1, posts, "one hand-over asked for");
            Assert.Equal(0, laters.Count, "at once - none was handed over lately");

            g.Begin();
            for (int i = 0; i < 500; i++) g.Changed();
            Assert.Equal(1, posts, "nothing more asked for while it is handed over");
            now += 30;
            g.End();
            Assert.Equal(1, laters.Count, "what came in meanwhile is asked for once");
            Assert.Equal(ScriptFlushGate.EVERY_MS, laters[0], "a while after, not at once");
            g.Changed();
            Assert.Equal(1, laters.Count, "and not again while that is waiting");

            g.Begin();
            g.End();
            now += 30;
            g.Changed();
            Assert.Equal(1, posts, "soon after a hand-over: not at once");
            Assert.Equal(ScriptFlushGate.EVERY_MS - 30, laters[1], "but when the gap is up");

            g.Begin();
            g.End();
            now += ScriptFlushGate.EVERY_MS + 1;
            g.Changed();
            Assert.Equal(2, posts, "long after: at once");
        }

        // ---------- nothing a script does ends the app ----------

        public static void TestJsonAndDynamicAreGone()
        {
            FakeDesk d = new FakeDesk();
            FakeOutlet o = new FakeOutlet();
            ScriptRig.Run("print(json, dynamic)", d, o);
            Assert.Equal("nil\tnil", o.All, "json - which follows a table holding itself forever - and dynamic are not there");
            // The script that ended the app: now a plain mistake on its line.
            ScriptResult r = OnItsThread("local t = {}\nt.t = t\nprint(json.serialize(t))", d, new FakeOutlet(), 0);
            StartsWith("Line 3: that looks inside something that has no value", r.Problem, "it ends with a line");
        }

        // Both used to run the thread out of stack, which ends the whole app
        // with no way to catch it. They run on the script's own thread here,
        // as in the app; each ended with a line in a separate process first.
        public static void TestASortThatSortsItselfEndsWithALine()
        {
            FakeDesk d = new FakeDesk();
            ScriptResult r = OnItsThread("local function f(a, b) table.sort({2, 1}, f) return a < b end\ntable.sort({2, 1}, f)", d, new FakeOutlet(), 0);
            StartsWith("Line 1: that goes too deep", r.Problem, "it ends, saying why and where");
        }

        public static void TestGsubAndPrintCallingThemselvesEndWithALine()
        {
            ScriptResult r = OnItsThread("local function f(c) string.gsub('a', 'a', f) return c end string.gsub('a', 'a', f)", new FakeDesk(), new FakeOutlet(), 0);
            StartsWith("Line 1: that goes too deep", r.Problem, "gsub");
            r = OnItsThread("local m = {}\nm.__tostring = function(t) print(setmetatable({}, m)) return 'x' end\nprint(setmetatable({}, m))", new FakeDesk(), new FakeOutlet(), 0);
            StartsWith("Line 2: that goes too deep", r.Problem, "print of something whose tostring prints");
            r = OnItsThread("local function h(e) xpcall(error, h) return e end xpcall(error, h)", new FakeDesk(), new FakeOutlet(), 0);
            StartsWith("Line 1: that goes too deep", r.Problem, "xpcall");
        }

        // pcall can't swallow it and carry on.
        public static void TestTooDeepCantBeCaughtByPcall()
        {
            FakeDesk d = new FakeDesk();
            FakeOutlet o = new FakeOutlet();
            ScriptResult r = OnItsThread("local function f(a, b) table.sort({2, 1}, f) return a < b end\nprint(pcall(table.sort, {2, 1}, f))\nprint('carried on')", d, o, 0);
            StartsWith("Line 1: that goes too deep", r.Problem, "it still ends");
            Assert.False(o.All.Contains("carried on"), "and goes no further");
        }

        // Ordinary sorting and gsub, with functions, still work.
        public static void TestSortAndGsubWithFunctionsStillWork()
        {
            FakeDesk d = new FakeDesk();
            FakeOutlet o = new FakeOutlet();
            ScriptResult r = ScriptRig.Run(
                "local t = {} for i = 1, 500 do t[i] = (i * 37) % 500 end table.sort(t, function(a, b) return a > b end)\n" +
                "print(t[1], t[500], (string.gsub('abc', '%w', function(c) return c:upper() end)), ('hello'):gsub('l', 'L'))", d, o);
            Assert.Equal(null, r.Problem, "ran");
            Assert.Equal("499\t0\tABC\theLLo\t2", o.All, "sorted, replaced");
        }

        // The reader goes as deep as the code does: code built to go deeper
        // than the thread's stack is refused before it is read.
        public static void TestCodeNestedTooDeepIsRefusedBeforeItIsRead()
        {
            FakeDesk d = new FakeDesk();
            ScriptResult r = ScriptRig.Run("print(1)\nx = " + Rep("(", 300) + "1" + Rep(")", 300), d, new FakeOutlet());
            StartsWith("Line 2: too many brackets or blocks", r.Problem, "brackets");
            r = ScriptRig.Run(Rep("do ", 300) + Rep("end ", 300), d, new FakeOutlet());
            StartsWith("Line 1: too many brackets or blocks", r.Problem, "blocks");
            string[] chains = { "x = 1" + Rep(" + 1", 6000), "x = y" + Rep(".a", 6000), "x = f" + Rep("()", 6000),
                                "x = " + Rep("- ", 6000) + "1", "x = 1" + Rep(" ~= 1", 6000), "x = 1" + Rep(" + function() end", 6000) };
            foreach (string c in chains)
            {
                r = ScriptRig.Run(c, d, new FakeOutlet());
                StartsWith("Line 1: this is too long to read in one go", r.Problem, "a chain: " + c.Substring(0, 20));
            }
        }

        public static void TestTheShapeCheckLetsRealScriptsThrough()
        {
            for (int i = 0; i < ScriptExamples.Names.Length; i++)
                Assert.Equal(null, LuaShape.Problem(ScriptExamples.Code(i)), "the example " + ScriptExamples.Names[i]);
            Assert.Equal(null, LuaShape.Problem(Rep("press(\"e\")\nwait(0.1)\nclick(50, 20)\n", 20000)), "a long macro made into a script");
            Assert.Equal(null, LuaShape.Problem("local t = {" + Rep("1, -2, 'x', {a = 1}, ", 20000) + "}"), "a long table");
            Assert.Equal(null, LuaShape.Problem(Rep("if x then ", 50) + "print(a.b.c:d(1 + 2 * 3) .. 'x' .. [[y]])" + Rep(" end", 50)), "nested a little");
            Assert.Equal(null, LuaShape.Problem("-- " + Rep("(", 1000) + "\nlocal s = '" + Rep("((", 1000) + "' --[[ " + Rep("{", 1000) + " ]]"), "brackets in comments and text are not counted");
        }

        // Lua calling itself forever never touches the thread's stack, but
        // got slower and slower and never ended by itself.
        public static void TestALuaFunctionCallingItselfForeverEnds()
        {
            System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
            ScriptResult r = OnItsThread("local function f(n)\nreturn 1 + f(n + 1)\nend\nf(1)", new FakeDesk(), new FakeOutlet(), 0);
            StartsWith("Line ", r.Problem, "it ends, saying where");
            Assert.Contains("that goes too deep - a function keeps calling itself", r.Problem, "and why");
            Assert.True(took.ElapsedMilliseconds < 20000, "soon (" + took.ElapsedMilliseconds + "ms)");

            // The same through a metatable: looking inside, comparing.
            r = OnItsThread("local m = {}\nm.__index = function(t, k) return setmetatable({}, m)[k] end\nprint(setmetatable({}, m).x)", new FakeDesk(), new FakeOutlet(), 0);
            Assert.Contains("that goes too deep", r.Problem ?? "", "__index");
            r = OnItsThread("local m = {}\nm.__lt = function(a, b) return setmetatable({}, m) < setmetatable({}, m) end\nprint(setmetatable({}, m) < setmetatable({}, m))", new FakeDesk(), new FakeOutlet(), 0);
            Assert.Contains("that goes too deep", r.Problem ?? "", "__lt");

            FakeOutlet o = new FakeOutlet();
            r = ScriptRig.Run("local function f(n) if n == 0 then return 0 end return 1 + f(n - 1) end print(f(500))", new FakeDesk(), o);
            Assert.Equal(null, r.Problem, "a function calling itself a few hundred times is fine");
            Assert.Equal("500", o.All, "and gets there");
        }

        // ---------- the Windows keys ----------

        public static void TestTheWindowsKeysCantBePressed()
        {
            string[] tries = { "press('key 91')", "keydown('lwin')", "press('rwin')", "keydown('key 92')", "press('menu')", "press('key 93')", "keyup('win')" };
            foreach (string code in tries)
            {
                FakeDesk d = new FakeDesk();
                ScriptResult r = ScriptRig.Run(code + " press('r')", d, new FakeOutlet());
                Assert.Equal("Line 1: " + ScriptKeys.SYSTEM_REFUSED, r.Problem, code);
                Assert.Equal(0, d.Sent.Count, code + ": nothing sent");
            }
        }

        static bool SentEsc(FakeDesk d)
        {
            foreach (KeyValuePair<string, InputAction> s in d.Sent)
                if ((s.Value.Kind == InputActionKind.KeyDown || s.Value.Kind == InputActionKind.KeyUp) && s.Value.Vk == 0x1B) return true;
                else if (s.Value.Kind == InputActionKind.Char && s.Value.Char == '\u001b') return true;
            return false;
        }

        // Ctrl+Esc opens Start as the Windows key does, and Ctrl+Shift+Esc
        // Task Manager. On a Roblox window the front-window check stops
        // anything after it; on the screen nothing would.
        public static void TestCtrlEscCantBePressedOnTheScreen()
        {
            string[] tries =
            {
                "keydown('ctrl') press('esc')", "keydown('lctrl') keydown('esc')", "keydown('rctrl') press('key 27')",
                "keydown('ctrl') keydown('shift') press('esc')", "keydown('ctrl') type('a\\27')", "keydown('ctrl') wait(1) press('escape')"
            };
            foreach (string code in tries)
            {
                FakeDesk d = new FakeDesk();
                ScriptResult r = ScriptRig.Run("use('screen') " + code + " type('cmd') press('enter')", d, new FakeOutlet());
                Assert.Equal("Line 1: " + ScriptKeys.CTRL_ESC_REFUSED, r.Problem, code);
                Assert.False(SentEsc(d), code + ": no Esc sent");
            }

            // Ctrl let go of for the nudge part-way through a wait, and
            // pressed again after: still held as far as Esc goes.
            FakeDesk nd = new FakeDesk();
            nd.Nudge = delegate { return nd.Now >= 200 && nd.Now < 600; };
            ScriptResult nr = ScriptRig.Run("use('screen') keydown('ctrl') wait(1) press('esc')", nd, new FakeOutlet());
            Assert.Equal("Line 1: " + ScriptKeys.CTRL_ESC_REFUSED, nr.Problem, "after making way for the nudge");
            Assert.True(nd.Gives >= 2, "the keyboard was given up for the nudge part-way");
            Assert.False(SentEsc(nd), "no Esc sent after the nudge");

            string[] fine = { "use('screen') press('esc')", "use('screen') keydown('shift') press('esc')", "use('screen') keydown('ctrl') keyup('ctrl') press('esc')", "keydown('ctrl') press('esc')" };
            foreach (string code in fine)
            {
                FakeDesk d = new FakeDesk();
                ScriptResult r = ScriptRig.Run(code, d, new FakeOutlet());
                Assert.Equal(null, r.Problem, code);
                Assert.True(SentEsc(d), code + ": Esc sent");
            }
        }

        // ---------- a script from someone else ----------

        public static void TestAnUntrustedScriptCantPlayOnTheScreen()
        {
            FakeDesk d = new FakeDesk();
            LuaRun run = new LuaRun("Shared", "use('screen') press('e')", d, ScriptRig.Window(d), new FakeOutlet(), null);
            run.Trusted = false;
            ScriptResult r = run.Run();
            Assert.Equal("Line 1: " + LuaRun.UNTRUSTED_SCREEN, r.Problem, "stopped, saying how to lift it");
            Assert.Equal(0, d.Sent.Count, "nothing sent");

            d = new FakeDesk();
            run = new LuaRun("Shared", "use(2) press('e')", d, ScriptRig.Window(d), new FakeOutlet(), null);
            run.Trusted = false;
            Assert.Equal(null, run.Run().Problem, "a Roblox window is fine");
            Assert.True(d.Sent.Count > 0, "and it played there");
        }

        // Trust fails closed: only a file that plainly says so gives it. A .lua
        // dropped in the folder with no settings, or settings lost in a failed
        // rename, must not skip the warning.
        public static void TestTrustIsKeptAndOnlyAPlainTrueGivesIt()
        {
            Assert.True(ScriptSettings.FromJson("{\"trusted\":true}").Trusted, "true gives it");
            Assert.False(ScriptSettings.FromJson("{\"playsOn\":\"front\",\"key\":0}").Trusted, "a file that doesn't say: not trusted");
            Assert.False(ScriptSettings.FromJson("not json").Trusted, "nothing readable: not trusted");
            Assert.False(ScriptSettings.FromJson(null).Trusted, "no file at all: not trusted");
            Assert.False(ScriptSettings.FromJson("{\"trusted\":false}").Trusted, "false is kept");
            ScriptSettings s = new ScriptSettings();
            s.Trusted = false;
            Assert.False(s.Copy().Trusted, "a copy keeps it - the Scripts window saves copies");
            Assert.False(ScriptSettings.FromJson(s.ToJson()).Trusted, "and so does the file");
        }

        static void Put(ZipArchive z, string name, byte[] bytes)
        {
            ZipArchiveEntry e = z.CreateEntry(name);
            using (Stream s = e.Open()) s.Write(bytes, 0, bytes.Length);
        }

        static void Put(ZipArchive z, string name, string text) { Put(z, name, Encoding.UTF8.GetBytes(text)); }

        static byte[] Png(Color c)
        {
            using (Bitmap b = new Bitmap(4, 4))
            {
                using (Graphics g = Graphics.FromImage(b)) g.Clear(c);
                using (MemoryStream ms = new MemoryStream()) { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); }
            }
        }

        // Whatever its own settings say, an imported script is not trusted,
        // plays on the Roblox window in front, and has no key.
        public static void TestAnImportedScriptIsNotTrusted()
        {
            string root = ScriptRig.TempDir();
            try
            {
                string zip = Path.Combine(root, "x" + ScriptPack.EXT);
                using (FileStream fs = File.Create(zip))
                using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    Put(z, "script.lua", "use('screen') press('e')");
                    Put(z, "settings.json", "{\"name\":\"Gift\",\"playsOn\":\"screen\",\"key\":117,\"discord\":true,\"trusted\":true}");
                    Put(z, "pictures.json", "[]");
                }
                ScriptStore store = new ScriptStore(Path.Combine(root, "scripts"));
                ScriptImport r = ScriptPack.Import(zip, store, Path.Combine(root, "pictures"), null, delegate { return null; });
                Assert.Equal(null, r.Problem, "imported");
                ScriptSettings s = store.Settings(r.Name);
                Assert.False(s.Trusted, "not trusted");
                Assert.True(s.OnFront, "plays on the Roblox window in front");
                Assert.Equal((byte)0, s.Key, "no key");
                Assert.False(r.KeyLeftOff, "not said to clash: it was free here");
                Assert.True(s.SummaryToDiscord, "the rest is kept");

                // Trusted once said, and kept.
                s.Trusted = true;
                store.SaveSettings(r.Name, s);
                Assert.True(new ScriptStore(store.Folder).Settings(r.Name).Trusted, "trusted, from the file");
                Assert.True(store.Create("Mine", "print(1)") == null && store.Settings("Mine").Trusted, "one made here is trusted");
            }
            finally { Directory.Delete(root, true); }
        }

        // ---------- keys let go of ----------

        // Its window closed with Shift held: Shift is let go of anyway,
        // straight to the PC, or it would stay down for everything.
        public static void TestAKeyHeldIsLetGoWhenItsWindowCloses()
        {
            FakeDesk d = new FakeDesk();
            d.Napped = delegate { if (d.Now >= 100) d.WindowGone = true; };
            ScriptResult r = ScriptRig.Run("keydown('shift') wait(1) press('e')", d, new FakeOutlet());
            Assert.Contains("closed", r.Problem ?? "", "it stopped because the window closed");
            Assert.True(d.Sent.Count >= 2, "something was sent");
            KeyValuePair<string, InputAction> last = d.Sent[d.Sent.Count - 1];
            Assert.Equal("plain", last.Key, "sent straight to the PC");
            Assert.True(last.Value.Kind == InputActionKind.KeyUp && last.Value.Vk == 0x10, "Shift let go of: " + FakeDesk.Describe(last.Value));
            Assert.False(d.Keyboard, "and the keyboard given up");
        }

        // ---------- memory ----------

        public static void TestStringRepIsCapped()
        {
            FakeDesk d = new FakeDesk();
            FakeOutlet o = new FakeOutlet();
            ScriptResult r = ScriptRig.Run("print(string.rep('ab', 3, ','), ('x'):rep(2), string.rep('x', 0), string.rep('x', 2.7))", d, o);
            Assert.Equal(null, r.Problem, "ran");
            Assert.Equal("ab,ab,ab\txx\t\txx", o.All, "as Lua's own");
            r = ScriptRig.Run("local s = string.rep('x', 2^28)", d, new FakeOutlet());
            StartsWith("Line 1: that text is too long", r.Problem, "too much at once");
            r = ScriptRig.Run("\nlocal s = ('x'):rep(2^28)", d, new FakeOutlet());
            StartsWith("Line 2: that text is too long", r.Problem, "written as a method too");
            r = ScriptRig.Run("local s = string.rep('x', 10, string.rep('y', 200000))", d, new FakeOutlet());
            StartsWith("Line 1: that text is too long", r.Problem, "with what goes between counted");
        }

        public static void TestOneCallCantBuildAHugeText()
        {
            FakeDesk d = new FakeDesk();
            ScriptResult r = ScriptRig.Run("local s = string.rep('x', 1000000) local w = string.rep('y', 1000000) s:gsub('x', w)", d, new FakeOutlet());
            StartsWith("Line 1: that text would get too long", r.Problem, "gsub with a long replacement");
            r = ScriptRig.Run("local s = string.rep('x', 1000000) local t = {x = string.rep('y', 1000000)} s:gsub('x', t)", d, new FakeOutlet());
            StartsWith("Line 1: that text would get too long", r.Problem, "gsub with a table");
            r = ScriptRig.Run("local t = {} local w = string.rep('y', 1000000) for i = 1, 20 do t[i] = w end table.concat(t)", d, new FakeOutlet());
            StartsWith("Line 1: that text would get too long", r.Problem, "concat");
            r = ScriptRig.Run("local t = {'a', 'b'} table.concat(t, string.rep('y', 1000000), 1, 100)", d, new FakeOutlet());
            StartsWith("Line 1: that text would get too long", r.Problem, "a long separator between many pieces, counted before they are looked at");
            r = ScriptRig.Run("table.unpack({}, 1, 1e8)", d, new FakeOutlet());
            StartsWith("Line 1: that unpacks too many values", r.Problem, "unpack");
            FakeOutlet o = new FakeOutlet();
            r = ScriptRig.Run("print(table.concat({1, 2, 3}, '-'), select('#', table.unpack({1, 2, 3})), ('a.b'):gsub('%.', '%0%0'))", d, o);
            Assert.Equal(null, r.Problem, "ordinary ones run");
            Assert.Equal("1-2-3\t3\ta..b\t1", o.All, "as before");
        }

        // print, string.format and string.byte each make all they make in
        // one step, where the memory check never looks. The sizes are just
        // over the cap, so a guard that failed costs tens of MB, not the PC.
        public static void TestPrintFormatAndByteCantBuildAHugeTextInOneCall()
        {
            string many = "local s = ('x'):rep(1048576) local t = {} for i = 1, 17 do t[i] = s end\n";
            FakeOutlet o = new FakeOutlet();
            ScriptResult r = OnItsThread(many + "print(table.unpack(t))", new FakeDesk(), o, 0);
            StartsWith("Line 2: that text would get too long", r.Problem, "print of many long texts");
            Assert.Equal(0, o.Printed.Count, "nothing printed");

            o = new FakeOutlet();
            r = OnItsThread("local s = ('x'):rep(1048576) local m = {__tostring = function() return s end} local t = {} for i = 1, 17 do t[i] = setmetatable({}, m) end\nprint(table.unpack(t))", new FakeDesk(), o, 0);
            StartsWith("Line 2: that text would get too long", r.Problem, "print of tables that turn into long texts");
            Assert.Equal(0, o.Printed.Count, "nothing printed for those either");

            r = OnItsThread(many + "string.format(('%s'):rep(17), table.unpack(t))", new FakeDesk(), new FakeOutlet(), 0);
            StartsWith("Line 2: that text would get too long", r.Problem, "string.format");
            r = OnItsThread(many + "local f = ('%s'):rep(17)\nf:format(table.unpack(t))", new FakeDesk(), new FakeOutlet(), 0);
            StartsWith("Line 3: that text would get too long", r.Problem, "format written as a method");

            r = OnItsThread("local s = ('x'):rep(200000)\nlocal n = select('#', string.byte(s, 1, -1))", new FakeDesk(), new FakeOutlet(), 0);
            StartsWith("Line 2: that gives too many values at once", r.Problem, "string.byte over a long text");
            r = OnItsThread("local s = ('x'):rep(200000)\nlocal n = select('#', s:byte(1, -1))", new FakeDesk(), new FakeOutlet(), 0);
            StartsWith("Line 2: that gives too many values at once", r.Problem, "byte written as a method");
            foreach (string odd in new string[] { "2^31, -1", "1e300, -1", "0/0, -1", "1, 0/0" })
            {
                r = OnItsThread("local s = ('x'):rep(200000)\nlocal n = select('#', s:byte(" + odd + "))", new FakeDesk(), new FakeOutlet(), 0);
                StartsWith("Line 2: that gives too many values at once", r.Problem, "an index MoonSharp would read as the whole text: " + odd);
            }

            o = new FakeOutlet();
            r = OnItsThread("local s = ('x'):rep(200000)\nprint(('abc'):byte(1, -1))\nprint(s:byte(-1), select('#', s:byte(5)), select('#', s:byte(1, 100000)))\n"
                + "print(string.format('%s=%5.1f %q', 'a', 2.5, 'b'), ('%d'):format(7))\nprint(1, nil, true, setmetatable({}, {__tostring = function() return 'T' end}))", new FakeDesk(), o, 0);
            Assert.Equal(null, r.Problem, "ordinary ones run");
            Assert.Equal("97\t98\t99\n120\t1\t100000\na=  2.5 \"b\"\t7\n1\tnil\ttrue\tT", string.Join("\n", o.Printed.ToArray()), "as Lua's own");
        }

        public static void TestARunUsingTooMuchMemoryEnds()
        {
            FakeDesk d = new FakeDesk();
            LuaRun run = new LuaRun("Hungry", "local t = {}\nfor i = 1, 1e8 do t[i] = {i} end", d, ScriptRig.Window(d), new FakeOutlet(), null);
            run.MemoryBudget = 32L * 1024 * 1024;
            ScriptResult r = run.Run();
            StartsWith("Line 2: that uses too much memory", r.Problem, "it ends, saying why");
        }

        // ---------- importing ----------

        // A painted-out copy named like a picture already here goes in under
        // a name of its own - the picture is not written over.
        public static void TestAnImportNeverWritesOverAPicture()
        {
            string root = ScriptRig.TempDir();
            try
            {
                string pics = Path.Combine(root, "pictures");
                Directory.CreateDirectory(pics);
                byte[] mine = Png(Color.Green), theirs = Png(Color.Red), theirMask = Png(Color.Blue);
                File.WriteAllBytes(Path.Combine(pics, "Sell-20250101-000000.png"), mine);
                File.WriteAllBytes(Path.Combine(pics, "egg-20261112-120000-mask-1.png"), mine);
                string zip = Path.Combine(root, "x" + ScriptPack.EXT);
                using (FileStream fs = File.Create(zip))
                using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    Put(z, "script.lua", "find('egg')");
                    Put(z, "settings.json", "{\"name\":\"Egg\"}");
                    Put(z, "pictures.json", "[{\"name\":\"egg\",\"file\":\"egg-20261112-120000.png\",\"mask\":\"Sell-20250101-000000.png\"}]");
                    Put(z, "egg-20261112-120000.png", theirs);
                    Put(z, "Sell-20250101-000000.png", theirMask);
                }
                ScriptImport r = ScriptPack.Import(zip, new ScriptStore(Path.Combine(root, "scripts")), pics, null, null);
                Assert.Equal(null, r.Problem, "imported");
                Assert.True(Same(mine, File.ReadAllBytes(Path.Combine(pics, "Sell-20250101-000000.png"))), "the picture called that is untouched");
                Assert.True(Same(mine, File.ReadAllBytes(Path.Combine(pics, "egg-20261112-120000-mask-1.png"))), "and so is a file with the mask's own name");
                Assert.True(Same(theirs, File.ReadAllBytes(Path.Combine(pics, "egg-20261112-120000.png"))), "their picture came");
                Assert.True(Same(theirMask, File.ReadAllBytes(Path.Combine(pics, "egg-20261112-120000-mask-1_2.png"))), "and its mask, under a name of its own");
                string file, mask;
                Assert.True(ScriptPictures.Resolve(PictureShelf.For(null, null, pics), pics, "egg", out file, out mask), "the script finds it");
                Assert.Equal("egg-20261112-120000-mask-1_2.png", mask, "with its own mask");
            }
            finally { Directory.Delete(root, true); }
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // A small file that unpacks to more than MAX_TOTAL_BYTES is refused,
        // and nothing of it is kept.
        public static void TestAHugeFileIsRefused()
        {
            string root = ScriptRig.TempDir();
            try
            {
                string zip = Path.Combine(root, "big" + ScriptPack.EXT);
                byte[] zeros = new byte[17 * 1024 * 1024];
                using (FileStream fs = File.Create(zip))
                using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    Put(z, "script.lua", "print(1)");
                    for (int i = 0; i < 4; i++) Put(z, "p" + i + ".png", zeros);
                }
                Assert.True(new FileInfo(zip).Length < 1024 * 1024, "the file itself is small");
                ScriptStore store = new ScriptStore(Path.Combine(root, "scripts"));
                ScriptImport r = ScriptPack.Import(zip, store, Path.Combine(root, "pictures"), null, null);
                Assert.Contains("too big", r.Problem ?? "", "refused, saying why");
                Assert.Equal(null, r.Name, "nothing made");
                Assert.Equal(0, store.Names().Count, "no script");
            }
            finally { Directory.Delete(root, true); }
        }

        // A picture renamed on the way in is renamed where the script uses
        // it as a picture - and nowhere else.
        public static void TestARenamedPictureOnlyChangesPictureCalls()
        {
            string code = "find(\"Sell\") waitfor 'Sell' waitgone( \"Sell\", 2) clickpicture\n(\"Sell\")\n" +
                          "see(\"Sell\") chat(\"Sell\") count(\"Sell\") local s = \"Sell\" print(s:find(\"Sell\"), string.find(s, \"Sell\"), t.find(\"Sell\"), myfind(\"Sell\"))";
            Dictionary<string, string> to = new Dictionary<string, string>();
            to["Sell"] = "Sell 2";
            Assert.Equal("find(\"Sell 2\") waitfor \"Sell 2\" waitgone( \"Sell 2\", 2) clickpicture\n(\"Sell 2\")\n" +
                         "see(\"Sell\") chat(\"Sell\") count(\"Sell\") local s = \"Sell\" print(s:find(\"Sell\"), string.find(s, \"Sell\"), t.find(\"Sell\"), myfind(\"Sell\"))",
                         ScriptLiterals.Rename(code, to), "only the picture functions' pictures");
        }
    }
}
