using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace RobloxKeeper
{
    // A private server link, in either form Roblox hands out.
    //
    // The current one is a share link - roblox.com/share?code=...&type=Server,
    // or the same thing wrapped by the mobile app - and has to be resolved
    // before anyone can join. The older one names the game and the link code
    // outright: games/<place>/...?privateServerLinkCode=<code>.
    class PrivateLink
    {
        public string ShareCode;       // the current form, still to be resolved
        public string PlaceId;         // the older form, ready to join
        public string LinkCode;

        public static PrivateLink Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string t = text.Trim();
            // A wrapped link carries the real one percent-encoded, sometimes twice.
            for (int i = 0; i < 2; i++)
            {
                string d = Uri.UnescapeDataString(t);
                if (d == t) break;
                t = d;
            }
            if (t.IndexOf("roblox", StringComparison.OrdinalIgnoreCase) < 0
                && t.IndexOf("ro.blox", StringComparison.OrdinalIgnoreCase) < 0) return null;

            Match old = Regex.Match(t, @"roblox\.com/games/(\d+)[^\s?#]*\?(?:[^\s#]*&)?privateServerLinkCode=([A-Za-z0-9_-]+)",
                                    RegexOptions.IgnoreCase);
            if (old.Success)
            {
                PrivateLink l = new PrivateLink();
                l.PlaceId = old.Groups[1].Value;
                l.LinkCode = old.Groups[2].Value;
                return l;
            }

            foreach (Match share in Regex.Matches(t, @"share(?:[-_]links)?\?([^\s#]+)", RegexOptions.IgnoreCase))
            {
                string code = null, type = null;
                foreach (string pair in share.Groups[1].Value.Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = pair.Substring(0, eq), value = pair.Substring(eq + 1);
                    if (code == null && key.Equals("code", StringComparison.OrdinalIgnoreCase)) code = value;
                    if (type == null && key.Equals("type", StringComparison.OrdinalIgnoreCase)) type = value;
                }
                if (code == null || !Regex.IsMatch(code, @"^[A-Za-z0-9_-]+$")) continue;
                if (!string.Equals(type, "Server", StringComparison.OrdinalIgnoreCase)) continue;
                PrivateLink l = new PrivateLink();
                l.ShareCode = code;
                return l;
            }
            return null;
        }
    }

    // What resolving a share link said.
    class ShareLinkAnswer
    {
        public string Status;          // Valid, Expired or Invalid
        public string UniverseId;
        public string PlaceId;
        public string LinkCode;
    }

    // Turning a share link into a game and a link code, exactly as roblox.com
    // does when one is opened: sharelinks/v1/resolve-link, signed in, gives the
    // server's universe and link code, and the universe's root place is the
    // game to join.
    static class PrivateServers
    {
        const string RESOLVE_URL = "https://apis.roblox.com/sharelinks/v1/resolve-link";
        const string GAMES_URL = "https://games.roblox.com/v1/games?universeIds=";

        // Two shapes. Measured, signed in, Roblox answers flat - status,
        // universeId, placeId, linkCode at the top. roblox.com's own code
        // reads them inside privateServerInviteData, so that is read too.
        // A 0 is Roblox's way of saying there is none.
        public static ShareLinkAnswer ParseResolve(string json)
        {
            ShareLinkAnswer a = new ShareLinkAnswer();
            if (string.IsNullOrEmpty(json)) return a;
            string part = json;
            int at = json.IndexOf("\"privateServerInviteData\"", StringComparison.Ordinal);
            if (at >= 0)
            {
                part = json.Substring(at);
                // Not a server link at all: nothing of its own to read.
                if (Regex.IsMatch(part, @"^""privateServerInviteData""\s*:\s*null")) return a;
                int end = part.IndexOf('}');
                if (end > 0) part = part.Substring(0, end);
            }
            Match s = Regex.Match(part, @"""status""\s*:\s*""([^""]+)""");
            if (s.Success) a.Status = s.Groups[1].Value;
            a.UniverseId = Id(part, "universeId");
            a.PlaceId = Id(part, "placeId");
            Match l = Regex.Match(part, @"""linkCode""\s*:\s*""([^""]+)""");
            if (l.Success) a.LinkCode = l.Groups[1].Value;
            return a;
        }

        static string Id(string json, string name)
        {
            Match m = Regex.Match(json, @"""" + name + @"""\s*:\s*(\d+)");
            return m.Success && m.Groups[1].Value.TrimStart('0').Length > 0 ? m.Groups[1].Value : null;
        }

        public static string RootPlaceFromGames(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Match m = Regex.Match(json, @"""rootPlaceId""\s*:\s*(\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Why a link can't be used, in words that say what to do about it.
        public static string Explain(string status)
        {
            if (string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase))
                return "that private server link has expired - ask its owner for a new one";
            if (string.Equals(status, "Invalid", StringComparison.OrdinalIgnoreCase))
                return "that private server link doesn't work any more - its owner may have made a new one";
            return "Roblox didn't accept that private server link" + (string.IsNullOrEmpty(status) ? "" : " (" + status + ")");
        }

        // Signed in as the account that will join: Roblox only resolves a
        // share link for someone signed in.
        public static bool Resolve(string code, string cookie, out string placeId, out string linkCode, out string error)
        {
            placeId = linkCode = null;
            string json = RobloxPlayers.Post(RESOLVE_URL, cookie,
                "{\"linkId\":\"" + code + "\",\"linkType\":\"Server\"}", out error);
            if (json == null) { error = "couldn't open the private server link: " + error; return false; }

            ShareLinkAnswer a = ParseResolve(json);
            if (!string.Equals(a.Status, "Valid", StringComparison.OrdinalIgnoreCase)) { error = Explain(a.Status); return false; }
            if (a.LinkCode == null || (a.PlaceId == null && a.UniverseId == null))
            {
                error = "Roblox said the link was fine, but not which server it opens";
                return false;
            }
            linkCode = a.LinkCode;

            // Named outright, as Roblox does now; otherwise the universe's
            // root place, as roblox.com's own code works it out.
            if (a.PlaceId != null)
            {
                placeId = a.PlaceId;
                return true;
            }

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(GAMES_URL + a.UniverseId);
                req.Accept = "application/json";
                req.UserAgent = "RobloxKeeper";
                req.Timeout = 12000;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(res.GetResponseStream()))
                    placeId = RootPlaceFromGames(r.ReadToEnd());
            }
            catch (Exception ex) { error = "couldn't find which game the private server is in (" + ex.Message + ")"; return false; }
            if (placeId == null) { error = "couldn't find which game the private server is in"; return false; }
            return true;
        }
    }
}
