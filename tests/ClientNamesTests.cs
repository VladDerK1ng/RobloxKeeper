using System;
using System.Collections.Generic;
using System.Threading;

namespace RobloxKeeper.Tests
{
    // Which account a client is signed in as, from Roblox's own word.
    //
    // A client used to be named by the process the app started. Roblox 0.740
    // can hand a launch to one of its own tray copies and exit, so the game
    // ended up in a process nobody had named - which is why a client launched
    // from the account browser showed up as a number. Roblox writes the user
    // id on every join; these are the rules for turning it into a name.
    static class ClientNamesTests
    {
        static AccountStore Store(params string[] namesAndIds)
        {
            AccountStore s = new AccountStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rk-none-" + Guid.NewGuid().ToString("N")));
            for (int i = 0; i + 1 < namesAndIds.Length; i += 2)
            {
                RobloxAccount a = new RobloxAccount();
                a.Name = namesAndIds[i];
                a.UserId = namesAndIds[i + 1];
                a.Cookie = "c";
                s.Add(a);
            }
            return s;
        }

        // Runs "background" work straight away, so the tests stay in order.
        static ClientNamer Namer(Dictionary<string, string> roblox, List<string> asked)
        {
            ClientNamer n = new ClientNamer();
            n.Run = delegate(WaitCallback w) { w(null); };
            n.LookUp = delegate(string id)
            {
                asked.Add(id);
                string name;
                return roblox.TryGetValue(id, out name) ? name : null;
            };
            return n;
        }

        // ---------- kept with the account ----------

        public static void TestTheUserIdSurvivesARoundTrip()
        {
            RobloxAccount a = new RobloxAccount();
            a.Name = "AltOne";
            a.Cookie = "c";
            a.UserId = "1000000001";
            RobloxAccount b = AccountStore.Deserialize(AccountStore.Serialize(a));
            Assert.Equal("1000000001", b.UserId, "kept");
            Assert.Equal("AltOne", b.Name, "beside everything else");
        }

        public static void TestAnAccountSavedBeforeUserIdsHasNone()
        {
            RobloxAccount b = AccountStore.Deserialize("old\tc\t123\t\t-1\t-1\t0\t0\tnote\tC:\\p");
            Assert.Equal("old", b.Name, "still loads");
            Assert.True(string.IsNullOrEmpty(b.UserId), "with no user id until one is learned");
        }

        // What Roblox answers when a cookie asks who it is.
        public static void TestTheUserIdIsReadFromWhoAmI()
        {
            string json = "{\"id\":1000000001,\"name\":\"AltOne\",\"displayName\":\"Dark\"}";
            Assert.Equal("1000000001", RobloxAuth.UserIdFromJson(json), "the id");
            Assert.Equal("AltOne", RobloxAuth.UsernameFromJson(json), "and the name, as before");
            Assert.Equal(null, RobloxAuth.UserIdFromJson("{\"errors\":[]}"), "nothing when there is none");
        }

        // Roblox's public profile puts the owner's own description first. Text
        // in it is escaped, so it can't pose as the name.
        public static void TestAProfileDescriptionCannotPoseAsTheName()
        {
            string json = "{\"description\":\"my \\\"name\\\":\\\"fake\\\" \\\"id\\\":5\",\"created\":\"2006-03-08T17:17:52.9Z\","
                        + "\"isBanned\":false,\"id\":156,\"name\":\"builderman\",\"displayName\":\"builderman\"}";
            Assert.Equal("builderman", RobloxAuth.UsernameFromJson(json), "the real name");
            Assert.Equal("156", RobloxAuth.UserIdFromJson(json), "and the real id");
        }

        // ---------- naming ----------

        public static void TestASavedAccountIsKnownByItsUserIdAtOnce()
        {
            List<string> asked = new List<string>();
            ClientNamer n = Namer(new Dictionary<string, string>(), asked);
            RobloxAccount recorded;
            string name = n.NameFor("1000000001", Store("AltOne", "1000000001"), delegate { }, out recorded);
            Assert.Equal("AltOne", name, "named straight away");
            Assert.Equal(0, asked.Count, "without asking Roblox");
            Assert.Equal(null, recorded, "nothing new learned");
        }

        // An account saved before user ids were kept learns its id the first
        // time one of its clients joins.
        public static void TestASavedAccountWithoutAnIdLearnsIt()
        {
            List<string> asked = new List<string>();
            Dictionary<string, string> roblox = new Dictionary<string, string>();
            roblox["1000000001"] = "AltOne";
            ClientNamer n = Namer(roblox, asked);
            AccountStore store = Store("ALTONE", "");

            int told = 0;
            RobloxAccount recorded;
            string first = n.NameFor("1000000001", store, delegate { told++; }, out recorded);
            Assert.Equal(null, first, "not known yet - Roblox is asked first");
            Assert.Equal(1, told, "and the caller is told when it is");

            string then = n.NameFor("1000000001", store, delegate { }, out recorded);
            Assert.Equal("ALTONE", then, "the saved account, spelled as it was saved");
            Assert.Equal("1000000001", store.Find("ALTONE").UserId, "which now knows its id");
            Assert.True(recorded == store.Find("ALTONE"), "and the caller hears so, to save it");
            Assert.Equal(1, asked.Count, "Roblox was asked once");
        }

        // Not one of yours - named after its Roblox account all the same.
        public static void TestAnyoneElseIsNamedByTheirRobloxName()
        {
            List<string> asked = new List<string>();
            Dictionary<string, string> roblox = new Dictionary<string, string>();
            roblox["156"] = "builderman";
            ClientNamer n = Namer(roblox, asked);
            RobloxAccount recorded;
            n.NameFor("156", Store(), delegate { }, out recorded);
            Assert.Equal("builderman", n.NameFor("156", Store(), delegate { }, out recorded), "their name");
            Assert.Equal(null, recorded, "no saved account touched");
        }

        // Offline, or Roblox didn't answer: no name, and no promise of one.
        // The next join asks again.
        public static void TestAFailedLookupIsTriedAgainNextTime()
        {
            List<string> asked = new List<string>();
            ClientNamer n = Namer(new Dictionary<string, string>(), asked);
            int told = 0;
            RobloxAccount recorded;
            Assert.Equal(null, n.NameFor("42", Store(), delegate { told++; }, out recorded), "unknown");
            Assert.Equal(0, told, "nobody is told a name that never came");
            n.NameFor("42", Store(), delegate { }, out recorded);
            Assert.Equal(2, asked.Count, "asked again");
        }

        // Five clients joining at once don't ask Roblox five times about one id.
        public static void TestOneLookupAtATimeForAnId()
        {
            ClientNamer n = new ClientNamer();
            List<WaitCallback> queued = new List<WaitCallback>();
            n.Run = delegate(WaitCallback w) { queued.Add(w); };
            n.LookUp = delegate(string id) { return "someone"; };
            RobloxAccount recorded;
            n.NameFor("7", Store(), delegate { }, out recorded);
            n.NameFor("7", Store(), delegate { }, out recorded);
            Assert.Equal(1, queued.Count, "one lookup waiting");
            queued[0](null);
            Assert.Equal("someone", n.NameFor("7", Store(), delegate { }, out recorded), "answered for both");
        }
    }
}
