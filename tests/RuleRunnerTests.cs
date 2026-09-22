using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Deciding which rules fire, on which clients, and when - from what
    // happened, the clock, and whether anyone is at the keyboard. Nothing is
    // played here; the runner only says what should be.
    static class RuleRunnerTests
    {
        static readonly DateTime T0 = new DateTime(2026, 9, 22, 19, 0, 0);
        static readonly TimeSpan Busy = TimeSpan.FromSeconds(2);
        static readonly TimeSpan Away = TimeSpan.FromMinutes(5);

        static WatchedClient C(int pid, string account)
        {
            WatchedClient c = new WatchedClient();
            c.Pid = pid; c.Label = "Client " + (pid / 100); c.AccountName = account;
            return c;
        }

        static readonly WatchedClient[] Clients = { C(100, "VladDerKing"), C(200, "alt1"), C(300, null) };

        static Rule OnWatcher(string watcher, string contains)
        {
            Rule r = new Rule();
            r.Name = "r"; r.Macro = "Buy egg"; r.When = RuleWhen.WatcherFinds;
            r.Watcher = watcher; r.FoundContains = contains;
            return r;
        }

        static RuleHappening Found(string watcher, string what, int pid)
        {
            RuleHappening h = new RuleHappening();
            h.Kind = RuleWhen.WatcherFinds; h.WatcherName = watcher; h.Found = what; h.Pid = pid;
            return h;
        }

        static List<int> Pids(List<RuleFiring> f)
        {
            List<int> p = new List<int>();
            foreach (RuleFiring x in f) p.Add(x.Pid);
            return p;
        }

        public static void TestAWatcherRuleFiresForItsWatcherOnTheClientThatSawIt()
        {
            RuleRunner run = new RuleRunner();
            Rule r = OnWatcher("Secret chat", null);
            List<RuleFiring> f = run.Happened(new Rule[] { r }, Found("Secret chat", "A Secret Kraken Egg", 200), Clients, 0, Busy, T0);
            Assert.Equal(1, f.Count, "fired once");
            Assert.Equal(200, f[0].Pid, "on the client that saw it");
            Assert.Contains("alt1", f[0].Label, "named for the log");
            Assert.Equal(0, run.Happened(new Rule[] { r }, Found("Boss", "x", 200), Clients, 0, Busy, T0).Count,
                "another watcher doesn't set it off");
        }

        public static void TestAnyWatcherAndWhatItFoundCanBeRequired()
        {
            RuleRunner run = new RuleRunner();
            Rule r = OnWatcher(null, "kraken");
            Assert.Equal(1, run.Happened(new Rule[] { r }, Found("Secret chat", "A Secret Kraken Egg", 100), Clients, 0, Busy, T0).Count,
                "any watcher, and it has Kraken in it");
            Assert.Equal(0, run.Happened(new Rule[] { r }, Found("Secret chat", "A Secret Centaur Egg", 100), Clients, 0, Busy, T0.AddMinutes(1)).Count,
                "not a Centaur");
        }

        public static void TestARuleWaitsItsGapOnEachClient()
        {
            RuleRunner run = new RuleRunner();
            Rule r = OnWatcher(null, null);
            r.GapSeconds = 30;
            Rule[] rs = { r };
            Assert.Equal(1, run.Happened(rs, Found("w", "x", 100), Clients, 0, Busy, T0).Count, "first");
            Assert.Equal(0, run.Happened(rs, Found("w", "x", 100), Clients, 0, Busy, T0.AddSeconds(10)).Count, "too soon on that client");
            Assert.Equal(1, run.Happened(rs, Found("w", "x", 200), Clients, 0, Busy, T0.AddSeconds(10)).Count, "another client's own turn");
            Assert.Equal(1, run.Happened(rs, Found("w", "x", 100), Clients, 0, Busy, T0.AddSeconds(31)).Count, "after the gap");
        }

        // "A macro that does it always."
        public static void TestATimerFiresEveryTimeItComesRound()
        {
            RuleRunner run = new RuleRunner();
            Rule r = new Rule();
            r.Name = "Collect"; r.Macro = "Collect"; r.When = RuleWhen.Every; r.EverySeconds = 60; r.On = RuleOn.EveryClient;
            Rule[] rs = { r };
            Assert.Equal(0, run.Tick(rs, Clients, 0, Busy, T0).Count, "not straight away");
            Assert.Equal(0, run.Tick(rs, Clients, 0, Busy, T0.AddSeconds(59)).Count, "not before a minute");
            Assert.Equal(3, run.Tick(rs, Clients, 0, Busy, T0.AddSeconds(60)).Count, "a minute in, on every client");
            Assert.Equal(0, run.Tick(rs, Clients, 0, Busy, T0.AddSeconds(61)).Count, "then waits again");
            Assert.Equal(3, run.Tick(rs, Clients, 0, Busy, T0.AddSeconds(120)).Count, "and again");
        }

        // A timer that holds the keyboard shouldn't fire while someone is
        // typing - it waits until they have been away a minute.
        public static void TestOnlyWhileAwayWaitsForTheKeyboardToGoQuiet()
        {
            RuleRunner run = new RuleRunner();
            Rule r = new Rule();
            r.Name = "Collect"; r.Macro = "Collect"; r.When = RuleWhen.Every; r.EverySeconds = 60;
            r.On = RuleOn.EveryClient; r.OnlyWhenAway = true;
            Rule[] rs = { r };
            run.Tick(rs, Clients, 0, Busy, T0);
            Assert.Equal(0, run.Tick(rs, Clients, 0, Busy, T0.AddSeconds(70)).Count, "someone is typing");
            Assert.Equal(3, run.Tick(rs, Clients, 0, Away, T0.AddSeconds(90)).Count, "they went away - now");
        }

        // "When I want": a key that plays it on the client being played.
        public static void TestAHotkeyPlaysOnTheClientInFront()
        {
            RuleRunner run = new RuleRunner();
            Rule r = new Rule();
            r.Name = "Dance"; r.Macro = "Dance"; r.When = RuleWhen.Hotkey; r.HotkeyVk = 0x70; r.Ctrl = true; r.On = RuleOn.InFront;
            Rule[] rs = { r };

            RuleHappening key = new RuleHappening();
            key.Kind = RuleWhen.Hotkey; key.Vk = 0x70; key.Ctrl = true;
            List<RuleFiring> f = run.Happened(rs, key, Clients, 200, Busy, T0);
            Assert.Equal(1, f.Count, "pressed with a client in front");
            Assert.Equal(200, f[0].Pid, "on that client");

            Assert.Equal(0, run.Happened(rs, key, Clients, 999, Busy, T0.AddMinutes(1)).Count, "something else in front: nothing");
            key.Ctrl = false;
            Assert.Equal(0, run.Happened(rs, key, Clients, 200, Busy, T0.AddMinutes(2)).Count, "F1 without Ctrl is another key");
        }

        public static void TestTheseAccountsMeansOnlyThem()
        {
            RuleRunner run = new RuleRunner();
            Rule r = new Rule();
            r.Name = "t"; r.Macro = "m"; r.When = RuleWhen.Every; r.EverySeconds = 10;
            r.On = RuleOn.TheseAccounts; r.Accounts = new string[] { "ALT1" };
            run.Tick(new Rule[] { r }, Clients, 0, Busy, T0);
            List<int> pids = Pids(run.Tick(new Rule[] { r }, Clients, 0, Busy, T0.AddSeconds(10)));
            Assert.Equal(1, pids.Count, "one account");
            Assert.Equal(200, pids[0], "alt1's client");
        }

        // Wait first - a shop that has to open, a game still loading after a
        // join.
        public static void TestAWaitFirstHoldsTheMacroBack()
        {
            RuleRunner run = new RuleRunner();
            Rule r = new Rule();
            r.Name = "Claim"; r.Macro = "Claim"; r.When = RuleWhen.Joins; r.DelaySeconds = 10;
            RuleHappening join = new RuleHappening();
            join.Kind = RuleWhen.Joins; join.Pid = 300;
            Rule[] rs = { r };
            Assert.Equal(0, run.Happened(rs, join, Clients, 0, Busy, T0).Count, "not yet");
            Assert.Equal(0, run.Tick(rs, Clients, 0, Busy, T0.AddSeconds(9)).Count, "still not");
            List<RuleFiring> f = run.Tick(rs, Clients, 0, Busy, T0.AddSeconds(10));
            Assert.Equal(1, f.Count, "ten seconds after the join");
            Assert.Equal(300, f[0].Pid, "on the client that joined");
            Assert.Equal(0, run.Tick(rs, Clients, 0, Busy, T0.AddSeconds(11)).Count, "and only once");
        }

        public static void TestASwitchedOffOrUnfinishedRuleNeverFires()
        {
            RuleRunner run = new RuleRunner();
            Rule off = OnWatcher(null, null);
            off.Enabled = false;
            Rule noMacro = OnWatcher(null, null);
            noMacro.Macro = null;
            Assert.Equal(0, run.Happened(new Rule[] { off, noMacro }, Found("w", "x", 100), Clients, 0, Busy, T0).Count, "neither");
        }

        public static void TestTimesTravelsWithTheFiring()
        {
            RuleRunner run = new RuleRunner();
            Rule r = OnWatcher(null, null);
            r.Times = 3;
            Assert.Equal(3, run.Happened(new Rule[] { r }, Found("w", "x", 100), Clients, 0, Busy, T0)[0].Times, "three times");
        }
    }
}
