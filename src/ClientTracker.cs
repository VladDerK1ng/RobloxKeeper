using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RobloxKeeper
{
    struct ClientInfo
    {
        public int Pid;
        public IntPtr Hwnd;
        public DateTime Start;
        public long WorkingSet;
    }

    static class ClientTracker
    {
        public const string ROBLOX_PROCESS = "RobloxPlayerBeta";

        // How long a client must go WITHOUT A WINDOW before it counts as leaked.
        // This is not measured from the process start - see GhostWatch for why
        // that distinction matters more than anything else in this file.
        public const int GHOST_GRACE_SECONDS = 150;

        public static List<ClientInfo> GetClients(out int windowless)
        {
            Process[] procs = Process.GetProcessesByName(ROBLOX_PROCESS);
            try { return ClientsFrom(procs, out windowless); }
            finally { foreach (Process p in procs) p.Dispose(); }
        }

        // Reads clients out of an existing process list. The per-second loop takes
        // one system snapshot and reuses it for every check, instead of walking the
        // whole process table once per question.
        //
        // Window-less processes are only counted here, never judged. Whether one
        // is a launching client, a client mid-teleport, or a genuine leak cannot
        // be told from a single snapshot - it takes history, which GhostWatch
        // keeps.
        public static List<ClientInfo> ClientsFrom(IList<Process> procs, out int windowless)
        {
            List<ClientInfo> list = new List<ClientInfo>();
            windowless = 0;
            foreach (Process p in procs)
            {
                try
                {
                    if (!IsClient(p)) continue;
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        ClientInfo ci = new ClientInfo();
                        ci.Pid = p.Id;
                        ci.Hwnd = p.MainWindowHandle;
                        try { ci.Start = p.StartTime; } catch { ci.Start = DateTime.MinValue; }
                        try { ci.WorkingSet = p.WorkingSet64; } catch { ci.WorkingSet = 0; }
                        list.Add(ci);
                    }
                    else windowless++;
                }
                catch { }
            }
            list.Sort(delegate(ClientInfo a, ClientInfo b) { return a.Start.CompareTo(b.Start); });
            return list;
        }

        public static bool IsClient(Process p)
        {
            try { return string.Equals(p.ProcessName, ROBLOX_PROCESS, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static bool IsWindowless(Process p)
        {
            try { return IsClient(p) && p.MainWindowHandle == IntPtr.Zero; }
            catch { return false; }
        }

        public static List<Process> ByName(IList<Process> snapshot, string name)
        {
            List<Process> hits = new List<Process>();
            foreach (Process p in snapshot)
            {
                try { if (string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)) hits.Add(p); }
                catch { }
            }
            return hits;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "-";
            double gb = bytes / 1073741824.0;
            if (gb >= 1.0) return gb.ToString("0.0") + " GB";
            return (bytes / 1048576.0).ToString("0") + " MB";
        }
    }
}
