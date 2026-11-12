using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The launcher shell, wired into the main window: a rail of pages down
    // the left, and what the pages ask of the window.
    //
    // The window as it was - anti-AFK, clients, performance, multi-instance,
    // the activity list - is one of the pages, moved whole and unchanged. Its
    // layout numbers are untouched: everything it placed on the form is
    // placed on a panel the same size instead.
    partial class MainForm : IShell
    {
        public const string PAGE_HOME = "home", PAGE_GAMES = "games", PAGE_FRIENDS = "friends", PAGE_KEEPER = "keeper",
                            PAGE_AUTO = "automation", PAGE_LOOKS = "looks", PAGE_SHOTS = "shots";

        Rail rail;
        Panel keeperPage;
        readonly Dictionary<string, ShellPage> pages = new Dictionary<string, ShellPage>();
        string currentPage = PAGE_KEEPER;
        readonly Recents recents = new Recents(Recents.DefaultPath);
        bool recentsLoaded;
        string playAs;

        internal Rail ShellRail { get { return rail; } }
        internal string CurrentPage { get { return currentPage; } }
        internal ShellPage PageNow { get { ShellPage p; return pages.TryGetValue(currentPage, out p) ? p : null; } }

        Size PageSize { get { return new Size(ClientSize.Width - Rail.WIDTH, ClientSize.Height - TITLEBAR_H); } }

        // After BuildUi: everything it put on the form moves onto the
        // keeper page, the title bar moves over, and the rail goes in.
        void BuildShell()
        {
            Size old = ClientSize;
            bool scrolls = AutoScroll;
            AutoScroll = false;

            keeperPage = new Panel();
            keeperPage.BackColor = Theme.Bg;
            keeperPage.Location = new Point(Rail.WIDTH, TITLEBAR_H);
            keeperPage.Size = new Size(old.Width, old.Height - TITLEBAR_H);

            List<Control> moved = new List<Control>();
            foreach (Control c in Controls) if (!ReferenceEquals(c, titleBar)) moved.Add(c);
            foreach (Control c in moved)
            {
                Controls.Remove(c);
                c.Top -= TITLEBAR_H;
                keeperPage.Controls.Add(c);
            }
            // A screen too short for the cards scrolled the whole window;
            // now it is this page that scrolls, under a title bar that stays.
            if (scrolls)
            {
                keeperPage.AutoScroll = true;
                keeperPage.AutoScrollMinSize = new Size(BASE_WIDTH, FULL_HEIGHT - TITLEBAR_H);
            }

            ClientSize = new Size(old.Width + Rail.WIDTH, old.Height);
            titleBar.Left = Rail.WIDTH;
            Controls.Add(keeperPage);

            rail = new Rail();
            rail.Location = new Point(0, 0);
            rail.Size = new Size(Rail.WIDTH, ClientSize.Height);
            rail.Add(new RailItem(PAGE_HOME, "Home", '', false));
            rail.Add(new RailItem(PAGE_GAMES, "Games", '', false));
            rail.Add(new RailItem(PAGE_FRIENDS, "Friends", '', false));
            rail.Add(new RailItem(PAGE_AUTO, "Automation - Macros, Autoclicker, Watchers", '', false));
            rail.Add(new RailItem(PAGE_LOOKS, "Looks - graphics and mods", '', false));
            rail.Add(new RailItem(PAGE_SHOTS, "Screenshots", '', false));
            rail.Add(new RailItem(PAGE_KEEPER, "Keeper - Anti-AFK, Roblox windows, performance, activity", '', true));
            rail.Selected = currentPage;
            rail.Chosen += ShowPage;
            // The strip above the first icon drags the window, as the title bar does.
            rail.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Y < 48) StartWindowDrag(s, e); };
            Controls.Add(rail);

            AddScriptsToTray();

            // A page only does anything while the window can be seen.
            VisibleChanged += delegate
            {
                ShellPage p = PageNow;
                if (Visible) { if (p != null) p.ComeIn(); }
                else
                {
                    if (p != null) p.GoOut();
                    Thumbs.ForgetMemory();
                }
            };
        }

        ShellPage MakePage(string key)
        {
            Size size = PageSize;
            switch (key)
            {
                case PAGE_HOME: return new HomePage(this, size);
                case PAGE_GAMES: return new GamesPage(this, size);
                case PAGE_FRIENDS: return new FriendsPage(this, size);
                case PAGE_AUTO: return new AutomationPage(this, size);
                case PAGE_SHOTS: return new ShotsPage(this, size);
                case PAGE_LOOKS: return new LooksPage(this, size);
                default: return null;
            }
        }

        // Made the first time it is asked for, and not before.
        internal void ShowPage(string key)
        {
            if (key == currentPage && (key == PAGE_KEEPER || pages.ContainsKey(key))) return;

            ShellPage was = PageNow;
            if (was != null) { was.GoOut(); was.Visible = false; }

            ShellPage page = null;
            if (key != PAGE_KEEPER && !pages.TryGetValue(key, out page))
            {
                page = MakePage(key);
                if (page == null) return;
                page.FitContent();
                page.Location = new Point(Rail.WIDTH, TITLEBAR_H);
                page.Visible = false;
                Controls.Add(page);
                pages[key] = page;
            }

            keeperPage.Visible = key == PAGE_KEEPER;
            currentPage = key;
            rail.Selected = key;
            if (page != null)
            {
                page.Visible = true;
                page.BringToFront();
                if (Visible) page.ComeIn();
            }
            settings.Page = key;
            if (started && !initializing) settings.Save();
        }

        // ---------- what pages ask for ----------

        public IList<RobloxAccount> Accounts()
        {
            if (!started) return new List<RobloxAccount>();
            EnsureAccounts();
            return accounts.Accounts;
        }

        public WatchedClient[] Clients() { return watchWork.Clients; }

        public long MemoryOf(int pid)
        {
            foreach (ClientInfo c in lastClients) if (c.Pid == pid) return c.WorkingSet;
            return 0;
        }

        public void ShowClient(int pid)
        {
            IntPtr hwnd = WindowCapture.GameWindow(pid);
            if (hwnd != IntPtr.Zero) InputSender.ShowClient(hwnd);
        }

        public void Go(string page) { ShowPage(page); }
        public void Say(string line) { Log(line); }
        public IMacroHost Macros { get { return this; } }
        public IScriptHost Scripts { get { return this; } }
        public WatchKit Kit { get { return watchKit; } }

        void IShell.OpenAccounts() { OpenAccounts(); RefreshPage(); }
        void IShell.OpenMacros() { OpenMacros(); }
        void IShell.OpenClicker() { OpenClicker(); }
        void IShell.OpenScripts() { OpenScripts(); }

        // Like the other windows opened from the tray: nothing without the
        // watch kit that the Scripts window's pictures and accounts come from.
        void OpenScripts()
        {
            if (watchKit == null || watchKit.Scripts == null) return;
            RestoreFromTray();
            using (ScriptsDialog d = new ScriptsDialog(watchKit)) d.ShowDialog(this);
        }

        // The tray menu is built before the shell, in BuildUi; "Scripts..."
        // goes in under "Macros...", where someone looking for it looks.
        void AddScriptsToTray()
        {
            if (tray == null || tray.ContextMenuStrip == null) return;
            ToolStripItemCollection items = tray.ContextMenuStrip.Items;
            int at = items.Count;
            for (int i = 0; i < items.Count; i++)
                if (items[i].Text == "Macros...") { at = i + 1; break; }
            items.Insert(at, new ToolStripMenuItem("Scripts...", null, delegate { OpenScripts(); }));
        }

        void IShell.OpenWatchers() { if (watchKit != null) OpenWatchers(); }

        public string PlayAs
        {
            get { return playAs ?? settings.PlayAs; }
            set
            {
                playAs = value;
                settings.PlayAs = value;
                if (started && !initializing) settings.Save();
            }
        }

        public bool TipsHidden
        {
            get { return settings.TipsHidden; }
            set
            {
                settings.TipsHidden = value;
                if (started && !initializing) settings.Save();
            }
        }

        public LooksChoices LooksChosen
        {
            get { return settings.Looks; }
            set
            {
                settings.Looks = value ?? new LooksChoices();
                if (started && !initializing) settings.Save();
            }
        }

        // ---------- the shader overlay ----------

        readonly ShaderRunner shaders = new ShaderRunner();
        string shaderProblem;

        public string Shader { get { return settings.Shader ?? ShaderLooks.OFF; } }
        public int ShaderStrength { get { return settings.ShaderStrength; } }

        public void SetShader(string look, int strength)
        {
            settings.Shader = look ?? ShaderLooks.OFF;
            settings.ShaderStrength = strength;
            if (started && !initializing) settings.Save();
            shaderProblem = null;
            shaders.TryAgain();
            Log(ShaderLooks.IsOn(settings.Shader)
                ? "Shader look on: " + ShaderLooks.Describe(settings.Shader) + ". It shows over a Roblox window while that window is in front."
                : "Shader look is off.");
            ShaderTick();
        }

        public string ShaderState
        {
            get
            {
                if (!ShaderLooks.IsOn(Shader)) return "Off.";
                if (shaderProblem != null) return "Couldn't show it: " + shaderProblem + ".";
                if (shaders.Count == 0) return "On. Open a Roblox window and it will show over the game.";
                return "On, over " + (shaders.Count == 1 ? "1 Roblox window" : shaders.Count + " Roblox windows")
                     + " - it shows while that window is in front.";
            }
        }

        // Once a second, from the main loop: an overlay over every Roblox
        // window while a look is on, and none when it is not.
        void ShaderTick()
        {
            if (!started) return;
            if (!ShaderLooks.IsOn(Shader))
            {
                if (shaders.Count > 0) shaders.StopAll();
                return;
            }
            string problem;
            string file = ShaderLooks.FileFor(Shader, ShaderStrength, ShaderLooks.Folder, out problem);
            if (file == null)
            {
                shaders.StopAll();
                if (shaderProblem != problem) { shaderProblem = problem; Log("Shader look: " + problem + "."); }
                return;
            }

            Dictionary<int, IntPtr> windows = new Dictionary<int, IntPtr>();
            foreach (ClientInfo c in lastClients)
            {
                IntPtr hwnd = WindowCapture.GameWindow(c.Pid);
                if (hwnd != IntPtr.Zero) windows[c.Pid] = hwnd;
            }
            shaders.Sync(windows, file, DateTime.Now);
            if (shaders.Problem != null)
            {
                // A shader with a mistake in it says so once, with the
                // compiler's own words, rather than failing quietly.
                if (shaderProblem != shaders.Problem) Log("Couldn't show the shader look: " + shaders.Problem + ".");
                shaderProblem = shaders.Problem;
                shaders.Problem = null;
            }
        }

        int looksTicks;
        bool looksBusy;

        // As the main loop last saw it: Roblox's installer is at work.
        bool RobloxInstallerRunning() { return installerSeen; }

        // Once a minute, from the main loop: a Roblox version installed since
        // gets the looks that were asked for. Off the window's thread - it
        // reads folders - and it does nothing at all unless something on the
        // Looks page was switched on.
        void LooksTick()
        {
            if (++looksTicks % 60 != 20 || looksBusy) return;
            // Not while Roblox is installing: a new version folder is still
            // being filled, and a pack's file put there first would be taken
            // for one the pack added - and deleted when Roblox is put back.
            if (RobloxInstallerRunning()) { looksTicks -= 15; return; }
            LooksChoices chosen = settings.Looks;
            looksBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string said = null;
                try { said = Looks.Reapply(LooksPlaces.Real(), chosen); } catch { }
                OnUi(delegate
                {
                    looksBusy = false;
                    if (said != null) Log(said);
                });
            });
        }

        public void Search(string words)
        {
            ShowPage(PAGE_GAMES);
            GamesPage games = PageNow as GamesPage;
            if (games != null) games.Search(words);
        }

        // Accounts changed, say: the page in view asks again.
        void RefreshPage()
        {
            ShellPage p = PageNow;
            if (p == null || !Visible) return;
            p.GoOut();
            p.ComeIn();
        }

        // ---------- played lately ----------

        public IList<string> RecentPlaces()
        {
            if (!recentsLoaded)
            {
                recentsLoaded = true;
                recents.Load();
                // Nothing kept yet: start from what Roblox's own logs say.
                if (recents.Places.Count == 0 && started)
                {
                    foreach (string place in Recents.FromLogs(RobloxLog.LogsDir, 12)) recents.Add(place);
                    if (recents.Places.Count > 0) recents.Save();
                }
            }
            return recents.Places;
        }

        // From the join hook: a client arrived in a game.
        void RememberPlace(string placeId)
        {
            RecentPlaces();
            if (recents.Add(placeId)) recents.Save();
        }

        // ---------- playing a game ----------

        public void Play(GameCard game, Control under)
        {
            ContextMenuStrip menu = DarkMenu.Make();
            IList<RobloxAccount> all = Accounts();
            List<RobloxAccount> usable = new List<RobloxAccount>();
            foreach (RobloxAccount a in all) if (a.IsUsable) usable.Add(a);

            string chosen = PlayAs;
            foreach (RobloxAccount a in usable)
            {
                if (!string.Equals(a.Name, chosen, StringComparison.OrdinalIgnoreCase)) continue;
                RobloxAccount first = a;
                menu.Items.Add("Play as " + a.Name, null, delegate { Launch(game, new RobloxAccount[] { first }); });
            }
            int others = 0;
            foreach (RobloxAccount a in usable)
            {
                if (string.Equals(a.Name, chosen, StringComparison.OrdinalIgnoreCase) || others >= 10) continue;
                RobloxAccount other = a;
                menu.Items.Add("Play as " + a.Name, null, delegate { Launch(game, new RobloxAccount[] { other }); });
                others++;
            }
            if (usable.Count > 1)
                menu.Items.Add("Play with all " + usable.Count + " accounts", null, delegate { Launch(game, usable.ToArray()); });
            if (usable.Count == 0)
                menu.Items.Add("Add an account to play from here...", null, delegate { ((IShell)this).OpenAccounts(); });

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open its page on roblox.com", null, delegate { OpenGamePage(game.PlaceId); });
            menu.Show(Cursor.Position);
        }

        static void OpenGamePage(string placeId)
        {
            long n;
            if (!long.TryParse(placeId, out n)) return;
            try { Process.Start("https://www.roblox.com/games/" + n); } catch { }
        }

        // The same launch the Accounts window makes: a ticket for the
        // account's own saved sign-in, as pressing Play on the website does.
        void Launch(GameCard game, IList<RobloxAccount> who)
        {
            if (string.IsNullOrEmpty(game.PlaceId)) { Log("Couldn't start " + game.Name + " - Roblox didn't say where it begins."); return; }
            LaunchRequest r = new LaunchRequest();
            r.Where = JoinWhere.Any;
            foreach (RobloxAccount a in who)
            {
                LaunchSeat s = new LaunchSeat();
                s.Account = a.Name;
                s.Cookie = a.Cookie;
                s.PlaceId = game.PlaceId;
                s.RunningPid = PidOfAccount(a.Name);
                s.Hunting = IsHuntingAccount(a.Name);
                r.Seats.Add(s);
            }
            // One account, already in a game: starting it again signs its
            // open client out, so it is asked - as the Accounts window asks.
            // Several at once are never asked and never moved: the launcher
            // leaves any that is already playing where it is.
            if (who.Count == 1 && r.Seats[0].RunningPid > 0 && !r.Seats[0].Hunting)
            {
                if (MessageBox.Show(this, who[0].Name + " is already playing.\r\n\r\nClose that Roblox window and start " + game.Name + " instead?",
                        "Already playing", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                r.Seats[0].Restart = true;
            }
            RunLaunch(r, "Starting " + game.Name + " as " + Names(who) + "...");
        }

        static string Names(IList<RobloxAccount> who)
        {
            List<string> names = new List<string>();
            foreach (RobloxAccount a in who) names.Add(a.Name);
            return string.Join(", ", names.ToArray());
        }

        // One launch at a time, as the Accounts window keeps it: clients
        // started at the same moment race each other over Roblox's launcher.
        bool launching;

        void RunLaunch(LaunchRequest r, string said)
        {
            if (launching)
            {
                Log("Still starting the last one. Try again in a moment.");
                return;
            }
            launching = true;
            Log(said);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LaunchResult> results;
                try { results = AccountLauncher.Run(r, new LiveLaunchWorld(), new Random(), null); }
                catch (Exception ex)
                {
                    LaunchResult failed = new LaunchResult();
                    failed.Account = r.Seats.Count > 0 ? r.Seats[0].Account : "";
                    failed.Problem = ex.Message;
                    results = new List<LaunchResult> { failed };
                }
                OnUi(delegate
                {
                    launching = false;
                    foreach (LaunchResult res in results)
                    {
                        if (res.Pid > 0) clientLabels.Assign(res.Pid, res.Account);
                        if (res.Problem != null) Log("Couldn't start " + res.Account + ": " + res.Problem);
                        else if (!string.IsNullOrEmpty(res.Said)) Log(res.Said);
                        else if (res.Pid > 0) Log(res.Account + " started.");
                    }
                });
            });
        }

        // To a friend's server, as the account whose friend they are.
        public void Join(FriendCard friend, Control under)
        {
            string who = friend.DisplayName ?? friend.Name;
            if (!friend.Joinable)
            {
                Log(friend.Online ? "Roblox won't say which server " + who + " is in - their privacy settings decide that."
                                  : who + " is offline.");
                return;
            }
            RobloxAccount a = null;
            foreach (RobloxAccount x in Accounts())
                if (x.IsUsable && string.Equals(x.Name, PlayAs, StringComparison.OrdinalIgnoreCase)) a = x;
            if (a == null) { Log("Choose an account to join " + who + " as."); return; }

            ContextMenuStrip menu = DarkMenu.Make();
            RobloxAccount account = a;
            menu.Items.Add("Join " + who + " as " + a.Name, null, delegate
            {
                LaunchRequest r = new LaunchRequest();
                r.ServerPlaceId = friend.PlaceId;
                r.ServerId = friend.JobId;
                LaunchSeat s = new LaunchSeat();
                s.Account = account.Name;
                s.Cookie = account.Cookie;
                s.RunningPid = PidOfAccount(account.Name);
                s.Hunting = IsHuntingAccount(account.Name);
                r.Seats.Add(s);
                RunLaunch(r, "Joining " + who + " as " + account.Name + ".");
            });
            menu.Show(Cursor.Position);
        }

        // ---------- screenshots ----------

        public byte ShotKey { get { return settings.ShotKey; } }

        // Null when the key was taken; otherwise why it can't be. Heard by
        // GlobalKeys, which never keeps a key from other programs, so one
        // another program uses as a shortcut is no reason to refuse it.
        public string SetShotKey(byte vk)
        {
            // The one check every key goes through: a rule's key or a
            // macro's is as taken as the stop key.
            string why = KeyUseProblem(vk, KeyUse.SHOT, null);
            if (why != null) return why;
            settings.ShotKey = vk;
            settings.Save();
            RefreshGlobalKeys();
            return null;
        }

        // The window in front if it is Roblox; otherwise every one that is open.
        public void TakeScreenshots()
        {
            WatchedClient[] clients = watchWork.Clients;
            if (clients.Length == 0) { Log("No Roblox window is open to take a screenshot of."); return; }
            int front = PerformanceManager.ForegroundPid();
            int taken = 0;
            foreach (WatchedClient c in clients)
            {
                bool inFront = c.Pid == front;
                bool anyInFront = false;
                foreach (WatchedClient x in clients) if (x.Pid == front) anyInFront = true;
                if (anyInFront && !inFront) continue;
                string problem;
                string path = Shots.Take(WindowCapture.GameWindow(c.Pid), c.AccountName ?? c.Label, Shots.Folder, DateTime.Now, out problem);
                if (path != null) taken++;
                else Log("Couldn't take a screenshot of " + c.Label + ": " + problem + ".");
            }
            if (taken > 0) Log("Saved " + (taken == 1 ? "a screenshot" : taken + " screenshots") + " to " + Shots.Folder + ".");
            ShotsPage shots = PageNow as ShotsPage;
            if (shots != null) shots.Reload();
        }
    }
}
