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
}
