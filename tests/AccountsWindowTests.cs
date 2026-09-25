using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper.Tests
{
    // The Accounts window: order, where they go, who is playing, and what it
    // remembers. Launching itself is AccountLaunchTests; this is the window
    // around it.
    static class AccountsWindowTests
    {
        class Host : IAccountsHost
        {
            public readonly AccountsPrefs P = new AccountsPrefs();
            public readonly Dictionary<string, int> Pids = new Dictionary<string, int>();
            public readonly List<string> Logged = new List<string>();
            public int Saves;

            public void Log(string line) { Logged.Add(line); }
            public void Launched(int pid, string account) { }
            public int PidOf(string account) { int p; return Pids.TryGetValue(account, out p) ? p : 0; }
            public bool IsHunting(string account) { return false; }
            public AccountsPrefs Prefs { get { return P; } }
            public void SavePrefs() { Saves++; }
            public void Follow(FollowRequest f) { }
            public string Following { get { return null; } }
            public void StopFollowing() { }
        }

        static AccountStore Store(string path, params string[] names)
        {
            AccountStore s = new AccountStore(path);
            foreach (string n in names)
            {
                RobloxAccount a = new RobloxAccount();
                a.Name = n;
                a.Cookie = "c";
                s.Add(a);
            }
            return s;
        }

        static string TempPath() { return Path.Combine(Path.GetTempPath(), "rk-accwin-" + Guid.NewGuid().ToString("N")); }

        public static void TestTheWindowRemembersWhoWasTicked()
        {
            Host h = new Host();
            h.P.Ticked.Add("alt2");
            using (AccountsDialog d = new AccountsDialog(Store(TempPath(), "main", "alt1", "alt2"), h))
            {
                Assert.Equal("alt2", d.TickedNames, "ticked as it was left");
                d.Tick("alt1", true);
                Assert.Equal("alt1,alt2", string.Join(",", h.P.Ticked.ToArray()), "and remembers the change");
                Assert.True(h.Saves > 0, "saved");
            }
        }

        // A player's name is only asked for when joining a player, and "all
        // in the same server" only means something otherwise.
        public static void TestEachChoiceShowsWhatItNeeds()
        {
            Host h = new Host();
            using (AccountsDialog d = new AccountsDialog(Store(TempPath(), "main"), h))
            {
                d.SetWhere(JoinWhere.Emptiest);
                Assert.True(d.ShowsTogether && !d.ShowsPlayer, "emptiest: the together box");
                d.SetWhere(JoinWhere.Player);
                Assert.True(d.ShowsPlayer && !d.ShowsTogether, "a player: the name box and keep following");
                Assert.Equal(JoinWhere.Player, h.P.Where, "remembered");
            }
        }

        public static void TestMovingAnAccountSavesTheOrder()
        {
            string path = TempPath();
            try
            {
                using (AccountsDialog d = new AccountsDialog(Store(path, "main", "alt1", "alt2"), new Host()))
                    d.MoveAt(2, 0);
                AccountStore back = new AccountStore(path);
                back.Load();
                Assert.Equal("alt2", back.Accounts[0].Name, "first now, and saved");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // An account's own game can be a private server link, and its row says so.
        public static void TestAnAccountsOwnPrivateServerShows()
        {
            AccountStore s = Store(TempPath(), "main", "alt1");
            s.Find("alt1").GameUrl = "https://www.roblox.com/share?code=abc123&type=Server";
            s.Find("main").GameUrl = "https://www.roblox.com/games/920587237/Adopt-Me";
            using (AccountsDialog d = new AccountsDialog(s, new Host()))
            {
                Assert.Equal("has a saved game", d.RowLine(0), "a game");
                Assert.Equal("has a private server", d.RowLine(1), "a private server");
            }
        }

        public static void TestAccountsWithAClientOpenSaySo()
        {
            Host h = new Host();
            h.Pids["alt1"] = 4242;
            using (AccountsDialog d = new AccountsDialog(Store(TempPath(), "main", "alt1"), h))
            {
                Assert.Contains("playing", d.RowLine(1), "alt1 is playing");
                Assert.False(d.RowLine(0).Contains("playing"), "main isn't");
                h.Pids.Remove("alt1");
                d.RefreshPlaying();
                Assert.False(d.RowLine(1).Contains("playing"), "and stops saying so when it closes");
            }
        }
    }
}
