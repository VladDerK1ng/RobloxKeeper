using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Where account credentials live.
    //
    // A .ROBLOSECURITY cookie IS the account - anyone holding one is logged in
    // as that user, no password needed. So the file is encrypted with DPAPI at
    // CurrentUser scope, which makes it unreadable on any other machine or
    // under any other Windows account, and nothing that can reach a log, a
    // window title or an error message is ever allowed to carry one.
    static class AccountStoreTests
    {
        static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "rk-acc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        // A realistic cookie: the warning banner Roblox prefixes, then hex.
        const string COOKIE = "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you"
                            + "-and-to-steal-your-ROBUX-and-items.|_ABCD1234EF567890";

        static RobloxAccount Acc(string name, string cookie)
        {
            RobloxAccount a = new RobloxAccount();
            a.Name = name;
            a.Cookie = cookie;
            a.BrowserTrackerId = "12345678901";
            return a;
        }

        // ---------- serialisation ----------

        public static void TestAnAccountSurvivesARoundTrip()
        {
            RobloxAccount a = Acc("MainAlt", COOKIE);
            a.GameUrl = "https://www.roblox.com/games/123456/Some-Place";
            a.NudgeMethod = NudgeMethod.MOVE;

            RobloxAccount b = AccountStore.Deserialize(AccountStore.Serialize(a));

            Assert.Equal("MainAlt", b.Name, "name");
            Assert.Equal(COOKIE, b.Cookie, "cookie intact - a truncated one is a dead account");
            Assert.Equal("12345678901", b.BrowserTrackerId, "tracker id");
            Assert.Equal("https://www.roblox.com/games/123456/Some-Place", b.GameUrl, "game url");
            Assert.Equal(NudgeMethod.MOVE, b.NudgeMethod, "nudge method");
        }

        public static void TestFieldsContainingSeparatorsSurvive()
        {
            // A display name with a tab or newline in it must not be able to
            // corrupt the record that follows, or one bad name silently eats
            // every account after it.
            RobloxAccount a = Acc("we\tird\nname\\here", COOKIE);

            RobloxAccount b = AccountStore.Deserialize(AccountStore.Serialize(a));

            Assert.Equal("we\tird\nname\\here", b.Name, "separators and backslashes survive");
            Assert.Equal(COOKIE, b.Cookie, "and the cookie after it is unharmed");
        }

        public static void TestEmptyOptionalFieldsAreFine()
        {
            RobloxAccount a = Acc("Bare", COOKIE);
            RobloxAccount b = AccountStore.Deserialize(AccountStore.Serialize(a));

            Assert.Equal("Bare", b.Name, "name");
            Assert.Equal("", b.GameUrl ?? "", "no game set");
        }

        public static void TestAGarbledLineIsSkippedNotFatal()
        {
            Assert.Equal(null, AccountStore.Deserialize("nonsense-with-no-fields"),
                "a corrupt record returns nothing rather than throwing");
        }

        // ---------- the store ----------

        public static void TestAddingAndFindingAccounts()
        {
            string dir = TempDir();
            try
            {
                AccountStore s = new AccountStore(Path.Combine(dir, "accounts.dat"));
                s.Add(Acc("One", COOKIE));
                s.Add(Acc("Two", COOKIE));

                Assert.Equal(2, s.Accounts.Count, "two accounts");
                Assert.Equal("One", s.Find("One").Name, "found by name");
                Assert.Equal(null, s.Find("Missing"), "absent name returns nothing");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestAddingTheSameNameReplacesRatherThanDuplicates()
        {
            string dir = TempDir();
            try
            {
                AccountStore s = new AccountStore(Path.Combine(dir, "accounts.dat"));
                s.Add(Acc("One", COOKIE));
                RobloxAccount updated = Acc("One", COOKIE + "NEWER");
                s.Add(updated);

                Assert.Equal(1, s.Accounts.Count, "still one account");
                Assert.Contains("NEWER", s.Find("One").Cookie, "re-adding refreshes the cookie");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestRemovingAnAccount()
        {
            string dir = TempDir();
            try
            {
                AccountStore s = new AccountStore(Path.Combine(dir, "accounts.dat"));
                s.Add(Acc("One", COOKIE));
                s.Add(Acc("Two", COOKIE));
                s.Remove("One");

                Assert.Equal(1, s.Accounts.Count, "one left");
                Assert.Equal(null, s.Find("One"), "gone");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---------- on disk ----------

        public static void TestSavedAccountsComeBack()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "accounts.dat");
                AccountStore s = new AccountStore(path);
                s.Add(Acc("Persisted", COOKIE));
                s.Save();

                AccountStore again = new AccountStore(path);
                again.Load();

                Assert.Equal(1, again.Accounts.Count, "loaded back");
                Assert.Equal(COOKIE, again.Find("Persisted").Cookie, "cookie intact across a save/load");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // The property that matters most: the file must be useless to anyone
        // who copies it off this machine.
        public static void TestTheFileOnDiskIsNotReadable()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "accounts.dat");
                AccountStore s = new AccountStore(path);
                s.Add(Acc("Secret", COOKIE));
                s.Save();

                byte[] raw = File.ReadAllBytes(path);
                string asText = System.Text.Encoding.UTF8.GetString(raw);

                Assert.False(asText.Contains(COOKIE), "the cookie must not be sitting there in plaintext");
                Assert.False(asText.Contains("Secret"), "nor the account name");
                Assert.False(asText.Contains("WARNING"), "nor any recognisable part of it");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestLoadingWhenNothingIsSavedYetIsEmptyNotAnError()
        {
            string dir = TempDir();
            try
            {
                AccountStore s = new AccountStore(Path.Combine(dir, "accounts.dat"));
                s.Load();
                Assert.Equal(0, s.Accounts.Count, "first run has no accounts");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestACorruptFileIsNotFatal()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "accounts.dat");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

                AccountStore s = new AccountStore(path);
                s.Load();

                Assert.Equal(0, s.Accounts.Count, "unreadable file loads as empty instead of crashing the app");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---------- leak safety ----------

        public static void TestDescribingAnAccountNeverRevealsItsCookie()
        {
            // Descriptions reach the Activity log, tooltips and error messages.
            RobloxAccount a = Acc("MainAlt", COOKIE);
            string described = a.ToString();

            Assert.Contains("MainAlt", described, "the name is fine to show");
            Assert.False(described.Contains(COOKIE), "the cookie is not");
            Assert.False(described.Contains("WARNING"), "not even part of it");
        }

        public static void TestAnAccountWithNoCookieIsNotUsable()
        {
            RobloxAccount a = new RobloxAccount();
            a.Name = "Empty";
            Assert.False(a.IsUsable, "no cookie means it cannot launch anything");

            a.Cookie = COOKIE;
            Assert.True(a.IsUsable, "with a cookie it can");
        }

        public static void TestEachAccountGetsItsOwnBrowserTrackerId()
        {
            string one = AccountStore.NewBrowserTrackerId();
            string two = AccountStore.NewBrowserTrackerId();

            Assert.NotEqual(one, two, "two accounts must not share a device identity");
            Assert.True(one.Length >= 6, "long enough to look like a real tracker id");
        }
    }
}
