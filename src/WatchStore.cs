using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RobloxKeeper
{
    // A named set of watchers - one for each game, say - so everything being
    // watched changes in one go instead of watcher by watcher.
    //
    // Only watchers. Boxes already belong to the game they were drawn on, so
    // every setup finds the right ones; and the Discord webhook is shared,
    // with a watcher's own link for anything that should go elsewhere.
    class WatchSetup
    {
        public string Name;
        public readonly List<Watcher> Watchers = new List<Watcher>();

        public WatchSetup(string name) { Name = name; }
    }

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

        // What the one setup everybody starts with is called, and where the
        // watchers in a file from before setups go.
        public const string FirstSetupName = "Main";

        // Long enough for a game's name, short enough for the list it is
        // picked from.
        public const int MaxSetupName = 40;

        readonly string path;
        readonly List<WatchSetup> setups = new List<WatchSetup>();
        readonly List<WatchRegion> regions = new List<WatchRegion>();
        readonly List<Macro> macros = new List<Macro>();
        WatchSetup chosen;

        // Separates the fields of one macro step, inside the list of steps.
        // Like LIST, a character no keyboard produces.
        static readonly char STEP_FIELD = (char)0x1E;

        public string WebhookUrl = "";

        public WatchStore(string path)
        {
            this.path = path;
            chosen = new WatchSetup(FirstSetupName);
            setups.Add(chosen);
        }

        // The chosen setup's watchers. Everything that shows, edits or runs
        // watchers goes through this, so choosing another setup changes all
        // of it at once.
        public IList<Watcher> Watchers { get { return chosen.Watchers; } }
        public IList<WatchRegion> Regions { get { return regions; } }

        // Never empty. Changed only through the methods below, which keep it
        // that way.
        public IList<WatchSetup> Setups { get { return setups.AsReadOnly(); } }
        public WatchSetup Chosen { get { return chosen; } }

        // Not per setup: a setup chooses which watchers run, and any of them
        // can name any macro.
        public IList<Macro> Macros { get { return macros; } }

        // Every watcher, in every setup, that plays the macro called `was`
        // is pointed at `now` instead - null for a macro that was removed.
        // Replaced by copies, never changed in place: the watch thread may be
        // reading the originals. Returns how many.
        public int RenameMacroUses(string was, string now)
        {
            int n = 0;
            foreach (WatchSetup s in setups)
                for (int i = 0; i < s.Watchers.Count; i++)
                {
                    Watcher w = s.Watchers[i];
                    if (!string.Equals(w.ThenMacro, was, StringComparison.OrdinalIgnoreCase)) continue;
                    Watcher copy = DeserializeWatcher(SerializeWatcher(w));
                    copy.ThenMacro = now;
                    s.Watchers[i] = copy;
                    n++;
                }
            return n;
        }

        public Macro FindMacro(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Macro m in macros)
                if (string.Equals(m.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        // ---------- setups ----------

        public WatchSetup FindSetup(string name)
        {
            if (name == null) return null;
            string n = name.Trim();
            foreach (WatchSetup s in setups)
                if (string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        public bool Choose(string name)
        {
            WatchSetup s = FindSetup(name);
            if (s == null) return false;
            chosen = s;
            return true;
        }

        // Null when the name will do. Setups are picked by name, so two with
        // one name could not be told apart.
        public string SetupNameProblem(string name, WatchSetup renaming)
        {
            string n = name == null ? "" : name.Trim();
            if (n.Length == 0) return "Give the setup a name.";
            if (n.Length > MaxSetupName) return "Keep the name to " + MaxSetupName + " characters or fewer.";
            WatchSetup same = FindSetup(n);
            if (same != null && !ReferenceEquals(same, renaming))
                return "There's already a setup called \"" + same.Name + "\".";
            return null;
        }

        // A new setup, chosen straight away, because the next thing anyone
        // does with one is fill it in. A copy starts with copies of the chosen
        // setup's watchers, so changing it leaves the original alone. Null if
        // the name will not do.
        public WatchSetup AddSetup(string name, bool copyChosen)
        {
            if (SetupNameProblem(name, null) != null) return null;
            WatchSetup s = new WatchSetup(name.Trim());
            if (copyChosen)
                foreach (Watcher w in chosen.Watchers)
                    s.Watchers.Add(DeserializeWatcher(SerializeWatcher(w)));
            setups.Add(s);
            chosen = s;
            return s;
        }

        public bool RenameSetup(WatchSetup s, string name)
        {
            if (s == null || !setups.Contains(s) || SetupNameProblem(name, s) != null) return false;
            s.Name = name.Trim();
            return true;
        }

        // Never the last one. Removing the one in use puts the one before it
        // in use, so there is never none.
        public bool RemoveSetup(WatchSetup s)
        {
            int i = setups.IndexOf(s);
            if (i < 0 || setups.Count == 1) return false;
            setups.RemoveAt(i);
            if (ReferenceEquals(s, chosen)) chosen = setups[Math.Max(0, i - 1)];
            return true;
        }

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
            return FindIn(regions, placeId, name);
        }

        // The same lookup over any list, so the watch thread can search a
        // snapshot rather than the list the UI is editing.
        public static WatchRegion FindIn(IEnumerable<WatchRegion> list, string placeId, string name)
        {
            if (list == null) return null;
            foreach (WatchRegion r in list)
                if (r != null &&
                    string.Equals(r.PlaceId, placeId, StringComparison.Ordinal) &&
                    string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        // Where a picture is kept. Stored as a bare name so the folder can be
        // opened and looked at; a full path is used as it is.
        public static string TemplatePath(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            return Path.IsPathRooted(file) ? file : Path.Combine(TemplatesDir, file);
        }

        public IList<WatchRegion> RegionsFor(string placeId)
        {
            List<WatchRegion> found = new List<WatchRegion>();
            foreach (WatchRegion r in regions)
                if (string.Equals(r.PlaceId, placeId, StringComparison.Ordinal)) found.Add(r);
            return found;
        }

        // ---------- file ----------

        // A setup line starts a setup, and the watcher lines after it are its
        // watchers; one more line says which setup was in use.
        public void Load()
        {
            setups.Clear();
            regions.Clear();
            macros.Clear();
            WebhookUrl = "";
            string wanted = null;

            try
            {
                if (!File.Exists(path)) return;
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null,
                                                       DataProtectionScope.CurrentUser);
                WatchSetup filling = null;
                foreach (string line in Encoding.UTF8.GetString(plain).Split('\n'))
                {
                    string row = line.TrimEnd('\r');
                    if (row.Length < 2) continue;

                    string kind = row.Substring(0, 2);
                    string rest = row.Substring(2);
                    if (kind == "W|")
                    {
                        Watcher w = DeserializeWatcher(rest);
                        if (w == null) continue;
                        // A file from before setups has watchers and no setup
                        // line. They all belong to the first one.
                        if (filling == null)
                        {
                            filling = new WatchSetup(FirstSetupName);
                            setups.Add(filling);
                        }
                        filling.Watchers.Add(w);
                    }
                    else if (kind == "S|")
                    {
                        filling = new WatchSetup(Unescape(rest));
                        setups.Add(filling);
                    }
                    else if (kind == "C|") wanted = Unescape(rest);
                    else if (kind == "M|")
                    {
                        Macro m = DeserializeMacro(rest);
                        if (m != null) macros.Add(m);
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
            finally
            {
                if (setups.Count == 0) setups.Add(new WatchSetup(FirstSetupName));
                chosen = FindSetup(wanted) ?? setups[0];
            }
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(WebhookUrl))
                sb.Append("U|").Append(Escape(WebhookUrl)).Append('\n');
            foreach (WatchRegion r in regions) sb.Append("R|").Append(SerializeRegion(r)).Append('\n');
            foreach (Macro m in macros) sb.Append("M|").Append(SerializeMacro(m)).Append('\n');
            sb.Append("C|").Append(Escape(chosen.Name)).Append('\n');
            foreach (WatchSetup s in setups)
            {
                sb.Append("S|").Append(Escape(s.Name)).Append('\n');
                foreach (Watcher w in s.Watchers) sb.Append("W|").Append(SerializeWatcher(w)).Append('\n');
            }

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
            sb.Append(Escape(w.WebhookUrl)).Append(FIELD);
            sb.Append(Escape(w.ThenMacro)).Append(FIELD);
            sb.Append(w.MacroGapSeconds);
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
            if (f.Length > 20) w.ThenMacro = Blank(Unescape(f[20]));
            if (f.Length > 21) w.MacroGapSeconds = ParseInt(f[21], 15);
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

        // A name, then the steps: each step's fields joined by STEP_FIELD, the
        // steps joined by LIST, the whole escaped as one field.
        public static string SerializeMacro(Macro m)
        {
            List<string> steps = new List<string>();
            foreach (MacroStep s in m.Steps)
                steps.Add(string.Join(STEP_FIELD.ToString(), new string[] {
                    ((int)s.Kind).ToString(CultureInfo.InvariantCulture),
                    s.Vk.ToString(CultureInfo.InvariantCulture),
                    s.HoldMs.ToString(CultureInfo.InvariantCulture),
                    s.X.ToString("R", CultureInfo.InvariantCulture),
                    s.Y.ToString("R", CultureInfo.InvariantCulture),
                    s.RightButton ? "1" : "0",
                    s.Ms.ToString(CultureInfo.InvariantCulture),
                    s.Text ?? "" }));
            return Escape(m.Name) + FIELD + Escape(Join(steps.ToArray()));
        }

        public static Macro DeserializeMacro(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string[] f = line.Split(FIELD);
            Macro m = new Macro();
            m.Name = Unescape(f[0]);
            if (f.Length < 2) return m;

            foreach (string one in Split(Unescape(f[1])))
            {
                string[] p = one.Split(STEP_FIELD);
                if (p.Length < 8) continue;         // not a step we wrote
                MacroStep s = new MacroStep();
                s.Kind = (MacroStepKind)ParseInt(p[0], 0);
                s.Vk = (byte)ParseInt(p[1], 0);
                s.HoldMs = ParseInt(p[2], 50);
                s.X = ParseDouble(p[3], 0);
                s.Y = ParseDouble(p[4], 0);
                s.RightButton = p[5] == "1";
                s.Ms = ParseInt(p[6], 0);
                s.Text = p[7];
                m.Steps.Add(s);
            }
            return m;
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
