using System;

namespace RobloxKeeper.Tests
{
    // Reading Roblox's own client logs.
    //
    // Roblox knows exactly why a client dropped and writes it down; the dialog
    // it shows the user does not. A client kicked for being idle and a client
    // evicted as a duplicate login both appear as "lost connection" on screen,
    // and telling them apart is the difference between "your anti-AFK isn't
    // reaching that client" and "your accounts are fighting over one session".
    //
    // Every fixture here is a real line taken from the logs on the machine this
    // was built against, not an invented one.
    static class RobloxLogTests
    {
        // ---------- joining ----------

        public static void TestReadsAJoinLine()
        {
            string line = "2026-09-18T00:27:40.382Z,1.382481,5db4,6 [FLog::Output] "
                        + "! Joining game '62489e3a-435c-49e5-860a-0eaaa56a8264' place 107778070777162 at 10.206.16.156";

            RobloxLogEvent e = RobloxLog.Parse(line);

            Assert.Equal(RobloxLogEvent.Kind.Joined, e.Type, "a join");
            Assert.Equal("107778070777162", e.PlaceId, "the place");
        }

        public static void TestAnOrdinaryLineIsNotAnEvent()
        {
            Assert.Equal(RobloxLogEvent.Kind.None,
                RobloxLog.Parse("2026-09-18T08:09:14.097Z,27695.09,2d1c,6,Info [FLog::WndProcessCheck] "
                              + "waitForNewPlayerProcess new waiting for mutex result is 0X000102").Type,
                "the mutex-wait spam is not an event");
            Assert.Equal(RobloxLogEvent.Kind.None, RobloxLog.Parse("").Type, "empty");
            Assert.Equal(RobloxLogEvent.Kind.None, RobloxLog.Parse(null).Type, "null");
        }

        // ---------- disconnects ----------

        public static void TestReadsADuplicateLoginEviction()
        {
            // The one that started all of this.
            string line = "2026-09-18T08:09:16.984Z,27697.98,5300,7 [FLog::Network] Disconnect reason received: 273";

            RobloxLogEvent e = RobloxLog.Parse(line);

            Assert.Equal(RobloxLogEvent.Kind.Disconnected, e.Type, "a disconnect");
            Assert.Equal(273, e.Reason, "reason 273");
        }

        public static void TestReadsAnIdleKick()
        {
            // The client kicks itself, so it reports the reason a different way.
            string line = "2026-09-20T21:09:31.454Z,2132.45,3d74,6,Info [DFLog::NetworkClient] "
                        + "Client:Disconnect setting replicator disconnect reason to 278";

            RobloxLogEvent e = RobloxLog.Parse(line);

            Assert.Equal(RobloxLogEvent.Kind.Disconnected, e.Type, "a disconnect");
            Assert.Equal(278, e.Reason, "reason 278");
        }

        public static void TestReadsASentDisconnect()
        {
            string line = "2026-09-20T16:10:41.426Z,1176.42,b714,7 [FLog::Network] Sending disconnect with reason: 285";

            RobloxLogEvent e = RobloxLog.Parse(line);
            Assert.Equal(RobloxLogEvent.Kind.Disconnected, e.Type, "a disconnect");
            Assert.Equal(285, e.Reason, "reason 285");
        }

        public static void TestReadsAHangDeath()
        {
            // Not a disconnect at all - Roblox's own watchdog killed the client.
            string line = "2026-09-18T06:57:53.587Z,23390.58,5014,6,Error [FLog::HangMonitor] "
                        + "Timeout while checking if the monitor step should be skipped";

            Assert.Equal(RobloxLogEvent.Kind.Hung, RobloxLog.Parse(line).Type,
                "a hang, which looks nothing like a disconnect and must not be reported as one");
        }

        // ---------- saying what it means ----------

        public static void TestTheDuplicateLoginIsExplainedAsNotYourInternet()
        {
            string plain = RobloxLog.Explain(273);

            Assert.Contains("another device", plain, "names the actual cause");
            Assert.False(plain.Contains("273") && plain.Length < 30, "not just the number back");
        }

        public static void TestTheIdleKickIsExplainedAsIdle()
        {
            Assert.Contains("idle", RobloxLog.Explain(278).ToLowerInvariant(),
                "so 'my anti-AFK isn't working' is distinguishable from a network fault");
        }

        public static void TestLeavingOnPurposeIsNotReportedAsAFault()
        {
            Assert.True(RobloxLog.IsNormalExit(285), "the client left the game itself");
            Assert.False(RobloxLog.IsNormalExit(273), "an eviction is not a normal exit");
            Assert.False(RobloxLog.IsNormalExit(278), "nor an idle kick");
        }

        public static void TestAnUnknownReasonStillSaysSomethingUseful()
        {
            string plain = RobloxLog.Explain(9999);
            Assert.Contains("9999", plain, "carries the code so it can be looked up");
            Assert.True(plain.Length > 10, "and is a sentence, not just a number");
        }

        // ---------- matching a log file to a client ----------

        public static void TestALogIsMatchedToTheClientThatStartedWithIt()
        {
            // Log names carry a UTC timestamp; a client's start time is local.
            DateTime started = new DateTime(2026, 9, 20, 20, 33, 59, DateTimeKind.Utc);

            Assert.True(RobloxLog.LooksLikeSameSession(
                "0.739.0.7390687_20260920T203359Z_Player_1EF47_last.log", started),
                "same second");
            Assert.True(RobloxLog.LooksLikeSameSession(
                "0.739.0.7390687_20260920T203404Z_Player_1EF47_last.log", started),
                "a few seconds later still counts - the process starts before it logs");
        }

        public static void TestADifferentSessionIsNotMatched()
        {
            DateTime started = new DateTime(2026, 9, 20, 20, 33, 59, DateTimeKind.Utc);

            Assert.False(RobloxLog.LooksLikeSameSession(
                "0.739.0.7390687_20260920T210000Z_Player_1EF47_last.log", started),
                "half an hour apart is a different client");
        }

        public static void TestANonLogNameIsNotMatched()
        {
            Assert.False(RobloxLog.LooksLikeSameSession("notalog.txt", DateTime.UtcNow), "not a log");
            Assert.False(RobloxLog.LooksLikeSameSession(null, DateTime.UtcNow), "null");
        }

        // The join line carries the server as well as the place. Keeping it is
        // what lets a detection link back into the exact server it happened in.
        public static void TestJoiningRecordsTheServerAsWellAsThePlace()
        {
            RobloxLogEvent e = RobloxLog.Parse(
                "2026-09-21T11:49:33.123Z,0.123,abcd,6 [FLog::Output] ! Joining game " +
                "'c1c5a3b9-4938-4cd8-9418-ca1a217858ae' place 107778070777162 at 10.30.4.209");
            Assert.Equal(RobloxLogEvent.Kind.Joined, e.Type, "a join");
            Assert.Equal("107778070777162", e.PlaceId, "the place");
            Assert.Equal("c1c5a3b9-4938-4cd8-9418-ca1a217858ae", e.JobId, "the server");
        }

        public static void TestAJoinLineWithoutAServerStillParses()
        {
            RobloxLogEvent e = RobloxLog.Parse("! Joining game '' place 1234 at 10.0.0.1");
            Assert.Equal(RobloxLogEvent.Kind.Joined, e.Type, "still a join");
            Assert.Equal("1234", e.PlaceId, "the place survives");
            Assert.Equal("", e.JobId, "no server, and that is not an error");
        }

        public static void TestAJoinLinkNamesBothThePlaceAndTheServer()
        {
            string link = RobloxLog.JoinLink("142823291", "55cf1f30-d19e-4a37-84aa-609ff3c1d3a0");
            Assert.Contains("placeId=142823291", link, "the place");
            Assert.Contains("gameInstanceId=55cf1f30-d19e-4a37-84aa-609ff3c1d3a0", link, "the server");
        }

        // A link that quietly dropped the server would drop you into a random
        // copy of the game, which looks exactly like the feature being broken.
        public static void TestThereIsNoJoinLinkWithoutAServer()
        {
            Assert.Equal(null, RobloxLog.JoinLink("142823291", null), "no server, no link");
            Assert.Equal(null, RobloxLog.JoinLink(null, "abc"), "no place, no link");
        }

    }
}
