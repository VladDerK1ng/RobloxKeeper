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

        static string FindExeAsset(string json)
        {
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
            {
                string u = m.Groups[1].Value;
                if (u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return u;
            }
            return null;
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
                string tag = null, url = null;
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
                    url = FindExeAsset(json);
                }
                catch { return; }

                if (tag == null || url == null || !IsNewer(tag, AppInfo.APP_VERSION)) return;
                string ver = Normalise(tag), link = url;
                try { owner.BeginInvoke((MethodInvoker)delegate { Offer(ver, link); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void Offer(string version, string url)
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
            Install(version, url);
        }

        // The running exe cannot overwrite itself, so a tiny script waits for this
        // process to exit, swaps the file, and starts the new one.
        void Install(string version, string url)
        {
            string exe = Application.ExecutablePath;
            string staged = exe + ".new";
            try
            {
                log("Downloading version " + version + "...");
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "RobloxKeeper");
                    wc.DownloadFile(url, staged);
                }
                if (!File.Exists(staged) || new FileInfo(staged).Length < 10000)
                {
                    log("Update download looked incomplete - keeping the current version.");
                    try { File.Delete(staged); } catch { }
                    return;
                }

                string bat = Path.Combine(Path.GetTempPath(), "RobloxKeeperUpdate.bat");
                string script =
                    "@echo off\r\n" +
                    ":wait\r\n" +
                    "tasklist /fi \"imagename eq RobloxKeeper.exe\" | find /i \"RobloxKeeper.exe\" >nul\r\n" +
                    "if not errorlevel 1 (\r\n" +
                    "  ping -n 2 127.0.0.1 >nul\r\n" +
                    "  goto wait\r\n" +
                    ")\r\n" +
                    "move /y \"" + staged + "\" \"" + exe + "\" >nul\r\n" +
                    "start \"\" \"" + exe + "\"\r\n" +
                    "del \"%~f0\"\r\n";
                File.WriteAllText(bat, script, Encoding.ASCII);

                ProcessStartInfo psi = new ProcessStartInfo(bat);
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = true;
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
