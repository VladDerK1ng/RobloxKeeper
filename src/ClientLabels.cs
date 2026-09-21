using System;
using System.Collections.Generic;

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
}
