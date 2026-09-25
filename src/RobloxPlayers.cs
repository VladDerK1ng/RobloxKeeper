using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RobloxKeeper
{
    // Where a player is, as far as the account asking is allowed to know.
    class PlayerWhere
    {
        public const int UNKNOWN = -1, OFFLINE = 0, ONLINE = 1, IN_GAME = 2, IN_STUDIO = 3, INVISIBLE = 4;

        public int Type = UNKNOWN;
        public string PlaceId;
        // The server. Roblox only shows it to an account that may join it.
        public string GameId;
    }

    // Finding a player: a name to a user id, and where that user is.
    //
    // Both are what the website itself asks. The name lookup needs no
    // sign-in. Where someone is, is asked with the account that wants to
    // follow them, because Roblox answers each account according to the
    // player's own privacy settings - "who can join me".
    static class RobloxPlayers
    {
        const string LOOKUP_URL = "https://users.roblox.com/v1/usernames/users";
        const string PRESENCE_URL = "https://presence.roblox.com/v1/presence/users";

        // A Roblox username: letters, digits and underscores, 3 to 20 long.
        // Anything else can't be anyone, and isn't sent.
        static readonly Regex NameRx = new Regex(@"^[A-Za-z0-9_]{3,20}$");

        public static string UserIdFromLookup(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Match m = Regex.Match(json, @"""id""\s*:\s*(\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static PlayerWhere ParsePresence(string json)
        {
            PlayerWhere w = new PlayerWhere();
            if (string.IsNullOrEmpty(json)) return w;
            Match t = Regex.Match(json, @"""userPresenceType""\s*:\s*(\d+)");
            if (!t.Success) return w;
            w.Type = int.Parse(t.Groups[1].Value);
            Match p = Regex.Match(json, @"""placeId""\s*:\s*(\d+)");
            if (p.Success) w.PlaceId = p.Groups[1].Value;
            Match g = Regex.Match(json, @"""gameId""\s*:\s*""([0-9a-fA-F-]{36})""");
            if (g.Success) w.GameId = g.Groups[1].Value;
            return w;
        }

        // Null, with no error, when there is no such player.
        public static string UserIdOf(string name, out string error)
        {
            error = null;
            name = (name ?? "").Trim();
            if (!NameRx.IsMatch(name)) return null;
            string body = "{\"usernames\":[\"" + name + "\"],\"excludeBannedUsers\":true}";
            string json = Post(LOOKUP_URL, null, body, out error);
            return json == null ? null : UserIdFromLookup(json);
        }

        public static PlayerWhere Where(string userId, string cookie, out string error)
        {
            string json = Post(PRESENCE_URL, cookie, "{\"userIds\":[" + userId + "]}", out error);
            return ParsePresence(json);
        }

        // A POST as the website makes it. Signed in, Roblox wants a CSRF token
        // it only hands out by refusing the first try - the same dance a
        // launch ticket needs. The cookie never appears in an error.
        internal static string Post(string url, string cookie, string body, out string error)
        {
            error = null;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string csrf = null;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.Accept = "application/json";
                    req.UserAgent = "RobloxKeeper";
                    req.Timeout = 12000;
                    if (!string.IsNullOrEmpty(cookie))
                    {
                        req.CookieContainer = new CookieContainer();
                        req.CookieContainer.Add(new Cookie(".ROBLOSECURITY", cookie, "/", ".roblox.com"));
                    }
                    if (csrf != null) req.Headers["X-CSRF-TOKEN"] = csrf;
                    req.ContentLength = bytes.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                    using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                    using (StreamReader r = new StreamReader(res.GetResponseStream()))
                        return r.ReadToEnd();
                }
                catch (WebException ex)
                {
                    HttpWebResponse res = ex.Response as HttpWebResponse;
                    if (res == null) { error = "couldn't reach Roblox (" + ex.Message + ")"; return null; }
                    string token = res.Headers["x-csrf-token"];
                    if ((int)res.StatusCode == 403 && !string.IsNullOrEmpty(token) && csrf == null)
                    {
                        csrf = token;
                        continue;
                    }
                    if ((int)res.StatusCode == 401) error = "this account's saved session has expired - sign in again";
                    else if ((int)res.StatusCode == 429) error = "Roblox asked to slow down";
                    else error = "Roblox refused (HTTP " + (int)res.StatusCode + ")";
                    return null;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return null;
                }
            }
            error = "Roblox kept asking for a security token";
            return null;
        }
    }
}
