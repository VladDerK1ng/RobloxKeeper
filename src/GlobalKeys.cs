using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // A key this app listens for went down, or came back up.
    class HeardKey
    {
        public int Key;         // as RuleHotkeys.Key makes it: the key, and Ctrl, Alt and Shift above it
        public bool Down;       // false: let go

        public HeardKey(int key, bool down) { Key = key; Down = down; }
    }

    // Which presses count, worked out from the keyboard's events alone - kept
    // apart from the hook so it can be tested without one.
    //
    // A key held down sends its down again and again until it is let go;
    // only the first is a press. Ctrl, Alt and Shift are read from the same
    // events, so Ctrl+F6 is heard as Ctrl+F6 and never as F6. Keys this app
    // sends itself - its macros and autoclicker - are not anybody pressing
    // anything, and are left out entirely. Keys other programs send are kept:
    // mouse software that turns a side button into F6 is somebody pressing F6.
    class KeyPressTracker
    {
        // A key that is held repeats its down every half second at the very
        // slowest, after waiting at most a second to start. A down for a key
        // thought held that comes later than this is a new press: its up was
        // lost - which happens while a window run as administrator, or the
        // Ctrl+Alt+Del screen, is in front.
        public const int REPEAT_GAP_MS = 1500;

        const int NONE = -1;
        public const int KEYS = 0x800;      // every vk with every mix of Ctrl, Alt and Shift

        readonly bool[] down = new bool[256];
        readonly uint[] lastDown = new uint[256];
        // The key reported when each vk went down, so its up is reported as
        // that same key even if Ctrl was let go first.
        readonly int[] firedAs = new int[256];
        volatile bool[] watched = new bool[KEYS];

        // Whether a key is really down right now, by Windows' own record.
        // Asked only about Ctrl, Alt, Shift and the Windows key, to let go of
        // one whose up was lost; without it, a lost Alt up would leave every
        // plain key unheard. Null in tests.
        public Predicate<byte> StillDown;

        public KeyPressTracker()
        {
            for (int i = 0; i < firedAs.Length; i++) firedAs[i] = NONE;
        }

        // Exactly these, from now on. Made fresh and handed over whole, so the
        // hook's thread never sees one half written.
        public void Watch(ICollection<int> keys)
        {
            watched = Table(keys);
        }

        public static bool[] Table(ICollection<int> keys)
        {
            bool[] w = new bool[KEYS];
            if (keys != null)
                foreach (int k in keys) if (k > 0 && k < KEYS) w[k] = true;
            return w;
        }

        public bool Watching(int key)
        {
            bool[] w = watched;
            return key > 0 && key < w.Length && w[key];
        }

        // A key found already down as listening starts - the key just chosen
        // in the "press a key" box, say, which then repeats. Its repeats are
        // not a press.
        public void AlreadyDown(byte vk, uint time)
        {
            if (vk == 0) return;
            down[vk] = true;
            lastDown[vk] = time;
        }

        // One event from the keyboard. Null unless it was a key being
        // listened for going down, or coming back up after its down was
        // reported. ours: this app sent it (see SentByUs). time: the event's
        // own clock, in milliseconds.
        public HeardKey Feed(byte vk, bool isDown, bool ours, uint time)
        {
            if (ours || vk == 0) return null;

            if (!isDown)
            {
                down[vk] = false;
                int was = firedAs[vk];
                firedAs[vk] = NONE;
                // Reported even while a key is being chosen, so whoever heard
                // the down always hears its up too.
                return was == NONE ? null : new HeardKey(was, false);
            }

            bool repeat = down[vk] && unchecked(time - lastDown[vk]) < REPEAT_GAP_MS;
            down[vk] = true;
            lastDown[vk] = time;
            if (repeat) return null;

            // A new press. Held keys are kept count of even when nothing is
            // listening for them, or while a key is being chosen: the key just
            // chosen is listened for a moment later, and must not then go off
            // with the repeats of the very press that chose it.
            firedAs[vk] = NONE;
            if (!IsModifier(vk)) LetGoOfLostModifiers();
            if (GlobalKeys.Capturing) return null;
            if (Held(VK_LWIN) || Held(VK_RWIN)) return null;       // Windows' own shortcuts

            int key = KeyFor(vk);
            if (!Watching(key)) return null;
            firedAs[vk] = key;
            return new HeardKey(key, true);
        }

        // Whether a key event came from this app. Windows marks every key a
        // program sends as injected, this app's and mouse software's alike,
        // so the flag can't tell them apart - the mark this app puts on its
        // own can.
        public static bool SentByUs(uint flags, IntPtr extraInfo)
        {
            return InputSender.IsOurs(extraInfo);
        }

        const byte VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
        const byte VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
        const byte VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        static readonly byte[] MODIFIERS = { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LSHIFT, VK_RSHIFT,
                                             VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN };

        static bool IsShift(byte vk) { return vk == VK_SHIFT || vk == VK_LSHIFT || vk == VK_RSHIFT; }
        static bool IsCtrl(byte vk) { return vk == VK_CONTROL || vk == VK_LCONTROL || vk == VK_RCONTROL; }
        static bool IsAlt(byte vk) { return vk == VK_MENU || vk == VK_LMENU || vk == VK_RMENU; }

        static bool IsModifier(byte vk)
        {
            return IsShift(vk) || IsCtrl(vk) || IsAlt(vk) || vk == VK_LWIN || vk == VK_RWIN;
        }

        bool Held(byte vk) { return down[vk]; }

        // The key as a rule names it. Ctrl, Alt or Shift pressed by itself is
        // the plain Shift (or Ctrl, or Alt) key - the key box only ever names
        // that one, not which side of the keyboard it was on.
        int KeyFor(byte vk)
        {
            bool shift = Held(VK_SHIFT) || Held(VK_LSHIFT) || Held(VK_RSHIFT);
            bool ctrl = Held(VK_CONTROL) || Held(VK_LCONTROL) || Held(VK_RCONTROL);
            bool alt = Held(VK_MENU) || Held(VK_LMENU) || Held(VK_RMENU);
            if (IsShift(vk)) { vk = VK_SHIFT; shift = false; }
            else if (IsCtrl(vk)) { vk = VK_CONTROL; ctrl = false; }
            else if (IsAlt(vk)) { vk = VK_MENU; alt = false; }
            return RuleHotkeys.Key(vk, ctrl, alt, shift);
        }

        // Asked while another key is going down, so Windows' record of these
        // is up to date - it is only the key being handled right now that it
        // has not caught up with yet.
        void LetGoOfLostModifiers()
        {
            Predicate<byte> still = StillDown;
            if (still == null) return;
            foreach (byte m in MODIFIERS)
            {
                if (!down[m]) continue;
                bool really;
                try { really = still(m); }
                catch { continue; }
                if (!really) { down[m] = false; firedAs[m] = NONE; }
            }
        }
    }

    // The keys this app answers to everywhere - the autoclicker's, the
    // screenshot key and the rules' - heard without being taken.
    //
    // Windows' own shortcut keys (RegisterHotKey) keep the key: with the
    // autoclicker on F6, F6 never reached Roblox or any other program again,
    // and not even this app's own "press a key" box could see it. This listens
    // to the keyboard instead and lets every key go on to where it was going.
    //
    // The stop key stays one of Windows' shortcut keys: it is held only while
    // something is playing, it is there to stop things, and Windows drops a
    // hook it thinks is too slow without saying so - stopping must not hang
    // on one.
    //
    // It runs only while there is a key to listen for, on a thread of its
    // own: a hook that is slow to answer stutters the whole system's keyboard,
    // and this app's window does work on its thread that a hook must never
    // wait behind. What it hears is handed to the window's thread.
    static class GlobalKeys
    {
        static readonly object gate = new object();
        static Listener current;
        static bool[] wanted = new bool[KeyPressTracker.KEYS];
        static Control ui;
        static Action<HeardKey> heard;
        static int capturing;

        public static bool Active
        {
            get { Listener l = current; return l != null && l.Hooked; }
        }

        // Exactly these keys, each heard going down and up, handed to `heard`
        // on `to`'s thread. None: the hook is taken away.
        public static void Listen(ICollection<int> keys, Control to, Action<HeardKey> onHeard)
        {
            lock (gate)
            {
                Remember(keys);
                ui = to;
                heard = onHeard;
                if (keys == null || keys.Count == 0) { StopLocked(); return; }
                // One that Windows refused is given up on, so this can ask again.
                if (current != null && !current.Hooked) StopLocked();
                if (current != null) { current.Tracker.Watch(keys); return; }
                current = new Listener(keys);
                current.Start();
            }
        }

        public static void Stop()
        {
            lock (gate)
            {
                wanted = new bool[KeyPressTracker.KEYS];
                StopLocked();
            }
        }

        static void StopLocked()
        {
            Listener l = current;
            current = null;
            if (l != null) l.Stop();
        }

        // What ListensFor answers from. Apart from Listen so a test can set it
        // without a hook.
        internal static void Remember(ICollection<int> keys)
        {
            wanted = KeyPressTracker.Table(keys);
        }

        // Whether this key (and Ctrl, Alt, Shift: see RuleHotkeys.Key) is one
        // the app answers to - whether or not the hook could be had.
        public static bool ListensFor(int key)
        {
            bool[] w = wanted;
            return key > 0 && key < w.Length && w[key];
        }

        // While a key is being chosen, pressing it must only choose it - not
        // also switch the autoclicker on, if it is the autoclicker's key.
        public static void CaptureOpened() { Interlocked.Increment(ref capturing); }
        public static void CaptureClosed() { Interlocked.Decrement(ref capturing); }
        public static bool Capturing { get { return Thread.VolatileRead(ref capturing) > 0; } }

        // Windows takes a hook away without a word when it is slow to answer
        // even once - a game using every core can starve its thread - and
        // every key would then be dead until a restart. So the hook is set
        // again this often, which puts back one that was taken away.
        public const int REHOOK_MS = 2 * 60 * 1000;

        // A fresh hook set, then the old one taken off - so there is never a
        // moment with no hook. Zero when Windows refused the fresh one: the
        // old one, which may already be gone, is left as it is.
        internal static IntPtr Rehook(IntPtr old, Func<IntPtr> hook, Action<IntPtr> unhook)
        {
            IntPtr fresh = IntPtr.Zero;
            try { fresh = hook(); } catch { }
            if (fresh == IntPtr.Zero) return IntPtr.Zero;
            if (old != IntPtr.Zero) try { unhook(old); } catch { }
            return fresh;
        }

        static void Hand(HeardKey h)
        {
            Control to = ui;
            Action<HeardKey> a = heard;
            if (to == null || a == null) return;
            // Posted, not waited for: the hook answers at once whatever the
            // window is doing.
            try { if (to.IsHandleCreated && !to.IsDisposed) to.BeginInvoke(a, h); }
            catch { }       // closing
        }

        // One spell of listening. Everything it hooks is its own, so one that
        // is still winding down can never unhook the one started after it.
        class Listener
        {
            public readonly KeyPressTracker Tracker = new KeyPressTracker();
            readonly ManualResetEvent ready = new ManualResetEvent(false);
            readonly ApplicationContext context = new ApplicationContext();
            Native.HookProc keyProc;        // kept alive: the hook calls it
            IntPtr keyHook;
            volatile bool hooked;

            public Listener(ICollection<int> keys)
            {
                Tracker.Watch(keys);
                Tracker.StillDown = delegate(byte vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; };
                // The hook only hears what happens from now on. A key held as
                // it starts - the one just chosen, still down - would
                // otherwise go off with its first repeat. The events' clock is
                // the same one as Environment.TickCount.
                uint now = unchecked((uint)Environment.TickCount);
                for (int vk = 1; vk < 256; vk++)
                    if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) Tracker.AlreadyDown((byte)vk, now);
            }

            public bool Hooked { get { return hooked; } }

            public void Start()
            {
                Thread t = new Thread(Listen);
                t.IsBackground = true;
                t.Name = "Global keys";
                // It does next to nothing per key, and a hook that answers
                // late is one Windows takes away: ahead of a busy game.
                t.Priority = ThreadPriority.Highest;
                t.Start();
                ready.WaitOne(2000);
            }

            public void Stop()
            {
                hooked = false;
                try { context.ExitThread(); } catch { }
            }

            void Listen()
            {
                try
                {
                    keyProc = OnKey;
                    keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, Native.GetModuleHandle(null), 0);
                    hooked = keyHook != IntPtr.Zero;
                }
                catch { hooked = false; }

                System.Windows.Forms.Timer again = null;
                try
                {
                    if (hooked)
                    {
                        // On this thread, so the hook is set again by the
                        // thread it calls.
                        again = new System.Windows.Forms.Timer();
                        again.Interval = REHOOK_MS;
                        again.Tick += delegate { HookAgain(); };
                        again.Start();

                        // Start comes back only once the loop is turning, so
                        // a Stop straight after it has a loop to end.
                        EventHandler turning = null;
                        turning = delegate { Application.Idle -= turning; ready.Set(); };
                        Application.Idle += turning;
                        Application.Run(context);
                    }
                }
                finally
                {
                    if (again != null) again.Dispose();
                    ready.Set();
                    hooked = false;
                    if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
                }
            }

            // Refused: said as not hooked, so the main window's every-ten-
            // seconds retry starts listening over again.
            void HookAgain()
            {
                if (!hooked) return;
                IntPtr fresh = Rehook(keyHook,
                    delegate { return Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, Native.GetModuleHandle(null), 0); },
                    delegate(IntPtr h) { Native.UnhookWindowsHookEx(h); });
                if (fresh != IntPtr.Zero) keyHook = fresh;
                else hooked = false;
            }

            // Every key is passed on, always: this only listens.
            IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
            {
                if (code >= 0)
                {
                    try
                    {
                        int msg = wParam.ToInt32();
                        // With Alt held, keys come as "system" keys.
                        bool isDown = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                        bool isUp = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
                        if (isDown || isUp)
                        {
                            // KBDLLHOOKSTRUCT: the key, its scan code, the
                            // flags and the time, each 32 bits, then the
                            // sender's extra info, pointer-sized - at 16 on
                            // 32 and 64 bits alike.
                            int vk = Marshal.ReadInt32(lParam, 0);
                            uint flags = unchecked((uint)Marshal.ReadInt32(lParam, 8));
                            uint time = unchecked((uint)Marshal.ReadInt32(lParam, 12));
                            IntPtr extra = Marshal.ReadIntPtr(lParam, 16);
                            if (vk > 0 && vk < 256)
                            {
                                HeardKey h = Tracker.Feed((byte)vk, isDown, KeyPressTracker.SentByUs(flags, extra), time);
                                if (h != null) Hand(h);
                            }
                        }
                    }
                    catch { }
                }
                return Native.CallNextHookEx(keyHook, code, wParam, lParam);
            }
        }
    }
}
