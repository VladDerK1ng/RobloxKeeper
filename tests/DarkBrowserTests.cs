using System;

namespace RobloxKeeper.Tests
{
    // The account browser in Roblox's own dark theme.
    //
    // Measured on roblox.com: the site doesn't follow Windows' dark setting.
    // Its Theme.js takes a class the server put on <body>, then a
    // RBXThemeOverride cookie, then what the page last chose, then light - and
    // offers Roblox['core-scripts']['color-mode'].setMode('dark'). Swapping
    // the body class gave Roblox's real dark theme, not a filter over it.
    static class DarkBrowserTests
    {
        public static void TestTheCookieIsTheOneRobloxsThemeScriptReads()
        {
            Assert.Equal("RBXThemeOverride", DarkRoblox.CookieName, "Theme.js reads this cookie");
            Assert.Equal("dark", DarkRoblox.CookieValue, "one of the modes it knows");
            Assert.Equal(".roblox.com", DarkRoblox.CookieDomain, "for every roblox.com page");
        }

        public static void TestTheScriptUsesRobloxsOwnSwitch()
        {
            Assert.Contains("setMode('dark')", DarkRoblox.Script, "Roblox's own switch, when the page has it");
            Assert.Contains("dark-theme", DarkRoblox.Script, "and the class it sets, when it hasn't");
            Assert.Contains("MutationObserver", DarkRoblox.Script, "kept dark if the page puts light back");
        }

        // It runs on every page the browser opens; only Roblox's are touched.
        public static void TestTheScriptLeavesOtherSitesAlone()
        {
            Assert.Contains("roblox\\.com", DarkRoblox.Script, "checks where it is first");
        }

        // A page can start a client three ways - a navigation, a frame's, or
        // an external-protocol request - and one press can raise more than one
        // of them. It is launched once.
        public static void TestOnePressLaunchesOnce()
        {
            LaunchOnce once = new LaunchOnce();
            DateTime t = new DateTime(2026, 9, 23, 10, 0, 0);
            Assert.True(once.Take("roblox-player:1+a", t), "the first");
            Assert.False(once.Take("roblox-player:1+a", t.AddSeconds(1)), "the same one, a moment later");
            Assert.True(once.Take("roblox-player:1+b", t.AddSeconds(1)), "a different one");
            Assert.True(once.Take("roblox-player:1+a", t.AddSeconds(10)), "pressed again later");
        }
    }
}
