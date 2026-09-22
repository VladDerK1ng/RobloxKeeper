using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper.Tests
{
    // The Hunt window, against a pretend hunt: what it passes on, what it
    // remembers, and what it shows.
    static class HuntDialogTests
    {
        class FakeHunts : IHuntControl
        {
            public HuntSettings Started;
            public string Refuse;
            public bool IsRunning;
            public int Stops;
            public List<string> Said = new List<string>();

            public string Start(HuntSettings s)
            {
                if (Refuse != null) return Refuse;
                Started = s;
                IsRunning = true;
                return null;
            }

            public void Stop() { Stops++; IsRunning = false; }
            public bool Running { get { return IsRunning; } }
            public IList<string> Lines(DateTime now) { return Said; }
        }

        class NoCapture : ICapture
        {
            public Pixels Grab(int pid, out WatchState state) { state = WatchState.NotInAGame; return null; }
        }

        class NoReader : IReadText
        {
            public string Read(Pixels image) { return ""; }
        }

        static WatchKit Kit(string path, FakeHunts hunts)
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(path);
            k.Capture = new NoCapture();
            k.Reader = new NoReader();
            k.Engine = new WatchEngine(k.Capture, k.Reader);
            k.Clients = delegate { return new WatchedClient[0]; };
            k.Accounts = delegate { return (IList<string>)new List<string> { "VladDerKing", "alt1" }; };
            k.Log = delegate(string s) { };
            k.Hunts = hunts;
            return k;
        }

        static string TempStore()
        {
            return Path.Combine(Path.GetTempPath(), "rk-huntui-" + Guid.NewGuid().ToString("N") + ".dat");
        }

        public static void TestStartingPassesTheSettingsOnAndRemembersThem()
        {
            string path = TempStore();
            try
            {
                FakeHunts f = new FakeHunts();
                WatchKit k = Kit(path, f);
                using (HuntDialog d = new HuntDialog(k))
                {
                    Assert.Equal(2, d.AccountChoices, "every saved account");
                    d.SetGame("https://www.roblox.com/games/107778070777162/Steal-an-Egg");
                    d.SetLook(45);
                    d.TickAccount("alt1", true);
                    Assert.Equal(null, d.StartOrStop(), "started");
                }
                Assert.Equal(1, f.Started.Accounts.Length, "one account");
                Assert.Equal("alt1", f.Started.Accounts[0], "the ticked one");
                Assert.Equal(45, f.Started.LookSeconds, "the time");

                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.Contains("107778070777162", back.Hunt.GameLink, "remembered for next time");

                WatchKit again = Kit(path, new FakeHunts());
                again.Store.Load();                 // as the app does when it starts
                using (HuntDialog d = new HuntDialog(again))
                    Assert.True(d.IsTicked("alt1"), "and shown ticked again");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // Busy servers for a bounty, quiet ones for something anyone can
        // take - chosen here and passed on to the hunt.
        public static void TestWhichServersToPreferIsPassedOn()
        {
            FakeHunts f = new FakeHunts();
            using (HuntDialog d = new HuntDialog(Kit(TempStore(), f)))
            {
                d.SetGame("107778070777162");
                d.TickAccount("alt1", true);
                d.SetServers(ServerSize.Busiest, 6, 20);
                Assert.Equal(null, d.StartOrStop(), "started");
            }
            Assert.Equal(ServerSize.Busiest, f.Started.Servers.Size, "the busiest");
            Assert.Equal(6, f.Started.Servers.MinPlayers, "at least six playing");
            Assert.Equal(20, f.Started.Servers.MaxPlayers, "at most twenty");
        }

        public static void TestWhyItCannotStartIsShown()
        {
            FakeHunts f = new FakeHunts();
            f.Refuse = "alt1 has no saved sign-in, so its client can't be moved. Sign it in again in Accounts.";
            using (HuntDialog d = new HuntDialog(Kit(TempStore(), f)))
            {
                d.SetGame("107778070777162");
                d.TickAccount("alt1", true);
                Assert.Equal(f.Refuse, d.StartOrStop(), "the reason");
                Assert.Contains("sign-in", d.ProblemText, "on screen");
            }
        }

        public static void TestWhileHuntingTheButtonStopsIt()
        {
            FakeHunts f = new FakeHunts();
            f.IsRunning = true;
            using (HuntDialog d = new HuntDialog(Kit(TempStore(), f)))
            {
                Assert.Contains("Stop", d.ButtonText, "offers to stop");
                d.StartOrStop();
                Assert.Equal(1, f.Stops, "stopped");
                Assert.Contains("Start", d.ButtonText, "and offers to start again");
            }
        }

        public static void TestItShowsWhereEachAccountIsUpTo()
        {
            FakeHunts f = new FakeHunts();
            f.Said.Add("VladDerKing - looking in server 3 - 41s left");
            using (HuntDialog d = new HuntDialog(Kit(TempStore(), f)))
                Assert.Contains("server 3", d.LinesShown, "each account's line");
        }
    }
}
