using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // What the launcher's pages are made of, kept apart from the windows so
    // it can be tested.
    static class Launcher
    {
        // Lists from Roblox are asked for again after this long, not on every
        // visit to a page.
        public static readonly TimeSpan FRESH_FOR = TimeSpan.FromMinutes(5);

        public static CardItem Card(GameCard g)
        {
            CardItem c = new CardItem();
            c.Title = g.Name;
            c.Line = g.Line();
            c.ImageUrl = g.IconUrl;
            c.Tag = g;
            return c;
        }

        public static CardSection Games(string title, IList<GameCard> games, int maxRows, string empty)
        {
            CardSection s = new CardSection();
            s.Title = title;
            s.MaxRows = maxRows;
            s.Empty = empty;
            if (games != null) foreach (GameCard g in games) s.Items.Add(Card(g));
            return s;
        }

        public static CardItem Row(FriendCard f)
        {
            CardItem c = new CardItem();
            c.Title = string.IsNullOrEmpty(f.DisplayName) || f.DisplayName == f.Name ? f.Name : f.DisplayName + "  (@" + f.Name + ")";
            c.Line = f.Line();
            c.ImageUrl = f.HeadUrl;
            c.Good = f.Online;
            c.Badge = f.Joinable ? "Join" : null;
            c.Tag = f;
            return c;
        }

        public static string Memory(long bytes)
        {
            if (bytes <= 0) return "";
            double gb = bytes / (1024.0 * 1024 * 1024);
            return gb >= 1 ? gb.ToString("0.0") + " GB" : (bytes / (1024 * 1024)) + " MB";
        }

        public static string Greeting(int hour)
        {
            return hour < 5 ? "Good night" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        }

        // A dark box to type in, with what it is for shown faintly until
        // something is typed.
        public static TextBox SearchBox(int x, int y, int w, Action<string> go)
        {
            TextBox t = WatchUi.Input(x, y, w);
            t.Font = Ui.F("Segoe UI", 10f);
            t.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                if (t.Text.Trim().Length > 0) go(t.Text.Trim());
            };
            return t;
        }
    }

    // Home: the Roblox windows that are open, then the games to go back to.
    class HomePage : ShellPage
    {
        readonly CardCanvas canvas = new CardCanvas();
        readonly Timer shots = new Timer();
        readonly Dictionary<int, Bitmap> pictures = new Dictionary<int, Bitmap>();
        ThemedPicker cmbAs;
        List<GameCard> recent, favourites, popular;
        string favouritesFor, problem;
        DateTime askedAt = DateTime.MinValue;
        bool asking, grabbing;

        public HomePage(IShell shell, Size size) : base(shell, size)
        {
            Head(Launcher.Greeting(DateTime.Now.Hour), "Your open Roblox windows and your games. Click a game to play it.");

            Label look = Ui.RowLabel("Find a game", Width - 500, 14, 26, 80, 9f, Theme.Muted);
            look.BackColor = Theme.Bg;
            Controls.Add(look);
            Controls.Add(Launcher.SearchBox(Width - 416, 15, 190, delegate(string words) { shell.Search(words); }));

            Label asWho = Ui.RowLabel("Play as", Width - 216, 14, 26, 50, 9f, Theme.Muted);
            asWho.BackColor = Theme.Bg;
            Controls.Add(asWho);
            cmbAs = Ui.DarkCombo(Width - 164, 14, 144);
            cmbAs.SelectedIndexChanged += delegate
            {
                if (cmbAs.SelectedIndex < 0 || cmbAs.SelectedIndex >= cmbAs.Items.Count) return;
                string name = cmbAs.Items[cmbAs.SelectedIndex];
                if (name == shell.PlayAs) return;
                shell.PlayAs = name;
                Ask(true);
            };
            Controls.Add(cmbAs);

            canvas.Bounds = Body;
            canvas.ItemClicked += Clicked;
            canvas.LinkClicked += delegate(CardSection s)
            {
                if (s.Title == START_HERE) { shell.TipsHidden = true; Rebuild(); }
                else shell.Go("games");
            };
            Controls.Add(canvas);

            // A picture of each window, kept fresh while this page is looked at.
            shots.Interval = 4000;
            shots.Tick += delegate { Grab(); };
        }

        public CardCanvas Canvas { get { return canvas; } }

        protected override void OnEnterPage()
        {
            FillAccounts();
            Rebuild();
            Grab();
            shots.Start();
            Ask(false);
        }

        protected override void OnLeavePage()
        {
            shots.Stop();
            // Nothing is looking at them: a picture of each window is a
            // megabyte this app has no business holding in the tray.
            foreach (Bitmap b in pictures.Values) b.Dispose();
            pictures.Clear();
        }

        void FillAccounts()
        {
            IList<RobloxAccount> accounts = shell.Accounts();
            cmbAs.Items.Clear();
            foreach (RobloxAccount a in accounts) cmbAs.Items.Add(a.Name);
            cmbAs.Enabled = accounts.Count > 0;
            if (accounts.Count == 0) return;
            int at = 0;
            for (int i = 0; i < accounts.Count; i++)
                if (string.Equals(accounts[i].Name, shell.PlayAs, StringComparison.OrdinalIgnoreCase)) at = i;
            if (string.IsNullOrEmpty(shell.PlayAs)) shell.PlayAs = accounts[at].Name;
            cmbAs.SelectedIndex = at;
        }

        string UserIdOfChosen()
        {
            foreach (RobloxAccount a in shell.Accounts())
                if (string.Equals(a.Name, shell.PlayAs, StringComparison.OrdinalIgnoreCase)) return a.UserId;
            return null;
        }

        // Asks Roblox for the lists, off the window's thread. Not again while
        // what it has is fresh, unless the account changed.
        void Ask(bool now)
        {
            if (asking || (!now && DateTime.Now - askedAt < Launcher.FRESH_FOR)) return;
            asking = true;
            string userId = UserIdOfChosen();
            IList<string> places = new List<string>(shell.RecentPlaces());

            InBackground(delegate
            {
                object[] got = new object[4];
                string e;
                got[0] = places.Count == 0 ? new List<GameCard>() : RobloxWeb.ByPlaces(FirstFew(places, 10), out e);
                got[1] = string.IsNullOrEmpty(userId) ? null : RobloxWeb.Favourites(userId, out e);
                List<GameSort> sorts = RobloxWeb.Sorts(out e);
                got[3] = sorts.Count == 0 ? e : null;
                foreach (GameSort s in sorts)
                    if (got[2] == null || s.Name.IndexOf("Playing", StringComparison.OrdinalIgnoreCase) >= 0) got[2] = s.Games;
                return got;
            },
            delegate(object[] got)
            {
                asking = false;
                if (got == null) { problem = "couldn't reach Roblox"; Rebuild(); return; }
                // Fresh only if it came: after a failure the next visit asks again.
                if (got[3] == null) askedAt = DateTime.Now;
                recent = (List<GameCard>)got[0];
                favourites = (List<GameCard>)got[1];
                favouritesFor = userId;
                popular = (List<GameCard>)got[2];
                problem = (string)got[3];
                Rebuild();
            });
        }

        static IList<string> FirstFew(IList<string> all, int n)
        {
            List<string> some = new List<string>();
            for (int i = 0; i < all.Count && i < n; i++) some.Add(all[i]);
            return some;
        }

        // Pictures of the open windows, taken the way watchers take them:
        // without touching the window, and not while it is minimized.
        void Grab()
        {
            if (grabbing || !IsShowing) return;
            // Only while this is the window being used: a picture of every
            // client every few seconds is not worth taking for a page that
            // is behind the game.
            if (pictures.Count > 0 && Form.ActiveForm != FindForm()) return;
            WatchedClient[] clients = shell.Clients();
            if (clients.Length == 0) { Rebuild(); return; }
            grabbing = true;
            InBackground(delegate
            {
                Dictionary<int, Bitmap> got = new Dictionary<int, Bitmap>();
                foreach (WatchedClient c in clients)
                {
                    IntPtr hwnd = WindowCapture.GameWindow(c.Pid);
                    if (hwnd == IntPtr.Zero || Native.IsIconic(hwnd)) continue;
                    Pixels shot = WindowCapture.Grab(hwnd);
                    if (shot == null || shot.IsEmpty) continue;
                    // Small: it is drawn at a card's size, and the whole
                    // window's pixels are not worth keeping for that.
                    using (Bitmap full = shot.ToBitmap())
                        got[c.Pid] = new Bitmap(full, new Size(CardLayout.CARD_W * 2, CardLayout.CARD_W * 2 * full.Height / Math.Max(1, full.Width)));
                }
                return got;
            },
            delegate(Dictionary<int, Bitmap> got)
            {
                grabbing = false;
                if (got == null) return;
                if (!IsShowing) { foreach (Bitmap b in got.Values) b.Dispose(); return; }
                List<Bitmap> old = new List<Bitmap>(pictures.Values);
                pictures.Clear();
                foreach (KeyValuePair<int, Bitmap> p in got) pictures[p.Key] = p.Value;
                Rebuild();
                foreach (Bitmap b in old) b.Dispose();
            });
        }

        const string START_HERE = "Start here";

        static CardItem Tip(string title, string says, string does, string key)
        {
            CardItem c = new CardItem();
            c.Title = title;
            c.Line = says;
            c.Badge = does;
            c.Tag = key;
            return c;
        }

        // What someone new most often wants, by name, until they put it away
        // or have done it. Nothing here is a setting: each row opens the
        // window that does the job.
        CardSection StartHere()
        {
            CardSection s = new CardSection();
            s.Title = START_HERE;
            s.Note = "what most people use this for";
            s.Link = "Hide this";
            s.Rows = true;
            if (shell.Accounts().Count == 0)
                s.Items.Add(Tip("Add your Roblox account", "Start any game from here with any of your accounts. Saved on this PC only.", "Add", "accounts"));
            s.Items.Add(Tip("Stop getting kicked for being idle", "Anti-AFK stops the 20-minute idle kick. Find it on the Keeper page.", "Open", "keeper"));
            if (shell.Macros == null || !shell.Macros.Clicker.Enabled)
                s.Items.Add(Tip("Click something over and over", "The autoclicker: pick a spot on your game, choose a speed, press F6.", "Set up", "clicker"));
            s.Items.Add(Tip("Record what you do and play it back", "Do it once, then play it back with a key. Quick Record, on the Automation page, works anywhere on your screen.", "Open", "macros"));
            return s;
        }

        public void Rebuild()
        {
            List<CardSection> sections = new List<CardSection>();
            if (!shell.TipsHidden) sections.Add(StartHere());

            CardSection running = new CardSection();
            running.Title = "Open now";
            WatchedClient[] clients = shell.Clients();
            running.Note = clients.Length == 0 ? null : clients.Length == 1 ? "1 Roblox window" : clients.Length + " Roblox windows";
            running.Empty = "No Roblox window is open. Click a game below to start one.";
            running.MaxRows = 2;
            foreach (WatchedClient c in clients)
            {
                CardItem item = new CardItem();
                item.Title = string.IsNullOrEmpty(c.AccountName) ? c.Label : c.AccountName;
                string memory = Launcher.Memory(shell.MemoryOf(c.Pid));
                item.Line = c.Label + (memory.Length > 0 ? " · " + memory : "");
                Bitmap shot;
                if (pictures.TryGetValue(c.Pid, out shot)) item.Image = shot;
                item.Badge = string.IsNullOrEmpty(c.PlaceId) ? "not in a game" : "playing";
                item.Good = !string.IsNullOrEmpty(c.PlaceId);
                item.Tag = c;
                running.Items.Add(item);
            }
            sections.Add(running);

            sections.Add(Launcher.Games("Continue", recent, 1,
                recent == null ? "Looking..." : "Games you play will show up here."));
            if (!string.IsNullOrEmpty(favouritesFor) || favourites != null)
                sections.Add(Launcher.Games("Favourites", favourites, 1,
                    favourites == null ? "Looking..." : shell.PlayAs + " has no favourites yet. Star a game on Roblox and it will show up here."));
            CardSection top = Launcher.Games("Popular right now", popular, 1,
                popular == null && problem == null ? "Looking..." : "Couldn't load Roblox's lists just now" + (problem == null ? "." : " - " + problem + "."));
            top.Link = "More games";
            sections.Add(top);

            canvas.Show(sections);
        }

        void Clicked(CardItem item, Point at)
        {
            WatchedClient client = item.Tag as WatchedClient;
            if (client != null) { shell.ShowClient(client.Pid); return; }
            GameCard game = item.Tag as GameCard;
            if (game != null) { shell.Play(game, canvas); return; }
            switch (item.Tag as string)
            {
                case "accounts": shell.OpenAccounts(); break;
                case "keeper": shell.Go(MainForm.PAGE_KEEPER); break;
                case "clicker": shell.OpenClicker(); Rebuild(); break;
                case "macros": shell.OpenMacros(); break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                shots.Dispose();
                foreach (Bitmap b in pictures.Values) b.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Games: Roblox's own lists, and a search.
    class GamesPage : ShellPage
    {
        readonly CardCanvas canvas = new CardCanvas();
        TextBox box;
        List<GameSort> sorts;
        List<GameCard> found;
        string foundFor, problem, expanded;
        DateTime askedAt = DateTime.MinValue;
        bool asking;

        public GamesPage(IShell shell, Size size) : base(shell, size)
        {
            Head("Games", "What people are playing right now. Type a name and press Enter to search. Click a game to play it.");
            box = Launcher.SearchBox(Width - 260, 15, 240, Search);
            Controls.Add(box);
            Label look = Ui.RowLabel("Search", Width - 316, 14, 26, 52, 9f, Theme.Muted);
            look.BackColor = Theme.Bg;
            Controls.Add(look);

            canvas.Bounds = Body;
            canvas.ItemClicked += delegate(CardItem item, Point at)
            {
                GameCard game = item.Tag as GameCard;
                if (game != null) shell.Play(game, canvas);
            };
            canvas.LinkClicked += delegate(CardSection s)
            {
                // "See all" on a list opens it out; on the search, clears it.
                if (s.Title.StartsWith("Results")) { found = null; foundFor = null; box.Text = ""; }
                else expanded = expanded == s.Title ? null : s.Title;
                Rebuild();
                canvas.ScrollTo(0, false);
                OpenedOut();
            };
            Controls.Add(canvas);
        }

        public CardCanvas Canvas { get { return canvas; } }

        protected override void OnEnterPage()
        {
            Rebuild();
            if (asking || DateTime.Now - askedAt < Launcher.FRESH_FOR) return;
            asking = true;
            InBackground(delegate
            {
                string e;
                List<GameSort> got = RobloxWeb.Sorts(out e);
                return new object[] { got, e };
            },
            delegate(object[] got)
            {
                asking = false;
                if (got != null) { sorts = (List<GameSort>)got[0]; problem = sorts.Count == 0 ? (string)got[1] ?? "nothing came back" : null; }
                else problem = "couldn't reach Roblox";
                // Fresh only if it came: after a failure the next visit asks again.
                if (problem == null) askedAt = DateTime.Now;
                Rebuild();
            });
        }

        // A list opened out shows games past its first row: their icons are
        // asked for now, not before anyone wanted to see them.
        void OpenedOut()
        {
            if (expanded == null || sorts == null) return;
            foreach (GameSort s in sorts)
            {
                if (s.Name != expanded) continue;
                List<GameCard> games = s.Games;
                InBackground(delegate { RobloxWeb.AddIcons(games); return true; },
                             delegate(bool done) { if (expanded != null) Rebuild(); });
            }
        }

        public void Search(string words)
        {
            box.Text = words;
            foundFor = words;
            found = null;
            Rebuild();
            canvas.ScrollTo(0, false);
            InBackground(delegate
            {
                string e;
                return RobloxWeb.Search(words, out e);
            },
            delegate(List<GameCard> got)
            {
                if (foundFor != words) return;      // another search since
                found = got ?? new List<GameCard>();
                Rebuild();
            });
        }

        public void Rebuild()
        {
            List<CardSection> sections = new List<CardSection>();
            if (foundFor != null)
            {
                CardSection results = Launcher.Games("Results for \"" + foundFor + "\"", found, 0,
                    found == null ? "Searching..." : "Nothing on Roblox is called that.");
                results.Link = "Clear search";
                sections.Add(results);
            }
            if (sorts == null || sorts.Count == 0)
            {
                CardSection none = new CardSection();
                none.Title = "Roblox's lists";
                none.Empty = sorts == null && problem == null ? "Looking..."
                           : "Couldn't load them just now" + (problem == null ? "." : " - " + problem + ".") + " Search still works.";
                sections.Add(none);
            }
            else
                foreach (GameSort s in sorts)
                {
                    if (expanded != null && expanded != s.Name) continue;
                    CardSection section = Launcher.Games(s.Name, s.Games, expanded == s.Name ? 0 : 1, null);
                    section.Link = expanded == s.Name ? "Back to every list" : "See all";
                    sections.Add(section);
                }
            canvas.Show(sections);
        }
    }

    // Friends: who is on, where, and joining them.
    class FriendsPage : ShellPage
    {
        readonly CardCanvas canvas = new CardCanvas();
        readonly Timer again = new Timer();
        ThemedPicker cmbAs;
        List<FriendCard> friends;
        string friendsFor, problem;
        bool asking;

        public FriendsPage(IShell shell, Size size) : base(shell, size)
        {
            Head("Friends", "Who is online and where. Click Join to go to a friend's server, if their privacy settings allow it.");
            Label whose = Ui.RowLabel("Friends of", Width - 240, 14, 26, 70, 9f, Theme.Muted);
            whose.BackColor = Theme.Bg;
            Controls.Add(whose);
            cmbAs = Ui.DarkCombo(Width - 164, 14, 144);
            cmbAs.SelectedIndexChanged += delegate
            {
                if (cmbAs.SelectedIndex < 0 || cmbAs.SelectedIndex >= cmbAs.Items.Count) return;
                string name = cmbAs.Items[cmbAs.SelectedIndex];
                if (name == shell.PlayAs) return;
                shell.PlayAs = name;
                friends = null;
                Rebuild();
                Ask();
            };
            Controls.Add(cmbAs);

            canvas.Bounds = Body;
            canvas.ItemClicked += delegate(CardItem item, Point at)
            {
                FriendCard f = item.Tag as FriendCard;
                if (f != null) shell.Join(f, canvas);
            };
            canvas.LinkClicked += delegate { shell.OpenAccounts(); };
            Controls.Add(canvas);

            // Only while the page is being looked at.
            again.Interval = 60000;
            again.Tick += delegate { Ask(); };
        }

        public CardCanvas Canvas { get { return canvas; } }

        protected override void OnEnterPage()
        {
            IList<RobloxAccount> accounts = shell.Accounts();
            cmbAs.Items.Clear();
            foreach (RobloxAccount a in accounts) cmbAs.Items.Add(a.Name);
            cmbAs.Enabled = accounts.Count > 0;
            for (int i = 0; i < accounts.Count; i++)
                if (string.Equals(accounts[i].Name, shell.PlayAs, StringComparison.OrdinalIgnoreCase)) cmbAs.SelectedIndex = i;
            if (accounts.Count > 0 && cmbAs.SelectedIndex < 0) { shell.PlayAs = accounts[0].Name; cmbAs.SelectedIndex = 0; }

            Rebuild();
            Ask();
            again.Start();
        }

        protected override void OnLeavePage() { again.Stop(); }

        RobloxAccount Chosen()
        {
            foreach (RobloxAccount a in shell.Accounts())
                if (string.Equals(a.Name, shell.PlayAs, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        void Ask()
        {
            RobloxAccount a = Chosen();
            if (asking || a == null || !a.IsUsable || string.IsNullOrEmpty(a.UserId)) return;
            asking = true;
            string name = a.Name, userId = a.UserId, cookie = a.Cookie;
            InBackground(delegate
            {
                string e;
                List<FriendCard> got = RobloxWeb.Friends(userId, cookie, out e);
                return new object[] { got, e };
            },
            delegate(object[] got)
            {
                asking = false;
                if (name != shell.PlayAs) return;       // another account chosen since
                friendsFor = name;
                friends = got == null ? new List<FriendCard>() : (List<FriendCard>)got[0];
                problem = got == null ? "couldn't reach Roblox" : (string)got[1];
                Rebuild();
            });
        }

        public void Rebuild()
        {
            List<CardSection> sections = new List<CardSection>();
            RobloxAccount a = Chosen();
            CardSection online = new CardSection(), offline = new CardSection();
            online.Rows = offline.Rows = true;
            online.Title = "Online";
            offline.Title = "Offline";

            if (a == null)
            {
                online.Title = "Friends";
                online.Empty = "Add a Roblox account to see its friends. It's saved on this PC only.";
                online.Link = "Add an account";
                sections.Add(online);
            }
            else if (string.IsNullOrEmpty(a.UserId) || !a.IsUsable)
            {
                online.Title = "Friends";
                online.Empty = a.Name + " needs to sign in again to show its friends.";
                online.Link = "Open Accounts";
                sections.Add(online);
            }
            else if (friends == null || friendsFor != a.Name)
            {
                online.Empty = "Looking...";
                sections.Add(online);
            }
            else
            {
                foreach (FriendCard f in friends) (f.Online ? online : offline).Items.Add(Launcher.Row(f));
                online.Note = online.Items.Count == 0 ? null : online.Items.Count.ToString();
                online.Empty = problem != null && friends.Count == 0
                    ? "Couldn't load " + a.Name + "'s friends - " + problem + "."
                    : "Nobody is online right now.";
                sections.Add(online);
                if (offline.Items.Count > 0) sections.Add(offline);
            }
            canvas.Show(sections);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) again.Dispose();
            base.Dispose(disposing);
        }
    }
}
