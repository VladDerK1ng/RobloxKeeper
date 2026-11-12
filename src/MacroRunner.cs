using System;
using System.Collections.Generic;
using System.Drawing;

namespace RobloxKeeper
{
    // What a look at the client found.
    struct PictureLook
    {
        public bool Found;
        public Point At;            // where to click, on the screen
        public double Score;
        public string Problem;      // the picture can't be looked for at all, in plain words
    }

    // Everything a macro touches while it plays: the client's window, the
    // keyboard and mouse, the clock. The real one is in MacroPlayer; tests
    // hand in their own, so every decision here is checked without a game.
    interface IMacroWorld
    {
        Rectangle Client { get; }           // the game's part of the window, on the screen
        PictureLook Look(MacroStep s);      // is this step's picture on the client right now, and where
        bool InFront();                     // is the client still the window in front
        void Send(InputAction a);           // never a Sleep
        void Sleep(int ms);                 // comes back early once StopAsked
        long NowMs { get; }
        bool StopAsked { get; }             // the stop key, or the session being switched off
        bool GateWanted { get; }            // a nudge is waiting for the keyboard, or a macro about to give up waiting
        bool OthersWaiting { get; }         // another macro is waiting its turn
    }

    enum RunEnd
    {
        Finished,       // played to the end
        Yielded,        // gave the keyboard up part-way; Run again carries on from there
        TakenOver,      // another window came to the front
        Cancelled,      // stopped on purpose
        Failed          // Problem says why
    }

    // Finding a picture in a capture, and where on the screen that is.
    //
    // A capture is a picture of the whole window - frame and title bar too -
    // and may not be the size the window is on the screen, where the display
    // is scaled. So a find is kept as a fraction of the capture and turned
    // into a screen point against the window, never the client area: by
    // pixels, or against the client area, a windowed client would be clicked
    // a title bar too high.
    static class PictureSearch
    {
        public static bool Find(Pixels shot, Pixels needle, bool[] mask, double tolerance, Rectangle box,
                                int offX, int offY, out double fx, out double fy, out double score)
        {
            fx = fy = score = 0;
            if (shot == null || needle == null || shot.IsEmpty || needle.IsEmpty) return false;

            Rectangle whole = new Rectangle(0, 0, shot.Width, shot.Height);
            Rectangle area = box.IsEmpty ? whole : Rectangle.Intersect(box, whole);
            // A box smaller than the picture can't hold it: look everywhere
            // instead of never finding it.
            if (area.Width < needle.Width || area.Height < needle.Height) area = whole;
            if (area.Width < needle.Width || area.Height < needle.Height) return false;

            Pixels hay = area == whole ? shot : shot.Crop(area);
            ImageHit hit = ImageMatch.FindQuick(hay, needle, mask, tolerance);
            score = hit.Score;
            if (!hit.Found) return false;

            fx = (area.X + hit.X + needle.Width / 2.0 + offX) / shot.Width;
            fy = (area.Y + hit.Y + needle.Height / 2.0 + offY) / shot.Height;
            return true;
        }

        public static Point OnScreen(Rectangle window, double fx, double fy)
        {
            if (fx < 0) fx = 0; if (fx > 1) fx = 1;
            if (fy < 0) fy = 0; if (fy > 1) fy = 1;
            return new Point(window.Left + (int)Math.Round(fx * (window.Width - 1)),
                             window.Top + (int)Math.Round(fy * (window.Height - 1)));
        }
    }

    // Plays a macro a step at a time, deciding as it goes: whether a picture
    // is there, whether a repeat goes round again, and when to give the
    // keyboard up to something else that needs it.
    //
    // It keeps its place. A session - a macro with a repeat that only
    // stopping it ends - is played in turns: Run comes back with Yielded, and
    // the next Run carries on from the same step with the same keys held.
    // So is anything with StepsAside set: a play long enough to keep the
    // nudge out, though it ends by itself.
    class MacroRunner
    {
        // A round of a repeat takes at least this long, so one with nothing
        // slow in it can't spin the processor or flood the game.
        public const int MIN_ROUND_MS = 20;

        // A session with others waiting plays for this long before it lets
        // the next one have its turn.
        public const int DEFAULT_TURN_MS = 5000;

        class Frame
        {
            public int Start;           // the Repeat step
            public int Done;            // rounds finished
            public long RoundBegan;
        }

        readonly Macro macro;
        readonly int[] endOf;           // for a Repeat, its EndRepeat; steps.Count when it has none
        readonly List<Frame> frames = new List<Frame>();
        readonly List<byte> held = new List<byte>();
        readonly List<byte> letGo = new List<byte>();   // let go to yield, pressed again on the way back in
        int pc;
        bool leftDown, rightDown;
        int waitLeft = -1;              // of a Wait a session stepped aside in the middle of

        // A long hold or drag stepped aside in the middle of: what is left of
        // it to send, and how far it had got.
        List<InputAction> part;
        int partAt;
        // The mouse buttons that drag had down when it stepped aside, and
        // where the pointer was: they go down again there on the way back in.
        bool pressLeft, pressRight;
        Point pointer;

        // What was in front, where the pointer was and how the client was,
        // before the first turn of this play - see MacroPlayer.PlayTurn.
        public readonly FrontBefore Before = new FrontBefore();

        public string Problem { get; private set; }
        public int Rounds { get; private set; }         // of the outermost repeat
        public int TurnMs = DEFAULT_TURN_MS;

        // Takes turns with the keyboard as a session does, though it ends by
        // itself. Set for a play that can last far past a minute - one on the
        // whole screen, or one played several times over or slowed down - so
        // the nudge, which gives up after a minute, is never kept out by it.
        public bool StepsAside;

        // A session, or told to step aside like one.
        bool TakesTurns { get { return StepsAside || macro.IsSession; } }

        public MacroRunner(Macro m)
        {
            macro = m;
            endOf = new int[m.Steps.Count];
            List<int> open = new List<int>();
            for (int i = 0; i < m.Steps.Count; i++)
            {
                endOf[i] = m.Steps.Count;
                if (m.Steps[i].Kind == MacroStepKind.Repeat) open.Add(i);
                else if (m.Steps[i].Kind == MacroStepKind.EndRepeat && open.Count > 0)
                {
                    endOf[open[open.Count - 1]] = i;
                    open.RemoveAt(open.Count - 1);
                }
            }
        }

        public Macro Macro { get { return macro; } }

        // Keys down right now, for whoever has to let go of them.
        public IList<byte> Held { get { return held; } }

        public RunEnd Run(IMacroWorld w)
        {
            long began = w.NowMs;

            // Stopped while it waited for its turn: what it let go of to step
            // aside stays let go. Pressing it again only to let go of it once
            // more would walk the character a step for nothing.
            if (w.StopAsked)
            {
                letGo.Clear();
                // So is a mouse button a drag let go of.
                pressLeft = pressRight = false;
                part = null;
                return End(w, RunEnd.Cancelled, null);
            }

            // Back from a turn given up: the keys it was holding go down again.
            foreach (byte vk in letGo)
            {
                InputAction down = InputAction.Of(InputActionKind.KeyDown);
                down.Vk = vk;
                w.Send(down);
                held.Add(vk);
            }
            letGo.Clear();

            // Every turn gets at least one step in, so two things that both
            // want the keyboard can't hand it back and forth doing nothing.
            bool stepped = false;
            bool session = TakesTurns;
            while (pc < macro.Steps.Count)
            {
                if (w.StopAsked) return End(w, RunEnd.Cancelled, null);
                if (!w.InFront()) return End(w, RunEnd.TakenOver, null);
                if (session && stepped &&
                    (w.GateWanted || (w.OthersWaiting && w.NowMs - began >= TurnMs)))
                    return Yield(w);
                stepped = true;

                MacroStep s = macro.Steps[pc];
                RunEnd? ended = null;
                switch (s.Kind)
                {
                    case MacroStepKind.Repeat: ended = BeginRound(w, s); break;
                    case MacroStepKind.EndRepeat: EndRound(w); break;
                    case MacroStepKind.ClickPicture: ended = ClickPicture(w, s); break;
                    case MacroStepKind.WaitPicture: ended = WaitPicture(w, s); break;
                    case MacroStepKind.Wait:
                        if (session) { ended = WaitInTurns(w, s); break; }
                        goto default;
                    default:
                    {
                        if (session && IsLong(s)) { ended = PlayInTurns(w, s); break; }
                        List<InputAction> plan = new List<InputAction>();
                        MacroPlan.Add(plan, new List<byte>(), s, w.Client);
                        ended = Play(w, plan);
                        pc++;
                        break;
                    }
                }
                if (ended.HasValue)
                    return ended.Value == RunEnd.Yielded ? Yield(w) : End(w, ended.Value, Problem);
            }
            return End(w, RunEnd.Finished, null);
        }

        // A long wait in a session is slept a little at a time, so a nudge
        // that wants the keyboard gets it now and not at the end of a minute
        // - by when it would have given up. What is left of the wait is
        // remembered for the turn after.
        const int WAIT_SLICE_MS = 250;

        RunEnd? WaitInTurns(IMacroWorld w, MacroStep s)
        {
            if (waitLeft < 0) waitLeft = s.Ms;
            bool slept = false;
            while (waitLeft > 0)
            {
                if (w.StopAsked) return RunEnd.Cancelled;
                if (slept && w.GateWanted) return RunEnd.Yielded;
                int slice = Math.Min(waitLeft, WAIT_SLICE_MS);
                w.Sleep(slice);
                waitLeft -= slice;
                slept = true;
            }
            waitLeft = -1;
            pc++;
            return null;
        }

        // A key held, or a drag, for longer than this is one sleep of the
        // whole length - a recording can hold W for twenty seconds, and a
        // quarter speed makes that eighty, past the minute the nudge waits.
        // So in a play that takes turns it is played so it can step aside
        // part-way. Anything shorter is played whole: letting go of a key or
        // a button and pressing it again is worse than a second's wait.
        public const int LONG_STEP_MS = 1000;

        static bool IsLong(MacroStep s)
        {
            if (s.Kind == MacroStepKind.Key) return s.HoldMs > LONG_STEP_MS;
            if (s.Kind == MacroStepKind.Drag) return s.Ms > LONG_STEP_MS;
            return false;
        }

        // A long hold or drag, played so a nudge that wants the keyboard gets
        // it part-way. Its pauses are slept a little at a time, as a long
        // wait is, and the place in it is kept for the turn after. A key held
        // is let go of and pressed again by Yield and Run, as any held key
        // is; a mouse button down mid-drag is let go of the same way and goes
        // down again here, where the pointer was, so the drag carries on.
        RunEnd? PlayInTurns(IMacroWorld w, MacroStep s)
        {
            if (part == null)
            {
                part = new List<InputAction>();
                MacroPlan.Add(part, new List<byte>(), s, w.Client);
                partAt = 0;
            }
            else if (pressLeft || pressRight)
            {
                if (w.StopAsked) return RunEnd.Cancelled;
                if (!w.InFront()) return RunEnd.TakenOver;
                InputAction move = InputAction.Of(InputActionKind.MoveTo);
                move.X = pointer.X; move.Y = pointer.Y;
                w.Send(move);
                // Roblox only knows where the pointer is once it has seen it
                // arrive, and only knows a button is down once it has seen
                // it go down.
                w.Sleep(MacroPlan.SETTLE_MS);
                // Stopped, or another window in front, in that moment: a
                // button pressed now would be a click nobody asked for.
                if (w.StopAsked) { pressLeft = pressRight = false; return RunEnd.Cancelled; }
                if (!w.InFront()) { pressLeft = pressRight = false; return RunEnd.TakenOver; }
                if (pressLeft) ButtonDown(w, false);
                if (pressRight) ButtonDown(w, true);
                pressLeft = pressRight = false;
                w.Sleep(MacroPlan.SETTLE_MS);
            }

            // Every turn gets somewhere in it before it can step aside again,
            // as with a long wait.
            bool went = false;
            while (partAt < part.Count)
            {
                if (w.StopAsked) return RunEnd.Cancelled;
                InputAction a = part[partAt];
                if (a.Kind == InputActionKind.Sleep)
                {
                    int left = a.Ms;
                    while (left > 0)
                    {
                        if (w.StopAsked) return RunEnd.Cancelled;
                        if (went && w.GateWanted)
                        {
                            // The rest of this pause is paused after, and a
                            // button down goes down again before it.
                            part[partAt] = InputAction.Sleep(left);
                            pressLeft = leftDown;
                            pressRight = rightDown;
                            return RunEnd.Yielded;
                        }
                        int slice = Math.Min(left, WAIT_SLICE_MS);
                        w.Sleep(slice);
                        left -= slice;
                        went = true;
                    }
                    partAt++;
                    continue;
                }
                if (!w.InFront()) return RunEnd.TakenOver;
                Sent(w, a);
                went = true;
                partAt++;
            }
            part = null;
            pc++;
            return null;
        }

        void ButtonDown(IMacroWorld w, bool right)
        {
            InputAction down = InputAction.Of(InputActionKind.ButtonDown);
            down.Right = right;
            w.Send(down);
            if (right) rightDown = true; else leftDown = true;
        }

        // ---------- repeats ----------

        // At a Repeat: goes into the round, or past its end when it is over.
        RunEnd? BeginRound(IMacroWorld w, MacroStep s)
        {
            Frame f = frames.Count > 0 && frames[frames.Count - 1].Start == pc ? frames[frames.Count - 1] : null;
            if (f == null)
            {
                f = new Frame();
                f.Start = pc;
                frames.Add(f);
            }

            bool again;
            switch (s.Mode)
            {
                case RepeatMode.Forever: again = true; break;
                case RepeatMode.UntilSeen:
                case RepeatMode.UntilGone:
                {
                    PictureLook look = w.Look(s);
                    if (look.Problem != null) { Problem = look.Problem; return RunEnd.Failed; }
                    again = look.Found != (s.Mode == RepeatMode.UntilSeen);
                    break;
                }
                default: again = f.Done < Math.Max(1, s.Count); break;
            }

            if (again)
            {
                f.RoundBegan = w.NowMs;
                pc++;
            }
            else
            {
                frames.RemoveAt(frames.Count - 1);
                pc = endOf[pc] + 1;
            }
            return null;
        }

        void EndRound(IMacroWorld w)
        {
            // One with no Repeat above it - a list dragged out of order - is
            // stepped over rather than trusted.
            if (frames.Count == 0) { pc++; return; }

            Frame f = frames[frames.Count - 1];
            f.Done++;
            if (frames.Count == 1) Rounds++;
            long took = w.NowMs - f.RoundBegan;
            if (took < MIN_ROUND_MS) w.Sleep((int)(MIN_ROUND_MS - took));
            pc = f.Start;
        }

        // ---------- pictures ----------

        RunEnd? ClickPicture(IMacroWorld w, MacroStep s)
        {
            PictureLook look;
            RunEnd? ended = WaitFor(w, s, true, out look);
            if (ended.HasValue) return ended;

            if (look.Found)
            {
                List<InputAction> plan = new List<InputAction>();
                MacroPlan.ClickAt(plan, look.At, s.RightButton);
                ended = Play(w, plan);
                if (ended.HasValue) return ended;
            }
            else if (s.StopIfMissing)
            {
                Problem = "couldn't find " + MacroStep.PictureLabel(s.Picture) + " on the Roblox window";
                return RunEnd.Failed;
            }
            pc++;
            return null;
        }

        RunEnd? WaitPicture(IMacroWorld w, MacroStep s)
        {
            PictureLook look;
            RunEnd? ended = WaitFor(w, s, !s.UntilGone, out look);
            if (ended.HasValue) return ended;

            if (look.Found == s.UntilGone && s.StopIfMissing)
            {
                Problem = MacroStep.PictureLabel(s.Picture) + (s.UntilGone ? " never went away" : " never showed");
                return RunEnd.Failed;
            }
            pc++;
            return null;
        }

        // Looks until the picture is there (or gone), or the step's time is up.
        RunEnd? WaitFor(IMacroWorld w, MacroStep s, bool wantSeen, out PictureLook look)
        {
            long began = w.NowMs;
            while (true)
            {
                look = w.Look(s);
                if (look.Problem != null) { Problem = look.Problem; return RunEnd.Failed; }
                if (look.Found == wantSeen) return null;
                if (w.NowMs - began >= s.TimeoutMs) return null;

                w.Sleep(MacroStep.LOOK_EVERY_MS);
                if (w.StopAsked) return RunEnd.Cancelled;
                if (!w.InFront()) return RunEnd.TakenOver;
                // A session waiting on a picture steps aside for the nudge
                // too. It starts the wait again when it is back, which is
                // the right way round: it was not looking while it was away.
                if (TakesTurns && w.GateWanted) return RunEnd.Yielded;
            }
        }

        // ---------- sending ----------

        RunEnd? Play(IMacroWorld w, List<InputAction> plan)
        {
            foreach (InputAction a in plan)
            {
                if (w.StopAsked) return RunEnd.Cancelled;
                if (a.Kind == InputActionKind.Sleep)
                {
                    if (a.Ms > 0) w.Sleep(a.Ms);
                    continue;
                }
                if (!w.InFront()) return RunEnd.TakenOver;
                Sent(w, a);
            }
            return null;
        }

        // Sends one action and keeps track of what it leaves held down, and
        // where it leaves the pointer.
        void Sent(IMacroWorld w, InputAction a)
        {
            w.Send(a);
            if (a.Kind == InputActionKind.KeyDown && !held.Contains(a.Vk)) held.Add(a.Vk);
            else if (a.Kind == InputActionKind.KeyUp) held.Remove(a.Vk);
            else if (a.Kind == InputActionKind.ButtonDown) { if (a.Right) rightDown = true; else leftDown = true; }
            else if (a.Kind == InputActionKind.ButtonUp) { if (a.Right) rightDown = false; else leftDown = false; }
            else if (a.Kind == InputActionKind.MoveTo || a.Kind == InputActionKind.DragTo) pointer = new Point(a.X, a.Y);
        }

        // Giving the keyboard up: whatever is held is let go, and remembered.
        RunEnd Yield(IMacroWorld w)
        {
            letGo.AddRange(held);
            LetGoOfEverything(w);
            return RunEnd.Yielded;
        }

        // However it ends, nothing is left held down in the game - walking
        // forward for ever.
        RunEnd End(IMacroWorld w, RunEnd how, string problem)
        {
            Problem = problem;
            LetGoOfEverything(w);
            return how;
        }

        void LetGoOfEverything(IMacroWorld w)
        {
            foreach (byte vk in held)
            {
                InputAction up = InputAction.Of(InputActionKind.KeyUp);
                up.Vk = vk;
                w.Send(up);
            }
            held.Clear();

            // A drag stopped half-way would leave the button down too.
            if (leftDown) ButtonUp(w, false);
            if (rightDown) ButtonUp(w, true);
            leftDown = rightDown = false;
        }

        static void ButtonUp(IMacroWorld w, bool right)
        {
            InputAction up = InputAction.Of(InputActionKind.ButtonUp);
            up.Right = right;
            w.Send(up);
        }
    }
}
