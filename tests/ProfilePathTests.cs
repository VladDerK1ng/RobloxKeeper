using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Which folder holds an account's browser session.
    //
    // Adding an account creates its profile under a temporary name, because
    // Roblox only says who signed in after the sign-in. The folder is then
    // renamed to the account. That rename can fail - WebView2's browser
    // processes outlive the window that started them and keep the folder open -
    // and when it did, the session stayed behind in the temporary folder while
    // everything else looked for the named one. Browse then opened an empty
    // profile and showed a login page.
    //
    // So the account remembers where its profile actually is, rather than
    // assuming the rename worked.
    static class ProfilePathTests
    {
        const string COOKIE = "_|WARNING:-DO-NOT-SHARE-THIS.|_ABCD";

        static RobloxAccount Acc(string name)
        {
            RobloxAccount a = new RobloxAccount();
            a.Name = name;
            a.Cookie = COOKIE;
            return a;
        }

        public static void TestAProfilePathSurvivesARoundTrip()
        {
            RobloxAccount a = Acc("Alt");
            a.ProfilePath = @"C:\Users\X\AppData\Local\RobloxKeeper\profiles\new_123_abcdef01";

            RobloxAccount b = AccountStore.Deserialize(AccountStore.Serialize(a));

            Assert.Equal(a.ProfilePath, b.ProfilePath, "the real folder is remembered");
        }

        // Accounts saved before this field existed must still load.
        public static void TestARecordWithoutAProfilePathStillLoads()
        {
            string older = "Legacy\t" + COOKIE + "\t123456789\t\t-1\t-1\t0\t0\tsome note";

            RobloxAccount a = AccountStore.Deserialize(older);

            Assert.NotEqual(null, a, "still a record");
            Assert.Equal("Legacy", a.Name, "name");
            Assert.Equal("some note", a.Note, "note");
            Assert.Equal("", a.ProfilePath ?? "", "no path stored, and no crash");
        }

        public static void TestAStoredPathIsPreferredOverTheDerivedOne()
        {
            RobloxAccount a = Acc("Alt");
            a.ProfilePath = @"C:\somewhere\else\new_999_deadbeef";

            Assert.Equal(a.ProfilePath, AccountStore.ProfilePathFor(a, @"C:\root"),
                "use where the session actually is");
        }

        public static void TestWithoutAStoredPathTheNameDecidesIt()
        {
            RobloxAccount a = Acc("Alt");

            string expected = System.IO.Path.Combine(@"C:\root", AccountStore.SafeFolderName("Alt"));
            Assert.Equal(expected, AccountStore.ProfilePathFor(a, @"C:\root"),
                "the ordinary case, where the rename worked");
        }

        // ---------- tidying up after a failed rename ----------

        public static void TestTemporaryFoldersNobodyOwnsAreOrphans()
        {
            List<string> folders = new List<string>(new string[]
                { "new_111_aaaaaaaa", "new_222_bbbbbbbb", "mrdark_9e1e2a28" });
            List<string> inUse = new List<string>(new string[] { "mrdark_9e1e2a28" });

            IList<string> orphans = AccountStore.OrphanProfiles(folders, inUse);

            Assert.Equal(2, orphans.Count, "both leftovers");
            Assert.True(orphans.Contains("new_111_aaaaaaaa"), "first");
            Assert.True(orphans.Contains("new_222_bbbbbbbb"), "second");
        }

        public static void TestAFolderAnAccountIsUsingIsNeverAnOrphan()
        {
            // The rename can fail, leaving an account pointing AT a new_ folder.
            // Deleting it would throw away that account's session.
            List<string> folders = new List<string>(new string[] { "new_111_aaaaaaaa" });
            List<string> inUse = new List<string>(new string[] { "new_111_aaaaaaaa" });

            Assert.Equal(0, AccountStore.OrphanProfiles(folders, inUse).Count,
                "a temporary name that is actually in use stays");
        }

        public static void TestARealAccountFolderIsNeverAnOrphan()
        {
            // Only folders this app named temporarily are ever candidates, so a
            // stray directory in there cannot be deleted by accident.
            List<string> folders = new List<string>(new string[] { "somebody_elses_folder" });

            Assert.Equal(0, AccountStore.OrphanProfiles(folders, new List<string>()).Count,
                "anything not named new_ is left alone");
        }

        public static void TestNothingToTidyIsFine()
        {
            Assert.Equal(0, AccountStore.OrphanProfiles(new List<string>(), new List<string>()).Count,
                "empty in, empty out");
        }
    }
}
