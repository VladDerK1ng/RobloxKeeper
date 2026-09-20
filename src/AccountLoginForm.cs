using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RobloxKeeper
{
    // Signing an account in, on Roblox's own login page.
    //
    // This is a real browser pointed at roblox.com - the user types their own
    // credentials into Roblox's page, solves Roblox's own CAPTCHA, and handles
    // their own two-factor prompt. Nothing here reads a password, automates a
    // login form, or works around a CAPTCHA. When Roblox hands the session
    // cookie to the browser, the app keeps a copy.
    //
    // Each account gets its own user-data folder, so each has its own cookie
    // jar and its own browser identity. Two accounts sharing one profile would
    // recreate the shared-session problem that gets clients evicted as
    // duplicate device logins.
    class AccountLoginForm : Form
    {
        const string LOGIN_URL = "https://www.roblox.com/login";

        readonly string profileDir;
        WebView2 web;
        Label status;
        Button save, cancel;

        public string Cookie { get; private set; }
        public string DetectedName { get; private set; }

        public AccountLoginForm(string profileDir)
        {
            this.profileDir = profileDir;

            Text = "Sign in to Roblox";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 760);
            MinimumSize = new Size(700, 520);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

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

            save = Ui.AccentButton("Save account", 0, 10, 130, 26);
            save.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            save.Enabled = false;
            save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            save.Click += delegate { Finish(true); };
            bar.Controls.Add(save);

            cancel = Ui.AccentButton("Cancel", 0, 10, 90, 26);
            cancel.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            bar.Controls.Add(cancel);

            bar.Resize += delegate { LayoutBar(bar); };
            LayoutBar(bar);

            web = new WebView2();
            web.Dock = DockStyle.Fill;
            Controls.Add(web);
            web.BringToFront();

            Shown += delegate { Start(); };
        }

        void LayoutBar(Panel bar)
        {
            cancel.Location = new Point(bar.Width - 104, 10);
            save.Location = new Point(bar.Width - 244, 10);
            status.Width = Math.Max(120, bar.Width - 260);
        }

        async void Start()
        {
            try
            {
                Directory.CreateDirectory(profileDir);

                // The profile folder is what keeps accounts apart. Passing null
                // for the browser executable lets WebView2 find the installed
                // Edge runtime.
                CoreWebView2Environment env =
                    await CoreWebView2Environment.CreateAsync(null, profileDir);
                await web.EnsureCoreWebView2Async(env);

                web.CoreWebView2.NavigationCompleted += delegate { CheckForSession(); };
                web.CoreWebView2.Navigate(LOGIN_URL);
                status.Text = "Sign in on Roblox's page. This window is only pointed at roblox.com.";
            }
            catch (Exception ex)
            {
                status.ForeColor = Theme.Amber;
                status.Text = "Could not start the browser: " + ex.Message;
            }
        }

        // Polls the profile's cookie jar after each navigation. Roblox sets
        // .ROBLOSECURITY once the sign-in (and any 2FA) actually completes, so
        // its presence is the signal that there is something worth saving.
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
                    save.Enabled = false;
                    return;
                }

                Cookie = found;
                save.Enabled = true;

                if (DetectedName == null)
                {
                    status.Text = "Signed in - checking which account...";
                    string cookie = found;
                    string name = await Task.Run(delegate { return RobloxAuth.GetUsername(cookie); });
                    DetectedName = name;
                }

                status.ForeColor = Theme.Text;
                status.Text = DetectedName != null
                    ? "Signed in as " + DetectedName + ". Click Save account to keep it."
                    : "Signed in. Click Save account to keep it.";
            }
            catch
            {
                // A navigation that races the window closing; the next one
                // will ask again.
            }
        }

        void Finish(bool ok)
        {
            if (string.IsNullOrEmpty(Cookie)) return;
            DialogResult = ok ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && web != null) { try { web.Dispose(); } catch { } web = null; }
            base.Dispose(disposing);
        }
    }
}
