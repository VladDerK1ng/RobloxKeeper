using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RobloxKeeper.Tests
{
    // When a watcher finds something, then run a macro on that client.
    static class RuleTests
    {
        public static void TestAWatchersMacroIsSaved()
        {
            Watcher w = Watcher.Default("Secret chat", WatchKind.ChatLine);
            w.ThenMacro = "Buy egg";
            w.MacroGapSeconds = 40;
            Watcher back = WatchStore.DeserializeWatcher(WatchStore.SerializeWatcher(w));
            Assert.Equal("Buy egg", back.ThenMacro, "which macro");
            Assert.Equal(40, back.MacroGapSeconds, "and how often at most");

            Watcher plain = WatchStore.DeserializeWatcher(WatchStore.SerializeWatcher(Watcher.Default("x", WatchKind.TextAppears)));
            Assert.Equal(null, plain.ThenMacro, "none unless chosen");
            Assert.Equal(15, plain.MacroGapSeconds, "fifteen seconds unless changed");
        }

        // A chat watcher fires once per line, and a busy chat must not play
        // the same macro back to back.
        public static void TestAMacroRunsAtMostOnceInItsGapPerClient()
        {
            RuleGate g = new RuleGate();
            Watcher w = Watcher.Default("Secret chat", WatchKind.ChatLine);
            w.ThenMacro = "Buy egg";
            DateTime t = new DateTime(2026, 9, 22, 18, 0, 0);

            Assert.True(g.Allow(w, 100, t), "the first time");
            Assert.False(g.Allow(w, 100, t.AddSeconds(5)), "not again five seconds later");
            Assert.True(g.Allow(w, 200, t.AddSeconds(5)), "another client has its own turn");
            Assert.True(g.Allow(w, 100, t.AddSeconds(16)), "after the gap, again");
        }

        // Watchers name their macro, so renaming it carries them along -
        // in every setup, not just the one on screen.
        public static void TestRenamingAMacroCarriesItsWatchersAlong()
        {
            WatchStore s = new WatchStore(Path.Combine(Path.GetTempPath(), "rk-unsaved.dat"));
            Watcher a = Watcher.Default("a", WatchKind.ChatLine); a.ThenMacro = "Buy";
            s.Watchers.Add(a);
            s.AddSetup("Other game", false);
            Watcher b = Watcher.Default("b", WatchKind.ChatLine); b.ThenMacro = "buy";
            Watcher c = Watcher.Default("c", WatchKind.ChatLine); c.ThenMacro = "Sell";
            s.Watchers.Add(b);
            s.Watchers.Add(c);

            Assert.Equal(2, s.RenameMacroUses("Buy", "Buy egg"), "two watchers used it");
            Assert.Equal("Buy egg", s.Watchers[0].ThenMacro, "this setup's");
            Assert.Equal("Sell", s.Watchers[1].ThenMacro, "the other macro left alone");
            Assert.False(ReferenceEquals(b, s.Watchers[0]), "replaced by a copy - the watch thread may be reading it");
            s.Choose(WatchStore.FirstSetupName);
            Assert.Equal("Buy egg", s.Watchers[0].ThenMacro, "and the other setup's");

            Assert.Equal(2, s.RenameMacroUses("Buy egg", null), "a removed macro, from both watchers");
            Assert.Equal(null, s.Watchers[0].ThenMacro, "is no longer run");
        }

        // The macro plays on the client that saw it, so the detection has to
        // say which process that was.
        public static void TestADetectionSaysWhichClientProcessSawIt()
        {
            WatchEngine e = new WatchEngine(new OneScreen(), new Says("a SECRET egg"));
            DetectionEvent got = null;
            e.Found = delegate(DetectionEvent d) { got = d; };
            WatchedClient c = new WatchedClient();
            c.Pid = 4242; c.Label = "Client 1";
            Watcher w = Watcher.Default("Secret", WatchKind.TextAppears);
            w.Rule.Words = new string[] { "secret" };
            w.ConfirmScans = 1;
            e.Pass(new WatchedClient[] { c }, new Watcher[] { w }, new WatchRegion[0], DateTime.Now);
            Assert.True(got != null, "found");
            Assert.Equal(4242, got.Pid, "on that process");
        }

        public static void TestTheWatchersListSaysWhatHappensNext()
        {
            Watcher w = Watcher.Default("Secret Egg", WatchKind.TextAppears);
            Assert.False(WatchList.Describe(w).Contains("then"), "nothing next");
            w.ThenMacro = "Buy egg";
            Assert.Contains("then plays Buy egg", WatchList.Describe(w), "says the macro");
        }

        class OneScreen : ICapture
        {
            public Pixels Grab(int pid, out WatchState state)
            {
                state = WatchState.Watchable;
                Pixels p = new Pixels(200, 100);
                p.Fill(unchecked((int)0xFF202020));
                return p;
            }
        }

        class Says : IReadText
        {
            readonly string text;
            public Says(string text) { this.text = text; }
            public string Read(Pixels image) { return text; }
        }
    }
}
