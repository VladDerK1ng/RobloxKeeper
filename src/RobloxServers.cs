using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace RobloxKeeper
{
    // One public server of a game.
    class PublicServer
    {
        public string Id;          // the job id - what a join names
        public int Playing, MaxPlayers, Ping;

        public bool HasRoom { get { return Playing < MaxPlayers; } }
    }

    class ServerPage
    {
        public readonly List<PublicServer> Servers = new List<PublicServer>();
        public string NextCursor;
    }

    // Which servers a hunt would rather be in. Something anyone can pick up
    // is best looked for where few people are; a bounty where many are.
    enum ServerSize { Any, Emptiest, Busiest }

    class ServerPrefs
    {
        public ServerSize Size = ServerSize.Any;
        public int MinPlayers;          // 0 = no limit
        public int MaxPlayers;          // 0 = no limit

        public ServerPrefs Copy() { return (ServerPrefs)MemberwiseClone(); }
    }

    // Which server to go to next.
    static class ServerPicker
    {
        // Emptiest or busiest picks among this many, so two accounts hunting
        // the same way still rarely land in the same server.
        const int SHORTLIST = 4;

        public static PublicServer Pick(IList<PublicServer> servers, Func<string, bool> avoid, Random rng)
        {
            return Pick(servers, avoid, rng, null);
        }

        // Among servers with room, within the player limits, and not to be
        // avoided - the one the client is in, ones visited lately, ones
        // another of our clients is in. Two free places are preferred over
        // the last one, which can be taken by the time the client arrives.
        // Then the emptiest or busiest few if asked, and random among those.
        public static PublicServer Pick(IList<PublicServer> servers, Func<string, bool> avoid, Random rng, ServerPrefs prefs)
        {
            ServerPrefs p = prefs ?? new ServerPrefs();
            List<PublicServer> spaces = new List<PublicServer>();
            List<PublicServer> lastSlot = new List<PublicServer>();
            foreach (PublicServer s in servers)
            {
                if (s == null || !s.HasRoom || string.IsNullOrEmpty(s.Id) || avoid(s.Id)) continue;
                if (p.MinPlayers > 0 && s.Playing < p.MinPlayers) continue;
                if (p.MaxPlayers > 0 && s.Playing > p.MaxPlayers) continue;
                if (s.MaxPlayers - s.Playing >= 2) spaces.Add(s);
                else lastSlot.Add(s);
            }
            List<PublicServer> from = spaces.Count > 0 ? spaces : lastSlot;
            if (from.Count == 0) return null;
            if (p.Size != ServerSize.Any) from = Shortlist(from, p.Size == ServerSize.Busiest, rng);
            return from[rng.Next(from.Count)];
        }

        // The emptiest or busiest few, ties broken at random.
        static List<PublicServer> Shortlist(List<PublicServer> from, bool busiest, Random rng)
        {
            List<KeyValuePair<int, PublicServer>> order = new List<KeyValuePair<int, PublicServer>>();
            foreach (PublicServer s in from) order.Add(new KeyValuePair<int, PublicServer>(rng.Next(), s));
            order.Sort(delegate(KeyValuePair<int, PublicServer> a, KeyValuePair<int, PublicServer> b)
            {
                int c = busiest ? b.Value.Playing.CompareTo(a.Value.Playing) : a.Value.Playing.CompareTo(b.Value.Playing);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            List<PublicServer> few = new List<PublicServer>();
            for (int i = 0; i < order.Count && i < SHORTLIST; i++) few.Add(order[i].Value);
            return few;
        }
    }

    // Servers visited lately, so a hunt does not go round in a small circle.
    class HopHistory
    {
        public static readonly TimeSpan Recent = TimeSpan.FromMinutes(30);

        readonly Dictionary<string, DateTime> visited =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public void Visited(string jobId, DateTime at)
        {
            if (!string.IsNullOrEmpty(jobId)) visited[jobId] = at;
        }

        public bool WasRecent(string jobId, DateTime now)
        {
            DateTime at;
            return !string.IsNullOrEmpty(jobId) && visited.TryGetValue(jobId, out at) && now - at < Recent;
        }

        public int Count { get { return visited.Count; } }

        public List<string> RecentIds(DateTime now)
        {
            List<string> ids = new List<string>();
            foreach (KeyValuePair<string, DateTime> v in visited)
                if (now - v.Value < Recent) ids.Add(v.Key);
            return ids;
        }
    }

    // A game's public servers, from the same public list the website's
    // Servers tab shows. No sign-in is needed or sent.
    static class RobloxServers
    {
        public static string PageUrl(string placeId, string cursor)
        {
            return PageUrl(placeId, cursor, false);
        }

        // Roblox sorts the list by how many are playing: ascending puts the
        // emptiest first - measured, the first page was all 1-of-7 servers -
        // and descending the fullest, so the busiest are on page one.
        public static string PageUrl(string placeId, string cursor, bool fullestFirst)
        {
            string url = "https://games.roblox.com/v1/games/" + placeId
                       + "/servers/Public?sortOrder=" + (fullestFirst ? "Desc" : "Asc") + "&excludeFullGames=true&limit=100";
            if (!string.IsNullOrEmpty(cursor)) url += "&cursor=" + Uri.EscapeDataString(cursor);
            return url;
        }

        static readonly Regex ServerId = new Regex("\"id\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"");
        static readonly Regex Cursor = new Regex("\"nextPageCursor\"\\s*:\\s*\"([^\"]+)\"");

        // Like every other answer from Roblox in this app, read with a few
        // tested patterns rather than a JSON library taken on for one list.
        //
        // A server is found by its id, which is always a quoted GUID; a
        // player inside it, when Roblox includes them, has a number for an id
        // and so is never taken for a server. Each server's numbers are read
        // from between its id and the next server's.
        public static ServerPage ParsePage(string json)
        {
            ServerPage page = new ServerPage();
            if (string.IsNullOrEmpty(json)) return page;

            Match c = Cursor.Match(json);
            if (c.Success) page.NextCursor = c.Groups[1].Value;

            MatchCollection ids = ServerId.Matches(json);
            for (int i = 0; i < ids.Count; i++)
            {
                int from = ids[i].Index;
                int to = i + 1 < ids.Count ? ids[i + 1].Index : json.Length;
                string part = json.Substring(from, to - from);

                PublicServer s = new PublicServer();
                s.Id = ids[i].Groups[1].Value;
                s.MaxPlayers = Number(part, "maxPlayers");
                s.Playing = Number(part, "playing");
                s.Ping = Number(part, "ping");
                page.Servers.Add(s);
            }
            return page;
        }

        static int Number(string part, string name)
        {
            Match m = Regex.Match(part, "\"" + name + "\"\\s*:\\s*(\\d+)");
            int v;
            return m.Success && int.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        // One page. Null and a plain reason on failure.
        public static ServerPage Fetch(string placeId, string cursor, out string error)
        {
            return Fetch(placeId, cursor, false, out error);
        }

        public static ServerPage Fetch(string placeId, string cursor, bool fullestFirst, out string error)
        {
            error = null;
            try
            {
                // .NET 4 offers only old TLS unless told otherwise, and Roblox
                // refuses it - measured: "Could not create SSL/TLS secure channel".
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(PageUrl(placeId, cursor, fullestFirst));
                req.Method = "GET";
                req.Accept = "application/json";
                req.UserAgent = "RobloxKeeper";
                req.Timeout = 15000;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(res.GetResponseStream()))
                    return ParsePage(r.ReadToEnd());
            }
            catch (WebException ex)
            {
                HttpWebResponse res = ex.Response as HttpWebResponse;
                if (res != null && (int)res.StatusCode == 429) error = "Roblox asked to slow down - too many server lists too quickly";
                else if (res != null) error = "Roblox refused the server list (HTTP " + (int)res.StatusCode + ")";
                else error = "couldn't reach Roblox (" + ex.Message + ")";
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }
    }
}
