using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Moving one account's client to another server, against a scripted
    // world: what gets done, in what order, and above all what does NOT get
    // done when something fails part-way. A client is never closed unless
    // its replacement can be started.
    static class HopperTests
    {
        class World : IHopWorld
        {
            public readonly List<ServerPage> Pages = new List<ServerPage>();
            public string ListError, TicketError, StartError;
            public readonly List<string> Did = new List<string>();
            public string StartedWith;

            public bool AskedFullestFirst;

            public ServerPage Servers(string placeId, string cursor, bool fullestFirst, out string error)
            {
                error = ListError;
                AskedFullestFirst = fullestFirst;
                int page = cursor == null ? 0 : int.Parse(cursor);
                Did.Add("list " + page);
                if (ListError != null) return null;
                return page < Pages.Count ? Pages[page] : new ServerPage();
            }

            public string Ticket(string cookie, out string error)
            {
                Did.Add("ticket");
                error = TicketError;
                return TicketError == null ? "TICKET" : null;
            }

            public void Close(int pid) { Did.Add("close " + pid); }

            public int Start(string url, out string error)
            {
                Did.Add("start");
                StartedWith = url;
                error = StartError;
                return StartError == null ? 777 : 0;
            }
        }

        static ServerPage Page(string next, params string[] ids)
        {
            ServerPage p = new ServerPage();
            p.NextCursor = next;
            foreach (string id in ids)
            {
                PublicServer s = new PublicServer();
                s.Id = id; s.Playing = 1; s.MaxPlayers = 7;
                p.Servers.Add(s);
            }
            return p;
        }

        static HopRequest Request(Func<string, bool> avoid)
        {
            HopRequest r = new HopRequest();
            r.AccountName = "VladDerKing";
            r.Cookie = "COOKIE";
            r.TrackerId = "12345";
            r.PlaceId = "107778070777162";
            r.CurrentPid = 100;
            r.Avoid = avoid ?? delegate(string id) { return false; };
            return r;
        }

        public static void TestAHopClosesTheOldClientAndStartsItInTheNewServer()
        {
            World w = new World();
            w.Pages.Add(Page(null, "c1c5a3b9-4938-4cd8-9418-ca1a217858ae"));
            HopResult r = Hopper.Hop(Request(null), w, new Random(1));

            Assert.Equal(null, r.Problem, "it worked");
            Assert.Equal(777, r.Pid, "the new client");
            Assert.Equal("c1c5a3b9-4938-4cd8-9418-ca1a217858ae", r.JobId, "and where it went");
            Assert.Equal("list 0|ticket|close 100|start", string.Join("|", w.Did.ToArray()), "in that order");
            Assert.Contains("RequestGameJob", Uri.UnescapeDataString(w.StartedWith), "into that server");
        }

        // No ticket means nothing to start it again with. The client stays.
        public static void TestWithoutATicketTheClientIsLeftAlone()
        {
            World w = new World();
            w.Pages.Add(Page(null, "c1c5a3b9-4938-4cd8-9418-ca1a217858ae"));
            w.TicketError = "this account's saved session has expired - sign in again";
            HopResult r = Hopper.Hop(Request(null), w, new Random(1));
            Assert.Contains("expired", r.Problem, "says why");
            Assert.False(w.Did.Contains("close 100"), "and never closed the client");
        }

        public static void TestWithoutAServerListTheClientIsLeftAlone()
        {
            World w = new World();
            w.ListError = "Roblox asked to slow down - too many server lists too quickly";
            HopResult r = Hopper.Hop(Request(null), w, new Random(1));
            Assert.Contains("slow down", r.Problem, "says why");
            Assert.False(w.Did.Contains("close 100"), "not closed");
        }

        // The first page may be all servers already visited; the next ones
        // are looked at, up to three.
        public static void TestLaterPagesAreLookedAtWhenTheFirstHasNothingNew()
        {
            World w = new World();
            w.Pages.Add(Page("1", "a7c31f39-0d44-4d46-b900-7d94e7eb0aa5"));
            w.Pages.Add(Page("2", "96447192-67fc-4bf9-99fa-8e144c3889e8"));
            w.Pages.Add(Page(null, "c4fb8e4c-29ed-4e23-af35-507967d97eba"));
            Func<string, bool> avoid = delegate(string id) { return id != "c4fb8e4c-29ed-4e23-af35-507967d97eba"; };
            HopResult r = Hopper.Hop(Request(avoid), w, new Random(1));
            Assert.Equal("c4fb8e4c-29ed-4e23-af35-507967d97eba", r.JobId, "found on the third page");
        }

        public static void TestNoNewServerAnywhereIsSaidPlainly()
        {
            World w = new World();
            for (int i = 0; i < 5; i++) w.Pages.Add(Page((i + 1).ToString(), "a7c31f39-0d44-4d46-b900-7d94e7eb0aa5"));
            HopResult r = Hopper.Hop(Request(delegate(string id) { return true; }), w, new Random(1));
            Assert.Contains("no server", r.Problem, "nothing new to go to");
            Assert.Equal(3, w.Did.FindAll(delegate(string d) { return d.StartsWith("list"); }).Count, "three pages at most");
            Assert.False(w.Did.Contains("close 100"), "not closed");
        }

        // A client not open yet - the first hop of a hunt - is just started.
        public static void TestWithNoClientOpenNothingIsClosed()
        {
            World w = new World();
            w.Pages.Add(Page(null, "c1c5a3b9-4938-4cd8-9418-ca1a217858ae"));
            HopRequest req = Request(null);
            req.CurrentPid = 0;
            HopResult r = Hopper.Hop(req, w, new Random(1));
            Assert.Equal(null, r.Problem, "started");
            Assert.Equal("list 0|ticket|start", string.Join("|", w.Did.ToArray()), "no close");
        }

        public static void TestAHuntThatWantsBusyServersAsksForThemFirst()
        {
            World w = new World();
            w.Pages.Add(Page(null, "c1c5a3b9-4938-4cd8-9418-ca1a217858ae"));
            HopRequest req = Request(null);
            req.Prefs = new ServerPrefs();
            req.Prefs.Size = ServerSize.Busiest;
            Hopper.Hop(req, w, new Random(1));
            Assert.True(w.AskedFullestFirst, "fullest first");
        }

        // By then the old client is gone, and saying so matters.
        public static void TestAFailedStartSaysTheOldClientWasClosed()
        {
            World w = new World();
            w.Pages.Add(Page(null, "c1c5a3b9-4938-4cd8-9418-ca1a217858ae"));
            w.StartError = "no installed Roblox client found";
            HopResult r = Hopper.Hop(Request(null), w, new Random(1));
            Assert.Contains("closed", r.Problem, "the old one is closed");
            Assert.Contains("no installed Roblox", r.Problem, "and why the new one didn't start");
        }
    }
}
