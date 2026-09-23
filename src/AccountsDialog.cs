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
    class AccountsDialog : Form
    {
        const int W = 700;
        const int ROW = 32;

        readonly AccountStore store;
        readonly Action<string> log;
        readonly Action<int, string> onLaunched;   // pid -> account, for client labelling

        ScrollPanel list;
        Label empty, selectedCount;
        TextBox gameBox;
        Button launchSelected;
        readonly Dictionary<string, ThemedCheckBox> picks =
            new Dictionary<string, ThemedCheckBox>(StringComparer.OrdinalIgnoreCase);

        public AccountsDialog(AccountStore store, Action<string> log, Action<int, string> onLaunched)
        {
            this.store = store;
            this.log = log;
            this.onLaunched = onLaunched;

            Text = "Accounts";
            // It has a taskbar button, and showed WinForms' default icon on it.
            Ui.GiveAppIcon(this);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(W, 500);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9f);

            Card card = new Card();
            card.Location = new Point(12, 12);
            card.Size = new Size(W - 24, 476);
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

            card.Controls.Add(Ui.MutedLabel(
                "Optional. Blank uses each account's own saved game - or use Browse to find one in Roblox itself.",
                Ui.PAD, gameRow + 30, 8.25f));

            const int selRow = 376;
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

            const int btnRow = 420;
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
            Rebuild();
        }

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
            list.Controls.Add(empty);
            empty.Visible = store.Accounts.Count == 0;

            int y = 4;
            foreach (RobloxAccount account in store.Accounts)
            {
                RobloxAccount a = account;   // captured per row, not per loop

                ThemedCheckBox pick = Ui.DarkCheck("", 4, y, 8.25f);
                Ui.CenterIn(pick, y, ROW);
                pick.CheckedChanged += delegate { UpdateSelectedCount(); };
                list.Controls.Add(pick);
                picks[a.Name] = pick;

                list.Controls.Add(Ui.RowLabel(a.Name, 28, y, ROW, 140, 9f, Theme.Text));

                // The note is the user's own description; the saved game is
                // shown only when there is no note to show instead.
                string second = !string.IsNullOrEmpty(a.Note)
                    ? a.Note
                    : (string.IsNullOrEmpty(a.GameUrl) ? "no game set" : "has a saved game");
                Label note = Ui.RowLabel(second, 172, y, ROW, 226, 8.25f, Theme.Muted);
                list.Controls.Add(note);

                LinkLabel play = Ui.RowLink("Play", 406, y, ROW);
                play.Click += delegate { LaunchOne(a, true); };
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
                b.LaunchRequested += delegate (string url) { StartClient(url, a.Name); };
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
            if (picked.Count == 0) return;

            int started = 0;
            foreach (RobloxAccount a in picked)
            {
                if (LaunchOne(a, false)) started++;

                // Staggered deliberately. Several clients starting at the same
                // instant race each other over the singleton and over Roblox's
                // launcher, and each needs its own ticket anyway.
                if (started > 0 && a != picked[picked.Count - 1]) Thread.Sleep(3000);
            }
            log("Launched " + started + " of " + picked.Count + " selected account(s).");
        }

        bool LaunchOne(RobloxAccount a, bool alone)
        {
            string link = !string.IsNullOrEmpty(gameBox.Text) ? gameBox.Text : a.GameUrl;
            string placeId = RobloxAuth.PlaceIdFromUrl(link);
            if (placeId == null)
            {
                string msg = "No game to launch " + a.Name + " into. Paste a game link, "
                           + "set one with Edit, or use Browse to pick one in Roblox.";
                log(msg);
                if (alone) MessageBox.Show(this, msg, "No game", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                string error;
                string ticket = RobloxAuth.RequestTicket(a.Cookie, out error);
                if (ticket == null)
                {
                    log("Could not launch " + a.Name + ": " + error);
                    if (alone) MessageBox.Show(this, "Could not launch " + a.Name + ":\r\n\r\n" + error,
                        "Launch failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                string url = RobloxAuth.BuildLaunchUrl(ticket, placeId, a.BrowserTrackerId, RobloxAuth.NowMs());
                return StartClient(url, a.Name);
            }
            finally { Cursor = Cursors.Default; }
        }

        // Starts the client and reports which account it belongs to, so the
        // Clients list can name it instead of numbering it.
        bool StartClient(string launchUrl, string accountName)
        {
            try
            {
                string version = RobloxInstall.NewestInstalledVersion();
                if (version == null)
                {
                    log("Could not launch " + accountName + ": no installed Roblox client found.");
                    return false;
                }

                string exe = Path.Combine(RobloxInstall.VersionsRoot, version, "RobloxPlayerBeta.exe");
                Process p = Process.Start(new ProcessStartInfo(exe, launchUrl) { UseShellExecute = false });
                if (p != null && onLaunched != null) onLaunched(p.Id, accountName);

                log("Launched " + accountName + ".");
                return true;
            }
            catch (Exception ex)
            {
                log("Could not launch " + accountName + ": " + ex.Message);
                return false;
            }
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
