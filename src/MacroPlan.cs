using System;
using System.Collections.Generic;
using System.Drawing;

namespace RobloxKeeper
{
    enum InputActionKind { KeyDown, KeyUp, MoveTo, ButtonDown, ButtonUp, Char, Sleep, DragTo, Wheel }

    // One thing sent to Windows, or a pause.
    struct InputAction
    {
        public InputActionKind Kind;
        public byte Vk;
        public bool Right;
        public int X, Y;        // MoveTo, DragTo: a point on the screen
        public int Dx, Dy;      // DragTo: how far that is from the point before
        public int Delta;       // Wheel: 120 a click, up when positive
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

        // Between pressing / and the chat box being ready to type into.
        public const int CHAT_OPEN_MS = 250;
        const byte VK_SLASH = 0xBF, VK_ENTER = 0x0D;

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

        // A drag is this many moves a second: enough for the game to see it
        // travel, not so many that it floods.
        const int DRAG_STEP_MS = 15;
        public const int WHEEL_CLICK = 120;

        // The whole macro as one list, for one that only presses, clicks and
        // waits. Steps that look at the screen, and repeats, are decided as
        // they run - MacroRunner plays those, a step at a time through Add.
        public static List<InputAction> For(Macro m, Rectangle client)
        {
            List<InputAction> plan = new List<InputAction>();
            List<byte> held = new List<byte>();
            foreach (MacroStep s in m.Steps) Add(plan, held, s, client);
            return Release(plan, held);
        }

        // Moves to a point, presses there and lets go.
        public static void ClickAt(List<InputAction> plan, Point p, bool right)
        {
            InputAction move = InputAction.Of(InputActionKind.MoveTo);
            move.X = p.X; move.Y = p.Y;
            plan.Add(move);
            // Roblox only knows a button is under the pointer once
            // it has seen the pointer arrive.
            plan.Add(InputAction.Sleep(SETTLE_MS));
            Press(plan, right, MacroStep.CLICK_MS, SETTLE_MS);
        }

        static void Press(List<InputAction> plan, bool right, int holdMs, int afterMs)
        {
            InputAction press = InputAction.Of(InputActionKind.ButtonDown);
            press.Right = right;
            plan.Add(press);
            plan.Add(InputAction.Sleep(holdMs));
            InputAction release = InputAction.Of(InputActionKind.ButtonUp);
            release.Right = right;
            plan.Add(release);
            if (afterMs > 0) plan.Add(InputAction.Sleep(afterMs));
        }

        // What one step sends, added to the plan. False for a step that has
        // to be decided as it runs, which adds nothing.
        public static bool Add(List<InputAction> plan, List<byte> held, MacroStep s, Rectangle client)
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
                    int hold = s.Quick ? Math.Max(1, s.HoldMs) : MacroStep.CLICK_MS;
                    int after = s.Quick ? 0 : SETTLE_MS;
                    // Where the pointer already is: nothing to arrive at.
                    if (!s.AtPointer)
                    {
                        Point p = PointIn(client, s.X, s.Y);
                        InputAction move = InputAction.Of(InputActionKind.MoveTo);
                        move.X = p.X; move.Y = p.Y;
                        plan.Add(move);
                        // Roblox only knows a button is under the pointer
                        // once it has seen the pointer arrive.
                        plan.Add(InputAction.Sleep(SETTLE_MS));
                    }
                    Press(plan, s.RightButton, hold, after);
                    break;
                }
                case MacroStepKind.Wait:
                    plan.Add(InputAction.Sleep(s.Ms));
                    break;
                case MacroStepKind.Move:
                {
                    Point p = PointIn(client, s.X, s.Y);
                    InputAction move = InputAction.Of(InputActionKind.MoveTo);
                    move.X = p.X; move.Y = p.Y;
                    plan.Add(move);
                    plan.Add(InputAction.Sleep(SETTLE_MS));
                    break;
                }
                case MacroStepKind.Drag:
                {
                    Point from = PointIn(client, s.X, s.Y), to = PointIn(client, s.X2, s.Y2);
                    InputAction move = InputAction.Of(InputActionKind.MoveTo);
                    move.X = from.X; move.Y = from.Y;
                    plan.Add(move);
                    plan.Add(InputAction.Sleep(SETTLE_MS));
                    InputAction press = InputAction.Of(InputActionKind.ButtonDown);
                    press.Right = s.RightButton;
                    plan.Add(press);
                    plan.Add(InputAction.Sleep(SETTLE_MS));

                    // In a straight line, a little at a time: turning the
                    // camera goes by how far the mouse travelled, and one
                    // jump from end to end is not travel.
                    int moves = Math.Max(2, s.Ms / DRAG_STEP_MS);
                    int each = Math.Max(1, s.Ms / moves);
                    Point last = from;
                    for (int i = 1; i <= moves; i++)
                    {
                        Point at = new Point(from.X + (to.X - from.X) * i / moves,
                                             from.Y + (to.Y - from.Y) * i / moves);
                        InputAction drag = InputAction.Of(InputActionKind.DragTo);
                        drag.X = at.X; drag.Y = at.Y;
                        drag.Dx = at.X - last.X; drag.Dy = at.Y - last.Y;
                        plan.Add(drag);
                        plan.Add(InputAction.Sleep(each));
                        last = at;
                    }

                    InputAction release = InputAction.Of(InputActionKind.ButtonUp);
                    release.Right = s.RightButton;
                    plan.Add(release);
                    plan.Add(InputAction.Sleep(SETTLE_MS));
                    break;
                }
                case MacroStepKind.Scroll:
                {
                    if (!s.AtPointer)
                    {
                        Point p = PointIn(client, s.X, s.Y);
                        InputAction move = InputAction.Of(InputActionKind.MoveTo);
                        move.X = p.X; move.Y = p.Y;
                        plan.Add(move);
                        plan.Add(InputAction.Sleep(SETTLE_MS));
                    }
                    for (int i = 0; i < Math.Abs(s.Notches); i++)
                    {
                        InputAction wheel = InputAction.Of(InputActionKind.Wheel);
                        wheel.Delta = s.Notches > 0 ? WHEEL_CLICK : -WHEEL_CLICK;
                        plan.Add(wheel);
                        plan.Add(InputAction.Sleep(MacroStep.NOTCH_MS));
                    }
                    break;
                }
                case MacroStepKind.Type:
                    if (s.InChat)
                    {
                        Tap(plan, VK_SLASH, s.HoldMs);
                        plan.Add(InputAction.Sleep(CHAT_OPEN_MS));
                    }
                    foreach (char c in s.Text ?? "")
                    {
                        InputAction ch = InputAction.Of(InputActionKind.Char);
                        ch.Char = c;
                        plan.Add(ch);
                        plan.Add(InputAction.Sleep(MacroStep.CHAR_MS));
                    }
                    if (s.InChat) Tap(plan, VK_ENTER, s.HoldMs);
                    break;
                default:
                    // Looks at the screen, or is a repeat - or is a kind a
                    // newer build saved, which this one sends nothing for.
                    return false;
            }
            return true;
        }

        static void Tap(List<InputAction> plan, byte vk, int holdMs)
        {
            InputAction down = InputAction.Of(InputActionKind.KeyDown);
            down.Vk = vk;
            plan.Add(down);
            plan.Add(InputAction.Sleep(holdMs));
            InputAction up = InputAction.Of(InputActionKind.KeyUp);
            up.Vk = vk;
            plan.Add(up);
        }

        static List<InputAction> Release(List<InputAction> plan, List<byte> held)
        {
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
        public bool IsMove;     // the pointer moved, nothing pressed
        public int Wheel;       // the wheel turned: 120 a click, up when positive
        public bool Down;
        public byte Vk;
        public bool Right;
        public int X, Y;        // a button, a move, the wheel: where on the screen
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

        public static RawInput Move(int x, int y, long ms)
        {
            RawInput r = new RawInput();
            r.IsMove = true; r.X = x; r.Y = y; r.Ms = ms;
            return r;
        }

        public static RawInput Scroll(int delta, int x, int y, long ms)
        {
            RawInput r = new RawInput();
            r.Wheel = delta; r.X = x; r.Y = y; r.Ms = ms;
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

        // A button let go this far from where it went down was a drag, not a
        // click with an unsteady hand.
        const int DRAG_FROM_PX = 8;

        // Pointer movement is kept as a place every so often, and only once
        // it has gone somewhere: a minute of every move a mouse reports would
        // be thousands of steps.
        const int MOVE_EVERY_MS = 100;
        const int MOVE_FROM_PX = 12;

        // Wheel clicks this close together are one scroll.
        const int SCROLL_WITHIN_MS = 300;

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
            Timed[] held = new Timed[2];
            bool[] pressed = new bool[2];           // left, right
            RawInput[] pressedAt = new RawInput[2];
            RawInput lastMove = new RawInput();
            bool moved = false;
            foreach (RawInput e in events)
            {
                if (e.IsMove)
                {
                    // While a button is down the travel is the drag's own.
                    if (pressed[0] || pressed[1] || !client.Contains(e.X, e.Y)) continue;
                    if (moved && (e.Ms - lastMove.Ms < MOVE_EVERY_MS ||
                                  (Math.Abs(e.X - lastMove.X) < MOVE_FROM_PX && Math.Abs(e.Y - lastMove.Y) < MOVE_FROM_PX)))
                        continue;
                    moved = true;
                    lastMove = e;
                    raw.Add(At(e.Ms, MacroStep.Move(Across(client, e.X), DownOf(client, e.Y))));
                }
                else if (e.Wheel != 0)
                {
                    if (!client.Contains(e.X, e.Y)) continue;
                    int notches = e.Wheel / MacroPlan.WHEEL_CLICK;
                    if (notches == 0) notches = e.Wheel > 0 ? 1 : -1;
                    Timed before = raw.Count > 0 ? raw[raw.Count - 1] : null;
                    if (before != null && before.Step.Kind == MacroStepKind.Scroll &&
                        e.Ms - before.End <= SCROLL_WITHIN_MS && (before.Step.Notches > 0) == (notches > 0))
                    {
                        before.Step.Notches += notches;
                        before.End = e.Ms;
                    }
                    else raw.Add(At(e.Ms, MacroStep.Scroll(Across(client, e.X), DownOf(client, e.Y), notches)));
                }
                else if (e.IsKey)
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
                else if (e.Down)
                {
                    if (!client.Contains(e.X, e.Y)) continue;
                    int b = e.Right ? 1 : 0;
                    // Kept as a click where it went down. Where it comes up
                    // says whether it was a drag instead.
                    Timed click = At(e.Ms, MacroStep.Click(Across(client, e.X), DownOf(client, e.Y), e.Right));
                    raw.Add(click);
                    pressed[b] = true;
                    pressedAt[b] = e;
                    held[b] = click;
                }
                else
                {
                    int b = e.Right ? 1 : 0;
                    if (!pressed[b]) continue;
                    pressed[b] = false;
                    RawInput from = pressedAt[b];
                    if (Math.Abs(e.X - from.X) < DRAG_FROM_PX && Math.Abs(e.Y - from.Y) < DRAG_FROM_PX) continue;
                    Timed drag = held[b];
                    drag.Step = MacroStep.Drag(Across(client, from.X), DownOf(client, from.Y),
                                               Across(client, e.X), DownOf(client, e.Y),
                                               (int)Math.Max(MacroStep.CLICK_MS, e.Ms - from.Ms), e.Right);
                    drag.End = e.Ms;
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

        static double Across(Rectangle client, int x)
        {
            double f = (x - client.Left) / (double)Math.Max(1, client.Width - 1);
            return f < 0 ? 0 : f > 1 ? 1 : f;
        }

        static double DownOf(Rectangle client, int y)
        {
            double f = (y - client.Top) / (double)Math.Max(1, client.Height - 1);
            return f < 0 ? 0 : f > 1 ? 1 : f;
        }

        static Timed At(long ms, MacroStep s)
        {
            Timed t = new Timed();
            t.At = ms; t.End = ms; t.Step = s;
            return t;
        }
    }
}
