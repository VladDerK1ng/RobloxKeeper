using System;

namespace RobloxKeeper.Tests
{
    // A watcher's own rules about where it runs, and what a detection says
    // when it fires.
    static class WatcherTests
    {
        public static void TestAWatcherWithNoAccountsRunsOnEveryClient()
        {
            Watcher w = Watcher.Default("Secret Egg", WatchKind.TextAppears);
            Assert.True(w.RunsOn("VladDerKing"), "no list means everything");
            Assert.True(w.RunsOn(null), "including a client we cannot name");
        }

        public static void TestAWatcherWithAccountsRunsOnlyOnThose()
        {
            Watcher w = Watcher.Default("Secret Egg", WatchKind.TextAppears);
            w.Accounts = new string[] { "VladDerKing", "alt_01" };
            Assert.True(w.RunsOn("VladDerKing"), "named");
            Assert.True(w.RunsOn("ALT_01"), "named, and case does not matter");
            Assert.False(w.RunsOn("someone_else"), "not named");
        }

        // A client launched from outside the account manager has no name, so
        // it can only ever be covered by "every client". Quietly including it
        // in a named list would put a watcher on the wrong account.
        public static void TestANamelessClientIsNotCoveredByANamedList()
        {
            Watcher w = Watcher.Default("Secret Egg", WatchKind.TextAppears);
            w.Accounts = new string[] { "VladDerKing" };
            Assert.False(w.RunsOn(null), "we cannot claim this is VladDerKing");
            Assert.False(w.RunsOn(""), "same for an empty name");
        }

        public static void TestNoRegionMeansTheWholeWindow()
        {
            Watcher w = Watcher.Default("Anything", WatchKind.TextAppears);
            Assert.True(w.WholeWindow, "nothing chosen");
            w.RegionName = "banner";
            Assert.False(w.WholeWindow, "a region was chosen");
        }

        // The defaults are the ones argued for in the spec; if they drift the
        // feature quietly changes character.
        public static void TestTheDefaultsAreTheOnesWeChose()
        {
            Watcher w = Watcher.Default("x", WatchKind.ImageFound);
            Assert.Equal(2, w.ConfirmScans, "two scans to confirm");
            Assert.Equal(30, w.CooldownSeconds, "thirty seconds between firings");
            Assert.Equal(0.82, w.Tolerance, "the image tolerance");
            Assert.True(w.Enabled, "a new watcher is on");
            Assert.True(w.SendDiscord, "telling you is the point of it");
        }

        public static void TestItBuildsAFireControlFromItsOwnSettings()
        {
            Watcher w = Watcher.Default("x", WatchKind.TextAppears);
            w.ConfirmScans = 1;
            Assert.True(w.NewFireControl().Update(true, DateTime.Now),
                "confirm of one fires at once");
        }

        // Each client needs its own counter, or two clients seeing the same
        // thing would confirm each other's sightings.
        public static void TestEachCallGivesAFreshFireControl()
        {
            Watcher w = Watcher.Default("x", WatchKind.TextAppears);
            FireControl a = w.NewFireControl();
            a.Update(true, DateTime.Now);
            Assert.False(w.NewFireControl().Update(true, DateTime.Now),
                "a second client starts its own count");
        }

        // The message names both, because the number is how you find the
        // window and the name is how you know whose it is.
        public static void TestAHeadlineNamesTheWatcherTheClientAndTheAccount()
        {
            DetectionEvent d = new DetectionEvent();
            d.WatcherName = "Secret Egg";
            d.ClientLabel = "Client 3";
            d.AccountName = "VladDerKing";
            string h = d.Headline();
            Assert.Contains("Secret Egg", h, "what was found");
            Assert.Contains("Client 3", h, "which window");
            Assert.Contains("VladDerKing", h, "whose account");
        }

        public static void TestAHeadlineCopesWithAnUnnamedClient()
        {
            DetectionEvent d = new DetectionEvent();
            d.WatcherName = "Secret Egg";
            d.ClientLabel = "Client 2";
            string h = d.Headline();
            Assert.Contains("Client 2", h, "still says which window");
            Assert.False(h.Contains("()"), "no empty brackets where a name would go");
            Assert.False(h.TrimEnd().EndsWith("-"), "and no dangling separator");
        }

        public static void TestADetectionCarriesALinkBackToItsServer()
        {
            DetectionEvent d = new DetectionEvent();
            d.PlaceId = "142823291";
            d.JobId = "55cf1f30-d19e-4a37-84aa-609ff3c1d3a0";
            Assert.Contains("gameInstanceId=55cf1f30", d.JoinLink, "the exact server");
        }

        public static void TestADetectionWithNoServerHasNoLink()
        {
            DetectionEvent d = new DetectionEvent();
            d.PlaceId = "142823291";
            Assert.Equal(null, d.JoinLink, "no server, no link");
        }
    }
}
