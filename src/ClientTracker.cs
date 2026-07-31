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

    // A window-less Roblox process is not automatically junk: every client is
    // window-less for the first seconds of its launch. Splitting the count keeps
    // "still starting" apart from "leaked", which is the difference between a
    // safe auto-clear and killing the client the user just opened.
    struct GhostCount
    {
        public int Stuck;
        public int Starting;
        public int Total { get { return Stuck + Starting; } }
    }

    static class ClientTracker
    {
        public const string ROBLOX_PROCESS = "RobloxPlayerBeta";

        // How long a client is allowed to sit window-less before it counts as
        // leaked. Roblox can take a while to draw its first frame on a cold disk,
        // so this is deliberately generous.
        public const int GHOST_GRACE_SECONDS = 150;

        public static List<ClientInfo> GetClients(out GhostCount ghosts)
        {
            Process[] procs = Process.GetProcessesByName(ROBLOX_PROCESS);
            try { return ClientsFrom(procs, out ghosts); }
            finally { foreach (Process p in procs) p.Dispose(); }
        }

        // Reads clients out of an existing process list. The per-second loop takes
        // one system snapshot and reuses it for every check, instead of walking the
        // whole process table once per question.
        public static List<ClientInfo> ClientsFrom(IList<Process> procs, out GhostCount ghosts)
        {
            List<ClientInfo> list = new List<ClientInfo>();
            ghosts = new GhostCount();
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
                    else if (OutlivedGrace(p)) ghosts.Stuck++;
                    else ghosts.Starting++;
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

        // A window-less client that has outlived the launch grace period. When the
        // start time can't be read the process is left alone - guessing here would
        // risk killing a healthy client.
        public static bool IsStuckGhost(Process p)
        {
            try
            {
                if (!IsClient(p)) return false;
                if (p.MainWindowHandle != IntPtr.Zero) return false;
                return OutlivedGrace(p);
            }
            catch { return false; }
        }

        public static bool IsWindowless(Process p)
        {
            try { return IsClient(p) && p.MainWindowHandle == IntPtr.Zero; }
            catch { return false; }
        }

        static bool OutlivedGrace(Process p)
        {
            DateTime started;
            try { started = p.StartTime; }
            catch { return false; }
            return OutlivedGrace(started, DateTime.Now);
        }

        public static bool OutlivedGrace(DateTime started, DateTime now)
        {
            return (now - started).TotalSeconds > GHOST_GRACE_SECONDS;
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
