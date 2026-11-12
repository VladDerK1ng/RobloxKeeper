using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RobloxKeeper
{
    // How one script is set to run. Kept in a small file beside it, so the
    // .lua file stays nothing but the code - editable in any editor.
    class ScriptSettings
    {
        // Where it plays when nothing else says: the Roblox window in front
        // as it starts, the whole screen, or else the name of an account,
        // whose window it plays on.
        public const string FRONT = "front";
        public const string SCREEN = "screen";

        public string PlaysOn = FRONT;
        public byte Key;                    // 0 for none; a press starts it, pressed again stops it
        public bool SummaryToDiscord;       // when a run ends, its summary goes to the watchers' webhook too

        // False for a script imported from someone else's .rkscript until
        // you have pressed "I trust this script": until then it doesn't
        // start. A script made here - and one saved before this was kept -
        // is trusted.
        public bool Trusted = true;

        public bool OnScreen { get { return string.Equals(PlaysOn, SCREEN, StringComparison.OrdinalIgnoreCase); } }
        public bool OnFront { get { return string.IsNullOrEmpty(PlaysOn) || string.Equals(PlaysOn, FRONT, StringComparison.OrdinalIgnoreCase); } }

        // The account named, or null for the window in front or the screen.
        public string Account { get { return OnScreen || OnFront ? null : PlaysOn; } }

        public ScriptSettings Copy()
        {
            ScriptSettings s = new ScriptSettings();
            s.PlaysOn = PlaysOn;
            s.Key = Key;
            s.SummaryToDiscord = SummaryToDiscord;
            s.Trusted = Trusted;
            return s;
        }

        public string ToJson()
        {
            return "{\"playsOn\":" + Json.Quote(string.IsNullOrEmpty(PlaysOn) ? FRONT : PlaysOn)
                 + ",\"key\":" + Key.ToString(CultureInfo.InvariantCulture)
                 + ",\"discord\":" + (SummaryToDiscord ? "true" : "false")
                 + ",\"trusted\":" + (Trusted ? "true" : "false") + "}";
        }

        // Anything missing or unreadable is the default, never a fault: the
        // file may have been edited by hand.
        public static ScriptSettings FromJson(string text)
        {
            ScriptSettings s = new ScriptSettings();
            object o = Json.Parse(text);
            if (o == null) return s;
            string on = Json.Str(o, "playsOn");
            if (!string.IsNullOrEmpty(on) && on.Trim().Length > 0) s.PlaysOn = on.Trim();
            long key = Json.Num(o, "key");
            if (key > 0 && key < 255) s.Key = (byte)key;
            s.SummaryToDiscord = Json.Flag(o, "discord");
            // Only a plain false takes trust away: a file from before it was
            // kept has none, and those scripts were made here.
            object trusted = Json.Get(o, "trusted");
            s.Trusted = !(trusted is bool) || (bool)trusted;
            return s;
        }
    }

    // The scripts: one .lua file each in a folder of their own beside the
    // watchers, with two small files beside each - how it is set to run, and
    // what it has remembered (remember/recall). The code is read from disk
    // each time it is asked for, because it may have been changed in another
    // editor; the settings are kept in memory as well, because every key the
    // app answers to is looked at once a second.
    //
    // Safe to use from several threads: a running script writes what it
    // remembers from its own.
    class ScriptStore
    {
        public const string EXT = ".lua";
        const string SETTINGS = ".settings.json";
        const string MEMORY = ".memory.json";

        // Long enough for any name worth typing, short enough for a list.
        public const int MAX_NAME = 60;

        // A script, its settings and what it remembers are each read whole;
        // anything bigger than this is not a script anyone typed.
        public const int MAX_CODE_BYTES = 1024 * 1024;
        const int MAX_MEMORY_BYTES = 256 * 1024;

        readonly string folder;
        readonly object gate = new object();
        // By name, as the file is called; filled the first time anything asks.
        Dictionary<string, ScriptSettings> settings;

        public ScriptStore(string folder)
        {
            this.folder = folder;
        }

        public string Folder { get { return folder; } }

        // Beside the watchers, as Quick Record's recording is.
        public static string DefaultDir
        {
            get { return Path.Combine(Path.GetDirectoryName(WatchStore.DefaultPath), "scripts"); }
        }

        public string PathOf(string name) { return Path.Combine(folder, name + EXT); }
        string SettingsPath(string name) { return Path.Combine(folder, name + SETTINGS); }
        string MemoryPath(string name) { return Path.Combine(folder, name + MEMORY); }

        // ---------- names ----------

        static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        // Null when a name can be a script's; otherwise why not, in a line
        // someone can act on. The name is the file's, so nothing that would
        // reach outside the folder or that Windows refuses is let through.
        public static string NameProblem(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return "Give the script a name.";
            if (name != name.Trim()) return "A name can't start or end with a space.";
            if (name.Length > MAX_NAME) return "Keep the name under " + MAX_NAME + " letters.";
            foreach (char c in name)
                if (c < ' ' || c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '"' ||
                    c == '<' || c == '>' || c == '|' || Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0)
                    return "A name can't have " + (c < ' ' ? "that character" : c.ToString()) + " in it.";
            if (name.EndsWith(".")) return "A name can't end with a dot.";
            if (name.StartsWith(".")) return "A name can't start with a dot.";
            string stem = name.Split('.')[0].Trim();
            foreach (string r in Reserved)
                if (string.Equals(stem, r, StringComparison.OrdinalIgnoreCase)) return "Windows keeps the name " + r + " for itself. Choose another.";
            return null;
        }

        // The same, and not taken by another script. renaming: the script
        // being renamed, whose own name is no clash.
        public string NameProblem(string name, string renaming)
        {
            string why = NameProblem(name);
            if (why != null) return why;
            string same = Find(name);
            if (same != null && !string.Equals(same, renaming, StringComparison.OrdinalIgnoreCase))
                return "There's already a script called \"" + same + "\".";
            return null;
        }

        // ---------- the list ----------

        // Every script, by name, in alphabetical order - read from the folder
        // each time, so one dropped in by hand shows up.
        public List<string> Names()
        {
            List<string> names = new List<string>();
            try
            {
                if (Directory.Exists(folder))
                    foreach (string path in Directory.GetFiles(folder, "*" + EXT))
                    {
                        // "*.lua" also matches "x.luax" on some file systems.
                        if (!path.EndsWith(EXT, StringComparison.OrdinalIgnoreCase)) continue;
                        string name = Path.GetFileNameWithoutExtension(path);
                        if (NameProblem(name) == null) names.Add(name);
                    }
            }
            catch { }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            lock (gate) settings = null;      // read afresh: one may have come or gone
            return names;
        }

        // The name as it is on disk, found whatever its case, or null.
        public string Find(string name)
        {
            if (NameProblem(name) != null) return null;
            foreach (string n in Names())
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        public bool Exists(string name) { return Find(name) != null; }

        // ---------- code ----------

        // The code, or null when there is no such script or it can't be read.
        public string Load(string name)
        {
            if (NameProblem(name) != null) return null;
            try
            {
                string path = PathOf(name);
                if (!File.Exists(path)) return null;
                if (new FileInfo(path).Length > MAX_CODE_BYTES) return null;
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch { return null; }
        }

        // False when it couldn't be written.
        public bool Save(string name, string code)
        {
            if (NameProblem(name) != null) return false;
            lock (gate)
                return WriteWhole(PathOf(name), code ?? "");
        }

        // A new script with this code. Null when it was made; otherwise why not.
        public string Create(string name, string code)
        {
            string why = NameProblem(name, null);
            if (why != null) return why;
            if (!Save(name, code)) return "Couldn't save the script in " + folder + ".";
            return null;
        }

        // Its settings and memory go with it. Null when renamed; otherwise
        // why not.
        public string Rename(string from, string to)
        {
            string was = Find(from);
            if (was == null) return "There's no script called \"" + from + "\".";
            string why = NameProblem(to, was);
            if (why != null) return why;
            lock (gate)
            {
                try
                {
                    // Only the case changing is the same file to Windows: moved
                    // through a name of its own first.
                    MoveFile(PathOf(was), PathOf(to));
                    if (File.Exists(SettingsPath(was))) MoveFile(SettingsPath(was), SettingsPath(to));
                    if (File.Exists(MemoryPath(was))) MoveFile(MemoryPath(was), MemoryPath(to));
                }
                catch { settings = null; return "Couldn't rename the script - is it open somewhere else?"; }
                settings = null;
            }
            return null;
        }

        static void MoveFile(string from, string to)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) && from != to)
            {
                string between = to + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.Move(from, between);
                File.Move(between, to);
                return;
            }
            File.Move(from, to);
        }

        // The script, its settings and what it remembered. False when there
        // was nothing to remove or it couldn't be.
        public bool Remove(string name)
        {
            string was = Find(name);
            if (was == null) return false;
            lock (gate)
            {
                try
                {
                    File.Delete(PathOf(was));
                    if (File.Exists(SettingsPath(was))) File.Delete(SettingsPath(was));
                    if (File.Exists(MemoryPath(was))) File.Delete(MemoryPath(was));
                }
                catch { settings = null; return false; }
                settings = null;
            }
            return true;
        }

        // ---------- settings ----------

        // A copy: change it, then SaveSettings to keep it. A script with no
        // settings file yet has the defaults.
        public ScriptSettings Settings(string name)
        {
            lock (gate)
            {
                ScriptSettings s;
                if (All().TryGetValue(name ?? "", out s)) return s.Copy();
                return new ScriptSettings();
            }
        }

        public bool SaveSettings(string name, ScriptSettings s)
        {
            if (NameProblem(name) != null || s == null) return false;
            lock (gate)
            {
                if (!WriteWhole(SettingsPath(name), s.ToJson())) return false;
                if (settings != null) settings[name] = s.Copy();
                return true;
            }
        }

        // Every script's own key, with whose it is - from memory, so asking
        // every second costs nothing.
        public List<KeyValuePair<string, byte>> Keys()
        {
            List<KeyValuePair<string, byte>> keys = new List<KeyValuePair<string, byte>>();
            lock (gate)
                foreach (KeyValuePair<string, ScriptSettings> p in All())
                    if (p.Value.Key != 0) keys.Add(new KeyValuePair<string, byte>(p.Key, p.Value.Key));
            keys.Sort(delegate(KeyValuePair<string, byte> a, KeyValuePair<string, byte> b)
            {
                return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });
            return keys;
        }

        // The script whose key this is, or null.
        public string OnKey(byte vk)
        {
            if (vk == 0) return null;
            foreach (KeyValuePair<string, byte> k in Keys()) if (k.Value == vk) return k.Key;
            return null;
        }

        // Under the lock.
        Dictionary<string, ScriptSettings> All()
        {
            if (settings != null) return settings;
            Dictionary<string, ScriptSettings> all = new Dictionary<string, ScriptSettings>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(folder))
                    foreach (string path in Directory.GetFiles(folder, "*" + EXT))
                    {
                        if (!path.EndsWith(EXT, StringComparison.OrdinalIgnoreCase)) continue;
                        string name = Path.GetFileNameWithoutExtension(path);
                        if (NameProblem(name) != null) continue;
                        all[name] = ScriptSettings.FromJson(ReadSmall(SettingsPath(name), 64 * 1024));
                    }
            }
            catch { }
            settings = all;
            return all;
        }

        // ---------- memory: remember and recall ----------

        // What a script remembered, by name: numbers (double), text (string)
        // and true/false (bool). Empty for none, or for a file that can't be
        // read - a script starting over is better than one that can't start.
        public Dictionary<string, object> Memory(string name)
        {
            Dictionary<string, object> kept = new Dictionary<string, object>(StringComparer.Ordinal);
            if (NameProblem(name) != null) return kept;
            object o;
            lock (gate) o = Json.Parse(ReadSmall(MemoryPath(name), MAX_MEMORY_BYTES));
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) return kept;
            foreach (KeyValuePair<string, object> p in d)
                if (p.Value is double || p.Value is string || p.Value is bool) kept[p.Key] = p.Value;
            return kept;
        }

        public bool SaveMemory(string name, IDictionary<string, object> kept)
        {
            if (NameProblem(name) != null || kept == null) return false;
            StringBuilder sb = new StringBuilder("{");
            bool first = true;
            List<string> keys = new List<string>(kept.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys)
            {
                object v = kept[k];
                string value;
                if (v is double)
                {
                    double d = (double)v;
                    // JSON has no infinity or not-a-number: kept as nothing.
                    if (double.IsNaN(d) || double.IsInfinity(d)) continue;
                    value = d.ToString("R", CultureInfo.InvariantCulture);
                }
                else if (v is bool) value = (bool)v ? "true" : "false";
                else if (v is string) value = Json.Quote((string)v);
                else continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Json.Quote(k)).Append(':').Append(value);
            }
            sb.Append('}');
            lock (gate) return WriteWhole(MemoryPath(name), sb.ToString());
        }

        // ---------- files ----------

        static string ReadSmall(string path, int max)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > max) return null;
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch { return null; }
        }

        // Written beside it first and moved into place, so a crash half-way
        // leaves the last good copy rather than half of this one.
        bool WriteWhole(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string temp = path + ".new";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                return true;
            }
            catch { return false; }
        }
    }
}
