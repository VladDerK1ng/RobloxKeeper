using System;

namespace RobloxKeeper.Tests
{
    // Hunt mode for one account: hop, look, hop again - and stop when a
    // watcher finds something. Driven by a fake clock and the events the app
    // would feed it; it only ever says what to do next.
    static class HuntTests
    {
        static readonly DateTime T0 = new DateTime(2026, 9, 22, 18, 0, 0);
        const string Job1 = "a7c31f39-0d44-4d46-b900-7d94e7eb0aa5";
        const string Job2 = "96447192-67fc-4bf9-99fa-8e144c3889e8";

        static Hunt New()
        {
            Hunt h = new Hunt("VladDerKing");
            h.LookSeconds = 60;
            h.JoinTimeoutSeconds = 90;
            h.StayWhenFound = true;
            return h;
        }

        public static void TestAHuntStartsByMovingToAServer()
        {
            Hunt h = New();
            Assert.Equal(HuntOrder.Hop, h.Tick(T0), "go somewhere");
            Assert.Equal(HuntOrder.None, h.Tick(T0.AddSeconds(1)), "and only once while that is under way");
        }

        public static void TestItLooksForAsLongAsItWasToldThenMovesOn()
        {
            Hunt h = New();
            h.Tick(T0);
            h.HopStarted(T0.AddSeconds(3));
            h.Joined(Job1, T0.AddSeconds(20));
            Assert.Equal(HuntStage.Looking, h.Stage, "in a server, looking");
            Assert.Equal(HuntOrder.None, h.Tick(T0.AddSeconds(79)), "still looking at 59 seconds");
            Assert.Equal(HuntOrder.Hop, h.Tick(T0.AddSeconds(80)), "a minute in, move on");
            Assert.Equal(1, h.Servers, "one server looked in");
        }

        // The whole point: something turned up, so stay in that server.
        public static void TestFindingSomethingStopsTheHuntInThatServer()
        {
            Hunt h = New();
            h.Tick(T0);
            h.HopStarted(T0);
            h.Joined(Job1, T0.AddSeconds(15));
            Assert.True(h.Found("Secret chat", T0.AddSeconds(30)), "counted");
            Assert.Equal(HuntStage.Found, h.Stage, "found");
            Assert.Equal(HuntOrder.None, h.Tick(T0.AddMinutes(10)), "and it never moves again");
            Assert.Contains("Secret chat", h.Describe(T0.AddMinutes(10)), "says what");
            Assert.Contains("staying", h.Describe(T0.AddMinutes(10)), "and that it stays");
        }

        // Told not to stay, it notes the find and keeps hunting.
        public static void TestItCanBeToldToKeepGoingAfterAFind()
        {
            Hunt h = New();
            h.StayWhenFound = false;
            h.Tick(T0);
            h.HopStarted(T0);
            h.Joined(Job1, T0);
            Assert.False(h.Found("Secret chat", T0.AddSeconds(5)), "not a reason to stop");
            Assert.Equal(HuntOrder.Hop, h.Tick(T0.AddSeconds(60)), "moves on as usual");
            Assert.Equal(1, h.Finds, "but counted");
        }

        // A detection raised by the last server, arriving late, is not a find
        // in a server it has not reached yet.
        public static void TestAFindBeforeArrivingDoesNotCount()
        {
            Hunt h = New();
            h.Tick(T0);
            h.HopStarted(T0);
            Assert.False(h.Found("Secret chat", T0.AddSeconds(2)), "still joining");
            Assert.Equal(HuntStage.Joining, h.Stage, "so still joining");
        }

        public static void TestAJoinThatNeverHappensIsTriedAgain()
        {
            Hunt h = New();
            h.Tick(T0);
            h.HopStarted(T0);
            Assert.Equal(HuntOrder.None, h.Tick(T0.AddSeconds(89)), "waiting to arrive");
            Assert.Equal(HuntOrder.Hop, h.Tick(T0.AddSeconds(90)), "ninety seconds and no join - another server");
            Assert.Equal(1, h.FailuresInARow, "counted as a failure");
        }

        // A failure waits a little before trying again, and five in a row
        // stops the hunt with the reason - rather than closing and opening a
        // client for ever.
        public static void TestFailuresBackOffThenGiveUp()
        {
            Hunt h = New();
            DateTime t = T0;
            for (int i = 1; i <= 4; i++)
            {
                Assert.Equal(HuntOrder.Hop, h.Tick(t), "try " + i);
                h.HopFailed("Roblox asked to slow down", t);
                Assert.Equal(HuntOrder.None, h.Tick(t.AddSeconds(Hunt.RETRY_SECONDS - 1)), "waits before try " + (i + 1));
                t = t.AddSeconds(Hunt.RETRY_SECONDS);
            }
            h.Tick(t);
            h.HopFailed("Roblox asked to slow down", t);
            Assert.Equal(HuntStage.Stopped, h.Stage, "five in a row stops it");
            Assert.Contains("slow down", h.Describe(t), "and says why");
            Assert.Equal(HuntOrder.None, h.Tick(t.AddMinutes(5)), "for good");
        }

        public static void TestArrivingSomewhereClearsTheFailures()
        {
            Hunt h = New();
            h.Tick(T0);
            h.HopFailed("x", T0);
            h.Tick(T0.AddSeconds(Hunt.RETRY_SECONDS));
            h.HopStarted(T0.AddSeconds(Hunt.RETRY_SECONDS));
            h.Joined(Job2, T0.AddSeconds(30));
            Assert.Equal(0, h.FailuresInARow, "a good hop wipes the slate");
            Assert.Equal(Job2, h.JobId, "and it knows where it is");
        }

        public static void TestAStoppedHuntStaysStopped()
        {
            Hunt h = New();
            h.Tick(T0);
            h.Stop();
            Assert.Equal(HuntOrder.None, h.Tick(T0.AddMinutes(1)), "no more hops");
            Assert.Equal(HuntStage.Stopped, h.Stage, "stopped");
        }

        // ---------- one move at a time ----------

        // Accounts due to move in the same second picked from the same list
        // at the same moment, and with the emptiest or busiest few to choose
        // from, two landed together one time in four.
        public static void TestOnlyOneAccountMovesAtATime()
        {
            HopLine line = new HopLine();
            Assert.True(line.Ask("VladDerKing"), "the first moves at once");
            Assert.False(line.Ask("alt1"), "the second waits");
            Assert.False(line.Ask("farm2"), "and the third");
            Assert.False(line.Ask("alt1"), "asking again doesn't queue it twice");

            Assert.Equal("alt1", line.Done("VladDerKing"), "then the next in line");
            Assert.Equal("farm2", line.Done("alt1"), "and the one after");
            Assert.Equal(null, line.Done("farm2"), "then nobody");
            Assert.True(line.Ask("VladDerKing"), "and the line is free again");
        }

        // A move that finishes after its hunt was stopped doesn't hand the
        // turn to anyone.
        public static void TestOnlyTheAccountMovingCanFinishItsTurn()
        {
            HopLine line = new HopLine();
            line.Ask("VladDerKing");
            line.Ask("alt1");
            Assert.Equal(null, line.Done("somebody else"), "not the one moving");
            Assert.True(line.Moving, "still moving");
            Assert.Equal("alt1", line.Done("VladDerKing"), "the real one");
        }

        public static void TestClearingTheLineForgetsEveryone()
        {
            HopLine line = new HopLine();
            line.Ask("VladDerKing");
            line.Ask("alt1");
            line.Clear();
            Assert.False(line.Moving, "nobody moving");
            Assert.True(line.Ask("farm2"), "free");
            Assert.Equal(null, line.Done("farm2"), "and alt1 is no longer waiting");
        }

        public static void TestItSaysWhereItIsUpTo()
        {
            Hunt h = New();
            Assert.Contains("finding a server", h.Describe(T0), "starting");
            h.Tick(T0);
            h.HopStarted(T0);
            Assert.Contains("joining", h.Describe(T0.AddSeconds(5)), "on its way");
            h.Joined(Job1, T0.AddSeconds(10));
            Assert.Contains("50s left", h.Describe(T0.AddSeconds(20)), "time left in this one");
            Assert.Contains("server 1", h.Describe(T0.AddSeconds(20)), "which server this is");
        }
    }
}
