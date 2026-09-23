using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The account manager.
    //
    // Roblox's own account switcher holds five and makes you sign out to
    // change. This holds as many as you like, keeps each one's session in its
    // own browser profile, launches any of them - or several at once - and can
    // hand you a browser signed in as any of them so you can find a game
    // yourself instead of pasting a link.
    // What the Accounts window needs from the app around it.
    interface IAccountsHost
    {
        void Log(string line);                   // from any thread
        void Launched(int pid, string account);  // from any thread
        int PidOf(string account);               // 0 when it has no client open
        bool IsHunting(string account);
        AccountsPrefs Prefs { get; }
        void SavePrefs();
        void Follow(FollowRequest f);            // from any thread
        string Following { get; }                // the player being followed, or null
        void StopFollowing();
    }

    // For a window made on its own, with nothing around it.
    class PlainAccountsHost : IAccountsHost
    {
        readonly Action<string> log;
        readonly Action<int, string> launched;
        readonly AccountsPrefs prefs = new AccountsPrefs();

        public PlainAccountsHost(Action<string> log, Action<int, string> launched)
        {
            this.log = log;
            this.launched = launched;
        }

        public void Log(string line) { if (log != null) log(line); }
        public void Launched(int pid, string account) { if (launched != null) launched(pid, account); }
        public int PidOf(string account) { return 0; }
        public bool IsHunting(string account) { return false; }
        public AccountsPrefs Prefs { get { return prefs; } }
        public void SavePrefs() { }
        public void Follow(FollowRequest f) { }
        public string Following { get { return null; } }
        public void StopFollowing() { }
    }

    class AccountsDialog : Form
    {
        const int W = 700;
        const int ROW = 32;
        const int LIST_TOP = 4;             // the first row's top inside the list

        readonly AccountStore store;
        readonly IAccountsHost host;
        readonly Action<string> log;

        ScrollPanel list;
        Label empty, selectedCount;
        TextBox gameBox, playerBox;
        Button launchSelected;
        ThemedPicker cmbWhere;
        ThemedCheckBox chkTogether, chkFollow;
        LinkLabel stopFollowing;
        readonly ToolTip tips = new ToolTip();
        readonly System.Windows.Forms.Timer playingTimer = new System.Windows.Forms.Timer();
        readonly Dictionary<string, ThemedCheckBox> picks =
            new Dictionary<string, ThemedCheckBox>(StringComparer.OrdinalIgnoreCase);
        readonly List<Label> rowLines = new List<Label>();
        readonly List<bool> rowPlaying = new List<bool>();
        bool busy, filling;

        public AccountsDialog(AccountStore store, Action<string> log, Action<int, string> onLaunched)
            : this(store, new PlainAccountsHost(log, onLaunched)) { }

        public AccountsDialog(AccountStore store, IAccountsHost host)
        {
            this.store = store;
            this.host = host;
            this.log = host.Log;

            Text = "Accounts";
            // It has a taskbar button, and showed WinForms' default icon on it.
            Ui.GiveAppIcon(this);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(W, 538);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9f);

            Card card = new Card();
            card.Location = new Point(12, 12);
            card.Size = new Size(W - 24, 514);
            Controls.Add(card);

            card.Controls.Add(Ui.SectionTitle("ACCOUNTS"));

            // No subtitle: the list takes the space instead of a line of text
            // explaining what the window plainly is. It still ends where it
            // always did, so nothing below moves.
            list = new ScrollPanel();
            list.Location = new Point(Ui.PAD, 44);
            list.Size = new Size(W - 24 - Ui.PAD * 2, 260);
            list.BackColor = Theme.Card;
            list.AutoScroll = true;
            card.Controls.Add(list);

            // Inside the list rather than over it: WinForms puts a later-added
            // sibling at the BACK of the z-order, where it would be invisible.
            empty = Ui.MutedLabel("No accounts yet - Add account opens Roblox's login page.", 6, 10, 9f);
            list.Controls.Add(empty);

            const int gameRow = 316;
            card.Controls.Add(Ui.RowLabel("Game link", Ui.PAD, gameRow, ROW_H_ROW, 70, 8.25f, Theme.Muted));
            gameBox = new TextBox();
            gameBox.Location = new Point(92, gameRow);
            gameBox.Size = new Size(W - 24 - 92 - Ui.PAD, 22);
            gameBox.BorderStyle = BorderStyle.FixedSingle;
            gameBox.BackColor = Theme.Inset;
            gameBox.ForeColor = Theme.Text;
            card.Controls.Add(gameBox);

            // Centred once the window is up, not here.
            //
            // A single-line TextBox ignores the height it is given and
            // recomputes it from its font when its handle is created, so its
            // Height is still wrong at this point - which is exactly why the
            // label beside it sat two pixels high. By Shown it is final.
            Shown += delegate { Ui.CenterIn(gameBox, gameRow, ROW_H_ROW); };

            // One line, the width of the box above it: MutedLabel wraps at 388
            // by default, which ran it into the Server row.
            Label hint = Ui.MutedLabel(
                "Optional. Blank uses each account's own saved game. A server link from a Discord post sends them all there.",
                Ui.PAD, gameRow + 30, 8.25f);
            hint.MaximumSize = new Size(W - 24 - Ui.PAD * 2, 0);
            card.Controls.Add(hint);

            BuildServerRow(card, 372);

            const int selRow = 414;
            // Width stops short of the links beside it: RowLabel is opaque and
            // sits in front of anything added later, so an oversized box here
            // silently paints over the first characters of "Select all".
            selectedCount = Ui.RowLabel("", Ui.PAD, selRow, ROW_H_BTN, 222, 8.25f, Theme.Muted);
            card.Controls.Add(selectedCount);

            LinkLabel all = Ui.RowLink("Select all", 246, selRow, ROW_H_BTN);
            all.Click += delegate { SetAll(true); };
            card.Controls.Add(all);

            LinkLabel none = Ui.RowLink("Clear", 330, selRow, ROW_H_BTN);
            none.Click += delegate { SetAll(false); };
            card.Controls.Add(none);

            launchSelected = Ui.AccentButton("Launch selected", W - 24 - Ui.PAD - 150, selRow, 150, 28);
            launchSelected.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            launchSelected.Click += delegate { LaunchSelected(); };
            card.Controls.Add(launchSelected);

            const int btnRow = 458;
            Button add = Ui.AccentButton("Add account", Ui.PAD, btnRow, 120, 28);
            add.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            add.Click += delegate { AddAccount(); };
            card.Controls.Add(add);

            Button close = Ui.AccentButton("Close", W - 24 - Ui.PAD - 90, btnRow, 90, 28);
            close.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            close.Click += delegate { Close(); };
            card.Controls.Add(close);

            // When shown, not when made: it deletes folders, and a window made
            // without being shown - by a test - has no business doing that.
            Shown += delegate { TidyOrphanProfiles(); };

            // Who is playing changes while the window is open.
            playingTimer.Interval = 2000;
            playingTimer.Tick += delegate { RefreshPlaying(); UpdateFollowing(); };
            Shown += delegate { playingTimer.Start(); };
            FormClosed += delegate { playingTimer.Stop(); playingTimer.Dispose(); tips.Dispose(); };

            FillServerRow();
            Rebuild();
        }

        // ---------- where they go ----------

        void BuildServerRow(Card card, int y)
        {
            card.Controls.Add(Ui.RowLabel("Server", Ui.PAD, y, ROW_H_ROW, 70, 8.25f, Theme.Muted));

            cmbWhere = Ui.DarkCombo(92, y, 170);
            cmbWhere.Items.Add("Any server");
            cmbWhere.Items.Add("Emptiest servers");
            cmbWhere.Items.Add("Busiest servers");
            cmbWhere.Items.Add("Where a player is");
            cmbWhere.SelectedIndexChanged += delegate { WhereChanged(); };
            tips.SetToolTip(cmbWhere, "Any: wherever Roblox puts them. Emptiest or busiest: each to its own "
                + "server with the fewest or most players. A player: into the server that player is in.");
            card.Controls.Add(cmbWhere);

            chkTogether = Ui.DarkCheck("All in the same server", 276, y, 8.25f);
            chkTogether.AutoSize = true;
            Ui.CenterIn(chkTogether, y, ROW_H_ROW);
            chkTogether.CheckedChanged += delegate { if (!filling) { host.Prefs.Together = chkTogether.Checked; host.SavePrefs(); } };
            tips.SetToolTip(chkTogether, "One server with room for every account being launched.");
            card.Controls.Add(chkTogether);

            playerBox = new TextBox();
            playerBox.Location = new Point(276, y);
            playerBox.Size = new Size(150, 22);
            playerBox.BorderStyle = BorderStyle.FixedSingle;
            playerBox.BackColor = Theme.Inset;
            playerBox.ForeColor = Theme.Text;
            playerBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            playerBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            playerBox.TextChanged += delegate { if (!filling) { host.Prefs.Player = playerBox.Text.Trim(); host.SavePrefs(); } };
            tips.SetToolTip(playerBox, "Their Roblox name - one of your own accounts, or anyone whose "
                + "privacy settings let your accounts join them.");
            card.Controls.Add(playerBox);
            Shown += delegate { Ui.CenterIn(playerBox, y, ROW_H_ROW); };

            chkFollow = Ui.DarkCheck("Keep following", 438, y, 8.25f);
            chkFollow.AutoSize = true;
            Ui.CenterIn(chkFollow, y, ROW_H_ROW);
            chkFollow.CheckedChanged += delegate { if (!filling) { host.Prefs.KeepFollowing = chkFollow.Checked; host.SavePrefs(); } };
            tips.SetToolTip(chkFollow, "When they move to another server, the accounts follow them there - "
                + "one at a time, checking every half minute.");
            card.Controls.Add(chkFollow);

            // Sized in the font it is drawn in, or it wraps.
            stopFollowing = Ui.RowLink("Stop following", W - 24 - Ui.PAD - 88, y, ROW_H_ROW, 8.25f);
            stopFollowing.Click += delegate { host.StopFollowing(); UpdateFollowing(); };
            card.Controls.Add(stopFollowing);
        }

        void FillServerRow()
        {
            filling = true;
            try
            {
                AccountsPrefs p = host.Prefs;
                cmbWhere.SelectedIndex = (int)p.Where;
                chkTogether.Checked = p.Together;
                playerBox.Text = p.Player ?? "";
                chkFollow.Checked = p.KeepFollowing;
                AutoCompleteStringCollection names = new AutoCompleteStringCollection();
                foreach (RobloxAccount a in store.Accounts) names.Add(a.Name);
                playerBox.AutoCompleteCustomSource = names;
            }
            finally { filling = false; }
            ShowWhatWhereNeeds();
            UpdateFollowing();
        }

        void WhereChanged()
        {
            ShowWhatWhereNeeds();
            if (filling) return;
            host.Prefs.Where = Where;
            host.SavePrefs();
        }

        JoinWhere Where { get { return cmbWhere.SelectedIndex < 0 ? JoinWhere.Any : (JoinWhere)cmbWhere.SelectedIndex; } }

        public bool ShowsPlayer { get { return Where == JoinWhere.Player; } }
        public bool ShowsTogether { get { return Where != JoinWhere.Player; } }

        void ShowWhatWhereNeeds()
        {
            chkTogether.Visible = ShowsTogether;
            playerBox.Visible = chkFollow.Visible = ShowsPlayer;
        }

        void UpdateFollowing()
        {
            string who = host.Following;
            stopFollowing.Visible = who != null;
            if (who != null) tips.SetToolTip(stopFollowing, "Following " + who + ".");
        }

        public void SetWhere(JoinWhere where) { cmbWhere.SelectedIndex = (int)where; }

        const int ROW_H_BTN = 28;
        // The standard row height shared with the main window.
        const int ROW_H_ROW = 26;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Color.FromArgb(58, 58, 80), 1f))
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        static string ProfilesRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RobloxKeeper", "profiles");
            }
        }

        static string ProfileDir(string accountName)
        {
            return Path.Combine(ProfilesRoot, AccountStore.SafeFolderName(accountName));
        }

        // ---------- the list ----------

        void Rebuild()
        {
            list.SuspendLayout();
            while (list.Controls.Count > 0)
            {
                Control c = list.Controls[0];
                list.Controls.Remove(c);
                if (!ReferenceEquals(c, empty)) c.Dispose();
            }
            picks.Clear();
            rowLines.Clear();
            rowPlaying.Clear();
            list.Controls.Add(empty);
            empty.Visible = store.Accounts.Count == 0;

            int y = LIST_TOP;
            for (int i = 0; i < store.Accounts.Count; i++)
            {
                RobloxAccount a = store.Accounts[i];   // captured per row, not per loop

                list.Controls.Add(RowGrip.For(i, store.Accounts.Count, y, ROW, LIST_TOP, MoveAt, null));

                ThemedCheckBox pick = Ui.DarkCheck("", 18, y, 8.25f);
                Ui.CenterIn(pick, y, ROW);
                pick.Checked = IsTicked(a.Name);
                pick.CheckedChanged += delegate { Remember(a.Name, pick.Checked); UpdateSelectedCount(); };
                list.Controls.Add(pick);
                picks[a.Name] = pick;

                list.Controls.Add(Ui.RowLabel(a.Name, 42, y, ROW, 126, 9f, Theme.Text));

                Label note = Ui.RowLabel("", 172, y, ROW, 226, 8.25f, Theme.Muted);
                note.AutoEllipsis = true;
                list.Controls.Add(note);
                rowLines.Add(note);
                rowPlaying.Add(false);
                ShowLine(i, a, host.PidOf(a.Name) > 0);

                LinkLabel play = Ui.RowLink("Play", 406, y, ROW);
                play.Click += delegate { LaunchOne(a); };
                list.Controls.Add(play);

                LinkLabel browse = Ui.RowLink("Browse", 452, y, ROW);
                browse.Click += delegate { BrowseAs(a); };
                list.Controls.Add(browse);

                LinkLabel edit = Ui.RowLink("Edit", 518, y, ROW);
                edit.Click += delegate { EditAccount(a); };
                list.Controls.Add(edit);

                LinkLabel remove = Ui.RowLink("Remove", 564, y, ROW);
                remove.Click += delegate { RemoveAccount(a); };
                list.Controls.Add(remove);

                y += ROW;
            }
            list.ResumeLayout();
            UpdateSelectedCount();
        }

        // Beside the name: "playing" while it has a client open, then the
        // note - the user's own description - or, without one, whether it
        // has a saved game.
        void ShowLine(int i, RobloxAccount a, bool playing)
        {
            string second = !string.IsNullOrEmpty(a.Note)
                ? a.Note
                : (string.IsNullOrEmpty(a.GameUrl) ? "no game set" : "has a saved game");
            rowPlaying[i] = playing;
            rowLines[i].Text = playing ? "playing · " + second : second;
            rowLines[i].ForeColor = playing ? Theme.Green : Theme.Muted;
        }

        public void RefreshPlaying()
        {
            for (int i = 0; i < rowLines.Count && i < store.Accounts.Count; i++)
            {
                bool now = host.PidOf(store.Accounts[i].Name) > 0;
                if (now != rowPlaying[i]) ShowLine(i, store.Accounts[i], now);
            }
        }

        public string RowLine(int i) { return rowLines[i].Text; }

        public void MoveAt(int from, int to)
        {
            if (!store.Move(from, to)) return;
            store.Save();
            Rebuild();
        }

        bool IsTicked(string name)
        {
            foreach (string n in host.Prefs.Ticked)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Kept in the order of the list, so what is remembered reads like it.
        void Remember(string name, bool on)
        {
            List<string> now = new List<string>();
            foreach (RobloxAccount a in store.Accounts)
            {
                bool ticked = string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) ? on : IsTicked(a.Name);
                if (ticked) now.Add(a.Name);
            }
            host.Prefs.Ticked.Clear();
            host.Prefs.Ticked.AddRange(now);
            host.SavePrefs();
        }

        public string TickedNames
        {
            get
            {
                List<string> n = new List<string>();
                foreach (RobloxAccount a in Selected()) n.Add(a.Name);
                return string.Join(",", n.ToArray());
            }
        }

        public void Tick(string name, bool on)
        {
            ThemedCheckBox c;
            if (picks.TryGetValue(name, out c)) c.Checked = on;
        }

        void SetAll(bool on)
        {
            foreach (ThemedCheckBox c in picks.Values) c.Checked = on;
            UpdateSelectedCount();
        }

        List<RobloxAccount> Selected()
        {
            List<RobloxAccount> picked = new List<RobloxAccount>();
            foreach (RobloxAccount a in store.Accounts)
            {
                ThemedCheckBox c;
                if (picks.TryGetValue(a.Name, out c) && c.Checked) picked.Add(a);
            }
            return picked;
        }

        void UpdateSelectedCount()
        {
            if (busy) return;              // it is counting a launch through
            int n = Selected().Count;
            selectedCount.Text = n == 0 ? "Tick accounts to launch several at once"
                                        : n + " selected";
            launchSelected.Enabled = n > 0;
        }

        // ---------- adding, editing, removing ----------

        void AddAccount()
        {
            string tempDir = ProfileDir("new-" + DateTime.Now.Ticks);

            string cookie, detected, userId;
            using (AccountBrowserForm login = new AccountBrowserForm(tempDir, null, true))
            {
                if (login.ShowDialog(this) != DialogResult.OK) { TryDelete(tempDir); return; }
                cookie = login.Cookie;
                detected = login.DetectedName;
                userId = login.DetectedUserId;
            }
            if (string.IsNullOrEmpty(cookie)) { TryDelete(tempDir); return; }

            string name = detected;
            if (string.IsNullOrEmpty(name))
            {
                name = Prompt("Roblox didn't say which account that was. What should it be called?", "");
                if (string.IsNullOrEmpty(name)) { TryDelete(tempDir); return; }
            }

            // Move the profile to its permanent home, and record where it
            // actually ended up.
            //
            // WebView2's browser processes outlive the window that started
            // them and hold the folder open for a moment, so the first attempt
            // often fails. Retrying handles that; if it still fails the account
            // remembers the temporary folder rather than pointing at an empty
            // one, which is what previously produced a login page on Browse.
            string finalDir = ProfileDir(name);
            string actualDir = MoveProfile(tempDir, finalDir);

            RobloxAccount a = store.Find(name);
            bool isNew = a == null;
            if (isNew)
            {
                a = new RobloxAccount();
                a.Name = name;
                a.BrowserTrackerId = AccountStore.NewBrowserTrackerId();
            }
            a.Cookie = cookie;
            a.ProfilePath = actualDir;
            if (!string.IsNullOrEmpty(userId)) a.UserId = userId;
            store.Add(a);
            store.Save();

            log(isNew ? "Account added: " + name + "." : "Account " + name + " signed in again.");
            Rebuild();
        }

        void EditAccount(RobloxAccount a)
        {
            string note = Prompt("What is " + a.Name + " for? (e.g. \"farms blox fruits overnight\")", a.Note);
            if (note == null) return;          // cancelled
            a.Note = note;

            string game = Prompt("Game link for " + a.Name + ", or blank for none:", a.GameUrl);
            if (game != null)
            {
                if (game.Length > 0 && RobloxAuth.PlaceIdFromUrl(game) == null)
                    MessageBox.Show(this, "That doesn't look like a Roblox game link or place id - leaving the old one.",
                        "Not a game link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    a.GameUrl = game;
            }

            store.Save();
            log(a.Name + " updated.");
            Rebuild();
        }

        void RemoveAccount(RobloxAccount a)
        {
            if (MessageBox.Show(this,
                    "Remove " + a.Name + "?\r\n\r\nIts saved sign-in and browser profile are deleted from this PC. "
                    + "The Roblox account itself is not affected.",
                    "Remove account", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            store.Remove(a.Name);
            store.Save();
            TryDelete(AccountStore.ProfilePathFor(a, ProfilesRoot));
            log("Account removed: " + a.Name + ".");
            Rebuild();
        }

        // ---------- browsing ----------

        void BrowseAs(RobloxAccount a)
        {
            // The account's own saved session is handed to the browser, so this
            // works whether or not the profile folder still holds a sign-in.
            using (AccountBrowserForm b = new AccountBrowserForm(
                       AccountStore.ProfilePathFor(a, ProfilesRoot), a.Name, false, a.Cookie))
            {
                // Play inside that browser already carries a ticket for this
                // account, so it only needs starting - no second ticket request.
                // A client of this account already open is closed first: a
                // second one would sign it out (Roblox's 273) anyway, and
                // pressing Join means "go there".
                b.LaunchRequested += delegate (string url) { StartFromBrowser(url, a.Name, host.PidOf(a.Name)); };
                b.ShowDialog(this);

                // Roblox rotates sessions; keep whatever the browser ended with.
                if (!string.IsNullOrEmpty(b.Cookie) && b.Cookie != a.Cookie)
                {
                    a.Cookie = b.Cookie;
                    store.Save();
                }
            }
        }

        // ---------- launching ----------

        void LaunchSelected()
        {
            List<RobloxAccount> picked = Selected();
            if (picked.Count > 0) Launch(picked, false);
        }

        void LaunchOne(RobloxAccount a)
        {
            Launch(new List<RobloxAccount> { a }, true);
        }

        // Everything about the launch is decided here, on the window's
        // thread; the tickets, the server lists and the three seconds between
        // clients run on a worker. They used to run here, and the window
        // froze for as long as it took - fifteen seconds for five accounts.
        void Launch(List<RobloxAccount> accounts, bool alone)
        {
            if (busy)
            {
                Say("Still starting the last lot - one moment.", true);
                return;
            }

            LaunchRequest r = new LaunchRequest();
            r.Where = Where;
            r.Together = chkTogether.Checked;
            r.Player = playerBox.Text.Trim();

            string link = gameBox.Text.Trim();
            string typedPlace = null, serverPlace, serverId;
            if (RobloxAuth.ServerFromUrl(link, out serverPlace, out serverId))
            {
                r.ServerPlaceId = serverPlace;
                r.ServerId = serverId;
            }
            else if (link.Length > 0)
            {
                typedPlace = RobloxAuth.PlaceIdFromUrl(link);
                if (typedPlace == null && r.Where != JoinWhere.Player)
                {
                    MessageBox.Show(this, "That doesn't look like a Roblox game link, a server link or a place id.",
                        "Not a game link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            foreach (RobloxAccount a in accounts)
            {
                LaunchSeat s = new LaunchSeat();
                s.Account = a.Name;
                s.Cookie = a.Cookie;
                s.TrackerId = a.BrowserTrackerId;
                s.PlaceId = typedPlace ?? RobloxAuth.PlaceIdFromUrl(a.GameUrl);
                s.RunningPid = host.PidOf(a.Name);
                s.Hunting = host.IsHunting(a.Name);
                r.Seats.Add(s);
            }

            // Play on one that is already playing, with nowhere in particular
            // to go: starting it again would sign its client out, so ask.
            if (alone && r.Seats[0].RunningPid > 0 && r.ServerId == null
                && r.Where == JoinWhere.Any && !r.Together)
            {
                if (MessageBox.Show(this, accounts[0].Name + " is already playing.\r\n\r\nClose that client and start it again?",
                        "Already playing", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                r.Seats[0].Restart = true;
            }

            bool follow = r.Where == JoinWhere.Player && chkFollow.Checked && r.ServerId == null;
            busy = true;
            launchSelected.Enabled = false;
            Say(alone ? "Starting " + accounts[0].Name + "..." : "Starting " + accounts.Count + " accounts...", false);

            Random rng = new Random();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LaunchResult> results;
                try
                {
                    results = AccountLauncher.Run(r, new LiveLaunchWorld(), rng,
                        delegate(string progress) { OnWindow(delegate { Say(progress, false); }); });
                }
                catch (Exception ex)
                {
                    results = new List<LaunchResult>();
                    host.Log("Launching stopped: " + ex.Message);
                }
                Report(r, results, alone, follow);
            });
        }

        // From the worker. What happened goes to the activity list whether or
        // not the window is still open; the window, if it is, is put back.
        void Report(LaunchRequest r, List<LaunchResult> results, bool alone, bool follow)
        {
            int started = 0;
            string firstProblem = null;
            List<string> following = new List<string>();
            foreach (LaunchResult res in results)
            {
                if (res.Pid > 0) { host.Launched(res.Pid, res.Account); started++; }
                if (res.Problem != null) { host.Log(Sentence(res.Problem)); if (firstProblem == null) firstProblem = res.Problem; }
                else if (res.Said != null) host.Log(res.Said);
                if (res.Problem == null && res.JobId != null) following.Add(res.Account);
            }
            if (!alone) host.Log("Launched " + started + " of " + results.Count + " selected account(s).");

            if (follow && following.Count > 0)
            {
                FollowRequest f = new FollowRequest();
                f.Player = r.Player;
                f.PlayerId = r.PlayerId;
                f.Accounts.AddRange(following);
                host.Follow(f);
            }

            OnWindow(delegate
            {
                busy = false;
                UpdateSelectedCount();
                UpdateFollowing();
                RefreshPlaying();
                if (alone && firstProblem != null)
                    MessageBox.Show(this, Sentence(firstProblem), "Couldn't launch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        // The browser's own Play, for the account it is signed in as.
        void StartFromBrowser(string launchUrl, string account, int runningPid)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                LiveHopWorld world = new LiveHopWorld();
                if (runningPid > 0) world.Close(runningPid);
                string why;
                int pid = world.Start(RobloxAuth.WithTracker(launchUrl, world.DeviceTracker()), out why);
                if (pid > 0)
                {
                    host.Launched(pid, account);
                    host.Log(runningPid > 0 ? "Moved " + account + " - its old client closed first." : "Launched " + account + ".");
                }
                else host.Log("Couldn't launch " + account + ": " + (why ?? "no reason given") + ".");
                OnWindow(delegate { RefreshPlaying(); });
            });
        }

        void Say(string text, bool warn)
        {
            selectedCount.Text = text;
            selectedCount.ForeColor = warn ? Theme.Amber : Theme.Muted;
            if (!warn) return;
            // Back to the count shortly, unless a launch is using the line.
            System.Windows.Forms.Timer back = new System.Windows.Forms.Timer();
            back.Interval = 3000;
            back.Tick += delegate { back.Stop(); back.Dispose(); selectedCount.ForeColor = Theme.Muted; UpdateSelectedCount(); };
            back.Start();
        }

        void OnWindow(MethodInvoker a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); }
            catch { }   // closed meanwhile
        }

        static string Sentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            return s.EndsWith(".") ? s : s + ".";
        }

        // Retries the rename while WebView2 lets go of the folder. Returns
        // where the profile actually is afterwards.
        static string MoveProfile(string tempDir, string finalDir)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    if (string.Equals(tempDir, finalDir, StringComparison.OrdinalIgnoreCase)) return finalDir;
                    if (Directory.Exists(finalDir)) Directory.Delete(finalDir, true);
                    Directory.Move(tempDir, finalDir);
                    return finalDir;
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
            return tempDir;   // still in use - remember where the session really is
        }

        // Profile folders left by a rename that failed for an account that was
        // then abandoned. Anything an account is using is never touched.
        void TidyOrphanProfiles()
        {
            try
            {
                if (!Directory.Exists(ProfilesRoot)) return;

                List<string> folders = new List<string>();
                foreach (string d in Directory.GetDirectories(ProfilesRoot))
                    folders.Add(Path.GetFileName(d));

                List<string> inUse = new List<string>();
                foreach (RobloxAccount a in store.Accounts)
                    inUse.Add(Path.GetFileName(AccountStore.ProfilePathFor(a, ProfilesRoot)));

                foreach (string orphan in AccountStore.OrphanProfiles(folders, inUse))
                    TryDelete(Path.Combine(ProfilesRoot, orphan));
            }
            catch { }
        }

        static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }

        // WinForms has no InputBox. Returns null when cancelled, so "cleared to
        // empty" and "left alone" stay distinguishable.
        string Prompt(string question, string initial)
        {
            TextBox t;
            using (Form f = MakePrompt(question, initial, out t))
                return f.ShowDialog(this) == DialogResult.OK ? t.Text.Trim() : null;
        }

        // Like every other small window: no taskbar button of its own - it
        // had one, with WinForms' default icon on it.
        public static Form MakePrompt(string question, string initial, out TextBox t)
        {
            Form f = new Form();
            f.FormBorderStyle = FormBorderStyle.None;
            f.ShowInTaskbar = false;
            Ui.GiveAppIcon(f);
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(420, 140);
            f.BackColor = Theme.Card;

            Label q = Ui.MutedLabel(question, 16, 18, 8.25f);
            q.MaximumSize = new Size(388, 0);
            f.Controls.Add(q);

            TextBox box = new TextBox();
            box.Location = new Point(16, 68);
            box.Size = new Size(388, 22);
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Theme.Inset;
            box.ForeColor = Theme.Text;
            box.Text = initial ?? "";
            f.Controls.Add(box);
            t = box;

            Button ok = Ui.AccentButton("OK", 224, 102, 90, 26);
            ok.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            ok.Click += delegate { f.DialogResult = DialogResult.OK; f.Close(); };
            f.Controls.Add(ok);
            f.AcceptButton = ok;

            Button cancelBtn = Ui.PlainButton("Cancel", 314, 102, 90, 26);
            cancelBtn.Click += delegate { f.DialogResult = DialogResult.Cancel; f.Close(); };
            f.Controls.Add(cancelBtn);
            f.CancelButton = cancelBtn;
            return f;
        }
    }
}
