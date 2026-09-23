using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Keep following a player: when they move to another server, the
    // accounts go after them - one at a time, the way hunt hops go.
    static class FollowTests
    {
        static readonly DateTime T0 = new DateTime(2026, 9, 23, 12, 0, 0);
        const string THERE = "aaaaaaaa-0000-0000-0000-000000000000";
        const string ELSEWHERE = "bbbbbbbb-0000-0000-0000-000000000000";

        static FollowState State(params string[] accounts)
        {
            FollowRequest r = new FollowRequest();
            r.Player = "main";
            r.PlayerId = "555";
            r.Accounts.AddRange(accounts);
            return new FollowState(r, T0);
        }

        static Func<string, string> Jobs(Dictionary<string, string> jobs)
        {
            return delegate(string a) { string j; return jobs.TryGetValue(a, out j) ? j : null; };
        }

        static readonly Func<string, bool> NotHunting = delegate { return false; };

        // Clients just launched are given time to join before the first look.
        public static void TestTheFirstLookWaitsForTheLaunchedClientsToJoin()
        {
            FollowState s = State("a");
            Assert.False(s.CheckDue(T0.AddSeconds(5)), "not straight away");
            Assert.True(s.CheckDue(T0.AddSeconds(31)), "half a minute on");
        }

        public static void TestOnlyAccountsElsewhereAreMoved()
        {
            FollowState s = State("a", "b");
            Dictionary<string, string> jobs = new Dictionary<string, string>();
            jobs["a"] = THERE;
            jobs["b"] = ELSEWHERE;
            DateTime later = T0.AddSeconds(90);
            Assert.Equal("b", s.NextToMove(THERE, Jobs(jobs), NotHunting, later), "b is elsewhere");
            jobs["b"] = THERE;
            Assert.Equal(null, s.NextToMove(THERE, Jobs(jobs), NotHunting, later), "everyone is with them");
        }

        // One move at a time. An account launched or moved in the last minute
        // is still joining - what it last said about where it is is stale -
        // so it isn't moved again for that. Nor is one a hunt is moving, or
        // one that hasn't said where it is yet.
        public static void TestNothingIsMovedTwiceOrFromUnderAHunt()
        {
            FollowState s = State("a", "b", "c");
            Dictionary<string, string> jobs = new Dictionary<string, string>();
            jobs["a"] = ELSEWHERE;
            jobs["b"] = ELSEWHERE;
            Assert.Equal(null, s.NextToMove(THERE, Jobs(jobs), NotHunting, T0.AddSeconds(20)), "all still joining");
            Assert.Equal("a", s.NextToMove(THERE, Jobs(jobs), NotHunting, T0.AddSeconds(90)), "the first elsewhere");
            s.MoveStarted("a", T0.AddSeconds(90));
            Assert.Equal(null, s.NextToMove(THERE, Jobs(jobs), NotHunting, T0.AddSeconds(95)), "one move at a time");
            s.MoveDone(T0.AddSeconds(110));
            Assert.Equal("b", s.NextToMove(THERE, Jobs(jobs), NotHunting, T0.AddSeconds(110)), "then the next - a is still joining");
            Assert.Equal(null, s.NextToMove(THERE, Jobs(jobs), delegate(string x) { return x == "b"; }, T0.AddSeconds(110)),
                         "not one that is hunting; c hasn't said where it is");
        }

        // After a move, look again at once for the next one, rather than half
        // a minute per account.
        public static void TestAFinishedMoveLooksAgainStraightAway()
        {
            FollowState s = State("a");
            s.Looked(T0.AddSeconds(40));
            Assert.False(s.CheckDue(T0.AddSeconds(45)), "just looked");
            s.MoveStarted("a", T0.AddSeconds(45));
            s.MoveDone(T0.AddSeconds(60));
            Assert.True(s.CheckDue(T0.AddSeconds(60)), "looks again now");
        }

        // A client you close yourself stops following; one that is only
        // restarting - launched or moved a moment ago - doesn't.
        public static void TestClosingAClientStopsItFollowing()
        {
            FollowState s = State("a", "b");
            Func<string, bool> onlyA = delegate(string x) { return x == "a"; };
            Assert.Equal(0, s.Dropped(onlyA, T0.AddSeconds(30)).Count, "b may still be starting");
            List<string> gone = s.Dropped(onlyA, T0.AddSeconds(100));
            Assert.Equal("b", string.Join(",", gone.ToArray()), "b has been gone too long");
            Assert.Equal("a", string.Join(",", s.Request.Accounts.ToArray()), "a follows on");
        }
    }
}
