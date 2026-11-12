using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Rules, wired into the main window: what happened is handed to the
    // runner, the clock is ticked once a second, hotkeys are listened for,
    // and whatever the runner says to play goes into the macro queue.
    partial class MainForm
    {
        readonly RuleRunner ruleRunner = new RuleRunner();
        readonly List<string> missingMacroSaid = new List<string>();
        RuleScripts ruleScripts;

        // Made on first use, because the log and the script list are there by
        // then.
        RuleScripts RuleScriptsNow()
        {
            if (ruleScripts == null) ruleScripts = new RuleScripts(new MainFormScripts(this), delegate(string line) { Log(line); });
            return ruleScripts;
        }

        // The script list and runner, as a rule sees them.
        class MainFormScripts : IRuleScripts
        {
            readonly MainForm form;
            public MainFormScripts(MainForm form) { this.form = form; }

            public bool Exists(string name) { return form.Store.Exists(name); }

            public string Start(string name, int pid, string tag) { return form.StartScript(name, pid, tag); }

            public bool IsRunning(string name, int pid, string tag)
            {
                foreach (ScriptRunInfo i in form.ScriptsRunning())
                    if (string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase) && i.Tag == tag && (pid <= 0 || i.Pid == pid))
                        return true;
                return false;
            }

            public int StopTagged(string tag) { return form.StopTag(tag) ? 1 : 0; }
        }

        IList<Rule> RulesNow()
        {
            return watchStore == null ? (IList<Rule>)new Rule[0] : new List<Rule>(watchStore.Rules);
        }

        // Once a second, from the watch tick.
        void RulesTick()
        {
            if (watchStore == null) return;
            IList<Rule> rules = RulesNow();
            if (rules.Count == 0) return;
            Play(ruleRunner.Tick(rules, watchWork.Clients, PerformanceManager.ForegroundPid(),
                                 NudgePolicy.UserIdleFor(), DateTime.Now));
        }

        void RulesHappened(RuleHappening h)
        {
            if (watchStore == null) return;
            IList<Rule> rules = RulesNow();
            if (rules.Count == 0) return;
            Play(ruleRunner.Happened(rules, h, watchWork.Clients, PerformanceManager.ForegroundPid(),
                                     NudgePolicy.UserIdleFor(), DateTime.Now));
        }

        void RulesFound(DetectionEvent d)
        {
            RuleHappening h = new RuleHappening();
            h.Kind = RuleWhen.WatcherFinds;
            h.WatcherName = d.WatcherName;
            h.Found = d.Line ?? d.Matched;
            h.Pid = d.Pid;
            RulesHappened(h);
        }

        void RulesJoined(int pid)
        {
            RuleHappening h = new RuleHappening();
            h.Kind = RuleWhen.Joins;
            h.Pid = pid;
            RulesHappened(h);
        }

        void Play(List<RuleFiring> firings)
        {
            foreach (RuleFiring f in firings)
            {
                if (f.Rule.PlaysScript) { RuleScriptsNow().Play(f.Rule, f.Pid, RuleTag(f.Rule)); continue; }
                Macro m = watchStore.FindMacro(f.Rule.Macro);
                if (m == null)
                {
                    // Once per rule, not once a second for a timer.
                    if (!missingMacroSaid.Contains(f.Rule.Name))
                    {
                        missingMacroSaid.Add(f.Rule.Name);
                        Log("The rule " + f.Rule.Name + " should play " + f.Rule.Macro + ", but that macro no longer exists.");
                    }
                    continue;
                }
                QueueMacro(m, f.Pid, f.Label, f.Times, RuleTag(f.Rule), false, MacroRunner.DEFAULT_TURN_MS);
            }
        }

        static string RuleTag(Rule r) { return "rule:" + r.Name; }

        // A key whose rule plays a macro that goes on until stopped: pressed
        // again, it stops it. True when that is what this press did.
        //
        // Decided here, before the runner sees the press - the runner drops a
        // second press inside the rule's "at most once every" and would
        // swallow the very press meant to stop it.
        bool StoppedByItsOwnKey(RuleHappening h)
        {
            if (watchStore == null) return false;
            bool stopped = false;
            foreach (Rule r in RulesNow())
            {
                if (r == null || !r.Enabled || r.When != RuleWhen.Hotkey) continue;
                if (r.HotkeyVk != h.Vk || r.Ctrl != h.Ctrl || r.Alt != h.Alt || r.Shift != h.Shift) continue;
                // StopTag stops what the rule started whether it is a macro or
                // a script.
                if (RuleScriptsNow().StopFor(RuleTag(r)) == 0) continue;
                // So the next press starts it again, however soon.
                ruleRunner.Forget(r);
                stopped = true;
            }
            return stopped;
        }

        // ---------- hotkeys ----------

        // Exactly the keys the chosen setup's rules use, for GlobalKeys to
        // listen for - less any that is the stop key, the autoclicker's or
        // the screenshot key. Asked whenever what is being watched changes.
        List<int> RuleKeysWanted()
        {
            List<int> keys = new List<int>();
            if (watchStore == null) return keys;
            foreach (int key in RuleHotkeys.Wanted(RulesNow()))
            {
                // The stop key and the autoclicker's are spoken for; a rule
                // on one of them would take it away from both.
                if (IsMacroKey(key))
                {
                    string taken = MacroKeys.Name((byte)(key & 0xFF));
                    if (!missingMacroSaid.Contains("key:" + taken))
                    {
                        missingMacroSaid.Add("key:" + taken);
                        // A macro's own key is said as that, so the person
                        // knows which of the two to change.
                        Macro own = MacroOnKey((byte)(key & 0xFF));
                        if (own != null)
                            Log(taken + " plays the macro " + own.Name + ", so a rule can't use it too. Choose another key for the rule, or change the macro's key in Macros.");
                        else
                            Log(taken + " is already used by another feature (the stop key, the autoclicker, a macro or a script), so a rule can't use it too. Choose another key for the rule.");
                    }
                    continue;
                }
                keys.Add(key);
            }
            return keys;
        }

        // Every key GlobalKeys listens for is let go of together.
        void ReleaseHotkeys()
        {
            if (!started) return;
            GlobalKeys.Stop();
            globalKeysAsked = null;
        }

        // A rule's key was pressed, with exactly these Ctrl, Alt and Shift.
        void RuleKeyPressed(int key)
        {
            if (watchStore == null || IsMacroKey(key)) return;
            RuleHappening h = new RuleHappening();
            h.Kind = RuleWhen.Hotkey;
            RuleHotkeys.Split(key, out h.Vk, out h.Ctrl, out h.Alt, out h.Shift);
            if (!StoppedByItsOwnKey(h)) RulesHappened(h);
        }

        // WM_HOTKEY: only the stop key is still one of Windows' shortcut keys.
        bool OnHotkey(ref Message m)
        {
            if (m.Msg != Native.WM_HOTKEY) return false;
            OnMacroKey(m.WParam.ToInt32());
            return true;
        }
    }

    // What a rule needs from the Lua scripts: the main window's side of
    // them, kept small so a test can stand in for it.
    interface IRuleScripts
    {
        bool Exists(string name);
        // Null when it started; otherwise why not.
        string Start(string name, int pid, string tag);
        // This script, on this window (any, when pid is 0), under this tag.
        bool IsRunning(string name, int pid, string tag);
        // Stops everything started under the tag, macros and scripts alike.
        // How many (at least 1 when it stopped something).
        int StopTagged(string tag);
    }

    // Starts and stops the scripts that rules run, and says what went wrong
    // once per rule rather than every time a timer comes round.
    class RuleScripts
    {
        readonly IRuleScripts host;
        readonly Action<string> say;
        readonly List<string> said = new List<string>();

        public RuleScripts(IRuleScripts host, Action<string> say)
        {
            this.host = host;
            this.say = say;
        }

        // pid: the Roblox window the rule fired on.
        public void Play(Rule rule, int pid, string tag)
        {
            if (!host.Exists(rule.Script))
            {
                Say(rule, "gone", "The rule " + rule.Name + " should run the script " + rule.Script + ", but it no longer exists.");
                return;
            }
            // A timer comes round while its script is still going. The script
            // is not started twice, and that is not worth saying.
            if (host.IsRunning(rule.Script, pid, tag)) return;
            string why = host.Start(rule.Script, pid, tag);
            if (why != null) Say(rule, why, "The rule " + rule.Name + " couldn't run " + rule.Script + ": " + why);
        }

        // A key rule's key, pressed again: its scripts stop. How many.
        public int StopFor(string tag)
        {
            return host.StopTagged(tag);
        }

        void Say(Rule rule, string reason, string line)
        {
            string key = rule.Name + "|" + reason;
            if (said.Contains(key)) return;
            said.Add(key);
            say(line);
        }
    }
}
