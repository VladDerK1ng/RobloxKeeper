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

    // A game's public servers, from the same public list the website's
    // Servers tab shows. No sign-in is needed or sent.
    static class RobloxServers
    {
        public static string PageUrl(string placeId, string cursor)
        {
            string url = "https://games.roblox.com/v1/games/" + placeId
                       + "/servers/Public?sortOrder=Asc&excludeFullGames=true&limit=100";
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
            error = null;
            try
            {
                // .NET 4 offers only old TLS unless told otherwise, and Roblox
                // refuses it - measured: "Could not create SSL/TLS secure channel".
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(PageUrl(placeId, cursor));
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
