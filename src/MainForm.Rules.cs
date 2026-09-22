using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Rules, wired into the main window: what happened is handed to the
    // runner, the clock is ticked once a second, hotkeys are asked of Windows,
    // and whatever the runner says to play goes into the macro queue.
    partial class MainForm
    {
        readonly RuleRunner ruleRunner = new RuleRunner();
        // Keys registered with Windows, by the id they were registered under.
        readonly Dictionary<int, int> hotkeyIds = new Dictionary<int, int>();
        readonly List<string> missingMacroSaid = new List<string>();
        const int HOTKEY_ID_BASE = 0x5100;

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
                Macro m = watchStore.FindMacro(f.Rule.Macro);
                if (m == null)
                {
                    // Once per rule, not once a second for a timer.
                    if (!missingMacroSaid.Contains(f.Rule.Name))
                    {
                        missingMacroSaid.Add(f.Rule.Name);
                        Log("The rule " + f.Rule.Name + " should play " + f.Rule.Macro + ", but there is no macro by that name any more.");
                    }
                    continue;
                }
                QueueMacro(m, f.Pid, f.Label, f.Times);
            }
        }

        // ---------- hotkeys ----------

        // Asks Windows for exactly the keys the chosen setup's rules use, and
        // gives back the rest. Called whenever what is being watched changes.
        void RefreshHotkeys()
        {
            if (!IsHandleCreated || watchStore == null) return;
            List<int> wanted = RuleHotkeys.Wanted(RulesNow());

            List<int> drop = new List<int>();
            foreach (KeyValuePair<int, int> p in hotkeyIds) if (!wanted.Contains(p.Value)) drop.Add(p.Key);
            foreach (int id in drop) { Native.UnregisterHotKey(Handle, id); hotkeyIds.Remove(id); }

            foreach (int key in wanted)
            {
                if (hotkeyIds.ContainsValue(key)) continue;
                int id = HOTKEY_ID_BASE;
                while (hotkeyIds.ContainsKey(id)) id++;
                byte vk; bool ctrl, alt, shift;
                RuleHotkeys.Split(key, out vk, out ctrl, out alt, out shift);
                uint mods = (uint)(key >> 8) | Native.MOD_NOREPEAT;
                if (Native.RegisterHotKey(Handle, id, mods, vk)) hotkeyIds[id] = key;
                else
                {
                    Rule r = new Rule();
                    r.HotkeyVk = vk; r.Ctrl = ctrl; r.Alt = alt; r.Shift = shift;
                    Log(r.HotkeyText() + " is already used by another program, so the rules on it can't hear it. Choose another key.");
                    hotkeyIds[id] = key;     // not asked again every second; freed with the rest
                }
            }
        }

        void ReleaseHotkeys()
        {
            if (!IsHandleCreated) return;
            foreach (int id in hotkeyIds.Keys) Native.UnregisterHotKey(Handle, id);
            hotkeyIds.Clear();
        }

        // WM_HOTKEY: the modifiers in the low word of lParam, the key in the high.
        bool OnHotkey(ref Message m)
        {
            if (m.Msg != Native.WM_HOTKEY) return false;
            int key;
            if (!hotkeyIds.TryGetValue(m.WParam.ToInt32(), out key)) return true;
            RuleHappening h = new RuleHappening();
            h.Kind = RuleWhen.Hotkey;
            RuleHotkeys.Split(key, out h.Vk, out h.Ctrl, out h.Alt, out h.Shift);
            RulesHappened(h);
            return true;
        }
    }
}
