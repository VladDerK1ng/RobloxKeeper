using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Knowing which game and server each client is in.
    //
    // Boxes belong to a game and a detection links back into its server, so
    // the watchers need both for every client - including one that was already
    // in a game before the app started, whose join line was written long
    // before anything was watching the log.
    static class RobloxLogWatchTests
    {
        const string LogName = "0.643.0.6430876_20260921T114933Z_Player_2A3F1_last.log";
        const string JoinA = "2026-09-21T11:49:40.123Z,0.1,ab,6 [FLog::Output] ! Joining game "
                           + "'aaaaaaaa-1111-2222-3333-444444444444' place 111 at 10.0.0.1";
        const string JoinB = "2026-09-21T11:58:02.456Z,0.1,ab,6 [FLog::Output] ! Joining game "
                           + "'bbbbbbbb-1111-2222-3333-444444444444' place 222 at 10.0.0.2";

        class Rig : IDisposable
        {
            public readonly string Dir = Path.Combine(Path.GetTempPath(), "rk-logs-" + Guid.NewGuid().ToString("N"));
            public readonly List<string> Log = new List<string>();
            public readonly List<string> Files = new List<string>();
            public readonly List<RobloxLogEvent> Joins = new List<RobloxLogEvent>();
            public readonly RobloxLogWatch Watch;

            public Rig()
            {
                Directory.CreateDirectory(Dir);
                Watch = new RobloxLogWatch(Dir);
                Watch.Log = delegate(string s) { Log.Add(s); };
                Watch.Joined = delegate(string file, RobloxLogEvent e) { Files.Add(file); Joins.Add(e); };
            }

            public void Write(string text) { File.WriteAllText(Path.Combine(Dir, LogName), text); }
            public void Append(string text) { File.AppendAllText(Path.Combine(Dir, LogName), text); }

            public void Dispose()
            {
                try { Directory.Delete(Dir, true); } catch { }
            }
        }

        // The client was already in a game when watching started. Its last
        // join says where it is now; the earlier one is where it used to be.
        public static void TestAClientAlreadyInAGameIsKnownWhenWatchingStarts()
        {
            using (Rig r = new Rig())
            {
                r.Write("starting up\r\n" + JoinA + "\r\nplaying\r\n" + JoinB + "\r\nstill playing\r\n");
                r.Watch.Tick();

                Assert.Equal(1, r.Joins.Count, "told once, about where it is now");
                Assert.Equal("222", r.Joins[0].PlaceId, "the last join, not the first");
                Assert.Equal("bbbbbbbb-1111-2222-3333-444444444444", r.Joins[0].JobId, "and its server");
                Assert.Equal(LogName, r.Files[0], "named by its log, so it can be matched to its client");
                Assert.Equal(0, r.Log.Count, "history is not replayed into the activity list");
            }
        }

        // A rule set to play a macro when a client joins must not fire on
        // every client the moment the app starts, for joins that happened
        // hours ago. A join already in a log from before watching started is
        // told as earlier.
        public static void TestAJoinFromBeforeWatchingStartedIsMarkedEarlier()
        {
            using (Rig r = new Rig())
            {
                r.Write(JoinA + "\r\n");
                r.Watch.Started = File.GetCreationTime(Path.Combine(r.Dir, LogName)).AddSeconds(1);
                r.Watch.Tick();
                Assert.Equal(1, r.Joins.Count, "still known - the watchers need to know where it is");
                Assert.True(r.Joins[0].Earlier, "but marked as having happened before");
            }
        }

        // A client started after watching began can have its first join
        // written before the log is first looked at. That is still news.
        public static void TestAJoinInALogStartedSinceWatchingBeganIsNew()
        {
            using (Rig r = new Rig())
            {
                r.Write(JoinA + "\r\n");        // the log appeared after the watch was made
                r.Watch.Tick();
                Assert.Equal(1, r.Joins.Count, "reported");
                Assert.False(r.Joins[0].Earlier, "as new");

                r.Append(JoinB + "\r\n");
                r.Watch.Tick();
                Assert.False(r.Joins[1].Earlier, "and so is every join after");
            }
        }

        public static void TestANewJoinIsReportedAsItHappens()
        {
            using (Rig r = new Rig())
            {
                r.Write("starting up\r\n");
                r.Watch.Tick();
                Assert.Equal(0, r.Joins.Count, "not in a game yet");

                r.Append(JoinA + "\r\n");
                r.Watch.Tick();
                Assert.Equal(1, r.Joins.Count, "joined");
                Assert.Equal("111", r.Joins[0].PlaceId, "that game");
                Assert.Contains("joined place 111", r.Log[0], "and the activity list says so, as before");
            }
        }

        // A teleport is a second join in the same log, and the client is now
        // somewhere else.
        public static void TestMovingToAnotherServerIsReportedToo()
        {
            using (Rig r = new Rig())
            {
                r.Write(JoinA + "\r\n");
                r.Watch.Tick();
                r.Append(JoinB + "\r\n");
                r.Watch.Tick();
                Assert.Equal(2, r.Joins.Count, "both");
                Assert.Equal("222", r.Joins[1].PlaceId, "and the latest is where it is");
            }
        }
    }
}
