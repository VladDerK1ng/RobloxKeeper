using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // The Scripts window: what the editor colours, the unsaved-changes
    // question, the keys, the examples and the Save as script buttons. The
    // engine underneath has its own tests; here a stand-in for the main
    // window answers, over a scripts folder of its own.
    static class ScriptsUiTests
    {
        // The main window as far as the Scripts window can tell.
        class FakeScriptHost : IScriptHost
        {
            public readonly ScriptStore Files;
            public readonly List<string> Started = new List<string>();
            public readonly List<string> Stopped = new List<string>();
            public readonly List<string> Running = new List<string>();
            public readonly List<ScriptRunInfo> Info = new List<ScriptRunInfo>();
            public string StartRefusal;
            public string KeyRefusal;
            public int Saved;
            public string NoticeText = "MoonSharp. Copyright and the disclaimer.";
            public string ExportedTo;

            public FakeScriptHost(string dir) { Files = new ScriptStore(dir); }

            public ScriptStore Store { get { return Files; } }

            public string StartScript(string name)
            {
                if (StartRefusal != null) return StartRefusal;
                Started.Add(name);
                Running.Add(name);
                ScriptRunInfo i = new ScriptRunInfo();
                i.Name = name;
                Info.Add(i);
                return null;
            }

            public string StartScript(string name, int pid, string tag) { return StartScript(name); }

            public bool StopScript(string name)
            {
                Stopped.Add(name);
                return Running.Remove(name);
            }

            public bool IsScriptRunning(string name) { return Running.Contains(name); }

            public List<ScriptRunInfo> ScriptsRunning()
            {
                List<ScriptRunInfo> l = new List<ScriptRunInfo>();
                foreach (ScriptRunInfo i in Info) if (Running.Contains(i.Name)) l.Add(i);
                return l;
            }

            public string ScriptKeyProblem(byte vk, string name) { return KeyRefusal; }
            public void ScriptsSaved() { Saved++; }
            public bool IsScriptTrusted(string name) { string n = Files.Find(name); return n == null || Files.Settings(n).Trusted; }
            public void TrustScript(string name)
            {
                string n = Files.Find(name);
                if (n == null) return;
                ScriptSettings s = Files.Settings(n);
                s.Trusted = true;
                Files.SaveSettings(n, s);
            }
            public string RenameScript(string from, string to) { return Files.Rename(from, to); }
            public string RemoveScript(string name) { return Files.Remove(name) ? null : "Couldn't remove it."; }
            public string MacroAsLua(Macro m) { return ScriptConvert.ToLua(m); }
            public string ExportScript(string name, string zipPath) { ExportedTo = zipPath; return null; }
            public ScriptImport ImportScript(string zipPath)
            {
                ScriptImport r = new ScriptImport();
                r.Problem = "That isn't a RobloxKeeper script.";
                return r;
            }
            public string Notices { get { return NoticeText; } }

            public event Action<string, string> ScriptOutput;
            public event Action<string, int> ScriptLine;
            public event Action<string, string> ScriptStatus;
            public event Action<string, ScriptResult> ScriptEnded;

            public void RaiseOutput(string name, string line) { if (ScriptOutput != null) ScriptOutput(name, line); }
            public void RaiseLine(string name, int line) { if (ScriptLine != null) ScriptLine(name, line); }
            public void RaiseStatus(string name, string text) { if (ScriptStatus != null) ScriptStatus(name, text); }
            public void RaiseEnded(string name, ScriptResult r) { Running.Remove(name); if (ScriptEnded != null) ScriptEnded(name, r); }
            public bool Listened { get { return ScriptOutput != null || ScriptLine != null || ScriptStatus != null || ScriptEnded != null; } }
        }

        class NoCapture : ICapture
        {
            public Pixels Grab(int pid, out WatchState state) { state = WatchState.NotInAGame; return null; }
        }

        class NoReader : IReadText
        {
            public string Read(Pixels image) { return ""; }
        }

        static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rk-scripts-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static FakeScriptHost Host(params string[] names)
        {
            FakeScriptHost h = new FakeScriptHost(TempDir());
            foreach (string n in names) h.Files.Create(n, "-- " + n + "\r\nwait(1)\r\n");
            return h;
        }

        static WatchKit Kit(FakeScriptHost host, params string[] accounts)
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(Path.Combine(TempDir(), "watch.dat"));
            k.Capture = new NoCapture();
            k.Reader = new NoReader();
            k.Engine = new WatchEngine(k.Capture, k.Reader);
            k.Clients = delegate { return new WatchedClient[0]; };
            List<string> names = new List<string>(accounts);
            k.Accounts = delegate { return (IList<string>)names; };
            k.Log = delegate(string s) { };
            k.Scripts = host;
            return k;
        }

        static string Kinds(string text, params string[] functions)
        {
            List<string> parts = new List<string>();
            foreach (LuaSpan s in LuaSyntax.Spans(text, text.Length, functions))
                parts.Add(s.Kind.ToString().ToLowerInvariant() + ":" + text.Substring(s.Start, s.Length));
            return string.Join("|", parts.ToArray());
        }

        // ---------- what is coloured ----------

        public static void TestLuaWordsStringsCommentsAndNumbersAreColoured()
        {
            Assert.Equal("keyword:local|keyword:while|keyword:true|keyword:do|function:press|string:\"e\"|number:0.5|keyword:end|comment:-- done",
                         Kinds("local x\nwhile true do press(\"e\", 0.5) end -- done", "press"),
                         "keywords, calls, strings, numbers and comments");
            Assert.Equal("number:0xFF|number:1e-3|number:.5|number:42", Kinds("0xFF 1e-3 .5 42"), "the ways a number is written");
            Assert.Equal("string:'it'|string:\"a\\\"b\"", Kinds("'it' \"a\\\"b\""), "both quotes, and an escaped one");
        }

        public static void TestLuaColouringReadsLongStringsAndCommentsAcrossLines()
        {
            string text = "x = [[one\nlocal two]] y\n--[[ a\ncomment ]] z\n--[==[ b ]] still ]==] w";
            string kinds = Kinds(text);
            Assert.Equal("string:[[one\nlocal two]]|comment:--[[ a\ncomment ]]|comment:--[==[ b ]] still ]==]", kinds,
                         "a keyword inside a long string or comment is not a keyword");
            Assert.Equal("comment:--[[ never closed\nlocal x", Kinds("--[[ never closed\nlocal x"), "one that never closes runs to the end");
        }

        public static void TestLuaColouringLeavesWordsInsideStringsAndCommentsAlone()
        {
            Assert.Equal("string:\"then end 12\"|comment:-- local 5", Kinds("\"then end 12\" -- local 5"), "only the string and the comment");
            Assert.Equal("string:\"never closed|keyword:local", Kinds("\"never closed\nlocal"), "a quote that never closes ends with its line");
        }

        public static void TestLuaColouringStopsWhereAsked()
        {
            string text = "local a\nlocal b\nlocal c\n";
            Assert.Equal(1, LuaSyntax.Spans(text, 3, null).Count, "only what starts before the end asked for");
            Assert.Equal(3, LuaSyntax.Spans(text, text.Length, null).Count, "and all of it when asked");
            Assert.Equal(0, LuaSyntax.Spans("", 10, null).Count, "nothing in nothing");
            Assert.Equal(0, LuaSyntax.Spans(null, 10, null).Count, "or in null");
        }

        public static void TestOnlyCallsOfTheAppsFunctionsAreColoured()
        {
            Assert.Equal("function:count|string:\"a\"|function:count|number:1|function:wait|number:1",
                         Kinds("count(\"a\") count ( 1 ) wait(1)", "count", "wait"), "a call, with room before the bracket");
            Assert.Equal("keyword:local|number:3", Kinds("local count = 3 print(count)", "count"), "a variable of that name is left alone");
            Assert.Equal("number:1|number:1", Kinds("t.count(1) t:count(1)", "count"), "so is something else's");
        }

        public static void TestEveryScriptFunctionIsKnownToTheColouring()
        {
            foreach (ScriptFunction f in ScriptFunctions.All)
                Assert.True(ScriptFunctions.Names.Contains(f.Name), f.Name + " is in the set that is coloured");
            Assert.True(LuaSyntax.IsKeyword("elseif") && LuaSyntax.IsKeyword("nil") && !LuaSyntax.IsKeyword("press"), "keywords");
        }

        // ---------- Enter and Tab ----------

        public static void TestEnterKeepsTheIndentAndOpensBlocks()
        {
            Assert.Equal("", LuaEdit.IndentAfter("x = 1"), "no indent stays none");
            Assert.Equal("    ", LuaEdit.IndentAfter("    x = 1"), "the same as the line above");
            Assert.Equal("    ", LuaEdit.IndentAfter("while true do"), "a block opens");
            Assert.Equal("        ", LuaEdit.IndentAfter("    if x then"), "inside another");
            Assert.Equal("    ", LuaEdit.IndentAfter("local function f(a, b)"), "a function");
            Assert.Equal("    ", LuaEdit.IndentAfter("for i = 1, 3 do -- three times"), "a comment on the end doesn't hide it");
            Assert.Equal("", LuaEdit.IndentAfter("-- then we do"), "a comment on its own opens nothing");
            Assert.Equal("", LuaEdit.IndentAfter("if x then y() end"), "a block that closes on its line opens nothing");
            Assert.Equal("    ", LuaEdit.IndentAfter("local t = {"), "a table");
        }

        public static void TestEditHistoryGoesBackAndForward()
        {
            EditHistory h = new EditHistory("a");
            Assert.False(h.CanUndo, "nothing to undo at the start");
            h.Record("ab", 2);
            h.Record("abc", 3);
            EditHistory.Snapshot s = h.Undo("abc", 3);
            Assert.Equal("ab", s.Text, "one step back");
            s = h.Undo("ab", 2);
            Assert.Equal("a", s.Text, "another");
            Assert.True(h.Undo("a", 1) == null, "and no more");
            s = h.Redo("a", 1);
            Assert.Equal("ab", s.Text, "forward again");
            h.Record("abX", 3);
            Assert.False(h.CanRedo, "typing something else forgets what was undone");
            Assert.Equal("ab", h.Undo("abX", 3).Text, "and goes back to where it left");
        }

        public static void TestEditHistoryTakesTheTextNotYetKeptAsAStep()
        {
            EditHistory h = new EditHistory("");
            // Typed, and Ctrl+Z pressed before the pause that would have kept it.
            EditHistory.Snapshot s = h.Undo("hello", 5);
            Assert.Equal("", s.Text, "back to the start");
            Assert.Equal("hello", h.Redo("", 0).Text, "and what was typed is not lost");
        }

        // ---------- the editor ----------

        static string Script(int lines)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 1; i <= lines; i++)
            {
                if (i % 5 == 0) sb.Append("-- line " + i + " is a comment\r\n");
                else sb.Append("if count(\"n" + i + "\") > " + i + " then press(\"e\", 0.5) wait(1.5) end\r\n");
            }
            return sb.ToString();
        }

        static LuaEditor Editor()
        {
            LuaEditor e = new LuaEditor(ScriptFunctions.Names);
            e.Size = new Size(504, 350);
            return e;
        }

        static Color ColourAt(LuaEditor e, int at, int length)
        {
            e.Box.Select(at, length);
            return e.Box.SelectionColor;
        }

        public static void TestTabInsertsFourSpaces()
        {
            using (LuaEditor e = Editor())
            {
                e.Load("x = 1");
                e.Box.Select(0, 0);
                e.InsertTab();
                Assert.Equal("    x = 1", e.PlainText, "four spaces, where the cursor was");
                Assert.Equal(4, e.CaretAt, "and the cursor after them");
                e.Box.Select(e.PlainText.Length, 0);
                e.InsertNewLine();
                Assert.Equal("    x = 1\n    ", e.PlainText, "Enter keeps the indent");
            }
        }

        public static void TestALoadedScriptIsSavedAndTypingMakesItUnsaved()
        {
            using (LuaEditor e = Editor())
            {
                int changed = 0, dirty = 0;
                e.Changed += delegate { changed++; };
                e.DirtyChanged += delegate { dirty++; };
                e.Load("-- one\r\nwait(1)\r\n");
                Assert.False(e.Dirty, "a script as it was on disk is not unsaved, whatever its line ends");
                Assert.Equal(0, changed, "and loading it is not typing");
                Assert.Equal("-- one\r\nwait(1)\r\n", e.Code, "it is written back with the line ends Notepad wants");
                e.Box.Select(e.Box.TextLength, 0);
                e.Box.SelectedText = "x";
                Assert.True(e.Dirty, "typing makes it unsaved");
                Assert.Equal(1, changed, "and says so");
                e.MarkSaved();
                Assert.False(e.Dirty, "saved");
                e.Box.SelectedText = "y";
                e.Box.Select(e.Box.TextLength - 1, 1);
                e.Box.SelectedText = "";
                Assert.False(e.Dirty, "typing it back to what was saved leaves it saved");
                Assert.Equal(4, dirty, "each change of that said so once");
            }
        }

        public static void TestOnlyTheLinesInViewAreColouredAndScrollingColoursTheRest()
        {
            using (LuaEditor e = Editor())
            {
                string code = Script(300);
                Stopwatch watch = Stopwatch.StartNew();
                e.Load(code);
                long loadMs = watch.ElapsedMilliseconds;
                string text = e.PlainText;
                Assert.Equal(LuaEditor.KeywordColour.ToArgb(), ColourAt(e, text.IndexOf("if"), 2).ToArgb(), "the first line's if");
                Assert.Equal(LuaEditor.FunctionColour.ToArgb(), ColourAt(e, text.IndexOf("press"), 5).ToArgb(), "a call of the app's own");
                Assert.Equal(LuaEditor.StringColour.ToArgb(), ColourAt(e, text.IndexOf("\"e\""), 3).ToArgb(), "a string");
                Assert.Equal(LuaEditor.CommentColour.ToArgb(), ColourAt(e, text.IndexOf("-- line 5"), 9).ToArgb(), "a comment");
                Assert.True(e.ColouredTo < text.Length, "not the whole script: only what is in view and a margin");
                int far = text.IndexOf("n249");
                Assert.Equal(Theme.Text.ToArgb(), ColourAt(e, far - 5, 2).ToArgb(), "a line far below is left as it was");

                e.ScrollToLine(249);
                e.ColourNow();
                Assert.True(e.ColouredFrom <= far && far < e.ColouredTo, "scrolling there colours it");
                int farIf = text.IndexOf("if", far - 20);
                Assert.Equal(LuaEditor.KeywordColour.ToArgb(), ColourAt(e, farIf, 2).ToArgb(), "its words are in colour now");

                // Colouring a screenful of a 300-line script is the whole cost
                // of a pause in typing.
                watch.Reset();
                watch.Start();
                for (int i = 0; i < 5; i++) e.Recolour();
                long each = watch.ElapsedMilliseconds / 5;
                Assert.True(each < 400, "a pass over the lines in view took " + each + " ms (load " + loadMs + " ms)");
            }
        }

        public static void TestColouringIsNotTypingAndUndoStillUndoesTyping()
        {
            using (LuaEditor e = Editor())
            {
                int changed = 0;
                e.Load("local x = 1\r\n");
                e.Changed += delegate { changed++; };
                e.Recolour();
                e.RunLine = 1;
                e.RunLine = 0;
                Assert.Equal(0, changed, "colouring and the running line are not edits");
                Assert.False(e.Dirty, "and leave it saved");

                e.Box.Select(e.Box.TextLength, 0);
                e.Box.SelectedText = "x = 2\n";
                e.Recolour();
                Assert.True(e.Undo(), "there is something to undo");
                Assert.Equal("local x = 1\n", e.PlainText, "it is the typing that was undone, not the colour");
                Assert.False(e.Dirty, "back to what was saved");
                Assert.True(e.Redo(), "and forward again");
                Assert.Equal("local x = 1\nx = 2\n", e.PlainText, "redone");
            }
        }

        public static void TestTheRunningLineIsMarkedInTheEditor()
        {
            using (LuaEditor e = Editor())
            {
                e.Load("one()\r\ntwo()\r\nthree()\r\n");
                e.RunLine = 2;
                e.Box.Select(7, 3);
                Assert.NotEqual(e.Box.BackColor.ToArgb(), e.Box.SelectionBackColor.ToArgb(), "the second line has a band behind it");
                e.Box.Select(0, 3);
                Assert.Equal(e.Box.BackColor.ToArgb(), e.Box.SelectionBackColor.ToArgb(), "the first does not");
                e.RunLine = 3;
                e.Box.Select(7, 3);
                Assert.Equal(e.Box.BackColor.ToArgb(), e.Box.SelectionBackColor.ToArgb(), "it moved off the second");
                e.RunLine = 0;
                e.Box.Select(14, 3);
                Assert.Equal(e.Box.BackColor.ToArgb(), e.Box.SelectionBackColor.ToArgb(), "and off the third");
            }
        }

        static int TopLine(LuaEditor e)
        {
            return (int)Native.SendMessage(e.Box.Handle, 0x00CE /* EM_GETFIRSTVISIBLELINE */, IntPtr.Zero, IntPtr.Zero);
        }

        // The running line moving along must leave the view where you
        // scrolled it, and your selection as you made it - a selection made
        // upwards stays one, so Shift+arrows go on growing it from its top.
        public static void TestTheRunningLineLeavesTheViewAndSelectionAlone()
        {
            using (LuaEditor e = Editor())
            {
                e.Load(Script(300));
                e.Box.Select(0, 0);
                Native.SendMessage(e.Box.Handle, 0x00B6 /* EM_LINESCROLL */, IntPtr.Zero, (IntPtr)150);
                int top = TopLine(e);
                Assert.True(top > 100, "scrolled away from the cursor (top line " + top + ")");
                e.RunLine = 3;
                Assert.Equal(top, TopLine(e), "a running line above the view doesn't scroll it");
                e.RunLine = 280;
                Assert.Equal(top, TopLine(e), "nor one below");
                e.RunLine = 0;
                Assert.Equal(top, TopLine(e), "nor taking it off");
                e.Recolour();
                Assert.Equal(top, TopLine(e), "nor colouring");
                Assert.Equal(0, e.Box.SelectionStart, "the cursor stays where it was");

                e.Box.Select(10, 10);
                Assert.False(e.SelectionBackward, "a selection made forwards reads so");
                Native.SendMessage(e.Box.Handle, 0x00B1 /* EM_SETSEL */, (IntPtr)20, (IntPtr)10);
                Assert.True(e.SelectionBackward, "and one made backwards reads differently");
                e.RunLine = 2;
                Assert.True(e.SelectionBackward, "still backwards after the running line moved");
                Assert.True(e.Box.SelectionStart == 10 && e.Box.SelectionLength == 10, "and the same characters");
                e.Recolour();
                Assert.True(e.SelectionBackward, "and after colouring");
                e.Box.Select(10, 10);
                e.RunLine = 4;
                Assert.False(e.SelectionBackward, "a forwards one stays forwards");
            }
        }

        // ---------- the Functions list ----------

        static FakeDesk DeskWithPictures()
        {
            FakeDesk d = new FakeDesk();
            d.Pictures["coin"] = "coin.png";
            d.Pictures["bag full"] = "full.png";
            d.Pictures["button"] = "button.png";
            return d;
        }

        public static void TestEveryFunctionInTheListRunsAsWritten()
        {
            foreach (ScriptFunction f in ScriptFunctions.All)
            {
                FakeDesk d = DeskWithPictures();
                ScriptResult r = ScriptRig.Run(f.Insert, d, new FakeOutlet());
                Assert.Equal(null, r.Problem, f.Insert + " as the list puts it in");
                Assert.True(f.Says.Length > 0 && f.Says.Length < 90, f.Name + " has one short line of its own");
            }
        }

        public static void TestEveryFunctionTheEngineGivesIsInTheList()
        {
            // Lua's own: the ones that come with the language, not with the app.
            string[] lua = { "assert", "collectgarbage", "error", "getmetatable", "ipairs", "next", "pairs", "pcall", "rawequal",
                             "rawget", "rawlen", "rawset", "select", "setmetatable", "tonumber", "tostring", "typeof", "xpcall",
                             "unpack", "pack", "loadstring", "dofile", "load" };
            FakeDesk d = new FakeDesk();
            FakeOutlet o = new FakeOutlet();
            ScriptRig.Run("for k, v in pairs(_G) do if typeof(v) == 'function' then print(k) end end", d, o);
            List<string> missing = new List<string>();
            foreach (string name in o.All.Split('\n'))
            {
                string n = name.Trim();
                if (n.Length == 0 || Array.IndexOf(lua, n) >= 0 || ScriptFunctions.Names.Contains(n)) continue;
                missing.Add(n);
            }
            Assert.Equal(0, missing.Count, "functions the engine gives that the Functions list doesn't show: " + string.Join(", ", missing.ToArray()));
        }

        // ---------- the examples ----------

        // Runs it until this much pretend time has passed, then stops it as
        // the Stop button does.
        static ScriptResult RunUntilStopped(string code, FakeDesk d, FakeOutlet o, int ms)
        {
            LuaRun run = new LuaRun("Example", code, d, ScriptRig.Window(d), o, null);
            int n = 0;
            // By the pretend clock; and by a count, so a loop that never waits fails rather than hangs.
            d.Napped = delegate { if (d.Now >= ms || ++n > 500000) run.Stop(); };
            return run.Run();
        }

        static double Counted(ScriptResult r, string what)
        {
            foreach (KeyValuePair<string, double> c in r.Counters) if (c.Key == what) return c.Value;
            return 0;
        }

        public static void TestEveryExampleHasWordsAtTheTopAndNamesItsPictures()
        {
            Assert.Equal(4, ScriptExamples.Names.Length, "the four");
            for (int i = 0; i < ScriptExamples.Names.Length; i++)
            {
                string code = ScriptExamples.Code(i);
                Assert.True(code.StartsWith("--"), ScriptExamples.Names[i] + " starts with what it is");
                foreach (string picture in ScriptExamples.Pictures(i))
                    Assert.Contains("\"" + picture + "\"", code, ScriptExamples.Names[i] + " looks for its picture " + picture);
                Assert.Equal(null, ScriptStore.NameProblem(ScriptExamples.Names[i]), "and its name can be a script's");
            }
        }

        public static void TestTheFarmLoopWalksCollectsAndSells()
        {
            FakeDesk d = DeskWithPictures();
            d.Seen = delegate(string name, int looks) { return name == "coin.png" ? looks % 3 != 2 : looks % 4 == 0; };
            FakeOutlet o = new FakeOutlet();
            ScriptResult r = RunUntilStopped(ScriptExamples.Code(0), d, o, 120000);
            Assert.Equal(null, r.Problem, "it runs cleanly until stopped");
            string said = d.SaidText();
            Assert.Contains("down W", said, "walks forward");
            Assert.Contains("up W", said, "and lets go of the key");
            Assert.Contains("down D", said, "and to the right");
            Assert.True(Counted(r, "collected") > 0, "collects what shows");
            Assert.True(Counted(r, "sold") > 0, "sells when the bag is full");
            Assert.True(o.Statuses.Contains("Selling") && o.Statuses.Contains("Farming"), "says what it is doing");
            Assert.Equal(0, d.Gives - d.Takes, "keeps no keyboard when it ends");
        }

        public static void TestTheFarmLoopWalksOnWhenNothingShows()
        {
            FakeDesk d = DeskWithPictures();
            ScriptResult r = RunUntilStopped(ScriptExamples.Code(0), d, new FakeOutlet(), 30000);
            Assert.Equal(null, r.Problem, "nothing found is no mistake");
            Assert.Equal(0.0, Counted(r, "collected"), "nothing collected");
            Assert.Contains("down W", d.SaidText(), "it still walks");
        }

        public static void TestClickWhenAPictureShowsClicksIt()
        {
            FakeDesk d = DeskWithPictures();
            d.Seen = delegate(string name, int looks) { return looks % 2 == 0; };
            ScriptResult r = RunUntilStopped(ScriptExamples.Code(1), d, new FakeOutlet(), 30000);
            Assert.Equal(null, r.Problem, "runs cleanly");
            Assert.True(Counted(r, "clicks") > 0, "clicks it when it shows");
            Assert.Contains("press left", d.SaidText(), "with the mouse");
        }

        public static void TestSayingSomethingInChatEveryFewMinutes()
        {
            FakeDesk d = DeskWithPictures();
            ScriptResult r = RunUntilStopped(ScriptExamples.Code(2), d, new FakeOutlet(), 700000);
            Assert.Equal(null, r.Problem, "runs cleanly");
            Assert.True(Counted(r, "said") >= 2, "says it again after the wait");
            Assert.Contains("char H", d.SaidText(), "typed out");
            Assert.True(d.Now >= 300000, "with minutes between (" + d.Now + " ms)");
        }

        public static void TestTakingTurnsUsesEveryRobloxWindow()
        {
            FakeDesk d = DeskWithPictures();
            FakeOutlet o = new FakeOutlet();
            ScriptResult r = RunUntilStopped(ScriptExamples.Code(3), d, o, 30000);
            Assert.Equal(null, r.Problem, "runs cleanly");
            bool first = false, second = false;
            foreach (KeyValuePair<string, InputAction> p in d.Sent)
            {
                if (p.Key == "11") first = true;
                if (p.Key == "22") second = true;
            }
            Assert.True(first && second, "each window had its turn");
            Assert.True(o.Statuses.Contains("Playing on Client 1") && o.Statuses.Contains("Playing on Client 2"), "and the label says whose");
        }

        // ---------- the window ----------

        static ScriptsDialog Open(FakeScriptHost host, string on, params string[] accounts)
        {
            return new ScriptsDialog(Kit(host, accounts), on);
        }

        static void Distrust(FakeScriptHost h, string name)
        {
            ScriptSettings s = h.Files.Settings(name);
            s.Trusted = false;
            h.Files.SaveSettings(name, s);
        }

        public static void TestABannerShowsForAScriptFromSomeoneElseAndNotForYourOwn()
        {
            FakeScriptHost h = Host("Mine", "Theirs");
            Distrust(h, "Theirs");
            using (ScriptsDialog d = Open(h, "Mine"))
            {
                Assert.False(d.BannerShown, "your own script: no banner");
                int top = d.Editor.Top;
                d.Open("Theirs");
                Assert.True(d.BannerShown, "someone else's: the banner");
                Assert.Equal("This script came from someone else. Read it before you run it - a script controls your keyboard and mouse.", d.BannerText, "what it says");
                Assert.Equal("I trust this script", d.TrustText, "the button");
                Assert.True(d.Editor.Top > top, "the editor moves down, so the banner covers nothing");
                d.Open("Mine");
                Assert.False(d.BannerShown, "and gone again");
                Assert.Equal(top, d.Editor.Top, "the editor is back");
            }
        }

        public static void TestTheTrustButtonTrustsTheScriptAndHidesTheBanner()
        {
            FakeScriptHost h = Host("Theirs");
            Distrust(h, "Theirs");
            using (ScriptsDialog d = Open(h, "Theirs"))
            {
                Assert.True(d.BannerShown, "shown first");
                d.Trust();
                Assert.True(h.IsScriptTrusted("Theirs"), "it is trusted now");
                Assert.False(d.BannerShown, "and the banner is gone");
            }
        }

        public static void TestTheWindowListsScriptsAndShowsTheFirst()
        {
            FakeScriptHost h = Host("Farm", "Chat");
            using (ScriptsDialog d = Open(h, null))
            {
                Assert.Equal("Chat,Farm", string.Join(",", d.Listed.ToArray()), "alphabetical");
                Assert.Equal("Chat", d.Current, "the first is shown");
                Assert.Contains("-- Chat", d.Editor.PlainText, "with its code");
                Assert.Equal("Run", d.RunText, "ready to run");
            }
            using (ScriptsDialog d = Open(h, "farm"))
                Assert.Equal("Farm", d.Current, "a script asked for, found whatever its case");
            using (ScriptsDialog d = Open(Host(), null))
            {
                Assert.True(d.Current == null, "with none, none is shown");
                Assert.False(d.Editor.Box.Enabled && !d.Editor.IsReadOnly, "and the editor can't be typed in");
                Assert.Contains("New script", d.StatusText, "and it says what to do");
            }
        }

        public static void TestUnsavedChangesAreAskedAboutBeforeSwitching()
        {
            FakeScriptHost h = Host("A", "B");
            using (ScriptsDialog d = Open(h, "A"))
            {
                int asked = 0;
                DialogResult answer = DialogResult.Cancel;
                d.Ask = delegate(string name) { asked++; Assert.Equal("A", name, "asked about the one being left"); return answer; };

                Assert.True(d.Open("B"), "nothing unsaved: no question");
                Assert.Equal(0, asked, "so none was asked");
                d.Open("A");

                d.Editor.Box.Select(0, 0);
                d.Editor.Box.SelectedText = "-- edited\n";
                Assert.True(d.Dirty, "edited");
                Assert.Contains("not saved", d.NameText, "and it says so");

                Assert.False(d.Open("B"), "Cancel stays");
                Assert.Equal("A", d.Current, "on the same script");
                Assert.True(d.Dirty, "with its changes");
                Assert.Equal(1, asked, "after one question");

                answer = DialogResult.Yes;
                Assert.True(d.Open("B"), "Yes saves and goes");
                Assert.Contains("-- edited", h.Files.Load("A"), "what was typed is in the file");
                Assert.Equal("B", d.Current, "now on the other");

                d.Open("A");
                d.Editor.Box.Select(0, 0);
                d.Editor.Box.SelectedText = "-- thrown away\n";
                answer = DialogResult.No;
                Assert.True(d.Open("B"), "No leaves it behind");
                Assert.False(h.Files.Load("A").Contains("thrown away"), "unsaved, as it was");
                d.Open("A");
                Assert.False(d.Dirty, "shown as it is on disk");
            }
        }

        public static void TestUnsavedChangesAreAskedAboutWhenClosing()
        {
            FakeScriptHost h = Host("A");
            using (ScriptsDialog d = Open(h, "A"))
            {
                DialogResult answer = DialogResult.Cancel;
                int asked = 0;
                d.Ask = delegate { asked++; return answer; };
                Assert.True(d.ConfirmLeave(), "saved: free to go, and not asked");
                Assert.Equal(0, asked, "no question");
                d.Editor.Box.SelectedText = "x";
                Assert.False(d.ConfirmLeave(), "Cancel keeps the window open");
                answer = DialogResult.Yes;
                Assert.True(d.ConfirmLeave(), "Yes saves, then closes");
                Assert.False(d.Dirty, "saved");
                Assert.Equal(2, asked, "asked each time it was dirty");
            }
        }

        public static void TestRunSavesWhatIsTypedThenStartsAndTheButtonFlips()
        {
            FakeScriptHost h = Host("A");
            using (ScriptsDialog d = Open(h, "A"))
            {
                d.Editor.Box.Select(0, 0);
                d.Editor.Box.SelectedText = "-- typed\n";
                Assert.Equal(null, d.RunOrStop(), "it starts");
                Assert.Contains("-- typed", h.Files.Load("A"), "after saving what is typed: it runs what is on disk");
                Assert.Equal("A", h.Started[0], "the one shown");
                Assert.Equal("Stop", d.RunText, "the button becomes Stop");
                Assert.Equal("Running.", d.StatusText, "and says so");
                Assert.Equal(null, d.RunOrStop(), "pressed again");
                Assert.Equal("A", h.Stopped[0], "it stops that one");
                h.RaiseEnded("A", new ScriptResult { Name = "A", Ms = 1000 });
                Assert.Equal("Run", d.RunText, "and is Run again when it has ended");
            }
        }

        public static void TestARefusedStartIsSaidAndTheButtonStaysRun()
        {
            FakeScriptHost h = Host("A");
            h.StartRefusal = "Open Roblox first - it plays on the Roblox window in front.";
            using (ScriptsDialog d = Open(h, "A"))
            {
                Assert.Equal(h.StartRefusal, d.RunOrStop(), "why");
                Assert.Equal(h.StartRefusal, d.StatusText, "said under the button");
                Assert.True(d.StatusIsWarning, "as a warning");
                Assert.Equal("Run", d.RunText, "nothing runs");
            }
        }

        public static void TestTheRunningLineOutputAndStatusFollowTheScript()
        {
            FakeScriptHost h = Host("A", "B");
            using (ScriptsDialog d = Open(h, "A"))
            {
                d.RunOrStop();
                h.RaiseLine("A", 2);
                Assert.Equal(2, d.Editor.RunLine, "the line it is on is marked");
                h.RaiseLine("A", 1);
                Assert.Equal(1, d.Editor.RunLine, "and follows it");
                h.RaiseOutput("A", "hello");
                h.RaiseStatus("A", "Farming");
                Assert.Equal("hello", string.Join("|", d.OutputLines.ToArray()), "what it printed");
                Assert.Equal("Farming", d.StatusText, "what status() said");

                h.RaiseLine("B", 2);
                h.RaiseOutput("B", "not this one's");
                Assert.Equal(1, d.Editor.RunLine, "another script's line is not marked here");
                Assert.Equal("hello", string.Join("|", d.OutputLines.ToArray()), "nor its output shown");

                d.Open("B");
                Assert.Equal("not this one's", string.Join("|", d.OutputLines.ToArray()), "but kept: it is there when that one is opened");
                Assert.Equal(0, d.Editor.RunLine, "and a script that isn't running has no line");
                d.Open("A");
                Assert.Equal(1, d.Editor.RunLine, "the running one has it back");
                Assert.Equal("Farming", d.StatusText, "and its status");

                h.RaiseStatus("A", null);
                Assert.Equal("", d.StatusText, "status() with nothing hides it");
                h.RaiseEnded("A", new ScriptResult { Name = "A", Ms = 61000 });
                Assert.Equal(0, d.Editor.RunLine, "nothing is marked when it ends");
                Assert.Contains("A ran 1m 1s", d.StatusText, "and the summary is the line under the button");
            }
        }

        public static void TestAMistakeIsShownAsALineInTheOutput()
        {
            Assert.True(ScriptsDialog.IsProblemLine("Line 12: that calls something that isn't a function"), "a mistake");
            Assert.False(ScriptsDialog.IsProblemLine("The line 12: not one"), "printed text is not");
            Assert.False(ScriptsDialog.IsProblemLine("Line: none"), "nor is this");
            FakeScriptHost h = Host("A");
            using (ScriptsDialog d = Open(h, "A"))
            {
                d.RunOrStop();
                ScriptResult r = new ScriptResult { Name = "A", Ms = 2000, Problem = "Line 3: there is no key called \"nokey\"" };
                h.RaiseEnded("A", r);
                Assert.Equal("Line 3: there is no key called \"nokey\"|A ran 2s.", string.Join("|", d.OutputLines.ToArray()), "the mistake, then how long it ran");
                Assert.Equal("Line 3: there is no key called \"nokey\"", d.StatusText, "and the mistake is the line under the button");
                Assert.True(d.StatusIsWarning, "and the line under the button warns");
                d.ClearOutput();
                Assert.Equal(0, d.OutputLines.Count, "Clear empties it");
            }
        }

        public static void TestOutputKeepsTheLastFiveHundredLines()
        {
            FakeScriptHost h = Host("A");
            using (ScriptsDialog d = Open(h, "A"))
            {
                for (int i = 1; i <= 620; i++) h.RaiseOutput("A", "line " + i);
                List<string> lines = d.OutputLines;
                Assert.True(lines.Count <= 500 && lines.Count >= 400, "about five hundred (" + lines.Count + ")");
                Assert.Equal("line 620", lines[lines.Count - 1], "the newest is kept");
                Assert.True(!lines.Contains("line 1"), "the oldest went");
            }
        }

        public static void TestAKeyThatClashesIsSaidAndNotKept()
        {
            FakeScriptHost h = Host("A");
            h.KeyRefusal = "F10 is already used to stop macros. Choose another.";
            using (ScriptsDialog d = Open(h, "A"))
            {
                Assert.Equal(h.KeyRefusal, d.SetKey(0x79), "refused with the reason");
                Assert.Equal(h.KeyRefusal, d.KeyLine, "which is shown under the key");
                Assert.True(d.KeyLineIsWarning, "as a warning");
                Assert.Equal((byte)0, h.Files.Settings("A").Key, "and not kept");
                Assert.Equal("Choose a key", d.KeyText, "the button still asks for one");
                Assert.Equal(0, h.Saved, "nothing was saved");

                h.KeyRefusal = null;
                Assert.Equal(null, d.SetKey(0x75), "another is taken");
                Assert.Equal("Roblox gets the key too, so pick one the game doesn't use.", d.KeyLine, "the usual line is back");
                Assert.False(d.KeyLineIsWarning, "not a warning");
                Assert.Equal((byte)0x75, h.Files.Settings("A").Key, "kept in its settings");
                Assert.Equal("F6", d.KeyText, "named on the button");
                Assert.Equal(1, h.Saved, "and the main window was told, to listen for it");

                d.ClearKey();
                Assert.Equal((byte)0, h.Files.Settings("A").Key, "No key takes it off");
                Assert.Equal("Choose a key", d.KeyText, "the button asks again");
                Assert.Equal(2, h.Saved, "and the main window was told again");
            }
        }

        public static void TestWherePlaysOnIsKeptAndNeverLost()
        {
            FakeScriptHost h = Host("A");
            ScriptSettings s = h.Files.Settings("A");
            s.PlaysOn = "Gone";
            h.Files.SaveSettings("A", s);
            using (ScriptsDialog d = Open(h, "A", "Vlad", "Alt"))
            {
                Assert.Equal("Gone", d.PlaysOnText, "an account this PC no longer lists is still shown, not reset");
                Assert.Equal("Gone", h.Files.Settings("A").PlaysOn, "and kept");
            }
            s.PlaysOn = "Alt";
            h.Files.SaveSettings("A", s);
            using (ScriptsDialog d = Open(h, "A", "Vlad", "Alt"))
            {
                Assert.Equal("Alt", d.PlaysOnText, "an account");
                s.PlaysOn = ScriptSettings.SCREEN;
                h.Files.SaveSettings("A", s);
            }
            using (ScriptsDialog d = Open(h, "A", "Vlad", "Alt"))
            {
                Assert.Equal("Anywhere on my screen", d.PlaysOnText, "the whole screen");
                Assert.False(d.SummaryToDiscord, "no Discord summary until asked");
            }
            s.PlaysOn = ScriptSettings.FRONT;
            s.SummaryToDiscord = true;
            h.Files.SaveSettings("A", s);
            using (ScriptsDialog d = Open(h, "A"))
            {
                Assert.Equal("The Roblox window in front", d.PlaysOnText, "the Roblox window in front");
                Assert.True(d.SummaryToDiscord, "and the Discord tick as it was left");
            }
        }

        public static void TestAFunctionClickPutsItInAtTheCursor()
        {
            FakeScriptHost h = Host("A");
            using (ScriptsDialog d = Open(h, "A"))
            {
                d.Editor.Box.Select(d.Editor.Box.TextLength, 0);
                ScriptFunction click = null;
                foreach (ScriptFunction f in ScriptFunctions.All) if (f.Name == "click") click = f;
                d.Functions.Pick(click);
                Assert.True(d.Editor.PlainText.EndsWith("click(50, 50)"), "ready to change, where the cursor was");
                Assert.True(d.Dirty, "and the script is unsaved");
                Assert.Equal(ScriptFunctions.All.Length, d.Functions.Rows.Count, "every function is in the list");
            }
        }

        public static void TestAPictureIsPutInByItsNameInQuotes()
        {
            FakeScriptHost h = Host("A");
            using (ScriptsDialog d = Open(h, "A"))
            {
                d.Editor.Box.Select(d.Editor.Box.TextLength, 0);
                d.Editor.Insert("find(");
                d.InsertPicture("Buy button");
                Assert.True(d.Editor.PlainText.EndsWith("find(\"Buy button\""), "named, in quotes, where the cursor was");
            }
        }

        public static void TestNewExampleRenameAndRemove()
        {
            FakeScriptHost h = Host("A");
            using (ScriptsDialog d = Open(h, "A"))
            {
                Assert.Equal(null, d.NewScript("Mine"), "a new script");
                Assert.Equal("Mine", d.Current, "is shown");
                Assert.Contains("Write your script here", d.Editor.PlainText, "with a note to start from");
                Assert.Equal("A,Mine", string.Join(",", d.Listed.ToArray()), "and listed");
                Assert.Contains("already a script", d.NewScript("mine"), "a name that is taken is refused, whatever its case");
                Assert.Contains("name", d.NewScript(""), "and so is none");

                Assert.Equal(null, d.AddExample(0), "an example");
                Assert.Equal("Farm loop", d.Current, "under its own name");
                Assert.Contains("keydown(\"w\")", d.Editor.PlainText, "with its code");
                Assert.Equal(null, d.AddExample(0), "twice");
                Assert.Equal("Farm loop 2", d.Current, "the second does not replace the first");

                Assert.Equal(null, d.Rename("Walk"), "renamed");
                Assert.Equal("Walk", d.Current, "shown by its new name");
                Assert.True(h.Files.Exists("Walk") && !h.Files.Exists("Farm loop 2"), "on disk");
                Assert.Contains("already", d.Rename("Mine"), "to a name that is taken: refused");

                Assert.Equal(null, d.Remove(), "removed");
                Assert.False(h.Files.Exists("Walk"), "gone");
                Assert.True(d.Current != null, "another is shown");
            }
        }

        public static void TestTheWindowHearsOnlyWhileItIsOpen()
        {
            FakeScriptHost h = Host("A");
            ScriptsDialog d = Open(h, "A");
            Assert.True(h.Listened, "listening");
            d.Dispose();
            Assert.False(h.Listened, "not once it is gone");
            h.RaiseOutput("A", "late");
        }

        public static void TestTheLicencesAreInTheApp()
        {
            Assert.Contains("MoonSharp", Notices.Text(), "the notice packed into the app names MoonSharp");
            using (NoticesDialog d = new NoticesDialog("MoonSharp. Copyright and the disclaimer."))
            {
                bool found = false;
                foreach (TextBox t in Boxes(d)) if (t.Text.Contains("MoonSharp") && t.ReadOnly) found = true;
                Assert.True(found, "shown in a box that can be scrolled and not typed in");
            }
        }

        static List<TextBox> Boxes(Control c)
        {
            List<TextBox> found = new List<TextBox>();
            foreach (Control k in c.Controls)
            {
                if (k is TextBox) found.Add((TextBox)k);
                found.AddRange(Boxes(k));
            }
            return found;
        }

        // ---------- making scripts ----------

        static Macro Recorded(string name)
        {
            Macro m = new Macro();
            m.Name = name;
            m.Steps.Add(MacroStep.Key(0x45, 50));
            m.Steps.Add(MacroStep.Wait(500));
            m.Steps.Add(MacroStep.Click(0.5, 0.8, false));
            return m;
        }

        public static void TestAMacroBecomesAScript()
        {
            FakeScriptHost h = Host("Taken");
            Assert.Equal(null, ScriptMaking.FromMacro(h, Recorded("Open the shop"), "Open the shop"), "made");
            string code = h.Files.Load("Open the shop");
            Assert.Contains("press(\"e\")", code, "its steps, one line each");
            Assert.Contains("click(50, 80)", code, "with places in %");
            Assert.Contains("already a script", ScriptMaking.FromMacro(h, Recorded("x"), "taken"), "a name that is taken is refused");
            Assert.Contains("nothing", ScriptMaking.FromMacro(h, new Macro(), "Empty"), "so is a macro with no steps");
            Assert.Contains("ready", ScriptMaking.FromMacro(null, Recorded("x"), "No host"), "and no main window to make it with");
            Assert.False(h.Files.Exists("Empty") || h.Files.Exists("No host"), "nothing was made");
        }

        public static void TestAScriptNameIsAlwaysUsable()
        {
            FakeScriptHost h = Host("Farm", "Farm 2");
            Assert.Equal("Farm 3", ScriptMaking.UniqueName(h.Files, "Farm"), "the next free number");
            Assert.Equal("Other", ScriptMaking.UniqueName(h.Files, "Other"), "a free name as it is");
            Assert.Equal("ab", ScriptMaking.UniqueName(h.Files, "a/b:"), "what a file can't have is left out");
            Assert.Equal("New script", ScriptMaking.UniqueName(h.Files, "  "), "nothing at all");
            Assert.Equal(null, ScriptStore.NameProblem(ScriptMaking.UniqueName(h.Files, new string('x', 100))), "and never too long");
        }

        public static void TestTheMacrosListCanSaveAMacroAsAScript()
        {
            FakeScriptHost h = Host();
            WatchKit k = Kit(h);
            k.Store.Macros.Add(Recorded("Walk"));
            using (MacrosDialog d = new MacrosDialog(k))
            {
                Assert.Equal(null, d.SaveAsScript(0, "Walk script"), "saved");
                Assert.True(h.Files.Exists("Walk script"), "a script now");
                Assert.Equal(1, k.Store.Macros.Count, "and the macro is still there");
                Assert.Contains("already", d.SaveAsScript(0, "Walk script"), "twice under one name is refused");
            }
        }

        class QuickHost : IMacroHost
        {
            readonly AutoClick clicker = new AutoClick();
            public Macro Quick;
            public AutoClick Clicker { get { return clicker; } }
            public void SaveClicker() { }
            public bool ClickerOn { get { return false; } }
            public string ToggleClicker() { return null; }
            public byte StopKey { get { return 0x79; } set { } }
            public string StopKeyProblem(byte vk) { return null; }
            public void StopEverything() { }
            public int SessionsRunning { get { return 0; } }
            public string KeyUseProblem(byte vk, string usedFor, string editing) { return null; }
            public void MacrosSaved() { }
            public string PlayAnywhere(Macro m) { return null; }
            public bool IsPlaying(string tag) { return false; }
            public bool StopTag(string tag) { return true; }
            public Macro QuickRecording
            {
                get { return Quick == null ? null : Quick.Copy(); }
                set { Quick = value == null ? null : value.Copy(); }
            }
        }

        public static void TestQuickRecordCanSaveItsRecordingAsAScript()
        {
            FakeScriptHost h = Host();
            QuickHost q = new QuickHost();
            WatchKit k = Kit(h);
            using (QuickRecordCard c = new QuickRecordCard(q, delegate { return k; }, delegate { return null; }, delegate(string s) { }, 862))
            {
                Assert.False(c.CanSaveAsScript, "nothing recorded: nothing to save");
                Assert.Contains("Record something", c.SaveAsScript("Rec"), "and it says why");
                q.Quick = Recorded("Quick recording");
                q.Quick.Anywhere = true;
                c.Reload();
                Assert.True(c.CanSaveAsScript, "a recording: the button is there");
                Assert.Equal(null, c.SaveAsScript("Rec"), "saved");
                Assert.True(h.Files.Load("Rec").Contains("use(\"screen\")"), "and it plays on the screen, as the recording does");
                Assert.Contains("Saved as the script Rec", c.StatusText, "the card says so");
            }
            WatchKit none = Kit(h);
            none.Scripts = null;
            using (QuickRecordCard c = new QuickRecordCard(q, delegate { return none; }, delegate { return null; }, delegate(string s) { }, 862))
                Assert.False(c.CanSaveAsScript, "with no main window to make scripts, there is no button");
        }

        // ---------- the Automation page ----------

        public static void TestTheAutomationPageHasAScriptsTileThatFits()
        {
            // On a screen tall enough for the whole window; the short one is
            // OverlapTests.TestOnAShortScreenEveryPageScrollsToItsEnd.
            MainForm.ScreenHeightForTests = 2000;
            try
            {
            using (MainForm f = new MainForm(false))
            {
                f.ShowPage(MainForm.PAGE_AUTO);
                AutomationPage page = (AutomationPage)f.PageNow;
                page.ShowStates();
                Assert.True(page.StateText(6).Length > 0, "the Scripts tile says how many there are");
                Assert.Contains("Nothing is playing", page.StateText(5), "the numbers of the others are as they were");

                List<Card> cards = new List<Card>();
                foreach (Control c in page.Controls) if (c is Card) cards.Add((Card)c);
                Assert.Equal(8, cards.Count, "six tools, the Stop strip and Quick Record");
                for (int i = 0; i < cards.Count; i++)
                {
                    Assert.True(cards[i].Bottom <= page.Height - ShellPage.FOOT_H, "a card ends above the line at the foot of the page");
                    Assert.True(cards[i].Right <= page.Width, "and within the page's width");
                    for (int j = i + 1; j < cards.Count; j++)
                        Assert.True(!cards[i].Bounds.IntersectsWith(cards[j].Bounds), "no two cards overlap");
                }
            }
            }
            finally { MainForm.ScreenHeightForTests = 0; }
        }
    }
}
