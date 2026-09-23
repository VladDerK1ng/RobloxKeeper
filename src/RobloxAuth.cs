using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RobloxKeeper
{
    // Turning a saved account into a running client, the same way the website
    // does it.
    //
    // Pressing Play on roblox.com exchanges your session for a single-use
    // authentication ticket and hands it to RobloxPlayerBeta as a
    // roblox-player:// URL. That is exactly what happens here, with the
    // account's own stored session instead of the browser's. Nothing is
    // bypassed or forged - the account authenticates itself to Roblox, and
    // Roblox decides whether to honour it.
    static class RobloxAuth
    {
        const string AUTH_TICKET_URL = "https://auth.roblox.com/v1/authentication-ticket/";
        const string REFERER = "https://www.roblox.com/";
        const string PLACE_LAUNCHER =
            "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={0}&placeId={1}&isPlayTogetherGame=false";
        // One particular server - what the website's Join button on a server
        // in the Servers tab asks for.
        const string PLACE_LAUNCHER_JOB =
            "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGameJob&browserTrackerId={0}&placeId={1}&gameId={2}&isPlayTogetherGame=false";

        // A place id out of whatever the user pasted: a full game link, a link
        // with tracking parameters on it, or just the number.
        public static string PlaceIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            url = url.Trim();

            if (Regex.IsMatch(url, @"^\d+$")) return url;

            Match m = Regex.Match(url, @"roblox\.com/games/(\d+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        // A link to one particular server - what the app's own Discord posts
        // hold: games/start?placeId=...&gameInstanceId=... A plain game link
        // names no server and is not one.
        public static bool ServerFromUrl(string url, out string placeId, out string jobId)
        {
            placeId = jobId = null;
            if (string.IsNullOrEmpty(url)) return false;
            Match p = Regex.Match(url, @"[?&]placeId=(\d+)", RegexOptions.IgnoreCase);
            Match j = Regex.Match(url, @"[?&]gameInstanceId=([0-9a-fA-F-]{36})", RegexOptions.IgnoreCase);
            if (!p.Success || !j.Success) return false;
            placeId = p.Groups[1].Value;
            jobId = j.Groups[1].Value;
            return true;
        }

        // The URL RobloxPlayerBeta is started with.
        //
        // placelauncherurl is a URL nested inside this one, so it has to be
        // percent-encoded: left raw, its own ? and & terminate the outer URL
        // and Roblox receives a truncated request.
        public static string BuildLaunchUrl(string ticket, string placeId,
                                            string browserTrackerId, long launchTimeMs)
        {
            return BuildLaunchUrl(ticket, placeId, browserTrackerId, launchTimeMs, null);
        }

        // With a job id, into that server; without, wherever Roblox puts it.
        public static string BuildLaunchUrl(string ticket, string placeId,
                                            string browserTrackerId, long launchTimeMs, string jobId)
        {
            if (string.IsNullOrEmpty(ticket)) return null;
            if (string.IsNullOrEmpty(placeId)) return null;

            // An empty tracker id is the shared-device-identity problem that
            // gets accounts evicted as duplicate logins, so never send one.
            if (string.IsNullOrEmpty(browserTrackerId))
                browserTrackerId = AccountStore.NewBrowserTrackerId();

            string launcher = string.IsNullOrEmpty(jobId)
                ? string.Format(PLACE_LAUNCHER, browserTrackerId, placeId)
                : string.Format(PLACE_LAUNCHER_JOB, browserTrackerId, placeId, Uri.EscapeDataString(jobId));

            StringBuilder sb = new StringBuilder();
            sb.Append("roblox-player:1");
            sb.Append("+launchmode:play");
            sb.Append("+gameinfo:").Append(ticket);
            sb.Append("+launchtime:").Append(launchTimeMs);
            sb.Append("+placelauncherurl:").Append(Uri.EscapeDataString(launcher));
            sb.Append("+browsertrackerid:").Append(browserTrackerId);
            sb.Append("+robloxLocale:en_us+gameLocale:en_us");
            return sb.ToString();
        }

        // Who a cookie belongs to. Roblox answers this with a small JSON
        // body; pulling one field out of it directly beats taking on a JSON
        // parser for a single string, provided it is tested against the shapes
        // Roblox actually returns.
        public static string UsernameFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Match m = Regex.Match(json, @"""name""\s*:\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        // The user id in the same answer - what a client's log names.
        public static string UserIdFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Match m = Regex.Match(json, @"""id""\s*:\s*(\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Asks Roblox which account a saved session belongs to, so the account
        // names itself. Returns null if the session has expired.
        public static string GetUsername(string cookie)
        {
            string userId;
            return WhoIs(cookie, out userId);
        }

        // The name, and the user id beside it.
        public static string WhoIs(string cookie, out string userId)
        {
            userId = null;
            if (string.IsNullOrEmpty(cookie)) return null;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(
                    "https://users.roblox.com/v1/users/authenticated");
                req.Method = "GET";
                req.Accept = "application/json";
                req.UserAgent = "Roblox/WinInet";
                req.Timeout = 12000;
                req.CookieContainer = new CookieContainer();
                req.CookieContainer.Add(new Cookie(".ROBLOSECURITY", cookie, "/", ".roblox.com"));

                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(res.GetResponseStream()))
                {
                    string json = r.ReadToEnd();
                    userId = UserIdFromJson(json);
                    return UsernameFromJson(json);
                }
            }
            catch { return null; }   // expired or offline; the caller names it manually
        }

        // Anyone's name from their user id, from Roblox's public profile. No
        // sign-in is sent. Null when offline or there is no such user.
        public static string NameOfUser(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(
                    "https://users.roblox.com/v1/users/" + Uri.EscapeDataString(userId));
                req.Method = "GET";
                req.Accept = "application/json";
                req.UserAgent = "RobloxKeeper";
                req.Timeout = 12000;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(res.GetResponseStream()))
                    return UsernameFromJson(r.ReadToEnd());
            }
            catch { return null; }
        }

        // Is this navigation the browser trying to start a Roblox client?
        //
        // Pressing Play on the website navigates to a roblox-player:// URL
        // carrying a launch ticket. Inside the account manager's browser that
        // has to be caught rather than handed to Windows: the system handler
        // would start a client against the shared cookie jar instead of the
        // account whose browser profile is being used.
        //
        // Matched by scheme only, and only Roblox's own two. Anything else a
        // page tries to open is somebody else's protocol and none of our
        // business.
        public static bool IsLaunchUrl(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return false;
            return uri.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase);
        }

        public static long NowMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        // Exchanges an account's cookie for a launch ticket.
        //
        // Roblox requires a CSRF token on this endpoint and only tells you what
        // it is by rejecting the first attempt with a 403 that carries the
        // token in a header - so one rejection is expected, not a failure.
        //
        // Returns null and an explanation on failure. The cookie never appears
        // in that explanation.
        public static string RequestTicket(string cookie, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(cookie)) { error = "no saved session for this account"; return null; }

            // Worked before only because the update check or a Discord post had
            // usually switched this on first. Hopping asks for tickets on its
            // own, so it is switched on here.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            string csrf = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(AUTH_TICKET_URL);
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.Accept = "application/json";
                    req.Referer = REFERER;
                    req.UserAgent = "Roblox/WinInet";
                    req.ContentLength = 0;
                    req.Timeout = 15000;
                    req.CookieContainer = new CookieContainer();
                    req.CookieContainer.Add(new Cookie(".ROBLOSECURITY", cookie, "/", ".roblox.com"));
                    if (csrf != null) req.Headers["X-CSRF-TOKEN"] = csrf;

                    using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                    {
                        string ticket = res.Headers["rbx-authentication-ticket"];
                        if (!string.IsNullOrEmpty(ticket)) return ticket;
                        error = "Roblox accepted the sign-in but returned no launch ticket";
                        return null;
                    }
                }
                catch (WebException ex)
                {
                    HttpWebResponse res = ex.Response as HttpWebResponse;
                    if (res == null) { error = "could not reach Roblox (" + ex.Message + ")"; return null; }

                    // The expected first response: 403 carrying the token.
                    string token = res.Headers["x-csrf-token"];
                    if ((int)res.StatusCode == 403 && !string.IsNullOrEmpty(token) && csrf == null)
                    {
                        csrf = token;
                        continue;
                    }

                    if ((int)res.StatusCode == 401 || (int)res.StatusCode == 403)
                        error = "this account's saved session has expired - sign in again";
                    else
                        error = "Roblox refused the request (HTTP " + (int)res.StatusCode + ")";
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
