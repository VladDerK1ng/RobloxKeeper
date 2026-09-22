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
            List<byte> held = new List<byte>();
            foreach (MacroStep s in m.Steps)
            {
                switch (s.Kind)
                {
                    case MacroStepKind.KeyDown:
                    {
                        InputAction down = InputAction.Of(InputActionKind.KeyDown);
                        down.Vk = s.Vk;
                        plan.Add(down);
                        if (!held.Contains(s.Vk)) held.Add(s.Vk);
                        break;
                    }
                    case MacroStepKind.KeyUp:
                    {
                        InputAction up = InputAction.Of(InputActionKind.KeyUp);
                        up.Vk = s.Vk;
                        plan.Add(up);
                        held.Remove(s.Vk);
                        break;
                    }
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
                        held.Remove(s.Vk);
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

            // A key held down and never let go would stay down in the game -
            // walking forward for ever - so whatever is still down at the end
            // is let go, however the macro was edited.
            foreach (byte vk in held)
            {
                InputAction up = InputAction.Of(InputActionKind.KeyUp);
                up.Vk = vk;
                plan.Add(up);
            }
            return plan;
        }
    }

    // One thing the hooks saw while recording.
    struct RawInput
    {
        public bool IsKey;
        public bool Down;
        public byte Vk;
        public bool Right;
        public int X, Y;        // a button: where on the screen
        public long Ms;         // when

        public static RawInput Key(bool down, byte vk, long ms)
        {
            RawInput r = new RawInput();
            r.IsKey = true; r.Down = down; r.Vk = vk; r.Ms = ms;
            return r;
        }

        public static RawInput Button(bool down, bool right, int x, int y, long ms)
        {
            RawInput r = new RawInput();
            r.Down = down; r.Right = right; r.X = x; r.Y = y; r.Ms = ms;
            return r;
        }
    }

    // What was pressed and clicked while recording, as steps.
    static class MacroRecording
    {
        // Ends a recording. Never part of the macro.
        public const byte STOP_KEY = 0x77;         // F8

        // Pauses shorter than this are the gaps between a person's own key
        // presses, not something the macro needs to wait for.
        const int MIN_WAIT_MS = 30;

        class Timed
        {
            public long At, End;
            public MacroStep Step;
        }

        public static List<MacroStep> ToSteps(IList<RawInput> events, Rectangle client, long endMs)
        {
            // First as holds, let-gos and clicks...
            List<Timed> raw = new List<Timed>();
            List<byte> down = new List<byte>();
            foreach (RawInput e in events)
            {
                if (e.IsKey)
                {
                    if (e.Vk == STOP_KEY) continue;
                    if (e.Down)
                    {
                        if (down.Contains(e.Vk)) continue;          // Windows repeating a held key
                        down.Add(e.Vk);
                        raw.Add(At(e.Ms, MacroStep.KeyDown(e.Vk)));
                    }
                    else if (down.Remove(e.Vk)) raw.Add(At(e.Ms, MacroStep.KeyUp(e.Vk)));
                }
                else if (e.Down && client.Contains(e.X, e.Y))
                {
                    double fx = (e.X - client.Left) / (double)Math.Max(1, client.Width - 1);
                    double fy = (e.Y - client.Top) / (double)Math.Max(1, client.Height - 1);
                    raw.Add(At(e.Ms, MacroStep.Click(fx, fy, e.Right)));
                }
            }
            foreach (byte vk in down) raw.Add(At(endMs, MacroStep.KeyUp(vk)));

            // ...then a hold straight followed by its own let-go is a press.
            List<Timed> steps = new List<Timed>();
            for (int i = 0; i < raw.Count; i++)
            {
                Timed t = raw[i];
                if (t.Step.Kind == MacroStepKind.KeyDown && i + 1 < raw.Count &&
                    raw[i + 1].Step.Kind == MacroStepKind.KeyUp && raw[i + 1].Step.Vk == t.Step.Vk)
                {
                    Timed press = At(t.At, MacroStep.Key(t.Step.Vk, (int)(raw[i + 1].At - t.At)));
                    press.End = raw[i + 1].At;
                    steps.Add(press);
                    i++;
                }
                else steps.Add(t);
            }

            // ...and the pauses between become waits. Not the one before the
            // first step: that is only how long it took to start.
            List<MacroStep> result = new List<MacroStep>();
            long last = steps.Count > 0 ? steps[0].At : 0;
            foreach (Timed t in steps)
            {
                long gap = t.At - last;
                if (gap >= MIN_WAIT_MS)
                    result.Add(MacroStep.Wait((int)(Math.Round(gap / 10.0, MidpointRounding.AwayFromZero) * 10)));
                result.Add(t.Step);
                last = t.End;
            }
            return result;
        }

        static Timed At(long ms, MacroStep s)
        {
            Timed t = new Timed();
            t.At = ms; t.End = ms; t.Step = s;
            return t;
        }
    }
}
