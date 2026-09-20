using System;

namespace RobloxKeeper.Tests
{
    // Turning a saved account into a running client.
    //
    // Roblox's own flow: exchange the account's cookie for a single-use
    // authentication ticket, then hand that ticket to RobloxPlayerBeta on the
    // command line as a roblox-player:// URL. That is the same URL the website
    // produces when you press Play - nothing is faked or bypassed, the account
    // simply authenticates itself.
    //
    // The network half needs Roblox and is verified by launching. The URL
    // construction and place-id parsing are pure, and are where a silent
    // mistake would produce "Authentication Failed" with no clue why.
    static class RobloxAuthTests
    {
        const string TICKET = "T-abc123XYZ_-456";
        const string TRACKER = "123456789";

        // ---------- place ids ----------

        public static void TestReadsThePlaceIdFromAGameUrl()
        {
            Assert.Equal("123456",
                RobloxAuth.PlaceIdFromUrl("https://www.roblox.com/games/123456/Some-Place-Name"),
                "the usual link you copy from the address bar");
        }

        public static void TestReadsThePlaceIdWithAQueryString()
        {
            Assert.Equal("987654",
                RobloxAuth.PlaceIdFromUrl("https://www.roblox.com/games/987654/Name?privateServerLinkCode=xyz"),
                "links carry tracking and private-server parameters");
        }

        public static void TestReadsThePlaceIdWithNoTrailingName()
        {
            Assert.Equal("555",
                RobloxAuth.PlaceIdFromUrl("https://www.roblox.com/games/555"), "bare game url");
        }

        public static void TestAcceptsAPlaceIdOnItsOwn()
        {
            Assert.Equal("101770480176177",
                RobloxAuth.PlaceIdFromUrl("101770480176177"), "pasting just the number should work");
        }

        public static void TestIgnoresSurroundingWhitespace()
        {
            Assert.Equal("123456",
                RobloxAuth.PlaceIdFromUrl("  https://www.roblox.com/games/123456/X  "), "pasted with spaces");
        }

        public static void TestRejectsSomethingThatIsNotAGameLink()
        {
            Assert.Equal(null, RobloxAuth.PlaceIdFromUrl("https://www.roblox.com/users/1/profile"),
                "a profile link is not a place");
            Assert.Equal(null, RobloxAuth.PlaceIdFromUrl("not a url"), "nonsense");
            Assert.Equal(null, RobloxAuth.PlaceIdFromUrl(""), "empty");
            Assert.Equal(null, RobloxAuth.PlaceIdFromUrl(null), "null");
        }

        // ---------- the launch url ----------

        public static void TestTheLaunchUrlCarriesEverythingRobloxNeeds()
        {
            string url = RobloxAuth.BuildLaunchUrl(TICKET, "123456", TRACKER, 1700000000000L);

            Assert.True(url.StartsWith("roblox-player:"), "it is a roblox-player URL");
            Assert.Contains("gameinfo:" + TICKET, url, "the ticket, which is what authenticates the account");
            Assert.Contains("browsertrackerid:" + TRACKER, url, "this account's own device identity");
            Assert.Contains("launchtime:1700000000000", url, "launch time");
            Assert.Contains("placelauncherurl:", url, "the place launcher");
        }

        public static void TestThePlaceLauncherUrlIsEncodedNotRaw()
        {
            string url = RobloxAuth.BuildLaunchUrl(TICKET, "123456", TRACKER, 1L);

            // The launcher URL is a parameter inside another URL. Left raw, its
            // own ? and & terminate the outer one and Roblox receives junk.
            Assert.False(url.Contains("placelauncherurl:https://"),
                "must be percent-encoded, not pasted in raw");
            Assert.Contains("placelauncherurl:https%3a%2f%2f", url.ToLowerInvariant(), "encoded form");
        }

        public static void TestThePlaceIdReachesTheLauncherUrl()
        {
            string url = RobloxAuth.BuildLaunchUrl(TICKET, "999888", TRACKER, 1L);
            Assert.Contains("999888", url, "the place actually being joined");
        }

        public static void TestTheTrackerIdReachesTheLauncherUrlToo()
        {
            // It appears twice, once for the client and once inside the
            // launcher request. Both matter for keeping accounts distinct.
            string url = RobloxAuth.BuildLaunchUrl(TICKET, "1", "5551234", 1L);
            int first = url.IndexOf("5551234", StringComparison.Ordinal);
            int second = url.IndexOf("5551234", first + 1, StringComparison.Ordinal);
            Assert.True(second > first, "tracker id present in both places");
        }

        public static void TestNoTicketMeansNoUrl()
        {
            // Better to refuse than to launch something that will fail with an
            // unexplained "Authentication Failed".
            Assert.Equal(null, RobloxAuth.BuildLaunchUrl(null, "123", TRACKER, 1L), "null ticket");
            Assert.Equal(null, RobloxAuth.BuildLaunchUrl("", "123", TRACKER, 1L), "empty ticket");
        }

        public static void TestNoPlaceMeansNoUrl()
        {
            Assert.Equal(null, RobloxAuth.BuildLaunchUrl(TICKET, null, TRACKER, 1L), "null place");
            Assert.Equal(null, RobloxAuth.BuildLaunchUrl(TICKET, "", TRACKER, 1L), "empty place");
        }

        public static void TestAMissingTrackerIdIsFilledInRatherThanLeftBlank()
        {
            // An empty browsertrackerid is exactly the shared-identity problem
            // that gets accounts evicted as duplicate logins.
            string url = RobloxAuth.BuildLaunchUrl(TICKET, "123", null, 1L);
            Assert.True(url != null, "still builds");
            Assert.False(url.Contains("browsertrackerid:+"), "no empty tracker id");
            Assert.False(url.EndsWith("browsertrackerid:"), "nor a trailing empty one");
        }
    }
}
