using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RobloxKeeper
{
    // What a press (or let-go) of a macro's own key does.
    enum MacroKeyAction { Nothing, Start, Stop }

    // Getting a macro ready to play a number of times at a speed, and what
    // its key does - decided here, apart from the keyboard and the screen,
    // so it can all be tested.
    static class MacroPlay
    {
        // Half speed, a quarter, to four times as fast. Past four times the
        // game stops noticing what is sent.
        public const int MIN_SPEED_PCT = 25;
        public const int MAX_SPEED_PCT = 400;

        // A key held for less than this is missed by Roblox, so a faster play
        // never holds one for shorter - and one already that short was made
        // that way on purpose and is left as it is.
        public const int SHORTEST_HOLD_MS = 30;

        // What Quick Record's recording is called when it is played.
        public const string QUICK_NAME = "Quick recording";

        // Who started a play, so exactly that play can be stopped: its key,
        // or a Play button in one of the app's windows.
        public static string KeyTag(string macroName) { return "key:" + macroName; }
        public static string PlayTag(string macroName) { return "play:" + macroName; }

        public static int ClampSpeed(int pct)
        {
            return pct < MIN_SPEED_PCT ? MIN_SPEED_PCT : pct > MAX_SPEED_PCT ? MAX_SPEED_PCT : pct;
        }

        // A time at a speed: 200 halves it. Never down to nothing - a wait of
        // 0 that was not 0 would be a different macro.
        public static int Scale(int ms, int speedPct)
        {
            if (ms <= 0) return ms;
            int pct = ClampSpeed(speedPct);
            if (pct == 100) return ms;
            long scaled = (long)Math.Round(ms * 100.0 / pct, MidpointRounding.AwayFromZero);
            if (scaled < 1) scaled = 1;
            return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
        }

        // A copy to play: its steps `times` times over (0 until it is
        // stopped), at a speed. The copy is a repeat around the whole macro,
        // which the player goes round without needing a client to hold, and
        // which the stop key ends like any other. Times 1 at speed 100 is the
        // macro as it is.
        //
        // The extra repeat can take it one past Macro.MAX_DEPTH. That limit
        // is for the list in the editor, which this copy never goes back to;
        // the player has none. Clicks are never made quicker: a click is held
        // for as long as Roblox needs to see it, whatever the speed.
        public static Macro Prepare(Macro m, int times, int speedPct)
        {
            Macro play = m.Copy();
            int pct = ClampSpeed(speedPct);
            if (pct != 100)
            {
                foreach (MacroStep s in play.Steps)
                {
                    switch (s.Kind)
                    {
                        case MacroStepKind.Wait:
                        case MacroStepKind.Drag:
                            s.Ms = Scale(s.Ms, pct);
                            break;
                        case MacroStepKind.Key:
                            if (s.HoldMs >= SHORTEST_HOLD_MS)
                                s.HoldMs = Math.Max(SHORTEST_HOLD_MS, Scale(s.HoldMs, pct));
                            break;
                    }
                }
            }

            if (times == 1 || play.Steps.Count == 0) return play;
            List<MacroStep> inner = new List<MacroStep>(play.Steps);
            play.Steps.Clear();
            play.Steps.Add(times <= 0 ? MacroStep.Repeat(RepeatMode.Forever, 0) : MacroStep.Repeat(RepeatMode.Times, times));
            play.Steps.AddRange(inner);
            play.Steps.Add(MacroStep.EndRepeat());
            return play;
        }

        // Whether a play of m made by Prepare takes turns with the keyboard
        // (see MacroRunner.StepsAside). The minute a macro on a Roblox window
        // is kept to is for one play at its own speed; several times over, or
        // slowed down, it can go on far longer, and the nudge must still get
        // in. One on the whole screen can last half an hour by itself.
        public static bool StepsAside(Macro m, int times, int speedPct)
        {
            if (m == null) return false;
            return m.Anywhere || times != 1 || ClampSpeed(speedPct) < 100;
        }

        // A macro made for the whole screen that something meant for a
        // Roblox window set off - a watcher's "then play", an older rule, a
        // Play button - is played on the screen instead: on a client its
        // places would land on the wrong spots. The play to start, `times`
        // times at its own speed, or null when m plays on a Roblox window.
        public static Macro OnScreenInstead(Macro m, int times)
        {
            if (m == null || !m.Anywhere) return null;
            return Prepare(m, Math.Max(1, times), m.SpeedPct);
        }

        // What that play is listed under: the caller's own tag, so stopping
        // that stops it - or, from a watcher, which has none, one made from
        // the macro's name, so a watcher firing again while it plays does
        // not start it twice.
        public static string ScreenTag(string tag, string macroName)
        {
            return tag ?? "screen:" + macroName;
        }

        // How many times a press of its key plays it: once-mode keys play it
        // as many times as it is set to, the others go round until stopped.
        public static int TimesFor(Macro m)
        {
            return m.KeyMode == MacroKeyMode.Once ? m.Times : 0;
        }

        // A key went down (or came up) and the macro is, or is not, playing
        // under that key now.
        //
        // Two downs with no up between mean the up was lost - a window run as
        // administrator was in front - so a held key that goes down again is
        // still held, and a play going while it is held is left alone.
        public static MacroKeyAction Decide(MacroKeyMode mode, bool down, bool playing)
        {
            switch (mode)
            {
                case MacroKeyMode.Toggle:
                    if (!down) return MacroKeyAction.Nothing;
                    return playing ? MacroKeyAction.Stop : MacroKeyAction.Start;
                case MacroKeyMode.Hold:
                    if (down) return playing ? MacroKeyAction.Nothing : MacroKeyAction.Start;
                    return playing ? MacroKeyAction.Stop : MacroKeyAction.Nothing;
                default:
                    if (!down) return MacroKeyAction.Nothing;
                    return playing ? MacroKeyAction.Nothing : MacroKeyAction.Start;
            }
        }

        // ---------- Quick Record's recording ----------

        // Kept in a file of its own beside the watchers, as one macro line in
        // the watchers' own format, so it is there after a restart. Missing,
        // empty or unreadable is simply no recording yet.
        public static string QuickPath
        {
            get { return Path.Combine(Path.GetDirectoryName(WatchStore.DefaultPath), "quick-record.dat"); }
        }

        public static Macro LoadQuick(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string line = File.ReadAllText(path, Encoding.UTF8).Trim('\r', '\n', (char)0xFEFF);
                Macro m = WatchStore.DeserializeMacro(line);
                if (m == null || m.Steps.Count == 0) return null;
                if (string.IsNullOrEmpty(m.Name)) m.Name = QUICK_NAME;
                // It records the whole screen, so it plays on the whole screen.
                m.Anywhere = true;
                return m;
            }
            catch { return null; }
        }

        // Null forgets it. False when it couldn't be written.
        public static bool SaveQuick(string path, Macro m)
        {
            try
            {
                if (m == null)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return true;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Written beside it first, so a crash half-way leaves the last
                // recording rather than half of this one.
                string temp = path + ".new";
                File.WriteAllText(temp, WatchStore.SerializeMacro(m) + "\n", new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                return true;
            }
            catch { return false; }
        }
    }

    // Every key the app answers to, as one list, so no two features can be
    // given the same key. Filled in by the main window as things stand.
    class KeyOwners
    {
        public byte StopKey;
        public byte ClickerKey;         // 0 while the autoclicker is not set up
        public byte ShotKey;            // 0 for none
        // Rules' keys with no Ctrl, Alt or Shift - those with them are other
        // keys - and every macro's own key, each with whose it is.
        public readonly List<KeyValuePair<string, byte>> RuleKeys = new List<KeyValuePair<string, byte>>();
        public readonly List<KeyValuePair<string, byte>> Macros = new List<KeyValuePair<string, byte>>();
        // Every Lua script's own key, which starts it and stops it again.
        public readonly List<KeyValuePair<string, byte>> Scripts = new List<KeyValuePair<string, byte>>();
    }

    // The one place that says whether a key is free for something.
    static class KeyUse
    {
        // What the key is wanted for. A job never clashes with itself.
        public const string STOP = "stop";
        public const string CLICKER = "clicker";
        public const string SHOT = "shot";
        public const string RULE = "rule";
        public const string MACRO = "macro";
        public const string EXTRAS = "extras";     // the crosshair, zoom and held keys
        public const string SCRIPT = "script";     // a Lua script's own key

        // Null when vk may be used for `usedFor`; otherwise why not, in a line
        // someone can act on. `editing` is the macro whose key is being
        // chosen: its own key is not a clash. For SCRIPT it is the script's.
        public static string Problem(byte vk, string usedFor, string editing, KeyOwners k)
        {
            if (vk == 0) return "Choose a key.";
            string name = MacroKeys.Name(vk);
            if (vk == MacroRecording.STOP_KEY) return name + " ends a recording. Choose another.";
            if (k == null) return null;
            if (usedFor != STOP && vk == k.StopKey) return name + " is the key that stops macros. Choose another.";
            if (usedFor != CLICKER && k.ClickerKey != 0 && vk == k.ClickerKey)
                return name + " switches the autoclicker. Choose another.";
            if (usedFor != SHOT && k.ShotKey != 0 && vk == k.ShotKey) return name + " takes screenshots. Choose another.";
            if (usedFor != RULE)
                foreach (KeyValuePair<string, byte> r in k.RuleKeys)
                    if (r.Value == vk) return "The rule " + r.Key + " is on " + name + ". Choose another.";
            foreach (KeyValuePair<string, byte> m in k.Macros)
            {
                if (m.Value != vk) continue;
                if (usedFor == MACRO && string.Equals(m.Key, editing, StringComparison.OrdinalIgnoreCase)) continue;
                return name + " plays the macro " + m.Key + ". Choose another.";
            }
            foreach (KeyValuePair<string, byte> s in k.Scripts)
            {
                if (s.Value != vk) continue;
                if (usedFor == SCRIPT && string.Equals(s.Key, editing, StringComparison.OrdinalIgnoreCase)) continue;
                return name + " starts the script " + s.Key + ". Choose another.";
            }
            return null;
        }
    }
}
