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

        public static bool TryEnter(int waitMs) { return Monitor.TryEnter(gate, waitMs); }
        public static void Exit() { Monitor.Exit(gate); }
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

        // Null when it played to the end; otherwise why not, in plain words.
        // Runs on a worker thread - it sleeps for as long as the macro lasts.
        public static string Play(Macro m, IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "the client has no window to play it in";
            if (!FocusGate.TryEnter(15000)) return "something else was using the keyboard for too long";
            try
            {
                IntPtr previous = Native.GetForegroundWindow();
                Native.POINT cursor;
                Native.GetCursorPos(out cursor);

                bool wasMinimized = Native.IsIconic(hwnd);
                if (wasMinimized) { Native.ShowWindow(hwnd, Native.SW_RESTORE); Thread.Sleep(180); }
                InputSender.FocusWindow(hwnd);
                Thread.Sleep(100);

                string problem = null;
                if (Native.GetForegroundWindow() != hwnd) problem = "Windows wouldn't bring the client to the front";
                else
                {
                    List<InputAction> plan = MacroPlan.For(m, ClientOnScreen(hwnd));
                    int done = 0;
                    for (; done < plan.Count; done++)
                    {
                        if (plan[done].Kind != InputActionKind.Sleep && Native.GetForegroundWindow() != hwnd)
                        {
                            problem = "stopped - another window came to the front";
                            break;
                        }
                        Send(plan[done]);
                    }
                    foreach (byte vk in StillHeld(plan, done)) InputSender.SendScan(vk, false);
                }

                if (wasMinimized) Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
                Native.SetCursorPos(cursor.X, cursor.Y);
                if (previous != IntPtr.Zero && previous != hwnd) InputSender.FocusWindow(previous);
                return problem;
            }
            finally { FocusGate.Exit(); }
        }

        public static Rectangle ClientOnScreen(IntPtr hwnd)
        {
            Native.RECT r;
            Native.GetClientRect(hwnd, out r);
            Native.POINT origin = new Native.POINT();
            Native.ClientToScreen(hwnd, ref origin);
            return new Rectangle(origin.X, origin.Y, r.Right - r.Left, r.Bottom - r.Top);
        }

        static void Send(InputAction a)
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
                case InputActionKind.Char:
                    Unicode(a.Char, true);
                    Unicode(a.Char, false);
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
            Native.SendInput(1, inp, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        static void Unicode(char c, bool down)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_KEYBOARD;
            inp[0].U.ki.wScan = c;
            inp[0].U.ki.dwFlags = Native.KEYEVENTF_UNICODE | (down ? 0u : Native.KEYEVENTF_KEYUP);
            Native.SendInput(1, inp, Marshal.SizeOf(typeof(Native.INPUT)));
        }
    }

    // A macro waiting to be played, and where.
    class MacroJob
    {
        public Macro Macro;
        public int Pid;
        public string Label;       // "Client 2 - VladDerKing", for the activity list
        public int Times = 1;      // played this many times in a row, while it holds the front
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
    }

    // Records what is pressed and clicked on one client until F8, or a minute.
    //
    // Only while that client is in front: a key pressed in any other window is
    // not seen at all, so recording can never pick up something typed
    // elsewhere. Input this app sends itself is ignored, so a nudge that
    // happens mid-recording is not recorded.
    class MacroRecorder : IDisposable
    {
        readonly IntPtr target;
        readonly Rectangle client;
        readonly Action<List<MacroStep>> done;
        readonly List<RawInput> events = new List<RawInput>();
        readonly Native.HookProc keyProc, mouseProc;     // kept alive: the hooks call these
        readonly System.Windows.Forms.Timer limit;
        readonly DateTime started = DateTime.Now;
        IntPtr keyHook, mouseHook;
        bool finished;

        public MacroRecorder(IntPtr target, Action<List<MacroStep>> done)
        {
            this.target = target;
            this.done = done;
            client = MacroPlayer.ClientOnScreen(target);
            keyProc = OnKey;
            mouseProc = OnMouse;

            limit = new System.Windows.Forms.Timer();
            limit.Interval = Macro.MAX_MS;
            limit.Tick += delegate { Stop(); };
        }

        // False if Windows refused the hooks.
        public bool Start()
        {
            IntPtr module = Native.GetModuleHandle(null);
            keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, module, 0);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, module, 0);
            if (keyHook == IntPtr.Zero || mouseHook == IntPtr.Zero) { Unhook(); return false; }
            limit.Start();
            return true;
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
                if ((k.flags & Native.LLKHF_INJECTED) == 0 && (down || up))
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
                    else if (Native.GetForegroundWindow() == target)
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
                    if ((m.flags & Native.LLMHF_INJECTED) == 0 && Native.GetForegroundWindow() == target)
                    {
                        bool down = msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN;
                        bool right = msg == Native.WM_RBUTTONDOWN || msg == Native.WM_RBUTTONUP;
                        events.Add(RawInput.Button(down, right, m.pt.X, m.pt.Y, Now()));
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
            limit.Dispose();
        }
    }
}
