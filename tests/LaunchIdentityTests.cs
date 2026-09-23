using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // What a launched client is told about the device and the server.
    //
    // Measured on 23 Sep 2026, Roblox 0.740, one client, same account, same
    // tracker, consecutive launches: a client started with a specific-server
    // join froze 10 times in 3 minutes, five to eight seconds each; the same
    // launch with a plain join froze 0 times. Earlier that morning, plain joins
    // that sent each account's own tracker - not the device's - froze 6 to 84
    // times per session. Website launches, which send the device's tracker,
    // never froze. So a launch has to look like the website's.
    static class LaunchIdentityTests
    {
        // ---------- the device's own tracker ----------

        public static void TestTheDeviceTrackerIsReadFromAppStorage()
        {
            Assert.Equal("514879703",
                RobloxAuth.TrackerFromAppStorage("{\"Something\":1,\"BrowserTrackerId\":\"514879703\",\"Other\":\"x\"}"),
                "the quoted form Roblox writes");
        }

        public static void TestAnUnquotedTrackerIsReadToo()
        {
            Assert.Equal("1234567890123456",
                RobloxAuth.TrackerFromAppStorage("{ \"BrowserTrackerId\" : 1234567890123456 }"),
                "a bare number");
        }

        public static void TestNoTrackerInTheFileMeansNone()
        {
            Assert.Equal(null, RobloxAuth.TrackerFromAppStorage("{\"UserId\":\"1\"}"), "not there");
            Assert.Equal(null, RobloxAuth.TrackerFromAppStorage(""), "empty file");
            Assert.Equal(null, RobloxAuth.TrackerFromAppStorage(null), "no file");
            Assert.Equal(null, RobloxAuth.TrackerFromAppStorage("{\"BrowserTrackerId\":\"\"}"), "blank value");
        }

        // ---------- a specific server, the way the website asks ----------

        static string Launcher(string url)
        {
            return Uri.UnescapeDataString(url.Substring(url.IndexOf("+placelauncherurl:") + 18).Split('+')[0]);
        }

        public static void TestAServerJoinIsAskedForTheWayTheWebsiteAsks()
        {
            string launcher = Launcher(RobloxAuth.BuildLaunchUrl("T", "107778070777162", "12345", 1,
                                                                 "c1c5a3b9-4938-4cd8-9418-ca1a217858ae"));
            Assert.True(launcher.StartsWith("https://www.roblox.com/Game/PlaceLauncher.ashx?"), "the website's own launcher");
            Assert.Contains("request=RequestGameJob", launcher, "a particular server");
            Assert.Contains("gameId=c1c5a3b9-4938-4cd8-9418-ca1a217858ae", launcher, "this one");
            Assert.Contains("isPlayTogetherGame=false", launcher, "not a party join");
            Assert.Contains("joinAttemptOrigin=publicServerListJoin", launcher, "said to come from the server list");
        }

        public static void TestEveryServerJoinIsItsOwnAttempt()
        {
            string a = Launcher(RobloxAuth.BuildLaunchUrl("T", "1", "2", 1, "c1c5a3b9-4938-4cd8-9418-ca1a217858ae"));
            string b = Launcher(RobloxAuth.BuildLaunchUrl("T", "1", "2", 1, "c1c5a3b9-4938-4cd8-9418-ca1a217858ae"));
            string idA = a.Substring(a.IndexOf("joinAttemptId=") + 14, 36);
            string idB = b.Substring(b.IndexOf("joinAttemptId=") + 14, 36);
            Guid ga, gb;
            Assert.True(Guid.TryParse(idA, out ga) && Guid.TryParse(idB, out gb), "a real attempt id each time");
            Assert.NotEqual(idA, idB, "and a fresh one each time");
        }

        // A plain join measured clean exactly as it was, so it is left alone.
        public static void TestAPlainJoinIsUnchanged()
        {
            string launcher = Launcher(RobloxAuth.BuildLaunchUrl("T", "107778070777162", "12345", 1));
            Assert.Equal("https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId=12345&placeId=107778070777162&isPlayTogetherGame=false",
                launcher, "the form that measured 0 freezes");
        }

        // ---------- a link from the in-app browser ----------

        // roblox.com inside an account's own browser profile writes that
        // profile's tracker into the link, twice. It is swapped for the
        // device's in both places, like every other launch.
        const string FromBrowser =
            "roblox-player:1+launchmode:play+gameinfo:TICKET+launchtime:1+placelauncherurl:" +
            "https%3A%2F%2Fwww.roblox.com%2FGame%2FPlaceLauncher.ashx%3Frequest%3DRequestGame%26browserTrackerId%3D777888999%26placeId%3D1%26isPlayTogetherGame%3Dfalse" +
            "+browsertrackerid:777888999+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";

        public static void TestABrowserLinkGetsTheDevicesTrackerInBothPlaces()
        {
            string url = RobloxAuth.WithTracker(FromBrowser, "555000111");
            Assert.Contains("+browsertrackerid:555000111+", url, "the client's own parameter");
            Assert.Contains("browserTrackerId%3D555000111%26", url, "and inside the launcher link");
            Assert.False(url.Contains("777888999"), "the profile's tracker is gone");
        }

        public static void TestTheRestOfABrowserLinkIsUntouched()
        {
            string url = RobloxAuth.WithTracker(FromBrowser, "555000111");
            Assert.Equal(FromBrowser.Replace("777888999", "555000111"), url, "only the tracker changed");
        }

        public static void TestWithNoDeviceTrackerTheLinkIsLeftAsItIs()
        {
            Assert.Equal(FromBrowser, RobloxAuth.WithTracker(FromBrowser, null), "nothing to swap in");
            Assert.Equal(FromBrowser, RobloxAuth.WithTracker(FromBrowser, ""), "blank");
            Assert.Equal(null, RobloxAuth.WithTracker(null, "555000111"), "no link");
        }

    }
}
