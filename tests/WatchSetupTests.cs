using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RobloxKeeper.Tests
{
    // Setups: a named set of watchers for each game, so what is being watched
    // changes in one go rather than watcher by watcher.
    //
    // Someone who never makes a second setup must see no difference, and a
    // file saved before setups existed must load with nothing lost.
    static class WatchSetupTests
    {
        static string TempPath()
        {
            return Path.Combine(Path.GetTempPath(),
                "rk_setuptest_" + Guid.NewGuid().ToString("n") + ".dat");
        }

        static Watcher Named(string name)
        {
            return Watcher.Default(name, WatchKind.ChatLine);
        }

        public static void TestThereIsAlwaysASetup()
        {
            WatchStore s = new WatchStore(TempPath());
            Assert.Equal(1, s.Setups.Count, "one to start with");
            Assert.Equal(WatchStore.FirstSetupName, s.Chosen.Name, "and it is the one in use");
            s.Watchers.Add(Named("Secret"));
            Assert.Equal(1, s.Chosen.Watchers.Count, "the watchers are the chosen setup's");
        }

        public static void TestTheWatchersAreTheChosenSetups()
        {
            WatchStore s = new WatchStore(TempPath());
            s.Watchers.Add(Named("Secret"));
            s.AddSetup("Other game", false);
            Assert.Equal("Other game", s.Chosen.Name, "a new setup is the one being filled in");
            Assert.Equal(0, s.Watchers.Count, "and it starts empty");
            s.Watchers.Add(Named("Boss"));
            s.Watchers.Add(Named("Drop"));

            Assert.True(s.Choose(WatchStore.FirstSetupName), "switched back");
            Assert.Equal(1, s.Watchers.Count, "the first setup's one watcher");
            Assert.Equal("Secret", s.Watchers[0].Name, "and it is that one");

            Assert.False(s.Choose("No such setup"), "a name that is not there");
            Assert.Equal(WatchStore.FirstSetupName, s.Chosen.Name, "changes nothing");
        }

        public static void TestSetupsAndTheChosenOneSurviveSavingAndLoading()
        {
            string path = TempPath();
            try
            {
                WatchStore s = new WatchStore(path);
                s.Watchers.Add(Named("Secret"));
                Assert.True(s.RenameSetup(s.Chosen, "Steal an Egg"), "renamed");
                s.AddSetup("Nothing yet", false);
                s.AddSetup("Other game", false);
                s.Watchers.Add(Named("Boss"));
                s.Watchers.Add(Named("Drop"));
                s.Save();

                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.Equal(3, back.Setups.Count, "all three, the empty one too");
                Assert.Equal("Steal an Egg", back.Setups[0].Name, "in the order they were made");
                Assert.Equal(1, back.Setups[0].Watchers.Count, "its one watcher");
                Assert.Equal(0, back.Setups[1].Watchers.Count, "still empty");
                Assert.Equal(2, back.Setups[2].Watchers.Count, "its two");
                Assert.Equal("Drop", back.Setups[2].Watchers[1].Name, "each in its own setup");
                Assert.Equal("Other game", back.Chosen.Name, "and the one in use when it was saved");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // Written by the build before setups: every watcher it had goes into
        // the one setup, and nothing is lost.
        public static void TestAFileFromBeforeSetupsLoadsIntoOneSetup()
        {
            string path = TempPath();
            try
            {
                string old = "U|https://discord.com/api/webhooks/1/abc\n"
                           + "W|" + WatchStore.SerializeWatcher(Named("Secret")) + "\n"
                           + "W|" + WatchStore.SerializeWatcher(Named("Boss")) + "\n";
                File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(old), null,
                                                               DataProtectionScope.CurrentUser));

                WatchStore s = new WatchStore(path);
                s.Load();
                Assert.Equal(1, s.Setups.Count, "one setup");
                Assert.Equal(WatchStore.FirstSetupName, s.Chosen.Name, "the first one, in use");
                Assert.Equal(2, s.Watchers.Count, "holding both watchers");
                Assert.Equal("https://discord.com/api/webhooks/1/abc", s.WebhookUrl, "the webhook as it was");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // A copy is a starting point for another game, so changing it must
        // not change the original.
        public static void TestACopiedSetupHasItsOwnWatchers()
        {
            WatchStore s = new WatchStore(TempPath());
            Watcher w = Named("Secret");
            w.ChatContains = "Secret";
            s.Watchers.Add(w);

            WatchSetup copy = s.AddSetup("Steal an Egg 2", true);
            Assert.Equal(1, copy.Watchers.Count, "the same watchers");
            Assert.Equal("Secret", copy.Watchers[0].ChatContains, "set up the same way");
            Assert.False(ReferenceEquals(w, copy.Watchers[0]), "but copies of its own");

            copy.Watchers.RemoveAt(0);
            s.Choose(WatchStore.FirstSetupName);
            Assert.Equal(1, s.Watchers.Count, "the original still has its watcher");
        }

        public static void TestTheLastSetupCannotBeRemoved()
        {
            WatchStore s = new WatchStore(TempPath());
            Assert.False(s.RemoveSetup(s.Chosen), "there is always one");
            Assert.Equal(1, s.Setups.Count, "still there");
        }

        // Removing the setup in use leaves another in use, never none.
        public static void TestRemovingTheSetupInUseChoosesAnother()
        {
            WatchStore s = new WatchStore(TempPath());
            s.Watchers.Add(Named("Secret"));
            s.AddSetup("Other game", false);
            Assert.True(s.RemoveSetup(s.Chosen), "removed");
            Assert.Equal(1, s.Setups.Count, "one left");
            Assert.Equal(WatchStore.FirstSetupName, s.Chosen.Name, "and that one is in use");
            Assert.Equal(1, s.Watchers.Count, "with its watcher");
        }

        // Setups are picked by name, so two with one name could not be told
        // apart.
        public static void TestASetupNeedsANameOfItsOwn()
        {
            WatchStore s = new WatchStore(TempPath());
            s.AddSetup("Steal an Egg", false);
            Assert.Contains("name", s.SetupNameProblem("  ", null), "blank");
            Assert.Contains("already", s.SetupNameProblem("steal an egg", null), "taken, whatever the case");
            Assert.Contains("characters", s.SetupNameProblem(new string('x', 41), null), "too long for the list");
            Assert.Equal(null, s.SetupNameProblem("Steal an Egg", s.Chosen), "its own name, when renaming it");

            Assert.Equal(null, s.AddSetup("STEAL AN EGG", false), "not added");
            Assert.Equal(2, s.Setups.Count, "still two");
            Assert.False(s.RenameSetup(s.Chosen, ""), "nor renamed to nothing");
            Assert.Equal("Steal an Egg", s.Chosen.Name, "the name kept");
        }

        public static void TestANameIsTrimmed()
        {
            WatchStore s = new WatchStore(TempPath());
            Assert.Equal("Other game", s.AddSetup("  Other game  ", false).Name, "no stray spaces");
            Assert.True(s.Choose(" other GAME "), "and found however it is typed");
        }
    }
}
