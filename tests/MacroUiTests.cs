using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper.Tests
{
    // The macro windows: the list, the editor, and the rules behind what can
    // be typed into them.
    static class MacroUiTests
    {
        class NoCapture : ICapture
        {
            public Pixels Grab(int pid, out WatchState state) { state = WatchState.NotInAGame; return null; }
        }

        class NoReader : IReadText
        {
            public string Read(Pixels image) { return ""; }
        }

        static WatchKit Kit(string path)
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(path);
            k.Capture = new NoCapture();
            k.Reader = new NoReader();
            k.Engine = new WatchEngine(k.Capture, k.Reader);
            k.Clients = delegate { return new WatchedClient[0]; };
            k.Accounts = delegate { return (IList<string>)new List<string>(); };
            k.Log = delegate(string s) { };
            return k;
        }

        static string TempStore()
        {
            return Path.Combine(Path.GetTempPath(), "rk-macros-" + Guid.NewGuid().ToString("N") + ".dat");
        }

        static Macro Named(string name)
        {
            Macro m = new Macro();
            m.Name = name;
            m.Steps.Add(MacroStep.Key(0x45, 50));
            return m;
        }

        // ---------- what can be typed ----------

        public static void TestSecondsCanBeTypedTheWaysPeopleTypeThem()
        {
            int ms;
            Assert.Equal(null, MacroForm.ParseSeconds("1.5", out ms), "a dot");
            Assert.Equal(1500, ms, "one and a half seconds");
            Assert.Equal(null, MacroForm.ParseSeconds("1,5", out ms), "a comma");
            Assert.Equal(1500, ms, "the same");
            Assert.Equal(null, MacroForm.ParseSeconds(" 2s ", out ms), "with an s");
            Assert.Equal(2000, ms, "two seconds");
        }

        public static void TestSecondsThatMakeNoSenseSaySo()
        {
            int ms;
            Assert.Contains("number", MacroForm.ParseSeconds("soon", out ms), "not a number");
            Assert.Contains("minute", MacroForm.ParseSeconds("90", out ms), "longer than a macro may be");
            Assert.Contains("more than", MacroForm.ParseSeconds("0", out ms), "nothing at all");
        }

        public static void TestAMacroIsSummedUpInAFewWords()
        {
            Macro m = Named("x");
            Assert.Equal("1 step · about 0.05s", MacroForm.Summary(m), "one");
            m.Steps.Add(MacroStep.Wait(1500));
            m.Steps.Add(MacroStep.Key(0x57, 2000));
            Assert.Equal("3 steps · about 3.55s", MacroForm.Summary(m), "three");
        }

        // A watcher names its macro, so two with one name could not be told
        // apart.
        public static void TestTwoMacrosCannotShareAName()
        {
            WatchStore s = new WatchStore(TempStore());
            Macro buy = Named("Buy egg");
            s.Macros.Add(buy);
            Assert.Contains("already", MacroForm.NameProblem(s, "buy EGG", null), "taken");
            Assert.Equal(null, MacroForm.NameProblem(s, "Buy egg", buy), "its own name, when editing it");
            Assert.Equal(null, MacroForm.NameProblem(s, "Sell egg", null), "a new one");
        }

        // ---------- the list ----------

        public static void TestTheListAddsAndRemovesMacrosAndSaves()
        {
            string path = TempStore();
            try
            {
                WatchKit k = Kit(path);
                using (MacrosDialog d = new MacrosDialog(k))
                {
                    d.AddMacro(Named("Buy egg"));
                    d.AddMacro(Named("Sell egg"));
                    Assert.Equal(2, d.RowCount, "a row each");
                    d.RemoveAt(0);
                    Assert.Equal(1, d.RowCount, "one left");
                }
                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.Equal(1, back.Macros.Count, "saved");
                Assert.Equal("Sell egg", back.Macros[0].Name, "the right one kept");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // ---------- the editor ----------

        public static void TestTheEditorBuildsUpStepsInOrder()
        {
            WatchKit k = Kit(TempStore());
            Macro m = new Macro();
            using (MacroEditDialog d = new MacroEditDialog(m, k))
            {
                d.AddStep(MacroStep.Key(0x45, 50));
                d.AddStep(MacroStep.Wait(1000));
                d.AddStep(MacroStep.Click(0.5, 0.5, false));
                Assert.Equal(3, d.StepCount, "three steps");

                d.MoveStep(2, -1);
                Assert.Equal("Press E | Click 50% across, 50% down | Wait 1s", d.StepsSaid, "the click moved up one");
                d.MoveStep(0, -1);
                Assert.Equal("Press E | Click 50% across, 50% down | Wait 1s", d.StepsSaid, "the first can't move up");
                d.RemoveStep(1);
                Assert.Equal("Press E | Wait 1s", d.StepsSaid, "the click gone");
            }
            Assert.Equal(0, m.Steps.Count, "and the macro being edited was never touched");
        }

        public static void TestTheEditorWontSaveAMacroWithAProblem()
        {
            WatchKit k = Kit(TempStore());
            using (MacroEditDialog d = new MacroEditDialog(new Macro(), k))
            {
                d.AddStep(MacroStep.Key(0x45, 50));
                Assert.False(d.TrySave(""), "no name");
                Assert.Contains("name", d.ProblemText, "and it says why");
                Assert.True(d.TrySave("Buy egg"), "named");
                Assert.Equal("Buy egg", d.Result.Name, "the result has the name");
                Assert.Equal(1, d.Result.Steps.Count, "and the step");
            }
        }
    }
}
