using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Where launched accounts go - asked for by the owner: "follow player",
    // "all accounts join servers with few players or etc."
    //
    // Everything that can fail without harm - finding the player, the server
    // list, the ticket - happens before anything is closed, the same order a
    // hunt's hop uses, so a failed move leaves an account where it was.
    static class AccountLaunchTests
    {
        const string PLACE = "920587237";
        const string OTHER_PLACE = "2753915549";

        class FakeWorld : ILaunchWorld
        {
            public readonly List<string> Did = new List<string>();
            public readonly List<PublicServer> Listed = new List<PublicServer>();
            public readonly Dictionary<string, string> Players = new Dictionary<string, string>();   // name -> id
            public readonly Dictionary<string, PlayerWhere> Presence = new Dictionary<string, PlayerWhere>();   // cookie -> where
            public int NextPid = 100;
            public bool NoTickets;
            public string Device;
            public string LastUrl;

            public string DeviceTracker() { return Device; }

            public ServerPage Servers(string placeId, string cursor, bool fullestFirst, out string error)
            {
                error = null;
                Did.Add("list " + placeId + (fullestFirst ? " fullest" : " emptiest"));
                ServerPage p = new ServerPage();
                p.Servers.AddRange(Listed);
                return p;
            }

            public string Ticket(string cookie, out string error)
            {
                error = NoTickets ? "this account's saved session has expired - sign in again" : null;
                if (NoTickets) return null;
                Did.Add("ticket " + cookie);
                return "T-" + cookie;
            }

            public void Close(int pid) { Did.Add("close " + pid); }

            public int Start(string launchUrl, out string error)
            {
                error = null;
                LastUrl = launchUrl;
                string job = launchUrl.Contains("gameId%3D") ? launchUrl.Substring(launchUrl.IndexOf("gameId%3D") + 9, 8) : "any";
                string place = launchUrl.Contains("placeId%3D") ? launchUrl.Substring(launchUrl.IndexOf("placeId%3D") + 10).Split('%')[0] : "?";
                Did.Add("start " + place + " " + job);
                return NextPid++;
            }

            public string UserIdOf(string name, out string error)
            {
                error = null;
                string id;
                return Players.TryGetValue(name.ToLowerInvariant(), out id) ? id : null;
            }

            public PlayerWhere Where(string userId, string cookie, out string error)
            {
                error = null;
                PlayerWhere w;
                if (Presence.TryGetValue(cookie, out w)) return w;
                return new PlayerWhere();
            }

            public void Wait(int ms) { Did.Add("wait " + ms); }

            public string Steps(string prefix)
            {
                List<string> s = new List<string>();
                foreach (string d in Did) if (d.StartsWith(prefix)) s.Add(d);
                return string.Join(" | ", s.ToArray());
            }
        }

        static PublicServer S(string id8, int playing, int max)
        {
            PublicServer s = new PublicServer();
            s.Id = id8 + "-0000-0000-0000-000000000000";
            s.Playing = playing;
            s.MaxPlayers = max;
            return s;
        }

        static LaunchSeat Seat(string name) { return Seat(name, PLACE, 0); }

        static LaunchSeat Seat(string name, string place, int runningPid)
        {
            LaunchSeat s = new LaunchSeat();
            s.Account = name;
            s.Cookie = "c-" + name;
            s.PlaceId = place;
            s.RunningPid = runningPid;
            return s;
        }

        static LaunchRequest Req(JoinWhere where, params LaunchSeat[] seats)
        {
            LaunchRequest r = new LaunchRequest();
            r.Where = where;
            r.Seats.AddRange(seats);
            return r;
        }

        static List<LaunchResult> Run(LaunchRequest r, FakeWorld w)
        {
            return AccountLauncher.Run(r, w, new Random(1), delegate(string s) { });
        }

        // ---------- anywhere ----------

        public static void TestAnyServerIsWhereverRobloxPutsThem()
        {
            FakeWorld w = new FakeWorld();
            List<LaunchResult> got = Run(Req(JoinWhere.Any, Seat("a"), Seat("b")), w);
            Assert.Equal("start " + PLACE + " any | start " + PLACE + " any", w.Steps("start"), "both, no server named");
            Assert.Equal(null, got[0].Problem, "a started");
            Assert.Equal(101, got[1].Pid, "b's client");
            Assert.Equal("", w.Steps("list"), "no server list needed");
        }

        // Several clients starting at once race each other.
        public static void TestClientsStartAFewSecondsApart()
        {
            FakeWorld w = new FakeWorld();
            Run(Req(JoinWhere.Any, Seat("a"), Seat("b"), Seat("c")), w);
            Assert.Equal("wait 3000 | wait 3000", w.Steps("wait"), "a gap between each, none after the last");
        }

        public static void TestAnAccountWithNoGameSaysSo()
        {
            FakeWorld w = new FakeWorld();
            List<LaunchResult> got = Run(Req(JoinWhere.Any, Seat("a", null, 0)), w);
            Assert.Contains("No game to launch a into", got[0].Problem, "said in plain words");
            Assert.Equal("", w.Steps("start"), "nothing started");
        }

        // ---------- emptiest and busiest ----------

        public static void TestTheEmptiestServersEachGetOneAccount()
        {
            FakeWorld w = new FakeWorld();
            w.Listed.Add(S("aaaaaaaa", 1, 8));
            w.Listed.Add(S("bbbbbbbb", 2, 8));
            w.Listed.Add(S("cccccccc", 7, 8));
            Run(Req(JoinWhere.Emptiest, Seat("a"), Seat("b")), w);
            string starts = w.Steps("start");
            Assert.Contains("aaaaaaaa", starts, "the emptiest");
            Assert.Contains("bbbbbbbb", starts, "and the next emptiest");
            Assert.Equal("list " + PLACE + " emptiest", w.Steps("list"), "the list asked for once, emptiest first");
        }

        public static void TestTheBusiestAreAskedForFullestFirst()
        {
            FakeWorld w = new FakeWorld();
            w.Listed.Add(S("aaaaaaaa", 1, 8));
            w.Listed.Add(S("cccccccc", 6, 8));
            Run(Req(JoinWhere.Busiest, Seat("a")), w);
            Assert.Contains("cccccccc", w.Steps("start"), "the busiest with room");
            Assert.Equal("list " + PLACE + " fullest", w.Steps("list"), "fullest first");
        }

        // ---------- together ----------

        public static void TestAllInOneServerNeedsRoomForAll()
        {
            FakeWorld w = new FakeWorld();
            w.Listed.Add(S("aaaaaaaa", 5, 8));     // room for 3 - not 3 and a spare
            w.Listed.Add(S("bbbbbbbb", 2, 8));     // room for 6
            LaunchRequest r = Req(JoinWhere.Any, Seat("a"), Seat("b"), Seat("c"));
            r.Together = true;
            Run(r, w);
            Assert.Equal("start " + PLACE + " bbbbbbbb | start " + PLACE + " bbbbbbbb | start " + PLACE + " bbbbbbbb",
                         w.Steps("start"), "all three into the one with room");
        }

        public static void TestNoServerWithRoomForAllSaysSoRatherThanSplittingThem()
        {
            FakeWorld w = new FakeWorld();
            w.Listed.Add(S("aaaaaaaa", 6, 8));
            LaunchRequest r = Req(JoinWhere.Emptiest, Seat("a"), Seat("b"), Seat("c"));
            r.Together = true;
            List<LaunchResult> got = Run(r, w);
            Assert.Contains("room for all 3", got[0].Problem, "why");
            Assert.Equal("", w.Steps("start"), "nobody started");
        }

        // ---------- a player's server ----------

        public static void TestJoinsThePlayersServer()
        {
            FakeWorld w = new FakeWorld();
            w.Players["mainaccount"] = "555";
            PlayerWhere there = new PlayerWhere();
            there.Type = PlayerWhere.IN_GAME; there.PlaceId = OTHER_PLACE; there.GameId = "dddddddd-0000-0000-0000-000000000000";
            w.Presence["c-a"] = there;
            w.Presence["c-b"] = there;
            LaunchRequest r = Req(JoinWhere.Player, Seat("a"), Seat("b"));
            r.Player = "MainAccount";
            Run(r, w);
            Assert.Equal("start " + OTHER_PLACE + " dddddddd | start " + OTHER_PLACE + " dddddddd",
                         w.Steps("start"), "into that server, in the game they're playing");
        }

        public static void TestAPlayerWhoCantBeFollowedSaysWhy()
        {
            FakeWorld w = new FakeWorld();
            w.Players["main"] = "555";
            PlayerWhere hidden = new PlayerWhere();
            hidden.Type = PlayerWhere.IN_GAME;          // in a game, but no server shown to this account
            w.Presence["c-a"] = hidden;
            PlayerWhere online = new PlayerWhere();
            online.Type = PlayerWhere.ONLINE;
            w.Presence["c-b"] = online;
            LaunchRequest r = Req(JoinWhere.Player, Seat("a"), Seat("b"));
            r.Player = "main";
            List<LaunchResult> got = Run(r, w);
            Assert.Contains("privacy settings don't let a join", got[0].Problem, "hidden from this account");
            Assert.Contains("isn't in a game right now", got[1].Problem, "not playing");
            Assert.Equal("", w.Steps("start"), "nobody started");

            LaunchRequest nobody = Req(JoinWhere.Player, Seat("a"));
            nobody.Player = "nosuchplayer";
            Assert.Contains("no Roblox player called nosuchplayer", Run(nobody, w)[0].Problem, "a name that isn't anyone");
        }

        // ---------- a server link ----------

        public static void TestAServerLinkSendsEveryoneThere()
        {
            string place, job;
            Assert.True(RobloxAuth.ServerFromUrl(
                "https://www.roblox.com/games/start?placeId=920587237&gameInstanceId=eeeeeeee-0000-0000-0000-000000000000",
                out place, out job), "the app's own Discord link");
            Assert.Equal(PLACE, place, "its game");
            Assert.Equal("eeeeeeee-0000-0000-0000-000000000000", job, "its server");
            string noPlace, noJob;
            Assert.False(RobloxAuth.ServerFromUrl("https://www.roblox.com/games/920587237/Adopt-Me", out noPlace, out noJob),
                         "a game link names no server");

            FakeWorld w = new FakeWorld();
            LaunchRequest r = Req(JoinWhere.Any, Seat("a", OTHER_PLACE, 0));
            r.ServerPlaceId = place;
            r.ServerId = job;
            Run(r, w);
            Assert.Equal("start " + PLACE + " eeeeeeee", w.Steps("start"), "that server");
        }

        // ---------- already playing ----------

        // Launching it again would sign its open client out (Roblox 273).
        public static void TestAnAccountAlreadyPlayingIsLeftWhereItIs()
        {
            FakeWorld w = new FakeWorld();
            List<LaunchResult> got = Run(Req(JoinWhere.Any, Seat("a", PLACE, 42)), w);
            Assert.Contains("already playing", got[0].Said, "said so");
            Assert.Equal("", w.Steps("start") + w.Steps("close"), "left alone");
        }

        // Sent somewhere in particular, it is moved: ticket first, then its
        // client closed, then the new one started.
        public static void TestAnAccountSentSomewhereIsMovedThereInASafeOrder()
        {
            FakeWorld w = new FakeWorld();
            w.Listed.Add(S("aaaaaaaa", 1, 8));
            Run(Req(JoinWhere.Emptiest, Seat("a", PLACE, 42)), w);
            Assert.Equal("ticket c-a | close 42 | start " + PLACE + " aaaaaaaa",
                         w.Steps("ticket") + " | " + w.Steps("close") + " | " + w.Steps("start"), "in that order");

            FakeWorld dead = new FakeWorld();
            dead.Listed.Add(S("aaaaaaaa", 1, 8));
            dead.NoTickets = true;
            Run(Req(JoinWhere.Emptiest, Seat("a", PLACE, 42)), dead);
            Assert.Equal("", dead.Steps("close"), "no ticket - its client is left open");
        }

        public static void TestPressingPlayAgainRestartsIt()
        {
            FakeWorld w = new FakeWorld();
            LaunchSeat s = Seat("a", PLACE, 42);
            s.Restart = true;
            Run(Req(JoinWhere.Any, s), w);
            Assert.Equal("close 42", w.Steps("close"), "closed");
            Assert.Equal("start " + PLACE + " any", w.Steps("start"), "and started again");
        }

        public static void TestAHuntingAccountIsNotLaunched()
        {
            FakeWorld w = new FakeWorld();
            LaunchSeat s = Seat("a");
            s.Hunting = true;
            List<LaunchResult> got = Run(Req(JoinWhere.Any, s), w);
            Assert.Contains("hunting", got[0].Problem, "why");
            Assert.Equal("", w.Steps("start"), "not started");
        }

        // ---------- which tracker the client is given ----------

        // The device's own tracker, as a website launch sends. Each account's
        // own tracker made Roblox 0.740 clients freeze for five seconds at a
        // time, all session long (measured 23 Sep 2026).
        public static void TestALaunchSendsTheDevicesTracker()
        {
            FakeWorld w = new FakeWorld();
            w.Device = "555000111";
            Run(Req(JoinWhere.Any, Seat("a"), Seat("b")), w);
            Assert.Contains("browsertrackerid:555000111", w.LastUrl, "the device's own, for every account");
        }

        // Roblox has never run here, so there is no device tracker yet. The
        // launch still carries one rather than none.
        public static void TestWithoutADeviceTrackerALaunchStillCarriesOne()
        {
            FakeWorld w = new FakeWorld();
            w.Device = null;
            Run(Req(JoinWhere.Any, Seat("a")), w);
            int at = w.LastUrl.IndexOf("+browsertrackerid:") + 18;
            Assert.True(at > 18 && at < w.LastUrl.Length && char.IsDigit(w.LastUrl[at]), "a tracker, not an empty one");
        }

    }
}
