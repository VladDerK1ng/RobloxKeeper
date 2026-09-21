using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Deciding which installed Roblox versions are safe to delete.
    //
    // Roblox leaves every version it has ever installed on disk - six of them,
    // about 2 GB, on the machine this was built against. Deleting them is
    // easy; deleting the wrong one makes Roblox reinstall, and its installer
    // closes every open client, which is the single most disruptive thing that
    // can happen here. So the rules are deliberately cautious.
    static class VersionCleanupTests
    {
        static List<string> V(params string[] names) { return new List<string>(names); }

        static Func<string, DateTime> Ages(params string[] pairs)
        {
            Dictionary<string, DateTime> map = new Dictionary<string, DateTime>();
            foreach (string p in pairs)
            {
                string[] bits = p.Split('=');
                map[bits[0]] = DateTime.Parse(bits[1]);
            }
            return delegate(string v) { return map.ContainsKey(v) ? map[v] : DateTime.MinValue; };
        }

        public static void TestTheVersionRobloxLaunchesIsNeverDeleted()
        {
            IList<string> go = RobloxInstall.DeletableVersions(
                V("version-a", "version-b", "version-c"),
                "version-a", V(), 1,
                Ages("version-a=2026-01-01", "version-b=2026-06-01", "version-c=2026-09-01"));

            Assert.False(go.Contains("version-a"),
                "deleting the registered one makes the next Play click reinstall Roblox");
        }

        public static void TestAVersionAClientIsRunningIsNeverDeleted()
        {
            IList<string> go = RobloxInstall.DeletableVersions(
                V("version-a", "version-b", "version-c"),
                "version-c", V("version-a"), 1,
                Ages("version-a=2026-01-01", "version-b=2026-06-01", "version-c=2026-09-01"));

            Assert.False(go.Contains("version-a"), "a client is playing on it right now");
        }

        public static void TestTheNewestAreKept()
        {
            // Roblox hands different accounts different versions, so the most
            // recent few are worth keeping to avoid a re-download.
            IList<string> go = RobloxInstall.DeletableVersions(
                V("old", "mid", "new"),
                "new", V(), 2,
                Ages("old=2026-01-01", "mid=2026-06-01", "new=2026-09-01"));

            Assert.False(go.Contains("new"), "newest kept");
            Assert.False(go.Contains("mid"), "second newest kept");
            Assert.True(go.Contains("old"), "the genuinely stale one goes");
        }

        public static void TestNothingIsDeletedWhenThereIsNothingSpare()
        {
            IList<string> go = RobloxInstall.DeletableVersions(
                V("version-a", "version-b"),
                "version-a", V(), 2,
                Ages("version-a=2026-09-01", "version-b=2026-08-01"));

            Assert.Equal(0, go.Count, "two installed, two kept, nothing to do");
        }

        public static void TestAnUnknownRegisteredVersionStillProtectsTheNewest()
        {
            // The registered version can point at a folder that is not there -
            // that is a real state this app repairs. Cleanup must not then
            // decide everything is fair game.
            IList<string> go = RobloxInstall.DeletableVersions(
                V("old", "new"),
                "version-that-is-not-installed", V(), 1,
                Ages("old=2026-01-01", "new=2026-09-01"));

            Assert.False(go.Contains("new"), "the newest is still kept");
            Assert.True(go.Contains("old"), "and the old one is still removable");
        }

        public static void TestKeepingAtLeastOneIsEnforced()
        {
            // A keep count of zero would wipe every version and force a full
            // reinstall. Refuse it rather than obey it.
            IList<string> go = RobloxInstall.DeletableVersions(
                V("only"), null, V(), 0,
                Ages("only=2026-09-01"));

            Assert.Equal(0, go.Count, "never leave the machine with no Roblox at all");
        }

        public static void TestNothingInstalledIsNotAnError()
        {
            Assert.Equal(0, RobloxInstall.DeletableVersions(V(), null, V(), 2,
                Ages()).Count, "empty in, empty out");
        }

        public static void TestEveryProtectedRuleAppliesAtOnce()
        {
            // Registered, in use, and newest are three separate reasons to keep
            // a version; a realistic machine hits all three.
            IList<string> go = RobloxInstall.DeletableVersions(
                V("ancient", "stale", "inuse", "registered", "newest"),
                "registered", V("inuse"), 1,
                Ages("ancient=2026-01-01", "stale=2026-02-01", "inuse=2026-03-01",
                     "registered=2026-04-01", "newest=2026-09-01"));

            Assert.False(go.Contains("registered"), "registered kept");
            Assert.False(go.Contains("inuse"), "in use kept");
            Assert.False(go.Contains("newest"), "newest kept");
            Assert.True(go.Contains("ancient"), "ancient goes");
            Assert.True(go.Contains("stale"), "stale goes");
            Assert.Equal(2, go.Count, "and only those two");
        }
    }
}
