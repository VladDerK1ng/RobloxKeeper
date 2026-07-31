using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxKeeper
{
    // What the user asked Windows to give one Roblox client.
    class ClientProfile
    {
        public int Priority = PerformanceManager.PRIORITY_NORMAL;
        public int Cores;        // 0 = every core
        public bool Eco;         // EcoQoS / "Efficiency mode"

        public ClientProfile Clone()
        {
            ClientProfile c = new ClientProfile();
            c.Priority = Priority;
            c.Cores = Cores;
            c.Eco = Eco;
            return c;
        }

        public bool SameAs(ClientProfile o)
        {
            return o != null && o.Priority == Priority && o.Cores == Cores && o.Eco == Eco;
        }

        public override string ToString()
        {
            string s = PerformanceManager.PriorityName(Priority);
            if (Cores > 0) s += ", " + Cores + " core" + (Cores == 1 ? "" : "s");
            if (Eco) s += ", eco";
            return s;
        }
    }

    // Per-client CPU and memory allocation.
    //
    // Roblox gives every instance the same slice of the machine, which is wrong
    // when one client is the one being played and three are parked in an AFK
    // game. Priority, core affinity and EcoQoS let the foreground client win, and
    // a working-set trim hands the parked clients' idle memory back to Windows.
    class PerformanceManager
    {
        public const int PRIORITY_LOW = 0;
        public const int PRIORITY_BELOW = 1;
        public const int PRIORITY_NORMAL = 2;
        public const int PRIORITY_ABOVE = 3;
        public const int PRIORITY_HIGH = 4;

        // Realtime is deliberately absent: it outranks input and disk drivers and
        // can wedge the whole machine.
        static readonly string[] PriorityNames =
            { "Low", "Below normal", "Normal", "Above normal", "High" };

        public Action<string> Log;

        // The profile handed to every client that appears from now on.
        public ClientProfile Defaults = new ClientProfile();

        // Per-client overrides. Keyed by PID, which Windows recycles across
        // launches, so these are deliberately session-only - a saved override
        // would eventually land on an unrelated process.
        readonly Dictionary<int, ClientProfile> overrides = new Dictionary<int, ClientProfile>();
        readonly Dictionary<int, ClientProfile> applied = new Dictionary<int, ClientProfile>();

        DateTime lastAutoTrim = DateTime.Now;

        public static string PriorityName(int index)
        {
            return index >= 0 && index < PriorityNames.Length ? PriorityNames[index] : "Normal";
        }

        public static string[] AllPriorityNames() { return (string[])PriorityNames.Clone(); }

        public ClientProfile ProfileFor(int pid)
        {
            ClientProfile p;
            if (overrides.TryGetValue(pid, out p)) return p;
            return Defaults;
        }

        public bool HasOverride(int pid) { return overrides.ContainsKey(pid); }

        public void SetOverride(int pid, ClientProfile profile)
        {
            overrides[pid] = profile;
        }

        public void Forget(int pid)
        {
            overrides.Remove(pid);
            applied.Remove(pid);
        }

        // Drops state for clients that have closed, so a recycled PID never
        // inherits the previous owner's settings.
        public void Prune(List<ClientInfo> alive)
        {
            List<int> gone = new List<int>();
            foreach (int pid in applied.Keys)
            {
                bool found = false;
                foreach (ClientInfo ci in alive) if (ci.Pid == pid) { found = true; break; }
                if (!found) gone.Add(pid);
            }
            foreach (int pid in gone) { applied.Remove(pid); overrides.Remove(pid); }
        }

        // Called once per tick. Anything whose live settings don't match its
        // profile gets them (re)applied - which covers newly launched clients
        // without needing to watch for launches separately.
        public void ApplyPending(List<ClientInfo> clients)
        {
            for (int i = 0; i < clients.Count; i++)
            {
                int pid = clients[i].Pid;
                ClientProfile want = ProfileFor(pid);
                ClientProfile have;
                if (applied.TryGetValue(pid, out have) && want.SameAs(have)) continue;

                string error;
                if (Apply(pid, want, i, out error))
                {
                    bool first = !applied.ContainsKey(pid);
                    applied[pid] = want.Clone();
                    if (!first || !want.SameAs(new ClientProfile()))
                        Log("Client PID " + pid + " set to " + want + ".");
                }
                else
                {
                    // Record the attempt anyway; retrying every second against a
                    // process that refuses would flood the log.
                    applied[pid] = want.Clone();
                    Log("Could not tune PID " + pid + ": " + error);
                }
            }
        }

        public bool Apply(int pid, ClientProfile profile, int clientIndex, out string error)
        {
            error = null;
            List<string> problems = new List<string>();

            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    try { p.PriorityClass = ToPriorityClass(profile.Priority); }
                    catch (Exception ex) { problems.Add("priority (" + ex.Message + ")"); }

                    try
                    {
                        IntPtr mask = AffinityMask(profile.Cores, clientIndex);
                        p.ProcessorAffinity = mask;
                    }
                    catch (Exception ex) { problems.Add("core affinity (" + ex.Message + ")"); }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (!SetEfficiencyMode(pid, profile.Eco))
                problems.Add("efficiency mode (needs Windows 10 2004 or newer)");

            if (problems.Count == 0) return true;
            error = string.Join(", ", problems.ToArray());
            return false;
        }

        static ProcessPriorityClass ToPriorityClass(int index)
        {
            switch (index)
            {
                case PRIORITY_LOW: return ProcessPriorityClass.Idle;
                case PRIORITY_BELOW: return ProcessPriorityClass.BelowNormal;
                case PRIORITY_ABOVE: return ProcessPriorityClass.AboveNormal;
                case PRIORITY_HIGH: return ProcessPriorityClass.High;
                default: return ProcessPriorityClass.Normal;
            }
        }

        // Successive clients get different, non-overlapping blocks of cores, so
        // asking for "4 cores" twice on a 16-thread CPU produces two clients that
        // genuinely do not fight, rather than two pinned to the same four.
        public static IntPtr AffinityMask(int coreCount, int clientIndex)
        {
            int total = Environment.ProcessorCount;
            if (total > 64) total = 64;          // an affinity mask is one word wide
            long all = total >= 64 ? -1L : (1L << total) - 1;
            if (coreCount <= 0 || coreCount >= total) return (IntPtr)all;

            long mask = 0;
            int start = (clientIndex * coreCount) % total;
            for (int i = 0; i < coreCount; i++)
                mask |= 1L << ((start + i) % total);
            return (IntPtr)mask;
        }

        public static bool SetEfficiencyMode(int pid, bool on)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_SET_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                Native.PROCESS_POWER_THROTTLING_STATE s = new Native.PROCESS_POWER_THROTTLING_STATE();
                s.Version = Native.PROCESS_POWER_THROTTLING_CURRENT_VERSION;
                // Clearing both masks returns the process to system-managed
                // throttling, which is what "off" should mean - not "pinned to
                // full speed forever".
                s.ControlMask = on ? Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0;
                s.StateMask = on ? Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0;
                return Native.SetProcessInformation(h, Native.ProcessPowerThrottling, ref s,
                    Marshal.SizeOf(typeof(Native.PROCESS_POWER_THROTTLING_STATE)));
            }
            catch { return false; }
            finally { Native.CloseHandle(h); }
        }

        // Pushes a client's idle pages out of physical RAM. Windows pages back
        // whatever is still needed, so this is safe to run on a client mid-game -
        // it costs a brief hitch, not stability.
        public static bool Trim(int pid)
        {
            IntPtr h = Native.OpenProcess(
                Native.PROCESS_SET_QUOTA | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try { return Native.SetProcessWorkingSetSize(h, (IntPtr)(-1), (IntPtr)(-1)); }
            catch { return false; }
            finally { Native.CloseHandle(h); }
        }

        public long TrimAll(List<ClientInfo> clients, int skipPid)
        {
            long before = 0, after = 0;
            int done = 0;
            foreach (ClientInfo ci in clients)
            {
                if (ci.Pid == skipPid) continue;
                before += ci.WorkingSet;
                if (!Trim(ci.Pid)) continue;
                done++;
                try { using (Process p = Process.GetProcessById(ci.Pid)) after += p.WorkingSet64; }
                catch { }
            }
            if (done == 0) return 0;
            long freed = before - after;
            return freed > 0 ? freed : 0;
        }

        // Trims every client except the one the user is looking at, on the
        // interval they chose. The foreground client is skipped so the game
        // being played never takes the paging hitch.
        public void AutoTrimTick(List<ClientInfo> clients, int intervalMinutes, int foregroundPid)
        {
            if (clients.Count == 0) return;
            if ((DateTime.Now - lastAutoTrim).TotalMinutes < intervalMinutes) return;
            lastAutoTrim = DateTime.Now;

            long freed = TrimAll(clients, foregroundPid);
            if (freed > 1048576)
                Log("Auto-trim released " + ClientTracker.FormatBytes(freed) + " from background client(s).");
        }

        public void ResetAutoTrimClock() { lastAutoTrim = DateTime.Now; }

        public static int ForegroundPid()
        {
            try
            {
                IntPtr hwnd = Native.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return 0;
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                return (int)pid;
            }
            catch { return 0; }
        }
    }
}
