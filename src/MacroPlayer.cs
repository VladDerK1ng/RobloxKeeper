using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Only one thing holds the foreground window at a time: the anti-AFK
    // nudge or a macro. Two of them moving focus about at once would each
    // send keys into the other's client.
    static class FocusGate
    {
        static readonly object gate = new object();
        static int waiting;         // everybody waiting to get in
        static int nudges;          // of them, the nudge
        static int givingUp;        // of them, macros most of the way to giving up

        // A wait is taken in slices this long, so a stop is felt at once
        // rather than at the end of fifteen seconds.
        const int SLICE_MS = 50;

        public static bool TryEnter(int waitMs) { return TryEnter(waitMs, false, null); }

        public static bool TryEnter(int waitMs, bool nudge) { return TryEnter(waitMs, nudge, null); }

        // nudge: this is the anti-AFK nudge, which whatever has the keyboard
        // makes way for at once - a macro waiting only gets its turn when the
        // one in front has had its own. stop: once true, it stops waiting;
        // false comes back, and the caller asks stop why.
        public static bool TryEnter(int waitMs, bool nudge, Func<bool> stop)
        {
            // The nudge is counted before it is counted as waiting, and
            // after it is not - so for a moment it is never a macro.
            if (nudge) Interlocked.Increment(ref nudges);
            Interlocked.Increment(ref waiting);
            bool late = false;
            try
            {
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    if (stop != null && stop()) return false;
                    long left = waitMs - clock.ElapsedMilliseconds;
                    if (Monitor.TryEnter(gate, (int)Math.Max(0, Math.Min(left, SLICE_MS)))) return true;
                    long waited = clock.ElapsedMilliseconds;
                    if (waited >= waitMs) return false;
                    // A macro two thirds of the way to giving up is let in
                    // as the nudge would be: the one in front may have a turn
                    // far longer than this wait, or sit in a long wait that
                    // only makes way for the nudge.
                    if (!nudge && !late && waited >= waitMs * 2L / 3)
                    {
                        late = true;
                        Interlocked.Increment(ref givingUp);
                    }
                }
            }
            finally
            {
                if (late) Interlocked.Decrement(ref givingUp);
                Interlocked.Decrement(ref waiting);
                if (nudge) Interlocked.Decrement(ref nudges);
            }
        }

        public static void Exit() { Monitor.Exit(gate); }

        // Somebody is waiting to get in. After stepping aside, whatever
        // stepped aside waits for them to get in first.
        public static bool Wanted { get { return Thread.VolatileRead(ref waiting) > 0; } }

        // The nudge is waiting. A session that plays for hours looks at this
        // between steps and steps aside, so the nudge - which gives up after
        // a minute - is never kept out.
        public static bool NudgeWanted { get { return Thread.VolatileRead(ref nudges) > 0; } }

        // A macro is waiting its turn. One playing a turn lets it in once its
        // turn is up - not after every step, which would swap the two back
        // and forth, letting go of and pressing again every held key.
        public static bool MacroWanted
        {
            get { return Thread.VolatileRead(ref waiting) - Thread.VolatileRead(ref nudges) > 0; }
        }

        // A macro waiting is close to giving up: it is let in now, turn or no.
        public static bool MacroGivingUp { get { return Thread.VolatileRead(ref givingUp) > 0; } }
    }

    // Stopping everything that is playing, at once: the stop key.
    static class MacroStop
    {
        static int epoch;

        public static int Epoch { get { return Thread.VolatileRead(ref epoch); } }
        public static void All() { Interlocked.Increment(ref epoch); }
    }

    // How one turn of a macro is played, beyond the macro itself.
    class PlayOptions
    {
        public Func<bool> Stop;                 // this one has been switched off
        public Func<bool> OthersWaiting;        // another macro is waiting its turn
        public IList<WatchRegion> Regions;      // the boxes drawn for games, for steps that look in one
        public string PlaceId;                  // which game the client is in, to choose its boxes

        // An autoclicker on the client you are playing: the pointer is yours,
        // so it is not put back, and Shift held to sprint is no reason to
        // refuse.
        public bool PointerIsTheUsers;
    }

    // How things were before a play on a client first came to the front:
    // the window you were using, the virtual desktop, the pointer, and
    // whether the client was minimised. Put back when the play ends.
    //
    // Kept from the first turn only. A play that steps aside for the nudge
    // comes back with its own client already in front and the pointer
    // wherever it left it; read again then, the end would put back the
    // client and that pointer instead of what you had.
    class FrontBefore
    {
        public bool Kept;
        public IntPtr Window;
        public Guid Desktop;
        public Point Pointer;
        public bool Minimized;

        // Only the first time: later turns change nothing.
        //
        // aside: what another play will put back, when the window in front is
        // that play's client because it stood aside for this one - or null.
        // That window is not what you were using, and what that play kept
        // is, so this one keeps the same. Otherwise whichever of the two
        // ended last would leave the other's client in front.
        public void Keep(IntPtr window, Guid desktop, Point pointer, bool minimized, FrontBefore aside)
        {
            if (Kept) return;
            if (aside != null && aside.Kept)
            {
                window = aside.Window;
                desktop = aside.Desktop;
                pointer = aside.Pointer;
            }
            Kept = true;
            Window = window;
            Desktop = desktop;
            Pointer = pointer;
            Minimized = minimized;
        }
    }

    // Plays a macro on one client: brings it to the front, sends the plan,
    // and puts the window that was in front and the pointer back.
    //
    // Everything it sends was decided by MacroPlan. What it decides itself is
    // only when to stop: if another window comes to the front part-way - you
    // clicked something - it stops at once rather than typing into that
    // window, and lets go of anything it was holding.
    static class MacroPlayer
    {
        // Keys pressed and not yet let go after the first `done` actions.
        public static List<byte> StillHeld(IList<InputAction> plan, int done)
        {
            List<byte> held = new List<byte>();
            for (int i = 0; i < done && i < plan.Count; i++)
            {
                if (plan[i].Kind == InputActionKind.KeyDown && !held.Contains(plan[i].Vk)) held.Add(plan[i].Vk);
                else if (plan[i].Kind == InputActionKind.KeyUp) held.Remove(plan[i].Vk);
            }
            return held;
        }

        // Ctrl, Shift, Alt and both Windows keys: held while a macro plays,
        // every key it sends arrives as a shortcut instead.
        public static readonly int[] Modifiers = { 0x10, 0x11, 0x12, 0x5B, 0x5C };
        const int POLL_MS = 25;
        const int LET_GO_MS = 2000;

        // True once none of the modifiers is held, false if one still is
        // after the time allowed.
        //
        // A key rule on Ctrl+F1 fires the moment the keys go down, with the
        // finger still on Ctrl - so the macro's E arrived as Ctrl+E.
        public static bool WaitForKeysUp(Func<int, bool> isDown, Action<int> sleep, int timeoutMs)
        {
            int waited = 0;
            while (true)
            {
                bool held = false;
                foreach (int vk in Modifiers) if (isDown(vk)) held = true;
                if (!held) return true;
                if (waited >= timeoutMs) return false;
                sleep(POLL_MS);
                waited += POLL_MS;
            }
        }

        // The real key presses that type a character, from what VkKeyScan
        // says about it on this keyboard - or null to send the character
        // itself. Real keys are what Roblox reads, the same way it reads the
        // nudge; a character needing Ctrl or Alt (AltGr on many layouts) is
        // sent as itself, since Ctrl or Alt would mean something else to the
        // game.
        public static List<KeyValuePair<byte, bool>> KeysFor(int vkScan)
        {
            if (vkScan == -1 || (vkScan & 0xFFFF) == 0xFFFF) return null;
            byte vk = (byte)(vkScan & 0xFF);
            int mods = (vkScan >> 8) & 0xFF;
            if ((mods & ~1) != 0) return null;
            bool shift = (mods & 1) != 0;

            const byte LSHIFT = 0xA0;
            List<KeyValuePair<byte, bool>> keys = new List<KeyValuePair<byte, bool>>();
            if (shift) keys.Add(new KeyValuePair<byte, bool>(LSHIFT, true));
            keys.Add(new KeyValuePair<byte, bool>(vk, true));
            keys.Add(new KeyValuePair<byte, bool>(vk, false));
            if (shift) keys.Add(new KeyValuePair<byte, bool>(LSHIFT, false));
            return keys;
        }

        public const string STOPPED = "stopped - another window came to the front";
        public const string CANCELLED = "stopped";

        static int playing;

        // Something is playing right now. The main window listens, to have
        // the stop key only while there is something for it to stop.
        public static bool Playing { get { return Thread.VolatileRead(ref playing) > 0; } }
        public static event Action PlayingChanged;

        static void NowPlaying(int by)
        {
            Interlocked.Add(ref playing, by);
            Action changed = PlayingChanged;
            if (changed != null) try { changed(); } catch { }
        }

        // Null when it played to the end; otherwise why not, in plain words.
        // Runs on a worker thread - it sleeps for as long as the macro lasts.
        public static string Play(Macro m, IntPtr hwnd)
        {
            return Play(m, hwnd, null);
        }

        // To the end, however many turns that takes: a session steps aside
        // for a nudge and comes back.
        public static string Play(Macro m, IntPtr hwnd, PlayOptions o)
        {
            MacroRunner run = new MacroRunner(m);
            while (true)
            {
                string problem;
                RunEnd end = PlayTurn(run, hwnd, o, out problem);
                if (end != RunEnd.Yielded) return problem;
                LetWaitersIn();
            }
        }

        // After stepping aside: whoever was waiting gets in before this
        // asks again - a lock makes no promise about whose turn it is.
        public static void LetWaitersIn()
        {
            for (int i = 0; i < 150 && FocusGate.Wanted; i++) Thread.Sleep(20);
        }

        // One turn at the front. Yielded means it stepped aside part-way and
        // the same runner carries on with the next call.
        public static RunEnd PlayTurn(MacroRunner run, IntPtr hwnd, PlayOptions o, out string problem)
        {
            problem = null;
            // Back for a turn, or ending without one: no longer standing aside.
            BackFromAside(run.Before);
            if (o == null) o = new PlayOptions();
            if (hwnd == IntPtr.Zero) { problem = "that Roblox window is hidden, so there's nothing to play it in"; return RunEnd.Failed; }
            // The stop key's count from before it waits its turn, so the stop
            // key pressed while it waited stops it - a macro a watcher set off
            // is never marked stopped, and has only this to go by.
            int epoch = MacroStop.Epoch;
            PlayOptions opts = o;
            RunEnd? kept = WaitYourTurn(delegate { return MacroStop.Epoch != epoch || (opts.Stop != null && opts.Stop()); },
                                        out problem);
            if (kept.HasValue) return kept.Value;
            NowPlaying(1);
            // Windows naps in steps of about 15ms unless asked for finer. A
            // click held for 10ms would be held for 15, and an autoclicker
            // set to 50 a second would manage 30. Asked for only while
            // something plays - it costs a little power.
            bool fine = false;
            try { fine = Native.timeBeginPeriod(1) == 0; } catch { }
            try
            {
                if (!o.PointerIsTheUsers &&
                    !WaitForKeysUp(delegate(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; },
                                   delegate(int ms) { Thread.Sleep(ms); }, LET_GO_MS))
                {
                    problem = "Ctrl, Alt or Shift was held down - let go of it and try again";
                    return RunEnd.Failed;
                }

                IntPtr previous = Native.GetForegroundWindow();
                // A client on another virtual desktop takes you there; you
                // are brought back to this one afterwards.
                Guid home = VirtualDesktops.Current();
                // The whole play in real pixels, so the window's position,
                // the clicks and putting the pointer back all agree.
                IntPtr dpiWas = RealPixels();
                // As things were before the first turn: by a later one the
                // client is in front and the pointer is where it left it.
                Native.POINT cursor;
                Native.GetCursorPos(out cursor);
                run.Before.Keep(previous, home, new Point(cursor.X, cursor.Y), Native.IsIconic(hwnd), AsideOn(previous));
                RunEnd end = RunEnd.Failed;
                try
                {
                    end = PlayInFront(run, hwnd, o, dpiWas, epoch, out problem);
                    return end;
                }
                finally
                {
                    Restore(dpiWas);
                    // Not when you took over part-way - switching desktops
                    // yourself is one way to - or it would pull you back. And
                    // not between turns: it is coming straight back.
                    if (end != RunEnd.TakenOver && end != RunEnd.Yielded) VirtualDesktops.ReturnTo(run.Before.Desktop);
                    if (end == RunEnd.Yielded) StandAside(hwnd, run.Before);
                }
            }
            finally
            {
                if (fine) try { Native.timeEndPeriod(1); } catch { }
                NowPlaying(-1);
                FocusGate.Exit();
            }
        }

        // Plays on a client standing aside between turns, by that client's
        // window, with what each will put back when it ends - see
        // FrontBefore.Keep.
        static readonly List<KeyValuePair<IntPtr, FrontBefore>> standingAside = new List<KeyValuePair<IntPtr, FrontBefore>>();

        static void StandAside(IntPtr hwnd, FrontBefore before)
        {
            lock (standingAside) standingAside.Add(new KeyValuePair<IntPtr, FrontBefore>(hwnd, before));
        }

        // Also for a play dropped between turns - stopped while it stood
        // aside - so nothing goes on copying what it kept.
        public static void BackFromAside(FrontBefore before)
        {
            if (before == null) return;
            lock (standingAside)
                standingAside.RemoveAll(delegate(KeyValuePair<IntPtr, FrontBefore> p) { return p.Value == before; });
        }

        static FrontBefore AsideOn(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            lock (standingAside)
                foreach (KeyValuePair<IntPtr, FrontBefore> p in standingAside)
                    if (p.Key == hwnd) return p.Value;
            return null;
        }

        // How long a turn waits for the keyboard before giving up on it.
        public const int GATE_WAIT_MS = 15000;

        // Waits for the keyboard. Null once this has it; otherwise how the
        // turn ends without it - stopped while it waited, said as stopped,
        // or kept out for too long.
        static RunEnd? WaitYourTurn(Func<bool> stopped, out string problem)
        {
            problem = null;
            if (FocusGate.TryEnter(GATE_WAIT_MS, false, stopped))
            {
                if (!stopped()) return null;
                FocusGate.Exit();
            }
            else if (!stopped())
            {
                problem = "something else was using the keyboard for too long";
                return RunEnd.Failed;
            }
            problem = CANCELLED;
            return RunEnd.Cancelled;
        }

        static RunEnd PlayInFront(MacroRunner run, IntPtr hwnd, PlayOptions o, IntPtr dpiWas,
                                  int epoch, out string problem)
        {
            problem = null;
            FrontBefore before = run.Before;

            // Looked at every turn: minimised while it stood aside is
            // brought back now, though only how it was before the first turn
            // is put back at the end.
            if (Native.IsIconic(hwnd)) { Native.ShowWindow(hwnd, Native.SW_RESTORE); Thread.Sleep(180); }
            // Already in front - a key rule on the client you are playing -
            // needs no bringing forward, and the Alt tap that does it would go
            // into the game.
            if (Native.GetForegroundWindow() != hwnd)
            {
                InputSender.FocusWindow(hwnd);
                Thread.Sleep(100);
            }

            RunEnd end;
            if (Native.GetForegroundWindow() != hwnd)
            {
                problem = "Windows wouldn't bring the Roblox window to the front";
                end = RunEnd.Failed;
            }
            else
            {
                LiveWorld world = new LiveWorld(hwnd, o, dpiWas, epoch);
                end = run.Run(world);
                if (end == RunEnd.TakenOver) problem = STOPPED;
                else if (end == RunEnd.Cancelled) problem = CANCELLED;
                else if (end == RunEnd.Failed) problem = run.Problem ?? "it couldn't go on";
            }

            // Stepping aside for a moment leaves everything where it is: it
            // is coming straight back, and whatever takes the keyboard next
            // puts this client back in front when it is done.
            if (end == RunEnd.Yielded) return end;

            if (before.Minimized) Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
            if (!o.PointerIsTheUsers) Native.SetCursorPos(before.Pointer.X, before.Pointer.Y);
            // Not when you took over part-way: you are where you chose to be.
            if (end != RunEnd.TakenOver && before.Window != IntPtr.Zero && before.Window != hwnd)
                InputSender.FocusWindow(before.Window);
            return end;
        }

        // A Lua script plays on a client through the same world a macro's
        // turn does, holding the keyboard itself (see ScriptPlayer). Its
        // thread is already in real pixels; dpiWas is what it was before.
        internal static IMacroWorld WorldOn(IntPtr hwnd, PlayOptions o, IntPtr dpiWas, int epoch)
        {
            return new LiveWorld(hwnd, o ?? new PlayOptions(), dpiWas, epoch);
        }

        // The real client: Windows' own keyboard, mouse, clock and a picture
        // of the window.
        class LiveWorld : IMacroWorld
        {
            readonly IntPtr hwnd;
            readonly PlayOptions o;
            readonly IntPtr dpiWas;
            readonly int epoch;
            readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            readonly Dictionary<string, Pixels> pictures = new Dictionary<string, Pixels>();

            // epoch: the stop key's count as the turn began to wait for the
            // keyboard.
            public LiveWorld(IntPtr hwnd, PlayOptions o, IntPtr dpiWas, int epoch)
            {
                this.hwnd = hwnd;
                this.o = o;
                this.dpiWas = dpiWas;
                this.epoch = epoch;
            }

            public Rectangle Client { get { return ClientOnScreen(hwnd); } }
            public bool InFront() { return Native.GetForegroundWindow() == hwnd; }
            public void Send(InputAction a) { MacroPlayer.Send(a); }
            public long NowMs { get { return clock.ElapsedMilliseconds; } }
            public bool GateWanted { get { return FocusGate.NudgeWanted || FocusGate.MacroGivingUp; } }
            public bool OthersWaiting
            {
                get { return (o.OthersWaiting != null && o.OthersWaiting()) || FocusGate.MacroWanted; }
            }

            public bool StopAsked
            {
                get { return MacroStop.Epoch != epoch || (o.Stop != null && o.Stop()); }
            }

            // In short naps, so the stop key is felt at once rather than at
            // the end of a ten-second wait.
            public void Sleep(int ms)
            {
                long until = clock.ElapsedMilliseconds + ms;
                while (!StopAsked)
                {
                    long left = until - clock.ElapsedMilliseconds;
                    if (left <= 0) return;
                    Thread.Sleep((int)Math.Min(left, 25));
                }
            }

            public PictureLook Look(MacroStep s)
            {
                PictureLook look = new PictureLook();
                Pixels needle = Picture(s.Picture);
                if (needle == null)
                {
                    look.Problem = "the picture " + MacroStep.PictureLabel(s.Picture) + " is missing - edit the step and choose it again";
                    return look;
                }
                bool[] mask = null;
                if (!string.IsNullOrEmpty(s.Mask))
                {
                    Pixels m = Picture(s.Mask);
                    // A painted-out copy that no longer fits is left out
                    // rather than failing: the picture alone still finds it.
                    if (m != null && m.Width == needle.Width && m.Height == needle.Height) mask = WatchEngine.MaskFrom(m);
                }

                // The picture was cut from a capture taken the way watchers
                // take them, so this one is taken the same way - not in the
                // real pixels the rest of the play is in - and where it was
                // found is carried across as a fraction of the window.
                Pixels shot;
                Restore(dpiWas);
                try { shot = WindowCapture.Grab(hwnd); }
                finally { RealPixels(); }
                if (shot == null) return look;      // nothing to see just now: not found, not a fault

                Rectangle box = Rectangle.Empty;
                if (!string.IsNullOrEmpty(s.Box) && o.Regions != null)
                {
                    WatchRegion r = WatchStore.FindIn(o.Regions, o.PlaceId, s.Box);
                    if (r != null) box = r.Project(shot.Width, shot.Height);
                }

                double fx, fy, score;
                look.Found = PictureSearch.Find(shot, needle, mask, s.Tolerance, box, s.OffX, s.OffY,
                                                out fx, out fy, out score);
                look.Score = score;
                if (look.Found)
                {
                    Native.RECT r;
                    if (!Native.GetWindowRect(hwnd, out r)) { look.Found = false; return look; }
                    look.At = PictureSearch.OnScreen(
                        new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), fx, fy);
                }
                return look;
            }

            Pixels Picture(string file)
            {
                if (string.IsNullOrEmpty(file)) return null;
                Pixels p;
                if (pictures.TryGetValue(file, out p)) return p;
                p = WatchEngine.LoadPicture(file);
                pictures[file] = p;
                return p;
            }
        }

        // In real screen pixels, the same units a mouse hook reports and
        // SetCursorPos takes while RealPixels is in force.
        public static Rectangle ClientOnScreen(IntPtr hwnd)
        {
            IntPtr was = RealPixels();
            try
            {
                Native.RECT r;
                Native.GetClientRect(hwnd, out r);
                Native.POINT origin = new Native.POINT();
                Native.ClientToScreen(hwnd, ref origin);
                return new Rectangle(origin.X, origin.Y, r.Right - r.Left, r.Bottom - r.Top);
            }
            finally { Restore(was); }
        }

        // This thread sees real pixels until Restore. The app is not DPI-aware,
        // so on a display scaled above 100% Windows hands it scaled-down
        // window positions, while a mouse hook reports real pixels - and a
        // recorded click would play back in the wrong place. On an unscaled
        // display, and on Windows older than 10 1607, nothing changes.
        internal static IntPtr RealPixels()
        {
            try { return Native.SetThreadDpiAwarenessContext(Native.DPI_PER_MONITOR_AWARE_V2); }
            catch { return IntPtr.Zero; }
        }

        internal static void Restore(IntPtr was)
        {
            if (was == IntPtr.Zero) return;
            try { Native.SetThreadDpiAwarenessContext(was); } catch { }
        }

        // Every monitor together, in real pixels - the units a mouse hook
        // reports and SetCursorPos takes while RealPixels is in force. With a
        // monitor to the left of the main one it starts below zero.
        public static Rectangle ScreenOnScreen()
        {
            IntPtr was = RealPixels();
            try { return SystemInformation.VirtualScreen; }
            finally { Restore(was); }
        }

        // ---------- anywhere on the screen ----------

        // A macro made for the whole screen, played on whatever is there.
        // Null when it played to the end; otherwise why not, in plain words.
        // Runs on a worker thread, for as long as the macro lasts.
        //
        // Nothing is brought to the front and the pointer is not put back: it
        // plays on the screen as it is, the way it was recorded. No window
        // coming to the front stops it either - there is no one window it
        // belongs to - so the stop key and o.Stop are the only ways to end
        // one that goes on until stopped. It still takes its turn with the
        // keyboard like any macro, and a session steps aside for the nudge.
        // So does every other play on the screen, which can last half an hour.
        public static string PlayOnScreen(Macro m, PlayOptions o)
        {
            if (o == null) o = new PlayOptions();
            MacroRunner run = new MacroRunner(m);
            // Up to half an hour, or many times over: it takes turns with the
            // keyboard like a session, so the nudge is never kept out.
            run.StepsAside = true;
            // One world, so one look at the stop key's count, for the whole
            // play: the stop key pressed while it had stepped aside for the
            // nudge still stops it when it comes back.
            ScreenWorld world = new ScreenWorld(o, MacroStop.Epoch, Rectangle.Empty);
            while (true)
            {
                if (world.StopAsked) return CANCELLED;
                string problem;
                RunEnd end = PlayOnScreenTurn(run, world, o, out problem);
                if (end != RunEnd.Yielded) return problem;
                LetWaitersIn();
            }
        }

        // One turn with the keyboard. The same care as a turn on a client:
        // one thing at a time, the finer clock, Ctrl, Alt and Shift let go
        // of first, and the whole turn in real pixels.
        public static RunEnd PlayOnScreenTurn(MacroRunner run, ScreenWorld world, PlayOptions o, out string problem)
        {
            problem = null;
            if (o == null) o = new PlayOptions();
            RunEnd? kept = WaitYourTurn(delegate { return world.StopAsked; }, out problem);
            if (kept.HasValue) return kept.Value;
            NowPlaying(1);
            bool fine = false;
            try { fine = Native.timeBeginPeriod(1) == 0; } catch { }
            try
            {
                if (!o.PointerIsTheUsers &&
                    !WaitForKeysUp(delegate(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; },
                                   delegate(int ms) { Thread.Sleep(ms); }, LET_GO_MS))
                {
                    problem = "Ctrl, Alt or Shift was held down - let go of it and try again";
                    return RunEnd.Failed;
                }

                IntPtr dpiWas = RealPixels();
                try
                {
                    // Read each turn: a monitor plugged in while it waited
                    // its turn moves where the fractions land.
                    world.Begin(dpiWas, SystemInformation.VirtualScreen);
                    RunEnd end = run.Run(world);
                    if (end == RunEnd.Cancelled) problem = CANCELLED;
                    else if (end == RunEnd.TakenOver) problem = STOPPED;
                    else if (end == RunEnd.Failed) problem = run.Problem ?? "it couldn't go on";
                    return end;
                }
                finally { Restore(dpiWas); }
            }
            finally
            {
                if (fine) try { Native.timeEndPeriod(1); } catch { }
                NowPlaying(-1);
                FocusGate.Exit();
            }
        }

        internal static void Send(InputAction a)
        {
            switch (a.Kind)
            {
                case InputActionKind.KeyDown: InputSender.SendScan(a.Vk, true); break;
                case InputActionKind.KeyUp: InputSender.SendScan(a.Vk, false); break;
                case InputActionKind.MoveTo:
                    Native.SetCursorPos(a.X, a.Y);
                    // Roblox takes its pointer from real mouse input, which
                    // setting the position does not produce. A one-pixel
                    // there-and-back does, and lands where it started.
                    InputSender.MoveMouse(1, 0);
                    InputSender.MoveMouse(-1, 0);
                    break;
                case InputActionKind.ButtonDown:
                    Mouse(a.Right ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_LEFTDOWN);
                    break;
                case InputActionKind.ButtonUp:
                    Mouse(a.Right ? Native.MOUSEEVENTF_RIGHTUP : Native.MOUSEEVENTF_LEFTUP);
                    break;
                case InputActionKind.DragTo:
                    // The travel as real mouse movement, which is what turns
                    // the camera - then the exact spot, because Windows'
                    // pointer speed bends how far a movement carries.
                    if (a.Dx != 0 || a.Dy != 0) InputSender.MoveMouse(a.Dx, a.Dy);
                    Native.SetCursorPos(a.X, a.Y);
                    break;
                case InputActionKind.Wheel:
                {
                    Native.INPUT[] wheel = new Native.INPUT[1];
                    wheel[0].type = Native.INPUT_MOUSE;
                    wheel[0].U.mi.mouseData = unchecked((uint)a.Delta);
                    wheel[0].U.mi.dwFlags = Native.MOUSEEVENTF_WHEEL;
                    InputSender.Send(wheel);
                    break;
                }
                case InputActionKind.Char:
                    List<KeyValuePair<byte, bool>> keys = KeysFor(Native.VkKeyScan(a.Char));
                    if (keys == null)
                    {
                        Unicode(a.Char, true);
                        Unicode(a.Char, false);
                    }
                    else foreach (KeyValuePair<byte, bool> k in keys) InputSender.SendScan(k.Key, k.Value);
                    break;
                default:
                    if (a.Ms > 0) Thread.Sleep(a.Ms);
                    break;
            }
        }

        static void Mouse(uint flags)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_MOUSE;
            inp[0].U.mi.dwFlags = flags;
            InputSender.Send(inp);
        }

        static void Unicode(char c, bool down)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_KEYBOARD;
            inp[0].U.ki.wScan = c;
            inp[0].U.ki.dwFlags = Native.KEYEVENTF_UNICODE | (down ? 0u : Native.KEYEVENTF_KEYUP);
            InputSender.Send(inp);
        }
    }

    // The whole screen as a macro's world: every monitor together is the
    // "client", and it is always in front. Built by PlayOnScreen; a test
    // builds one with a screen of its own and never sends through it.
    class ScreenWorld : IMacroWorld
    {
        readonly PlayOptions o;
        readonly int epoch;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        readonly Dictionary<string, Pixels> pictures = new Dictionary<string, Pixels>();
        Rectangle screen;
        IntPtr dpiWas;

        // epoch: the stop key's count as the play began. screen: in real
        // pixels.
        public ScreenWorld(PlayOptions o, int epoch, Rectangle screen)
        {
            this.o = o ?? new PlayOptions();
            this.epoch = epoch;
            this.screen = screen;
        }

        // At the start of each turn, already in real pixels.
        public void Begin(IntPtr dpiWas, Rectangle screen)
        {
            this.dpiWas = dpiWas;
            this.screen = screen;
        }

        public Rectangle Client { get { return screen; } }

        // Never another window's turn: it plays on whatever is in front.
        public bool InFront() { return true; }

        public void Send(InputAction a) { MacroPlayer.Send(a); }
        public long NowMs { get { return clock.ElapsedMilliseconds; } }
        public bool GateWanted { get { return FocusGate.NudgeWanted || FocusGate.MacroGivingUp; } }
        public bool OthersWaiting
        {
            get { return (o.OthersWaiting != null && o.OthersWaiting()) || FocusGate.MacroWanted; }
        }

        public bool StopAsked
        {
            get { return MacroStop.Epoch != epoch || (o.Stop != null && o.Stop()); }
        }

        // In short naps, so the stop key is felt at once rather than at the
        // end of a ten-second wait.
        public void Sleep(int ms)
        {
            long until = clock.ElapsedMilliseconds + ms;
            while (!StopAsked)
            {
                long left = until - clock.ElapsedMilliseconds;
                if (left <= 0) return;
                Thread.Sleep((int)Math.Min(left, 25));
            }
        }

        public PictureLook Look(MacroStep s)
        {
            PictureLook look = new PictureLook();
            Pixels needle = Picture(s.Picture);
            if (needle == null)
            {
                look.Problem = "the picture " + MacroStep.PictureLabel(s.Picture) + " is missing - edit the step and choose it again";
                return look;
            }
            bool[] mask = null;
            if (!string.IsNullOrEmpty(s.Mask))
            {
                Pixels m = Picture(s.Mask);
                if (m != null && m.Width == needle.Width && m.Height == needle.Height) mask = WatchEngine.MaskFrom(m);
            }

            // Pictures are cut from captures taken the way watchers take
            // them, outside real pixels, so the screen is taken the same way
            // and where it was found is carried across as a fraction of it -
            // as a look on a client does.
            Pixels shot = null;
            MacroPlayer.Restore(dpiWas);
            try { shot = GrabScreen(); }
            finally { MacroPlayer.RealPixels(); }
            if (shot == null) return look;      // nothing to see just now: not found, not a fault

            double fx, fy, score;
            look.Found = PictureSearch.Find(shot, needle, mask, s.Tolerance, Rectangle.Empty, s.OffX, s.OffY,
                                            out fx, out fy, out score);
            look.Score = score;
            if (look.Found) look.At = PictureSearch.OnScreen(screen, fx, fy);
            return look;
        }

        // Every monitor together; a script's read() uses it too.
        internal static Pixels GrabScreen()
        {
            try
            {
                Rectangle all = SystemInformation.VirtualScreen;
                if (all.Width <= 0 || all.Height <= 0) return null;
                using (Bitmap b = new Bitmap(all.Width, all.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(b))
                        g.CopyFromScreen(all.Left, all.Top, 0, 0, all.Size, CopyPixelOperation.SourceCopy);
                    return Pixels.FromBitmap(b);
                }
            }
            catch { return null; }      // the lock screen, or a UAC prompt, is in front
        }

        Pixels Picture(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            Pixels p;
            if (pictures.TryGetValue(file, out p)) return p;
            p = WatchEngine.LoadPicture(file);
            pictures[file] = p;
            return p;
        }
    }

    // A macro waiting to be played, and where.
    class MacroJob
    {
        public Macro Macro;
        public int Pid;
        public string Label;       // "Client 2 - VladDerKing", for the activity list
        public int Times = 1;      // played this many times in a row, while it holds the front - stepping aside for the nudge

        // A session - a macro that goes on until stopped - is played a turn
        // at a time and goes back in the queue between. This is its place.
        public MacroRunner Run;
        public volatile bool Cancelled;     // switched off: dropped at its next turn, or mid-turn
        public string Tag;                  // who started it: "autoclick", or "rule:" and the rule's name
        public int TurnMs = MacroRunner.DEFAULT_TURN_MS;
        public bool UserIsPlaying;          // on the client you are using: the pointer is yours
        public bool StepsAside;             // takes turns like a session though it ends: see MacroRunner.StepsAside
    }

    // Macros set off by watchers and rules, played one at a time.
    //
    // What stops it backing up is that the same macro for the same client is
    // never waiting twice: a busy chat firing again and again adds nothing
    // while one is already queued. A rule on every client still reaches every
    // client. The cap is only a safety net, and the caller says so if it is
    // ever hit.
    class MacroQueue
    {
        public const int MAX_WAITING = 30;

        readonly List<MacroJob> waiting = new List<MacroJob>();
        readonly object gate = new object();

        public bool Offer(MacroJob j)
        {
            lock (gate)
            {
                if (waiting.Count >= MAX_WAITING) return false;
                foreach (MacroJob w in waiting)
                    if (w.Pid == j.Pid && string.Equals(w.Macro.Name, j.Macro.Name, StringComparison.OrdinalIgnoreCase))
                        return false;
                waiting.Add(j);
                return true;
            }
        }

        public MacroJob Take()
        {
            lock (gate)
            {
                if (waiting.Count == 0) return null;
                MacroJob j = waiting[0];
                waiting.RemoveAt(0);
                return j;
            }
        }

        public int Waiting { get { lock (gate) return waiting.Count; } }

        // A session back for its next turn, behind whatever came in since.
        // It was let in once already, so nothing is checked again.
        public void Requeue(MacroJob j)
        {
            lock (gate) waiting.Add(j);
        }

        // The stop key: nothing that was waiting plays.
        public void Clear()
        {
            lock (gate) waiting.Clear();
        }
    }

    // Records what is pressed and clicked on one client until F8, or a minute.
    //
    // Only while that client is in front: a key pressed in any other window is
    // not seen at all, so recording can never pick up something typed
    // elsewhere. Input this app sends itself is ignored, so a nudge that
    // happens mid-recording is not recorded.
    //
    // Or, made by ForScreen, what is pressed and clicked anywhere on the
    // screen, until F8 or ten minutes - except on this app's own windows, so
    // the clicks that start and stop the recording are never part of it.
    class MacroRecorder : IDisposable
    {
        readonly IntPtr target;
        readonly bool screen;           // the whole screen, not one client
        readonly Rectangle client;
        readonly bool moves;
        readonly Action<List<MacroStep>> done;
        readonly List<RawInput> events = new List<RawInput>();
        readonly Native.HookProc keyProc, mouseProc;     // kept alive: the hooks call these
        readonly System.Windows.Forms.Timer limit;
        readonly DateTime started = DateTime.Now;
        IntPtr keyHook, mouseHook;
        bool finished;
        bool counted;                   // in `recording`, until it ends

        // A mouse reports a thousand moves a second; a minute of them is
        // more than is worth holding on to.
        const int MAX_EVENTS = 100000;

        // A move this soon after the last one kept is let go as it comes in.
        // A fast mouse reports a thousand a second, which would fill
        // MAX_EVENTS in under two minutes of a ten-minute recording of the
        // screen, and every move after that would be lost. A hundred a
        // second is still ten times what ToSteps keeps.
        public const int MOVE_THIN_MS = 10;
        long lastMoveMs = -1;           // when the last move kept came in; -1 before the first

        // Whether a move that came in at nowMs is kept, the last kept one
        // having come in at lastMs (-1 for none yet).
        public static bool KeepsMove(long nowMs, long lastMs)
        {
            return lastMs < 0 || nowMs - lastMs >= MOVE_THIN_MS;
        }

        // A recording of the whole screen can be a whole task, not one move
        // in a game - but not one left running by mistake all afternoon.
        public const int MAX_SCREEN_MS = 10 * 60 * 1000;

        static readonly int ownPid = System.Diagnostics.Process.GetCurrentProcess().Id;
        static int recording;

        // Some recording is going on right now. The keys that play macros
        // and switch things on do nothing meanwhile: what is pressed is being
        // recorded, not meant.
        public static bool Recording { get { return Thread.VolatileRead(ref recording) > 0; } }

        public MacroRecorder(IntPtr target, Action<List<MacroStep>> done) : this(target, false, done) { }

        // moves: keep where the pointer went between clicks as well. Drags
        // and the wheel are always kept.
        public MacroRecorder(IntPtr target, bool moves, Action<List<MacroStep>> done)
            : this(target, false, moves, done) { }

        MacroRecorder(IntPtr target, bool screen, bool moves, Action<List<MacroStep>> done)
        {
            this.target = target;
            this.screen = screen;
            this.moves = moves;
            this.done = done;
            // The steps are fractions of this: one client, or every monitor.
            client = screen ? MacroPlayer.ScreenOnScreen() : MacroPlayer.ClientOnScreen(target);
            keyProc = OnKey;
            mouseProc = OnMouse;

            limit = new System.Windows.Forms.Timer();
            limit.Interval = screen ? MAX_SCREEN_MS : Macro.MAX_MS;
            limit.Tick += delegate { Stop(); };
        }

        // Anything on the screen, for a macro that plays anywhere. Start it
        // as any other; `done` has the steps as fractions of every monitor
        // together.
        public static MacroRecorder ForScreen(bool moves, Action<List<MacroStep>> done)
        {
            return new MacroRecorder(IntPtr.Zero, true, moves, done);
        }

        // Whether an event is kept, by where it happened. On one client:
        // only while that client is in front. On the whole screen: anywhere
        // but this app's own windows - neither the one in front, nor the one
        // under the pointer, since the click that brings this app's window to
        // the front arrives while the window before it is still in front.
        // frontPid, pointPid: whose those windows are, 0 when unknown.
        public static bool Keeps(bool screen, IntPtr target, IntPtr front, int frontPid, int pointPid, int ownPid)
        {
            if (!screen) return front == target;
            return frontPid != ownPid && pointPid != ownPid;
        }

        // False if Windows refused the hooks.
        public bool Start()
        {
            IntPtr module = Native.GetModuleHandle(null);
            keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, module, 0);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, module, 0);
            if (keyHook == IntPtr.Zero || mouseHook == IntPtr.Zero) { Unhook(); return false; }
            limit.Start();
            if (!counted) { counted = true; Interlocked.Increment(ref recording); }
            return true;
        }

        void Uncount()
        {
            if (!counted) return;
            counted = false;
            Interlocked.Decrement(ref recording);
        }

        static int PidOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return 0;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            return (int)pid;
        }

        // At a point the hook reported, which is in real pixels.
        static int PidAt(int x, int y)
        {
            IntPtr was = MacroPlayer.RealPixels();
            try
            {
                Xn.POINT p = new Xn.POINT();
                p.X = x; p.Y = y;
                return PidOf(Xn.WindowFromPoint(p));
            }
            catch { return 0; }
            finally { MacroPlayer.Restore(was); }
        }

        bool KeepsKey()
        {
            IntPtr front = Native.GetForegroundWindow();
            return Keeps(screen, target, front, screen ? PidOf(front) : 0, 0, ownPid);
        }

        bool KeepsAt(int x, int y)
        {
            IntPtr front = Native.GetForegroundWindow();
            if (!screen) return Keeps(false, target, front, 0, 0, ownPid);
            return Keeps(true, target, front, PidOf(front), PidAt(x, y), ownPid);
        }

        long Now() { return (long)(DateTime.Now - started).TotalMilliseconds; }

        IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !finished)
            {
                Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                int msg = wParam.ToInt32();
                bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
                // Only what this app sent itself is left out; a key sent by
                // mouse software is somebody pressing it.
                if (!InputSender.IsOurs(k.dwExtraInfo) && (down || up))
                {
                    if (down && k.vkCode == MacroRecording.STOP_KEY)
                    {
                        // Finished after this callback returns: unhooking from
                        // inside the hook's own call is asking for trouble.
                        System.Windows.Forms.Timer later = new System.Windows.Forms.Timer();
                        later.Interval = 1;
                        later.Tick += delegate { later.Stop(); later.Dispose(); Stop(); };
                        later.Start();
                    }
                    else if (KeepsKey())
                        events.Add(RawInput.Key(down, (byte)k.vkCode, Now()));
                }
            }
            return Native.CallNextHookEx(keyHook, code, wParam, lParam);
        }

        IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !finished)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_LBUTTONUP ||
                    msg == Native.WM_RBUTTONDOWN || msg == Native.WM_RBUTTONUP)
                {
                    Native.MSLLHOOKSTRUCT m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    if (!InputSender.IsOurs(m.dwExtraInfo) && KeepsAt(m.pt.X, m.pt.Y))
                    {
                        bool down = msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN;
                        bool right = msg == Native.WM_RBUTTONDOWN || msg == Native.WM_RBUTTONUP;
                        events.Add(RawInput.Button(down, right, m.pt.X, m.pt.Y, Now()));
                    }
                }
                else if (msg == Native.WM_MOUSEWHEEL || (moves && msg == Native.WM_MOUSEMOVE))
                {
                    Native.MSLLHOOKSTRUCT m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    // A move is left out when any program made it, this app's
                    // unmarked SetCursorPos among them; the wheel only when
                    // this app turned it. Where it happened is looked up for
                    // the wheel alone - a thousand moves a second are only
                    // asked which window is in front.
                    bool kept = msg == Native.WM_MOUSEWHEEL
                        ? !InputSender.IsOurs(m.dwExtraInfo) && KeepsAt(m.pt.X, m.pt.Y)
                        : (m.flags & Native.LLMHF_INJECTED) == 0 && KeepsKey();
                    if (kept)
                    {
                        // The wheel's turn is the high half of mouseData, signed.
                        if (msg == Native.WM_MOUSEWHEEL)
                            events.Add(RawInput.Scroll((short)(m.mouseData >> 16), m.pt.X, m.pt.Y, Now()));
                        else if (events.Count < MAX_EVENTS)
                        {
                            long now = Now();
                            if (KeepsMove(now, lastMoveMs))
                            {
                                events.Add(RawInput.Move(m.pt.X, m.pt.Y, now));
                                lastMoveMs = now;
                            }
                        }
                    }
                }
            }
            return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        public void Stop()
        {
            if (finished) return;
            finished = true;
            limit.Stop();
            Unhook();
            Uncount();
            if (done != null) done(MacroRecording.ToSteps(events, client, Now()));
        }

        void Unhook()
        {
            if (keyHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        }

        public void Dispose()
        {
            finished = true;
            Unhook();
            Uncount();
            limit.Dispose();
        }
    }
}
