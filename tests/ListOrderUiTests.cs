using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Macros, their steps, rules and watchers in the order you want, and
    // copies of them - "the same should be for macros etc", the owner asked,
    // after asking for it on accounts.
    static class ListOrderUiTests
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
            return Path.Combine(Path.GetTempPath(), "rk-order-" + Guid.NewGuid().ToString("N") + ".dat");
        }

        static Macro M(string name)
        {
            Macro m = new Macro();
            m.Name = name;
            m.Steps.Add(MacroStep.Key(0x45, 50));
            return m;
        }

        static Rule R(string name)
        {
            Rule r = new Rule();
            r.Name = name;
            r.Macro = "Buy";
            return r;
        }

        static string MacroNames(WatchStore s)
        {
            List<string> n = new List<string>();
            foreach (Macro m in s.Macros) n.Add(m.Name);
            return string.Join(",", n.ToArray());
        }

        // ---------- macros ----------

        public static void TestMacrosCanBePutInOrder()
        {
            string path = TempStore();
            try
            {
                WatchKit k = Kit(path);
                using (MacrosDialog d = new MacrosDialog(k))
                {
                    d.AddMacro(M("Buy"));
                    d.AddMacro(M("Sell"));
                    d.AddMacro(M("Rejoin"));
                    d.MoveAt(2, 0);
                }
                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.Equal("Rejoin,Buy,Sell", MacroNames(back), "saved in that order");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // A copy sits under what it was copied from, with a name of its own,
        // and changing it leaves the original alone.
        public static void TestAMacroCanBeCopied()
        {
            string path = TempStore();
            try
            {
                WatchKit k = Kit(path);
                using (MacrosDialog d = new MacrosDialog(k))
                {
                    d.AddMacro(M("Buy"));
                    d.AddMacro(M("Sell"));
                    d.CopyAt(0);
                    Assert.Equal("Buy,Buy copy,Sell", MacroNames(k.Store), "under the original");
                    k.Store.Macros[1].Steps.Clear();
                    Assert.Equal(1, k.Store.Macros[0].Steps.Count, "its own steps, not the original's");
                    d.CopyAt(0);
                    Assert.Equal("Buy,Buy copy 2,Buy copy,Sell", MacroNames(k.Store), "a second copy, named apart");
                }
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // ---------- steps ----------

        public static void TestStepsCanBeMovedAnywhereAndCopied()
        {
            Macro m = new Macro();
            m.Name = "Buy";
            m.Steps.Add(MacroStep.Key(0x45, 50));
            m.Steps.Add(MacroStep.Wait(1000));
            m.Steps.Add(MacroStep.Click(0.5, 0.5, false));
            using (MacroEditDialog d = new MacroEditDialog(m, Kit(TempStore())))
            {
                d.MoveStepTo(2, 0);
                Assert.Equal("Click 50% across, 50% down | Press E | Wait 1s", d.StepsSaid, "the click first");
                d.CopyStep(1);
                Assert.Equal("Click 50% across, 50% down | Press E | Press E | Wait 1s", d.StepsSaid, "a copy under it");
            }
        }

        // ---------- rules ----------

        public static void TestRulesCanBePutInOrderAndCopied()
        {
            string path = TempStore();
            try
            {
                WatchKit k = Kit(path);
                k.Store.Macros.Add(M("Buy"));
                using (RulesDialog d = new RulesDialog(k))
                {
                    d.AddRule(R("Every minute"));
                    d.AddRule(R("On F2"));
                    d.MoveAt(1, 0);
                    d.CopyAt(0);
                }
                WatchStore back = new WatchStore(path);
                back.Load();
                List<string> names = new List<string>();
                foreach (Rule r in back.Rules) names.Add(r.Name);
                Assert.Equal("On F2,On F2 copy,Every minute", string.Join(",", names.ToArray()), "saved so");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // ---------- watchers ----------

        public static void TestWatchersCanBePutInOrderAndCopied()
        {
            string path = TempStore();
            try
            {
                WatchKit k = Kit(path);
                k.Store.Watchers.Add(Watcher.Default("Secret egg", WatchKind.ChatLine));
                k.Store.Watchers.Add(Watcher.Default("Rare fruit", WatchKind.TextAppears));
                int told = 0;
                using (WatchersDialog d = new WatchersDialog(k, delegate { told++; }))
                {
                    d.MoveAt(1, 0);
                    Assert.True(told > 0, "the watch thread is handed the new list");
                    d.CopyAt(0);
                    Assert.True(!ReferenceEquals(k.Store.Watchers[0], k.Store.Watchers[1]), "a copy, not the same watcher twice");
                }
                WatchStore back = new WatchStore(path);
                back.Load();
                List<string> names = new List<string>();
                foreach (Watcher w in back.Watchers) names.Add(w.Name);
                Assert.Equal("Rare fruit,Rare fruit copy,Secret egg", string.Join(",", names.ToArray()), "saved so");
            }
            finally { try { File.Delete(path); } catch { } }
        }
    }
}
