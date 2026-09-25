using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Self-update against the GitHub releases API.
    class Updater
    {
        readonly Form owner;
        readonly Action<string> log;

        public Updater(Form owner, Action<string> log)
        {
            this.owner = owner;
            this.log = log;
        }

        static string JsonString(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        // A release's download ending in suffix: ".exe" for the app, ".sha256"
        // for the checksum the release workflow publishes beside it.
        public static string FindAsset(string json, string suffix)
        {
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
            {
                string u = m.Groups[1].Value;
                if (u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return u;
            }
            return null;
        }

        // The hash in a checksum file - "<sha256>  RobloxKeeper.exe" - or
        // null when it isn't one.
        public static string HashIn(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Match m = Regex.Match(text, @"\b([0-9a-fA-F]{64})\b");
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }

        static string HashOf(string path)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        static bool IsNewer(string remote, string local)
        {
            try
            {
                Version a = new Version(Normalise(remote));
                Version b = new Version(Normalise(local));
                return a > b;
            }
            catch { return false; }
        }

        static string Normalise(string v)
        {
            if (string.IsNullOrEmpty(v)) return "0.0";
            v = v.Trim().TrimStart('v', 'V').Split('-')[0].Trim();
            return v.IndexOf('.') < 0 ? v + ".0" : v;
        }

        // Checks GitHub for a newer release in the background. Anything that goes
        // wrong (no network, rate limit, odd response) is ignored silently - an
        // update check must never get in the way of running the app.
        public void CheckInBackground()
        {
            Thread t = new Thread(delegate()
            {
                string tag = null, url = null, sha = null;
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(AppInfo.RELEASE_API);
                    req.UserAgent = "RobloxKeeper";
                    req.Timeout = 15000;
                    string json;
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                        json = sr.ReadToEnd();
                    tag = JsonString(json, "tag_name");
                    url = FindAsset(json, ".exe");
                    sha = FindAsset(json, ".sha256");
                }
                catch { return; }

                if (tag == null || url == null || !IsNewer(tag, AppInfo.APP_VERSION)) return;
                string ver = Normalise(tag), link = url, check = sha;
                try { owner.BeginInvoke((MethodInvoker)delegate { Offer(ver, link, check); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void Offer(string version, string url, string shaUrl)
        {
            log("Version " + version + " is available (you have " + AppInfo.APP_VERSION + ").");
            if (MessageBox.Show(owner,
                    "RobloxKeeper " + version + " is available.\r\nYou are running " + AppInfo.APP_VERSION + ".\r\n\r\n" +
                    "Download it and restart now?",
                    "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            {
                log("Update skipped. It will be offered again next time you start.");
                return;
            }
            Install(version, url, shaUrl);
        }

        // Downloaded beside this exe, checked against the checksum the
        // release publishes, then swapped in by renaming - Windows allows a
        // running exe to be renamed - and started. The new copy waits for
        // this one to exit before it starts properly. No script is involved.
        void Install(string version, string url, string shaUrl)
        {
            string exe = Application.ExecutablePath;
            string staged = Path.Combine(Path.GetDirectoryName(exe),
                                         Path.GetFileNameWithoutExtension(exe) + ".new.exe");
            try
            {
                log("Downloading version " + version + "...");
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                string expected = null;
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "RobloxKeeper");
                    if (shaUrl != null) expected = HashIn(wc.DownloadString(shaUrl));
                    wc.Headers.Add("User-Agent", "RobloxKeeper");
                    wc.DownloadFile(url, staged);
                }
                if (!File.Exists(staged) || new FileInfo(staged).Length < 10000)
                {
                    log("Update download looked incomplete - keeping the current version.");
                    try { File.Delete(staged); } catch { }
                    return;
                }
                if (expected != null && HashOf(staged) != expected)
                {
                    log("The download didn't match the checksum published with the release - keeping the current version.");
                    try { File.Delete(staged); } catch { }
                    return;
                }

                string why = SelfSwap.Swap(exe, staged, new LiveFileOps());
                if (why != null)
                {
                    log("Update failed: " + why + " - you can download it from the GitHub releases page.");
                    try { if (File.Exists(staged)) File.Delete(staged); } catch { }
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo(exe, "--after " + Process.GetCurrentProcess().Id);
                psi.UseShellExecute = false;
                Process.Start(psi);

                log("Update ready - restarting into version " + version + ".");
                owner.Close();
            }
            catch (Exception ex)
            {
                log("Update failed: " + ex.Message + " - you can download it from the GitHub releases page.");
                try { if (File.Exists(staged)) File.Delete(staged); } catch { }
            }
        }
    }
}
