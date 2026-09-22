using System;
using System.Diagnostics;
using System.IO;

namespace RobloxKeeper
{
    // Everything a hop touches outside this app, behind one interface so the
    // order of it can be tested.
    interface IHopWorld
    {
        ServerPage Servers(string placeId, string cursor, out string error);
        string Ticket(string cookie, out string error);
        void Close(int pid);
        int Start(string launchUrl, out string error);
    }

    class HopRequest
    {
        public string AccountName;
        public string Cookie;              // never logged, never shown
        public string TrackerId;
        public string PlaceId;
        public int CurrentPid;             // 0 when the account has no client open
        public Func<string, bool> Avoid;   // servers not to go to
    }

    class HopResult
    {
        public int Pid;
        public string JobId;
        public string Problem;             // null when it worked
    }

    // Moves one account's client to another server of a game.
    //
    // The order is the point. Everything that can fail without harm - the
    // server list, the launch ticket - happens first, and only then is the
    // old client closed: a failed hop leaves the account where it was, never
    // with no client at all. The old client is closed before the new one
    // starts because two clients signed in as one account is exactly the
    // duplicate-login eviction (Roblox error 273).
    static class Hopper
    {
        const int PAGES = 3;

        public static HopResult Hop(HopRequest req, IHopWorld world, Random rng)
        {
            HopResult result = new HopResult();

            PublicServer pick = null;
            string cursor = null;
            for (int page = 0; page < PAGES && pick == null; page++)
            {
                string error;
                ServerPage p = world.Servers(req.PlaceId, cursor, out error);
                if (p == null) { result.Problem = error ?? "couldn't get the list of servers"; return result; }
                pick = ServerPicker.Pick(p.Servers, req.Avoid, rng);
                cursor = p.NextCursor;
                if (cursor == null) break;
            }
            if (pick == null)
            {
                result.Problem = "no server with room that hasn't been visited in the last half hour";
                return result;
            }

            string why;
            string ticket = world.Ticket(req.Cookie, out why);
            if (ticket == null) { result.Problem = why ?? "Roblox gave no launch ticket"; return result; }

            string url = RobloxAuth.BuildLaunchUrl(ticket, req.PlaceId, req.TrackerId, RobloxAuth.NowMs(), pick.Id);
            if (req.CurrentPid > 0) world.Close(req.CurrentPid);

            int pid = world.Start(url, out why);
            if (pid <= 0)
            {
                result.Problem = (req.CurrentPid > 0 ? "closed the old client, but couldn't start the new one: "
                                                     : "couldn't start the client: ") + (why ?? "no reason given");
                return result;
            }

            result.Pid = pid;
            result.JobId = pick.Id;
            return result;
        }
    }

    // The real world: Roblox's server list and ticket, and this PC's clients.
    class LiveHopWorld : IHopWorld
    {
        public ServerPage Servers(string placeId, string cursor, out string error)
        {
            return RobloxServers.Fetch(placeId, cursor, out error);
        }

        public string Ticket(string cookie, out string error)
        {
            return RobloxAuth.RequestTicket(cookie, out error);
        }

        // Asked to close as its own close button would, then made to if it
        // hasn't within eight seconds.
        public void Close(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(8000)) { p.Kill(); p.WaitForExit(5000); }
                }
            }
            catch { }   // already gone
        }

        public int Start(string launchUrl, out string error)
        {
            error = null;
            try
            {
                string version = RobloxInstall.NewestInstalledVersion();
                if (version == null) { error = "no installed Roblox client found"; return 0; }
                string exe = Path.Combine(RobloxInstall.VersionsRoot, version, "RobloxPlayerBeta.exe");
                ProcessStartInfo info = new ProcessStartInfo(exe, launchUrl);
                info.UseShellExecute = false;
                using (Process p = Process.Start(info))
                    return p == null ? 0 : p.Id;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return 0;
            }
        }
    }
}
