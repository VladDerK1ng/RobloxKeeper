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

        // Wait for a lull before nudging, so a nudge never lands mid-match.
        public bool IdleOnly = true;

        // The Custom nudge key: a name from the offered list, or a raw
        // virtual-key captured straight from the keyboard.
        public string CustomKeyName;
        public byte CustomKeyVk;

        public bool ThrottleBackground;
        public bool MemoryCeiling;
        public int MemoryCeilingMb = 2000;

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
                    else if (key == "idleonly") s.IdleOnly = val == "1";
                    else if (key == "customkeyname") s.CustomKeyName = val;
                    else if (key == "customkeyvk") { if (int.TryParse(val, out tmp)) s.CustomKeyVk = (byte)tmp; }
                    else if (key == "throttlebg") s.ThrottleBackground = val == "1";
                    else if (key == "memceiling") s.MemoryCeiling = val == "1";
                    else if (key == "memceilingmb") { if (int.TryParse(val, out tmp)) s.MemoryCeilingMb = tmp; }
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
            // A hand-edited or stale settings file must never arm an unsafe
            // key or a method index that no longer exists.
            if (s.CustomKeyVk != 0 && !NudgeKeys.IsSafe(s.CustomKeyVk)) s.CustomKeyVk = 0;
            if (s.KeysIndex < 0 || s.KeysIndex >= NudgeMethod.Count) s.KeysIndex = NudgeMethod.CAMERA;
            if (s.MemoryCeilingMb < 256) s.MemoryCeilingMb = 256;
            if (s.MemoryCeilingMb > 16384) s.MemoryCeilingMb = 16384;
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
                lines.Add("idleonly=" + (IdleOnly ? "1" : "0"));
                lines.Add("customkeyname=" + (CustomKeyName ?? ""));
                lines.Add("customkeyvk=" + CustomKeyVk);
                lines.Add("throttlebg=" + (ThrottleBackground ? "1" : "0"));
                lines.Add("memceiling=" + (MemoryCeiling ? "1" : "0"));
                lines.Add("memceilingmb=" + MemoryCeilingMb);
                lines.Add("autotrim=" + (AutoTrim ? "1" : "0"));
                lines.Add("autotrimmin=" + AutoTrimMinutes);
                File.WriteAllLines(Path, lines.ToArray());
            }
            catch { }
        }
    }
}
