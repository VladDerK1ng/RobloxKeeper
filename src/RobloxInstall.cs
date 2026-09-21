using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using Microsoft.Win32;

namespace RobloxKeeper
{
    // Everything the app knows about how Roblox is installed and registered on
    // this machine: version folders, the roblox-player protocol handler, desktop
    // shortcuts, and the third-party launchers that fight over all three.
    static class RobloxInstall
    {
        public static string VersionsRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "Versions");
            }
        }

        // ---------- Protocol registration ----------

        // How Windows launches Roblox when you press Play on the website.
        // A handler pointing at RobloxPlayerLauncher/Installer means the legacy
        // bootstrapper runs on every launch, and that closes running clients
        // regardless of who holds the singleton mutex.
        public static string RobloxLaunchCommand()
        {
            string[] roots = { "roblox-player", "roblox" };
            foreach (string proto in roots)
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                        "Software\\Classes\\" + proto + "\\shell\\open\\command"))
                    {
                        string v = k != null ? k.GetValue("") as string : null;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
                catch { }
                try
                {
                    using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(
                        proto + "\\shell\\open\\command"))
                    {
                        string v = k != null ? k.GetValue("") as string : null;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
                catch { }
            }
            return "(not registered)";
        }

        // The version folder the roblox-player protocol is currently registered to.
        public static string LaunchPathVersion()
        {
            string cmd = RobloxLaunchCommand();
            int i = cmd.IndexOf("version-", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "?";
            int end = cmd.IndexOfAny(new char[] { '\\', '/', '"' }, i);
            return end > i ? cmd.Substring(i, end - i) : cmd.Substring(i);
        }

        // Point the roblox-player protocol at a specific installed version.
        // Roblox does exactly this itself; doing it up-front means the next
        // launch already matches what that account wants, so Roblox has no
        // reason to run its installer - and the installer is what kills clients.
        public static bool SetRegisteredVersion(string versionFolder)
        {
            string exe = Path.Combine(VersionsRoot, versionFolder, "RobloxPlayerBeta.exe");
            if (!File.Exists(exe)) return false;
            string value = "\"" + exe + "\" %1";
            bool ok = false;
            foreach (string proto in new string[] { "roblox-player", "roblox" })
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(
                        "Software\\Classes\\" + proto + "\\shell\\open\\command"))
                    {
                        if (k != null) { k.SetValue("", value); ok = true; }
                    }
                }
                catch { }
            }
            return ok;
        }

        // The executable out of a registry command string, which is the path
        // followed by %1 - quoted if it contains spaces, which it always does
        // under AppData.
        public static string ExeFromCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;
            if (command == "(not registered)") return null;

            command = command.Trim();
            if (command.StartsWith("\""))
            {
                int close = command.IndexOf('"', 1);
                return close > 1 ? command.Substring(1, close - 1) : null;
            }

            int sp = command.IndexOf(' ');
            string path = sp < 0 ? command : command.Substring(0, sp);
            return path.Length == 0 ? null : path;
        }

        // Registered, but pointing at something that is not there.
        //
        // This is the failure that reads as "clients randomly close and Roblox
        // keeps updating": Windows runs a missing exe, Roblox's installer fires
        // to repair the install, and that installer closes every open client.
        // Nothing registered at all is a different problem, and deliberately
        // does not count here.
        public static bool HandlerTargetMissing(string command, Func<string, bool> fileExists)
        {
            string exe = ExeFromCommand(command);
            if (string.IsNullOrEmpty(exe)) return false;
            try { return !fileExists(exe); }
            catch { return false; }
        }

        public static bool HandlerTargetMissing()
        {
            return HandlerTargetMissing(RobloxLaunchCommand(), File.Exists);
        }

        // The version to repair a broken registration to: the most recently
        // installed one, because that is the one Roblox most recently decided
        // it wanted. Candidates come from InstalledVersionList, which already
        // excludes folders with no player executable.
        public static string NewestVersion(IList<string> versions, Func<string, DateTime> stamp)
        {
            string best = null;
            DateTime bestAt = DateTime.MinValue;
            foreach (string v in versions)
            {
                DateTime at;
                try { at = stamp(v); }
                catch { continue; }
                if (best == null || at > bestAt) { best = v; bestAt = at; }
            }
            return best;
        }

        public static string NewestInstalledVersion()
        {
            return NewestVersion(InstalledVersionList(), delegate(string v)
            {
                try { return File.GetLastWriteTimeUtc(Path.Combine(VersionsRoot, v, "RobloxPlayerBeta.exe")); }
                catch { return DateTime.MinValue; }
            });
        }

        // Which installed versions are safe to remove.
        //
        // Roblox keeps every version it has ever installed - six of them, about
        // 2 GB, on the machine this was written against. Deleting the wrong one
        // makes Roblox reinstall, and its installer closes every open client,
        // which is the most disruptive thing that can happen here. So three
        // separate things are protected: the version the launch handler points
        // at, any version a client is running right now, and the newest few,
        // because Roblox serves different accounts different versions and
        // throwing one away means downloading it again.
        public static IList<string> DeletableVersions(IList<string> installed, string registered,
                                                      IList<string> inUse, int keepNewest,
                                                      Func<string, DateTime> installedAt)
        {
            List<string> safe = new List<string>();
            if (installed == null || installed.Count == 0) return safe;

            // Never leave the machine with no Roblox at all.
            if (keepNewest < 1) keepNewest = 1;

            List<string> byAge = new List<string>(installed);
            byAge.Sort(delegate(string a, string b)
            {
                DateTime ta, tb;
                try { ta = installedAt(a); } catch { ta = DateTime.MinValue; }
                try { tb = installedAt(b); } catch { tb = DateTime.MinValue; }
                return tb.CompareTo(ta);          // newest first
            });

            for (int i = 0; i < byAge.Count; i++)
            {
                string v = byAge[i];
                if (i < keepNewest) continue;
                if (string.Equals(v, registered, StringComparison.OrdinalIgnoreCase)) continue;

                bool running = false;
                if (inUse != null)
                    foreach (string u in inUse)
                        if (string.Equals(u, v, StringComparison.OrdinalIgnoreCase)) { running = true; break; }
                if (running) continue;

                safe.Add(v);
            }
            return safe;
        }

        // The same question against this machine, with the versions any running
        // client is using worked out from the clients themselves.
        public static IList<string> DeletableVersions(int keepNewest, IList<ClientInfo> clients)
        {
            List<string> inUse = new List<string>();
            if (clients != null)
                foreach (ClientInfo ci in clients)
                {
                    string v = VersionOfPid(ci.Pid);
                    if (!string.IsNullOrEmpty(v) && v != "?") inUse.Add(v);
                }

            return DeletableVersions(InstalledVersionList(), LaunchPathVersion(), inUse, keepNewest,
                delegate(string v)
                {
                    try { return File.GetLastWriteTimeUtc(Path.Combine(VersionsRoot, v, "RobloxPlayerBeta.exe")); }
                    catch { return DateTime.MinValue; }
                });
        }

        public static long VersionSize(string version)
        {
            long total = 0;
            try
            {
                DirectoryInfo d = new DirectoryInfo(Path.Combine(VersionsRoot, version));
                if (!d.Exists) return 0;
                foreach (FileInfo f in d.GetFiles("*", SearchOption.AllDirectories)) total += f.Length;
            }
            catch { }
            return total;
        }

        public static bool DeleteVersion(string version)
        {
            try
            {
                string dir = Path.Combine(VersionsRoot, version);
                if (!Directory.Exists(dir)) return false;
                Directory.Delete(dir, true);
                return true;
            }
            catch { return false; }
        }

        public static bool UsesLegacyBootstrapper()
        {
            string cmd = RobloxLaunchCommand().ToLowerInvariant();
            return cmd.Contains("robloxplayerlauncher") || cmd.Contains("robloxplayerinstaller");
        }

        // ---------- Installed versions ----------

        public static List<string> InstalledVersionList()
        {
            List<string> list = new List<string>();
            try
            {
                string root = VersionsRoot;
                if (!Directory.Exists(root)) return list;
                foreach (string d in Directory.GetDirectories(root))
                    if (File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")))
                        list.Add(Path.GetFileName(d));
            }
            catch { }
            return list;
        }

        public static string InstalledVersions()
        {
            try
            {
                string root = VersionsRoot;
                if (!Directory.Exists(root)) return "(no Versions folder)";
                string[] dirs = Directory.GetDirectories(root);
                StringBuilder sb = new StringBuilder();
                sb.Append(dirs.Length).Append(" version folder(s)");
                DateTime newest = DateTime.MinValue;
                string newestName = "?";
                foreach (string d in dirs)
                {
                    string exe = Path.Combine(d, "RobloxPlayerBeta.exe");
                    if (!File.Exists(exe)) continue;
                    DateTime t = File.GetLastWriteTime(exe);
                    if (t > newest) { newest = t; newestName = Path.GetFileName(d); }
                }
                sb.Append(", newest: ").Append(newestName);
                if (newest != DateTime.MinValue) sb.Append(" (").Append(newest.ToString("yyyy-MM-dd HH:mm")).Append(")");
                return sb.ToString();
            }
            catch { return "(unreadable)"; }
        }

        public static string AllVersionFolders()
        {
            try
            {
                string root = VersionsRoot;
                if (!Directory.Exists(root)) return "(no Versions folder)";
                StringBuilder sb = new StringBuilder();
                foreach (string d in Directory.GetDirectories(root))
                {
                    string exe = Path.Combine(d, "RobloxPlayerBeta.exe");
                    if (!File.Exists(exe)) continue;
                    if (sb.Length > 0) sb.Append("\r\n              ");
                    sb.Append(Path.GetFileName(d)).Append("  (")
                      .Append(File.GetLastWriteTime(exe).ToString("MM-dd HH:mm")).Append(")");
                }
                return sb.Length > 0 ? sb.ToString() : "(none with a client exe)";
            }
            catch { return "(unreadable)"; }
        }

        // "...\Versions\version-abc123\RobloxPlayerBeta.exe" -> "version-abc123"
        public static string VersionFolderOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            try
            {
                string dir = Path.GetFileName(Path.GetDirectoryName(path));
                return string.IsNullOrEmpty(dir) ? "?" : dir;
            }
            catch { return "?"; }
        }

        public static string VersionOfPid(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return VersionFolderOf(PathOf(p));
            }
            catch { return "?"; }
        }

        // ---------- Process inspection ----------

        public static string CommandLineOf(int pid)
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid))
                {
                    foreach (ManagementObject mo in s.Get())
                        return mo["CommandLine"] as string;
                }
            }
            catch { }
            return null;
        }

        // Everything after the executable path - for a browser launch this is the
        // roblox-player:// URL carrying the join ticket.
        public static string ArgsOf(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine)) return null;
            string rest;
            if (commandLine.StartsWith("\""))
            {
                int close = commandLine.IndexOf('"', 1);
                if (close < 0) return null;
                rest = commandLine.Substring(close + 1);
            }
            else
            {
                int sp = commandLine.IndexOf(' ');
                if (sp < 0) return null;
                rest = commandLine.Substring(sp);
            }
            rest = rest.Trim();
            return rest.Length == 0 ? null : rest;
        }

        public static string PathOf(Process p)
        {
            try { return p.MainModule.FileName; }
            catch { return "(path unavailable)"; }
        }

        // Who started this client. This is what identifies a third-party
        // launcher (Bloxstrap and friends) or a stale shortcut launching the
        // wrong installed version - the thing that triggers the repair loop.
        public static string ParentOf(int pid)
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = " + pid))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        object o = mo["ParentProcessId"];
                        if (o == null) continue;
                        int ppid = Convert.ToInt32(o);
                        try
                        {
                            using (Process pp = Process.GetProcessById(ppid))
                                return pp.ProcessName + " (PID " + ppid + ")";
                        }
                        catch { return "PID " + ppid + " (already exited)"; }
                    }
                }
            }
            catch { }
            return "(unknown)";
        }

        // Names + paths of any Roblox helper processes, so a shared log shows
        // exactly which one interfered rather than just "something did".
        public static string DescribeHelpers()
        {
            StringBuilder sb = new StringBuilder();
            string[] names = { "RobloxPlayerInstaller", "RobloxPlayerLauncher" };
            foreach (string n in names)
            {
                Process[] procs = Process.GetProcessesByName(n);
                foreach (Process p in procs)
                {
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(n).Append(" -> ").Append(PathOf(p));
                    p.Dispose();
                }
            }
            return sb.Length > 0 ? sb.ToString() : "(none)";
        }

        // ---------- Third-party launchers ----------

        // Known third-party Roblox launchers/bootstrappers. These install and
        // manage their own Roblox version and re-register the protocol, which
        // is a common cause of two versions fighting.
        // A leftover settings folder is NOT an installed launcher - reporting one
        // as active sends people hunting for software they already removed. Only
        // a live process, an executable, or an uninstall entry counts as active.
        public static string ThirdPartyLaunchers()
        {
            StringBuilder sb = new StringBuilder();
            string[] names = { "Bloxstrap", "Fishstrap", "Voidstrap", "Lunarstrap", "Roblox Account Manager" };
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            };

            List<string[]> uninstallers = FindLauncherUninstallers();

            foreach (string name in names)
            {
                bool running = false;
                try
                {
                    Process[] ps = Process.GetProcessesByName(name.Replace(" ", ""));
                    running = ps.Length > 0;
                    foreach (Process p in ps) p.Dispose();
                }
                catch { }

                bool hasExe = false, hasFolder = false;
                foreach (string root in roots)
                {
                    try
                    {
                        string dir = Path.Combine(root, name);
                        if (!Directory.Exists(dir)) continue;
                        hasFolder = true;
                        if (Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories).Length > 0)
                            hasExe = true;
                    }
                    catch { }
                }

                bool registered = false;
                foreach (string[] u in uninstallers)
                    if (u[0].ToLowerInvariant().Contains(name.ToLowerInvariant().Replace(" ", "")))
                        registered = true;

                if (!running && !hasExe && !hasFolder && !registered) continue;

                string state = running ? "RUNNING - this one is active"
                             : (hasExe || registered) ? "installed"
                             : "leftover settings only, not installed";
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(name).Append(" (").Append(state).Append(")");
            }
            return sb.Length > 0 ? sb.ToString() : "(none found)";
        }

        // Only launchers that are actually installed/running can cause the loop.
        public static bool HasActiveThirdPartyLauncher()
        {
            string s = ThirdPartyLaunchers();
            return s.Contains("RUNNING") || s.Contains("(installed)");
        }

        // Windows uninstall entries for third-party Roblox launchers, so the
        // one step the app cannot do for the user is at least one click away.
        public static List<string[]> FindLauncherUninstallers()
        {
            List<string[]> found = new List<string[]>();
            string[] needles = { "bloxstrap", "fishstrap", "voidstrap", "lunarstrap" };
            RegistryKey[] roots = { Registry.CurrentUser, Registry.LocalMachine };
            string[] paths =
            {
                "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
            };
            foreach (RegistryKey root in roots)
            {
                foreach (string p in paths)
                {
                    try
                    {
                        using (RegistryKey k = root.OpenSubKey(p))
                        {
                            if (k == null) continue;
                            foreach (string sub in k.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey s = k.OpenSubKey(sub))
                                    {
                                        if (s == null) continue;
                                        string name = s.GetValue("DisplayName") as string;
                                        string cmd = s.GetValue("UninstallString") as string;
                                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(cmd)) continue;
                                        string lower = name.ToLowerInvariant();
                                        foreach (string n in needles)
                                        {
                                            if (lower.Contains(n))
                                            {
                                                found.Add(new string[] { name, cmd });
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            return found;
        }

        // ---------- Shortcuts ----------

        // Roblox shortcuts point at a VERSIONED exe path. After an update the old
        // shortcut still launches the previous client, which then repairs itself
        // and closes every open client. Stale shortcuts are a prime trigger for
        // the ping-pong, and they survive uninstalling Roblox.
        public static List<string[]> FindRobloxShortcuts()
        {
            List<string[]> hits = new List<string[]>();
            List<string> dirs = new List<string>();
            try
            {
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar"));
            }
            catch { }

            object shell = null;
            Type shellType = null;
            try
            {
                shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return hits;
                shell = Activator.CreateInstance(shellType);
            }
            catch { return hits; }

            foreach (string dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                string[] files;
                try { files = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (string f in files)
                {
                    try
                    {
                        object lnk = shellType.InvokeMember("CreateShortcut",
                            System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { f });
                        string target = lnk.GetType().InvokeMember("TargetPath",
                            System.Reflection.BindingFlags.GetProperty, null, lnk, null) as string;
                        if (string.IsNullOrEmpty(target)) continue;
                        if (target.IndexOf("\\Roblox\\Versions\\", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        hits.Add(new string[] { f, target, VersionFolderOf(target) });
                    }
                    catch { }
                }
            }
            return hits;
        }

        public static string DescribeShortcuts()
        {
            List<string[]> sc = FindRobloxShortcuts();
            if (sc.Count == 0) return "(none pointing at a version folder)";
            string reg = LaunchPathVersion();
            StringBuilder sb = new StringBuilder();
            foreach (string[] s in sc)
            {
                if (sb.Length > 0) sb.Append("\r\n                  ");
                sb.Append(Path.GetFileName(s[0])).Append(" -> ").Append(s[2]);
                if (!string.Equals(s[2], reg, StringComparison.OrdinalIgnoreCase))
                    sb.Append("  <-- STALE (registered is ").Append(reg).Append(")");
            }
            return sb.ToString();
        }

        // Repoint shortcuts at the version that is actually installed/registered.
        public static int RetargetShortcuts(string keepVersion, string versionsRoot, Action<string> log)
        {
            if (string.IsNullOrEmpty(keepVersion)) return 0;
            string goodExe = Path.Combine(versionsRoot, keepVersion, "RobloxPlayerBeta.exe");
            if (!File.Exists(goodExe)) return 0;

            int fixedCount = 0;
            object shell = null;
            Type shellType = null;
            try
            {
                shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return 0;
                shell = Activator.CreateInstance(shellType);
            }
            catch { return 0; }

            foreach (string[] sc in FindRobloxShortcuts())
            {
                if (string.Equals(sc[2], keepVersion, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    object lnk = shellType.InvokeMember("CreateShortcut",
                        System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { sc[0] });
                    lnk.GetType().InvokeMember("TargetPath",
                        System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { goodExe });
                    lnk.GetType().InvokeMember("Save",
                        System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
                    fixedCount++;
                }
                catch (Exception ex)
                {
                    log("Could not repoint " + Path.GetFileName(sc[0]) + ": " + ex.Message);
                }
            }
            return fixedCount;
        }

        // ---------- Environment ----------

        // Environment.OSVersion is compatibility-shimmed for this framework target
        // and reports 6.2 on Windows 10/11, which is useless in a shared log.
        public static string OsDescription()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion"))
                {
                    if (k != null)
                    {
                        string name = k.GetValue("ProductName") as string;
                        string disp = k.GetValue("DisplayVersion") as string;
                        string build = k.GetValue("CurrentBuild") as string;
                        int b;
                        if (name != null && build != null && int.TryParse(build, out b) && b >= 22000)
                            name = name.Replace("Windows 10", "Windows 11");
                        return (name ?? "Windows") + (disp != null ? " " + disp : "") +
                               " (build " + (build ?? "?") + ")";
                    }
                }
            }
            catch { }
            return Environment.OSVersion.Version.ToString();
        }
    }
}
