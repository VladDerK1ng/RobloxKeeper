using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RobloxKeeper
{
    // Where launched accounts go.
    enum JoinWhere { Any, Emptiest, Busiest, Player }

    // One account to launch, with everything decided about it on the window's
    // thread - the worker that launches it touches nothing else.
    class LaunchSeat
    {
        public string Account;
        public string Cookie;           // never logged, never shown
        public string TrackerId;
        public string PlaceId;          // the game box's, or the account's own saved game
        public int RunningPid;          // its client, if one is open
        public bool Hunting;
        public bool Restart;            // Play pressed on it again, and the restart agreed to
    }

    class LaunchRequest
    {
        public JoinWhere Where;
        public bool Together;           // Any, Emptiest or Busiest: all in one server
        public string Player;           // Where == Player
        public string PlayerId;         // filled in once the player is found
        public string ServerPlaceId;    // a server link in the game box: everyone there
        public string ServerId;
        public readonly List<LaunchSeat> Seats = new List<LaunchSeat>();
    }

    class LaunchResult
    {
        public string Account;
        public int Pid;                 // the client started, 0 when none was
        public string Problem;          // why not, in plain words
        public string Said;             // anything else worth saying
        public string PlaceId, JobId;   // where it was sent
    }

    // Everything a launch touches outside the app.
    interface ILaunchWorld : IHopWorld
    {
        string UserIdOf(string name, out string error);
        PlayerWhere Where(string userId, string cookie, out string error);
        void Wait(int ms);
    }

    // Launches accounts where they were asked to go.
    //
    // Everything that can fail without harm - finding the player, the server
    // list, the ticket - comes before anything is closed, the order a hunt's
    // hop uses: a failed move leaves an account where it was.
    static class AccountLauncher
    {
        // Clients started at the same moment race each other over Roblox's
        // launcher, so they start this far apart.
        public const int GAP_MS = 3000;
        const int PAGES = 3;

        class Dest
        {
            public string Place, Job, Problem;
        }

        public static List<LaunchResult> Run(LaunchRequest r, ILaunchWorld world, Random rng, Action<string> progress)
        {
            List<LaunchSeat> seats = r.Seats;
            Dest[] dest = new Dest[seats.Count];
            for (int i = 0; i < seats.Count; i++) dest[i] = new Dest();

            // Hunting accounts first: a hunt moves its client itself.
            for (int i = 0; i < seats.Count; i++)
                if (seats[i].Hunting)
                    dest[i].Problem = seats[i].Account + " is hunting - stop the hunt first to move it.";

            if (!string.IsNullOrEmpty(r.ServerId))
                ToServerLink(r, seats, dest);
            else if (r.Where == JoinWhere.Player)
                ToPlayer(r, seats, dest, world);
            else
                ToGame(r, seats, dest, world, rng);

            List<LaunchResult> results = new List<LaunchResult>();
            List<int> toStart = new List<int>();
            for (int i = 0; i < seats.Count; i++)
            {
                LaunchResult res = new LaunchResult();
                res.Account = seats[i].Account;
                res.Problem = dest[i].Problem;
                res.PlaceId = dest[i].Place;
                res.JobId = dest[i].Job;
                results.Add(res);
                if (res.Problem != null) continue;

                // Launching it again would sign its open client out - Roblox's
                // 273 - so one already playing is only moved when it is being
                // sent somewhere in particular, or Play was pressed on it again.
                if (seats[i].RunningPid > 0 && dest[i].Job == null && !seats[i].Restart)
                {
                    res.Said = seats[i].Account + " is already playing - left where it is.";
                    continue;
                }
                toStart.Add(i);
            }

            for (int k = 0; k < toStart.Count; k++)
            {
                int i = toStart[k];
                LaunchSeat seat = seats[i];
                LaunchResult res = results[i];
                if (k > 0) world.Wait(GAP_MS);
                if (progress != null)
                    progress(toStart.Count == 1 ? "Starting " + seat.Account + "..." : "Starting " + (k + 1) + " of " + toStart.Count + "...");

                string why;
                string ticket = world.Ticket(seat.Cookie, out why);
                if (ticket == null)
                {
                    res.Problem = "couldn't launch " + seat.Account + ": " + (why ?? "Roblox gave no launch ticket");
                    continue;
                }

                string url = RobloxAuth.BuildLaunchUrl(ticket, dest[i].Place,
                    RobloxAuth.LaunchTracker(world.DeviceTracker(), seat.TrackerId), RobloxAuth.NowMs(), dest[i].Job);
                bool moving = seat.RunningPid > 0;
                if (moving) world.Close(seat.RunningPid);

                int pid = world.Start(url, out why);
                if (pid <= 0)
                {
                    res.Problem = (moving ? "closed " + seat.Account + "'s old client, but couldn't start the new one: "
                                          : "couldn't start " + seat.Account + ": ") + (why ?? "no reason given");
                    continue;
                }
                res.Pid = pid;
                res.Said = moving ? "Moved " + seat.Account + " - its old client closed first." : "Launched " + seat.Account + ".";
            }
            return results;
        }

        static void ToServerLink(LaunchRequest r, List<LaunchSeat> seats, Dest[] dest)
        {
            for (int i = 0; i < seats.Count; i++)
            {
                if (dest[i].Problem != null) continue;
                dest[i].Place = r.ServerPlaceId;
                dest[i].Job = r.ServerId;
            }
        }

        // Each account asks where the player is with its own sign-in, because
        // Roblox answers each account by the player's own privacy settings.
        static void ToPlayer(LaunchRequest r, List<LaunchSeat> seats, Dest[] dest, ILaunchWorld world)
        {
            string name = (r.Player ?? "").Trim();
            string error = null, userId = null;
            if (name.Length == 0) error = "Type the name of the player to join.";
            else
            {
                string lookupError;
                userId = world.UserIdOf(name, out lookupError);
                r.PlayerId = userId;
                if (userId == null)
                    error = lookupError != null ? "couldn't look up " + name + ": " + lookupError
                                                : "there's no Roblox player called " + name + ".";
            }

            for (int i = 0; i < seats.Count; i++)
            {
                if (dest[i].Problem != null) continue;
                if (error != null) { dest[i].Problem = error; continue; }

                string why;
                PlayerWhere w = world.Where(userId, seats[i].Cookie, out why);
                if (w.Type == PlayerWhere.IN_GAME && !string.IsNullOrEmpty(w.GameId) && !string.IsNullOrEmpty(w.PlaceId))
                {
                    dest[i].Place = w.PlaceId;
                    dest[i].Job = w.GameId;
                }
                else if (w.Type == PlayerWhere.IN_GAME)
                    dest[i].Problem = name + " is in a game, but their privacy settings don't let " + seats[i].Account + " join them.";
                else if (w.Type == PlayerWhere.IN_STUDIO)
                    dest[i].Problem = name + " is in Roblox Studio, not a game.";
                else if (w.Type == PlayerWhere.UNKNOWN)
                    dest[i].Problem = "couldn't find out where " + name + " is" + (why != null ? ": " + why : ".");
                else
                    dest[i].Problem = name + " isn't in a game right now.";
            }
        }

        static void ToGame(LaunchRequest r, List<LaunchSeat> seats, Dest[] dest, ILaunchWorld world, Random rng)
        {
            // Grouped by game: each account goes to its own saved game when
            // the game box is blank.
            Dictionary<string, List<int>> byPlace = new Dictionary<string, List<int>>();
            List<string> order = new List<string>();
            for (int i = 0; i < seats.Count; i++)
            {
                if (dest[i].Problem != null) continue;
                string place = seats[i].PlaceId;
                if (string.IsNullOrEmpty(place))
                {
                    dest[i].Problem = "No game to launch " + seats[i].Account + " into. Paste a game link, "
                                    + "set one with Edit, or use Browse to pick one in Roblox.";
                    continue;
                }
                dest[i].Place = place;
                if (r.Where == JoinWhere.Any && !r.Together) continue;       // wherever Roblox puts it
                if (!byPlace.ContainsKey(place)) { byPlace[place] = new List<int>(); order.Add(place); }
                byPlace[place].Add(i);
            }

            foreach (string place in order)
            {
                List<int> group = byPlace[place];
                string error;
                List<PublicServer> servers = Servers(world, place, r.Where == JoinWhere.Busiest, out error);
                if (servers == null)
                {
                    foreach (int i in group) dest[i].Problem = "couldn't get the list of servers: " + error;
                    continue;
                }

                if (r.Together)
                {
                    PublicServer one = RoomFor(servers, group.Count, r.Where, rng);
                    foreach (int i in group)
                    {
                        if (one != null) dest[i].Job = one.Id;
                        else dest[i].Problem = "no server of that game has room for all " + group.Count
                                             + " - try fewer accounts, or untick All in the same server.";
                    }
                    continue;
                }

                // Each to its own: the emptiest or busiest not already taken.
                List<string> taken = new List<string>();
                foreach (int i in group)
                {
                    PublicServer s = Best(servers, taken, r.Where == JoinWhere.Busiest);
                    if (s == null) { dest[i].Problem = "no server of that game with room is left for " + seats[i].Account + "."; continue; }
                    taken.Add(s.Id);
                    dest[i].Job = s.Id;
                }
            }
        }

        static List<PublicServer> Servers(ILaunchWorld world, string place, bool fullestFirst, out string error)
        {
            error = null;
            List<PublicServer> all = new List<PublicServer>();
            string cursor = null;
            for (int page = 0; page < PAGES; page++)
            {
                ServerPage p = world.Servers(place, cursor, fullestFirst, out error);
                if (p == null) return all.Count > 0 ? all : null;
                all.AddRange(p.Servers);
                cursor = p.NextCursor;
                if (cursor == null) break;
            }
            return all;
        }

        // Room for everyone and one spare place, since a place can be taken
        // by the time the last account arrives.
        static PublicServer RoomFor(List<PublicServer> servers, int count, JoinWhere where, Random rng)
        {
            List<PublicServer> fits = new List<PublicServer>();
            foreach (PublicServer s in servers)
                if (s != null && !string.IsNullOrEmpty(s.Id) && s.MaxPlayers - s.Playing >= count + 1) fits.Add(s);
            if (fits.Count == 0) return null;
            if (where == JoinWhere.Any) return fits[rng.Next(fits.Count)];
            fits.Sort(delegate(PublicServer a, PublicServer b)
            {
                return where == JoinWhere.Busiest ? b.Playing.CompareTo(a.Playing) : a.Playing.CompareTo(b.Playing);
            });
            return fits[0];
        }

        // Two free places preferred over the last one, as a hunt does.
        static PublicServer Best(List<PublicServer> servers, List<string> taken, bool busiest)
        {
            PublicServer best = null, lastSlot = null;
            foreach (PublicServer s in servers)
            {
                if (s == null || string.IsNullOrEmpty(s.Id) || !s.HasRoom || taken.Contains(s.Id)) continue;
                bool spare = s.MaxPlayers - s.Playing >= 2;
                if (spare && (best == null || (busiest ? s.Playing > best.Playing : s.Playing < best.Playing))) best = s;
                if (!spare && (lastSlot == null || (busiest ? s.Playing > lastSlot.Playing : s.Playing < lastSlot.Playing))) lastSlot = s;
            }
            return best ?? lastSlot;
        }
    }

    // The real world: Roblox, and this PC's clients.
    class LiveLaunchWorld : LiveHopWorld, ILaunchWorld
    {
        public string UserIdOf(string name, out string error) { return RobloxPlayers.UserIdOf(name, out error); }

        public PlayerWhere Where(string userId, string cookie, out string error)
        {
            return RobloxPlayers.Where(userId, cookie, out error);
        }

        public void Wait(int ms) { System.Threading.Thread.Sleep(ms); }
    }
}
