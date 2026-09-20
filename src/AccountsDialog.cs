using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // The account manager.
    //
    // Roblox's own account switcher holds five and makes you sign out to
    // change. This holds as many as you like, keeps each one's session in its
    // own browser profile, and launches any of them straight into a game
    // without touching the others.
    class AccountsDialog : Form
    {
        const int W = 620;
        const int ROW = 34;

        readonly AccountStore store;
        readonly Action<string> log;
        ScrollPanel list;
        Label empty;
        TextBox gameBox;

        public AccountsDialog(AccountStore store, Action<string> log)
        {
            this.store = store;
            this.log = log;

            Text = "Accounts";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(W, 430);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9f);

            Card card = new Card();
            card.Location = new Point(12, 12);
            card.Size = new Size(W - 24, 406);
            Controls.Add(card);

            card.Controls.Add(Ui.SectionTitle("ACCOUNTS"));
            card.Controls.Add(Ui.Subtitle("Each account keeps its own sign-in - no five-account limit, no signing out"));

            list = new ScrollPanel();
            list.Location = new Point(Ui.PAD, 54);
            list.Size = new Size(W - 24 - Ui.PAD * 2, 210);
            list.BackColor = Theme.Card;
            list.AutoScroll = true;
            card.Controls.Add(list);

            // Inside the list, not behind it: WinForms puts a later-added
            // control at the BACK of the z-order, so a sibling placed over the
            // panel would be invisible.
            empty = Ui.MutedLabel("No accounts yet - Add account opens Roblox's login page.", 6, 10, 9f);
            list.Controls.Add(empty);

            // Where a launch goes. Left blank, each account uses its own saved
            // game; this box overrides it for one launch.
            const int gameRow = 276;
            card.Controls.Add(Ui.RowLabel("Game link", Ui.PAD, gameRow, 26, 70, 8.25f, Theme.Muted));
            gameBox = new TextBox();
            gameBox.Location = new Point(92, gameRow + 3);
            gameBox.Size = new Size(W - 24 - 92 - Ui.PAD, 22);
            gameBox.BorderStyle = BorderStyle.FixedSingle;
            gameBox.BackColor = Theme.Inset;
            gameBox.ForeColor = Theme.Text;
            card.Controls.Add(gameBox);

            card.Controls.Add(Ui.MutedLabel(
                "Paste a roblox.com/games/... link or just the place id. Blank uses each account's saved game.",
                Ui.PAD, gameRow + 30, 8.25f));

            const int btnRow = 340;
            Button add = Ui.AccentButton("Add account", Ui.PAD, btnRow, 120, 28);
            add.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            add.Click += delegate { AddAccount(); };
            card.Controls.Add(add);

            Button close = Ui.AccentButton("Close", W - 24 - Ui.PAD - 90, btnRow, 90, 28);
            close.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            close.Click += delegate { Close(); };
            card.Controls.Add(close);

            Rebuild();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Color.FromArgb(58, 58, 80), 1f))
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        static string ProfileDir(string accountName)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RobloxKeeper", "profiles", AccountStore.SafeFolderName(accountName));
        }

        void Rebuild()
        {
            list.SuspendLayout();
            // The empty-state label lives in this panel too, so it is kept
            // rather than disposed along with the account rows.
            while (list.Controls.Count > 0)
            {
                Control c = list.Controls[0];
                list.Controls.Remove(c);
                if (!ReferenceEquals(c, empty)) c.Dispose();
            }
            list.Controls.Add(empty);

            empty.Visible = store.Accounts.Count == 0;

            int y = 4;
            foreach (RobloxAccount account in store.Accounts)
            {
                RobloxAccount a = account;   // captured per row, not per loop

                Label name = Ui.RowLabel(a.Name, 4, y, ROW, 200, 9f, Theme.Text);
                list.Controls.Add(name);

                string where = string.IsNullOrEmpty(a.GameUrl) ? "no game set" : "has a saved game";
                list.Controls.Add(Ui.RowLabel(where, 208, y, ROW, 110, 8.25f, Theme.Muted));

                LinkLabel launch = Ui.RowLink("Launch", 326, y + 9);
                launch.Click += delegate { Launch(a); };
                list.Controls.Add(launch);

                LinkLabel setGame = Ui.RowLink("Set game", 386, y + 9);
                setGame.Click += delegate { SetGame(a); };
                list.Controls.Add(setGame);

                LinkLabel remove = Ui.RowLink("Remove", 462, y + 9);
                remove.Click += delegate { RemoveAccount(a); };
                list.Controls.Add(remove);

                y += ROW;
            }
            list.ResumeLayout();
        }

        void AddAccount()
        {
            // Named by a placeholder first so the browser profile has somewhere
            // to live; renamed to the real account once Roblox tells us who it
            // is, and the profile folder moves with it.
            string temp = "new-" + DateTime.Now.Ticks;
            string tempDir = ProfileDir(temp);

            string cookie, detected;
            using (AccountLoginForm login = new AccountLoginForm(tempDir))
            {
                if (login.ShowDialog(this) != DialogResult.OK) { TryDelete(tempDir); return; }
                cookie = login.Cookie;
                detected = login.DetectedName;
            }

            if (string.IsNullOrEmpty(cookie)) { TryDelete(tempDir); return; }

            string name = detected;
            if (string.IsNullOrEmpty(name))
            {
                name = Prompt("Roblox didn't say which account that was. What should it be called?");
                if (string.IsNullOrEmpty(name)) { TryDelete(tempDir); return; }
            }

            // Move the profile to its permanent home so the sign-in persists.
            string finalDir = ProfileDir(name);
            try
            {
                if (Directory.Exists(finalDir)) Directory.Delete(finalDir, true);
                Directory.Move(tempDir, finalDir);
            }
            catch { /* keep the temp profile rather than losing the sign-in */ }

            RobloxAccount a = store.Find(name);
            bool isNew = a == null;
            if (isNew)
            {
                a = new RobloxAccount();
                a.Name = name;
                a.BrowserTrackerId = AccountStore.NewBrowserTrackerId();
            }
            a.Cookie = cookie;
            store.Add(a);
            store.Save();

            log(isNew ? "Account added: " + name + "." : "Account " + name + " signed in again.");
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
            TryDelete(ProfileDir(a.Name));
            log("Account removed: " + a.Name + ".");
            Rebuild();
        }

        void SetGame(RobloxAccount a)
        {
            string typed = gameBox.Text;
            if (string.IsNullOrEmpty(typed))
            {
                MessageBox.Show(this, "Put a game link in the box first, then click Set game.",
                    "No game link", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (RobloxAuth.PlaceIdFromUrl(typed) == null)
            {
                MessageBox.Show(this, "That doesn't look like a Roblox game link or place id.",
                    "Not a game link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            a.GameUrl = typed;
            store.Save();
            log(a.Name + " will launch into " + RobloxAuth.PlaceIdFromUrl(typed) + ".");
            Rebuild();
        }

        void Launch(RobloxAccount a)
        {
            string link = !string.IsNullOrEmpty(gameBox.Text) ? gameBox.Text : a.GameUrl;
            string placeId = RobloxAuth.PlaceIdFromUrl(link);
            if (placeId == null)
            {
                MessageBox.Show(this,
                    "No game to launch " + a.Name + " into.\r\n\r\nPaste a game link in the box, "
                    + "or use Set game to give this account one.",
                    "No game", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                string error;
                string ticket = RobloxAuth.RequestTicket(a.Cookie, out error);
                if (ticket == null)
                {
                    log("Could not launch " + a.Name + ": " + error);
                    MessageBox.Show(this, "Could not launch " + a.Name + ":\r\n\r\n" + error,
                        "Launch failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string url = RobloxAuth.BuildLaunchUrl(ticket, placeId, a.BrowserTrackerId, RobloxAuth.NowMs());
                string exe = RobloxInstall.NewestInstalledVersion();
                if (exe == null)
                {
                    log("Could not launch " + a.Name + ": no installed Roblox client found.");
                    return;
                }

                string path = Path.Combine(RobloxInstall.VersionsRoot, exe, "RobloxPlayerBeta.exe");
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(path, url) { UseShellExecute = false });

                log("Launched " + a.Name + " into place " + placeId + ".");
            }
            catch (Exception ex)
            {
                log("Could not launch " + a.Name + ": " + ex.Message);
            }
            finally { Cursor = Cursors.Default; }
        }

        static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }

        // A small themed replacement for InputBox, which WinForms has no
        // equivalent of.
        string Prompt(string question)
        {
            using (Form f = new Form())
            {
                f.FormBorderStyle = FormBorderStyle.None;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(380, 130);
                f.BackColor = Theme.Card;

                Label q = Ui.MutedLabel(question, 16, 18, 8.25f);
                q.MaximumSize = new Size(348, 0);
                f.Controls.Add(q);

                TextBox t = new TextBox();
                t.Location = new Point(16, 60);
                t.Size = new Size(348, 22);
                t.BorderStyle = BorderStyle.FixedSingle;
                t.BackColor = Theme.Inset;
                t.ForeColor = Theme.Text;
                f.Controls.Add(t);

                Button ok = Ui.AccentButton("OK", 274, 92, 90, 26);
                ok.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
                ok.Click += delegate { f.DialogResult = DialogResult.OK; f.Close(); };
                f.Controls.Add(ok);
                f.AcceptButton = ok;

                return f.ShowDialog(this) == DialogResult.OK ? t.Text.Trim() : null;
            }
        }
    }
}
