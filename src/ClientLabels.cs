using System;
using System.Collections.Generic;
using System.Threading;

namespace RobloxKeeper
{
    // Which running client belongs to which account.
    //
    // Only knowable for clients this app launched, because that is the moment
    // the account and the process id are both in hand. A client started from
    // the website has no account attached to it as far as the app is concerned,
    // and is left labelled by number rather than guessed at.
    //
    // PIDs are recycled by Windows, so a label that outlived its process would
    // eventually put somebody's account name on an unrelated client. Prune is
    // what prevents that, and it runs on the same tick as everything else.
    class ClientLabels
    {
        readonly Dictionary<int, string> byPid = new Dictionary<int, string>();

        public void Assign(int pid, string accountName)
        {
            if (pid <= 0 || string.IsNullOrEmpty(accountName)) return;
            byPid[pid] = accountName;
        }

        public string NameFor(int pid)
        {
            string name;
            return byPid.TryGetValue(pid, out name) ? name : null;
        }

        public void Prune(IList<int> alivePids)
        {
            List<int> gone = new List<int>();
            foreach (int pid in byPid.Keys)
                if (!alivePids.Contains(pid)) gone.Add(pid);
            foreach (int pid in gone) byPid.Remove(pid);
        }

        public void Forget(int pid) { byPid.Remove(pid); }

        // What the client row is called: the account when we know it, the old
        // numbering when we do not.
        public static string RowTitle(string accountName, int index)
        {
            return string.IsNullOrEmpty(accountName) ? "Client " + index : accountName;
        }
    }

    // Which client writes which log.
    //
    // Windows is asked which client has the log open: a client holds its own
    // log from the moment it starts until it exits - measured on a live one.
    // The time in the log's name is not enough. A log opens three to four
    // seconds after its client starts, and clients launched together start
    // three seconds apart, so by the clock every log of a batch fitted the
    // first client: it took each account's name in turn, and the others were
    // left as "Client 2", "Client 3".
    //
    // An answer is kept while its client runs - a log's writer never changes,
    // and asking costs Windows a look through every handle on the PC. Only
    // when Windows can't say is the clock used, and then only when a single
    // client fits; two that fit are not guessed between.
    class LogOwners
    {
        // Every process with the log open; null when Windows couldn't say.
        public Func<string, IList<int>> Writers = RobloxLog.WritersOf;

        readonly Dictionary<string, int> owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // `alive` is every Roblox client process, window or not; `clients` the
        // ones with a window, which carry when they started. 0 when no client
        // can be told.
        public int PidFor(string logFileName, IList<int> alive, IList<ClientInfo> clients)
        {
            if (string.IsNullOrEmpty(logFileName)) return 0;
            int pid;
            if (owners.TryGetValue(logFileName, out pid)) return alive.Contains(pid) ? pid : 0;

            IList<int> holders = null;
            try { holders = Writers(logFileName); } catch { }
            if (holders != null)
            {
                // None: it is closed, so its client has exited - or it is not
                // a client's log, like the crash handler's beside each one.
                pid = 0;
                foreach (int h in holders)
                {
                    if (!alive.Contains(h) || h == pid) continue;
                    if (pid != 0) return 0;
                    pid = h;
                }
                if (pid != 0) owners[logFileName] = pid;
                return pid;
            }

            int found = 0;
            foreach (ClientInfo ci in clients)
            {
                if (ci.Start == DateTime.MinValue) continue;
                if (!RobloxLog.LooksLikeSameSession(logFileName, ci.Start.ToUniversalTime())) continue;
                if (found != 0) return 0;
                found = ci.Pid;
            }
            return found;
        }

        // PIDs are recycled, so an answer goes when its client does.
        public void Prune(IList<int> alivePids)
        {
            List<string> gone = new List<string>();
            foreach (KeyValuePair<string, int> o in owners)
                if (!alivePids.Contains(o.Value)) gone.Add(o.Key);
            foreach (string log in gone) owners.Remove(log);
        }
    }

    // Turns the user id a client's log names into what to call the client.
    //
    // A saved account is known by its user id at once. One saved before ids
    // were kept is matched by name the first time Roblox says whose id it is,
    // and learns its id then. Anyone else is called by their Roblox name - it
    // just isn't one of yours, so nothing that goes by account matches it.
    class ClientNamer
    {
        // Blocking; run on a worker. Null when Roblox didn't say.
        public Func<string, string> LookUp = RobloxAuth.NameOfUser;
        public Action<WaitCallback> Run = delegate(WaitCallback w) { ThreadPool.QueueUserWorkItem(w); };

        readonly object gate = new object();
        readonly Dictionary<string, string> names = new Dictionary<string, string>();
        readonly Dictionary<string, bool> asking = new Dictionary<string, bool>();

        // The name to show now, or null while Roblox is asked - whenKnown is
        // called (from the worker) once it has answered. `recorded` is a saved
        // account that has just learned its id, for the caller to save.
        public string NameFor(string userId, AccountStore accounts, Action whenKnown, out RobloxAccount recorded)
        {
            recorded = null;
            if (string.IsNullOrEmpty(userId)) return null;

            if (accounts != null)
                foreach (RobloxAccount a in accounts.Accounts)
                    if (a.UserId == userId) return a.Name;

            string robloxName;
            lock (gate)
            {
                if (!names.TryGetValue(userId, out robloxName))
                {
                    if (!asking.ContainsKey(userId))
                    {
                        asking[userId] = true;
                        Ask(userId, whenKnown);
                    }
                    return null;
                }
            }

            RobloxAccount saved = accounts == null ? null : accounts.Find(robloxName);
            if (saved == null) return robloxName;
            if (string.IsNullOrEmpty(saved.UserId))
            {
                saved.UserId = userId;
                recorded = saved;
            }
            return saved.Name;
        }

        void Ask(string userId, Action whenKnown)
        {
            Run(delegate
            {
                string name = null;
                try { name = LookUp(userId); } catch { }
                lock (gate)
                {
                    asking.Remove(userId);
                    if (!string.IsNullOrEmpty(name)) names[userId] = name;
                }
                if (!string.IsNullOrEmpty(name) && whenKnown != null) whenKnown();
            });
        }
    }
}
