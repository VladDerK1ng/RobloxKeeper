using System;
using System.Collections.Generic;
using System.Drawing;

namespace RobloxKeeper
{
    enum InputActionKind { KeyDown, KeyUp, MoveTo, ButtonDown, ButtonUp, Char, Sleep }

    // One thing sent to Windows, or a pause.
    struct InputAction
    {
        public InputActionKind Kind;
        public byte Vk;
        public bool Right;
        public int X, Y;        // MoveTo: a point on the screen
        public char Char;
        public int Ms;          // Sleep

        public static InputAction Of(InputActionKind kind)
        {
            InputAction a = new InputAction();
            a.Kind = kind;
            return a;
        }

        public static InputAction Sleep(int ms)
        {
            InputAction a = Of(InputActionKind.Sleep);
            a.Ms = ms;
            return a;
        }
    }

    // What playing a macro sends, worked out before anything is sent.
    //
    // The sender is a loop over this list and nothing else, so everything a
    // macro does to the game is decided here, where it can be tested.
    static class MacroPlan
    {
        // After a key or a click, and between moving to a button and pressing
        // it: long enough for Roblox to notice the one before, short enough
        // not to matter.
        public const int SETTLE_MS = 40;

        // A fraction of the client area, as a point on the screen. Held inside
        // the window: a click that fell off its edge would land on whatever is
        // next to it.
        public static Point PointIn(Rectangle client, double fx, double fy)
        {
            if (fx < 0) fx = 0; if (fx > 1) fx = 1;
            if (fy < 0) fy = 0; if (fy > 1) fy = 1;
            return new Point(client.Left + (int)Math.Round(fx * (client.Width - 1)),
                             client.Top + (int)Math.Round(fy * (client.Height - 1)));
        }

        public static List<InputAction> For(Macro m, Rectangle client)
        {
            List<InputAction> plan = new List<InputAction>();
            foreach (MacroStep s in m.Steps)
            {
                switch (s.Kind)
                {
                    case MacroStepKind.Key:
                    {
                        InputAction down = InputAction.Of(InputActionKind.KeyDown);
                        down.Vk = s.Vk;
                        plan.Add(down);
                        plan.Add(InputAction.Sleep(s.HoldMs));
                        InputAction up = InputAction.Of(InputActionKind.KeyUp);
                        up.Vk = s.Vk;
                        plan.Add(up);
                        plan.Add(InputAction.Sleep(SETTLE_MS));
                        break;
                    }
                    case MacroStepKind.Click:
                    {
                        Point p = PointIn(client, s.X, s.Y);
                        InputAction move = InputAction.Of(InputActionKind.MoveTo);
                        move.X = p.X; move.Y = p.Y;
                        plan.Add(move);
                        // Roblox only knows a button is under the pointer once
                        // it has seen the pointer arrive.
                        plan.Add(InputAction.Sleep(SETTLE_MS));
                        InputAction press = InputAction.Of(InputActionKind.ButtonDown);
                        press.Right = s.RightButton;
                        plan.Add(press);
                        plan.Add(InputAction.Sleep(MacroStep.CLICK_MS));
                        InputAction release = InputAction.Of(InputActionKind.ButtonUp);
                        release.Right = s.RightButton;
                        plan.Add(release);
                        plan.Add(InputAction.Sleep(SETTLE_MS));
                        break;
                    }
                    case MacroStepKind.Wait:
                        plan.Add(InputAction.Sleep(s.Ms));
                        break;
                    default:
                        foreach (char c in s.Text ?? "")
                        {
                            InputAction ch = InputAction.Of(InputActionKind.Char);
                            ch.Char = c;
                            plan.Add(ch);
                            plan.Add(InputAction.Sleep(MacroStep.CHAR_MS));
                        }
                        break;
                }
            }
            return plan;
        }
    }
}
