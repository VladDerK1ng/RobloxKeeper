using System;
using System.Collections.Generic;
using System.Globalization;

namespace RobloxKeeper
{
    // What sets a rule off.
    enum RuleWhen
    {
        WatcherFinds,       // a watcher found something - any, or one by name
        Every,              // on a timer, over and over
        Hotkey,             // a key pressed anywhere
        Joins               // a client arrived in a server
    }

    // Which clients the macro plays on.
    enum RuleOn
    {
        WhereItHappened,    // the client the watcher saw it on, or that joined
        InFront,            // whichever client is in front right now
        EveryClient,
        TheseAccounts
    }

    // When something happens, play a macro - on some clients, within some
    // limits. Kept in a setup beside its watchers, so a rule for one game
    // never fires in another.
    class Rule
    {
        public string Name;
        public bool Enabled = true;

        public RuleWhen When;
        public string Watcher;              // WatcherFinds: null for any watcher
        public string FoundContains;        // WatcherFinds: null for anything it found
        public int EverySeconds = 300;      // Every
        public byte HotkeyVk = 0x70;        // Hotkey: F1 to start with
        public bool Ctrl, Alt, Shift;       // Hotkey

        public string Macro;
        public int Times = 1;
        public int DelaySeconds;            // wait this long first - a game still loading, a shop opening

        public RuleOn On = RuleOn.WhereItHappened;
        public string[] Accounts;           // TheseAccounts

        public int GapSeconds = 15;         // at most once every this long on each client
        public bool OnlyWhenAway;           // only while nobody is using the keyboard and mouse

        // A timer faster than this would hold the keyboard nearly all the time.
        public const int MIN_EVERY = 5;

        public bool HappensOnAClient { get { return When == RuleWhen.WatcherFinds || When == RuleWhen.Joins; } }

        public string Problem()
        {
            if (string.IsNullOrEmpty(Name) || Name.Trim().Length == 0) return "Give the rule a name.";
            if (string.IsNullOrEmpty(Macro)) return "Choose a macro for it to play.";
            if (When == RuleWhen.Every && EverySeconds < MIN_EVERY)
                return "Make it every " + MIN_EVERY + " seconds or more - a macro holds the keyboard while it plays.";
            if (When == RuleWhen.Hotkey && HotkeyVk == 0) return "Choose a key.";
            if (On == RuleOn.WhereItHappened && !HappensOnAClient)
                return "Choose which clients it plays on - a timer or a key doesn't happen on one client.";
            if (On == RuleOn.TheseAccounts && (Accounts == null || Accounts.Length == 0))
                return "Tick at least one account for it to play on.";
            return null;
        }

        public string Describe()
        {
            string when;
            switch (When)
            {
                case RuleWhen.Every: when = "Every " + Duration(EverySeconds); break;
                case RuleWhen.Hotkey: when = "When you press " + HotkeyText(); break;
                case RuleWhen.Joins: when = "When a client joins a server"; break;
                default:
                    when = "When " + (string.IsNullOrEmpty(Watcher) ? "any watcher" : Watcher) + " finds something"
                         + (string.IsNullOrEmpty(FoundContains) ? "" : " with \"" + FoundContains + "\" in it");
                    break;
            }

            string what = "play " + (Macro ?? "nothing")
                        + (Times > 1 ? " " + Times + " times" : "")
                        + (DelaySeconds > 0 ? " after " + DelaySeconds + "s" : "");
            return when + ", " + what + " on " + OnText() + (OnlyWhenAway ? " - only while you're away" : "");
        }

        string OnText()
        {
            switch (On)
            {
                case RuleOn.InFront: return "the client in front";
                case RuleOn.EveryClient: return "every client";
                case RuleOn.TheseAccounts:
                    return Accounts == null || Accounts.Length == 0 ? "no account yet" : string.Join(", ", Accounts);
                default:
                    return When == RuleWhen.WatcherFinds ? "the client that saw it" : "that client";
            }
        }

        public string HotkeyText()
        {
            return (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + MacroKeys.Name(HotkeyVk);
        }

        public static string Duration(int seconds)
        {
            if (seconds >= 3600 && seconds % 3600 == 0) return Count(seconds / 3600, "hour");
            if (seconds >= 60 && seconds % 60 == 0) return Count(seconds / 60, "minute");
            return Count(seconds, "second");
        }

        static string Count(int n, string unit)
        {
            return n == 1 ? unit : n.ToString(CultureInfo.InvariantCulture) + " " + unit + "s";
        }

        public Rule Copy()
        {
            Rule r = (Rule)MemberwiseClone();
            r.Accounts = Accounts == null ? null : (string[])Accounts.Clone();
            return r;
        }
    }

    // The keys to ask Windows for, as its own modifier bits over the key -
    // MOD_ALT 1, MOD_CONTROL 2, MOD_SHIFT 4 - so a key is one number.
    static class RuleHotkeys
    {
        public static int Key(byte vk, bool ctrl, bool alt, bool shift)
        {
            int mods = (alt ? 1 : 0) | (ctrl ? 2 : 0) | (shift ? 4 : 0);
            return (mods << 8) | vk;
        }

        public static void Split(int key, out byte vk, out bool ctrl, out bool alt, out bool shift)
        {
            vk = (byte)(key & 0xFF);
            int mods = key >> 8;
            alt = (mods & 1) != 0; ctrl = (mods & 2) != 0; shift = (mods & 4) != 0;
        }

        // Each key once, however many rules share it, and only for rules
        // that are switched on and could fire.
        public static List<int> Wanted(IList<Rule> rules)
        {
            List<int> keys = new List<int>();
            foreach (Rule r in rules)
            {
                if (r == null || !r.Enabled || r.When != RuleWhen.Hotkey || r.Problem() != null) continue;
                int k = Key(r.HotkeyVk, r.Ctrl, r.Alt, r.Shift);
                if (!keys.Contains(k)) keys.Add(k);
            }
            return keys;
        }
    }

    // Something that happened which a rule may be waiting for. Timers are not
    // happenings - the clock is, and RuleRunner.Tick reads it.
    class RuleHappening
    {
        public RuleWhen Kind;
        public string WatcherName;      // WatcherFinds
        public string Found;            // WatcherFinds: the line or word it found
        public int Pid;                 // WatcherFinds, Joins: which client
        public byte Vk;                 // Hotkey
        public bool Ctrl, Alt, Shift;   // Hotkey
    }

    // A rule's macro, to be played on one client.
    class RuleFiring
    {
        public Rule Rule;
        public int Pid;
        public string Label;            // "Client 2 - alt1", for the activity list
        public int Times;
        public DateTime Due;
    }

    // Which rules fire, on which clients, and when. Decides only; the main
    // window plays what it is handed.
    class RuleRunner
    {
        // Nobody has touched the keyboard or mouse for this long: away.
        public static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(1);

        readonly Dictionary<Rule, DateTime> nextDue = new Dictionary<Rule, DateTime>();
        readonly Dictionary<Rule, Dictionary<int, DateTime>> lastPlayed = new Dictionary<Rule, Dictionary<int, DateTime>>();
        readonly List<RuleFiring> waiting = new List<RuleFiring>();

        // Something happened: the rules it sets off, due now. Ones told to
        // wait first come back from Tick when their time comes.
        public List<RuleFiring> Happened(IList<Rule> rules, RuleHappening h, IList<WatchedClient> clients,
                                         int foregroundPid, TimeSpan idle, DateTime now)
        {
            List<RuleFiring> fire = new List<RuleFiring>();
            foreach (Rule r in rules)
            {
                if (!Ready(r) || r.When == RuleWhen.Every || !Matches(r, h)) continue;
                if (r.OnlyWhenAway && idle < AwayAfter) continue;
                Fire(r, Targets(r, h.Pid, clients, foregroundPid), now, fire);
            }
            return fire;
        }

        // Once a second: timers that have come round, and waits that are over.
        public List<RuleFiring> Tick(IList<Rule> rules, IList<WatchedClient> clients,
                                     int foregroundPid, TimeSpan idle, DateTime now)
        {
            List<RuleFiring> fire = new List<RuleFiring>();
            for (int i = waiting.Count - 1; i >= 0; i--)
                if (waiting[i].Due <= now) { fire.Insert(0, waiting[i]); waiting.RemoveAt(i); }

            foreach (Rule r in rules)
            {
                if (!Ready(r) || r.When != RuleWhen.Every) continue;
                DateTime due;
                if (!nextDue.TryGetValue(r, out due)) { nextDue[r] = now.AddSeconds(r.EverySeconds); continue; }
                if (now < due) continue;
                // Waiting for the keyboard to go quiet keeps it due, so it
                // plays the moment it may rather than a whole turn later.
                if (r.OnlyWhenAway && idle < AwayAfter) continue;
                nextDue[r] = now.AddSeconds(r.EverySeconds);
                Fire(r, Targets(r, 0, clients, foregroundPid), now, fire);
            }

            // A rule removed or edited (and so replaced) takes its clock with it.
            List<Rule> gone = new List<Rule>();
            foreach (Rule r in nextDue.Keys) if (!rules.Contains(r)) gone.Add(r);
            foreach (Rule r in gone) { nextDue.Remove(r); lastPlayed.Remove(r); }
            return fire;
        }

        static bool Ready(Rule r)
        {
            return r != null && r.Enabled && r.Problem() == null;
        }

        static bool Matches(Rule r, RuleHappening h)
        {
            if (h == null || r.When != h.Kind) return false;
            switch (r.When)
            {
                case RuleWhen.WatcherFinds:
                    if (!string.IsNullOrEmpty(r.Watcher) &&
                        !string.Equals(r.Watcher, h.WatcherName, StringComparison.OrdinalIgnoreCase)) return false;
                    return string.IsNullOrEmpty(r.FoundContains) ||
                           (h.Found ?? "").IndexOf(r.FoundContains, StringComparison.OrdinalIgnoreCase) >= 0;
                case RuleWhen.Hotkey:
                    return r.HotkeyVk == h.Vk && r.Ctrl == h.Ctrl && r.Alt == h.Alt && r.Shift == h.Shift;
                default:
                    return true;
            }
        }

        static List<WatchedClient> Targets(Rule r, int where, IList<WatchedClient> clients, int foregroundPid)
        {
            List<WatchedClient> list = new List<WatchedClient>();
            foreach (WatchedClient c in clients)
            {
                if (c == null) continue;
                bool take;
                switch (r.On)
                {
                    case RuleOn.InFront: take = c.Pid == foregroundPid; break;
                    case RuleOn.EveryClient: take = true; break;
                    case RuleOn.TheseAccounts: take = Named(r.Accounts, c.AccountName); break;
                    default: take = c.Pid == where; break;
                }
                if (take) list.Add(c);
            }
            return list;
        }

        static bool Named(string[] accounts, string name)
        {
            if (accounts == null || string.IsNullOrEmpty(name)) return false;
            foreach (string a in accounts)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void Fire(Rule r, List<WatchedClient> targets, DateTime now, List<RuleFiring> fire)
        {
            Dictionary<int, DateTime> byPid;
            if (!lastPlayed.TryGetValue(r, out byPid)) { byPid = new Dictionary<int, DateTime>(); lastPlayed[r] = byPid; }

            foreach (WatchedClient c in targets)
            {
                DateTime was;
                if (byPid.TryGetValue(c.Pid, out was) && now - was < TimeSpan.FromSeconds(r.GapSeconds)) continue;
                byPid[c.Pid] = now;

                RuleFiring f = new RuleFiring();
                f.Rule = r;
                f.Pid = c.Pid;
                f.Label = string.IsNullOrEmpty(c.AccountName) ? c.Label : c.Label + " - " + c.AccountName;
                f.Times = Math.Max(1, r.Times);
                f.Due = now.AddSeconds(r.DelaySeconds);
                if (r.DelaySeconds > 0) waiting.Add(f);
                else fire.Add(f);
            }
        }
    }
}
