using System;
using System.Collections.Generic;

namespace RobloxKeeper
{
    // What the Hunt window was last set to, kept for next time.
    class HuntSettings
    {
        public string GameLink = "";
        public int LookSeconds = 60;
        public bool StayWhenFound = true;
        public string[] Accounts = new string[0];
        public ServerPrefs Servers = new ServerPrefs();
    }

    // What the Hunt window and the watchers list need from the running hunt.
    interface IHuntControl
    {
        string Start(HuntSettings s);        // null when it started, otherwise why not
        void Stop();
        bool Running { get; }
        IList<string> Lines(DateTime now);   // one per account
    }

    // The rules around starting a hunt, kept apart so they can be tested.
    static class Hunting
    {
        // Null when a hunt may start. hasSignIn: is this account's session
        // saved; watched: does a switched-on watcher look at its client.
        public static string StartProblem(string placeId, IList<string> accounts,
                                          Func<string, bool> hasSignIn, Func<string, bool> watched)
        {
            if (string.IsNullOrEmpty(placeId)) return "Paste the link of the game to hunt in.";
            if (accounts == null || accounts.Count == 0) return "Tick at least one account to hunt with.";
            foreach (string a in accounts)
                if (!hasSignIn(a))
                    return a + " has no saved sign-in, so its client can't be moved. Sign it in again in Accounts.";
            foreach (string a in accounts)
                if (!watched(a))
                    return "No switched-on watcher in this setup looks at " + a + "'s client, so a hunt would never find anything.";
            return null;
        }
    }

    enum HuntStage { Starting, Hopping, Joining, Looking, Waiting, Found, Stopped }

    enum HuntOrder { None, Hop }

    // Hunt mode for one account: move to a server, look for a while, move
    // on - and stay where a watcher finds something.
    //
    // It decides only what happens next. The looking is the chosen setup's
    // watchers, running exactly as they always do; the moving is Hopper. The
    // app feeds this the clock and what happened, and carries out its orders.
    class Hunt
    {
        public const int RETRY_SECONDS = 15;
        public const int MAX_FAILURES = 5;

        public readonly string Account;
        public int LookSeconds = 60;
        public int JoinTimeoutSeconds = 90;
        public bool StayWhenFound = true;

        public HuntStage Stage { get; private set; }
        public string JobId { get; private set; }
        public int Servers { get; private set; }         // servers arrived in
        public int Finds { get; private set; }
        public int FailuresInARow { get; private set; }

        DateTime since, retryAt;
        string problem, foundWhat;

        public Hunt(string account)
        {
            Account = account;
            Stage = HuntStage.Starting;
        }

        public bool Active { get { return Stage != HuntStage.Found && Stage != HuntStage.Stopped; } }

        public HuntOrder Tick(DateTime now)
        {
            switch (Stage)
            {
                case HuntStage.Starting:
                    return Hop();
                case HuntStage.Waiting:
                    return now >= retryAt ? Hop() : HuntOrder.None;
                case HuntStage.Joining:
                    if (now - since < TimeSpan.FromSeconds(JoinTimeoutSeconds)) return HuntOrder.None;
                    if (Fail("didn't arrive in a server within " + JoinTimeoutSeconds + " seconds")) return HuntOrder.None;
                    return Hop();
                case HuntStage.Looking:
                    return now - since >= TimeSpan.FromSeconds(LookSeconds) ? Hop() : HuntOrder.None;
                default:
                    return HuntOrder.None;
            }
        }

        HuntOrder Hop()
        {
            Stage = HuntStage.Hopping;
            return HuntOrder.Hop;
        }

        public void HopStarted(DateTime now)
        {
            if (!Active) return;
            Stage = HuntStage.Joining;
            since = now;
        }

        public void HopFailed(string why, DateTime now)
        {
            if (!Active) return;
            if (Fail(why)) return;
            Stage = HuntStage.Waiting;
            retryAt = now.AddSeconds(RETRY_SECONDS);
        }

        // True when that was one failure too many and the hunt has stopped.
        bool Fail(string why)
        {
            FailuresInARow++;
            problem = why;
            if (FailuresInARow < MAX_FAILURES) return false;
            Stage = HuntStage.Stopped;
            problem = MAX_FAILURES + " tries in a row didn't work - the last: " + why;
            return true;
        }

        public void Joined(string jobId, DateTime now)
        {
            if (!Active) return;
            Stage = HuntStage.Looking;
            JobId = jobId;
            since = now;
            Servers++;
            FailuresInARow = 0;
            problem = null;
        }

        // A watcher found something on this account's client. Only counts in
        // the server it is looking in - a find raised by the last server and
        // arriving late is not a find in the next one. True when the hunt
        // stops here because of it.
        public bool Found(string what, DateTime now)
        {
            if (Stage != HuntStage.Looking) return false;
            Finds++;
            if (!StayWhenFound) return false;
            Stage = HuntStage.Found;
            foundWhat = what;
            return true;
        }

        public void Stop()
        {
            Stage = HuntStage.Stopped;
            problem = null;
        }

        public string Describe(DateTime now)
        {
            switch (Stage)
            {
                case HuntStage.Starting:
                case HuntStage.Hopping:
                    return "finding a server...";
                case HuntStage.Joining:
                    return "joining server " + (Servers + 1) + "...";
                case HuntStage.Looking:
                    int left = Math.Max(0, LookSeconds - (int)(now - since).TotalSeconds);
                    return "looking in server " + Servers + " - " + left + "s left";
                case HuntStage.Waiting:
                    int wait = Math.Max(0, (int)Math.Ceiling((retryAt - now).TotalSeconds));
                    return "couldn't move (" + problem + ") - trying again in " + wait + "s";
                case HuntStage.Found:
                    return "found \"" + foundWhat + "\" in server " + Servers + " - staying there";
                default:
                    return problem == null ? "stopped" : "stopped: " + problem;
            }
        }
    }
}
