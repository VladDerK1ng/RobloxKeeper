using System;
using System.Collections.Generic;
using System.Threading;

namespace RobloxKeeper
{
    // What the Accounts window needs from the main window, and keeping
    // accounts with a player they follow.
    partial class MainForm : IAccountsHost
    {
        FollowState follow;

        void IAccountsHost.Log(string line) { OnUi(delegate { Log(line); }); }

        void IAccountsHost.Launched(int pid, string account) { OnUi(delegate { clientLabels.Assign(pid, account); }); }

        int IAccountsHost.PidOf(string account) { return PidOfAccount(account); }

        bool IAccountsHost.IsHunting(string account) { return IsHuntingAccount(account); }

        AccountsPrefs IAccountsHost.Prefs { get { return settings.Accounts; } }

        void IAccountsHost.SavePrefs() { SaveSettings(); }

        void IAccountsHost.Follow(FollowRequest f) { OnUi(delegate { StartFollowing(f); }); }

        string IAccountsHost.Following { get { return follow == null ? null : follow.Request.Player; } }

        void IAccountsHost.StopFollowing() { StopFollowing(true); }

        bool IsHuntingAccount(string account)
        {
            Hunt h;
            return hunts.TryGetValue(account, out h) && h.Active;
        }

        void StartFollowing(FollowRequest f)
        {
            follow = new FollowState(f, DateTime.Now);
            Log("Following " + f.Player + " with " + string.Join(", ", f.Accounts.ToArray())
                + " - when they move to another server, " + (f.Accounts.Count == 1 ? "it goes" : "they go") + " too.");
        }

        void StopFollowing(bool say)
        {
            if (follow == null) return;
            if (say) Log("Stopped following " + follow.Request.Player + ". Every client stays where it is.");
            follow = null;
        }

        // Where an account's client is, as its log last said.
        string JobOfAccount(string account)
        {
            int pid = PidOfAccount(account);
            RobloxLogEvent e;
            return pid > 0 && clientWhere.TryGetValue(pid, out e) ? e.JobId : null;
        }

        // Once a second, from the main loop.
        void FollowTick()
        {
            if (follow == null) return;
            DateTime now = DateTime.Now;
            string player = follow.Request.Player;

            foreach (string gone in follow.Dropped(delegate(string a) { return PidOfAccount(a) > 0; }, now))
                Log(gone + "'s client was closed, so it has stopped following " + player + ".");
            if (follow.Request.Accounts.Count == 0)
            {
                Log("No account is following " + player + " any more.");
                follow = null;
                return;
            }

            // A hunt moving one of its clients goes first; the two never move
            // clients at the same time.
            if (!follow.CheckDue(now) || hopLine.Moving) return;

            EnsureAccounts();
            RobloxAccount asker = null;
            foreach (string a in follow.Request.Accounts)
            {
                RobloxAccount acc = accounts.Find(a);
                if (acc != null && acc.IsUsable) { asker = acc; break; }
            }
            if (asker == null)
            {
                Log("Stopped following " + player + " - none of the accounts following has a saved sign-in.");
                follow = null;
                return;
            }

            FollowState state = follow;
            state.Checking = true;
            string playerId = state.Request.PlayerId, cookie = asker.Cookie, askerName = asker.Name;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                PlayerWhere w;
                try { w = RobloxPlayers.Where(playerId, cookie, out error); }
                catch (Exception ex) { w = new PlayerWhere(); error = ex.Message; }
                OnUi(delegate { FollowSaw(state, w, error, askerName); });
            });
        }

        void FollowSaw(FollowState state, PlayerWhere w, string error, string asker)
        {
            if (!ReferenceEquals(state, follow)) return;       // stopped meanwhile
            DateTime now = DateTime.Now;
            state.Looked(now);

            string player = state.Request.Player;
            bool there = w.Type == PlayerWhere.IN_GAME && !string.IsNullOrEmpty(w.GameId) && !string.IsNullOrEmpty(w.PlaceId);
            int seen = there ? PlayerWhere.IN_GAME : (w.Type == PlayerWhere.IN_GAME ? -3 : w.Type);
            if (seen != state.LastSeenType)
            {
                bool first = state.LastSeenType == -2;
                state.LastSeenType = seen;
                if (seen == -3)
                    Log(player + " is in a game, but their privacy settings don't let " + asker
                        + " join them - the accounts stay where they are.");
                else if (seen == PlayerWhere.UNKNOWN)
                    Log("Couldn't find out where " + player + " is" + (error != null ? ": " + error : "") + " - trying again shortly.");
                else if (!there)
                    Log(player + " isn't in a game right now - the accounts stay where they are until they join one.");
                else if (!first)
                    Log(player + " is in a game again - following.");
            }
            if (!there) return;

            string next = state.NextToMove(w.GameId, JobOfAccount, IsHuntingAccount, now);
            if (next != null) MoveFollower(state, next, w);
        }

        // Following takes its turn in the hunts' line: a hunt due to move
        // while an account follows waits in it, and goes as soon as this is
        // done. Not an account name, so it can never be mistaken for one.
        const string FOLLOW_TURN = "\u0001following";

        void MoveFollower(FollowState state, string account, PlayerWhere w)
        {
            EnsureAccounts();
            RobloxAccount acc = accounts.Find(account);
            if (acc == null || !acc.IsUsable) return;
            // A hunt started moving while Roblox was being asked; next time.
            if (hopLine.Moving || !hopLine.Ask(FOLLOW_TURN)) return;

            state.MoveStarted(account, DateTime.Now);
            LaunchRequest r = new LaunchRequest();
            r.ServerPlaceId = w.PlaceId;
            r.ServerId = w.GameId;
            LaunchSeat s = new LaunchSeat();
            s.Account = acc.Name;
            s.Cookie = acc.Cookie;
            s.RunningPid = PidOfAccount(acc.Name);
            r.Seats.Add(s);
            Log(state.Request.Player + " moved server - moving " + acc.Name + " there too.");

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LaunchResult> results;
                try { results = AccountLauncher.Run(r, new LiveLaunchWorld(), new Random(), null); }
                catch (Exception ex)
                {
                    LaunchResult failed = new LaunchResult();
                    failed.Account = account;
                    failed.Problem = ex.Message;
                    results = new List<LaunchResult> { failed };
                }
                OnUi(delegate
                {
                    foreach (LaunchResult res in results)
                    {
                        if (res.Pid > 0) clientLabels.Assign(res.Pid, res.Account);
                        if (res.Problem != null) Log("Couldn't move " + res.Account + " after " + state.Request.Player + ": " + res.Problem);
                    }
                    state.MoveDone(DateTime.Now);
                    NextInLine(FOLLOW_TURN);
                });
            });
        }
    }
}
