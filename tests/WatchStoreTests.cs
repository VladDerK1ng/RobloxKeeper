using System;
using System.Drawing;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Saving and reloading watchers.
    //
    // The round trip is the whole test: anything that does not survive it is
    // a setting the user has to enter twice, and they will not find out which
    // one until it bites.
    static class WatchStoreTests
    {
        static string TempPath()
        {
            return Path.Combine(Path.GetTempPath(),
                "rk_watchtest_" + Guid.NewGuid().ToString("n") + ".dat");
        }

        static Watcher Sample()
        {
            Watcher w = Watcher.Default("Secret Egg", WatchKind.TextAppears);
            w.RegionName = "banner";
            w.Accounts = new string[] { "VladDerKing", "alt_01" };
            w.Rule.Words = new string[] { "secret", "rainbow" };
            w.Rule.Mode = MatchMode.All;
            w.Rule.IgnoreCase = false;
            w.Rule.WholeWordsOnly = true;
            w.ConfirmScans = 3;
            w.CooldownSeconds = 45;
            w.Tolerance = 0.91;
            w.PlaySound = true;
            w.SendDiscord = false;
            return w;
        }

        public static void TestAWatcherSurvivesTheRoundTrip()
        {
            Watcher back = WatchStore.DeserializeWatcher(WatchStore.SerializeWatcher(Sample()));
            Assert.Equal("Secret Egg", back.Name, "name");
            Assert.Equal(WatchKind.TextAppears, back.Kind, "kind");
            Assert.Equal("banner", back.RegionName, "region");
            Assert.Equal(3, back.ConfirmScans, "confirm");
            Assert.Equal(45, back.CooldownSeconds, "cooldown");
            Assert.Equal(0.91, back.Tolerance, "tolerance");
            Assert.True(back.PlaySound, "sound on");
            Assert.False(back.SendDiscord, "discord off");
        }

        public static void TestTheAccountListSurvives()
        {
            Watcher back = WatchStore.DeserializeWatcher(WatchStore.SerializeWatcher(Sample()));
            Assert.Equal(2, back.Accounts.Length, "two accounts");
            Assert.True(back.RunsOn("alt_01"), "and it still runs on them");
            Assert.False(back.RunsOn("someone_else"), "and not on others");
        }

        public static void TestTheMatchRuleSurvives()
        {
            Watcher back = WatchStore.DeserializeWatcher(WatchStore.SerializeWatcher(Sample()));
            Assert.Equal(MatchMode.All, back.Rule.Mode, "mode");
            Assert.False(back.Rule.IgnoreCase, "case sensitivity");
            Assert.True(back.Rule.WholeWordsOnly, "whole words");
            Assert.Equal(2, back.Rule.Words.Length, "both words");
        }

        // A watcher with nothing set must not come back with an empty account
        // list that looks like a named list of nobody.
        public static void TestAWatcherWithNoAccountsStillRunsEverywhere()
        {
            Watcher w = Watcher.Default("Plain", WatchKind.ChatLine);
            Watcher back = WatchStore.DeserializeWatcher(WatchStore.SerializeWatcher(w));
            Assert.True(back.RunsOn("anyone"), "still every client");
            Assert.True(back.WholeWindow, "and still the whole window");
        }

        // A tab in a word would otherwise split one field into two and shift
        // every setting after it along by one.
        public static void TestTabsAndNewlinesInAWordDoNotCorruptTheRecord()
        {
            Watcher w = Sample();
            w.Rule.Words = new string[] { "one\ttwo", "three\nfour" };
            Watcher back = WatchStore.DeserializeWatcher(WatchStore.SerializeWatcher(w));
            Assert.Equal("one\ttwo", back.Rule.Words[0], "tab survived inside the field");
            Assert.Equal("three\nfour", back.Rule.Words[1], "newline too");
            Assert.Equal(45, back.CooldownSeconds, "and nothing after it shifted");
        }

        public static void TestARegionSurvivesTheRoundTrip()
        {
            WatchRegion r = WatchRegion.FromBox(new Rectangle(484, 0, 968, 230),
                1936, 1048, RegionAnchor.TopCentre, RegionScaling.Uniform);
            r.Name = "banner";
            r.PlaceId = "142823291";

            WatchRegion back = WatchStore.DeserializeRegion(WatchStore.SerializeRegion(r));
            Assert.Equal("banner", back.Name, "name");
            Assert.Equal("142823291", back.PlaceId, "place");
            Assert.Equal(RegionAnchor.TopCentre, back.Anchor, "anchor");
            Assert.Equal(RegionScaling.Uniform, back.Scaling, "scaling");
            Assert.Equal(1936, back.DrawnWidth, "drawn width");

            // The point of storing it at all: it still projects to the same box.
            Assert.Equal(r.Project(1936, 1048), back.Project(1936, 1048), "same box");
            Assert.Equal(r.Project(800, 800), back.Project(800, 800), "and the same after a resize");
        }

        public static void TestSavingAndLoadingAFileKeepsEverything()
        {
            string path = TempPath();
            try
            {
                WatchStore s = new WatchStore(path);
                s.Watchers.Add(Sample());
                WatchRegion r = WatchRegion.FromBox(new Rectangle(0, 0, 100, 50),
                    1000, 600, RegionAnchor.TopLeft, RegionScaling.Stretch);
                r.Name = "banner"; r.PlaceId = "142823291";
                s.Regions.Add(r);
                s.WebhookUrl = "https://discord.com/api/webhooks/1/secret";
                s.Save();

                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.Equal(1, back.Watchers.Count, "the watcher");
                Assert.Equal(1, back.Regions.Count, "the region");
                Assert.Equal("https://discord.com/api/webhooks/1/secret", back.WebhookUrl, "the url");
                Assert.Equal("Secret Egg", back.Watchers[0].Name, "and it is the right watcher");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // The webhook token lets anyone post into the channel, so the file
        // must not be readable by anything that simply opens it.
        public static void TestTheSavedFileDoesNotContainTheUrlInPlainText()
        {
            string path = TempPath();
            try
            {
                WatchStore s = new WatchStore(path);
                s.WebhookUrl = "https://discord.com/api/webhooks/1/verysecrettoken";
                s.Save();

                byte[] raw = File.ReadAllBytes(path);
                byte[] token = System.Text.Encoding.UTF8.GetBytes("verysecrettoken");
                Assert.False(Occurs(raw, token), "the token is not sitting there in the file");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        static bool Occurs(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                bool all = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { all = false; break; }
                if (all) return true;
            }
            return false;
        }

        public static void TestLoadingAMissingFileIsNotAnError()
        {
            WatchStore s = new WatchStore(TempPath());
            s.Load();
            Assert.Equal(0, s.Watchers.Count, "nothing saved yet, and that is fine");
        }

        // A file from a newer or older build must not take the app down.
        public static void TestNonsenseInTheFileIsSkippedNotThrown()
        {
            Assert.Equal(null, WatchStore.DeserializeWatcher(""), "empty line");
            Assert.Equal(null, WatchStore.DeserializeWatcher("garbage"), "one field");
            Assert.Equal(null, WatchStore.DeserializeRegion("also garbage"), "same for a region");
        }

        public static void TestRegionsAreFoundByPlaceAndName()
        {
            WatchStore s = new WatchStore(TempPath());
            s.Regions.Add(Region("banner", "111"));
            s.Regions.Add(Region("banner", "222"));

            Assert.Equal("222", s.FindRegion("222", "banner").PlaceId, "the right game's banner");
            Assert.Equal(null, s.FindRegion("333", "banner"), "a game with no regions yet");
            Assert.Equal(1, s.RegionsFor("111").Count, "one region for that game");
        }

        static WatchRegion Region(string name, string placeId)
        {
            WatchRegion r = WatchRegion.FromBox(new Rectangle(0, 0, 10, 10),
                100, 100, RegionAnchor.TopLeft, RegionScaling.Stretch);
            r.Name = name;
            r.PlaceId = placeId;
            return r;
        }

        // On a machine whose locale writes 0,82 a stored tolerance would come
        // back as 82 and every image watcher would quietly stop matching.
        public static void TestADecimalSurvivesOnAnyLocale()
        {
            System.Globalization.CultureInfo was =
                System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");
                Watcher back = WatchStore.DeserializeWatcher(WatchStore.SerializeWatcher(Sample()));
                Assert.Equal(0.91, back.Tolerance, "still a fraction, not ninety-one");
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = was; }
        }
    }
}
