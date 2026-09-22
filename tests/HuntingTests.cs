using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Hunt mode as the app runs it: what is remembered between runs, what
    // has to be true before it may start, and what the main window says.
    static class HuntingTests
    {
        public static void TestHuntSettingsAreRemembered()
        {
            string path = Path.Combine(Path.GetTempPath(), "rk-hunt-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                WatchStore s = new WatchStore(path);
                Assert.Equal(60, s.Hunt.LookSeconds, "a minute a server to begin with");
                Assert.True(s.Hunt.StayWhenFound, "and staying when something is found");

                s.Hunt.GameLink = "https://www.roblox.com/games/107778070777162/Steal-an-Egg";
                s.Hunt.LookSeconds = 45;
                s.Hunt.StayWhenFound = false;
                s.Hunt.Accounts = new string[] { "VladDerKing", "alt1" };
                s.Save();

                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.Equal("https://www.roblox.com/games/107778070777162/Steal-an-Egg", back.Hunt.GameLink, "the game");
                Assert.Equal(45, back.Hunt.LookSeconds, "the time");
                Assert.False(back.Hunt.StayWhenFound, "keep going");
                Assert.Equal(2, back.Hunt.Accounts.Length, "both accounts");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        static readonly Func<string, bool> Yes = delegate(string a) { return true; };

        public static void TestAHuntNeedsAGameAndAnAccount()
        {
            Assert.Contains("game", Hunting.StartProblem(null, new string[] { "a" }, Yes, Yes), "no game");
            Assert.Contains("account", Hunting.StartProblem("1", new string[0], Yes, Yes), "no account");
            Assert.Equal(null, Hunting.StartProblem("1", new string[] { "a" }, Yes, Yes), "both");
        }

        // Moving a client means starting it again as that account, which
        // needs the account's saved sign-in.
        public static void TestAHuntNeedsEachAccountsSignIn()
        {
            string p = Hunting.StartProblem("1", new string[] { "VladDerKing", "alt1" },
                delegate(string a) { return a != "alt1"; }, Yes);
            Assert.Contains("alt1", p, "names the account");
            Assert.Contains("sign", p, "and what it needs");
        }

        // With nothing watching an account's client, a hunt would hop for
        // ever and never find anything.
        public static void TestAHuntNeedsSomethingWatchingEachAccount()
        {
            string p = Hunting.StartProblem("1", new string[] { "VladDerKing" }, Yes, delegate(string a) { return false; });
            Assert.Contains("VladDerKing", p, "names the account");
            Assert.Contains("watcher", p, "and what is missing");
        }

        public static void TestTheMainWindowSaysAHuntIsOn()
        {
            Assert.Equal("Hunting · 2 accounts · 7 servers so far", Watching.HuntStatusLine(2, 7, false), "the full line");
            Assert.Equal("Hunting · 1 account · 1 server so far", Watching.HuntStatusLine(1, 1, false), "the singular");
            Assert.Equal("hunting · 7 servers", Watching.HuntStatusLine(2, 7, true), "beside the setup picker");
            using (System.Drawing.Font f = new System.Drawing.Font("Segoe UI", 8.25f))
            {
                Assert.True(System.Windows.Forms.TextRenderer.MeasureText(Watching.HuntStatusLine(20, 999, false), f).Width <= 262, "fits its row");
                Assert.True(System.Windows.Forms.TextRenderer.MeasureText(Watching.HuntStatusLine(20, 999, true), f).Width <= Watching.SETUP_LINE_W, "and beside the picker");
            }
        }
    }
}
