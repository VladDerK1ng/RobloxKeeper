using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Loaders;

namespace RobloxKeeper
{
    // How a script's run ended, in what the activity list says.
    class ScriptResult
    {
        public string Name;
        public long Ms;
        public List<KeyValuePair<string, double>> Counters = new List<KeyValuePair<string, double>>();

        // Why it stopped, when that is worth saying: a mistake ("Line 12:
        // ..."), another window coming to the front, its window closing.
        // Null when it ended by itself, by stop(), or was stopped on purpose.
        public string Problem;

        public string Summary { get { return ScriptSummary.Line(Name, Ms, Counters, Problem); } }
    }

    // One run of one Lua script.
    //
    // The script runs in MoonSharp's soft sandbox - no io, no os.execute, no
    // loading files or libraries - and the only way out of it is the
    // functions below. They play the game from outside, as a person does:
    // keys, the mouse, what is on the screen. Nothing is put into Roblox.
    //
    // It runs as a coroutine that MoonSharp suspends by itself every thousand
    // instructions, so between resumes the run can look at whether it was
    // stopped - a `while true do end` still ends. A debugger that does nothing
    // but note each line as it starts gives the line it is on: MoonSharp 2.0
    // does not say where a function was called from. Stopping is also felt
    // there, so a runaway loop inside a sort or a gsub - where MoonSharp does
    // not suspend - still ends. A stop is thrown as ScriptStopped, which is
    // not one of MoonSharp's own exceptions and so is never caught by pcall.
    //
    // Everything MoonSharp is kept in this class and in no class the app's
    // startup touches: the interpreter is found when the first script runs
    // (see LuaRuntime).
    class LuaRun
    {
        // Suspended this often, in Lua instructions.
        public const int YIELD_EVERY = 1000;

        // A pop-up, or a Discord message, this soon after the last is left
        // out: a loop could send one every frame.
        public const int NOTIFY_EVERY_MS = 5000;
        public const int DISCORD_EVERY_MS = 2000;
        const int REMEMBER_EVERY_MS = 1000;
        const int DISCORD_MAX = 1900;
        const int TYPE_PIECE = 20;
        const int CHAT_MAX = 200;

        // say() goes to the activity list, which is kept: a loop saying
        // something every round would bury everything else there.
        public const int SAY_MAX = 20;
        public const int SAY_EVERY_MS = 10000;

        // One line printed or said, at most; a status, at most. The output
        // pane and the activity list are text boxes on the window's thread,
        // which a line of megabytes would hold up.
        public const int LINE_MAX = 2000;
        const int STATUS_MAX = 200;

        // How deep the library's own functions may call back into the script
        // - a sort's comparison sorting again, a gsub's function calling
        // gsub. Each level is C# calling Lua on the thread's own stack, and
        // running out of that stack ends the whole app at once, with no way
        // to catch it: the anti-AFK would stop and idle accounts be kicked.
        public const int MAX_DEPTH = 100;

        // The script's own thread is given this much stack, so MAX_DEPTH
        // levels of the heaviest of those (table.sort) fit with room to spare.
        public const int STACK_BYTES = 16 << 20;

        // Text one string.rep may make, and text one gsub or table.concat
        // may make. Each of those is a single step to MoonSharp, so nothing
        // else could stop one building gigabytes.
        public const int REP_MAX = 1024 * 1024;
        public const int BUILT_MAX = 16 * 1024 * 1024;
        public const int UNPACK_MAX = 100000;

        const string TOO_DEEP = "that goes too deep - something keeps calling itself (through sort, gsub or print)";

        // Lua functions inside each other, at most - see LineTracker.Update.
        public const int MAX_CALLS = 1000;
        const string TOO_MANY_CALLS = "that goes too deep - a function keeps calling itself (1000 calls inside each other at most)";

        // What stop() in the script means: it ended itself, nothing to say.
        const string BY_ITSELF = "the script ended itself";

        public const string UNTRUSTED_SCREEN = "This script came from someone else, so it can only play on Roblox windows. Open it in Scripts and press 'I trust this script' to lift that.";

        readonly string name;
        readonly string code;
        readonly IScriptDesk desk;
        readonly IScriptOutlet outlet;
        readonly ScriptStore store;
        readonly ScriptPlayer player;
        readonly int epoch;
        volatile bool stopping;
        readonly List<KeyValuePair<string, double>> counters = new List<KeyValuePair<string, double>>();
        Dictionary<string, object> memory;
        bool memoryDirty;
        long memorySavedMs = long.MinValue / 2;
        long notifiedMs = long.MinValue / 2, discordMs = long.MinValue / 2;
        bool notifySaid, discordSaid;
        long began;
        int lastLine = -1;
        Script script;
        LineTracker tracker;
        long saySince = long.MinValue / 2;
        int saidSince;
        bool sayLeftOutSaid;
        int depth;

        // Why the run must end, once something has said so: kept, because
        // MoonSharp's table.sort turns any mistake inside its comparison into
        // one of its own errors, which pcall could then catch and carry on
        // past. Every instruction after looks here and throws it again.
        string endWhy;
        bool endIsStop;

        // How much more memory the whole app may hold while it runs; past
        // that, the run ends. The app's own memory is counted too, so this
        // is growth from when it started.
        public long MemoryBudget = 512L * 1024 * 1024;
        long memoryAtStart;
        bool collected;
        int collectedAt;

        // False for a script that came from someone else and has not been
        // looked at: it only plays on Roblox windows, never the whole screen,
        // where it could type into anything.
        public bool Trusted = true;

        // store: where what it remembers is kept, or null to keep nothing.
        public LuaRun(string name, string code, IScriptDesk desk, ScriptTarget target, IScriptOutlet outlet, ScriptStore store)
        {
            this.name = name;
            this.code = code ?? "";
            this.desk = desk;
            this.outlet = outlet;
            this.store = store;
            // The stop key's count as it was asked to start: pressed after
            // that, it stops it.
            epoch = MacroStop.Epoch;
            player = new ScriptPlayer(desk, target, delegate { return StopAsked; });
        }

        public string Name { get { return name; } }
        public ScriptTarget Target { get { return player.Target; } }

        // Its Stop, its key again, Stop everything: felt at once, also in the
        // middle of a wait.
        public void Stop() { stopping = true; }
        public bool StopAsked { get { return stopping || MacroStop.Epoch != epoch; } }

        // On a thread of its own. ended is called on that thread, last.
        public Thread Start(Action<ScriptResult> ended)
        {
            // A bigger stack than the 1 MB a thread gets, so the depth the
            // run allows (MAX_DEPTH) is always reached long before the end of
            // the stack.
            Thread t = new Thread(delegate()
            {
                ScriptResult r;
                try { r = Run(); }
                catch (Exception ex)
                {
                    // Never out of the thread: that would end the app.
                    r = new ScriptResult();
                    r.Name = name;
                    r.Problem = Plain(ex.Message);
                }
                if (ended != null) try { ended(r); } catch { }
            }, STACK_BYTES);
            t.IsBackground = true;
            t.Name = "Script: " + name;
            t.Start();
            return t;
        }

        // The whole run, on this thread, to its end. Never throws.
        public ScriptResult Run()
        {
            ScriptResult result = new ScriptResult();
            result.Name = name;
            began = desk.NowMs;
            memoryAtStart = GC.GetTotalMemory(false);
            string why = null;
            desk.Begin();
            try
            {
                // MoonSharp reads code by calling itself for every bracket and
                // block, and for every link of a long chain like a+a+a...:
                // code built to go deep enough would end the app (see
                // MAX_DEPTH). Such code is refused before it is read.
                string shape = LuaShape.Problem(code);
                if (shape != null) throw new ScriptFault(shape);

                script = Build();
                DynValue fn;
                try { fn = script.LoadString(code, null, name); }
                catch (SyntaxErrorException ex) { throw new ScriptFault(LineOf(ex) + Plain(ex.Message)); }

                DynValue co = script.CreateCoroutine(fn);
                co.Coroutine.AutoYieldCounter = YIELD_EVERY;
                while (true)
                {
                    if (StopAsked) throw new ScriptStopped(MacroPlayer.CANCELLED);
                    co.Coroutine.Resume();
                    if (co.Coroutine.State == CoroutineState.Dead) break;
                    // Suspended part-way through its own work: the nudge is
                    // let in, and a keyboard nothing is pressed on given up.
                    player.Between();
                    KeepMemory(false);
                }
            }
            catch (ScriptStopped s) { why = s.Message; }
            catch (ScriptFault f)
            {
                why = f.Message.StartsWith("Line ", StringComparison.Ordinal) ? f.Message : LineNow() + f.Message;
            }
            catch (InterpreterException ex) { why = LineOf(ex) + Plain(ex.Message); }
            catch (OutOfMemoryException) { why = LineNow() + "that uses too much memory"; }
            catch (Exception ex) { why = LineNow() + Plain(ex.Message); }
            finally
            {
                player.Finish();
                try { KeepMemory(true); } catch { }
                try { if (outlet != null) outlet.Status(null); } catch { }
                desk.End();
            }

            // What was said first wins over what it turned into on the way
            // out (a sort's "invalid order function").
            if (endWhy != null) why = endWhy;
            result.Ms = desk.NowMs - began;
            result.Counters.AddRange(counters);
            if (why != null && why != BY_ITSELF && why != MacroPlayer.CANCELLED) result.Problem = why;
            return result;
        }

        // ---------- the sandbox ----------

        Script Build()
        {
            // The soft sandbox has no io, no os.execute and nothing that
            // loads a file. Coroutines are left out as well: one made by the
            // script would run outside the suspending that lets it be stopped.
            // So are json - its writer follows a table that holds itself
            // forever, which ends the app - and dynamic, which reads code
            // where the depth check before LoadString never sees it.
            Script s = new Script(CoreModules.Preset_SoftSandbox & ~(CoreModules.Coroutine | CoreModules.Json | CoreModules.Dynamic));
            s.Options.DebugPrint = delegate(string text) { if (outlet != null) outlet.Print(Cap(text)); };
            s.Options.ScriptLoader = new NoFiles();
            foreach (string gone in new string[] { "dofile", "loadfile", "require", "load", "loadsafe" })
                s.Globals.Set(gone, DynValue.Nil);
            // Lua's own type() is still there, under another name: the
            // design gives type() to typing text into the game.
            s.Globals.Set("typeof", s.Globals.Get("type"));

            tracker = new LineTracker(this);
            s.AttachDebugger(tracker);
            s.DebuggerEnabled = true;

            Guard(s);
            Bind(s);
            return s;
        }

        // Every function of Lua's own library counts how deep it is called
        // inside itself (see MAX_DEPTH), and the few that can make a huge
        // text in one go are checked before they do.
        void Guard(Script s)
        {
            List<DynValue> keys = new List<DynValue>();
            foreach (TablePair p in s.Globals.Pairs) keys.Add(p.Key);
            foreach (DynValue k in keys)
            {
                DynValue v = s.Globals.RawGet(k);
                if (v == null) continue;
                if (v.Type == DataType.ClrFunction) s.Globals.Set(k, Counted(v));
                else if (v.Type == DataType.Table && v.Table != s.Globals)
                {
                    Table lib = v.Table;
                    List<DynValue> names = new List<DynValue>();
                    foreach (TablePair p in lib.Pairs) names.Add(p.Key);
                    foreach (DynValue n in names)
                    {
                        DynValue f = lib.RawGet(n);
                        if (f != null && f.Type == DataType.ClrFunction) lib.Set(n, Counted(f));
                    }
                }
            }

            // The string table is also what ("x"):rep() finds, so these
            // cover the method form too.
            DynValue str = s.Globals.Get("string");
            if (str.Type == DataType.Table)
            {
                str.Table.Set("rep", DynValue.NewCallback(delegate(ScriptExecutionContext ctx, CallbackArguments a) { return Rep(a); }, "rep"));
                Before(str.Table, "gsub", GsubCheck);
                Before(str.Table, "format", FormatCheck);
                Before(str.Table, "byte", ByteCheck);
            }
            // MoonSharp's print joins everything it is given before the line
            // is cut (Cap), so print(table.unpack(t)) of many long texts
            // would build gigabytes in one step. This one joins as it goes
            // and stops past BUILT_MAX. Its __tostring calls are Lua, counted
            // by Counted like the one it replaces.
            if (s.Globals.Get("print").Type == DataType.ClrFunction)
                s.Globals.Set("print", Counted(DynValue.NewCallback(delegate(ScriptExecutionContext ctx, CallbackArguments a) { return Print(ctx, a); }, "print")));
            DynValue tbl = s.Globals.Get("table");
            if (tbl.Type == DataType.Table)
            {
                Before(tbl.Table, "concat", ConcatCheck);
                Before(tbl.Table, "unpack", UnpackCheck);
            }
            if (s.Globals.Get("unpack").Type == DataType.ClrFunction) Before(s.Globals, "unpack", UnpackCheck);
        }

        DynValue Counted(DynValue f)
        {
            CallbackFunction cb = f.Callback;
            return DynValue.NewCallback(delegate(ScriptExecutionContext ctx, CallbackArguments a)
            {
                if (depth >= MAX_DEPTH) throw Fail(TOO_DEEP);
                depth++;
                try { return cb.ClrCallback(ctx, a); }
                finally { depth--; }
            }, cb.Name);
        }

        // Runs check, which throws when the call would go too far, then the
        // function itself.
        static void Before(Table t, string fname, Action<CallbackArguments> check)
        {
            DynValue f = t.Get(fname);
            if (f.Type != DataType.ClrFunction) return;
            CallbackFunction cb = f.Callback;
            t.Set(fname, DynValue.NewCallback(delegate(ScriptExecutionContext ctx, CallbackArguments a)
            {
                check(a);
                return cb.ClrCallback(ctx, a);
            }, cb.Name));
        }

        // string.rep, as Lua's, but never more than REP_MAX of text.
        DynValue Rep(CallbackArguments a)
        {
            string text = Text(a, 0, "string.rep");
            double n = Number(a, 1, "string.rep", "how many times");
            string sep = Has(a, 2) ? Text(a, 2, "string.rep") : "";
            if (n < 1 || text.Length + sep.Length == 0) return DynValue.NewString("");
            double times = Math.Floor(n);
            double size = text.Length * times + sep.Length * (times - 1);
            if (size > REP_MAX) throw Fail("that text is too long - string.rep makes " + REP_MAX / (1024 * 1024) + " MB of text at most");
            StringBuilder sb = new StringBuilder((int)size);
            for (int i = 0; i < (int)times; i++)
            {
                if (i > 0) sb.Append(sep);
                sb.Append(text);
            }
            return DynValue.NewString(sb.ToString());
        }

        static string TextOrNull(DynValue v)
        {
            if (v == null) return null;
            if (v.Type == DataType.String) return v.String;
            if (v.Type == DataType.Number) return v.CastToString();
            return null;
        }

        // The most text gsub could make: the subject, each match's
        // replacement, and each %1 in it as long as the whole subject at most
        // (matches never overlap). A function as the replacement runs Lua for
        // every match, where the memory check sees it.
        void GsubCheck(CallbackArguments a)
        {
            string subject = a.Count > 0 ? TextOrNull(a[0]) : null;
            if (subject == null || a.Count < 3) return;
            double matches = subject.Length + 1;
            if (Has(a, 3) && a[3].Type == DataType.Number) matches = Math.Min(matches, Math.Max(0, Math.Floor(a[3].Number)));
            DynValue r = a[2];
            double worst;
            string with = TextOrNull(r);
            if (with != null)
            {
                int captures = 0;
                for (int i = 0; i + 1 < with.Length; i++)
                    if (with[i] == '%') { if (char.IsDigit(with[i + 1])) captures++; i++; }
                worst = subject.Length + matches * with.Length + (double)captures * subject.Length;
            }
            else if (r.Type == DataType.Table)
            {
                int longest = 0;
                foreach (TablePair p in r.Table.Pairs)
                {
                    string t = TextOrNull(p.Value);
                    if (t != null && t.Length > longest) longest = t.Length;
                }
                worst = subject.Length + matches * longest;
            }
            else return;
            if (worst > BUILT_MAX) throw Fail("that text would get too long - " + BUILT_MAX / (1024 * 1024) + " MB at most");
        }

        void ConcatCheck(CallbackArguments a)
        {
            if (a.Count < 1 || a[0].Type != DataType.Table) return;
            Table t = a[0].Table;
            string sep = a.Count > 1 ? TextOrNull(a[1]) ?? "" : "";
            double from = Has(a, 2) && a[2].Type == DataType.Number ? Math.Floor(a[2].Number) : 1;
            double to = Has(a, 3) && a[3].Type == DataType.Number ? Math.Floor(a[3].Number) : t.Length;
            if (to < from) return;
            double total = (to - from) * sep.Length;
            // What is there is counted until it is too much; a gap ends it,
            // and concat itself then says what is wrong.
            for (double i = from; i <= to && total <= BUILT_MAX; i++)
            {
                string piece = TextOrNull(t.Get(i));
                if (piece == null) return;
                total += piece.Length;
            }
            if (total > BUILT_MAX) throw Fail("that text would get too long - " + BUILT_MAX / (1024 * 1024) + " MB at most");
        }

        const string TOO_LONG = "that text would get too long - ";

        // print, as Lua's: each value as tostring makes it, tabs between.
        DynValue Print(ScriptExecutionContext ctx, CallbackArguments a)
        {
            // The line of the print itself: a __tostring run on the way
            // moves the line on to its own.
            string at = LineNow();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].IsVoid()) break;
                string piece = a.AsStringUsingMeta(ctx, i, "print") ?? "";
                if ((double)sb.Length + 1 + piece.Length > BUILT_MAX) throw Fail(at + TOO_LONG + BUILT_MAX / (1024 * 1024) + " MB at most");
                if (i > 0) sb.Append('\t');
                sb.Append(piece);
            }
            ctx.GetScript().Options.DebugPrint(sb.ToString());
            return DynValue.Nil;
        }

        // The most string.format could make: the format itself, and for
        // each value its text twice over (%q puts a \ before some
        // characters) and a width of up to 99 - Lua allows two digits. A
        // number is a few hundred characters at most (%.99f). Only texts and
        // numbers are taken; anything else, format itself refuses.
        void FormatCheck(CallbackArguments a)
        {
            string fmt = a.Count > 0 ? TextOrNull(a[0]) : null;
            if (fmt == null) return;
            double total = fmt.Length;
            for (int i = 1; i < a.Count && total <= BUILT_MAX; i++)
            {
                if (a[i].Type == DataType.String) total += 2.0 * a[i].String.Length + 99;
                else total += 512;
            }
            if (total > BUILT_MAX) throw Fail(TOO_LONG + BUILT_MAX / (1024 * 1024) + " MB at most");
        }

        // string.byte gives back one value for each character it is asked
        // for: all of a 16 MB text would be 16 million values at once, a
        // gigabyte. Its range is counted as Lua reads it - from i (1 when
        // left out) to j (i when left out), counting back from the end
        // when less than 1 - and held to UNPACK_MAX, as unpack is.
        void ByteCheck(CallbackArguments a)
        {
            string text = a.Count > 0 ? TextOrNull(a[0]) : null;
            if (text == null) return;
            double len = text.Length;
            double? i = Has(a, 1) ? a[1].CastToNumber() : 1;
            if (i == null) return;
            double? j = Has(a, 2) ? a[2].CastToNumber() : i;
            if (j == null) return;
            double from = Math.Floor(i.Value), to = Math.Floor(j.Value);
            // MoonSharp turns the indices into whole numbers its own way, and
            // one that is not a number or too big for that can come out
            // meaning the whole text (byte(2^31, -1) does). Such a range is
            // counted as the whole text, so it can't slip past.
            if (double.IsNaN(from) || double.IsNaN(to) || Math.Abs(from) > int.MaxValue || Math.Abs(to) > int.MaxValue)
            {
                from = 1;
                to = len;
            }
            if (from < 0) from = len + from + 1;
            if (to < 0) to = len + to + 1;
            if (from < 1) from = 1;
            if (to > len) to = len;
            if (to - from + 1 > UNPACK_MAX) throw Fail("that gives too many values at once - string.byte gives " + UNPACK_MAX + " at most");
        }

        void UnpackCheck(CallbackArguments a)
        {
            if (a.Count < 1 || a[0].Type != DataType.Table) return;
            double from = Has(a, 1) && a[1].Type == DataType.Number ? Math.Floor(a[1].Number) : 1;
            double to = Has(a, 2) && a[2].Type == DataType.Number ? Math.Floor(a[2].Number) : a[0].Table.Length;
            if (to - from + 1 > UNPACK_MAX) throw Fail("that unpacks too many values at once - " + UNPACK_MAX + " at most");
        }

        // ---------- ending, whatever is on the stack ----------

        // The run must end with this mistake: kept (see endWhy) and handed
        // back to throw.
        ScriptFault Fail(string why)
        {
            if (endWhy == null)
            {
                endWhy = why.StartsWith("Line ", StringComparison.Ordinal) ? why : LineNow() + why;
                endIsStop = false;
            }
            return new ScriptFault(endWhy);
        }

        // Before every instruction: an end already said is said again, the
        // stack has room, and memory is within the budget.
        void Check()
        {
            if (endWhy != null)
            {
                if (endIsStop) throw new ScriptStopped(endWhy);
                throw new ScriptFault(endWhy);
            }
            try { RuntimeHelpers.EnsureSufficientExecutionStack(); }
            catch (InsufficientExecutionStackException) { throw Fail(TOO_DEEP); }
            CheckMemory();
        }

        // What the app holds is read every instruction - it costs a few
        // nanoseconds. It counts rubbish not yet collected as well, so before
        // the run is ended for it the rubbish is collected and it is read
        // again; at most every two seconds, unless it is far over.
        void CheckMemory()
        {
            long used = GC.GetTotalMemory(false) - memoryAtStart;
            if (used <= MemoryBudget) return;
            int now = Environment.TickCount;
            if (collected && unchecked(now - collectedAt) < 2000 && used <= 2 * MemoryBudget) return;
            collected = true;
            collectedAt = now;
            used = GC.GetTotalMemory(true) - memoryAtStart;
            if (used > MemoryBudget) throw Fail("that uses too much memory - " + MemoryBudget / (1024 * 1024) + " MB at most");
        }

        // A printed or said line, cut to LINE_MAX.
        public static string Cap(string text)
        {
            if (text == null || text.Length <= LINE_MAX) return text;
            return text.Substring(0, LINE_MAX) + "...";
        }

        // Refuses every file: nothing a script names is read from disk.
        class NoFiles : IScriptLoader
        {
            public object LoadFile(string file, Table globalContext) { throw new ScriptRuntimeException("scripts can't open files"); }
            public string ResolveFileName(string filename, Table globalContext) { return null; }
            public string ResolveModuleName(string modname, Table globalContext) { return null; }
        }

        // Notes the line each statement starts on, and feels a stop there.
        class LineTracker : IDebugger
        {
            readonly LuaRun run;
            readonly DebuggerAction step = new DebuggerAction();
            public volatile int Line;

            public LineTracker(LuaRun run)
            {
                this.run = run;
                step.Action = DebuggerAction.ActionType.StepIn;
            }

            public DebuggerAction GetAction(int ip, SourceRef sourceref)
            {
                if (sourceref != null && !sourceref.IsClrLocation && sourceref.FromLine > 0) Line = sourceref.FromLine;
                if (run.StopAsked) throw new ScriptStopped(MacroPlayer.CANCELLED);
                if (Calls > MAX_CALLS) throw run.Fail(TOO_MANY_CALLS);
                run.Check();
                return step;
            }

            public DebuggerCaps GetDebuggerCaps() { return DebuggerCaps.CanDebugSourceCode | DebuggerCaps.HasLineBasedBreakpoints; }
            public void SetDebugService(DebugService debugService) { }
            public void SetSourceCode(SourceCode sourceCode) { }
            public void SetByteCode(string[] byteCode) { }
            public bool IsPauseRequested() { return false; }
            public bool SignalRuntimeException(ScriptRuntimeException ex) { return false; }
            public void SignalExecutionEnded() { }
            // MoonSharp hands over the whole call stack before each new line,
            // so its length is how deep Lua functions are inside each other.
            // Building that list costs as much as it is long, every line: a
            // function calling itself without end would get slower and
            // slower - and the nudge is only let in between lines - rather
            // than ever ending by itself. So it ends at MAX_CALLS.
            public void Update(WatchType watchType, IEnumerable<WatchItem> items)
            {
                if (watchType != WatchType.CallStack) return;
                ICollection<WatchItem> c = items as ICollection<WatchItem>;
                Calls = c != null ? c.Count : 0;
            }

            public int Calls;
            public List<DynamicExpression> GetWatchItems() { return new List<DynamicExpression>(); }
            public void RefreshBreakpoints(IEnumerable<SourceRef> refs) { }
        }

        // ---------- mistakes, in plain words ----------

        static readonly Regex At = new Regex(@"\((\d+),\d+(-\d+)?\)\s*:");

        string LineNow()
        {
            int line = tracker == null ? 0 : tracker.Line;
            return line > 0 ? "Line " + line + ": " : "";
        }

        string LineOf(InterpreterException ex)
        {
            Match m = At.Match(ex.DecoratedMessage ?? "");
            if (m.Success) return "Line " + m.Groups[1].Value + ": ";
            return LineNow();
        }

        // MoonSharp's words, made plainer where they are known.
        public static string Plain(string message)
        {
            string m = (message ?? "").Trim();
            if (m.Length == 0) return "something went wrong";
            if (m.StartsWith("attempt to call a nil value", StringComparison.Ordinal))
                return "that calls something that isn't a function - check how its name is spelled";
            if (m.StartsWith("attempt to call", StringComparison.Ordinal))
                return "that calls something that isn't a function";
            if (m.StartsWith("attempt to index a nil value", StringComparison.Ordinal))
                return "that looks inside something that has no value (nil)";
            if (m.StartsWith("attempt to perform arithmetic on a nil value", StringComparison.Ordinal))
                return "that does sums with something that has no value (nil)";
            if (m.StartsWith("attempt to concatenate a nil value", StringComparison.Ordinal))
                return "that joins text to something that has no value (nil)";
            if (m.StartsWith("attempt to compare", StringComparison.Ordinal))
                return "that compares two things that can't be compared (" + m.Substring("attempt to compare".Length).Trim() + ")";
            if (m.Contains("expected near '<eof>'") || m.Contains("expected near <eof>"))
                return "the script ends too soon - an \"end\" or a bracket is missing (" + m + ")";
            return m;
        }

        // ---------- the functions ----------

        delegate DynValue Body(CallbackArguments a);

        // Each function: a stop is felt first, the nudge is let in - a loop
        // of nothing but looks, with no wait, would otherwise hold the
        // keyboard far longer than a thousand instructions take - the line
        // it is on is said, and whatever it remembered is written now and
        // then.
        void Fn(Script s, string fname, Body body)
        {
            s.Globals.Set(fname, DynValue.NewCallback(delegate(ScriptExecutionContext ctx, CallbackArguments a)
            {
                try
                {
                    player.CheckStop();
                    player.Between();
                    int line = tracker.Line;
                    if (line != lastLine && line > 0)
                    {
                        lastLine = line;
                        if (outlet != null) outlet.Line(line);
                    }
                    KeepMemory(false);
                    return body(a);
                }
                // Kept, so a sort the call was inside can't turn it into
                // something pcall catches (see endWhy).
                catch (ScriptFault f) { throw Fail(f.Message); }
                catch (ScriptStopped st)
                {
                    if (endWhy == null) { endWhy = st.Message; endIsStop = true; }
                    throw;
                }
            }, fname));
        }

        void Bind(Script s)
        {
            // ----- keys -----
            Fn(s, "press", delegate(CallbackArguments a)
            {
                byte vk = Key(a, 0, "press");
                int hold = Has(a, 1) ? Ms(a, 1, "press", "how long to hold it") : 50;
                player.Act(new MacroStep[] { MacroStep.Key(vk, hold) });
                return DynValue.Nil;
            });
            Fn(s, "keydown", delegate(CallbackArguments a) { player.KeyDown(Key(a, 0, "keydown")); return DynValue.Nil; });
            Fn(s, "keyup", delegate(CallbackArguments a) { player.KeyUp(Key(a, 0, "keyup")); return DynValue.Nil; });
            Fn(s, "type", delegate(CallbackArguments a)
            {
                // A piece at a time, so a long text makes way for the nudge
                // between pieces: typing is never a long step to a macro.
                string text = Text(a, 0, "type");
                for (int i = 0; i < text.Length; i += TYPE_PIECE)
                    player.Act(new MacroStep[] { MacroStep.Type(text.Substring(i, Math.Min(TYPE_PIECE, text.Length - i))) });
                return DynValue.Nil;
            });
            Fn(s, "chat", delegate(CallbackArguments a)
            {
                string said = Text(a, 0, "chat");
                // Roblox's chat takes this much; more would be cut off there.
                if (said.Length > CHAT_MAX) throw new ScriptFault("chat says " + CHAT_MAX + " letters at most - Roblox cuts off the rest");
                MacroStep t = MacroStep.Type(said);
                t.InChat = true;
                player.Act(new MacroStep[] { t });
                return DynValue.Nil;
            });

            // ----- the mouse -----
            Fn(s, "click", delegate(CallbackArguments a) { player.Act(new MacroStep[] { ClickStep(a, "click") }); return DynValue.Nil; });
            Fn(s, "doubleclick", delegate(CallbackArguments a)
            {
                MacroStep first = ClickStep(a, "doubleclick");
                // The second where the first left the pointer, straight after:
                // that is what makes it a double-click.
                player.Act(new MacroStep[] { first, MacroStep.ClickHere(first.RightButton) });
                return DynValue.Nil;
            });
            Fn(s, "move", delegate(CallbackArguments a)
            {
                player.Act(new MacroStep[] { MacroStep.Move(Place(a, 0, "move"), Place(a, 1, "move")) });
                return DynValue.Nil;
            });
            Fn(s, "drag", delegate(CallbackArguments a)
            {
                double x1 = Place(a, 0, "drag"), y1 = Place(a, 1, "drag"), x2 = Place(a, 2, "drag"), y2 = Place(a, 3, "drag");
                int ms = 500;
                bool right = false;
                for (int i = 4; i < a.Count; i++)
                {
                    if (IsRight(a[i], "drag")) right = true;
                    else if (!a[i].IsNil()) ms = Ms(a, i, "drag", "how long the drag takes");
                }
                player.Act(new MacroStep[] { MacroStep.Drag(x1, y1, x2, y2, Math.Max(MacroStep.CLICK_MS, ms), right) });
                return DynValue.Nil;
            });
            Fn(s, "scroll", delegate(CallbackArguments a)
            {
                double n = Number(a, 0, "scroll", "how many notches (up when more than 0, down when less)");
                int notches = (int)Math.Round(n);
                if (notches == 0) return DynValue.Nil;
                if (Math.Abs(notches) > 100) throw new ScriptFault("scroll goes 100 notches at most at a time");
                MacroStep st;
                if (Has(a, 1)) st = MacroStep.Scroll(Place(a, 1, "scroll"), Place(a, 2, "scroll"), notches);
                else { st = MacroStep.Scroll(0, 0, notches); st.AtPointer = true; }
                player.Act(new MacroStep[] { st });
                return DynValue.Nil;
            });

            // ----- time -----
            Fn(s, "wait", delegate(CallbackArguments a)
            {
                player.Wait(Has(a, 0) ? Ms(a, 0, "wait", "how many seconds") : 0);
                return DynValue.Nil;
            });
            Fn(s, "elapsed", delegate(CallbackArguments a) { return DynValue.NewNumber((desk.NowMs - began) / 1000.0); });

            // ----- the screen -----
            Fn(s, "find", delegate(CallbackArguments a)
            {
                return Where(Look(a, "find", 0, true));
            });
            Fn(s, "waitfor", delegate(CallbackArguments a)
            {
                return Where(Look(a, "waitfor", Has(a, 1) ? Ms(a, 1, "waitfor", "how many seconds to wait") : 10000, true));
            });
            Fn(s, "waitgone", delegate(CallbackArguments a)
            {
                PictureLook l = Look(a, "waitgone", Has(a, 1) ? Ms(a, 1, "waitgone", "how many seconds to wait") : 10000, false);
                return DynValue.NewBoolean(!l.Found);
            });
            Fn(s, "clickpicture", delegate(CallbackArguments a)
            {
                int ms = 0;
                bool right = false;
                for (int i = 1; i < a.Count; i++)
                {
                    if (IsRight(a[i], "clickpicture")) right = true;
                    else if (!a[i].IsNil()) ms = Ms(a, i, "clickpicture", "how many seconds to wait for it");
                }
                PictureLook l = Look(a, "clickpicture", ms, true);
                if (!l.Found) return DynValue.False;
                // Where it is now, as a place on what it plays on - the click
                // lands on that same pixel.
                Rectangle area = player.Area;
                double fx = (l.At.X - area.Left) / (double)Math.Max(1, area.Width - 1);
                double fy = (l.At.Y - area.Top) / (double)Math.Max(1, area.Height - 1);
                player.Act(new MacroStep[] { MacroStep.Click(fx, fy, right) });
                return DynValue.True;
            });
            Fn(s, "color", delegate(CallbackArguments a)
            {
                Color c = player.ColorAt(Place(a, 0, "color") , Place(a, 1, "color"));
                return DynValue.NewTuple(DynValue.NewNumber(c.R), DynValue.NewNumber(c.G), DynValue.NewNumber(c.B));
            });
            Fn(s, "read", delegate(CallbackArguments a) { return DynValue.NewString(player.Read()); });
            Fn(s, "see", delegate(CallbackArguments a)
            {
                string words = Text(a, 0, "see");
                int ms = Has(a, 1) ? Ms(a, 1, "see", "how many seconds to keep looking") : 0;
                return DynValue.NewBoolean(player.See(words, ms));
            });

            // ----- windows -----
            Fn(s, "windows", delegate(CallbackArguments a)
            {
                Table list = new Table(script);
                foreach (WatchedClient c in desk.Clients())
                {
                    if (c == null) continue;
                    Table w = new Table(script);
                    w.Set("name", DynValue.NewString(c.Label ?? ""));
                    w.Set("account", string.IsNullOrEmpty(c.AccountName) ? DynValue.Nil : DynValue.NewString(c.AccountName));
                    list.Append(DynValue.NewTable(w));
                }
                return DynValue.NewTable(list);
            });
            Fn(s, "use", delegate(CallbackArguments a)
            {
                player.Use(Choose(a));
                return DynValue.Nil;
            });

            // ----- telling you -----
            Fn(s, "say", delegate(CallbackArguments a)
            {
                string text = Text(a, 0, "say");
                long now = desk.NowMs;
                if (now - saySince >= SAY_EVERY_MS) { saySince = now; saidSince = 0; }
                if (saidSince >= SAY_MAX)
                {
                    if (!sayLeftOutSaid && outlet != null) outlet.Print("say: left out - " + SAY_MAX + " lines every " + SAY_EVERY_MS / 1000 + " seconds at most");
                    sayLeftOutSaid = true;
                    return DynValue.Nil;
                }
                saidSince++;
                if (outlet != null) outlet.Say(Cap(text));
                return DynValue.Nil;
            });
            Fn(s, "notify", delegate(CallbackArguments a)
            {
                string text = Text(a, 0, "notify");
                long now = desk.NowMs;
                if (now - notifiedMs < NOTIFY_EVERY_MS)
                {
                    if (!notifySaid && outlet != null) outlet.Print("notify: left out - one pop-up every " + NOTIFY_EVERY_MS / 1000 + " seconds at most");
                    notifySaid = true;
                    return DynValue.Nil;
                }
                notifiedMs = now;
                if (outlet != null) outlet.Notify(text);
                return DynValue.Nil;
            });
            Fn(s, "discord", delegate(CallbackArguments a)
            {
                string text = Text(a, 0, "discord");
                long now = desk.NowMs;
                if (now - discordMs < DISCORD_EVERY_MS)
                {
                    if (!discordSaid && outlet != null) outlet.Print("discord: left out - one message every " + DISCORD_EVERY_MS / 1000 + " seconds at most");
                    discordSaid = true;
                    return DynValue.Nil;
                }
                discordMs = now;
                if (text.Length > DISCORD_MAX) text = text.Substring(0, DISCORD_MAX) + "...";
                if (outlet != null) outlet.Discord(text);
                return DynValue.Nil;
            });
            Fn(s, "status", delegate(CallbackArguments a)
            {
                string text = Has(a, 0) ? a[0].CastToString() : null;
                if (text != null && text.Length > STATUS_MAX) text = text.Substring(0, STATUS_MAX);
                if (outlet != null) outlet.Status(string.IsNullOrEmpty(text) ? null : text);
                return DynValue.Nil;
            });

            // ----- ending -----
            Fn(s, "stop", delegate(CallbackArguments a) { throw new ScriptStopped(BY_ITSELF); });

            // ----- counting and remembering -----
            Fn(s, "count", delegate(CallbackArguments a)
            {
                string what = Text(a, 0, "count");
                double by = Has(a, 1) ? Number(a, 1, "count", "how many to add") : 1;
                for (int i = 0; i < counters.Count; i++)
                    if (counters[i].Key == what)
                    {
                        counters[i] = new KeyValuePair<string, double>(what, counters[i].Value + by);
                        return DynValue.NewNumber(counters[i].Value);
                    }
                counters.Add(new KeyValuePair<string, double>(what, by));
                return DynValue.NewNumber(by);
            });
            Fn(s, "remember", delegate(CallbackArguments a)
            {
                string key = Text(a, 0, "remember");
                DynValue v = Has(a, 1) ? a[1] : DynValue.Nil;
                Memory();
                if (v.IsNil()) memory.Remove(key);
                else if (v.Type == DataType.Number) memory[key] = v.Number;
                else if (v.Type == DataType.Boolean) memory[key] = v.Boolean;
                else if (v.Type == DataType.String) memory[key] = v.String;
                else throw new ScriptFault("remember keeps numbers, text and true/false - not a " + v.Type.ToString().ToLowerInvariant());
                memoryDirty = true;
                KeepMemory(false);
                return DynValue.Nil;
            });
            Fn(s, "recall", delegate(CallbackArguments a)
            {
                string key = Text(a, 0, "recall");
                object v;
                if (!Memory().TryGetValue(key, out v)) return DynValue.Nil;
                if (v is double) return DynValue.NewNumber((double)v);
                if (v is bool) return DynValue.NewBoolean((bool)v);
                return DynValue.NewString(v as string ?? "");
            });
        }

        // ---------- memory ----------

        Dictionary<string, object> Memory()
        {
            if (memory == null) memory = store == null ? new Dictionary<string, object>() : store.Memory(name);
            return memory;
        }

        // Written at most once a second while it runs, and at the end: a loop
        // that remembers every round would otherwise write the disk as fast
        // as it can.
        void KeepMemory(bool now)
        {
            if (!memoryDirty || store == null || memory == null) return;
            long t = desk.NowMs;
            if (!now && t - memorySavedMs < REMEMBER_EVERY_MS) return;
            memoryDirty = false;
            memorySavedMs = t;
            store.SaveMemory(name, memory);
        }

        // ---------- reading what a function was given ----------

        static bool Has(CallbackArguments a, int i) { return i < a.Count && !a[i].IsNil(); }

        static double Number(CallbackArguments a, int i, string fname, string what)
        {
            if (!Has(a, i)) throw new ScriptFault(fname + " needs " + what);
            double? d = a[i].CastToNumber();
            if (!d.HasValue || double.IsNaN(d.Value) || double.IsInfinity(d.Value))
                throw new ScriptFault(fname + " needs a number for " + what + ", not " + Shown(a[i]));
            return d.Value;
        }

        // Seconds, as milliseconds. A day at most: longer is a mistake.
        static int Ms(CallbackArguments a, int i, string fname, string what)
        {
            double sec = Number(a, i, fname, what + " (in seconds)");
            if (sec < 0) throw new ScriptFault(fname + " can't wait less than 0 seconds");
            if (sec > 86400) throw new ScriptFault(fname + " waits a day at most");
            return (int)Math.Round(sec * 1000);
        }

        static string Text(CallbackArguments a, int i, string fname)
        {
            if (!Has(a, i)) throw new ScriptFault(fname + " needs some text, in quotes");
            DynValue v = a[i];
            if (v.Type == DataType.String) return v.String;
            if (v.Type == DataType.Number || v.Type == DataType.Boolean) return v.CastToString();
            throw new ScriptFault(fname + " needs some text, not " + Shown(v));
        }

        static string Shown(DynValue v)
        {
            if (v.Type == DataType.String) return "\"" + v.String + "\"";
            if (v.IsNil()) return "nil";
            return v.Type.ToString().ToLowerInvariant();
        }

        // A key by its name, as printed: "e", "space", "shift", "f1", "left".
        static byte Key(CallbackArguments a, int i, string fname)
        {
            if (!Has(a, i)) throw new ScriptFault(fname + " needs a key, like " + fname + "(\"e\")");
            string n = a[i].Type == DataType.Number ? a[i].CastToString() : a[i].Type == DataType.String ? a[i].String : null;
            byte vk = ScriptKeys.Parse(n);
            if (vk == 0) throw new ScriptFault("there is no key called " + Shown(a[i]) + ". Write it the way it is printed: \"e\", \"5\", \"space\", \"enter\", \"shift\", \"f5\", \"left\"");
            if (ScriptKeys.IsMouse(vk)) throw new ScriptFault("that is a button on the mouse, not a key - use click()");
            if (ScriptKeys.IsSystem(vk)) throw new ScriptFault(ScriptKeys.SYSTEM_REFUSED);
            return vk;
        }

        // % across or down, 0 to 100, as a fraction.
        static double Place(CallbackArguments a, int i, string fname)
        {
            double v = Number(a, i, fname, "a place: % across and % down, from 0 to 100");
            if (v < 0 || v > 100) throw new ScriptFault("a place goes from 0 to 100, and " + ScriptSummary.Number(v) + " is outside that");
            return v / 100;
        }

        static bool IsRight(DynValue v, string fname)
        {
            if (v.Type != DataType.String) return false;
            if (string.Equals(v.String, "right", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(v.String, "left", StringComparison.OrdinalIgnoreCase)) return false;
            throw new ScriptFault(fname + " takes \"right\" for the right button, not \"" + v.String + "\"");
        }

        // click() / click("right") / click(x, y) / click(x, y, "right").
        static MacroStep ClickStep(CallbackArguments a, string fname)
        {
            if (!Has(a, 0)) return MacroStep.ClickHere(false);
            if (a[0].Type == DataType.String && a[0].CastToNumber() == null)
                return MacroStep.ClickHere(IsRight(a[0], fname));
            double x = Place(a, 0, fname), y = Place(a, 1, fname);
            bool right = a.Count > 2 && !a[2].IsNil() && IsRight(a[2], fname);
            if (a.Count > 2 && !a[2].IsNil() && a[2].Type != DataType.String)
                throw new ScriptFault(fname + " takes \"right\" after the place, for the right button");
            return MacroStep.Click(x, y, right);
        }

        PictureLook Look(CallbackArguments a, string fname, int timeoutMs, bool wantSeen)
        {
            string pic = Text(a, 0, fname);
            string file, mask;
            if (!desk.FindPicture(pic, out file, out mask))
                throw new ScriptFault("there's no picture called \"" + pic + "\" - add it with Add a picture, or check its name");
            return player.WaitFor(file, mask, timeoutMs, wantSeen);
        }

        // x, y in % of what it plays on, or nil.
        DynValue Where(PictureLook l)
        {
            if (!l.Found) return DynValue.Nil;
            Rectangle area = player.Area;
            return DynValue.NewTuple(DynValue.NewNumber(Math.Round(ScriptPlayer.Across(area, l.At.X), 2)),
                                     DynValue.NewNumber(Math.Round(ScriptPlayer.Down(area, l.At.Y), 2)));
        }

        // use("screen"), use(2) - the second in windows() - or use("Client 2")
        // or use("an account's name").
        ScriptTarget Choose(CallbackArguments a)
        {
            if (!Has(a, 0)) throw new ScriptFault("use needs a window: use(1), use(\"Client 2\"), an account's name, or use(\"screen\")");
            WatchedClient[] list = desk.Clients();
            DynValue v = a[0];
            if (v.Type == DataType.Number)
            {
                int n = (int)v.Number;
                if (n < 1 || n > list.Length || n != v.Number)
                    throw new ScriptFault("there's no Roblox window number " + ScriptSummary.Number(v.Number) + " - " + list.Length + " " + (list.Length == 1 ? "is" : "are") + " open");
                return ScriptTarget.Of(list[n - 1]);
            }
            string want = Text(a, 0, "use").Trim();
            if (string.Equals(want, ScriptSettings.SCREEN, StringComparison.OrdinalIgnoreCase))
            {
                // On the screen nothing checks which window is in front, so
                // its keys would go to whatever is there.
                if (!Trusted) throw new ScriptFault(UNTRUSTED_SCREEN);
                return ScriptTarget.OnScreen();
            }
            foreach (WatchedClient c in list)
                if (c != null && string.Equals(c.Label, want, StringComparison.OrdinalIgnoreCase)) return ScriptTarget.Of(c);
            foreach (WatchedClient c in list)
                if (c != null && string.Equals(c.AccountName, want, StringComparison.OrdinalIgnoreCase)) return ScriptTarget.Of(c);
            throw new ScriptFault("there's no Roblox window called \"" + want + "\" open - windows() lists them");
        }
    }

    // Code MoonSharp would go too deep reading, found without reading it
    // as Lua. Its reader calls itself for every bracket and block inside
    // another, and for every link of one long chain - a+a+a..., x.a.b.c...,
    // f()()(), - - - x - so code made of hundreds of thousands of them runs
    // the thread out of stack, which ends the whole app. No script anyone
    // writes comes near these limits.
    //
    // A chain is counted from the last place a new piece starts (a comma, a
    // semicolon, an =, a word like local or then, or a new statement), level
    // by level: what is inside brackets adds to the chain it is in.
    static class LuaShape
    {
        public const int MAX_NESTING = 200;
        public const int MAX_CHAIN = 5000;

        // Null when it is fine to read; otherwise "Line N: ..." to say why not.
        public static string Problem(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            int n = code.Length, i = 0, line = 1, depth = 0, total = 0;
            int[] counts = new int[MAX_NESTING + 2];
            bool value = false;         // the last thing ended a value: a name, number, text, ) ] }
            while (i < n)
            {
                char c = code[i];
                if (c == '\n') { line++; i++; continue; }
                if (char.IsWhiteSpace(c)) { i++; continue; }

                int level;
                if (c == '-' && i + 1 < n && code[i + 1] == '-')
                {
                    i += 2;
                    if (LongOpen(code, i, out level)) i = SkipLong(code, i, level, ref line);
                    else while (i < n && code[i] != '\n') i++;
                    continue;
                }

                // Text in quotes or [[...]]: a value, or - after a value - a
                // call, as in f"x".
                bool text = false;
                if (c == '[' && LongOpen(code, i, out level)) { i = SkipLong(code, i, level, ref line); text = true; }
                else if (c == '"' || c == '\'')
                {
                    i++;
                    while (i < n && code[i] != c && code[i] != '\n')
                    {
                        if (code[i] == '\\' && i + 1 < n) { if (code[i + 1] == '\n') line++; i++; }
                        i++;
                    }
                    if (i < n && code[i] == c) i++;
                    text = true;
                }
                if (text)
                {
                    if (value) { counts[depth]++; if (++total > MAX_CHAIN) return TooLong(line); }
                    value = true;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                    string w = code.Substring(start, i - start);
                    switch (w)
                    {
                        case "and": case "or": case "not":
                            counts[depth]++;
                            if (++total > MAX_CHAIN) return TooLong(line);
                            value = false;
                            break;
                        case "true": case "false": case "nil":
                            if (value) { total -= counts[depth]; counts[depth] = 0; }
                            value = true;
                            break;
                        case "function": case "do": case "if": case "repeat":
                            // A function can sit in the middle of a chain,
                            // so only the others start a new piece.
                            if (w != "function") { total -= counts[depth]; counts[depth] = 0; }
                            if (++depth > MAX_NESTING) return TooDeep(line);
                            counts[depth] = 0;
                            value = false;
                            break;
                        case "end":
                            if (depth > 0) { counts[depth - 1] += counts[depth]; depth--; }
                            value = true;
                            break;
                        case "until":
                            if (depth > 0) { counts[depth - 1] += counts[depth]; depth--; }
                            total -= counts[depth]; counts[depth] = 0;
                            value = false;
                            break;
                        case "local": case "return": case "then": case "else": case "elseif": case "while":
                        case "for": case "in": case "break": case "goto":
                            total -= counts[depth]; counts[depth] = 0;
                            value = false;
                            break;
                        default:
                            // Two values in a row: a new statement.
                            if (value) { total -= counts[depth]; counts[depth] = 0; }
                            value = true;
                            break;
                    }
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(code[i + 1])))
                {
                    i++;
                    while (i < n)
                    {
                        char d = code[i];
                        if (char.IsLetterOrDigit(d) || d == '.') { i++; continue; }
                        char e = code[i - 1];
                        if ((d == '+' || d == '-') && (e == 'e' || e == 'E' || e == 'p' || e == 'P')) { i++; continue; }
                        break;
                    }
                    if (value) { total -= counts[depth]; counts[depth] = 0; }
                    value = true;
                    continue;
                }

                i++;
                switch (c)
                {
                    case '(': case '[': case '{':
                        // After a value it calls or indexes it: one more link.
                        if (value) { counts[depth]++; if (++total > MAX_CHAIN) return TooLong(line); }
                        if (++depth > MAX_NESTING) return TooDeep(line);
                        counts[depth] = 0;
                        value = false;
                        break;
                    case ')': case ']': case '}':
                        if (depth > 0) { counts[depth - 1] += counts[depth]; depth--; }
                        value = true;
                        break;
                    case ',': case ';':
                        total -= counts[depth]; counts[depth] = 0;
                        value = false;
                        break;
                    case '=':
                        if (i < n && code[i] == '=') { i++; goto case '+'; }
                        total -= counts[depth]; counts[depth] = 0;
                        value = false;
                        break;
                    case ':':
                        if (i < n && code[i] == ':') { i++; total -= counts[depth]; counts[depth] = 0; value = false; break; }
                        goto case '+';
                    case '.':
                        if (i + 1 < n && code[i] == '.' && code[i + 1] == '.') { i += 2; value = true; break; }     // ...
                        goto case '+';
                    case '+':
                        counts[depth]++;
                        if (++total > MAX_CHAIN) return TooLong(line);
                        value = false;
                        break;
                    default:
                        // - * / % ^ # & | ~ < > and the like: a link too.
                        // The = of ~= <= >= is part of it, not a new piece.
                        if ((c == '~' || c == '<' || c == '>') && i < n && code[i] == '=') i++;
                        goto case '+';
                }
            }
            return null;
        }

        static string TooDeep(int line)
        {
            return "Line " + line + ": too many brackets or blocks inside each other here - " + MAX_NESTING + " at most";
        }

        static string TooLong(int line)
        {
            return "Line " + line + ": this is too long to read in one go - break it into smaller pieces";
        }

        // "[[" or "[==[" at i: how many = signs.
        static bool LongOpen(string s, int i, out int level)
        {
            level = 0;
            if (i >= s.Length || s[i] != '[') return false;
            int j = i + 1;
            while (j < s.Length && s[j] == '=') { level++; j++; }
            return j < s.Length && s[j] == '[';
        }

        // Just past the close of the long text or comment opening at i - or
        // the end, if it never closes - counting the lines in it.
        static int SkipLong(string s, int i, int level, ref int line)
        {
            string close = "]" + new string('=', level) + "]";
            int from = i + level + 2;
            int at = s.IndexOf(close, from, StringComparison.Ordinal);
            int end = at < 0 ? s.Length : at + close.Length;
            for (int k = from; k < end; k++) if (s[k] == '\n') line++;
            return end;
        }
    }
}
