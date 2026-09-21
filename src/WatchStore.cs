using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RobloxKeeper
{
    // Watchers and regions on disk.
    //
    // Encrypted the same way the account list is, for the same reason: the
    // webhook URL contains a token, and anyone holding that token can post
    // into the channel. DPAPI at CurrentUser scope ties the key to this user
    // on this machine, so a copied file is useless anywhere else.
    //
    // The format is AccountStore's: one record a line, tab-separated fields,
    // short records tolerated so a file written by an older build still loads.
    class WatchStore
    {
        const char FIELD = '\t';

        // Separates words inside one field. A character no keyboard produces,
        // so a user's own text can never split it.
        const char LIST = '\u001f';

        readonly string path;
        readonly List<Watcher> watchers = new List<Watcher>();
        readonly List<WatchRegion> regions = new List<WatchRegion>();

        public string WebhookUrl = "";

        public WatchStore(string path) { this.path = path; }

        public IList<Watcher> Watchers { get { return watchers; } }
        public IList<WatchRegion> Regions { get { return regions; } }

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RobloxKeeper", "watchers.dat");
            }
        }

        // Ordinary png files, deliberately: being able to open the folder and
        // see what a watcher is looking for is worth more than tidiness.
        public static string TemplatesDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RobloxKeeper", "templates");
            }
        }

        public WatchRegion FindRegion(string placeId, string name)
        {
            foreach (WatchRegion r in regions)
                if (string.Equals(r.PlaceId, placeId, StringComparison.Ordinal) &&
                    string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        public IList<WatchRegion> RegionsFor(string placeId)
        {
            List<WatchRegion> found = new List<WatchRegion>();
            foreach (WatchRegion r in regions)
                if (string.Equals(r.PlaceId, placeId, StringComparison.Ordinal)) found.Add(r);
            return found;
        }

        // ---------- file ----------

        public void Load()
        {
            watchers.Clear();
            regions.Clear();
            WebhookUrl = "";

            try
            {
                if (!File.Exists(path)) return;
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null,
                                                       DataProtectionScope.CurrentUser);
                foreach (string line in Encoding.UTF8.GetString(plain).Split('\n'))
                {
                    string row = line.TrimEnd('\r');
                    if (row.Length < 2) continue;

                    string kind = row.Substring(0, 2);
                    string rest = row.Substring(2);
                    if (kind == "W|")
                    {
                        Watcher w = DeserializeWatcher(rest);
                        if (w != null) watchers.Add(w);
                    }
                    else if (kind == "R|")
                    {
                        WatchRegion r = DeserializeRegion(rest);
                        if (r != null) regions.Add(r);
                    }
                    else if (kind == "U|") WebhookUrl = Unescape(rest);
                }
            }
            catch
            {
                // A file written by another user, on another machine, or by a
                // build that changed the format. Starting empty is
                // recoverable; throwing during startup is not.
            }
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(WebhookUrl))
                sb.Append("U|").Append(Escape(WebhookUrl)).Append('\n');
            foreach (WatchRegion r in regions) sb.Append("R|").Append(SerializeRegion(r)).Append('\n');
            foreach (Watcher w in watchers) sb.Append("W|").Append(SerializeWatcher(w)).Append('\n');

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(sb.ToString()), null,
                                                      DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path, cipher);
            }
            catch { }   // a failed save is not worth taking the app down for
        }

        // ---------- records ----------

        public static string SerializeWatcher(Watcher w)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Escape(w.Name)).Append(FIELD);
            sb.Append((int)w.Kind).Append(FIELD);
            sb.Append(w.Enabled ? "1" : "0").Append(FIELD);
            sb.Append(Escape(w.RegionName)).Append(FIELD);
            sb.Append(Escape(Join(w.Accounts))).Append(FIELD);
            sb.Append(Escape(Join(w.Rule.Words))).Append(FIELD);
            sb.Append((int)w.Rule.Mode).Append(FIELD);
            sb.Append(w.Rule.IgnoreCase ? "1" : "0").Append(FIELD);
            sb.Append(w.Rule.WholeWordsOnly ? "1" : "0").Append(FIELD);
            sb.Append(Escape(w.ChatContains)).Append(FIELD);
            sb.Append(Escape(w.TemplateFile)).Append(FIELD);
            sb.Append(Escape(w.MaskFile)).Append(FIELD);
            sb.Append(w.Tolerance.ToString("R", CultureInfo.InvariantCulture)).Append(FIELD);
            sb.Append(w.ConfirmScans).Append(FIELD);
            sb.Append(w.CooldownSeconds).Append(FIELD);
            sb.Append(w.SendDiscord ? "1" : "0").Append(FIELD);
            sb.Append(w.ShowTray ? "1" : "0").Append(FIELD);
            sb.Append(w.PlaySound ? "1" : "0").Append(FIELD);
            sb.Append(w.WriteLog ? "1" : "0").Append(FIELD);
            sb.Append(Escape(w.WebhookUrl));
            return sb.ToString();
        }

        public static Watcher DeserializeWatcher(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] f = line.Split(FIELD);
            if (f.Length < 6) return null;      // not a record we wrote

            Watcher w = new Watcher();
            w.Name = Unescape(f[0]);
            w.Kind = (WatchKind)ParseInt(f[1], 0);
            w.Enabled = f[2] != "0";
            w.RegionName = Blank(Unescape(f[3]));
            w.Accounts = Split(Unescape(f[4]));
            w.Rule = new MatchRule();
            w.Rule.Words = Split(Unescape(f[5]));
            if (f.Length > 6) w.Rule.Mode = (MatchMode)ParseInt(f[6], 0);
            if (f.Length > 7) w.Rule.IgnoreCase = f[7] != "0";
            if (f.Length > 8) w.Rule.WholeWordsOnly = f[8] == "1";
            if (f.Length > 9) w.ChatContains = Blank(Unescape(f[9]));
            if (f.Length > 10) w.TemplateFile = Blank(Unescape(f[10]));
            if (f.Length > 11) w.MaskFile = Blank(Unescape(f[11]));
            if (f.Length > 12) w.Tolerance = ParseDouble(f[12], 0.82);
            if (f.Length > 13) w.ConfirmScans = ParseInt(f[13], 2);
            if (f.Length > 14) w.CooldownSeconds = ParseInt(f[14], 30);
            if (f.Length > 15) w.SendDiscord = f[15] != "0";
            if (f.Length > 16) w.ShowTray = f[16] != "0";
            if (f.Length > 17) w.PlaySound = f[17] == "1";
            if (f.Length > 18) w.WriteLog = f[18] != "0";
            if (f.Length > 19) w.WebhookUrl = Blank(Unescape(f[19]));
            return w;
        }

        public static string SerializeRegion(WatchRegion r)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Escape(r.Name)).Append(FIELD);
            sb.Append(Escape(r.PlaceId)).Append(FIELD);
            sb.Append((int)r.Anchor).Append(FIELD);
            sb.Append(r.OffsetX.ToString("R", CultureInfo.InvariantCulture)).Append(FIELD);
            sb.Append(r.OffsetY.ToString("R", CultureInfo.InvariantCulture)).Append(FIELD);
            sb.Append(r.SizeW.ToString("R", CultureInfo.InvariantCulture)).Append(FIELD);
            sb.Append(r.SizeH.ToString("R", CultureInfo.InvariantCulture)).Append(FIELD);
            sb.Append(r.DrawnWidth).Append(FIELD);
            sb.Append(r.DrawnHeight).Append(FIELD);
            sb.Append((int)r.Scaling);
            return sb.ToString();
        }

        public static WatchRegion DeserializeRegion(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] f = line.Split(FIELD);
            if (f.Length < 10) return null;

            WatchRegion r = new WatchRegion();
            r.Name = Unescape(f[0]);
            r.PlaceId = Unescape(f[1]);
            r.Anchor = (RegionAnchor)ParseInt(f[2], 0);
            r.OffsetX = ParseDouble(f[3], 0);
            r.OffsetY = ParseDouble(f[4], 0);
            r.SizeW = ParseDouble(f[5], 0);
            r.SizeH = ParseDouble(f[6], 0);
            r.DrawnWidth = ParseInt(f[7], 1);
            r.DrawnHeight = ParseInt(f[8], 1);
            r.Scaling = (RegionScaling)ParseInt(f[9], 0);
            return r;
        }

        // ---------- field helpers ----------

        static string Join(string[] items)
        {
            return items == null ? "" : string.Join(LIST.ToString(), items);
        }

        static string[] Split(string joined)
        {
            if (string.IsNullOrEmpty(joined)) return new string[0];
            return joined.Split(LIST);
        }

        static string Blank(string s) { return string.IsNullOrEmpty(s) ? null : s; }

        // Tabs and newlines are what separate fields and records, so any that
        // appear inside a value have to stop being those characters.
        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "\\t")
                    .Replace("\n", "\\n").Replace("\r", "\\r");
        }

        static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                i++;
                switch (s[i])
                {
                    case 't': sb.Append('\t'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append('\\').Append(s[i]); break;
                }
            }
            return sb.ToString();
        }

        // Always the invariant culture. On a machine whose locale writes 0,82
        // a stored tolerance would come back as 82 and every image watcher
        // would quietly stop matching anything.
        static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                ? v : fallback;
        }

        static double ParseDouble(string s, double fallback)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                ? v : fallback;
        }
    }
}
