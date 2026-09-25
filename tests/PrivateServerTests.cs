using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Joining a private server from the link its owner generated - asked for
    // by the owner: pasting one into the Accounts window didn't work.
    //
    // Roblox hands these out in two forms. The old one names the game and a
    // link code outright. The current one is a share link that has to be
    // resolved first, exactly as roblox.com does: sharelinks/v1/resolve-link
    // gives a universe and a link code, and the universe's root place is the
    // game. The join is then the website's own: RequestPrivateGame with that
    // place and link code.
    static class PrivateServerTests
    {
        // ---------- reading the link ----------

        public static void TestAShareLinkIsRecognised()
        {
            PrivateLink l = PrivateLink.Parse("https://www.roblox.com/share?code=2a6f1d0c9b8e4f7aa1b2c3d4e5f60718&type=Server");
            Assert.True(l != null, "a private server link");
            Assert.Equal("2a6f1d0c9b8e4f7aa1b2c3d4e5f60718", l.ShareCode, "its code");
            Assert.Equal(null, l.LinkCode, "to be resolved");
        }

        // The mobile app's link wraps the same share link, encoded.
        public static void TestAWrappedShareLinkIsRecognised()
        {
            PrivateLink l = PrivateLink.Parse("https://ro.blox.com/Ebh5?af_dp=roblox%3A%2F%2Fnavigation%2Fshare_links%3Fcode%3Dabc123DEF456%26type%3DServer"
                + "&af_web_dp=https%3A%2F%2Fwww.roblox.com%2Fshare-links%3Fcode%3Dabc123DEF456%26type%3DServer");
            Assert.True(l != null, "recognised");
            Assert.Equal("abc123DEF456", l.ShareCode, "its code");
        }

        public static void TestTheOldFormNamesTheGameAndCode()
        {
            PrivateLink l = PrivateLink.Parse("https://www.roblox.com/games/920587237/Adopt-Me?privateServerLinkCode=48155326431094526318732125643261");
            Assert.True(l != null, "a private server link");
            Assert.Equal("920587237", l.PlaceId, "the game");
            Assert.Equal("48155326431094526318732125643261", l.LinkCode, "the code, nothing to resolve");
            Assert.Equal(null, l.ShareCode, "no share code");
        }

        public static void TestOtherLinksAreNotPrivateServers()
        {
            Assert.Equal(null, PrivateLink.Parse("https://www.roblox.com/games/920587237/Adopt-Me"), "a game");
            Assert.Equal(null, PrivateLink.Parse("https://www.roblox.com/share?code=abc123&type=Profile"), "a profile share");
            Assert.Equal(null, PrivateLink.Parse("https://www.roblox.com/games/start?placeId=1&gameInstanceId=eeeeeeee-0000-0000-0000-000000000000"), "a public server");
            Assert.Equal(null, PrivateLink.Parse(""), "nothing");
        }

        // ---------- Roblox's answers ----------

        public static void TestAResolvedShareLink()
        {
            ShareLinkAnswer a = PrivateServers.ParseResolve("{\"experienceInviteData\":null,\"privateServerInviteData\":{\"status\":\"Valid\","
                + "\"ownerUserId\":1,\"privateServerId\":123,\"linkCode\":\"48155326431094526318732125643261\",\"universeId\":383310974,"
                + "\"placeId\":920587237,\"name\":\"our server\"},\"profileLinkResolutionResponseData\":null}");
            Assert.Equal("Valid", a.Status, "usable");
            Assert.Equal("383310974", a.UniverseId, "its universe");
            Assert.Equal("48155326431094526318732125643261", a.LinkCode, "its link code");
        }

        // Measured 25 Sep 2026, signed in, for a made-up code: Roblox answers
        // flat, not inside privateServerInviteData, and names the place too.
        public static void TestRobloxsFlatAnswer()
        {
            ShareLinkAnswer bad = PrivateServers.ParseResolve(
                "{\"status\":\"Expired\",\"ownerUserId\":0,\"universeId\":0,\"privateServerId\":0,\"linkCode\":null,\"placeId\":0}");
            Assert.Equal("Expired", bad.Status, "read");
            Assert.Equal(null, bad.UniverseId, "0 is no universe");
            Assert.Equal(null, bad.PlaceId, "and 0 no place");

            ShareLinkAnswer good = PrivateServers.ParseResolve(
                "{\"status\":\"Valid\",\"ownerUserId\":1,\"universeId\":383310974,\"privateServerId\":123,\"linkCode\":\"4815\",\"placeId\":920587237}");
            Assert.Equal("Valid", good.Status, "usable");
            Assert.Equal("920587237", good.PlaceId, "the game, named outright");
            Assert.Equal("4815", good.LinkCode, "and the link code");
        }

        public static void TestAResetLinkSaysSo()
        {
            ShareLinkAnswer a = PrivateServers.ParseResolve("{\"privateServerInviteData\":{\"status\":\"Invalid\",\"linkCode\":null,\"universeId\":null}}");
            Assert.Equal("Invalid", a.Status, "not usable");
            Assert.Contains("doesn't work any more", PrivateServers.Explain(a.Status), "in plain words");
            Assert.Contains("expired", PrivateServers.Explain("Expired"), "and an expired one");
        }

        // What roblox.com does with the universe: the root place is the game.
        public static void TestTheGameOfAUniverse()
        {
            Assert.Equal("920587237", PrivateServers.RootPlaceFromGames(
                "{\"data\":[{\"id\":383310974,\"rootPlaceId\":920587237,\"name\":\"Adopt Me!\"}]}"), "its root place");
            Assert.Equal(null, PrivateServers.RootPlaceFromGames("{\"data\":[]}"), "none");
        }

        // ---------- the launch ----------

        // Exactly the launcher roblox.com's joinPrivateGame builds: its own
        // host, the device's tracker, no access code, the link code, and a
        // fresh join attempt.
        public static void TestTheLaunchIsTheWebsitesOwn()
        {
            string url = RobloxAuth.BuildPrivateLaunchUrl("TICKET", "920587237", "4815", "123456789", 1000);
            string launcher = Uri.UnescapeDataString(url.Substring(url.IndexOf("+placelauncherurl:") + 18).Split('+')[0]);
            Assert.True(launcher.StartsWith("https://www.roblox.com/Game/PlaceLauncher.ashx?request=RequestPrivateGame"
                + "&browserTrackerId=123456789&placeId=920587237&accessCode=&linkCode=4815&joinAttemptId="), launcher);
            Assert.True(launcher.EndsWith("&joinAttemptOrigin=PlayButton"), "from the Play button, as the site does");
            Assert.Contains("+gameinfo:TICKET", url, "the ticket");
            Assert.Contains("+browsertrackerid:123456789", url, "the device's tracker");
        }
    }
}
