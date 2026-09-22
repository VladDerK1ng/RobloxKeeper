using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Choosing the next server, remembering where we have been, and asking
    // Roblox for that exact server.
    static class ServerHopTests
    {
        static PublicServer S(string id, int playing, int max)
        {
            PublicServer s = new PublicServer();
            s.Id = id; s.Playing = playing; s.MaxPlayers = max;
            return s;
        }

        static readonly Func<string, bool> Nothing = delegate(string id) { return false; };

        public static void TestAFullServerIsNeverPicked()
        {
            List<PublicServer> list = new List<PublicServer> { S("full", 7, 7), S("room", 3, 7) };
            for (int seed = 0; seed < 20; seed++)
                Assert.Equal("room", ServerPicker.Pick(list, Nothing, new Random(seed)).Id, "only the one with room, seed " + seed);
        }

        // Where the client is now, where it has been lately, and where another
        // of our clients already is.
        public static void TestServersToAvoidAreAvoided()
        {
            List<PublicServer> list = new List<PublicServer> { S("here", 1, 7), S("been", 1, 7), S("fresh", 1, 7) };
            Func<string, bool> avoid = delegate(string id) { return id == "here" || id == "been"; };
            for (int seed = 0; seed < 20; seed++)
                Assert.Equal("fresh", ServerPicker.Pick(list, avoid, new Random(seed)).Id, "the one not avoided, seed " + seed);
        }

        // One slot left can be gone by the time the client arrives, and the
        // join fails. Two or more is a safer bet whenever there is one.
        public static void TestAServerWithAFewSpacesIsPreferredOverOneWithTheLast()
        {
            List<PublicServer> list = new List<PublicServer> { S("last slot", 6, 7), S("spaces", 4, 7) };
            for (int seed = 0; seed < 20; seed++)
                Assert.Equal("spaces", ServerPicker.Pick(list, Nothing, new Random(seed)).Id, "two free beats one, seed " + seed);
            List<PublicServer> only = new List<PublicServer> { S("last slot", 6, 7) };
            Assert.Equal("last slot", ServerPicker.Pick(only, Nothing, new Random(1)).Id, "but the last slot beats nothing");
        }

        // Two hunting accounts picking from the same list should not both
        // land in the same server, so the pick is spread out.
        public static void TestPicksAreSpreadOut()
        {
            List<PublicServer> list = new List<PublicServer>();
            for (int i = 0; i < 20; i++) list.Add(S("s" + i, 1, 7));
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            for (int seed = 0; seed < 30; seed++) seen[ServerPicker.Pick(list, Nothing, new Random(seed)).Id] = true;
            Assert.True(seen.Count >= 8, "spread over " + seen.Count + " servers");
        }

        public static void TestNothingToPickIsSaidSo()
        {
            Assert.Equal(null, ServerPicker.Pick(new List<PublicServer> { S("full", 7, 7) }, Nothing, new Random(1)), "all full");
            Assert.Equal(null, ServerPicker.Pick(new List<PublicServer>(), Nothing, new Random(1)), "none at all");
        }

        // ---------- which servers a hunt prefers ----------

        static List<PublicServer> Mixed()
        {
            List<PublicServer> list = new List<PublicServer>();
            int[] counts = { 1, 2, 3, 5, 8, 11, 12, 14, 20, 23, 25 };
            foreach (int n in counts) list.Add(S("p" + n, n, 25));
            return list;
        }

        static ServerPrefs Prefs(ServerSize size, int min, int max)
        {
            ServerPrefs p = new ServerPrefs();
            p.Size = size; p.MinPlayers = min; p.MaxPlayers = max;
            return p;
        }

        // Something anyone can pick up: go where there are fewest people.
        public static void TestTheEmptiestServersCanBePreferred()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                PublicServer s = ServerPicker.Pick(Mixed(), Nothing, new Random(seed), Prefs(ServerSize.Emptiest, 0, 0));
                Assert.True(s.Playing <= 5, "one of the emptiest, seed " + seed + " got " + s.Playing);
            }
        }

        // A bounty to hunt: go where the people are - but never a full one,
        // and still preferring room for two.
        public static void TestTheBusiestServersCanBePreferred()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                PublicServer s = ServerPicker.Pick(Mixed(), Nothing, new Random(seed), Prefs(ServerSize.Busiest, 0, 0));
                Assert.True(s.Playing >= 12 && s.Playing <= 23, "one of the busiest with room for two, seed " + seed + " got " + s.Playing);
            }
        }

        public static void TestPlayerLimitsAreKept()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                PublicServer s = ServerPicker.Pick(Mixed(), Nothing, new Random(seed), Prefs(ServerSize.Any, 5, 12));
                Assert.True(s.Playing >= 5 && s.Playing <= 12, "between 5 and 12, seed " + seed + " got " + s.Playing);
            }
            Assert.Equal(null, ServerPicker.Pick(Mixed(), Nothing, new Random(1), Prefs(ServerSize.Any, 30, 0)),
                "nobody has thirty");
        }

        // The list is asked for fullest first when the fullest are wanted,
        // so they are on the first page rather than the tenth.
        public static void TestTheBusiestAreAskedForFirst()
        {
            Assert.Contains("sortOrder=Desc", RobloxServers.PageUrl("1", null, true), "fullest first");
            Assert.Contains("sortOrder=Asc", RobloxServers.PageUrl("1", null, false), "emptiest first");
        }

        public static void TestServersVisitedLatelyAreRemembered()
        {
            HopHistory h = new HopHistory();
            DateTime t = new DateTime(2026, 9, 22, 18, 0, 0);
            h.Visited("abc", t);
            Assert.True(h.WasRecent("abc", t.AddMinutes(10)), "ten minutes ago");
            Assert.False(h.WasRecent("abc", t.AddMinutes(31)), "half an hour ago is long enough");
            Assert.False(h.WasRecent("xyz", t), "never visited");
            h.Visited("def", t);
            h.Visited("abc", t.AddMinutes(1));
            Assert.Equal(2, h.Count, "two different servers");
        }

        // The place launcher is told which server, the way the website's own
        // Join button on a server does it.
        public static void TestJoiningAServerNamesIt()
        {
            string url = RobloxAuth.BuildLaunchUrl("TICKET", "107778070777162", "12345", 1000,
                                                   "c1c5a3b9-4938-4cd8-9418-ca1a217858ae");
            string launcher = Uri.UnescapeDataString(url.Substring(url.IndexOf("+placelauncherurl:") + 18).Split('+')[0]);
            Assert.Contains("request=RequestGameJob", launcher, "asks for a particular server");
            Assert.Contains("gameId=c1c5a3b9-4938-4cd8-9418-ca1a217858ae", launcher, "this one");
            Assert.Contains("placeId=107778070777162", launcher, "in this game");

            string any = RobloxAuth.BuildLaunchUrl("TICKET", "107778070777162", "12345", 1000);
            Assert.Contains("request%3DRequestGame%26", any, "without a server, any server - as before");
        }
    }
}
