using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // KeyDown and KeyUp are for keys held across other steps - walking while
    // jumping. A key pressed and let go on its own is a Key.
    enum MacroStepKind { Key, Click, Wait, Type, KeyDown, KeyUp }

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

        public int DurationMs
        {
            get
            {
                switch (Kind)
                {
                    case MacroStepKind.Key: return HoldMs;
                    case MacroStepKind.Click: return CLICK_MS;
                    case MacroStepKind.Wait: return Ms;
                    case MacroStepKind.Type: return (Text ?? "").Length * CHAR_MS;
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
                    return (RightButton ? "Right-click " : "Click ") + Percent(X) + " across, " + Percent(Y) + " down";
                case MacroStepKind.Wait:
                    return "Wait " + Seconds(Ms);
                case MacroStepKind.KeyDown:
                    return "Hold down " + MacroKeys.Name(Vk);
                case MacroStepKind.KeyUp:
                    return "Let go of " + MacroKeys.Name(Vk);
                default:
                    return "Type \"" + Text + "\"";
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

        public const int MAX_MS = 60000;

        public int DurationMs
        {
            get
            {
                int ms = 0;
                foreach (MacroStep s in Steps) ms += s.DurationMs;
                return ms;
            }
        }

        // Null when it can be saved and played.
        public string Problem()
        {
            if (string.IsNullOrEmpty(Name) || Name.Trim().Length == 0) return "Give the macro a name.";
            if (Steps.Count == 0) return "Add at least one step.";
            if (DurationMs > MAX_MS)
                return "Keep it under a minute - the client is in front the whole time a macro plays.";
            return null;
        }

        public Macro Copy()
        {
            Macro m = new Macro();
            m.Name = Name;
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
