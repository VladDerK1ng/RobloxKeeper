using System;
using System.Collections.Generic;

namespace RobloxKeeper
{
    // Accounts to keep in whatever server a player is in.
    class FollowRequest
    {
        public string Player;
        public string PlayerId;
        public readonly List<string> Accounts = new List<string>();
    }

    // Where following is up to, apart from the app so its rules can be tested.
    //
    // Every half minute one account asks Roblox where the player is. Any
    // account in a different server is moved there, one at a time, and after
    // each move it looks again at once for the next - so five accounts catch
    // up in a minute or two, not five half-minutes.
    class FollowState
    {
        public const int CHECK_SECONDS = 30;
        // Launched or moved this recently, a client is still joining, and
        // what it last said about where it is doesn't count yet.
        public const int JOIN_SECONDS = 60;
        // Gone this long - and not being moved - it was closed on purpose.
        public const int GRACE_SECONDS = 90;

        public readonly FollowRequest Request;
        public string Moving;               // the account being moved now
        public bool Checking;               // asking Roblox where they are
        public int LastSeenType = -2;       // what was said last, so it's said once

        readonly Dictionary<string, DateTime> started = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        DateTime nextCheck;

        public FollowState(FollowRequest r, DateTime now)
        {
            Request = r;
            foreach (string a in r.Accounts) started[a] = now;
            nextCheck = now.AddSeconds(CHECK_SECONDS);
        }

        public bool CheckDue(DateTime now)
        {
            return !Checking && Moving == null && now >= nextCheck;
        }

        public void Looked(DateTime now)
        {
            Checking = false;
            nextCheck = now.AddSeconds(CHECK_SECONDS);
        }

        public string NextToMove(string job, Func<string, string> jobOf, Func<string, bool> hunting, DateTime now)
        {
            if (Moving != null || string.IsNullOrEmpty(job)) return null;
            foreach (string a in Request.Accounts)
            {
                DateTime at;
                if (started.TryGetValue(a, out at) && (now - at).TotalSeconds < JOIN_SECONDS) continue;
                if (hunting(a)) continue;
                string where = jobOf(a);
                if (where == null) continue;
                if (!string.Equals(where, job, StringComparison.OrdinalIgnoreCase)) return a;
            }
            return null;
        }

        public void MoveStarted(string account, DateTime now)
        {
            Moving = account;
            started[account] = now;
        }

        // Looks again straight away for the next one.
        public void MoveDone(DateTime now)
        {
            Moving = null;
            nextCheck = now;
        }

        // Accounts whose client has been gone longer than a restart takes:
        // closed on purpose. They stop following, and are returned so it can
        // be said.
        public List<string> Dropped(Func<string, bool> hasClient, DateTime now)
        {
            List<string> gone = new List<string>();
            foreach (string a in Request.Accounts)
            {
                if (a == Moving || hasClient(a)) continue;
                DateTime at;
                if (started.TryGetValue(a, out at) && (now - at).TotalSeconds < GRACE_SECONDS) continue;
                gone.Add(a);
            }
            foreach (string a in gone) { Request.Accounts.Remove(a); started.Remove(a); }
            return gone;
        }
    }
}
