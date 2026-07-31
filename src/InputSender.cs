using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace RobloxKeeper
{
    // Keyboard nudges and window focus. Roblox reads raw input, so everything
    // here goes through SendInput rather than posted messages.
    static class InputSender
    {
        public static void FocusWindow(IntPtr hwnd)
        {
            // A quick Alt tap makes Windows allow the foreground switch.
            SendVk(Native.VK_LMENU, true);
            SendVk(Native.VK_LMENU, false);
            Native.SetForegroundWindow(hwnd);
            Thread.Sleep(120);
            if (Native.GetForegroundWindow() != hwnd)
            {
                uint pid;
                uint target = Native.GetWindowThreadProcessId(hwnd, out pid);
                uint mine = Native.GetCurrentThreadId();
                Native.AttachThreadInput(mine, target, true);
                Native.SetForegroundWindow(hwnd);
                Native.AttachThreadInput(mine, target, false);
                Thread.Sleep(120);
            }
        }

        public static void ShowClient(IntPtr hwnd)
        {
            if (Native.IsIconic(hwnd)) { Native.ShowWindow(hwnd, Native.SW_RESTORE); Thread.Sleep(150); }
            FocusWindow(hwnd);
        }

        public static void TapKey(byte vk, int holdMs)
        {
            SendScan(vk, true);
            Thread.Sleep(holdMs);
            SendScan(vk, false);
        }

        // Scan-code input: what games reading raw/hardware input actually listen for.
        // Arrow keys are extended keys and need the E0 flag, or they read as numpad.
        public static void SendScan(byte vk, bool down)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_KEYBOARD;
            inp[0].U.ki.wVk = 0;
            inp[0].U.ki.wScan = (ushort)Native.MapVirtualKey(vk, 0);
            uint flags = Native.KEYEVENTF_SCANCODE;
            if (vk >= 0x21 && vk <= 0x2E) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            if (!down) flags |= Native.KEYEVENTF_KEYUP;
            inp[0].U.ki.dwFlags = flags;
            Native.SendInput(1, inp, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public static void SendVk(byte vk, bool down)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_KEYBOARD;
            inp[0].U.ki.wVk = vk;
            inp[0].U.ki.dwFlags = down ? 0u : Native.KEYEVENTF_KEYUP;
            Native.SendInput(1, inp, Marshal.SizeOf(typeof(Native.INPUT)));
        }
    }
}
