using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // How long since you really touched the keyboard or mouse - while this
    // app is itself pressing keys.
    //
    // Windows' own idle clock counts every key and click, including the ones
    // a macro sends. With an autoclicker or a session going it would never
    // read more than a moment, so "only nudge while I'm away" would never see
    // you leave and rules that wait for you to be away would never play. This
    // listens for input and leaves out what was sent by a program.
    //
    // It runs only while a session does, on a thread of its own: a hook that
    // is slow to answer stutters the whole system's mouse, and this app's
    // window does work on its thread that a hook must never wait behind.
    static class RealInput
    {
        static readonly object gate = new object();
        static Listener current;
        static int lastTick;

        public static bool Active
        {
            get { Listener l = current; return l != null && l.Hooked; }
        }

        public static TimeSpan IdleFor()
        {
            return IdleFor(Thread.VolatileRead(ref lastTick), Environment.TickCount);
        }

        // Tick counts wrap every 49 days; subtracting as unsigned keeps the
        // answer right across the wrap.
        public static TimeSpan IdleFor(int lastTick, int nowTick)
        {
            return TimeSpan.FromMilliseconds(unchecked((uint)nowTick - (uint)lastTick));
        }

        // startIdle: how long you had already been away as it starts, by
        // Windows' clock, which is still right at that moment.
        public static void Start(TimeSpan startIdle)
        {
            lock (gate)
            {
                if (current != null) return;
                double ms = Math.Min(startIdle.TotalMilliseconds, int.MaxValue / 2);
                Thread.VolatileWrite(ref lastTick, unchecked(Environment.TickCount - (int)ms));
                current = new Listener();
                current.Start();
            }
        }

        public static void Stop()
        {
            lock (gate)
            {
                Listener l = current;
                current = null;
                if (l != null) l.Stop();
            }
        }

        static void Touched()
        {
            Thread.VolatileWrite(ref lastTick, Environment.TickCount);
        }

        // One spell of listening. Everything it hooks is its own, so one that
        // is still winding down can never unhook the one started after it.
        class Listener
        {
            readonly ManualResetEvent ready = new ManualResetEvent(false);
            readonly ApplicationContext context = new ApplicationContext();
            Native.HookProc keyProc, mouseProc;      // kept alive: the hooks call these
            IntPtr keyHook, mouseHook;
            volatile bool hooked;

            public bool Hooked { get { return hooked; } }

            public void Start()
            {
                Thread t = new Thread(Listen);
                t.IsBackground = true;
                t.Name = "Real input";
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
                    mouseProc = OnMouse;
                    IntPtr module = Native.GetModuleHandle(null);
                    keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, module, 0);
                    mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, module, 0);
                    // Refused: Hooked stays false and Windows' own clock is
                    // used, which is what there was before.
                    hooked = keyHook != IntPtr.Zero && mouseHook != IntPtr.Zero;
                }
                catch { hooked = false; }

                try
                {
                    if (hooked)
                    {
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
                    ready.Set();
                    hooked = false;
                    if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
                    if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
                }
            }

            IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
            {
                if (code >= 0)
                {
                    // The flags are the third field, after two 32-bit numbers.
                    uint flags = unchecked((uint)Marshal.ReadInt32(lParam, 8));
                    if ((flags & Native.LLKHF_INJECTED) == 0) Touched();
                }
                return Native.CallNextHookEx(keyHook, code, wParam, lParam);
            }

            IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
            {
                if (code >= 0)
                {
                    // After the point (two 32-bit numbers) and the wheel data.
                    uint flags = unchecked((uint)Marshal.ReadInt32(lParam, 12));
                    if ((flags & Native.LLMHF_INJECTED) == 0) Touched();
                }
                return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
            }
        }
    }
}
