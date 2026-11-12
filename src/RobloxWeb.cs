using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace RobloxKeeper
{
    // A game, as the launcher's pages show one.
    class GameCard
    {
        public string UniverseId;       // what Roblox's lists go by
        public string PlaceId;          // what starts it
        public string Name;
        public long Playing;
        public long Up, Down;
        public string IconUrl;

        // How many of those who voted liked it, 0 to 100; -1 with no votes.
        public int LikePercent
        {
            get { return Up + Down <= 0 ? -1 : (int)Math.Round(100.0 * Up / (Up + Down)); }
        }

        // "12.4K playing · 91%", leaving out what isn't known.
        public string Line()
        {
            List<string> parts = new List<string>();
            if (Playing > 0) parts.Add(Short(Playing) + " playing");
            if (LikePercent >= 0) parts.Add(LikePercent + "%");
            return string.Join(" · ", parts.ToArray());
        }

        public static string Short(long n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (n >= 1000) return (n / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return n.ToString(CultureInfo.InvariantCulture);
        }
    }

    // One of Roblox's own lists of games: "Top Playing Now".
    class GameSort
    {
        public string Name;
        public readonly List<GameCard> Games = new List<GameCard>();
    }

    // A friend, and where they are if Roblox will say.
    class FriendCard
    {
        public string UserId, Name, DisplayName, HeadUrl;
        public int Presence;            // 0 offline, 1 on the website, 2 in a game, 3 in Studio
        public string Where;            // "Brookhaven", "Website" - as Roblox words it
        public string PlaceId, JobId, UniverseId;   // null unless their privacy settings allow it

        public bool Online { get { return Presence != 0; } }
        public bool Joinable { get { return Presence == 2 && !string.IsNullOrEmpty(PlaceId) && !string.IsNullOrEmpty(JobId); } }

        public string Line()
        {
            switch (Presence)
            {
                case 2: return string.IsNullOrEmpty(Where) ? "In a game" : Where;
                case 3: return "In Studio";
                case 1: return "On the website";
                default: return "Offline";
            }
        }
    }

    // What the launcher's pages ask of Roblox's website, and reading the
    // answers.
    //
    // These are the same addresses the website itself calls. Roblox calls them
    // legacy and says they can change without notice, so every reader here
    // gives back an empty list rather than failing, and every page that uses
    // one says "couldn't load" and carries on.
    //
    // A sign-in cookie is only ever sent to roblox.com - Get and Post refuse
    // anywhere else - and is never put in an address, a log or an error.
    static class RobloxWeb
    {
        public const string SORTS_URL = "https://apis.roblox.com/explore-api/v1/get-sorts?sessionId=";
        public const string SEARCH_URL = "https://apis.roblox.com/search-api/omni-search?pageType=all&sessionId=";
        public const string GAMES_URL = "https://games.roblox.com/v1/games?universeIds=";
        public const string VOTES_URL = "https://games.roblox.com/v1/games/votes?universeIds=";
        public const string ICONS_URL = "https://thumbnails.roblox.com/v1/games/icons?size=150x150&format=Png&isCircular=false&universeIds=";
        public const string HEADS_URL = "https://thumbnails.roblox.com/v1/users/avatar-headshot?size=48x48&format=Png&isCircular=true&userIds=";
        public const string PRESENCE_URL = "https://presence.roblox.com/v1/presence/users";

        // Roblox wants a session id on these two and takes any; this is no
        // one's, and says nothing about the PC it comes from.
        const string NO_SESSION = "00000000-0000-0000-0000-000000000000";

        public static string FavouritesUrl(string userId)
        {
            return "https://games.roblox.com/v2/users/" + Uri.EscapeDataString(userId) + "/favorite/games?limit=50&sortOrder=Desc";
        }

        public static string FriendsUrl(string userId)
        {
            return "https://friends.roblox.com/v1/users/" + Uri.EscapeDataString(userId) + "/friends";
        }

        public static string UniverseUrl(string placeId)
        {
            return "https://apis.roblox.com/universes/v1/places/" + Uri.EscapeDataString(placeId) + "/universe";
        }

        // ---------- reading the answers ----------

        static GameCard GameFrom(object g)
        {
            GameCard c = new GameCard();
            c.UniverseId = Json.Str(g, "universeId") ?? Json.Str(g, "id");
            c.PlaceId = Json.Str(g, "rootPlaceId") ?? Json.Str(Json.Get(g, "rootPlace"), "id");
            c.Name = Json.Str(g, "name");
            c.Playing = Json.Num(g, "playerCount");
            if (c.Playing == 0) c.Playing = Json.Num(g, "playing");
            c.Up = Json.Num(g, "totalUpVotes");
            c.Down = Json.Num(g, "totalDownVotes");
            return c;
        }

        static bool Usable(GameCard c)
        {
            return !string.IsNullOrEmpty(c.UniverseId) && !string.IsNullOrEmpty(c.Name);
        }

        // Roblox's lists of games. Its other rows - filters, and anything
        // paid to be there - are left out: this app shows no adverts.
        public static List<GameSort> ParseSorts(string json)
        {
            List<GameSort> sorts = new List<GameSort>();
            foreach (object s in Json.List(Json.Parse(json), "sorts"))
            {
                if (Json.Str(s, "contentType") != "Games") continue;
                GameSort sort = new GameSort();
                sort.Name = Json.Str(s, "sortDisplayName");
                foreach (object g in Json.List(s, "games"))
                {
                    if (Json.Flag(g, "isSponsored")) continue;
                    GameCard c = GameFrom(g);
                    if (Usable(c)) sort.Games.Add(c);
                }
                if (!string.IsNullOrEmpty(sort.Name) && sort.Games.Count > 0) sorts.Add(sort);
            }
            return sorts;
        }

        public static List<GameCard> ParseSearch(string json)
        {
            List<GameCard> games = new List<GameCard>();
            foreach (object group in Json.List(Json.Parse(json), "searchResults"))
            {
                if (Json.Str(group, "contentGroupType") != "Game") continue;
                foreach (object g in Json.List(group, "contents"))
                {
                    if (Json.Flag(g, "isSponsored")) continue;
                    GameCard c = GameFrom(g);
                    if (Usable(c) && !Has(games, c.UniverseId)) games.Add(c);
                }
            }
            return games;
        }

        // Details asked for by universe id, and favourites: both are a list
        // called data.
        public static List<GameCard> ParseGames(string json)
        {
            List<GameCard> games = new List<GameCard>();
            foreach (object g in Json.List(Json.Parse(json), "data"))
            {
                GameCard c = GameFrom(g);
                if (Usable(c)) games.Add(c);
            }
            return games;
        }

        static bool Has(List<GameCard> games, string universeId)
        {
            foreach (GameCard g in games) if (g.UniverseId == universeId) return true;
            return false;
        }

        // Likes and dislikes onto games that came without them.
        public static void AddVotes(IList<GameCard> games, string json)
        {
            foreach (object v in Json.List(Json.Parse(json), "data"))
            {
                string id = Json.Str(v, "id");
                foreach (GameCard g in games)
                    if (g.UniverseId == id) { g.Up = Json.Num(v, "upVotes"); g.Down = Json.Num(v, "downVotes"); }
            }
        }

        // A picture's address for each id. Only ones Roblox has finished
        // making, and only from its own picture servers.
        public static Dictionary<string, string> ParseImages(string json)
        {
            Dictionary<string, string> urls = new Dictionary<string, string>();
            foreach (object t in Json.List(Json.Parse(json), "data"))
            {
                string id = Json.Str(t, "targetId"), url = Json.Str(t, "imageUrl");
                if (id != null && Json.Str(t, "state") == "Completed" && Thumbs.IsRobloxPicture(url)) urls[id] = url;
            }
            return urls;
        }

        public static string ParseUniverse(string json)
        {
            return Json.Str(Json.Parse(json), "universeId");
        }

        public static List<FriendCard> ParseFriends(string json)
        {
            List<FriendCard> friends = new List<FriendCard>();
            foreach (object f in Json.List(Json.Parse(json), "data"))
            {
                FriendCard c = new FriendCard();
                c.UserId = Json.Str(f, "id");
                c.Name = Json.Str(f, "name");
                c.DisplayName = Json.Str(f, "displayName");
                if (!string.IsNullOrEmpty(c.UserId)) friends.Add(c);
            }
            return friends;
        }

        // Where each friend is, onto the friends it is about.
        public static void AddPresence(IList<FriendCard> friends, string json)
        {
            foreach (object p in Json.List(Json.Parse(json), "userPresences"))
            {
                string id = Json.Str(p, "userId");
                foreach (FriendCard f in friends)
                {
                    if (f.UserId != id) continue;
                    f.Presence = (int)Json.Num(p, "userPresenceType");
                    f.Where = Json.Str(p, "lastLocation");
                    // The place they are actually in - what following a
                    // player goes by - before the game's front door.
                    f.PlaceId = Json.Str(p, "placeId") ?? Json.Str(p, "rootPlaceId");
                    f.JobId = Json.Str(p, "gameId");
                    f.UniverseId = Json.Str(p, "universeId");
                }
            }
        }

        public static string PresenceBody(IList<FriendCard> friends)
        {
            List<string> ids = new List<string>();
            foreach (FriendCard f in friends)
            {
                long n;
                if (long.TryParse(f.UserId, out n)) ids.Add(n.ToString(CultureInfo.InvariantCulture));
            }
            return "{\"userIds\":[" + string.Join(",", ids.ToArray()) + "]}";
        }

        public static string Ids<T>(IList<T> items, Func<T, string> id, int max)
        {
            List<string> ids = new List<string>();
            foreach (T i in items)
            {
                string one = id(i);
                long n;
                if (long.TryParse(one, out n) && !ids.Contains(one)) ids.Add(one);
                if (ids.Count >= max) break;
            }
            return string.Join(",", ids.ToArray());
        }

        // Online first, in a game before on the website, then by name.
        public static void SortFriends(List<FriendCard> friends)
        {
            friends.Sort(delegate(FriendCard a, FriendCard b)
            {
                int ra = Rank(a), rb = Rank(b);
                if (ra != rb) return ra.CompareTo(rb);
                return string.Compare(a.DisplayName ?? a.Name, b.DisplayName ?? b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        static int Rank(FriendCard f)
        {
            return f.Presence == 2 ? 0 : f.Presence == 0 ? 3 : f.Presence == 3 ? 2 : 1;
        }

        // ---------- asking ----------

        // Only Roblox's own website is ever asked, with or without a sign-in.
        public static bool IsRoblox(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != Uri.UriSchemeHttps) return false;
            return u.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase)
                || u.Host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase);
        }

        public static string SortsUrl() { return SORTS_URL + NO_SESSION; }

        public static string SearchUrl(string words)
        {
            return SEARCH_URL + NO_SESSION + "&searchQuery=" + Uri.EscapeDataString(words ?? "");
        }

        // The answer's text, or null and a plain reason. cookie may be null.
        public static string Get(string url, string cookie, out string error)
        {
            return Send(url, null, cookie, null, out error);
        }

        public static string Post(string url, string body, string cookie, out string error)
        {
            return Send(url, body ?? "", cookie, null, out error);
        }

        static string Send(string url, string body, string cookie, string csrf, out string error)
        {
            error = null;
            if (!IsRoblox(url)) { error = "that isn't a Roblox address"; return null; }
            try
            {
                // .NET 4 offers only old TLS unless told otherwise, and Roblox
                // refuses it.
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = body == null ? "GET" : "POST";
                req.Accept = "application/json";
                req.UserAgent = "RobloxKeeper";
                req.Timeout = 15000;
                if (!string.IsNullOrEmpty(cookie))
                {
                    req.CookieContainer = new CookieContainer();
                    req.CookieContainer.Add(new Cookie(".ROBLOSECURITY", cookie, "/", ".roblox.com"));
                }
                if (csrf != null) req.Headers["X-CSRF-TOKEN"] = csrf;
                if (body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    req.ContentType = "application/json";
                    req.ContentLength = bytes.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch (WebException ex)
            {
                HttpWebResponse res = ex.Response as HttpWebResponse;
                // Signed in, Roblox answers a first POST with a token to send
                // back - its guard against one website posting as another.
                string token = res == null ? null : res.Headers["x-csrf-token"];
                if (res != null && (int)res.StatusCode == 403 && csrf == null && !string.IsNullOrEmpty(token) && body != null)
                    return Send(url, body, cookie, token, out error);

                if (res != null && (int)res.StatusCode == 429) error = "Roblox asked to slow down";
                else if (res != null && (int)res.StatusCode == 401) error = "Roblox wants that account to sign in again";
                else if (res != null) error = "Roblox said no (HTTP " + (int)res.StatusCode + ")";
                else error = "couldn't reach Roblox";
                return null;
            }
            catch (Exception)
            {
                error = "couldn't reach Roblox";
                return null;
            }
        }

        // ---------- the questions the pages ask ----------

        // What one row of a page shows: the games whose icons are asked for
        // straight away. The rest of a list gets its icons when it is opened.
        public const int FIRST_ROW = 6;

        static readonly object sortsGate = new object();
        static List<GameSort> sortsKept;
        static DateTime sortsAt;

        // Roblox's lists of games right now. No sign-in. Kept for a few
        // minutes, so Home and Games ask once between them.
        public static List<GameSort> Sorts(out string error)
        {
            error = null;
            lock (sortsGate)
                if (sortsKept != null && DateTime.UtcNow - sortsAt < TimeSpan.FromMinutes(5)) return sortsKept;

            string json = Get(SortsUrl(), null, out error);
            List<GameSort> sorts = json == null ? new List<GameSort>() : ParseSorts(json);
            // One question for the icons of every list's first row, not one a list.
            List<GameCard> first = new List<GameCard>();
            foreach (GameSort s in sorts)
                for (int i = 0; i < s.Games.Count && i < FIRST_ROW; i++) first.Add(s.Games[i]);
            AddIcons(first);
            if (sorts.Count > 0)
                lock (sortsGate) { sortsKept = sorts; sortsAt = DateTime.UtcNow; }
            return sorts;
        }

        public static List<GameCard> Search(string words, out string error)
        {
            string json = Get(SearchUrl(words), null, out error);
            List<GameCard> games = json == null ? new List<GameCard>() : ParseSearch(json);
            AddIcons(games);
            return games;
        }

        public static List<GameCard> Favourites(string userId, out string error)
        {
            string json = Get(FavouritesUrl(userId), null, out error);
            List<GameCard> games = json == null ? new List<GameCard>() : ParseGames(json);
            Fill(games);
            return games;
        }

        // Games by the place that starts them: what "played lately" is kept as.
        public static List<GameCard> ByPlaces(IList<string> placeIds, out string error)
        {
            error = null;
            List<string> universes = new List<string>();
            foreach (string place in placeIds)
            {
                string e;
                string json = Get(UniverseUrl(place), null, out e);
                string u = json == null ? null : ParseUniverse(json);
                if (u == null) { error = e ?? error; continue; }
                if (!universes.Contains(u)) universes.Add(u);
            }
            if (universes.Count == 0) return new List<GameCard>();

            string details = Get(GAMES_URL + string.Join(",", universes.ToArray()), null, out error);
            List<GameCard> games = details == null ? new List<GameCard>() : ParseGames(details);
            // In the order they were played, which is not the order Roblox answers in.
            games.Sort(delegate(GameCard a, GameCard b) { return universes.IndexOf(a.UniverseId).CompareTo(universes.IndexOf(b.UniverseId)); });
            Fill(games);
            return games;
        }

        // Icons, and likes where a list came without them.
        static void Fill(List<GameCard> games)
        {
            if (games.Count == 0) return;
            string e;
            string votes = Get(VOTES_URL + Ids(games, delegate(GameCard g) { return g.UniverseId; }, AT_A_TIME), null, out e);
            if (votes != null) AddVotes(games, votes);
            AddIcons(games);
        }

        // As many ids as Roblox takes in one question.
        const int AT_A_TIME = 100;

        // Icons for the games that have none yet. Asked a hundred at a time.
        public static void AddIcons(IList<GameCard> games)
        {
            List<GameCard> without = new List<GameCard>();
            foreach (GameCard g in games) if (string.IsNullOrEmpty(g.IconUrl)) without.Add(g);
            for (int from = 0; from < without.Count; from += AT_A_TIME)
            {
                List<GameCard> some = without.GetRange(from, Math.Min(AT_A_TIME, without.Count - from));
                string e;
                string json = Get(ICONS_URL + Ids(some, delegate(GameCard g) { return g.UniverseId; }, AT_A_TIME), null, out e);
                if (json == null) continue;
                Dictionary<string, string> urls = ParseImages(json);
                foreach (GameCard g in some)
                {
                    string url;
                    if (urls.TryGetValue(g.UniverseId, out url)) g.IconUrl = url;
                }
            }
        }

        // An account's friends and where they are. Needs that account's
        // sign-in: Roblox shows a friends list, and where a friend is, only to
        // someone allowed to see it.
        public static List<FriendCard> Friends(string userId, string cookie, out string error)
        {
            string json = Get(FriendsUrl(userId), cookie, out error);
            List<FriendCard> friends = json == null ? new List<FriendCard>() : ParseFriends(json);
            if (friends.Count == 0) return friends;

            string e;
            // Roblox takes a few dozen ids at a time.
            for (int from = 0; from < friends.Count; from += 50)
            {
                List<FriendCard> some = friends.GetRange(from, Math.Min(50, friends.Count - from));
                string presence = Post(PRESENCE_URL, PresenceBody(some), cookie, out e);
                if (presence != null) AddPresence(some, presence);
                string heads = Get(HEADS_URL + Ids(some, delegate(FriendCard f) { return f.UserId; }, 50), null, out e);
                if (heads == null) continue;
                Dictionary<string, string> urls = ParseImages(heads);
                foreach (FriendCard f in some)
                {
                    string url;
                    if (urls.TryGetValue(f.UserId, out url)) f.HeadUrl = url;
                }
            }
            SortFriends(friends);
            return friends;
        }
    }
}
