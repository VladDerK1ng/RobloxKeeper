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
