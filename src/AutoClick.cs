using System;
using System.Collections.Generic;
using System.Globalization;

namespace RobloxKeeper
{
    // Where an autoclicker clicks.
    enum ClickWhere
    {
        AtPointer,      // wherever the pointer is - you aim, it clicks
        Place,          // one place on the window
        Picture         // on a picture, wherever it is, each time it is found
    }

    // Which clients it clicks on.
    enum ClickOn
    {
        InFront,        // the client you are playing
        TakeTurns       // several, each brought to the front for its turn
    }

    // What the macro window and the autoclicker ask of the main window.
    interface IMacroHost
    {
        AutoClick Clicker { get; }
        void SaveClicker();                 // after an edit: saved, and its key listened for again
        bool ClickerOn { get; }
        string ToggleClicker();             // null when it switched; otherwise why not
        byte StopKey { get; set; }
        string StopKeyProblem(byte vk);     // null when that key may be the stop key
        void StopEverything();
        int SessionsRunning { get; }

        // The one check for every key the app answers to: null when vk is
        // free for usedFor (KeyUse.STOP, CLICKER, SHOT, RULE, MACRO or
        // EXTRAS), otherwise why not. editing: the name of the macro whose
        // own key is being chosen, so its present key is no clash.
        string KeyUseProblem(byte vk, string usedFor, string editing);

        // The Macros window saved the macros: their own keys are listened
        // for again.
        void MacrosSaved();

        // A macro made for the whole screen (Anywhere), played now on a
        // thread of its own as many times and as fast as it says (its Times
        // and SpeedPct; this prepares it), under MacroPlay.PlayTag(its name).
        // Null when it started; otherwise why not.
        string PlayAnywhere(Macro m);

        // Whether anything started under a tag - MacroPlay.PlayTag or
        // KeyTag - is playing or waiting to. Not raised as an event: a window
        // with a Play button asks again on a timer.
        bool IsPlaying(string tag);

        // Stops exactly what was started under a tag. True when there was
        // something to stop.
        bool StopTag(string tag);

        // Quick Record's last recording, kept across restarts; null for
        // none. A copy each time: set it to keep a change.
        Macro QuickRecording { get; set; }
    }

    // Playing a macro there and then, from the Macros window. Apart from
    // IMacroHost so that what stands in for the main window in a test need
    // not be able to.
    interface IMacroPlay
    {
        // How many of those clients it was sent to. It is not sent twice to
        // a client it is already waiting for or playing on.
        int PlayNow(Macro m, IList<WatchedClient> on);
    }

    // An autoclicker: a click, over and over, until it is switched off.
    //
    // It is a macro like any other - a repeat until stopped around one click
    // and one wait - so everything a macro is held to holds for it: only the
    // client in front is ever clicked, another window coming to the front
    // stops it, and it steps aside for the anti-AFK nudge.
    class AutoClick
    {
        // Off until it has been set up in its window. Its key is only
        // listened for once it is on - so F6 does nothing in this app for
        // someone who never opened this, and is free for anything else. It
        // is heard without being taken (see GlobalKeys), so every other
        // program still gets it either way.
        public bool Enabled;
        public bool Right;
        public int EveryMs = 100;
        public ClickWhere Where = ClickWhere.AtPointer;
        public double X = 0.5, Y = 0.5;         // Place
        public string Picture, Mask;            // Picture
        public double Tolerance = MacroStep.DEFAULT_TOLERANCE;
        public string Box;                      // Picture: a box drawn for the game to look in, or null
        public ClickOn On = ClickOn.InFront;
        public string[] Accounts;               // TakeTurns: null or empty for every client
        public int TurnSeconds = 5;             // TakeTurns: how long each client is in front
        public byte ToggleVk = 0x75;            // F6

        // The shortest gap offered. Measured on a live client: asked for
        // 20ms it sends a click every 22-23ms, asked for 50ms every 53ms.
        // How many of those a game counts is the game's business - a toggle
        // button with an animation dropped some at 100ms and none at 200ms -
        // so this is a floor on what is sent, not a promise of what lands.
        public const int MIN_EVERY_MS = 20;
        public const int MAX_EVERY_MS = 600000;
        public const string MACRO_NAME = "Autoclicker";
        public const string TAG = "autoclick";

        // How long the button stays down: half the gap, within what the game
        // notices at the short end and what a macro's click uses at the long.
        public static int HoldFor(int everyMs)
        {
            int hold = everyMs / 2;
            if (hold < 10) hold = 10;
            if (hold > MacroStep.CLICK_MS) hold = MacroStep.CLICK_MS;
            return hold;
        }

        public Macro ToMacro()
        {
            Macro m = new Macro();
            m.Name = MACRO_NAME;
            m.Steps.Add(MacroStep.Repeat(RepeatMode.Forever, 0));

            int spent;
            if (Where == ClickWhere.Picture)
            {
                MacroStep click = MacroStep.ClickPicture(Picture, Mask, 0);
                click.RightButton = Right;
                click.Tolerance = Tolerance;
                click.Box = Box;
                m.Steps.Add(click);
                spent = 0;      // looking takes what it takes; the wait is the gap between looks
            }
            else
            {
                MacroStep click = Where == ClickWhere.AtPointer ? MacroStep.ClickHere(Right) : MacroStep.Click(X, Y, Right);
                click.Quick = true;
                click.HoldMs = HoldFor(EveryMs);
                m.Steps.Add(click);
                spent = click.HoldMs;
            }

            int wait = Math.Max(1, EveryMs - spent);
            m.Steps.Add(MacroStep.Wait(wait));
            m.Steps.Add(MacroStep.EndRepeat());
            return m;
        }

        // Null when it can be switched on.
        public string Problem()
        {
            if (EveryMs < MIN_EVERY_MS)
                return "Use " + (MIN_EVERY_MS / 1000.0).ToString("0.##", CultureInfo.InvariantCulture)
                     + " seconds or slower. That's as fast as it can click.";
            if (EveryMs > MAX_EVERY_MS) return "Use ten minutes or faster.";
            if (Where == ClickWhere.Picture && string.IsNullOrEmpty(Picture)) return "Choose the picture to click.";
            if (On == ClickOn.TakeTurns && Where == ClickWhere.AtPointer)
                return "Taking turns needs a place or a picture to click. The mouse can only be in one window.";
            if (ToggleVk == 0) return "Choose a key to switch it on and off.";
            return null;
        }

        public string Describe()
        {
            string what = (Right ? "Right-clicks " : "Clicks ")
                        + (Where == ClickWhere.AtPointer ? "where the pointer is"
                         : Where == ClickWhere.Picture ? "on " + MacroStep.PictureLabel(Picture)
                         : "one place")
                        + " every " + MacroStep.Seconds(EveryMs);
            string on = On == ClickOn.InFront ? "on the Roblox window in front"
                      : Accounts == null || Accounts.Length == 0 ? "on every Roblox window in turn"
                      : "on " + string.Join(", ", Accounts) + " in turn";
            return what + ", " + on;
        }

        // Which of the running clients it plays on. In front means the one
        // that is in front as it is switched on - none, if you are somewhere
        // else, rather than whichever client happens to be first.
        public List<WatchedClient> Targets(IList<WatchedClient> clients, int foregroundPid)
        {
            List<WatchedClient> list = new List<WatchedClient>();
            foreach (WatchedClient c in clients)
            {
                if (c == null) continue;
                if (On == ClickOn.InFront) { if (c.Pid == foregroundPid) list.Add(c); continue; }
                if (Accounts == null || Accounts.Length == 0) { list.Add(c); continue; }
                foreach (string a in Accounts)
                    if (string.Equals(a, c.AccountName, StringComparison.OrdinalIgnoreCase)) { list.Add(c); break; }
            }
            return list;
        }

        public AutoClick Copy()
        {
            AutoClick c = (AutoClick)MemberwiseClone();
            c.Accounts = Accounts == null ? null : (string[])Accounts.Clone();
            return c;
        }

        // ---------- settings.txt ----------

        public void Write(List<string> lines)
        {
            lines.Add("click_enabled=" + (Enabled ? "1" : "0"));
            lines.Add("click_alike=" + Tolerance.ToString("R", CultureInfo.InvariantCulture));
            lines.Add("click_right=" + (Right ? "1" : "0"));
            lines.Add("click_every=" + EveryMs);
            lines.Add("click_where=" + (int)Where);
            lines.Add("click_x=" + X.ToString("R", CultureInfo.InvariantCulture));
            lines.Add("click_y=" + Y.ToString("R", CultureInfo.InvariantCulture));
            lines.Add("click_picture=" + (Picture ?? ""));
            lines.Add("click_mask=" + (Mask ?? ""));
            lines.Add("click_box=" + (Box ?? ""));
            lines.Add("click_on=" + (int)On);
            List<string> names = new List<string>();
            if (Accounts != null) foreach (string n in Accounts) if (n.IndexOf('|') < 0) names.Add(n);
            lines.Add("click_accounts=" + string.Join("|", names.ToArray()));
            lines.Add("click_turn=" + TurnSeconds);
            lines.Add("click_key=" + ToggleVk);
        }

        // False when the key is somebody else's.
        public bool Read(string key, string val)
        {
            int n;
            double d;
            if (key == "click_enabled") Enabled = val == "1";
            else if (key == "click_alike") { if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) Tolerance = d; }
            else if (key == "click_right") Right = val == "1";
            else if (key == "click_every") { if (int.TryParse(val, out n)) EveryMs = n; }
            else if (key == "click_where") { if (int.TryParse(val, out n) && n >= 0 && n <= (int)ClickWhere.Picture) Where = (ClickWhere)n; }
            else if (key == "click_x") { if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) X = d; }
            else if (key == "click_y") { if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) Y = d; }
            else if (key == "click_picture") Picture = val.Length == 0 ? null : val;
            else if (key == "click_mask") Mask = val.Length == 0 ? null : val;
            else if (key == "click_box") Box = val.Length == 0 ? null : val;
            else if (key == "click_on") { if (int.TryParse(val, out n) && n >= 0 && n <= (int)ClickOn.TakeTurns) On = (ClickOn)n; }
            else if (key == "click_accounts")
            {
                List<string> names = new List<string>();
                foreach (string a in val.Split('|')) if (a.Length > 0) names.Add(a);
                Accounts = names.Count == 0 ? null : names.ToArray();
            }
            else if (key == "click_turn") { if (int.TryParse(val, out n)) TurnSeconds = n; }
            else if (key == "click_key") { if (int.TryParse(val, out n) && n > 0 && n < 256) ToggleVk = (byte)n; }
            else return false;
            return true;
        }

        // A hand-edited file can't put it somewhere the window can't show.
        public void Clamp()
        {
            if (EveryMs < MIN_EVERY_MS) EveryMs = MIN_EVERY_MS;
            if (EveryMs > MAX_EVERY_MS) EveryMs = MAX_EVERY_MS;
            if (X < 0) X = 0; if (X > 1) X = 1;
            if (Y < 0) Y = 0; if (Y > 1) Y = 1;
            if (TurnSeconds < 1) TurnSeconds = 1;
            if (TurnSeconds > 600) TurnSeconds = 600;
            if (Tolerance < 0.5 || Tolerance > 1) Tolerance = MacroStep.DEFAULT_TOLERANCE;
        }
    }
}
