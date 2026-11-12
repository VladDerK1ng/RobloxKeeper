using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // KeyDown and KeyUp are for keys held across other steps - walking while
    // jumping. A key pressed and let go on its own is a Key.
    //
    // The numbers are what is saved, so new kinds only ever go on the end.
    enum MacroStepKind
    {
        Key, Click, Wait, Type, KeyDown, KeyUp,

        // Looks at the client as the step runs and clicks the picture where
        // it is now - not where it was when the macro was made.
        ClickPicture,

        // Nothing is sent: waits for a picture to turn up, or to go.
        WaitPicture,

        Move, Drag, Scroll,

        // The steps down to the matching EndRepeat, over again.
        Repeat, EndRepeat
    }

    // How long a Repeat goes on.
    enum RepeatMode
    {
        Times,          // a number of times
        Forever,        // until it is stopped
        UntilSeen,      // until a picture turns up
        UntilGone       // until a picture has gone
    }

    // What a macro's own key does. The numbers are what is saved.
    enum MacroKeyMode
    {
        Once,           // plays it, as many times as it is set to
        Toggle,         // starts it going round until the same key stops it
        Hold            // plays it round and round while the key is held
    }

    // One thing a macro does.
    class MacroStep
    {
        public MacroStepKind Kind;
        public byte Vk;                 // Key
        public int HoldMs = 50;         // Key: how long it stays down
        public double X, Y;             // Click: fractions of the window, 0 to 1
        public bool RightButton;        // Click
        public int Ms;                  // Wait
        public string Text;             // Type
        public bool InChat;             // Type: open the chat first with /, send it after with Enter

        public bool AtPointer;          // Click, Scroll: wherever the pointer already is
        // Click: down for HoldMs and no pause after. An autoclicker's click,
        // which keeps its own time; never saved, because only it makes them.
        public bool Quick;
        public double X2, Y2;           // Drag: where it ends, as fractions of the window
        public int Notches;             // Scroll: wheel clicks, up when positive

        // ClickPicture, WaitPicture, and a Repeat that goes by a picture.
        public string Picture;          // the file in the templates folder
        public string Mask;             // its painted-out copy, or null
        public double Tolerance = DEFAULT_TOLERANCE;
        public string Box;              // a box drawn for the game to look in, or null for the whole window
        public int OffX, OffY;          // ClickPicture: this many pixels right and down of its middle
        public int TimeoutMs;           // how long to wait for it; 0 looks once
        public bool StopIfMissing;      // not there in time: stop the macro, rather than carry on
        public bool UntilGone;          // WaitPicture: wait for it to go, not to turn up

        public RepeatMode Mode;         // Repeat
        public int Count = 2;           // Repeat: Times

        // The same as a picture watcher starts with.
        public const double DEFAULT_TOLERANCE = 0.82;

        // Between two looks while waiting for a picture.
        public const int LOOK_EVERY_MS = 150;

        // One wheel click, and the pause after it.
        public const int NOTCH_MS = 30;

        // A click is pressed and let go this far apart; typing takes this long
        // a character. Both are what Roblox reliably notices.
        public const int CLICK_MS = 50;
        public const int CHAR_MS = 15;

        // A key held this long or more is described as held, not pressed.
        const int HOLD_FROM_MS = 300;

        public static MacroStep Key(byte vk, int holdMs)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.Key; s.Vk = vk; s.HoldMs = holdMs;
            return s;
        }

        // Where on the window, as fractions, so a click lands on the same
        // button whatever size the window is.
        public static MacroStep Click(double x, double y, bool right)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.Click; s.X = x; s.Y = y; s.RightButton = right;
            return s;
        }

        public static MacroStep KeyDown(byte vk)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.KeyDown; s.Vk = vk;
            return s;
        }

        public static MacroStep KeyUp(byte vk)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.KeyUp; s.Vk = vk;
            return s;
        }

        public static MacroStep Wait(int ms)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.Wait; s.Ms = ms;
            return s;
        }

        public static MacroStep Type(string text)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.Type; s.Text = text ?? "";
            return s;
        }

        public static MacroStep ClickHere(bool right)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.Click; s.AtPointer = true; s.RightButton = right;
            return s;
        }

        public static MacroStep ClickPicture(string picture, string mask, int timeoutMs)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.ClickPicture; s.Picture = picture; s.Mask = mask; s.TimeoutMs = timeoutMs;
            return s;
        }

        public static MacroStep WaitPicture(string picture, string mask, int timeoutMs, bool untilGone)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.WaitPicture; s.Picture = picture; s.Mask = mask;
            s.TimeoutMs = timeoutMs; s.UntilGone = untilGone;
            return s;
        }

        public static MacroStep Move(double x, double y)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.Move; s.X = x; s.Y = y;
            return s;
        }

        public static MacroStep Drag(double x, double y, double x2, double y2, int ms, bool right)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.Drag; s.X = x; s.Y = y; s.X2 = x2; s.Y2 = y2; s.Ms = ms; s.RightButton = right;
            return s;
        }

        public static MacroStep Scroll(double x, double y, int notches)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.Scroll; s.X = x; s.Y = y; s.Notches = notches;
            return s;
        }

        public static MacroStep Repeat(RepeatMode mode, int count)
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.Repeat; s.Mode = mode; s.Count = count;
            return s;
        }

        public static MacroStep EndRepeat()
        {
            MacroStep s = new MacroStep();
            s.Kind = MacroStepKind.EndRepeat;
            return s;
        }

        public bool UsesPicture
        {
            get
            {
                return Kind == MacroStepKind.ClickPicture || Kind == MacroStepKind.WaitPicture
                    || (Kind == MacroStepKind.Repeat && (Mode == RepeatMode.UntilSeen || Mode == RepeatMode.UntilGone));
            }
        }

        // A picture is saved as "name-20261110-213000.png"; the name is what
        // it is called.
        public static string PictureLabel(string file)
        {
            if (string.IsNullOrEmpty(file)) return "a picture";
            string stem = System.IO.Path.GetFileNameWithoutExtension(file);
            int cut = stem.Length - 16;         // "-yyyyMMdd-HHmmss"
            if (cut > 0 && stem[cut] == '-' && stem[cut + 9] == '-') stem = stem.Substring(0, cut);
            return stem;
        }

        public int DurationMs
        {
            get
            {
                switch (Kind)
                {
                    case MacroStepKind.Key: return HoldMs;
                    case MacroStepKind.Click: return CLICK_MS;
                    case MacroStepKind.Wait: return Ms;
                    case MacroStepKind.Type:
                        return (Text ?? "").Length * CHAR_MS + (InChat ? MacroPlan.CHAT_OPEN_MS + 2 * HoldMs : 0);
                    // The longest they can take: the whole wait, then the click.
                    case MacroStepKind.ClickPicture: return TimeoutMs + CLICK_MS;
                    case MacroStepKind.WaitPicture: return TimeoutMs;
                    case MacroStepKind.Drag: return Ms + CLICK_MS;
                    case MacroStepKind.Scroll: return Math.Abs(Notches) * NOTCH_MS;
                    default: return 0;
                }
            }
        }

        public string Describe()
        {
            switch (Kind)
            {
                case MacroStepKind.Key:
                    return HoldMs >= HOLD_FROM_MS
                        ? "Hold " + MacroKeys.Name(Vk) + " for " + Seconds(HoldMs)
                        : "Press " + MacroKeys.Name(Vk);
                case MacroStepKind.Click:
                    if (AtPointer) return (RightButton ? "Right-click" : "Click") + " where the pointer is";
                    return (RightButton ? "Right-click " : "Click ") + Percent(X) + " across, " + Percent(Y) + " down";
                case MacroStepKind.Wait:
                    return "Wait " + Seconds(Ms);
                case MacroStepKind.KeyDown:
                    return "Hold down " + MacroKeys.Name(Vk);
                case MacroStepKind.KeyUp:
                    return "Release " + MacroKeys.Name(Vk);
                case MacroStepKind.ClickPicture:
                    return (RightButton ? "Right-click " : "Click ") + PictureLabel(Picture) + " where it is"
                         + (TimeoutMs > 0 ? ", waiting up to " + Seconds(TimeoutMs) : "")
                         + (StopIfMissing ? " - stop if it isn't there" : "");
                case MacroStepKind.WaitPicture:
                    return "Wait until " + PictureLabel(Picture) + (UntilGone ? " disappears" : " appears")
                         + ", up to " + Seconds(TimeoutMs) + (StopIfMissing ? " - stop if not" : "");
                case MacroStepKind.Move:
                    return "Move to " + Percent(X) + " across, " + Percent(Y) + " down";
                case MacroStepKind.Drag:
                    return (RightButton ? "Right-drag " : "Drag ") + Percent(X) + ", " + Percent(Y) + " to "
                         + Percent(X2) + ", " + Percent(Y2) + " over " + Seconds(Ms);
                case MacroStepKind.Scroll:
                    return "Scroll " + (Notches >= 0 ? "up " : "down ") + Math.Abs(Notches)
                         + (AtPointer ? " where the pointer is" : "");
                case MacroStepKind.Repeat:
                    switch (Mode)
                    {
                        case RepeatMode.Forever: return "Repeat until stopped:";
                        case RepeatMode.UntilSeen: return "Repeat until " + PictureLabel(Picture) + " appears:";
                        case RepeatMode.UntilGone: return "Repeat until " + PictureLabel(Picture) + " disappears:";
                        default: return "Repeat " + Count + " times:";
                    }
                case MacroStepKind.EndRepeat:
                    return "End of repeat";
                case MacroStepKind.Type:
                    return InChat ? "Say \"" + Text + "\" in chat" : "Type \"" + Text + "\"";
                default:
                    return "A step from a newer RobloxKeeper";
            }
        }

        public static string Seconds(int ms)
        {
            return (ms / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + "s";
        }

        static string Percent(double f)
        {
            return ((int)Math.Round(f * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        public MacroStep Copy()
        {
            return (MacroStep)MemberwiseClone();
        }
    }

    // A named list of steps, played on one client.
    //
    // Playing needs the client in front - Roblox only reads input that reaches
    // the foreground window - so a macro holds focus for as long as it runs.
    // That is why it is kept short.
    class Macro
    {
        public string Name;
        public readonly List<MacroStep> Steps = new List<MacroStep>();

        // Played on the screen itself rather than on a Roblox window: its
        // places are fractions of the whole desktop, every monitor together,
        // and nothing is brought to the front for it. Quick Record makes these.
        public bool Anywhere;

        // A key that plays it from anywhere, or 0 for none, and what pressing
        // it does.
        public byte Key;
        public MacroKeyMode KeyMode = MacroKeyMode.Once;

        // How a play goes when nothing else says: this many times (0 until it
        // is stopped), at this speed - 200 is twice as fast.
        public int Times = 1;
        public int SpeedPct = 100;

        public const int MAX_MS = 60000;

        // One that plays anywhere holds no client in front, so the minute does
        // not apply; this is only there so a slip of the keyboard can't make
        // one that goes on all day.
        public const int MAX_ANYWHERE_MS = 30 * 60 * 1000;

        // A repeat inside a repeat inside a repeat is as deep as anyone can
        // follow in a list.
        public const int MAX_DEPTH = 3;

        // The longest one play can take, a repeat counted as many times as it
        // runs - and once, where only stopping it or a picture ends it.
        public int DurationMs
        {
            get
            {
                long ms = 0;
                List<long> outer = new List<long>();
                List<int> times = new List<int>();
                foreach (MacroStep s in Steps)
                {
                    if (s.Kind == MacroStepKind.Repeat)
                    {
                        outer.Add(ms);
                        times.Add(s.Mode == RepeatMode.Times ? Math.Max(1, s.Count) : 1);
                        ms = 0;
                    }
                    else if (s.Kind == MacroStepKind.EndRepeat)
                    {
                        if (outer.Count == 0) continue;
                        ms = outer[outer.Count - 1] + ms * times[times.Count - 1];
                        outer.RemoveAt(outer.Count - 1);
                        times.RemoveAt(times.Count - 1);
                    }
                    else ms += s.DurationMs;
                }
                while (outer.Count > 0)
                {
                    ms = outer[outer.Count - 1] + ms * times[times.Count - 1];
                    outer.RemoveAt(outer.Count - 1);
                    times.RemoveAt(times.Count - 1);
                }
                return ms > int.MaxValue ? int.MaxValue : (int)ms;
            }
        }

        // It has a repeat that only stopping it, or a picture, ends: it plays
        // for as long as it is left to, and gives the keyboard up in between
        // to a nudge or to another client's turn.
        public bool IsSession
        {
            get
            {
                foreach (MacroStep s in Steps)
                    if (s.Kind == MacroStepKind.Repeat && s.Mode != RepeatMode.Times) return true;
                return false;
            }
        }

        // Null when every Repeat has its End and nothing is nested too deep.
        public string BlockProblem()
        {
            int depth = 0;
            foreach (MacroStep s in Steps)
            {
                if (s.Kind == MacroStepKind.Repeat)
                {
                    depth++;
                    if (depth > MAX_DEPTH) return "That's too many repeats inside each other. " + MAX_DEPTH + " deep is the most.";
                }
                else if (s.Kind == MacroStepKind.EndRepeat)
                {
                    if (depth == 0) return "There's an \"End of repeat\" with no repeat above it. Move it below one, or remove it.";
                    depth--;
                }
            }
            return depth > 0 ? "A repeat has no end. Add \"End of repeat\" below the steps it repeats." : null;
        }

        // Null when it can be saved and played.
        public string Problem()
        {
            if (string.IsNullOrEmpty(Name) || Name.Trim().Length == 0) return "Give the macro a name.";
            if (Steps.Count == 0) return "Add at least one step.";
            string block = BlockProblem();
            if (block != null) return block;
            for (int i = 0; i < Steps.Count; i++)
            {
                MacroStep s = Steps[i];
                if (s.UsesPicture && string.IsNullOrEmpty(s.Picture))
                    return "Step " + (i + 1) + " has no picture yet. Edit it and choose one.";
                if (s.Kind == MacroStepKind.Repeat && i + 1 < Steps.Count && Steps[i + 1].Kind == MacroStepKind.EndRepeat)
                    return "Step " + (i + 1) + " repeats nothing. Put steps between it and its end.";
            }
            if (Anywhere)
            {
                if (!IsSession && DurationMs > MAX_ANYWHERE_MS)
                    return "Keep it under half an hour. For longer, put the steps in a repeat until stopped.";
            }
            else if (!IsSession && DurationMs > MAX_MS)
                return "Keep it under a minute, because the Roblox window stays in front while a macro plays. "
                     + "For longer, put the steps in a repeat until stopped.";
            return null;
        }

        public Macro Copy()
        {
            Macro m = new Macro();
            m.Name = Name;
            m.Anywhere = Anywhere;
            m.Key = Key;
            m.KeyMode = KeyMode;
            m.Times = Times;
            m.SpeedPct = SpeedPct;
            foreach (MacroStep s in Steps) m.Steps.Add(s.Copy());
            return m;
        }
    }

    // What a key is called, as it is printed on the keyboard.
    static class MacroKeys
    {
        public static string Name(byte vk)
        {
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            switch (vk)
            {
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x10: case 0xA0: case 0xA1: return "Shift";
                case 0x11: case 0xA2: case 0xA3: return "Ctrl";
                case 0x12: case 0xA4: case 0xA5: return "Alt";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x25: return "Left arrow";
                case 0x26: return "Up arrow";
                case 0x27: return "Right arrow";
                case 0x28: return "Down arrow";
                case 0x2E: return "Delete";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "`";
                default: return ((Keys)vk).ToString();
            }
        }
    }
}
