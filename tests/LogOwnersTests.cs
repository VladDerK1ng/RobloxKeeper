using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Which client writes which log - and so which client a join, the account
    // it names, or a disconnect belongs to.
    //
    // Issue #1: of seven clients, only some showed their account; the rest
    // stayed "Client 2", "Client 3". Logs were matched to clients by the time
    // in their names, and that cannot tell clients launched together apart:
    // they start three seconds apart, and - measured on live 0.744 clients -
    // a log opens three to four seconds after its client starts. Every log of
    // a batch fitted the first client, which took each account's name in turn.
    static class LogOwnersTests
    {
        static readonly DateTime T = new DateTime(2026, 9, 21, 11, 49, 30, DateTimeKind.Utc);

        const string LogA = "0.744.0_20260921T114933Z_Player_AAAAA_last.log";
        const string LogB = "0.744.0_20260921T114936Z_Player_BBBBB_last.log";
        const string LogC = "0.744.0_20260921T114939Z_Player_CCCCC_last.log";

        static ClientInfo Info(int pid, DateTime startedUtc)
        {
            ClientInfo c = new ClientInfo();
            c.Pid = pid;
            c.Start = startedUtc.ToLocalTime();
            return c;
        }

        // Three clients launched together, three seconds apart.
        static List<ClientInfo> Batch()
        {
            List<ClientInfo> clients = new List<ClientInfo>();
            clients.Add(Info(10, T));
            clients.Add(Info(20, T.AddSeconds(3)));
            clients.Add(Info(30, T.AddSeconds(6)));
            return clients;
        }

        static LogOwners Owners(Dictionary<string, int> open, List<string> asked)
        {
            LogOwners o = new LogOwners();
            o.Writers = delegate(string name)
            {
                if (asked != null) asked.Add(name);
                int pid;
                return open.TryGetValue(name, out pid) ? new List<int> { pid } : new List<int>();
            };
            return o;
        }

        public static void TestClientsLaunchedTogetherEachGetTheirOwnLog()
        {
            Dictionary<string, int> open = new Dictionary<string, int>();
            open[LogA] = 10;
            open[LogB] = 20;
            open[LogC] = 30;
            LogOwners o = Owners(open, null);
            List<int> alive = new List<int> { 10, 20, 30 };

            Assert.Equal(10, o.PidFor(LogA, alive, Batch()), "first client's log");
            Assert.Equal(20, o.PidFor(LogB, alive, Batch()), "second's - by the clock it fitted the first");
            Assert.Equal(30, o.PidFor(LogC, alive, Batch()), "third's");
        }

        // Roblox's crash handler keeps a log of its own beside each client's,
        // named almost the same and opened the same second.
        public static void TestALogOnlyAnotherProgramHasOpenIsNoClients()
        {
            Dictionary<string, int> open = new Dictionary<string, int>();
            open["0.744.0_20260921T114933Z_Player_DC63F_CrashHandler_last.log"] = 4242;
            Assert.Equal(0, Owners(open, null).PidFor("0.744.0_20260921T114933Z_Player_DC63F_CrashHandler_last.log",
                new List<int> { 10 }, Batch()), "the crash handler's, though the clock fits client 10");
        }

        // Nobody has it open: its client has exited. A late line read from it
        // is not put on another client that started around then.
        public static void TestALogNobodyHasOpenIsNoClients()
        {
            Assert.Equal(0, Owners(new Dictionary<string, int>(), null).PidFor(LogA, new List<int> { 10, 20, 30 }, Batch()),
                "closed");
        }

        // Before its window shows, a client isn't in the list of clients yet -
        // its log is still its own.
        public static void TestAClientWithoutAWindowYetStillOwnsItsLog()
        {
            Dictionary<string, int> open = new Dictionary<string, int>();
            open[LogA] = 40;
            Assert.Equal(40, Owners(open, null).PidFor(LogA, new List<int> { 10, 40 }, new List<ClientInfo>()),
                "alive, no window");
        }

        // Windows looks through every handle on the PC to answer, so each log
        // is asked about once while its client runs - and forgotten when the
        // client exits, because its PID can go to another process.
        public static void TestEachLogIsAskedAboutOnceWhileItsClientRuns()
        {
            Dictionary<string, int> open = new Dictionary<string, int>();
            open[LogB] = 20;
            List<string> asked = new List<string>();
            LogOwners o = Owners(open, asked);
            List<int> alive = new List<int> { 10, 20, 30 };

            o.PidFor(LogB, alive, Batch());
            o.Prune(alive);
            Assert.Equal(20, o.PidFor(LogB, alive, Batch()), "still known");
            Assert.Equal(1, asked.Count, "asked once");

            List<int> without20 = new List<int> { 10, 30 };
            Assert.Equal(0, o.PidFor(LogB, without20, Batch()), "its client has exited");
            o.Prune(without20);
            open.Remove(LogB);
            Assert.Equal(0, o.PidFor(LogB, new List<int> { 10, 20, 30 }, Batch()), "PID 20 again is someone else");
            Assert.Equal(2, asked.Count, "asked again once forgotten");
        }

        // A log no client has open yet is asked about again next time, not
        // written off.
        public static void TestNoAnswerIsNotKept()
        {
            Dictionary<string, int> open = new Dictionary<string, int>();
            LogOwners o = Owners(open, null);
            List<int> alive = new List<int> { 10, 20 };
            Assert.Equal(0, o.PidFor(LogA, alive, Batch()), "not yet");
            open[LogA] = 20;
            Assert.Equal(20, o.PidFor(LogA, alive, Batch()), "now");
        }

        // When Windows can't say, the clock decides - but only when one client
        // fits. Two that fit are not guessed between.
        public static void TestByTheClockAloneOnlyAClientThatFitsAloneIsTaken()
        {
            LogOwners o = new LogOwners();
            o.Writers = delegate { return null; };
            List<ClientInfo> clients = new List<ClientInfo>();
            clients.Add(Info(10, new DateTime(2026, 9, 21, 10, 40, 0, DateTimeKind.Utc)));
            clients.Add(Info(20, T));
            List<int> alive = new List<int> { 10, 20, 30 };

            Assert.Equal(20, o.PidFor(LogA, alive, clients), "opened three seconds after client 20 started");
            Assert.Equal(0, o.PidFor("0.744.0_20260921T090000Z_Player_AAAAA_last.log", alive, clients),
                "a log from before either client");

            Assert.Equal(0, o.PidFor(LogB, alive, Batch()), "clients 10 and 20 both fit, and 30 too");

            o.Writers = delegate(string name) { throw new InvalidOperationException(); };
            Assert.Equal(0, o.PidFor(LogB, alive, Batch()), "the same when asking throws");
        }

        // Two clients can't write one log; if Windows ever said so, neither is
        // picked.
        public static void TestTwoClientsHoldingOneLogAreNotGuessedBetween()
        {
            LogOwners o = new LogOwners();
            o.Writers = delegate { return new List<int> { 10, 20 }; };
            Assert.Equal(0, o.PidFor(LogA, new List<int> { 10, 20 }, Batch()), "two");
            o.Writers = delegate { return new List<int> { 20, 20, 4242 }; };
            Assert.Equal(20, o.PidFor(LogA, new List<int> { 10, 20 }, Batch()), "one client, said twice, and the crash handler");
        }

        // The real thing: Windows says which process has a file open.
        public static void TestWindowsSaysWhoHasAFileOpen()
        {
            string path = Path.Combine(Path.GetTempPath(), "rk-open-" + Guid.NewGuid().ToString("N") + ".log");
            try
            {
                using (FileStream held = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    IList<int> holders = OpenFiles.Holders(path);
                    Assert.True(holders != null, "Windows answered");
                    Assert.True(holders.Contains(Process.GetCurrentProcess().Id), "this process holds it");
                }
                IList<int> after = OpenFiles.Holders(path);
                Assert.True(after != null && after.Count == 0, "nobody once it's closed");
            }
            finally { try { File.Delete(path); } catch { } }

            IList<int> gone = OpenFiles.Holders(path);
            Assert.True(gone != null && gone.Count == 0, "a file that isn't there is held by nobody");
        }
    }
}
