using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RobloxKeeper
{
    // Roblox, in a browser that belongs to one account.
    //
    // Two jobs, same window. Signing an account in for the first time, and
    // afterwards browsing as that account - searching games, opening a friend's
    // profile, joining what they are playing - without needing a link pasted
    // from somewhere else.
    //
    // The user signs in on Roblox's own page, solves Roblox's own CAPTCHA and
    // handles their own 2FA. Nothing here reads a password, fills a login form
    // or works around a CAPTCHA.
    //
    // Each account has its own user-data folder, so each has its own cookie jar
    // and its own browser identity. Sharing one would recreate the problem that
    // gets clients evicted as duplicate device logins.
    class AccountBrowserForm : Form
    {
        const string LOGIN_URL = "https://www.roblox.com/login";
        const string HOME_URL = "https://www.roblox.com/home";
        const string GAMES_URL = "https://www.roblox.com/discover";

        readonly string profileDir;
        readonly bool loginMode;
        readonly string accountName;
        readonly string seedCookie;

        WebView2 web;
        Label status;
        Button save, cancel;
        Panel nav;

        public string Cookie { get; private set; }
        public string DetectedName { get; private set; }
        public string DetectedUserId { get; private set; }

        // Raised when Play is pressed on a page. The URL already carries a
        // launch ticket for whichever account this browser is signed in as.
        public event Action<string> LaunchRequested;

        public AccountBrowserForm(string profileDir, string accountName, bool loginMode)
            : this(profileDir, accountName, loginMode, null) { }

        public AccountBrowserForm(string profileDir, string accountName, bool loginMode, string seedCookie)
        {
            this.profileDir = profileDir;
            this.accountName = accountName;
            this.loginMode = loginMode;
            this.seedCookie = seedCookie;

            Text = loginMode ? "Sign in to Roblox" : "Roblox - " + accountName;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1100, 800);
            MinimumSize = new Size(760, 540);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildBottomBar();
            if (!loginMode) BuildNavBar();

            web = new WebView2();
            web.Dock = DockStyle.Fill;
            Controls.Add(web);
            web.BringToFront();

            Shown += delegate { Start(); };
        }

        void BuildBottomBar()
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 46;
            bar.BackColor = Theme.Card;
            Controls.Add(bar);

            status = new Label();
            status.AutoSize = false;
            status.Location = new Point(14, 0);
            status.Size = new Size(620, 46);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Theme.Muted;
            status.BackColor = Theme.Card;
            status.Text = "Starting the browser...";
            bar.Controls.Add(status);

            if (loginMode)
            {
                save = Ui.AccentButton("Save account", 0, 10, 130, 26);
                save.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
                save.Enabled = false;
                save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                save.Click += delegate { Finish(); };
                bar.Controls.Add(save);
            }

            cancel = Ui.AccentButton(loginMode ? "Cancel" : "Close", 0, 10, 90, 26);
            cancel.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            bar.Controls.Add(cancel);

            bar.Resize += delegate { LayoutBar(bar); };
            LayoutBar(bar);
        }

        void LayoutBar(Panel bar)
        {
            cancel.Location = new Point(bar.Width - 104, 10);
            if (save != null) save.Location = new Point(bar.Width - 244, 10);
            status.Width = Math.Max(120, bar.Width - (save != null ? 260 : 120));
        }

        void BuildNavBar()
        {
            nav = new Panel();
            nav.Dock = DockStyle.Top;
            nav.Height = 40;
            nav.BackColor = Theme.Card;
            Controls.Add(nav);

            AddNav("Back", 12, delegate { if (web.CanGoBack) web.GoBack(); });
            AddNav("Forward", 82, delegate { if (web.CanGoForward) web.GoForward(); });
            AddNav("Home", 162, delegate { Go(HOME_URL); });
            AddNav("Games", 232, delegate { Go(GAMES_URL); });

            // No caption here. The window title already says which account this
            // is, and the old line asserted it was not the user's main account
            // - which it may well be.
        }

        void AddNav(string text, int x, EventHandler onClick)
        {
            Button b = Ui.AccentButton(text, x, 7, text.Length > 5 ? 74 : 62, 26);
            b.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            b.Click += onClick;
            nav.Controls.Add(b);
        }

        void Go(string url)
        {
            try { if (web.CoreWebView2 != null) web.CoreWebView2.Navigate(url); }
            catch { }
        }

        async void Start()
        {
            try
            {
                Directory.CreateDirectory(profileDir);

                CoreWebView2Environment env =
                    await CoreWebView2Environment.CreateAsync(null, profileDir);
                await web.EnsureCoreWebView2Async(env);

                web.CoreWebView2.NavigationCompleted += delegate { CheckForSession(); };
                web.CoreWebView2.NavigationStarting += OnNavigationStarting;

                // Roblox opens some things in a popup; keep them in this window
                // so they stay inside this account's profile.
                web.CoreWebView2.NewWindowRequested += delegate (object s, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    e.Handled = true;
                    Go(e.Uri);
                };

                // Put the account's saved session into this profile before
                // loading anything.
                //
                // Without this, browsing depends on the profile folder still
                // holding the sign-in - and that folder gets renamed when an
                // account is added, a rename that can fail while WebView2's
                // processes still hold it open. Seeding makes the stored cookie
                // the source of truth, so Browse works even against a brand new
                // empty profile, and a rotated session heals itself.
                SeedSession();

                web.CoreWebView2.Navigate(loginMode ? LOGIN_URL : HOME_URL);
                status.Text = loginMode
                    ? "Sign in on Roblox's page. This window only goes to roblox.com."
                    : "Browsing as " + accountName + ".";
            }
            catch (Exception ex)
            {
                status.ForeColor = Theme.Amber;
                status.Text = "Could not start the browser: " + ex.Message;
            }
        }

        void SeedSession()
        {
            if (string.IsNullOrEmpty(seedCookie)) return;
            try
            {
                CoreWebView2Cookie c = web.CoreWebView2.CookieManager.CreateCookie(
                    ".ROBLOSECURITY", seedCookie, ".roblox.com", "/");
                c.IsSecure = true;
                c.IsHttpOnly = true;
                c.Expires = DateTime.UtcNow.AddYears(1);
                web.CoreWebView2.CookieManager.AddOrUpdateCookie(c);
            }
            catch
            {
                // Seeding is a convenience; the profile may already be signed
                // in, and if it is not the login page is a correct outcome.
            }
        }

        // Pressing Play navigates to roblox-player://... Handing that to Windows
        // would start a client against the shared cookie jar rather than this
        // account, so it is caught here and launched by the app instead.
        void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!RobloxAuth.IsLaunchUrl(e.Uri)) return;

            e.Cancel = true;
            if (loginMode)
            {
                status.ForeColor = Theme.Amber;
                status.Text = "Save the account first, then use Browse to join a game as it.";
                return;
            }

            status.ForeColor = Theme.Text;
            status.Text = "Launching " + accountName + " into that game...";
            if (LaunchRequested != null) LaunchRequested(e.Uri);
        }

        // Roblox rotates sessions, so the cookie seen here may be newer than
        // the stored one. Kept current in both modes.
        async void CheckForSession()
        {
            try
            {
                var cookies = await web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
                string found = null;
                foreach (CoreWebView2Cookie c in cookies)
                    if (c.Name == ".ROBLOSECURITY" && !string.IsNullOrEmpty(c.Value)) found = c.Value;

                if (string.IsNullOrEmpty(found))
                {
                    if (save != null) save.Enabled = false;
                    return;
                }

                Cookie = found;
                if (!loginMode) return;

                save.Enabled = true;
                if (DetectedName == null)
                {
                    status.Text = "Signed in - checking which account...";
                    string cookie = found;
                    string[] who = await Task.Run(delegate
                    {
                        string id;
                        string name = RobloxAuth.WhoIs(cookie, out id);
                        return new string[] { name, id };
                    });
                    DetectedName = who[0];
                    DetectedUserId = who[1];
                }

                status.ForeColor = Theme.Text;
                status.Text = DetectedName != null
                    ? "Signed in as " + DetectedName + ". Click Save account to keep it."
                    : "Signed in. Click Save account to keep it.";
            }
            catch
            {
                // A navigation racing the window closing; the next one asks again.
            }
        }

        void Finish()
        {
            if (string.IsNullOrEmpty(Cookie)) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && web != null) { try { web.Dispose(); } catch { } web = null; }
            base.Dispose(disposing);
        }
    }
}
