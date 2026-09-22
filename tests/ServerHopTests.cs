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
