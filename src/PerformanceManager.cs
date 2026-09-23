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

    // Hands each client a block of cores and lets it keep that block.
    //
    // The obvious version - number the clients and give client N the Nth block -
    // is wrong, because the numbering comes from a list sorted by start time.
    // Close the oldest client and every later client's number shifts by one,
    // while the masks already applied to them do not. Two clients then believe
    // they own blocks that overlap, which is precisely what pinning was meant to
    // prevent. A block belongs to a PID until that PID goes away.
    class CoreBlocks
    {
        readonly Dictionary<int, int> blocks = new Dictionary<int, int>();

        public int BlockFor(int pid)
        {
            int block;
            if (blocks.TryGetValue(pid, out block)) return block;

            block = 0;
            while (blocks.ContainsValue(block)) block++;
            blocks[pid] = block;
            return block;
        }

        public void Release(int pid) { blocks.Remove(pid); }
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

        // Applying a profile to a process. Swapped out by the tests so the retry
        // ladder and the "new clients only" rule can be driven without a real
        // Roblox client to tune.
        public delegate bool ApplyFunc(int pid, ClientProfile profile, int coreBlock, out string error);

        public Action<string> Log;
        public ApplyFunc Applier;

        // Swapped out by the tests so backoffs don't take real seconds.
        public Func<DateTime> Clock = delegate { return DateTime.Now; };

        // The profile handed to every client that appears from now on. Changing
        // this does NOT reach clients that are already running - the card says
        // "New clients:" and must mean it. ApplyToAllRunning is the opt-in.
        public ClientProfile Defaults = new ClientProfile();

        // Throttle every client except the one being looked at. This is what
        // replaced a per-client FPS cap: capping FPS is only reachable through a
        // Roblox config file, which is global to all clients and would mean
        // modifying Roblox's own files. Priority and EcoQoS are outside the
        // process entirely and cost nothing in compatibility.
        public bool ThrottleBackground;

        // Per-client overrides. Keyed by PID, which Windows recycles across
        // launches, so these are deliberately session-only - a saved override
        // would eventually land on an unrelated process.
        readonly Dictionary<int, ClientProfile> overrides = new Dictionary<int, ClientProfile>();

        // The default as it stood when each client was first seen. Snapshotting
        // it here is what stops a later change to Defaults from retuning a
        // client that is already playing.
        readonly Dictionary<int, ClientProfile> assigned = new Dictionary<int, ClientProfile>();

        // What Windows has confirmed, not what we asked for.
        readonly Dictionary<int, ClientProfile> applied = new Dictionary<int, ClientProfile>();

        readonly Dictionary<int, Retry> retries = new Dictionary<int, Retry>();
        readonly CoreBlocks blocks = new CoreBlocks();

        DateTime lastAutoTrim = DateTime.Now;

        public PerformanceManager() { Applier = Apply; Capper = SetMemoryCap; }

        // A tune Windows refused, and when to try it again. Recording a refusal
        // as though it had worked - which is what the old code did - left
        // clients that were merely still starting up untuned forever.
        class Retry
        {
            public int Attempts;
            public DateTime NextAt;
        }

        // 1s, 2s, 4s ... capped, so a process that will never accept the call
        // costs one attempt a minute rather than one a second.
        internal static int BackoffSeconds(int attempts)
        {
            int seconds = 1;
            for (int i = 1; i < attempts && seconds < 60; i++) seconds *= 2;
            return seconds > 60 ? 60 : seconds;
        }

        public static string PriorityName(int index)
        {
            return index >= 0 && index < PriorityNames.Length ? PriorityNames[index] : "Normal";
        }

        public static string[] AllPriorityNames() { return (string[])PriorityNames.Clone(); }

        // The profile this client runs under: its own override if it has one,
        // otherwise the default as it stood when the client first appeared.
        // Taking that snapshot here is deliberate - it is the whole mechanism
        // behind "new clients only".
        public ClientProfile ProfileFor(int pid)
        {
            ClientProfile p;
            if (overrides.TryGetValue(pid, out p)) return p;
            if (assigned.TryGetValue(pid, out p)) return p;

            p = Defaults.Clone();
            assigned[pid] = p;
            return p;
        }

        public bool HasOverride(int pid) { return overrides.ContainsKey(pid); }

        // What a client should actually be running at right now: its profile,
        // throttled if it is in the background and throttling is on.
        public ClientProfile EffectiveFor(int pid, int foregroundPid)
        {
            ClientProfile want = ProfileFor(pid);
            if (!ThrottleBackground || pid == foregroundPid) return want;
            return Throttled(want);
        }

        // One step down and efficiency mode on. Returns a new profile - the
        // stored one has to survive untouched or the client could never be
        // restored when it comes back to the foreground.
        public static ClientProfile Throttled(ClientProfile p)
        {
            ClientProfile t = p.Clone();
            if (t.Priority > PRIORITY_LOW) t.Priority--;
            t.Eco = true;
            return t;                 // core pinning is the user's choice, left alone
        }

        // A client sitting on more memory than the user is willing to give it.
        // Zero means no ceiling at all.
        public static bool OverCeiling(long workingSet, int ceilingMb)
        {
            if (ceilingMb <= 0) return false;
            return workingSet > (long)ceilingMb * 1024 * 1024;
        }

        // Park every client except the one being played. Written as per-client
        // overrides so it survives a default change and can be undone exactly.
        public void EnterAfkMode(List<ClientInfo> clients, int foregroundPid)
        {
            foreach (ClientInfo ci in clients)
            {
                ClientProfile p;
                if (ci.Pid == foregroundPid)
                {
                    p = new ClientProfile();
                    p.Priority = PRIORITY_NORMAL;
                    p.Cores = 0;
                    p.Eco = false;
                }
                else
                {
                    p = new ClientProfile();
                    p.Priority = PRIORITY_BELOW;
                    p.Cores = 0;
                    p.Eco = true;
                }
                SetOverride(ci.Pid, p);
            }
        }

        public void LeaveAfkMode(List<ClientInfo> clients)
        {
            foreach (ClientInfo ci in clients) Forget(ci.Pid);
        }

        // Holding a client to a memory ceiling. Swapped out by the tests.
        // ceilingMb 0 lets it go.
        public delegate bool CapFunc(int pid, int ceilingMb, out string error);
        public CapFunc Capper;

        // What Windows is holding each client to, and when a refused one may
        // be asked again.
        readonly Dictionary<int, int> caps = new Dictionary<int, int>();
        readonly Dictionary<int, DateTime> capRetry = new Dictionary<int, DateTime>();

        // The memory ceiling, once a tick; 0 when it is off.
        //
        // It used to empty a background client's whole working set whenever
        // it crossed the line. A game that really uses more than the line
        // climbs straight back, so it was emptied again seconds later - steady
        // stutter, and never actually under the line. A hard maximum is a
        // ceiling: Windows keeps the client at or under it by moving out only
        // the pages it has used least. It is set once, lifted while the client
        // is the one in front - the game being played is never held back - and
        // put back when it goes behind.
        public void CeilingTick(List<ClientInfo> clients, int ceilingMb, int foregroundPid)
        {
            foreach (ClientInfo ci in clients)
            {
                int want = ceilingMb > 0 && ci.Pid != foregroundPid ? ceilingMb : 0;
                int have;
                caps.TryGetValue(ci.Pid, out have);
                if (want == have) continue;
                SetCap(ci.Pid, want);
            }
        }

        // Everything let go - the ceiling can't be left holding clients once
        // nothing is managing it.
        public void LiftCeiling()
        {
            List<int> pids = new List<int>(caps.Keys);
            pids.Sort();
            foreach (int pid in pids) SetCap(pid, 0);
        }

        void SetCap(int pid, int mb)
        {
            DateTime next;
            if (capRetry.TryGetValue(pid, out next) && Clock() < next) return;

            string error;
            if (Capper(pid, mb, out error))
            {
                capRetry.Remove(pid);
                if (mb == 0) caps.Remove(pid);
                else caps[pid] = mb;
                return;
            }

            bool first = !capRetry.ContainsKey(pid);
            capRetry[pid] = Clock().AddSeconds(60);
            if (first)
                Log((mb == 0 ? "Couldn't let go of PID " + pid + "'s memory ceiling: "
                             : "Couldn't hold PID " + pid + " under " + mb + " MB: ")
                    + error + ". Trying again in a minute.");
        }

        // The minimum and maximum Windows is given. The client's own minimum is
        // kept unless it is above what a ceiling allows.
        public static void CapLimits(int ceilingMb, long currentMin, out long min, out long max)
        {
            max = (long)ceilingMb * 1024 * 1024;
            min = currentMin;
            if (min <= 0 || min > max / 2) min = max / 2;
        }

        public static bool SetMemoryCap(int pid, int ceilingMb, out string error)
        {
            error = null;
            IntPtr h = Native.OpenProcess(
                Native.PROCESS_SET_QUOTA | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
            {
                error = "Windows wouldn't let the app reach it (error " + Marshal.GetLastWin32Error() + ")";
                return false;
            }
            try
            {
                IntPtr curMin, curMax;
                uint flags;
                if (!Native.GetProcessWorkingSetSizeEx(h, out curMin, out curMax, out flags))
                {
                    error = "Windows wouldn't say how much it may use (error " + Marshal.GetLastWin32Error() + ")";
                    return false;
                }

                bool ok;
                if (ceilingMb <= 0)
                    ok = Native.SetProcessWorkingSetSizeEx(h, curMin, curMax,
                        Native.QUOTA_LIMITS_HARDWS_MIN_DISABLE | Native.QUOTA_LIMITS_HARDWS_MAX_DISABLE);
                else
                {
                    long min, max;
                    CapLimits(ceilingMb, (long)curMin, out min, out max);
                    ok = Native.SetProcessWorkingSetSizeEx(h, (IntPtr)min, (IntPtr)max,
                        Native.QUOTA_LIMITS_HARDWS_MIN_DISABLE | Native.QUOTA_LIMITS_HARDWS_MAX_ENABLE);
                }
                if (!ok) error = "Windows refused (error " + Marshal.GetLastWin32Error() + ")";
                return ok;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally { Native.CloseHandle(h); }
        }

        public void SetOverride(int pid, ClientProfile profile)
        {
            overrides[pid] = profile;
            retries.Remove(pid);        // a fresh request deserves a fresh attempt
        }

        public void Forget(int pid)
        {
            overrides.Remove(pid);
            assigned.Remove(pid);       // re-snapshots from the current default
            retries.Remove(pid);
        }

        // Drops state for clients that have closed, so a recycled PID never
        // inherits the previous owner's settings.
        public void Prune(List<ClientInfo> alive)
        {
            // Every pid we track, not just the ones that applied cleanly. A
            // client that kept failing lives only in assigned/retries, and
            // walking applied alone would leak its backoff onto the next
            // process to reuse that PID.
            List<int> tracked = new List<int>();
            foreach (int pid in applied.Keys) tracked.Add(pid);
            foreach (int pid in assigned.Keys) if (!tracked.Contains(pid)) tracked.Add(pid);
            foreach (int pid in overrides.Keys) if (!tracked.Contains(pid)) tracked.Add(pid);
            foreach (int pid in caps.Keys) if (!tracked.Contains(pid)) tracked.Add(pid);
            foreach (int pid in capRetry.Keys) if (!tracked.Contains(pid)) tracked.Add(pid);

            List<int> gone = new List<int>();
            foreach (int pid in tracked)
            {
                bool found = false;
                foreach (ClientInfo ci in alive) if (ci.Pid == pid) { found = true; break; }
                if (!found) gone.Add(pid);
            }
            foreach (int pid in gone)
            {
                applied.Remove(pid);
                overrides.Remove(pid);
                assigned.Remove(pid);
                retries.Remove(pid);
                blocks.Release(pid);
                caps.Remove(pid);
                capRetry.Remove(pid);
            }
        }

        // Called once per tick. Anything whose live settings don't match its
        // profile gets them (re)applied - which covers newly launched clients
        // without needing to watch for launches separately.
        public void ApplyPending(List<ClientInfo> clients) { ApplyPending(clients, 0); }

        public void ApplyPending(List<ClientInfo> clients, int foregroundPid)
        {
            for (int i = 0; i < clients.Count; i++)
            {
                int pid = clients[i].Pid;
                ClientProfile want = EffectiveFor(pid, foregroundPid);
                ClientProfile have;
                if (applied.TryGetValue(pid, out have) && want.SameAs(have)) continue;

                // A refused tune waits out its backoff rather than being retried
                // every tick - or, as before, never retried at all.
                Retry retry;
                if (retries.TryGetValue(pid, out retry) && Clock() < retry.NextAt) continue;

                string error;
                if (Applier(pid, want, blocks.BlockFor(pid), out error))
                {
                    bool first = !applied.ContainsKey(pid);
                    applied[pid] = want.Clone();
                    bool recovered = retries.Remove(pid);
                    if (recovered || !first || !want.SameAs(new ClientProfile()))
                        Log("Client PID " + pid + " set to " + want + ".");
                }
                else
                {
                    int attempts = retry == null ? 1 : retry.Attempts + 1;
                    int wait = BackoffSeconds(attempts);
                    Retry next = new Retry();
                    next.Attempts = attempts;
                    next.NextAt = Clock().AddSeconds(wait);
                    retries[pid] = next;

                    // Only the first refusal and every fourth after it, so a
                    // process that will never accept the call cannot fill the log.
                    if (attempts == 1 || attempts % 4 == 0)
                        Log("Could not tune PID " + pid + ": " + error +
                            " (attempt " + attempts + ", trying again in " + wait + "s).");
                }
            }
        }

        // The explicit "I mean all of them" action. Changing the default is
        // deliberately not this, because the common case is setting what the
        // next AFK client should get while the one being played stays put.
        public void ApplyToAllRunning(List<ClientInfo> clients)
        {
            foreach (ClientInfo ci in clients)
            {
                if (overrides.ContainsKey(ci.Pid)) continue;   // Tune outranks the default
                assigned[ci.Pid] = Defaults.Clone();
                retries.Remove(ci.Pid);
            }
            ApplyPending(clients);
        }

        public bool Apply(int pid, ClientProfile profile, int coreBlock, out string error)
        {
            error = null;
            List<string> problems = new List<string>();
            ProcessPriorityClass wantPriority = ToPriorityClass(profile.Priority);
            long wantMask = (long)AffinityMask(profile.Cores, coreBlock);

            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    try { p.PriorityClass = wantPriority; }
                    catch (Exception ex) { problems.Add("priority (" + ex.Message + ")"); }

                    try { p.ProcessorAffinity = (IntPtr)wantMask; }
                    catch (Exception ex) { problems.Add("core affinity (" + ex.Message + ")"); }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            int ecoError;
            if (!SetEfficiencyMode(pid, profile.Eco, out ecoError))
                problems.Add("efficiency mode - " + EcoFailureReason(ecoError));

            // Windows can accept a call and leave the process exactly as it was,
            // so ask it afterwards instead of treating "threw nothing" as proof.
            // A drift reported here feeds the retry ladder like any other failure.
            try
            {
                using (Process check = Process.GetProcessById(pid))
                {
                    string drift = ReadBackProblem(wantPriority, wantMask,
                        check.PriorityClass, (long)check.ProcessorAffinity);
                    if (drift != null) problems.Add(drift);
                }
            }
            catch { }   // the client closed mid-tune; the next tick sorts it out

            if (problems.Count == 0) return true;
            error = string.Join(", ", problems.ToArray());
            return false;
        }

        // What the process looks like now versus what was asked for.
        internal static string ReadBackProblem(ProcessPriorityClass wantPriority, long wantMask,
                                               ProcessPriorityClass actualPriority, long actualMask)
        {
            List<string> drift = new List<string>();
            if (actualPriority != wantPriority)
                drift.Add("priority did not stick (Windows left it at " + actualPriority + ")");
            if (actualMask != wantMask)
                drift.Add("core affinity did not stick (Windows left it at 0x" +
                          actualMask.ToString("X") + ")");
            return drift.Count == 0 ? null : string.Join(", ", drift.ToArray());
        }

        // Every efficiency-mode failure used to be reported as an old version of
        // Windows, which sent anyone hitting a permissions problem off to check
        // their build number.
        internal static string EcoFailureReason(int win32Error)
        {
            switch (win32Error)
            {
                case 5:   return "access to the process was denied";
                case 6:   return "the process handle was rejected";
                case 87:  return "this build of Windows has no EcoQoS (needs Windows 10 2004 or newer)";
                default:  return "Windows refused the call (error " + win32Error + ")";
            }
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
            int ignored;
            return SetEfficiencyMode(pid, on, out ignored);
        }

        public static bool SetEfficiencyMode(int pid, bool on, out int win32Error)
        {
            win32Error = 0;
            IntPtr h = Native.OpenProcess(Native.PROCESS_SET_INFORMATION, false, pid);
            if (h == IntPtr.Zero) { win32Error = Marshal.GetLastWin32Error(); return false; }
            try
            {
                Native.PROCESS_POWER_THROTTLING_STATE s = new Native.PROCESS_POWER_THROTTLING_STATE();
                s.Version = Native.PROCESS_POWER_THROTTLING_CURRENT_VERSION;
                // Clearing both masks returns the process to system-managed
                // throttling, which is what "off" should mean - not "pinned to
                // full speed forever".
                s.ControlMask = on ? Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0;
                s.StateMask = on ? Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0;
                bool ok = Native.SetProcessInformation(h, Native.ProcessPowerThrottling, ref s,
                    Marshal.SizeOf(typeof(Native.PROCESS_POWER_THROTTLING_STATE)));
                if (!ok) win32Error = Marshal.GetLastWin32Error();
                return ok;
            }
            catch { win32Error = Marshal.GetLastWin32Error(); return false; }
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
