using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Watching, as the rest of the app sees it: the line on the main window,
    // which client is which, the watchers list, and turning on text reading.
    static class WatchingTests
    {
        // ---------- the main window's line ----------

        public static void TestTheStatusLineCountsWatchersClientsAndHits()
        {
            Assert.Equal("3 watchers · 2 clients · 4 hits today", Watching.StatusLine(3, 3, 2, 4), "the spec's line");
        }

        public static void TestTheStatusLineUsesTheSingular()
        {
            Assert.Equal("1 watcher · 1 client · 1 hit today", Watching.StatusLine(1, 1, 1, 1), "one of each");
        }

        public static void TestWithNoWatchersItSaysWhatWatchingIsFor()
        {
            Assert.Contains("turns up", Watching.StatusLine(0, 0, 0, 0), "an invitation, not 0 watchers");
        }

        public static void TestSwitchedOffWatchersAreSaidToBeOff()
        {
            Assert.Equal("2 watchers, all switched off", Watching.StatusLine(0, 2, 0, 0), "not silently idle");
            Assert.Equal("1 watcher, switched off", Watching.StatusLine(0, 1, 0, 0), "and the singular");
        }

        public static void TestHitsAreCountedForTodayOnly()
        {
            HitCounter h = new HitCounter();
            DateTime late = new DateTime(2026, 9, 21, 23, 59, 0);
            h.Add(late);
            h.Add(late.AddSeconds(20));
            Assert.Equal(2, h.Today(late.AddSeconds(30)), "two today");
            Assert.Equal(0, h.Today(late.AddMinutes(2)), "a new day starts at nought");
        }

        // ---------- each client ----------

        public static void TestEachClientSaysWhetherItCanBeWatched()
        {
            Assert.Contains("being watched", Watching.StateText(true, true, WatchState.Watchable), "fine");
            Assert.Contains("minimized", Watching.StateText(true, true, WatchState.Minimized), "said plainly");
            Assert.Contains("not in a game", Watching.StateText(true, true, WatchState.NotInAGame), "tray or loading");
            Assert.Contains("no watcher", Watching.StateText(false, false, WatchState.Watchable), "nothing assigned");
        }

        // A log file is matched to its client by when it opened, the same way
        // the activity list already names a client in a log line.
        public static void TestAJoinInALogIsMatchedToTheClientThatWroteIt()
        {
            List<ClientInfo> clients = new List<ClientInfo>();
            clients.Add(Info(10, new DateTime(2026, 9, 21, 10, 40, 0, DateTimeKind.Utc)));
            clients.Add(Info(20, new DateTime(2026, 9, 21, 11, 49, 30, DateTimeKind.Utc)));

            Assert.Equal(20, Watching.PidForLog("0.643.0_20260921T114933Z_Player_2A3F1_last.log", clients),
                "opened three seconds after client 20 started");
            Assert.Equal(0, Watching.PidForLog("0.643.0_20260921T090000Z_Player_2A3F1_last.log", clients),
                "a log from before either client");
        }

        static ClientInfo Info(int pid, DateTime startedUtc)
        {
            ClientInfo c = new ClientInfo();
            c.Pid = pid;
            c.Start = startedUtc.ToLocalTime();
            return c;
        }

        // "Client 2" is the second row in the list, as on the main window; the
        // account is kept alongside it, and so is the server it is in.
        public static void TestClientsAreNamedByPositionAndAccountAndKnowTheirServer()
        {
            List<ClientInfo> clients = new List<ClientInfo>();
            clients.Add(Info(10, DateTime.UtcNow));
            clients.Add(Info(20, DateTime.UtcNow));
            Dictionary<int, RobloxLogEvent> where = new Dictionary<int, RobloxLogEvent>();
            RobloxLogEvent e = new RobloxLogEvent();
            e.PlaceId = "142823291";
            e.JobId = "55cf1f30-d19e-4a37-84aa-609ff3c1d3a0";
            where[20] = e;

            WatchedClient[] w = Watching.ClientsFor(clients,
                delegate(int pid) { return pid == 20 ? "VladDerKing" : null; }, where);

            Assert.Equal("Client 1", w[0].Label, "first row");
            Assert.Equal(null, w[0].AccountName, "launched outside the account manager");
            Assert.Equal(null, w[0].PlaceId, "no join seen yet");
            Assert.Equal("Client 2", w[1].Label, "second row");
            Assert.Equal("VladDerKing", w[1].AccountName, "its account");
            Assert.Equal("142823291", w[1].PlaceId, "its game");
            Assert.Equal("55cf1f30-d19e-4a37-84aa-609ff3c1d3a0", w[1].JobId, "and its server");
        }

        public static void TestOnlyClientsAWatcherRunsOnCountAsWatched()
        {
            WatchedClient a = new WatchedClient(); a.Pid = 1; a.AccountName = "VladDerKing";
            WatchedClient b = new WatchedClient(); b.Pid = 2; b.AccountName = "alt1";
            Watcher mine = Watcher.Default("x", WatchKind.TextAppears);
            mine.Accounts = new string[] { "VladDerKing" };
            Watcher off = Watcher.Default("y", WatchKind.TextAppears);
            off.Enabled = false;

            Assert.Equal(1, Watching.ClientsWatched(new WatchedClient[] { a, b }, new Watcher[] { mine, off }),
                "the switched-off watcher covers nobody");
        }

        // It needs administrator rights, so Windows asks rather than the app
        // assuming it may.
        public static void TestInstallingTextReadingAsksForAdministratorRights()
        {
            ProcessStartInfo p = Watching.InstallOcr();
            Assert.Equal("DISM.exe", p.FileName, "Windows' own installer");
            Assert.Contains("Language.OCR~~~en-US", p.Arguments, "the recogniser");
            Assert.Equal("runas", p.Verb, "asks for administrator rights");
            Assert.True(p.UseShellExecute, "which only works through the shell");
        }

        // ---------- the watchers list ----------

        public static void TestARowSaysWhatWhereAndWho()
        {
            Watcher w = Watcher.Default("Secret Egg", WatchKind.TextAppears);
            w.RegionName = "banner";
            Assert.Equal("A word appears · the box called \"banner\" · every client", WatchList.Describe(w), "all three");
            w.Accounts = new string[] { "VladDerKing", "alt1" };
            w.RegionName = null;
            Assert.Equal("A word appears · the whole window · VladDerKing, alt1", WatchList.Describe(w), "named accounts");
        }

        public static void TestTextWatchersSayWhyTheyCannotRunWithoutTheRecogniser()
        {
            Watcher text = Watcher.Default("t", WatchKind.TextAppears);
            Watcher chat = Watcher.Default("c", WatchKind.ChatLine);
            Watcher pic = Watcher.Default("p", WatchKind.ImageFound);
            Assert.Contains("read text", WatchList.RowProblem(text, false, null), "words need it");
            Assert.Contains("read text", WatchList.RowProblem(chat, false, null), "chat needs it");
            Assert.Equal(null, WatchList.RowProblem(pic, false, null), "pictures do not");
            Assert.Equal("no box", WatchList.RowProblem(text, true, "no box"), "the engine's own reason otherwise");
        }

        public static void TestOnlyADiscordWebhookCanBeSaved()
        {
            Assert.Equal(null, WatchList.WebhookProblem("https://discord.com/api/webhooks/1/abc"), "a real one");
            Assert.Contains("Discord", WatchList.WebhookProblem("https://example.com/hook"), "not Discord");
            Assert.Equal(null, WatchList.WebhookProblem(""), "blank is not an error, just nothing to save");
        }

        // ---------- the watchers window ----------

        class NoCapture : ICapture
        {
            public Pixels Grab(int pid, out WatchState state) { state = WatchState.NotInAGame; return null; }
        }

        class NoReader : IReadText
        {
            public string Read(Pixels image) { return ""; }
        }

        static WatchKit Kit(string path, params Watcher[] watchers)
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(path);
            foreach (Watcher w in watchers) k.Store.Watchers.Add(w);
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
            return Path.Combine(Path.GetTempPath(), "rk-watchers-" + Guid.NewGuid().ToString("N") + ".dat");
        }

        public static void TestTheListShowsEveryWatcher()
        {
            string path = TempStore();
            WatchKit k = Kit(path, Watcher.Default("one", WatchKind.TextAppears), Watcher.Default("two", WatchKind.ChatLine));
            using (WatchersDialog d = new WatchersDialog(k, delegate { }))
                Assert.Equal(2, d.RowCount, "a row each");
        }

        // The watch thread may be reading that watcher at this moment, so it is
        // replaced by a switched-off copy, never changed underneath it.
        public static void TestSwitchingAWatcherOffReplacesItAndSaves()
        {
            string path = TempStore();
            try
            {
                Watcher w = Watcher.Default("one", WatchKind.TextAppears);
                int told = 0;
                WatchKit k = Kit(path, w);
                using (WatchersDialog d = new WatchersDialog(k, delegate { told++; }))
                {
                    d.SetEnabled(0, false);
                    Assert.False(k.Store.Watchers[0].Enabled, "off in the list");
                    Assert.False(ReferenceEquals(w, k.Store.Watchers[0]), "as a new copy");
                    Assert.True(w.Enabled, "the one being read was left alone");
                    Assert.True(told > 0, "and the app was told to pick it up");
                }

                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.False(back.Watchers[0].Enabled, "saved");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        public static void TestRemovingAWatcherTakesItOutAndSaves()
        {
            string path = TempStore();
            try
            {
                WatchKit k = Kit(path, Watcher.Default("one", WatchKind.TextAppears), Watcher.Default("two", WatchKind.ChatLine));
                using (WatchersDialog d = new WatchersDialog(k, delegate { }))
                {
                    d.RemoveAt(0);
                    Assert.Equal(1, d.RowCount, "one row left");
                }
                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.Equal(1, back.Watchers.Count, "saved");
                Assert.Equal("two", back.Watchers[0].Name, "and the right one is left");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        public static void TestAWebhookThatIsNotDiscordsIsNotSaved()
        {
            string path = TempStore();
            try
            {
                WatchKit k = Kit(path);
                k.Store.WebhookUrl = "https://discord.com/api/webhooks/1/old";
                using (WatchersDialog d = new WatchersDialog(k, delegate { }))
                    Assert.False(d.SaveWebhook("https://example.com/hook"), "refused");
                Assert.Equal("https://discord.com/api/webhooks/1/old", k.Store.WebhookUrl, "the old one kept");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // Saved, and the box it was typed into is emptied - it is never shown
        // again once saved.
        public static void TestAGoodWebhookIsSavedAndNotShownAgain()
        {
            string path = TempStore();
            try
            {
                WatchKit k = Kit(path);
                using (WatchersDialog d = new WatchersDialog(k, delegate { }))
                {
                    Assert.True(d.SaveWebhook("https://discord.com/api/webhooks/1/new"), "accepted");
                    Assert.Equal("", d.WebhookBoxText, "the box is emptied");
                }
                Assert.Equal("https://discord.com/api/webhooks/1/new", k.Store.WebhookUrl, "saved");
            }
            finally { try { File.Delete(path); } catch { } }
        }
    }
}
