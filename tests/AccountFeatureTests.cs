using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Knowing which client is which account, joining from the browser, and the
    // per-account note.
    static class AccountFeatureTests
    {
        const string COOKIE = "_|WARNING:-DO-NOT-SHARE-THIS.|_ABCD1234";

        static RobloxAccount Acc(string name)
        {
            RobloxAccount a = new RobloxAccount();
            a.Name = name;
            a.Cookie = COOKIE;
            a.BrowserTrackerId = "123456789";
            return a;
        }

        // ---------- the note ----------

        public static void TestANoteSurvivesARoundTrip()
        {
            RobloxAccount a = Acc("Farmer");
            a.Note = "farms blox fruits overnight";

            RobloxAccount b = AccountStore.Deserialize(AccountStore.Serialize(a));

            Assert.Equal("farms blox fruits overnight", b.Note, "the note comes back");
        }

        public static void TestANoteWithTabsAndNewlinesSurvives()
        {
            RobloxAccount a = Acc("Odd");
            a.Note = "line one\tcolumn\nline two";

            RobloxAccount b = AccountStore.Deserialize(AccountStore.Serialize(a));

            Assert.Equal("line one\tcolumn\nline two", b.Note, "separators inside a note stay inside it");
            Assert.Equal("Odd", b.Name, "and the record is not corrupted");
        }

        // Accounts saved before notes existed must still load.
        public static void TestAnOlderRecordWithoutANoteStillLoads()
        {
            string old = "Legacy\t" + COOKIE + "\t123456789\t\t-1\t-1\t0\t0";

            RobloxAccount a = AccountStore.Deserialize(old);

            Assert.NotEqual(null, a, "an eight-field record is still a record");
            Assert.Equal("Legacy", a.Name, "name");
            Assert.Equal(COOKIE, a.Cookie, "cookie");
            Assert.Equal("", a.Note ?? "", "no note, and no crash");
        }

        public static void TestANoteIsNotAllowedToLeakACookie()
        {
            // The note is shown in the list and the log, so it must be the
            // note and nothing else.
            RobloxAccount a = Acc("Farmer");
            a.Note = "does things";
            Assert.False(a.ToString().Contains(COOKIE), "still no cookie in a description");
        }

        // ---------- which client is which account ----------

        public static void TestAClientLaunchedForAnAccountIsLabelled()
        {
            ClientLabels labels = new ClientLabels();
            labels.Assign(1234, "FarmAlt");

            Assert.Equal("FarmAlt", labels.NameFor(1234), "the client we launched");
        }

        public static void TestAClientWeDidNotLaunchHasNoLabel()
        {
            ClientLabels labels = new ClientLabels();
            labels.Assign(1234, "FarmAlt");

            Assert.Equal(null, labels.NameFor(9999), "a client started from the website is unlabelled");
        }

        // Windows reuses PIDs, so a label surviving its process would eventually
        // put somebody's account name on an unrelated client.
        public static void TestALabelIsDroppedWhenItsClientExits()
        {
            ClientLabels labels = new ClientLabels();
            labels.Assign(1234, "FarmAlt");
            labels.Assign(5678, "MainAlt");

            labels.Prune(new List<int>(new int[] { 5678 }));

            Assert.Equal(null, labels.NameFor(1234), "the closed client's label is gone");
            Assert.Equal("MainAlt", labels.NameFor(5678), "the live one keeps its label");
        }

        public static void TestPruningToNothingClearsEverything()
        {
            ClientLabels labels = new ClientLabels();
            labels.Assign(1, "A");
            labels.Assign(2, "B");

            labels.Prune(new List<int>());

            Assert.Equal(null, labels.NameFor(1), "all gone");
            Assert.Equal(null, labels.NameFor(2), "all gone");
        }

        public static void TestRelaunchingAnAccountMovesItsLabel()
        {
            // Roblox can replace the process it was given; the account should
            // follow the client that is actually alive.
            ClientLabels labels = new ClientLabels();
            labels.Assign(1000, "FarmAlt");
            labels.Assign(2000, "FarmAlt");

            Assert.Equal("FarmAlt", labels.NameFor(2000), "the new process is the account");
            Assert.Equal("FarmAlt", labels.NameFor(1000), "the old one keeps it until it is pruned");
        }

        public static void TestTheRowLabelUsesTheAccountNameWhenThereIsOne()
        {
            Assert.Equal("FarmAlt", ClientLabels.RowTitle("FarmAlt", 3),
                "a known account is named");
            Assert.Equal("Client 3", ClientLabels.RowTitle(null, 3),
                "an unknown one keeps its number");
        }

        // ---------- joining from the browser ----------

        public static void TestARobloxLaunchUrlIsRecognised()
        {
            Assert.True(RobloxAuth.IsLaunchUrl(
                "roblox-player:1+launchmode:play+gameinfo:TICKET+launchtime:1"),
                "pressing Play in the browser produces this");
        }

        public static void TestTheOtherRobloxSchemeIsRecognisedToo()
        {
            Assert.True(RobloxAuth.IsLaunchUrl("roblox://placeId=123"), "the shorter scheme");
        }

        public static void TestOrdinaryBrowsingIsNotALaunch()
        {
            Assert.False(RobloxAuth.IsLaunchUrl("https://www.roblox.com/games/123/Name"),
                "browsing a game page is not joining it");
            Assert.False(RobloxAuth.IsLaunchUrl("https://www.roblox.com/home"), "the home page");
            Assert.False(RobloxAuth.IsLaunchUrl(""), "empty");
            Assert.False(RobloxAuth.IsLaunchUrl(null), "null");
        }

        // A page could try to send the browser somewhere that is not Roblox.
        public static void TestANonRobloxSchemeIsNotTreatedAsALaunch()
        {
            Assert.False(RobloxAuth.IsLaunchUrl("steam://run/12345"), "another app's protocol");
            Assert.False(RobloxAuth.IsLaunchUrl("file:///C:/Windows/System32/cmd.exe"), "a local file");
        }
    }
}
