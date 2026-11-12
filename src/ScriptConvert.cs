using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RobloxKeeper
{
    // A macro - Quick Record's recording or any saved one - as a Lua script,
    // one readable line a step, ready to edit:
    //
    //     -- Made from the macro "Farm".
    //     while true do
    //         press("e")
    //         wait(0.5)
    //         click(50, 80)
    //     end
    //
    // What it sends is what the macro sends: the same keys, the same places,
    // the same waits. Only the macro's own Repeat and Speed settings are left
    // behind - it plays once, at its own speed - and a picture step's box,
    // likeness and offset, which scripts don't have, are said in a comment.
    static class ScriptConvert
    {
        public static string ToLua(Macro m)
        {
            PictureShelf shelf = new PictureShelf();
            if (m != null) shelf.AddSteps(m.Steps);
            return ToLua(m, shelf);
        }

        // shelf: every picture this PC knows, so a picture is written as what
        // it is called unless another is called that too.
        public static string ToLua(Macro m, PictureShelf shelf)
        {
            StringBuilder sb = new StringBuilder();
            if (m == null) return "";
            if (shelf == null) shelf = new PictureShelf();
            sb.Append("-- Made from the macro ").Append(Quote(m.Name ?? "")).Append(".\r\n");
            if (m.Times != 1 || m.SpeedPct != 100)
                sb.Append("-- The macro was set to play ")
                  .Append(m.Times <= 0 ? "until stopped" : m.Times == 1 ? "once" : m.Times + " times")
                  .Append(m.SpeedPct != 100 ? " at " + Num(m.SpeedPct / 100.0) + "x speed" : "")
                  .Append("; this plays it once, at its own speed.\r\n");
            if (m.Anywhere) sb.Append("use(\"screen\")\r\n");

            int depth = 0;
            foreach (MacroStep s in m.Steps)
            {
                if (s == null) continue;
                if (s.Kind == MacroStepKind.EndRepeat)
                {
                    // One with no repeat above it is stepped over, as the
                    // player steps over it.
                    if (depth == 0) continue;
                    depth--;
                    Line(sb, depth, "end");
                    continue;
                }
                if (s.Kind == MacroStepKind.Repeat)
                {
                    Line(sb, depth, RepeatLine(s, depth, shelf));
                    depth++;
                    continue;
                }
                Line(sb, depth, StepLine(s, shelf));
            }
            // A repeat with no end goes to the end of the macro.
            while (depth > 0) { depth--; Line(sb, depth, "end"); }
            return sb.ToString();
        }

        static void Line(StringBuilder sb, int depth, string text)
        {
            sb.Append(' ', depth * 4).Append(text).Append("\r\n");
        }

        static readonly string[] Counters = { "i", "j", "k" };

        static string RepeatLine(MacroStep s, int depth, PictureShelf shelf)
        {
            switch (s.Mode)
            {
                case RepeatMode.Forever: return "while true do";
                case RepeatMode.UntilSeen: return "while not find(" + Quote(shelf.NameOf(s.Picture)) + ") do" + Unsaid(s);
                case RepeatMode.UntilGone: return "while find(" + Quote(shelf.NameOf(s.Picture)) + ") do" + Unsaid(s);
                default:
                    string v = depth < Counters.Length ? Counters[depth] : "n" + depth;
                    return "for " + v + " = 1, " + Math.Max(1, s.Count).ToString(CultureInfo.InvariantCulture) + " do";
            }
        }

        static string StepLine(MacroStep s, PictureShelf shelf)
        {
            string right = s.RightButton ? ", \"right\"" : "";
            switch (s.Kind)
            {
                case MacroStepKind.Key:
                    return "press(" + Quote(ScriptKeys.Name(s.Vk)) + (s.HoldMs != 50 ? ", " + Sec(s.HoldMs) : "") + ")";
                case MacroStepKind.KeyDown: return "keydown(" + Quote(ScriptKeys.Name(s.Vk)) + ")";
                case MacroStepKind.KeyUp: return "keyup(" + Quote(ScriptKeys.Name(s.Vk)) + ")";
                case MacroStepKind.Wait: return "wait(" + Sec(s.Ms) + ")";
                case MacroStepKind.Click:
                    if (s.AtPointer) return s.RightButton ? "click(\"right\")" : "click()";
                    return "click(" + Pct(s.X) + ", " + Pct(s.Y) + right + ")";
                case MacroStepKind.Type:
                    return (s.InChat ? "chat(" : "type(") + Quote(s.Text ?? "") + ")";
                case MacroStepKind.ClickPicture:
                {
                    string call = "clickpicture(" + Quote(shelf.NameOf(s.Picture))
                                + (s.TimeoutMs > 0 || s.RightButton ? ", " + Sec(s.TimeoutMs) : "") + right + ")";
                    return (s.StopIfMissing ? "if not " + call + " then stop() end" : call) + Unsaid(s);
                }
                case MacroStepKind.WaitPicture:
                {
                    string call = (s.UntilGone ? "waitgone(" : "waitfor(") + Quote(shelf.NameOf(s.Picture)) + ", " + Sec(s.TimeoutMs) + ")";
                    return (s.StopIfMissing ? "if not " + call + " then stop() end" : call) + Unsaid(s);
                }
                case MacroStepKind.Move: return "move(" + Pct(s.X) + ", " + Pct(s.Y) + ")";
                case MacroStepKind.Drag:
                    return "drag(" + Pct(s.X) + ", " + Pct(s.Y) + ", " + Pct(s.X2) + ", " + Pct(s.Y2) + ", " + Sec(s.Ms) + right + ")";
                case MacroStepKind.Scroll:
                    return "scroll(" + s.Notches.ToString(CultureInfo.InvariantCulture)
                         + (s.AtPointer ? "" : ", " + Pct(s.X) + ", " + Pct(s.Y)) + ")";
                default:
                    return "-- a step from a newer RobloxKeeper";
            }
        }

        // What a picture step said that a script can't: kept as a comment so
        // nothing is lost from sight.
        static string Unsaid(MacroStep s)
        {
            List<string> said = new List<string>();
            if (!string.IsNullOrEmpty(s.Box)) said.Add("looked only in the box " + Quote(s.Box));
            if (Math.Abs(s.Tolerance - MacroStep.DEFAULT_TOLERANCE) >= 0.005)
                said.Add(((int)Math.Round(s.Tolerance * 100)).ToString(CultureInfo.InvariantCulture) + "% alike");
            if (s.Kind == MacroStepKind.ClickPicture && (s.OffX != 0 || s.OffY != 0))
                said.Add("clicked " + s.OffX.ToString(CultureInfo.InvariantCulture) + ", " + s.OffY.ToString(CultureInfo.InvariantCulture) + " pixels off its middle");
            return said.Count == 0 ? "" : "  -- the macro " + string.Join(", ", said.ToArray());
        }

        // Enough decimals that the place lands on the same pixel of a window
        // up to a hundred thousand pixels across.
        static string Pct(double fraction) { return Num(fraction * 100); }
        static string Sec(int ms) { return Num(ms / 1000.0); }

        static string Num(double v)
        {
            return Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);
        }

        // A Lua string in double quotes that reads back as exactly this text.
        public static string Quote(string text)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in text ?? "")
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        // Three digits always, so a digit after it is not read
                        // as part of it.
                        if (c < ' ' || c == (char)127) sb.Append('\\').Append(((int)c).ToString("000", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
