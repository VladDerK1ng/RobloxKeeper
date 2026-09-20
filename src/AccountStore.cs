using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RobloxKeeper
{
    // One saved Roblox account.
    //
    // The cookie is the account. A .ROBLOSECURITY value is a bearer token: hold
    // one and you ARE that user, with no password involved and no second
    // factor. Everything here is built around not leaking it.
    class RobloxAccount
    {
        public string Name;              // what to call it in the UI
        public string Cookie;            // .ROBLOSECURITY - never log, never display
        public string BrowserTrackerId;  // this account's own device identity
        public string GameUrl;           // the place it normally AFKs in
        public int NudgeMethod = -1;     // -1 = follow the global setting
        public int Priority = -1;        // -1 = follow the Performance defaults
        public int Cores;
        public bool Eco;

        public bool IsUsable { get { return !string.IsNullOrEmpty(Cookie); } }

        // Reaches logs, tooltips and exception messages, so it carries the name
        // and nothing else. There is no code path that prints a cookie.
        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) ? "(unnamed account)" : Name;
        }
    }

    // The account list, encrypted on disk.
    //
    // DPAPI at CurrentUser scope: Windows ties the key to this user on this
    // machine, so the file is useless if it is copied anywhere else - which is
    // the realistic threat, since a stolen accounts file would otherwise hand
    // over every account at once. Nothing is ever sent anywhere; the cookies
    // only travel to roblox.com, and only to request a launch ticket.
    class AccountStore
    {
        const char FIELD = '\t';

        readonly string path;
        readonly List<RobloxAccount> accounts = new List<RobloxAccount>();

        public AccountStore(string path) { this.path = path; }

        public IList<RobloxAccount> Accounts { get { return accounts; } }

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RobloxKeeper", "accounts.dat");
            }
        }

        public RobloxAccount Find(string name)
        {
            foreach (RobloxAccount a in accounts)
                if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        // Re-adding a name refreshes it rather than duplicating, because the
        // common case is signing the same account in again after its cookie
        // expired.
        public void Add(RobloxAccount account)
        {
            if (account == null || string.IsNullOrEmpty(account.Name)) return;
            RobloxAccount existing = Find(account.Name);
            if (existing != null) accounts.Remove(existing);
            accounts.Add(account);
        }

        public void Remove(string name)
        {
            RobloxAccount a = Find(name);
            if (a != null) accounts.Remove(a);
        }

        // Roblox's own browser tracker id is a run of digits. Each account gets
        // its own, so two accounts never present the same device identity -
        // the same problem the session lock exists to contain.
        public static string NewBrowserTrackerId()
        {
            byte[] b = new byte[6];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider()) rng.GetBytes(b);
            ulong n = 0;
            foreach (byte x in b) n = (n << 8) | x;
            return (100000000UL + (n % 899999999UL)).ToString();
        }

        // A directory name for this account's browser profile.
        //
        // Account names are user input and end up as a path, so anything that
        // could climb out of the profiles folder has to go. A hash is appended
        // so two names that sanitise to the same string still get their own
        // profile - sharing one would mean sharing a cookie jar, which is the
        // exact problem this whole feature exists to avoid.
        public static string SafeFolderName(string accountName)
        {
            if (accountName == null) accountName = "";

            StringBuilder sb = new StringBuilder();
            foreach (char c in accountName)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');

            string cleaned = sb.ToString().Trim('_');
            if (cleaned.Length > 32) cleaned = cleaned.Substring(0, 32);
            if (cleaned.Length == 0) cleaned = "account";

            uint hash = 2166136261;
            foreach (char c in accountName) { hash ^= c; hash *= 16777619; }
            return cleaned + "_" + hash.ToString("x8");
        }

        // ---------- serialisation ----------

        public static string Serialize(RobloxAccount a)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Escape(a.Name)).Append(FIELD);
            sb.Append(Escape(a.Cookie)).Append(FIELD);
            sb.Append(Escape(a.BrowserTrackerId)).Append(FIELD);
            sb.Append(Escape(a.GameUrl)).Append(FIELD);
            sb.Append(a.NudgeMethod).Append(FIELD);
            sb.Append(a.Priority).Append(FIELD);
            sb.Append(a.Cores).Append(FIELD);
            sb.Append(a.Eco ? "1" : "0");
            return sb.ToString();
        }

        public static RobloxAccount Deserialize(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] f = line.Split(FIELD);
            if (f.Length < 4) return null;          // not a record we wrote

            RobloxAccount a = new RobloxAccount();
            a.Name = Unescape(f[0]);
            a.Cookie = Unescape(f[1]);
            a.BrowserTrackerId = Unescape(f[2]);
            a.GameUrl = Unescape(f[3]);
            if (f.Length > 4) a.NudgeMethod = ParseInt(f[4], -1);
            if (f.Length > 5) a.Priority = ParseInt(f[5], -1);
            if (f.Length > 6) a.Cores = ParseInt(f[6], 0);
            if (f.Length > 7) a.Eco = f[7] == "1";
            return a;
        }

        static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        // A display name containing a tab or a newline must not be able to
        // corrupt the records after it.
        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char n = s[++i];
                if (n == 't') sb.Append('\t');
                else if (n == 'r') sb.Append('\r');
                else if (n == 'n') sb.Append('\n');
                else if (n == '\\') sb.Append('\\');
                else sb.Append(n);
            }
            return sb.ToString();
        }

        // ---------- disk ----------

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (RobloxAccount a in accounts) sb.AppendLine(Serialize(a));

                byte[] plain = Encoding.UTF8.GetBytes(sb.ToString());
                byte[] sealed_ = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                Array.Clear(plain, 0, plain.Length);

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, sealed_);
            }
            catch { }   // a failed save must never take the app down
        }

        public void Load()
        {
            accounts.Clear();
            try
            {
                if (!File.Exists(path)) return;
                byte[] sealed_ = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(sealed_, null, DataProtectionScope.CurrentUser);

                foreach (string line in Encoding.UTF8.GetString(plain).Split('\n'))
                {
                    RobloxAccount a = Deserialize(line.TrimEnd('\r'));
                    if (a != null && !string.IsNullOrEmpty(a.Name)) accounts.Add(a);
                }
                Array.Clear(plain, 0, plain.Length);
            }
            catch
            {
                // Copied from another machine, written by another user, or
                // simply damaged. Start empty rather than refusing to run.
                accounts.Clear();
            }
        }
    }
}
