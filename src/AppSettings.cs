using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper
{
    // Plain key=value text in %APPDATA%\RobloxKeeper\settings.txt. Unknown keys
    // are ignored and missing keys keep their default, so a settings file written
    // by an older or newer build always loads.
    class AppSettings
    {
        public bool Afk = true;
        public int IntervalMinutes = 15;
        public int KeysIndex = 1;
        public bool Multi = true;
        public bool AutoGhost = true;

        // Default resource profile handed to every client that launches.
        public int PerfPriority = PerformanceManager.PRIORITY_NORMAL;
        public int PerfCores;            // 0 = all cores
        public bool PerfEco;

        public bool AutoTrim;
        public int AutoTrimMinutes = 10;

        public static string Path
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RobloxKeeper", "settings.txt");
            }
        }

        public ClientProfile ToProfile()
        {
            ClientProfile p = new ClientProfile();
            p.Priority = PerfPriority;
            p.Cores = PerfCores;
            p.Eco = PerfEco;
            return p;
        }

        public void FromProfile(ClientProfile p)
        {
            PerfPriority = p.Priority;
            PerfCores = p.Cores;
            PerfEco = p.Eco;
        }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                if (!File.Exists(Path)) return s;
                foreach (string line in File.ReadAllLines(Path))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    int tmp;
                    if (key == "afk") s.Afk = val == "1";
                    else if (key == "interval") { if (int.TryParse(val, out tmp)) s.IntervalMinutes = tmp; }
                    else if (key == "keys") { if (int.TryParse(val, out tmp)) s.KeysIndex = tmp; }
                    else if (key == "multi") s.Multi = val == "1";
                    else if (key == "autoghost") s.AutoGhost = val == "1";
                    else if (key == "perfpriority") { if (int.TryParse(val, out tmp)) s.PerfPriority = tmp; }
                    else if (key == "perfcores") { if (int.TryParse(val, out tmp)) s.PerfCores = tmp; }
                    else if (key == "perfeco") s.PerfEco = val == "1";
                    else if (key == "autotrim") s.AutoTrim = val == "1";
                    else if (key == "autotrimmin") { if (int.TryParse(val, out tmp)) s.AutoTrimMinutes = tmp; }
                }
            }
            catch { }
            Clamp(s);
            return s;
        }

        // A hand-edited or truncated settings file must not be able to put the UI
        // into a state its controls can't represent.
        static void Clamp(AppSettings s)
        {
            if (s.IntervalMinutes < 1) s.IntervalMinutes = 1;
            if (s.IntervalMinutes > 19) s.IntervalMinutes = 19;
            if (s.PerfPriority < 0 || s.PerfPriority > PerformanceManager.PRIORITY_HIGH)
                s.PerfPriority = PerformanceManager.PRIORITY_NORMAL;
            if (s.PerfCores < 0) s.PerfCores = 0;
            if (s.PerfCores >= Environment.ProcessorCount) s.PerfCores = 0;
            if (s.AutoTrimMinutes < 1) s.AutoTrimMinutes = 1;
            if (s.AutoTrimMinutes > 120) s.AutoTrimMinutes = 120;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                List<string> lines = new List<string>();
                lines.Add("afk=" + (Afk ? "1" : "0"));
                lines.Add("interval=" + IntervalMinutes);
                lines.Add("keys=" + KeysIndex);
                lines.Add("multi=" + (Multi ? "1" : "0"));
                lines.Add("autoghost=" + (AutoGhost ? "1" : "0"));
                lines.Add("perfpriority=" + PerfPriority);
                lines.Add("perfcores=" + PerfCores);
                lines.Add("perfeco=" + (PerfEco ? "1" : "0"));
                lines.Add("autotrim=" + (AutoTrim ? "1" : "0"));
                lines.Add("autotrimmin=" + AutoTrimMinutes);
                File.WriteAllLines(Path, lines.ToArray());
            }
            catch { }
        }
    }
}
